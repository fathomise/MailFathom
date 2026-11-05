// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Keeps the durable record of every change MailFathom asked a mail server to make.</summary>
/// <remarks>
/// <para>
/// The idempotency identity is the email occurrence, the requester, and the mutation together, and it is enforced by a
/// unique constraint rather than by this contract declining to write. Two callers asking for the same change at the
/// same moment both reach the database, and one of them loses there; a check-then-insert would let both through the
/// window between the two statements, which is the window the crash-safety of everything above depends on being closed.
/// </para>
/// <para>
/// Writes take the caller's session because a mutation record is written alongside whatever else the caller is
/// committing. Reads take none, because a read joins no transaction.
/// </para>
/// </remarks>
public interface IMailboxMutationRecordStore
{
    /// <summary>Writes the intent down, or reads back the record that already holds this idempotency identity.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="request">The change that was asked for.</param>
    /// <param name="heldUntil">The instant before which no convergence pass may take the record in hand, or <see langword="null" /> where it may be taken at once.</param>
    /// <param name="localChange">What the record has to do with MailFathom's own copy: nothing, the erasure a second delete of a trashed message opens on an account holding or restoring its mailbox, or the change a restoring account has already committed and is opening this record to carry to the source.</param>
    /// <param name="cancellationToken">Cancels the write or the read that follows a losing insert.</param>
    /// <returns>The record for this request, whether this call created it or another one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>The record starts at <see cref="MailboxMutationStage.Recorded" /> with no attempt counted, so opening one performs nothing by itself.</para>
    /// <para>
    /// A hold is what makes a change withdrawable for a stated stretch after it was asked for, and it belongs to the
    /// record rather than to whoever asked: a client that closes, loses its network, or is put to sleep costs the
    /// mailbox nothing, because the window elapses and the record is taken in hand exactly as it would have been. It
    /// is honoured by <see cref="ReadOutstandingAsync(MailAccountId, int, CancellationToken)" /> and lifted by <see cref="ReleaseAsync" />, and a record that
    /// already exists under this identity keeps the hold it was opened with rather than taking this call's.
    /// </para>
    /// <para>
    /// What the record is for is written down with it for the same reason, and read back as
    /// <see cref="MailboxMutationRecord.LocalChange" />: a record outlives the phase it was opened under, and the three
    /// kinds a held account can meet are told apart by what each was opened as rather than by what the account is now.
    /// A record already existing under this identity keeps the value it was opened with rather than taking this call's,
    /// exactly as it keeps its hold, because both say what was decided when the intent became durable.
    /// </para>
    /// </remarks>
    Task<MailboxMutationRecord> OpenAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        DateTimeOffset? heldUntil,
        MailboxMutationLocalChange localChange,
        CancellationToken cancellationToken);

    /// <summary>Opens the record of a change committed locally on a mirrored account, which overlays the stored message until it ends.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="request">The change.</param>
    /// <param name="heldUntil">How long the record waits before a convergence pass may take it in hand.</param>
    /// <param name="replaced">The stored values the act replaces, which are the server's last observation of each field no earlier change is still outstanding on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record, and whether this call opened it rather than finding it already written for the same request.</returns>
    /// <remarks>
    /// <para>
    /// A field an earlier overlaying change is still outstanding on stores that change's value rather than the server's,
    /// so the new record takes the earlier record's last observation of the field instead of <paramref name="replaced" />.
    /// An earlier change still unissued that this one supersedes (<see cref="MailboxMutationRequest.Supersedes" />) is
    /// withdrawn in the same transaction, so only the state last asked for reaches the server.
    /// </para>
    /// <para>
    /// A request already recorded is answered with its record and nothing is withdrawn, so the caller commits nothing
    /// locally a second time.
    /// </para>
    /// </remarks>
    Task<OpenedMailboxMutationRecord> OpenOverlayAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        DateTimeOffset? heldUntil,
        MailboxMutationOverlay replaced,
        CancellationToken cancellationToken);

    /// <summary>Names, on an overlaying copy's record, the second stored message the copy wrote locally.</summary>
    /// <param name="session">The transaction the copy commits in, which opened the record.</param>
    /// <param name="recordId">The copy's record.</param>
    /// <param name="copy">The second stored message.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record as it now stands.</returns>
    Task<MailboxMutationRecord> RecordLocalCopyAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        StoredEmailId copy,
        CancellationToken cancellationToken);

    /// <summary>Reports whether one local email has ever had a mutation of a given kind asked for by a given kind of requester.</summary>
    /// <param name="storedEmailId">The local email, which is the identity that survives the email being moved.</param>
    /// <param name="mutation">The change asked for.</param>
    /// <param name="origin">The kind of act that asked.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when at least one such record exists, whatever stage it reached.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mutation" /> is unspecified.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin.</exception>
    /// <remarks>
    /// <para>
    /// It answers a question the idempotency identity deliberately cannot: whether this email was ever moved by this kind
    /// of requester, rather than whether one particular request has already been made. The identity is keyed to the
    /// occurrence and to what asked, so a message that has since moved is a new occurrence and a requester whose terms
    /// changed is a new requester — both of which ask afresh, which is right for a retry and wrong for deciding whether
    /// somebody has since undone the change.
    /// </para>
    /// <para>
    /// Every stage counts, including an abandoned one. What the caller is establishing is that MailFathom has already
    /// acted on this email once, and a change that was attempted and given up on is still a change the user may have
    /// seen and reversed.
    /// </para>
    /// </remarks>
    Task<bool> HasRecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutation mutation,
        MailboxMutationOrigin origin,
        CancellationToken cancellationToken);

    /// <summary>Reads the records a change on the caller's own accounts carries, by the identities that change was answered with.</summary>
    /// <param name="accounts">The accounts the caller is assigned, which are the accounts the records must belong to.</param>
    /// <param name="recordIds">The records to read, in any order and with repetitions.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records those accounts hold under those identities, ordered by when each was recorded, and empty where they hold none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> or <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The accounts are a parameter rather than something the caller checks afterwards, because they are what makes an
    /// identifier on a mailbox the caller is not assigned unreadable rather than merely unreported: a record outside
    /// them is absent from the answer, so no timing or shape separates one that exists from one that never did. A
    /// record identity is generated rather than guessable, and this is what keeps that from being the only thing
    /// standing between two mailboxes. An empty list therefore reads nothing rather than everything, which is what a
    /// caller assigned no account is answered with.
    /// </para>
    /// <para>
    /// Every stage is answered, completed and terminal ones included, because the caller is asking where its own change
    /// got to and the answer it is waiting for is the one that says the change is done.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReadAsync(
        IReadOnlyList<MailAccountId> accounts,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken);

    /// <summary>Withdraws the changes among those named on the caller's own accounts, wherever nothing has been asked of the mail server for one yet.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="accounts">The accounts the caller is assigned, which are the accounts the records must belong to.</param>
    /// <param name="recordIds">The records to withdraw.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Each named record as it now stands, unchanged where the change had already been attempted, and absent where those accounts hold no such record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" />, <paramref name="accounts" /> or <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A record past <see cref="MailboxMutationRecord.IsWithdrawable" /> is returned rather than refused, because the
    /// caller's question is what became of the change and "it had already gone out" is the answer to it. That also makes
    /// the call safe to repeat: withdrawing an already withdrawn record reports it withdrawn and writes nothing.
    /// </para>
    /// <para>
    /// The call takes the whole set rather than one record, because a caller withdrawing what it submitted as a batch
    /// withdraws it as one, and the commit this joins retries as a whole — so a call per record would pay the round trip
    /// again on every attempt.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> WithdrawAsync(
        IPersistenceSession session,
        IReadOnlyList<MailAccountId> accounts,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken);

    /// <summary>Lifts the hold on the changes among those named on the caller's own accounts, so the next convergence pass may take each in hand.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="accounts">The accounts the caller is assigned, which are the accounts the records must belong to.</param>
    /// <param name="recordIds">The records to release.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Each named record as it now stands, unchanged where nothing was holding it, and absent where those accounts hold no such record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" />, <paramref name="accounts" /> or <paramref name="recordIds" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It is the opposite half of <see cref="WithdrawAsync" /> and is shaped like it for the same reasons: a record
    /// nothing was holding is reported where it stands rather than refused, which is what makes the call safe to
    /// repeat, and the whole set is taken in one commit because a caller that submitted a batch releases it as one.
    /// Releasing changes no stage, so a record already withdrawn stays withdrawn and one already under way is
    /// untouched — what it removes is a wait, never a decision.
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationRecord>> ReleaseAsync(
        IPersistenceSession session,
        IReadOnlyList<MailAccountId> accounts,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken);

    /// <summary>Records that the mail server answered the change and declined it in a way asking again might alter.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record the refused attempt belonged to.</param>
    /// <param name="failure">The code identifying the refusal.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>When the server first declined the change, which this write sets where no earlier refusal had.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />.</exception>
    /// <remarks>
    /// The attempt is counted here, after the answer, rather than before the command, because only an answer is an
    /// attempt: a server that could not be reached was asked nothing. The first refusal's instant is kept rather than
    /// moved, since it is what the refusal window is measured from.
    /// </remarks>
    Task<DateTimeOffset> RecordRefusalAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to <see cref="MailboxMutationStage.PlacementIssued" /> and states what the command will leave behind.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record to advance.</param>
    /// <param name="requiresSourceRemoval"><see langword="true" /> when the command leaves the source in place and a separate removal will be owed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage and the answer are written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />, or when the record has already passed this stage.</exception>
    /// <remarks>
    /// It is a transition of its own rather than a flag on <see cref="AdvanceAsync" />, because this is the only stage
    /// the answer is knowable at and the only one it means anything for. Writing both in one transaction is what stops
    /// a crash between them from leaving a placement whose obligation nobody recorded.
    /// </remarks>
    Task RecordPlacementIssuedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        bool requiresSourceRemoval,
        CancellationToken cancellationToken);

    /// <summary>Moves the record to a later stage, recording the placement where the server named one.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record to advance.</param>
    /// <param name="stage">The stage the mutation has now reached.</param>
    /// <param name="placement">Where the server said it put the email, or <see langword="null" /> to leave the recorded placement alone.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the stage is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />, or when the record has already reached a terminal stage or a later one than <paramref name="stage" />.</exception>
    /// <remarks>A stage only ever moves forward, so a late write from a lost attempt cannot pull a mutation back to a stage a retry has already passed.</remarks>
    Task AdvanceAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken);

    /// <summary>Records the failure the last attempt ended in, without moving the stage.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="recordId">The record the attempt belonged to.</param>
    /// <param name="failure">The code identifying what ended the attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the failure is written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no record carries <paramref name="recordId" />.</exception>
    /// <remarks>The stage stays where the sequence actually got to, which is what a resumed attempt reads; the failure says why it got no further.</remarks>
    Task RecordFailureAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken);

    /// <summary>Reads the mutations of one account that have not completed, with the folder binding each was recorded against.</summary>
    /// <param name="account">The account whose mutations are read.</param>
    /// <param name="limit">The greatest number of records to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding records, oldest first, at most <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// An abandoned mutation is part of the answer rather than excluded from it. Reaching a terminal failed stage is
    /// what stops a change being retried, and it would be worth nothing if it also stopped the change being seen: the
    /// operator's question is which changes are in flight and which are stuck, and a mutation nothing will attempt again
    /// is the second kind. Only a completed one leaves this answer.
    /// </para>
    /// <para>
    /// Oldest first, because the answer starts with whatever has been outstanding longest. It is bounded like every
    /// other public query, and convergence treats the bound as a page it comes back for rather than as a cut.
    /// </para>
    /// <para>
    /// A record still inside the hold <see cref="OpenAsync" /> opened it under is absent from this answer, and it is
    /// absent rather than skipped afterwards so that a page is spent on work a pass can actually do. It counts as
    /// pending in <see cref="ReadLifecycleCountsAsync" /> throughout, which is what it is: a change asked for that
    /// nothing has started.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Reads the mutations of one account that have not completed and were opened to erase MailFathom's own copy, with the folder binding each was recorded against.</summary>
    /// <param name="account">The account whose mutations are read.</param>
    /// <param name="limit">The greatest number of records to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding records opened as <see cref="MailboxMutationLocalChange.Erasure" />, oldest first, at most <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// The same answer as the unfiltered read, narrowed for a pass that can do only one kind of work: a held account
    /// runs its erasures and issues nothing else, so every other record is left out of its page for the reason a record
    /// inside its hold is — a page spent on records the pass cannot act on is a page its real backlog does not get. The
    /// narrowing is the mark rather than the kind of change, because a delete a restore opened for the source is a
    /// delete too, and one outstanding on an account that is held again would otherwise fill the page forever.
    /// </remarks>
    Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingLocalErasuresAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Points a change nothing has issued yet at the occurrence its message holds now, which an earlier move may have changed.</summary>
    /// <param name="session">The session the write joins, which is the one committed before the change is issued.</param>
    /// <param name="outstanding">The change about to be issued, as the outstanding read returned it.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>The change against the occurrence it is to be issued at, which is <paramref name="outstanding" /> itself where nothing moved it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="outstanding" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A change waits while an earlier move of the same message is outstanding, and the move, once the server takes it,
    /// leaves the message at an occurrence the change was not recorded against: the source is gone, and the destination
    /// is named by the move's own placement until the destination's forward pass carries the stored message onto it.
    /// So the occurrence is resolved here, at issue time, rather than read off the change — from the stored message
    /// where it already holds the new one, and otherwise from each completed move whose placement nothing has carried
    /// yet, followed from the occurrence the message last held. See
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
    /// axis F.
    /// </para>
    /// <para>
    /// The record itself is rewritten, because a change is resumed by finding its record again, and the identity it is
    /// found by includes the occurrence. A change already issued is never repointed: its command named the occurrence it
    /// was recorded against, and what that command left is settled from there.
    /// </para>
    /// </remarks>
    Task<OutstandingMailboxMutation> ResolveIssuedOccurrenceAsync(
        IPersistenceSession session,
        OutstandingMailboxMutation outstanding,
        CancellationToken cancellationToken);

    /// <summary>Counts one account's uncompleted mutations by kind and by where in its lifecycle each one stands.</summary>
    /// <param name="account">The account whose mutations are counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per kind and lifecycle that has at least one record, in no particular order.</returns>
    /// <remarks>
    /// <para>
    /// It is an aggregate rather than a count of what <see cref="ReadOutstandingAsync(MailAccountId, int, CancellationToken)" /> returned, because that read is
    /// bounded and a bounded count is wrong exactly when it matters — the moment an account has more stuck changes than
    /// one pass looks at is the moment somebody needs the real number.
    /// </para>
    /// <para>
    /// The database groups and counts, so the answer is a handful of rows however many mutations the account has: at
    /// most one per permitted mutation per lifecycle. Nothing derived from a message takes part in it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<MailboxMutationLifecycleCount>> ReadLifecycleCountsAsync(
        MailAccountId account,
        CancellationToken cancellationToken);

    /// <summary>Reads one account's changes the mail server has declined and that have not ended, with the folder binding each was recorded against.</summary>
    /// <param name="account">The account whose refused changes are read.</param>
    /// <param name="limit">The greatest number of records to return.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records carrying a first refusal, no terminal stage, and no placement awaiting its answer, earliest first refusal first, at most <paramref name="limit" /> of them.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// It is what the refusal window is kept from without a connection: the account's supervision reads it between runs
    /// to give up on the changes whose window has passed and to learn when the next one will. Earliest first, so a page
    /// always holds the deadlines that come soonest.
    /// </para>
    /// <para>
    /// A record at <see cref="MailboxMutationStage.PlacementIssued" /> is left out even when an earlier attempt was
    /// refused: its last placement went unanswered, so the server may have made the copy, and the converger settles it
    /// from what the mailbox shows rather than undoing it on a deadline.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<OutstandingMailboxMutation>> ReadRefusedAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Reads, for each of the accounts named, how many changes MailFathom holds that have not reached its mail server, and when the oldest was committed.</summary>
    /// <param name="accounts">The accounts to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>One entry per named account that has at least one change waiting; an account with none is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A change is waiting from the moment it is recorded until it ends, whatever stage it has reached on the way, because
    /// a person reads it as made and the mail server does not have it yet. A local erasure is not one, since it is never
    /// carried to a mail server, and a send is not one either: it is the outbox's, which reports it apart. The database
    /// groups and counts, so the answer is one row per account however many changes wait.
    /// </remarks>
    Task<IReadOnlyDictionary<MailAccountId, MailboxWaitingChanges>> ReadWaitingChangesAsync(
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken);
}
