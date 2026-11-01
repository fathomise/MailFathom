// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.AppHost;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Delivery;

/// <summary>Proves that editing a draft leaves one message in the folder, that sending it leaves none, and that the user's own draft survives both.</summary>
/// <remarks>
/// <para>
/// Three claims, and no substitute settles any of them. Whether replacing a stored message leaves one draft or two is
/// the server's answer to an <c>APPEND</c> followed by a <c>UID EXPUNGE</c> rather than the adapter's; whether the copy
/// a promotion gives up is the one that append reported runs through a real <c>APPENDUID</c>; and whether a message
/// nobody here wrote is still in the folder afterwards is a read over a connection nothing under test owns.
/// </para>
/// <para>
/// They are one test because they are one draft going round one loop: the append, the replacement, the promotion, and
/// the delivery are consecutive states of the same message, and a second test would have to reproduce the first one's
/// folder to reach the state it asserts on. What each state adds is asserted where it happens, so a failure names the
/// step.
/// </para>
/// <para>
/// The folder holds the control every absence assertion needs. A draft appended beside MailFathom's own — same folder,
/// same <c>\Draft</c> flag, reachable by the same commands — is what makes "one draft left" an observation rather than
/// a removal that emptied the folder.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailDraftTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The one mailbox the orchestrated server has, which is both the sender and the addressee here.</summary>
    private const string Mailbox = OrchestrationContract.MailServerAccountEmailAddress;

    /// <summary>The domain every identity this test mints belongs to, which is reserved and reaches nothing.</summary>
    private const string MessageIdDomain = "mailfathom.test";

    /// <summary>The whole loop a draft travels, from the first append to the folder a delivered send leaves behind.</summary>
    [Fact]
    public async Task SaveAsync_ADraftEditedAndThenPromoted_LeavesOneDraftThenNoneAndSparesTheUsersOwn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        // Recreated rather than reused, so the messages this test counts are the only ones the folders have ever held.
        await mailbox.RecreateFolderAsync(SyntheticMailAccount.DraftCopyFolderPath, cancellationToken);
        await mailbox.RecreateFolderAsync(SyntheticMailAccount.OutgoingCopyFolderPath, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            filesSentCopies: true,
            keepsDrafts: true);

        // The bindings a synchronization run over the two role folders would have recorded. Both the draft filer and
        // the sent-copy filer resolve their destination by role and then read that binding, and nothing here
        // synchronizes either folder — so without these the appends report their destinations as unavailable and the
        // draft never leaves the composed stage.
        await OrchestratedFolderBinding.CommitAsync(
            services,
            SyntheticMailAccount.DraftCopyFolderAlias,
            SyntheticMailAccount.DraftCopyFolderPath,
            cancellationToken);
        await OrchestratedFolderBinding.CommitAsync(
            services,
            SyntheticMailAccount.OutgoingCopyFolderAlias,
            SyntheticMailAccount.OutgoingCopyFolderPath,
            cancellationToken);

        var written = $"draft-{Guid.NewGuid():N}";
        var revised = $"draft-revised-{Guid.NewGuid():N}";
        var foreign = $"draft-foreign-{Guid.NewGuid():N}";

        // The draft MailFathom did not write, appended before anything else reaches the folder so that every command
        // the run afterwards issues has had the chance to take it.
        await mailbox.AppendDraftAsync(SyntheticMailAccount.DraftCopyFolderPath, foreign, cancellationToken);
        var users = Assert.Single(await ReadDraftsAsync(mailbox, cancellationToken), Named(foreign));

        // Act
        var draft = await SaveAsync(services, written, OutgoingReceiptRequests.None, revises: null, cancellationToken);

        // Assert
        Assert.Equal(MailDraftStage.Filed, draft.Stage);

        var appended = Assert.Single(await ReadDraftsAsync(mailbox, cancellationToken), Named(written));
        Assert.True(appended.IsDraft);
        Assert.Equal(appended.Uid, draft.CurrentCopy?.Placement.Uid);

        // The edit: one append and one removal, and the folder is what says whether both of them happened.
        var edited = await SaveAsync(services, revised, OutgoingReceiptRequests.None, draft.Id, cancellationToken);

        Assert.Equal(MailDraftStage.Filed, edited.Stage);
        Assert.Equal(2, edited.Revision);
        Assert.Null(edited.Divergence);

        var afterEdit = await ReadDraftsAsync(mailbox, cancellationToken);
        Assert.True(Assert.Single(afterEdit, Named(revised)).IsDraft);
        Assert.DoesNotContain(afterEdit, Named(written));
        Assert.Contains(afterEdit, Named(foreign));

        // The promotion, which writes an ordinary outgoing record and leaves the draft where it is until it is sent.
        var promoted = await PromoteAsync(services, edited.Id, cancellationToken);

        Assert.Equal(promoted.Id, (await FindAsync(services, edited.Id, cancellationToken))?.PromotedTo);
        Assert.Contains(await ReadDraftsAsync(mailbox, cancellationToken), Named(revised));

        var report = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutboxPass>().RunAsync(
                SyntheticMailAccount.Account,
                token),
            cancellationToken);

        Assert.Equal(
            MailOutboxDeliveryOutcome.Sent,
            Assert.Single(report.Results, entry => entry.OutgoingEmailId == promoted.Id).Outcome);
        Assert.Equal(
            MailDraftFilingOutcome.Discarded,
            Assert.Single(report.DraftResults, entry => entry.DraftId == edited.Id).Outcome);
        Assert.Equal(
            OutgoingMailFilingOutcome.Filed,
            Assert.Single(report.FilingResults, entry => entry.OutgoingEmailId == promoted.Id).Outcome);

        // The independent witness on both folders: the drafts folder holds the user's own message and nothing else,
        // still under the UID it was appended with, and what was drafted is in the sent folder exactly once.
        var remaining = Assert.Single(await ReadDraftsAsync(mailbox, cancellationToken));
        Assert.Equal(foreign, remaining.Subject);
        Assert.Equal(users.Uid, remaining.Uid);
        Assert.True(remaining.IsDraft);

        Assert.Single(
            await mailbox.ReadAsync(SyntheticMailAccount.OutgoingCopyFolderPath, cancellationToken),
            Named(revised));

        // The record goes with the copy: a draft that has been sent is no longer a draft this deployment holds.
        Assert.Null(await FindAsync(services, edited.Id, cancellationToken));
    }

    /// <summary>
    /// A refused send written from a draft goes back to that draft and out of the outbox in one statement, and only once:
    /// the draft is listed for its writer again and the send is dismissed by them.
    /// </summary>
    /// <remarks>
    /// It is here because the reopening is one SQL statement whose three parts — the lock on the send, the draft let go
    /// of its promotion, and the dismissal conditioned on that — only PostgreSQL can be wrong about together.
    /// </remarks>
    [Fact]
    public async Task ReopenAsDraftAsync_ARefusedSendWrittenFromADraft_ListsTheDraftAgainAndDismissesTheSendOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(SyntheticMailAccount.DraftCopyFolderPath, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            keepsDrafts: true);

        await OrchestratedFolderBinding.CommitAsync(
            services,
            SyntheticMailAccount.DraftCopyFolderAlias,
            SyntheticMailAccount.DraftCopyFolderPath,
            cancellationToken);

        var draft = await SaveAsync(
            services,
            $"draft-reopened-{Guid.NewGuid():N}",
            OutgoingReceiptRequests.None,
            revises: null,
            cancellationToken);
        var promoted = await PromoteAsync(services, draft.Id, cancellationToken);
        await RefuseAsync(services, promoted.Id, cancellationToken);

        // Act
        var reopened = await ReopenAsync(services, promoted.Id, cancellationToken);
        var again = await ReopenAsync(services, promoted.Id, cancellationToken);

        // Assert
        Assert.Equal(new OutboxReopening(OutboxDecisionOutcome.Accepted, draft.Id), reopened);
        Assert.NotEqual(OutboxDecisionOutcome.Accepted, again.Outcome);
        Assert.Null(again.Draft);
        Assert.Null((await FindAsync(services, draft.Id, cancellationToken))?.PromotedTo);
        Assert.Contains(draft.Id, await HeldForUserAsync(services, cancellationToken));

        var send = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(promoted.Id, token),
            cancellationToken);
        Assert.Equal(SyntheticMailAccount.User, send?.Dismissal?.DismissedBy);
    }

    /// <summary>
    /// What a draft asks to be told about is kept by each revision in turn and is what its send asks for, read back
    /// through PostgreSQL at every step.
    /// </summary>
    /// <remarks>
    /// The two requests are two columns on each of two tables, written by an insert, overwritten by a revision, and
    /// copied by the promotion; every application-level test reaches them through an in-memory store, so a swapped
    /// column or a revision that kept the old value would leave both suites green. The revision asks for the opposite
    /// of what the first write asked for, which is what makes a value that was never overwritten fail rather than pass.
    /// </remarks>
    [Fact]
    public async Task SaveAsync_ARevisionAskingForDifferentReceipts_KeepsTheLatestAndPromotesThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(SyntheticMailAccount.DraftCopyFolderPath, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            keepsDrafts: true);

        await OrchestratedFolderBinding.CommitAsync(
            services,
            SyntheticMailAccount.DraftCopyFolderAlias,
            SyntheticMailAccount.DraftCopyFolderPath,
            cancellationToken);

        var subject = $"draft-receipts-{Guid.NewGuid():N}";
        var first = new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: false);
        var revised = new OutgoingReceiptRequests(DeliveryNotification: false, ReadReceipt: true);

        // Act
        var draft = await SaveAsync(services, subject, first, revises: null, cancellationToken);
        var afterOpening = await FindAsync(services, draft.Id, cancellationToken);

        await SaveAsync(services, subject, revised, draft.Id, cancellationToken);
        var afterRevision = await FindAsync(services, draft.Id, cancellationToken);

        var promoted = await PromoteAsync(services, draft.Id, cancellationToken);
        var send = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(promoted.Id, token),
            cancellationToken);

        await RefuseAsync(services, promoted.Id, cancellationToken);

        // Assert
        Assert.Equal(first, afterOpening?.ReceiptRequests);
        Assert.Equal(revised, afterRevision?.ReceiptRequests);
        Assert.Equal(revised, send?.ReceiptRequests);
        Assert.Null(send?.DeliveryNotificationCarried);
    }

    /// <summary>
    /// Where each of a draft's addresses came from survives the round trip through PostgreSQL, which is what the
    /// promotion's answer is built from months later.
    /// </summary>
    /// <remarks>
    /// It is here rather than in the unit suite because only the real mapping can be wrong about it: every
    /// Application-level test writes through an in-memory store, so a column the insert never sets or a read that
    /// answers with the CLR default would leave both suites green. What that costs is the whole point of persisting
    /// the value — on a deployment refusing unvouched recipients, a promoted draft of a reply would be refused for
    /// naming somebody the answered message itself named.
    /// </remarks>
    [Fact]
    public async Task OpenAsync_RecipientsFromDifferentSources_ReadsEachProvenanceBack()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var recipients = new[]
        {
            RecipientNamed("caller@mailfathom.test", AuthoredRecipientProvenance.NamedByCaller),
            RecipientNamed("answered@mailfathom.test", AuthoredRecipientProvenance.DerivedFromAnsweredEmail),
            RecipientNamed("book@mailfathom.test", AuthoredRecipientProvenance.ResolvedFromContactBook),
        };

        MailDraftRecord? opened = null;

        // Act
        var commit = await services.CommitAsync(
            async (scope, session, token) => opened = await scope.GetRequiredService<IMailDraftStore>().OpenAsync(
                session,
                SyntheticMailAccount.Account,
                SyntheticMailAccount.User,
                OutgoingEmailRequester.Command($"provenance-{Guid.NewGuid():N}"),
                recipients,
                "Provenance survives a revision",
                OutgoingReceiptRequests.None,
                mimeByteLength: 128,
                DateTimeOffset.UnixEpoch,
                token),
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commit);
        Assert.NotNull(opened);

        var stored = await FindAsync(services, opened.Id, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(
            recipients.Select(recipient => (recipient.Recipient.Address.Address, recipient.Provenance)),
            stored.Recipients.Select(recipient => (recipient.Recipient.Address.Address, recipient.Provenance)));
    }

    /// <summary>A draft with a file staged against it: what the row keeps, what the octets come back as, and what reads it.</summary>
    /// <remarks>
    /// Every claim here is the database's rather than a store double's. A staged file is a second table, a cascade, and
    /// a projection that no application-level test composes: the description a listing draws is read without the
    /// octets, and the octets a composition needs are read separately and have to survive the round trip through
    /// <c>bytea</c> byte for byte. The two reads that reach the draft are asserted beside it, because which drafts a
    /// pass settles and which a user is shown are predicates PostgreSQL evaluates over the row and its copies rather
    /// than domain properties a double could answer differently.
    /// </remarks>
    [Fact]
    public async Task StageAttachmentAsync_ADraftCarryingAStagedFile_KeepsItsDescriptionAndItsOctetsAcrossEveryRead()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var subject = $"Local draft {Guid.NewGuid():N}";

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        MailDraftRecord? opened = null;

        // Act
        var commit = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var drafts = scope.GetRequiredService<IMailDraftStore>();

                opened = await drafts.OpenAsync(
                    session,
                    SyntheticMailAccount.Account,
                    SyntheticMailAccount.User,
                    OutgoingEmailRequester.Command(subject),
                    [RecipientAtTheMailbox()],
                    subject,
                    OutgoingReceiptRequests.None,
                    mimeByteLength: 128,
                    DateTimeOffset.UnixEpoch,
                    token);

                await drafts.StageAttachmentAsync(
                    session,
                    opened.Id,
                    new AuthoredEmailAttachment("note.txt", "text/plain", "Remember the milk."u8.ToArray()),
                    DateTimeOffset.UnixEpoch,
                    token);
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commit);
        Assert.NotNull(opened);

        var stored = await FindAsync(services, opened.Id, cancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(subject, stored.Subject);
        Assert.Equal(
            [("note.txt", "text/plain", 18L)],
            stored.Attachments.Select(file => (file.FileName, file.MediaType, file.ByteLength)));

        var content = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailDraftStore>()
                .ReadAttachmentContentAsync(opened.Id, token),
            cancellationToken);

        Assert.Equal(
            ["Remember the milk."],
            content.Select(file => Encoding.UTF8.GetString(file.Content.Span)));

        Assert.Contains(opened.Id, await OutstandingAsync(services, cancellationToken));
        Assert.Contains(opened.Id, await HeldForUserAsync(services, cancellationToken));
    }

    /// <summary>Reads which of this account's drafts owe the mail server something, as the pass reads it.</summary>
    private static async Task<IReadOnlyList<MailDraftId>> OutstandingAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) =>
    [
        .. (await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailDraftStore>().ReadOutstandingAsync(
                SyntheticMailAccount.Account,
                maxCount: 50,
                token),
            cancellationToken)).Select(draft => draft.Id),
    ];

    /// <summary>Reads which drafts this user is still writing, as the client's own listing reads them.</summary>
    private static async Task<IReadOnlyList<MailDraftId>> HeldForUserAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) =>
    [
        .. (await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailDraftStore>().ReadForUserAsync(
                SyntheticMailAccount.User,
                SyntheticMailAccount.Account,
                maxCount: 50,
                token),
            cancellationToken)).Select(draft => draft.Id),
    ];

    /// <summary>Recognizes one of this test's own messages among whatever the folder holds.</summary>
    private static Predicate<ObservedEmail> Named(string subject) => message => message.Subject == subject;

    private static Task<IReadOnlyList<ObservedEmail>> ReadDraftsAsync(
        OrchestratedMailbox mailbox,
        CancellationToken cancellationToken) =>
        mailbox.ReadAsync(SyntheticMailAccount.DraftCopyFolderPath, cancellationToken);

    /// <summary>Writes a draft down as a caller holding the grant drafting is admitted under, which is what a command is.</summary>
    private static Task<MailDraftRecord> SaveAsync(
        OrchestratedMailFathomServices services,
        string subject,
        OutgoingReceiptRequests receiptRequests,
        MailDraftId? revises,
        CancellationToken cancellationToken)
    {
        var messageId = InternetMessageId.Mint(MessageIdDomain);

        return services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailDraftBook>().SaveAsync(
                SyntheticMailAccount.Account,
                SyntheticMailAccount.User,
                OutgoingEmailRequester.Command(subject),
                new ComposedMailDraft([RecipientAtTheMailbox()], subject, messageId, MimeOf(subject, messageId)),
                receiptRequests,
                revises,
                token),
            [MailFathomPermission.MailDraftsWrite],
            cancellationToken);
    }

    private static Task<OutgoingEmailRecord> PromoteAsync(
        OrchestratedMailFathomServices services,
        MailDraftId draftId,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailDraftPromotion>().PromoteAsync(draftId, SyntheticMailAccount.User, token),
            [MailFathomPermission.MailSend],
            cancellationToken);

    /// <summary>Ends one send refused, under a lease that lapses at once so no other send of the account stays held.</summary>
    private static async Task RefuseAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var claimed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(SyntheticMailAccount.Account, batchSize: 100, TimeSpan.FromMilliseconds(1)),
                token),
            cancellationToken);
        var lease = Assert.Single(claimed, entry => entry.Record.Id == outgoingEmailId).Lease;

        var committed = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .AdvanceAsync(session, lease, outgoingEmailId, OutgoingEmailStage.Refused, 550, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);
    }

    private static Task<OutboxReopening> ReopenAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .ReopenAsDraftAsync(outgoingEmailId, SyntheticMailAccount.User, token),
            cancellationToken);

    private static Task<MailDraftRecord?> FindAsync(
        OrchestratedMailFathomServices services,
        MailDraftId draftId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailDraftStore>().FindAsync(draftId, token),
            cancellationToken);

    /// <summary>Addresses the draft to the one mailbox the orchestrated server has, so a promotion is deliverable.</summary>
    /// <remarks>
    /// Named as the caller's own word, which is the strict reading the sending governance judges at the promotion. It
    /// is admitted because the address is the deployment's own mailbox, which this installation vouches for whatever
    /// its contact book holds.
    /// </remarks>
    private static MailDraftRecipient RecipientAtTheMailbox()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, Mailbox, out var recipient));

        return new MailDraftRecipient(
            OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To),
            AuthoredRecipientProvenance.NamedByCaller);
    }

    /// <summary>Addresses a draft to one mailbox, saying where the address came from.</summary>
    private static MailDraftRecipient RecipientNamed(string address, AuthoredRecipientProvenance provenance)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var parsed));

        return new MailDraftRecipient(
            OutgoingRecipient.Create(parsed, OutgoingRecipientRole.To),
            provenance);
    }

    /// <summary>Builds a synthetic message whose subject is how a test recognizes its own draft.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string subject, InternetMessageId messageId) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{messageId.Value}>\r\n"
        + $"From: {Mailbox}\r\n"
        + $"To: {Mailbox}\r\n"
        + $"Subject: {subject}\r\n"
        + "MIME-Version: 1.0\r\n"
        + "Content-Type: text/plain; charset=us-ascii\r\n\r\n"
        + "Synthetic body.\r\n")
        .AsMemory();
}
