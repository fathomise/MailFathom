// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what the three decisions about one send actually do to the row, what refuses them, and what each outbox reading serves.</summary>
/// <remarks>
/// <para>
/// Every decision — a withdrawal, a requeue, and a dismissal — is a single conditional <c>UPDATE</c> whose condition is
/// the whole of the exclusion, so what it refuses is decided by PostgreSQL evaluating a stage, a lease, and a dismissal
/// rather than by anything a substitute could stand in for. That is why every claim here is against the real database:
/// a statement that matched no row and a statement that matched the wrong one are indistinguishable from a fake, and the
/// difference between them is a message withdrawn and a message sent twice.
/// </para>
/// <para>
/// The reading side is here for a second reason. A cursor walk is a keyset comparison over two columns of which one is
/// a <c>uuid</c>, and how PostgreSQL orders those is exactly what a unit test cannot ask; an account's outbox adds a
/// filter on the stage and the dismissal, and a count split by stage, which a substitute answers however it is told.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOutboxOperationStoreTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>A lease long enough that no decision in a test outlives it.</summary>
    private static readonly TimeSpan LiveLease = TimeSpan.FromMinutes(10);

    /// <summary>A lease already over by the time the next statement runs, which is a send no attempt holds.</summary>
    private static readonly TimeSpan ExpiredLease = TimeSpan.FromMilliseconds(1);

    /// <summary>Gets the account this suite writes under, whose user the orchestrated database provisioned.</summary>
    /// <remarks>Read on each use rather than captured in a field, because the user is resolved when the harness starts.</remarks>
    private static MailAccountId Account => SyntheticMailAccount.Account;

    /// <summary>
    /// The ordinary withdrawal, and the two refusals that follow it. A send already withdrawn cannot be withdrawn
    /// again, and a decision naming a record this deployment never held says so rather than reporting a stage.
    /// </summary>
    [Fact]
    public async Task CancelAsync_ASendNothingHasClaimed_WithdrawsItAndRefusesEveryDecisionAfterwards()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var enqueued = await EnqueueAsync(services, "outbox-operations-cancel", cancellationToken);

        // Act
        var withdrawn = await CancelAsync(services, enqueued.Id, cancellationToken);
        var again = await CancelAsync(services, enqueued.Id, cancellationToken);
        var unheld = await CancelAsync(services, OutgoingEmailId.Create(Guid.CreateVersion7()), cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.Accepted, withdrawn);
        Assert.Equal(OutboxDecisionOutcome.StageDoesNotAllowIt, again);
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, unheld);

        var record = await FindAsync(services, enqueued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Cancelled, record.Stage);
    }

    /// <summary>
    /// The race the condition exists for. A delivery pass holds the record, the operator's withdrawal reaches the same
    /// row, and the statement matches nothing — so the message keeps going rather than being withdrawn underneath the
    /// attempt that is transmitting it.
    /// </summary>
    [Fact]
    public async Task CancelAsync_ASendADeliveryAttemptHolds_LeavesItAloneAndReportsTheAttempt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var enqueued = await EnqueueAsync(services, "outbox-operations-claimed", cancellationToken);

        var claimed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                token),
            cancellationToken);
        Assert.Contains(claimed, entry => entry.Record.Id == enqueued.Id);

        // Act
        var refused = await CancelAsync(services, enqueued.Id, cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.AttemptUnderWay, refused);

        var record = await FindAsync(services, enqueued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
    }

    /// <summary>
    /// A send nothing will attempt again, offered another chance. The refusal has to be restated before the statement
    /// matches it, and once it does the record is back where a pass finds it, with the attempts it already spent
    /// cleared rather than counted against the chance it was just given.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_APermanentlyRefusedSend_IsOfferedAgainOnlyOnceTheRefusalIsRestated()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var enqueued = await EnqueueAsync(services, "outbox-operations-requeue", cancellationToken);

        var claimed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                token),
            cancellationToken);
        var lease = Assert.Single(claimed, entry => entry.Record.Id == enqueued.Id).Lease;

        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().AdvanceAsync(
                session,
                lease,
                enqueued.Id,
                OutgoingEmailStage.Refused,
                replyCode: 550,
                token),
            cancellationToken);

        // Act
        var withoutRestatement = await RequeueAsync(
            services,
            enqueued.Id,
            refusalRestated: false,
            reachesDismissedSend: true,
            cancellationToken);
        var restated = await RequeueAsync(
            services,
            enqueued.Id,
            refusalRestated: true,
            reachesDismissedSend: true,
            cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RefusalNotRestated, withoutRestatement);
        Assert.Equal(OutboxDecisionOutcome.Accepted, restated);

        var record = await FindAsync(services, enqueued.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(0, record.AttemptCount);

        // Left where the rest of the collection expects an account's outbox to be, rather than queued for a delivery
        // pass another class in this collection would then have to account for.
        Assert.Equal(OutboxDecisionOutcome.Accepted, await CancelAsync(services, enqueued.Id, cancellationToken));
    }

    /// <summary>
    /// A dismissed send is offered again only where the caller may reach a dismissal, and the dismissal is a condition
    /// of the statement rather than of a read before it — so a user's requeue leaves it in place, and the operator's
    /// clears it.
    /// </summary>
    [Fact]
    public async Task RequeueAsync_ADismissedSend_IsOfferedAgainOnlyByACallerThatMayReachTheDismissal()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var enqueued = await EnqueueAsync(services, "outbox-operations-requeue-dismissed", cancellationToken);

        var claimed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 100, TimeSpan.FromMinutes(10)),
                token),
            cancellationToken);
        var lease = Assert.Single(claimed, entry => entry.Record.Id == enqueued.Id).Lease;

        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>().AdvanceAsync(
                session,
                lease,
                enqueued.Id,
                OutgoingEmailStage.Refused,
                replyCode: 550,
                token),
            cancellationToken);

        var dismissed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .DismissAsync(enqueued.Id, UserId.Create(Guid.CreateVersion7()), token),
            cancellationToken);

        // Act
        var byTheUser = await RequeueAsync(
            services,
            enqueued.Id,
            refusalRestated: true,
            reachesDismissedSend: false,
            cancellationToken);
        var stillDismissed = await FindAsync(services, enqueued.Id, cancellationToken);
        var byTheOperator = await RequeueAsync(
            services,
            enqueued.Id,
            refusalRestated: true,
            reachesDismissedSend: true,
            cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.Accepted, dismissed);
        Assert.Equal(OutboxDecisionOutcome.StageDoesNotAllowIt, byTheUser);
        Assert.NotNull(stillDismissed.Dismissal);
        Assert.Equal(OutgoingEmailStage.Refused, stillDismissed.Stage);
        Assert.Equal(OutboxDecisionOutcome.Accepted, byTheOperator);

        var requeued = await FindAsync(services, enqueued.Id, cancellationToken);
        Assert.Null(requeued.Dismissal);
        Assert.Equal(OutgoingEmailStage.Recorded, requeued.Stage);

        // Left withdrawn for the reason the test above leaves its send withdrawn.
        Assert.Equal(OutboxDecisionOutcome.Accepted, await CancelAsync(services, enqueued.Id, cancellationToken));
    }

    /// <summary>
    /// A dismissal is written at <see cref="OutgoingEmailStage.Refused" />, and at
    /// <see cref="OutgoingEmailStage.TransmissionBegun" /> once no attempt holds the send, and nowhere else — not at
    /// <see cref="OutgoingEmailStage.Recorded" />, not under a live attempt, and not a second time.
    /// </summary>
    [Fact]
    public async Task DismissAsync_EachStageASendStandsAt_IsWrittenOnlyWhereTheDismissalApplies()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var dismissedBy = UserId.Create(Guid.CreateVersion7());

        var held = await EnqueueAsync(services, "outbox-operations-dismiss-held", cancellationToken);
        var heldLease = (await ClaimLeasesAsync(services, LiveLease, cancellationToken))[held.Id];
        await RecordTransmissionBegunAsync(services, heldLease, held.Id, cancellationToken);

        var unanswered = await EnqueueAsync(services, "outbox-operations-dismiss-unanswered", cancellationToken);
        var refused = await EnqueueAsync(services, "outbox-operations-dismiss-refused", cancellationToken);
        var expired = await ClaimLeasesAsync(services, ExpiredLease, cancellationToken);
        await RecordTransmissionBegunAsync(services, expired[unanswered.Id], unanswered.Id, cancellationToken);
        await AdvanceAsync(services, expired[refused.Id], refused.Id, OutgoingEmailStage.Refused, 550, cancellationToken);

        var recorded = await EnqueueAsync(services, "outbox-operations-dismiss-recorded", cancellationToken);

        // Act
        var atRecorded = await DismissAsync(services, recorded.Id, dismissedBy, cancellationToken);
        var underAnAttempt = await DismissAsync(services, held.Id, dismissedBy, cancellationToken);
        var withNobodyHoldingIt = await DismissAsync(services, unanswered.Id, dismissedBy, cancellationToken);
        var atRefused = await DismissAsync(services, refused.Id, dismissedBy, cancellationToken);
        var again = await DismissAsync(services, refused.Id, dismissedBy, cancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.StageDoesNotAllowIt, atRecorded);
        Assert.Equal(OutboxDecisionOutcome.AttemptUnderWay, underAnAttempt);
        Assert.Equal(OutboxDecisionOutcome.Accepted, withNobodyHoldingIt);
        Assert.Equal(OutboxDecisionOutcome.Accepted, atRefused);
        Assert.Equal(OutboxDecisionOutcome.StageDoesNotAllowIt, again);

        Assert.Null((await FindAsync(services, recorded.Id, cancellationToken)).Dismissal);
        Assert.Null((await FindAsync(services, held.Id, cancellationToken)).Dismissal);

        var dismissedUnanswered = await FindAsync(services, unanswered.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, dismissedUnanswered.Stage);
        Assert.Equal(dismissedBy, dismissedUnanswered.Dismissal?.DismissedBy);

        var dismissedRefused = await FindAsync(services, refused.Id, cancellationToken);
        Assert.Equal(OutgoingEmailStage.Refused, dismissedRefused.Stage);
        Assert.Equal(dismissedBy, dismissedRefused.Dismissal?.DismissedBy);

        // Left terminal or withdrawn, so no other class in this collection finds a send of this one waiting.
        await AdvanceAsync(services, heldLease, held.Id, OutgoingEmailStage.Sent, 250, cancellationToken);
        Assert.Equal(OutboxDecisionOutcome.Accepted, await CancelAsync(services, recorded.Id, cancellationToken));
    }

    /// <summary>
    /// An account's outbox, walked one send a page: it serves every send at an outbox stage exactly once and none that
    /// was dismissed, names each send's recipient, and counts what it serves — refused apart from the rest.
    /// </summary>
    [Fact]
    public async Task ReadAccountOutboxAsync_WalkedOneSendAPage_ServesWhatTheOutboxHoldsOnceAndCountsIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        var first = await EnqueueAsync(services, "outbox-operations-account-first", cancellationToken);
        var second = await EnqueueAsync(services, "outbox-operations-account-second", cancellationToken);
        var refusedKept = await EnqueueAsync(services, "outbox-operations-account-refused", cancellationToken);
        var refusedDismissed = await EnqueueAsync(services, "outbox-operations-account-dismissed", cancellationToken);

        var leases = await ClaimLeasesAsync(services, ExpiredLease, cancellationToken);
        await AdvanceAsync(services, leases[refusedKept.Id], refusedKept.Id, OutgoingEmailStage.Refused, 550, cancellationToken);
        await AdvanceAsync(
            services,
            leases[refusedDismissed.Id],
            refusedDismissed.Id,
            OutgoingEmailStage.Refused,
            550,
            cancellationToken);
        Assert.Equal(
            OutboxDecisionOutcome.Accepted,
            await DismissAsync(services, refusedDismissed.Id, UserId.Create(Guid.CreateVersion7()), cancellationToken));

        // Act
        var walked = new List<AccountOutboxEntry>();
        OutboxCursor? cursor = null;

        do
        {
            var page = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOutboxOperationStore>().ReadAccountOutboxAsync(
                    OutboxQuery.Create(Account, stage: null, pageSize: 1, cursor).Query!,
                    token),
                cancellationToken);

            walked.AddRange(page.Sends);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        var counted = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .CountAccountOutboxesAsync([Account], token),
            cancellationToken);

        // Assert
        var walkedIds = walked.Select(entry => entry.OutgoingEmailId).ToArray();
        Assert.Equal(walkedIds.Length, walkedIds.Distinct().Count());
        Assert.Contains(first.Id, walkedIds);
        Assert.Contains(second.Id, walkedIds);
        Assert.Contains(refusedKept.Id, walkedIds);
        Assert.DoesNotContain(refusedDismissed.Id, walkedIds);
        Assert.All(walked, entry => Assert.Contains(entry.Stage, OutgoingEmailRecord.OutboxStages));

        var firstEntry = Assert.Single(walked, entry => entry.OutgoingEmailId == first.Id);
        Assert.Equal("anna@example.test", Assert.Single(firstEntry.NamedRecipients).Address.Address);
        Assert.Equal(0, firstEntry.OtherRecipientCount);

        // Every class in this collection writes under one account, so the count is held against the walk rather than
        // against a total of its own: another class's waiting send is a row both legitimately report.
        var count = Assert.Single(counted);
        Assert.Equal(walked.Count(entry => entry.Stage == OutgoingEmailStage.Refused), count.Refused);
        Assert.Equal(walked.Count(entry => entry.Stage != OutgoingEmailStage.Refused), count.Waiting);

        Assert.Equal(OutboxDecisionOutcome.Accepted, await CancelAsync(services, first.Id, cancellationToken));
        Assert.Equal(OutboxDecisionOutcome.Accepted, await CancelAsync(services, second.Id, cancellationToken));
    }

    /// <summary>
    /// A walk of the whole account, one short page at a time. What it establishes is that the keyset boundary neither
    /// repeats a send nor steps over one — including where two were written down in the same instant, which is decided
    /// by how PostgreSQL compares two <c>uuid</c> values rather than by how the CLR would.
    /// </summary>
    [Fact]
    public async Task ReadPageAsync_WalkedWithTheCursorItIssues_ServesEverySendOfTheAccountExactlyOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var first = await EnqueueAsync(services, "outbox-operations-page-first", cancellationToken);
        var second = await EnqueueAsync(services, "outbox-operations-page-second", cancellationToken);

        // Act
        var walked = new List<OutgoingEmailId>();
        OutboxCursor? cursor = null;

        do
        {
            var page = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOutboxOperationStore>().ReadPageAsync(
                    OutboxQuery.Create(Account, stage: null, pageSize: 2, cursor).Query!,
                    token),
                cancellationToken);

            walked.AddRange(page.Sends.Select(send => send.OutgoingEmailId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        // Assert
        Assert.Equal(walked.Count, walked.Distinct().Count());
        Assert.Contains(first.Id, walked);
        Assert.Contains(second.Id, walked);

        // Every account in this collection is one account, so the counts are asserted as covering the walk rather than
        // as a total of their own: another class's queued send is a row this reading legitimately reports.
        var counted = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .CountByStageAsync(Account, token),
            cancellationToken);

        Assert.Equal(walked.Count, counted.Sum(stage => stage.Count));
    }

    /// <summary>Takes one decision against the outbox, in a scope of its own, as an administrative request does.</summary>
    private static Task<OutboxDecisionOutcome> CancelAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .CancelAsync(outgoingEmailId, token),
            cancellationToken);

    /// <summary>Offers one send again, in a scope of its own, as an administrative or a user's request does.</summary>
    private static Task<OutboxDecisionOutcome> RequeueAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        bool reachesDismissedSend,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .RequeueAsync(outgoingEmailId, refusalRestated, reachesDismissedSend, token),
            cancellationToken);

    /// <summary>Dismisses one send, in a scope of its own, as a user's request does.</summary>
    private static Task<OutboxDecisionOutcome> DismissAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        UserId dismissedBy,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutboxOperationStore>()
                .DismissAsync(outgoingEmailId, dismissedBy, token),
            cancellationToken);

    /// <summary>Claims every claimable send of the account under one lease, as a delivery pass does, and names each one's lease.</summary>
    private static async Task<IReadOnlyDictionary<OutgoingEmailId, OutgoingEmailLease>> ClaimLeasesAsync(
        OrchestratedMailFathomServices services,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var claimed = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 100, leaseDuration),
                token),
            cancellationToken);

        return claimed.ToDictionary(entry => entry.Record.Id, entry => entry.Lease);
    }

    /// <summary>Announces a transmission under the lease an attempt holds, as a delivery attempt does before the envelope.</summary>
    /// <remarks>The write is asserted to have committed, so an arrangement the lease did not admit fails here rather than as a stage the decision under test never saw.</remarks>
    private static async Task RecordTransmissionBegunAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var committed = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .RecordTransmissionBegunAsync(session, lease, outgoingEmailId, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);
    }

    /// <summary>Moves one send to the stage an attempt ended it at, under the lease that attempt holds.</summary>
    /// <remarks>The write is asserted to have committed, for the reason <see cref="RecordTransmissionBegunAsync" /> gives.</remarks>
    private static async Task AdvanceAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        OutgoingEmailStage stage,
        int replyCode,
        CancellationToken cancellationToken)
    {
        var committed = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IOutgoingEmailStore>()
                .AdvanceAsync(session, lease, outgoingEmailId, stage, replyCode, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);
    }

    /// <summary>Reads one record back through a scope of its own, failing the test where nothing holds it.</summary>
    private static async Task<OutgoingEmailRecord> FindAsync(
        OrchestratedMailFathomServices services,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var record = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOutgoingEmailStore>().FindAsync(outgoingEmailId, token),
            cancellationToken);

        Assert.NotNull(record);

        return record;
    }

    /// <summary>Writes one send down as the agent that asked for it, which is the principal the outbox admits a command from.</summary>
    private static async Task<OutgoingEmailRecord> EnqueueAsync(
        OrchestratedMailFathomServices services,
        string invocationIdentity,
        CancellationToken cancellationToken)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

        var request = OutgoingEmailRequest.Create(
            Account,
            SyntheticMailAccount.User,
            OutgoingEmailRequester.Command(invocationIdentity),
            [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To, contact: null)]);

        var opened = await services.AsCallerInScopeAsync(
            (scope, token) => scope.GetRequiredService<MailOutbox>()
                .EnqueueAsync(request, MimeOf(invocationIdentity), token),
            [MailFathomPermission.MailSend],
            cancellationToken);

        return opened.Record;
    }

    /// <summary>Builds a synthetic outgoing message whose bytes differ per scenario.</summary>
    private static ReadOnlyMemory<byte> MimeOf(string discriminator) => Encoding.ASCII.GetBytes(
        $"Message-ID: <{discriminator}@example.test>\r\nSubject: {discriminator}\r\n\r\nSynthetic body.\r\n")
        .AsMemory();
}
