// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>One account, its user, their automatic reply, and arrivals to answer, wired to a real outbox over in-memory stores.</summary>
internal sealed class AutomaticReplyWorld
{
    /// <summary>The account mail arrives on.</summary>
    internal static readonly MailAccountId Account = MailAccountId.Create("primary");

    /// <summary>The address that account sends from.</summary>
    internal const string OwnAddress = "jan@example.test";

    /// <summary>The instant the world starts at, inside the period <see cref="AnswerAsync" /> sets by default.</summary>
    internal static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<StoredEmailId, ArrivedMailEnvelope> envelopes = [];
    private readonly IEmailContentStore contentStore = ContentStores.Substituted();
    private readonly List<AuthoredEmail> composed = [];
    private readonly DateTimeOffset periodAround;

    internal AutomaticReplyWorld(
        FakeTimeProvider? clock = null,
        OutgoingSendRefusalReason? sendRefusal = null,
        DateTimeOffset? periodAround = null)
    {
        this.Clock = clock ?? new FakeTimeProvider(Now);
        this.periodAround = periodAround ?? Now;
        this.Outgoing = new InMemoryOutgoingEmailStore(timeProvider: this.Clock);
        this.Assignments.Assigning(SyntheticUser.Deployment, Account);

        this.contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => ContentNaming(call.ArgAt<StoredEmailId>(0)));

        var envelopeReader = Substitute.For<IArrivedMailEnvelopeReader>();
        envelopeReader
            .Read(Arg.Any<ReadOnlyMemory<byte>>())
            .Returns(call => this.envelopes.GetValueOrDefault(
                StoredEmailId.Create(Guid.Parse(Encoding.ASCII.GetString(call.ArgAt<ReadOnlyMemory<byte>>(0).Span)))));

        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities.FindSenderIdentity(Account).Returns(OutgoingSenderIdentity.Create(Account, Address(OwnAddress)));

        this.Responder = new AutomaticReplyResponder(
            this.Assignments,
            this.Store,
            this.Summaries,
            this.contentStore,
            envelopeReader,
            senderIdentities,
            this.ComposerRecording(),
            this.Outbox(sendRefusal),
            this.Clock);
    }

    internal FakeTimeProvider Clock { get; }

    internal StubMailAccountAssignments Assignments { get; } = new();

    internal InMemoryAutomaticReplyStore Store { get; } = new();

    internal InMemoryStoredEmailSummaries Summaries { get; } = new();

    internal InMemoryOutgoingEmailStore Outgoing { get; }

    internal AutomaticReplyResponder Responder { get; }

    /// <summary>Gets every reply the composer was handed, in order.</summary>
    internal IReadOnlyList<AuthoredEmail> Composed => this.composed;

    internal static EmailAddress Address(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return emailAddress;
    }

    /// <summary>Sets the user's automatic reply, answering on the account from a day before the instant the world's period is placed around to six days after it.</summary>
    internal async Task AnswerAsync(string? insideText = null, bool isOn = true, MailAccountId? account = null)
    {
        var around = this.periodAround;

        Assert.True(AutomaticReply.TryCreate(
            insideText,
            "I am away until Monday.",
            [account ?? Account],
            around.AddDays(-1),
            around.AddDays(6),
            isOn,
            out var reply,
            out _));

        await this.Store.SaveAsync(SyntheticUser.Deployment, reply!, CancellationToken.None);
    }

    /// <summary>Stores one arrival with the header block and the sender standing it is answered on.</summary>
    internal StoredEmailId Arrive(
        string from,
        EmailAutomation automation = EmailAutomation.None,
        bool hasNullReturnPath = false,
        SenderTrustSource trustedBy = SenderTrustSource.None,
        string? replyTo = null,
        DateTimeOffset? receivedAt = null)
    {
        var id = StoredEmailId.Create(Guid.CreateVersion7());

        this.Summaries.With(SyntheticEmailSummaries.Create(
            receivedAt: receivedAt ?? this.Clock.GetUtcNow(),
            storedEmailId: id.Value,
            accountId: Account.Value,
            senderVerification: SenderVerificationGrantedBy(trustedBy)));
        this.envelopes[id] = new ArrivedMailEnvelope(
            Address(replyTo ?? from),
            Address(from),
            automation,
            hasNullReturnPath,
            "Budget",
            EmailThreadReferences.Create($"<{id}@example.test>", inReplyTo: null, references: null));

        return id;
    }

    internal Task<int> RespondAsync(params StoredEmailId[] arrivals) =>
        this.Responder.AnswerAsync(Account, arrivals, TestContext.Current.CancellationToken);

    private static SenderVerification SenderVerificationGrantedBy(SenderTrustSource trustedBy) =>
        SenderVerification.NotEstablished with { DeploymentTrustGrantedBy = trustedBy };

    private static StoredEmailContent ContentNaming(StoredEmailId id)
    {
        var bytes = Encoding.ASCII.GetBytes(id.Value.ToString());

        return new StoredEmailContent(bytes, bytes.Length, SHA256.HashData(bytes));
    }

    private IAuthoredEmailComposer ComposerRecording()
    {
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer
            .Compose(
                Arg.Any<MailAccountId>(),
                Arg.Any<UserId?>(),
                Arg.Any<OutgoingEmailRequester>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(call =>
            {
                var authored = call.ArgAt<AuthoredEmail>(3);
                this.composed.Add(authored);

                return AuthoredEmailComposition.Composed(new ComposedOutgoingEmail(
                    OutgoingEmailRequest.Create(
                        call.ArgAt<MailAccountId>(0),
                        call.ArgAt<UserId?>(1),
                        call.ArgAt<OutgoingEmailRequester>(2),
                        [.. authored.Recipients.Select(static recipient => OutgoingRecipient.Create(Address(recipient.Address), recipient.Role))]),
                    InternetMessageId.Mint("example.test"),
                    Encoding.ASCII.GetBytes("Auto-Submitted: auto-replied\r\n\r\nAway.")));
            });

        return composer;
    }

    private MailOutbox Outbox(OutgoingSendRefusalReason? sendRefusal)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new MailOutbox(
            this.Outgoing,
            this.contentStore,
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), TimeProvider.System),
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
            OutgoingMailGovernors.Governing(sendRefusal),
            OutgoingMailScreenings.Inactive(),
            OrganizationMailSigners.SigningNothing(),
            this.Clock,
            ClientSignalPublishers.ReachingNobody);
    }
}
