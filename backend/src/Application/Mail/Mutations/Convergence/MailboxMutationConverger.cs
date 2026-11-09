// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Notifications;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Mutations.Convergence;

/// <summary>Drives one account's unfinished mutations towards the only two endings a change is allowed to have.</summary>
/// <remarks>
/// <para>
/// The durable record makes a remote write resumable; this is what makes it converge. A mutation somebody authored is
/// not a request that succeeded or failed at one moment — it is a state the mailbox is heading for — and the endings
/// are completed, or given up on where a person can see it. Pending forever is the third ending this exists to remove,
/// because it looks exactly like success from every screen an operator reads.
/// </para>
/// <para>
/// A pass is deliberately not a retry loop. It takes each outstanding record in hand once and either moves it or leaves
/// it, and everything about *when* to come back belongs to the account's synchronization schedule: an unreachable
/// server fails the run, the run's jittered backoff defers the next one, and the refusal window decides when a change
/// the server keeps declining stops being asked for — kept between runs by <see cref="AbandonExpiredRefusalsAsync" />,
/// which the account's supervision calls without opening a connection. Adding a wait here would put a second retry
/// policy around the same commands, which is the storm both the resilience pipeline and that backoff are shaped to
/// avoid.
/// </para>
/// <para>
/// Resuming is one call: the request is read back off the record and handed to the performer, which finds the record by
/// its idempotency identity and continues the protocol sequence from the stage the record names.
/// Nothing here knows what a relocation is made of, and nothing here decides which command to skip.
/// </para>
/// <para>
/// The one case that is not resumed is a placement whose answer never came back. Reissuing it would put a second
/// message in the destination folder and nothing there afterwards would say which one MailFathom made, so it is settled
/// from what the mailbox has since shown about the source occurrence, or given up on when it stays unsettled for the
/// configured grace period.
/// </para>
/// </remarks>
public sealed class MailboxMutationConverger
{
    private readonly IMailboxMutationRecordStore store;
    private readonly IMailboxMutationPerformer performer;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;
    private readonly IMailboxMutationAuditTrail auditTrail;
    private readonly MailFailureNotifications failures;
    private readonly IMailboxConvergenceDelayTelemetry delays;
    private readonly MailboxConvergenceOptions options;
    private readonly TimeProvider timeProvider;
    private readonly IMailAccountCustodyStore custody;
    private readonly MailboxChangeSubmission submission;
    private readonly ClientSignals signals;
    private readonly TimeSpan refusalWindow;

    /// <summary>Initializes the converger from the record store and the one path able to perform a change.</summary>
    /// <param name="store">Reads the outstanding records and, through the journal, is written back to.</param>
    /// <param name="performer">Resumes a mutation from the stage its record names.</param>
    /// <param name="transportSecurityPolicyReader">Supplies the connection and authentication policy every attempt obeys.</param>
    /// <param name="commitPolicy">Commits the record movements this class makes without the performer.</param>
    /// <param name="auditTrail">Keeps the history a finished mutation leaves behind, where the account asked for one.</param>
    /// <param name="failures">Tells the account's users about a change this class gives up on itself.</param>
    /// <param name="delays">Measures how long a change this class ends itself waited before it ended.</param>
    /// <param name="options">Bounds one pass and carries the unknown-outcome grace period.</param>
    /// <param name="timeProvider">Measures how long an unresolved outcome has been unresolved.</param>
    /// <param name="custody">Answers whether the account is held, where no change is carried to a server.</param>
    /// <param name="submission">Runs a record opened as a local erasure once its window has passed.</param>
    /// <param name="signals">Tells every client of the account to re-read a message whose change this class gives up on.</param>
    /// <param name="mutationOptions">Supplies how long a change the server keeps declining is asked again.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the configured pass bound is below one, the grace period is negative, or the refusal window is not positive.</exception>
    public MailboxMutationConverger(
        IMailboxMutationRecordStore store,
        IMailboxMutationPerformer performer,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        IMailboxMutationAuditTrail auditTrail,
        MailFailureNotifications failures,
        IMailboxConvergenceDelayTelemetry delays,
        MailboxConvergenceOptions options,
        TimeProvider timeProvider,
        IMailAccountCustodyStore custody,
        MailboxChangeSubmission submission,
        ClientSignals signals,
        MailboxMutationOptions mutationOptions)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(performer);
        ArgumentNullException.ThrowIfNull(transportSecurityPolicyReader);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(delays);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(custody);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(mutationOptions);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxMutationsPerPass, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(mutationOptions.RefusalWindow, TimeSpan.Zero, nameof(mutationOptions));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.UnknownOutcomeGrace, TimeSpan.Zero, nameof(options));

        this.store = store;
        this.performer = performer;
        this.transportSecurityPolicyReader = transportSecurityPolicyReader;
        this.commitPolicy = commitPolicy;
        this.auditTrail = auditTrail;
        this.failures = failures;
        this.delays = delays;
        this.options = options;
        this.timeProvider = timeProvider;
        this.custody = custody;
        this.submission = submission;
        this.signals = signals;
        this.refusalWindow = mutationOptions.RefusalWindow;
    }

    /// <summary>Takes one bounded pass over everything the account has asked a mail server for and not seen finished.</summary>
    /// <param name="account">The account whose mutations are converged.</param>
    /// <param name="cancellationToken">Cancels the pass; a mutation already under way is cancelled with it.</param>
    /// <returns>What the pass did, and what the account still owes afterwards.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels the pass.</exception>
    /// <remarks>
    /// One record's failure never ends the pass. A mail server that refuses one change is usually refusing every change,
    /// so the remaining records fail quickly beside it and the account's next run is what defers them; a change that is
    /// broken on its own must not stop the ones that are not, which is the whole of what "does not block unrelated work"
    /// means here.
    /// </remarks>
    public async Task<MailboxConvergenceReport> ConvergeAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var isHeld = await this.custody.ReadAsync(account, cancellationToken) is { Phase: MailAccountCustodyPhase.Held };

        // A held account issues no IMAP command, so a record of any other change opened before the hold, or opened by a
        // restore the hold interrupted, is work no pass on it can do. Reading only the records opened as local erasures
        // keeps those out of the page instead of letting them fill it ahead of the erasures behind them; they stay
        // pending in the lifecycle counts, which is what they are.
        var outstanding = isHeld
            ? await this.store.ReadOutstandingLocalErasuresAsync(account, this.options.MaxMutationsPerPass, cancellationToken)
            : await this.store.ReadOutstandingAsync(account, this.options.MaxMutationsPerPass, cancellationToken);

        if (outstanding.Count == 0 && !isHeld)
        {
            // An empty unfiltered page is nothing to count: the lifecycle read answers over the same rows, so an account with
            // no unfinished mutation — which is nearly every account on nearly every run — spares that query. A held
            // account's page holds only its erasures, so an empty one can still leave inherited records to report.
            return new MailboxConvergenceReport(0, 0, 0, 0, []);
        }

        var transportSecurityPolicy = this.transportSecurityPolicyReader.GetPolicy(account);
        var tally = new ConvergenceTally();

        foreach (var candidate in outstanding)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // An abandoned record is in this answer so it stays visible, not so it is worked on again. It is counted
            // where an operator reads it, in the lifecycle totals below.
            if (candidate.Record.IsTerminal)
            {
                continue;
            }

            // What the record was opened as decides this, never what the account is now. A restoring account opens
            // ordinary records beside its local commits, and a hold taken again while one is outstanding would
            // otherwise erase a local copy whose own disposition asked for it to be kept.
            if (candidate.Record.LocalChange is MailboxMutationLocalChange.Erasure)
            {
                await this.EraseLocalCopyAsync(candidate, tally, cancellationToken);

                continue;
            }

            // The held page is already narrowed to erasures, so nothing ought to reach here. It refuses anyway, because
            // the cost of the two being out of step is an IMAP command issued to a source that is no longer the truth.
            if (isHeld)
            {
                continue;
            }

            if (candidate.Record.Request.LeavesServerUntouched)
            {
                await this.ForgetAsync(candidate, tally, cancellationToken);

                continue;
            }

            await this.ConvergeOneAsync(candidate, transportSecurityPolicy, tally, cancellationToken);
        }

        var outstandingCounts = await this.store.ReadLifecycleCountsAsync(account, cancellationToken);

        return new MailboxConvergenceReport(
            tally.CompletedCount,
            tally.DeadLetteredCount,
            tally.DeferredCount,
            tally.FailedCount,
            outstandingCounts);
    }

    /// <summary>Runs one record opened as a local erasure, whose window has passed, as the erasure it was opened as.</summary>
    /// <remarks>
    /// Such a record is a person's second delete of a message already in the local trash, so this issues no IMAP command
    /// at all and the phase decides nothing: an erasure asked for while the account was held is the same act once the
    /// mailbox is being restored, and running it there is what keeps a message the person erased from being appended
    /// back to the source. Every other record is left where it is — carried on a restoring account, deferred on a held
    /// one — because a delete whose disposition asked for the local copy to be kept is not an erasure whatever phase it
    /// is met in. The isolation is the one a remote change gets, for the same reason.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one erasure's failure so the account's remaining records are still taken in hand; the record stays outstanding and the next pass tries it again.")]
    private async Task EraseLocalCopyAsync(
        OutstandingMailboxMutation candidate,
        ConvergenceTally tally,
        CancellationToken cancellationToken)
    {
        try
        {
            var erased = await this.commitPolicy.CommitAsync(
                (session, token) => this.submission.EraseAsync(session, candidate.Record, token),
                cancellationToken);

            if (erased is not null)
            {
                this.submission.Announce(erased);
            }
            else
            {
                // The cascade removes the record with the message, so only a message that was already gone leaves the
                // record behind, and completing it is what stops every later pass reading it as outstanding again.
                await new MailboxMutationJournal(
                        this.store,
                        this.commitPolicy,
                        this.auditTrail,
                        this.failures,
                        this.delays,
                        this.signals,
                        candidate.Record,
                        candidate.Folder)
                    .CompleteAsync(candidate.Record.Placement, cancellationToken);
            }

            tally.CompletedCount++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            tally.FailedCount++;
        }
    }

    /// <summary>Settles one delete that leaves its server untouched, whose window has passed, without opening a connection to it.</summary>
    /// <remarks>The isolation is the one a remote change gets, for the same reason.</remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one delete's failure so the account's remaining records are still taken in hand; the record stays outstanding and the next pass tries it again.")]
    private async Task ForgetAsync(
        OutstandingMailboxMutation candidate,
        ConvergenceTally tally,
        CancellationToken cancellationToken)
    {
        try
        {
            var forgotten = await this.commitPolicy.CommitAsync(
                (session, token) => this.submission.ForgetAsync(session, candidate, token),
                cancellationToken);

            if (forgotten is not null)
            {
                this.submission.Announce(forgotten);
            }

            tally.CompletedCount++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            tally.FailedCount++;
        }
    }

    /// <summary>Moves one record, and absorbs whatever moving it failed on.</summary>
    /// <remarks>
    /// The isolation is around both paths rather than around the mail server alone, because a record read at the start
    /// of the pass can be moved by a concurrent run before this one acts on it — a stage that will not go backwards is
    /// refused where it is written, which is the point of that rule, and a refusal there must cost this account its
    /// remaining mutations no more than a refusal from a mail server does.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A pass isolates one mutation's failure so the account's remaining mutations still converge; the record already carries how far it got and the account's run backoff defers the next attempt.")]
    private async Task ConvergeOneAsync(
        OutstandingMailboxMutation candidate,
        MailTransportSecurityPolicy transportSecurityPolicy,
        ConvergenceTally tally,
        CancellationToken cancellationToken)
    {
        try
        {
            if (candidate.Record.HasUnknownOutcome)
            {
                await this.SettleUnknownOutcomeAsync(candidate, tally, cancellationToken);

                return;
            }

            // A change that waited on an earlier move of its message is issued where that move left the message, which
            // only the moment of issue can know.
            var issued = candidate.Record.Stage == MailboxMutationStage.Recorded
                ? await this.commitPolicy.CommitAsync(
                    (session, token) => this.store.ResolveIssuedOccurrenceAsync(session, candidate, token),
                    cancellationToken)
                : candidate;

            var outcome = await this.performer.PerformAsync(
                issued.Record.Request,
                issued.Folder,
                transportSecurityPolicy,
                cancellationToken);

            tally.Count(outcome.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The performer has already written the failure onto the record, so nothing is lost by swallowing it here.
            // A refusal never arrives here — the server answered, so the performer returned a status for it — which
            // leaves only failures to reach the server, and a pass that met one is a run that failed: that is what puts
            // the account into backoff without counting anything against the change.
            tally.FailedCount++;
        }
    }

    /// <summary>Settles a placement whose answer never came back, from the mailbox rather than by asking again.</summary>
    /// <remarks>
    /// <para>
    /// The three mutations differ here, and the difference is why this is bounded work rather than a retry. A relocation
    /// carried by <c>MOVE</c> removes the source as part of the same command, so a source that has since been seen to
    /// leave its folder is the server's own statement that the command ran, against an occurrence the record names
    /// exactly. A copy and a fallback relocation both leave the source in place, so nothing about it distinguishes a
    /// command that landed from one that never arrived, and the only way to tell would be to search the destination
    /// folder for a message that looks like the right one — a guess about identity, which
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
    /// refuses.
    /// </para>
    /// <para>
    /// What is left for those is time. The record waits for the grace period in case a later run settles it, and is then
    /// given up on so it stands as dead-lettered rather than as a change that is still apparently happening. Giving up
    /// removes nothing and duplicates nothing: it stops MailFathom claiming to know an outcome it does not.
    /// </para>
    /// </remarks>
    private async Task SettleUnknownOutcomeAsync(
        OutstandingMailboxMutation candidate,
        ConvergenceTally tally,
        CancellationToken cancellationToken)
    {
        var record = candidate.Record;
        var journal = new MailboxMutationJournal(
            this.store,
            this.commitPolicy,
            this.auditTrail,
            this.failures,
            this.delays,
            this.signals,
            record,
            candidate.Folder);

        if (record.IsUnknownPlacementSettledBySourceRemoval)
        {
            await journal.CompleteAsync(record.Placement, cancellationToken);
            tally.CompletedCount++;

            return;
        }

        if (this.timeProvider.GetUtcNow() - record.StageChangedAt < this.options.UnknownOutcomeGrace)
        {
            tally.DeferredCount++;

            return;
        }

        // Nobody learned whether the placement landed, so asking again might well be answered differently; the person is
        // offered the act again rather than told it is refused.
        await journal.AbandonAsync(
            MailFathomErrorCode.MailboxMutationOutcomeUnknown,
            MailboxMutationRefusalClass.Transient,
            cancellationToken);
        tally.DeadLetteredCount++;
    }

    /// <summary>Gives up on every change of the account whose refusal window has passed, without opening a connection.</summary>
    /// <param name="account">The account whose refused changes are read.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>When the next refused change's window will pass, or <see langword="null" /> when no refused change remains.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels the pass.</exception>
    /// <remarks>
    /// <para>
    /// This is what makes the window a promise rather than an estimate. The performer reads it too, but only when a run
    /// takes the change in hand, and a run is as far away as the account's interval or its backoff; an outage that
    /// follows a refusal would hold the change for as long as the outage lasted. The account's supervision calls this
    /// between runs at the deadline it returned, so the change is undone and the person told on time whatever the
    /// server is doing. Each one is given up on exactly as the performer would: its overlaid fields restored in the
    /// transaction that abandons it, every client told to re-read, and a notification raised.
    /// </para>
    /// <para>
    /// A held account carries nothing to a server, so nothing is refused there and nothing is given up on; a change
    /// refused before the hold waits with the rest of what the account owes its source.
    /// </para>
    /// </remarks>
    public async Task<DateTimeOffset?> AbandonExpiredRefusalsAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (await this.custody.ReadAsync(account, cancellationToken) is { Phase: MailAccountCustodyPhase.Held })
        {
            return null;
        }

        var refused = await this.store.ReadRefusedAsync(account, this.options.MaxMutationsPerPass, cancellationToken);
        DateTimeOffset? nextDeadline = null;

        foreach (var candidate in refused)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var deadline = candidate.Record.FirstRefusedAt!.Value + this.refusalWindow;

            if (deadline > this.timeProvider.GetUtcNow())
            {
                nextDeadline = nextDeadline is { } earlier && earlier < deadline ? earlier : deadline;

                continue;
            }

            await this.AbandonExpiredRefusalAsync(candidate, cancellationToken);
        }

        return nextDeadline;
    }

    /// <summary>Gives up on one change whose refusal window has passed, and absorbs a write that lost a race.</summary>
    /// <remarks>
    /// A run on this account may have taken the same record in hand and ended it a moment earlier; the store refuses to
    /// move a terminal record, and that refusal must not cost the account the rest of its expired changes.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "One record's failed ending must not stop the account's other expired changes from being given up on; the record stays outstanding and the next pass, or the next deadline, takes it again.")]
    [SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception", Justification = "Nothing is lost by the empty clause: a record that did not end stays outstanding, and the performer gives it up on the next run because its window has already passed.")]
    private async Task AbandonExpiredRefusalAsync(
        OutstandingMailboxMutation candidate,
        CancellationToken cancellationToken)
    {
        var journal = new MailboxMutationJournal(
            this.store,
            this.commitPolicy,
            this.auditTrail,
            this.failures,
            this.delays,
            this.signals,
            candidate.Record,
            candidate.Folder);

        try
        {
            await journal.AbandonAsync(
                candidate.Record.LastFailure ?? MailFathomErrorCode.MailboxMutationDeclined,
                MailboxMutationRefusalClass.Transient,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Left for the next pass, which reads it again if it is still outstanding.
        }
    }

    /// <summary>Accumulates what one pass did, so the counting is not spread across four call sites.</summary>
    private sealed class ConvergenceTally
    {
        internal int CompletedCount { get; set; }

        internal int DeadLetteredCount { get; set; }

        internal int DeferredCount { get; set; }

        internal int FailedCount { get; set; }

        internal void Count(MailboxMutationStatus status)
        {
            switch (status)
            {
                case MailboxMutationStatus.Performed:
                case MailboxMutationStatus.AlreadyPerformed:
                    this.CompletedCount++;
                    break;
                case MailboxMutationStatus.Abandoned:
                    this.DeadLetteredCount++;
                    break;

                // The server answered and declined, and the window has not passed: the change is asked again on a later
                // pass. It is no failure of the run — the server is healthy — so it never backs the account off.
                case MailboxMutationStatus.Refused:

                // Both are records a concurrent writer moved after this pass read them, and both are left exactly where
                // the performer left them. A withdrawn one is counted here rather than given a total of its own because
                // the counts say what this pass did, and what it did with it was nothing; the outstanding figures
                // beside them are read afresh and never count a withdrawn record at all.
                case MailboxMutationStatus.OutcomeUnknown:
                case MailboxMutationStatus.Withdrawn:
                default:
                    this.DeferredCount++;
                    break;
            }
        }
    }
}
