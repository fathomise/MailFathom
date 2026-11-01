// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Receipts;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Receipts;

/// <summary>Covers sending the read receipt a received message asked for: only on request, once, and never where it is refused.</summary>
public sealed class ReadReceiptSendingTests
{
    private const string SendingAddress = "owner@example.test";

    private const string RequestedAddress = "anna@elsewhere.test";

    private static readonly DateTimeOffset Now = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> ComposedMime = Encoding.ASCII.GetBytes("From: owner@example.test\r\n\r\nReceipt");

    private readonly IReceiptReportStore store = Substitute.For<IReceiptReportStore>();

    private readonly IAuthoredEmailComposer composer = ComposerThatComposesReceipts();

    /// <summary>A message asking for a receipt is answered once the reader asks, from the account it arrived in.</summary>
    [Fact]
    public async Task SendAsync_AMessageAskingForAReceipt_ComposesAndSendsIt()
    {
        // Arrange
        var answered = Requesting();
        var sending = this.SendingOver(answered);

        // Act
        var result = await sending.SendAsync(answered.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ReadReceiptSendingResult(ReadReceiptSendingOutcome.Sent, Now), result);
        this.composer.Received(1).ComposeReadReceipt(
            answered.Account,
            Arg.Any<UserId>(),
            OutgoingEmailRequester.ReadReceipt(answered.StoredEmailId),
            Arg.Is<ReadReceiptAnswer>(answer => answer.Recipient == RequestedAddress
                && answer.Subject == "Read: Lunch"
                && answer.OriginalMessageId == "lunch@elsewhere.test"),
            Arg.Any<MailDeliveryCapabilities>());
    }

    /// <summary>Each refusal is decided before anything is composed or claimed, so a refused request leaves no trace.</summary>
    [Theory]
    [InlineData(ReadReceiptSendingOutcome.NotRequested)]
    [InlineData(ReadReceiptSendingOutcome.AlreadySent)]
    [InlineData(ReadReceiptSendingOutcome.Spam)]
    [InlineData(ReadReceiptSendingOutcome.OwnMessage)]
    public async Task SendAsync_ARequestThatCannotBeAnswered_IsRefusedBeforeAnythingIsComposed(ReadReceiptSendingOutcome refusal)
    {
        // Arrange
        var answered = refusal switch
        {
            ReadReceiptSendingOutcome.NotRequested => Requesting() with { ReadReceiptRequest = null },
            ReadReceiptSendingOutcome.AlreadySent => Requesting() with { ReadReceiptRequest = new EmailReadReceiptRequest(RequestedAddress, false, Now.AddDays(-1)) },
            ReadReceiptSendingOutcome.Spam => Requesting() with
            {
                Spam = new EmailSpamVerdict(SpamVerdict.Spam, SpamClassificationStage.Model, Subcategory: null, PhishingRequest: null, ImpersonatedParty: null),
            },
            _ => Requesting() with { SenderAddress = SendingAddress },
        };
        var sending = this.SendingOver(answered);

        // Act
        var result = await sending.SendAsync(answered.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(refusal, result.Outcome);
        this.composer.DidNotReceiveWithAnyArgs().ComposeReadReceipt(default, default, default!, default!, default!);
        await this.store.DidNotReceiveWithAnyArgs().ClaimReadReceiptAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A second request racing the first finds the claim taken and is told when the receipt went.</summary>
    [Fact]
    public async Task SendAsync_AReceiptAnotherRequestAlreadyClaimed_IsRefusedWithWhenItWent()
    {
        // Arrange
        var answered = Requesting();
        var earlier = Now.AddMinutes(-1);
        this.store.ClaimReadReceiptAsync(answered.StoredEmailId, Now, Arg.Any<CancellationToken>()).Returns(earlier);
        var sending = this.SendingOver(answered);

        // Act
        var result = await sending.SendAsync(answered.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ReadReceiptSendingResult(ReadReceiptSendingOutcome.AlreadySent, earlier), result);
    }

    /// <summary>A receipt the deployment's sending rules refuse never left, so the reader may ask again.</summary>
    [Fact]
    public async Task SendAsync_AReceiptTheSendingRulesRefuse_ReleasesTheClaim()
    {
        // Arrange
        var answered = Requesting();
        Assert.True(OutgoingRecipientRule.TryCreateForDomain("elsewhere.test", out var denied));
        var sending = this.SendingOver(
            answered,
            AuthoredSendGovernors.Governing(recipientPolicy: OutgoingRecipientPolicy.Create([], [denied]), authorization: Granted()));

        // Act
        await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => sending.SendAsync(answered.StoredEmailId, TestContext.Current.CancellationToken));

        // Assert
        await this.store.Received(1).ReleaseReadReceiptAsync(answered.StoredEmailId, Now, Arg.Any<CancellationToken>());
    }

    /// <summary>A message the reader cannot read is answered exactly as one that does not exist.</summary>
    [Fact]
    public async Task SendAsync_AMessageNotHeld_IsNotFound()
    {
        // Arrange
        var answered = Requesting();
        var sending = this.SendingOver(answered, summaryReader: Substitute.For<IStoredEmailSummaryReader>());

        // Act
        var result = await sending.SendAsync(answered.StoredEmailId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ReadReceiptSendingOutcome.NotFound, result.Outcome);
    }

    private static EmailSummary Requesting() => SyntheticEmailSummaries.Create(subject: "Lunch", senderAddress: RequestedAddress) with
    {
        InternetMessageId = "lunch@elsewhere.test",
        ReadReceiptRequest = new EmailReadReceiptRequest(RequestedAddress, NamesAnotherAddress: false, SentAt: null),
    };

    private static AccessAuthorization Granted() =>
        AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend, MailFathomPermission.MailRead);

    private static IAuthoredEmailComposer ComposerThatComposesReceipts()
    {
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer
            .ComposeReadReceipt(
                Arg.Any<MailAccountId>(),
                Arg.Any<UserId>(),
                Arg.Any<OutgoingEmailRequester>(),
                Arg.Any<ReadReceiptAnswer>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(call => AuthoredEmailComposition.Composed(new ComposedOutgoingEmail(
                OutgoingEmailRequest.Create(
                    call.ArgAt<MailAccountId>(0),
                    call.ArgAt<UserId>(1),
                    call.ArgAt<OutgoingEmailRequester>(2),
                    [OutgoingRecipient.Create(Address(call.ArgAt<ReadReceiptAnswer>(3).Recipient), OutgoingRecipientRole.To)]),
                InternetMessageId.Mint("example.test"),
                ComposedMime)));

        return composer;
    }

    private ReadReceiptSending SendingOver(
        EmailSummary answered,
        AuthoredSendGovernor? governor = null,
        IStoredEmailSummaryReader? summaryReader = null)
    {
        var granted = Granted();
        var participation = StubMailFolderParticipation.Mapping(new MailFolderIdentity(answered.Account, answered.Folder.Alias!.Value));
        var summaries = summaryReader ?? Substitute.For<IStoredEmailSummaryReader>();

        if (summaryReader is null)
        {
            summaries.FindAsync(answered.StoredEmailId, Arg.Any<CancellationToken>()).Returns(answered);
        }

        var languages = Substitute.For<IUserLanguages>();
        languages.LanguageOf(Arg.Any<UserId>()).Returns(UserLanguage.English);

        return new ReadReceiptSending(
            summaries,
            new MailboxScopeResolver(
                CatalogServing(answered.Account),
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            SenderIdentitiesFor(answered.Account),
            this.store,
            this.composer,
            governor ?? AuthoredSendGovernors.Permitting(granted),
            OutboxOver(granted),
            languages,
            granted,
            new FakeTimeProvider(Now));
    }

    private static MailOutbox OutboxOver(AccessAuthorization granted)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new MailOutbox(
            new InMemoryOutgoingEmailStore(timeProvider: new FakeTimeProvider(Now)),
            ContentStores.Substituted(),
            new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), new FakeTimeProvider()),
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            granted,
            OutgoingMailGovernors.Permitting(),
            OutgoingMailScreenings.Inactive(),
            OrganizationMailSigners.SigningNothing(),
            new FakeTimeProvider(Now),
            ClientSignalPublishers.ReachingNobody);
    }

    private static IOutgoingSenderIdentityReader SenderIdentitiesFor(MailAccountId accountId)
    {
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities.FindSenderIdentity(Arg.Any<MailAccountId>()).Returns(OutgoingSenderIdentity.Create(accountId, Address(SendingAddress)));

        return senderIdentities;
    }

    private static ICallerMailAccountCatalog CatalogServing(MailAccountId accountId)
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([SyntheticServedAccount.Of(accountId)]);

        return catalog;
    }

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var emailAddress)
            ? emailAddress
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
}
