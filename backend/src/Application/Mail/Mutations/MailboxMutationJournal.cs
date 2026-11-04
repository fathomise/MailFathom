// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Notifications;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Writes one mutation record's every movement, each in a transaction of its own.</summary>
/// <remarks>
/// <para>
/// It is the single writer of a record after the record exists, which is what keeps the rule that a stage only moves
/// forward in one place. Every write commits before it returns and none of them joins a transaction the caller is
/// holding: a stage remembered only in memory is exactly the stage lost to the crash the record exists to survive, and a
/// transaction held open across a mail server would be the one thing the persistence rules forbid outright.
/// </para>
/// <para>
/// Each write is idempotent from a fresh read, so it goes through the optimistic concurrency policy rather than being
/// attempted once. A concurrent reader of the same account's mutations is ordinary, and losing a race is not a reason to
/// leave a stage unwritten.
/// </para>
/// <para>
/// Being that single writer is also why the audit trail is appended from here rather than from the callers. The two
/// terminal stages are reached from four places between the performer and convergence, and an entry owed by one of them
/// and not written by another would be a history whose gaps mean nothing. The append happens after the terminal stage is
/// durable and cannot fail the mutation, which <see cref="IMailboxMutationAuditTrail" /> states in full. How long the
/// change waited before ending is measured at the same point and for the same reason: an ending reached from one of
/// those places and not measured from another would make the wait read shorter than it is.
/// </para>
/// <para>
/// The same reasoning puts the notification about a change given up here, and the signal that tells every client to
/// re-read what it shows: every way a record is abandoned — a permanent refusal, a transient one whose window passed, an
/// outcome nobody learned, on a mirrored account and on the record a restoring account opens to carry a change to its
/// source — ends in <see cref="AbandonAsync" />, and the store refuses to move a record out of a terminal stage, so each
/// is raised exactly once per record. The store restores a field the change overlaid in the same transaction that
/// abandons it, so by the time the signal goes out every reader already sees the field as the server last showed it.
/// </para>
/// </remarks>
internal sealed class MailboxMutationJournal : IMailboxMutationJournal
{
    private readonly IMailboxMutationRecordStore store;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IMailboxMutationAuditTrail auditTrail;
    private readonly MailFailureNotifications failures;
    private readonly IMailboxConvergenceDelayTelemetry delays;
    private readonly ClientSignals signals;
    private readonly MailFolderResolution sourceFolder;
    private MailboxMutationRecord record;

    internal MailboxMutationJournal(
        IMailboxMutationRecordStore store,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IMailboxMutationAuditTrail auditTrail,
        MailFailureNotifications failures,
        IMailboxConvergenceDelayTelemetry delays,
        ClientSignals signals,
        MailboxMutationRecord record,
        MailFolderResolution sourceFolder)
    {
        this.store = store;
        this.commitPolicy = commitPolicy;
        this.auditTrail = auditTrail;
        this.failures = failures;
        this.delays = delays;
        this.signals = signals;
        this.record = record;
        this.sourceFolder = sourceFolder;
    }

    /// <summary>Gets the record every write here names.</summary>
    internal MailboxMutationRecordId RecordId => this.record.Id;

    /// <inheritdoc />
    public MailboxMutationStage Stage => this.record.Stage;

    /// <inheritdoc />
    public RemoteEmailPlacement Placement => this.record.Placement;

    /// <inheritdoc />
    public bool RequiresSourceRemoval => this.record.RequiresSourceRemoval;

    /// <summary>Gets when the server first declined the change in a way asking again might alter, or <see langword="null" /> while it has not.</summary>
    internal DateTimeOffset? FirstRefusedAt => this.record.FirstRefusedAt;

    /// <summary>Gets the failure the last attempt ended in, or <see langword="null" /> while none has.</summary>
    internal MailFathomErrorCode? LastFailure => this.record.LastFailure;

    /// <inheritdoc />
    public async Task PlacementIssuedAsync(bool requiresSourceRemoval, CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.RecordPlacementIssuedAsync(
                session,
                this.RecordId,
                requiresSourceRemoval,
                token),
            cancellationToken);

        this.record = this.record with
        {
            Stage = MailboxMutationStage.PlacementIssued,
            RequiresSourceRemoval = requiresSourceRemoval,
        };
    }

    /// <inheritdoc />
    public Task PlacementConfirmedAsync(RemoteEmailPlacement placement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placement);

        return this.AdvanceAsync(MailboxMutationStage.PlacementConfirmed, placement, cancellationToken);
    }

    /// <inheritdoc />
    public Task SourceFlaggedDeletedAsync(CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.SourceFlaggedDeleted, placement: null, cancellationToken);

    /// <summary>Records that the server answered and declined the change in a way asking again might alter.</summary>
    internal async Task RecordRefusalAsync(MailFathomErrorCode refusal, CancellationToken cancellationToken)
    {
        var firstRefusedAt = default(DateTimeOffset);

        await this.commitPolicy.CommitAsync(
            async (session, token) =>
            {
                firstRefusedAt = await this.store.RecordRefusalAsync(session, this.RecordId, refusal, token);
            },
            cancellationToken);

        // A placement the server answered with a refusal placed nothing, so the store returns the record to the stage it
        // was issued from; held here too, so this journal does not read the answer as one that never came.
        this.record = this.record with
        {
            Stage = this.record.Stage is MailboxMutationStage.PlacementIssued
                ? MailboxMutationStage.Recorded
                : this.record.Stage,
            RequiresSourceRemoval = this.record.Stage is not MailboxMutationStage.PlacementIssued
                && this.record.RequiresSourceRemoval,
            AttemptCount = this.record.AttemptCount + 1,
            FirstRefusedAt = firstRefusedAt,
            LastFailure = refusal,
        };
    }

    /// <summary>Ends the record in the stage that says the change was made.</summary>
    internal Task CompleteAsync(RemoteEmailPlacement placement, CancellationToken cancellationToken) =>
        this.AdvanceAsync(MailboxMutationStage.Completed, placement, cancellationToken);

    /// <summary>Ends the record in the stage that says nothing will attempt the change again, and tells whoever reads the message.</summary>
    /// <param name="failure">What ended the change.</param>
    /// <param name="refusalClass">Whether asking for the change again could come out differently, which decides what the person is offered.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    internal async Task AbandonAsync(
        MailFathomErrorCode failure,
        MailboxMutationRefusalClass refusalClass,
        CancellationToken cancellationToken)
    {
        await this.RecordFailureAsync(failure, cancellationToken);
        await this.AdvanceAsync(MailboxMutationStage.Abandoned, placement: null, cancellationToken);
        this.AnnounceAbandonment();
        await this.ReportAbandonmentAsync(refusalClass, cancellationToken);
    }

    /// <summary>Records why an attempt ended without moving the stage the next attempt resumes from.</summary>
    internal async Task RecordFailureAsync(MailFathomErrorCode failure, CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.RecordFailureAsync(session, this.RecordId, failure, token),
            cancellationToken);

        this.record = this.record with { LastFailure = failure };
    }

    /// <summary>Tells the account's users that the change was given up, and never lets that fail the mutation.</summary>
    /// <remarks>
    /// The record is already terminal, so what fails here is the notification and nothing else, exactly as an audit
    /// entry that could not be written leaves the mutation standing. The caller's own cancellation travels on.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The change has already been given up durably; a notification that could not be written must not turn that into a failed mutation the converger would then report again.")]
    [SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception", Justification = "There is no second action to take: the record already says the change was given up.")]
    private async Task ReportAbandonmentAsync(
        MailboxMutationRefusalClass refusalClass,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.failures.ReportAbandonedChangeAsync(this.record, refusalClass, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Nothing to restore: the record says the change was given up whatever became of telling anybody.
        }
    }

    /// <summary>Tells every client of the account to re-read the message, whose stored state the abandonment may have restored.</summary>
    /// <remarks>
    /// Named by no folder: a move given up on puts the message back in the folder it left, so the statement reaches every
    /// folder of the account rather than one named wrong. It travels over the signal backplane where one is configured,
    /// so a client connected to another replica re-reads as well.
    /// </remarks>
    private void AnnounceAbandonment()
    {
        if (this.signals.Reaches)
        {
            this.signals.Publish(
                ClientSignal.MailChanged(this.record.Request.Account, folder: null, [this.record.Request.StoredEmailId]));
        }
    }

    private async Task AdvanceAsync(
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken)
    {
        await this.commitPolicy.CommitAsync(
            (session, token) => this.store.AdvanceAsync(session, this.RecordId, stage, placement, token),
            cancellationToken);

        this.record = this.record with
        {
            Stage = stage,
            Placement = placement ?? this.record.Placement,
        };

        if (this.record.IsTerminal)
        {
            // A local erasure is a person's second delete of a message already in the local trash, and it is never
            // carried to a mail server, so its ending says nothing about how long a change waited for one.
            if (this.record.LocalChange is not MailboxMutationLocalChange.Erasure)
            {
                this.delays.Ended(this.record);
            }

            await this.auditTrail.RecordAsync(this.record, this.sourceFolder, cancellationToken);
        }
    }
}
