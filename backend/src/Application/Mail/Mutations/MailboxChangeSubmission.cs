// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Takes one authored change on a message in hand, through whichever custodian holds the truth about the account's mailbox.</summary>
/// <remarks>
/// <para>
/// Every requester — a person through the client, a tool, a rule, and a spam verdict — reaches a mailbox through a use
/// case that submits here, so none of them branches on where the mail lives:
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
/// carries every act through the custodian <see cref="MailboxCustodianResolver" /> chooses, and this is where a change
/// on a message reaches it.
/// </para>
/// <para>
/// The custodian is chosen inside the caller's transaction rather than before it, so a change and the custody it was
/// judged under commit together. What each custodian does with it — a durable record the convergence pass carries, a
/// local commit, or both — is that custodian's own.
/// </para>
/// <para>
/// A copy is the one change that may take two phases, because a local copy creates a second stored message with a
/// payload of its own and a payload is placed with no transaction open across the placement. The caller places one
/// through <see cref="PrepareCopiesAsync" /> before it opens its transaction and hands it back in, which is the same
/// shape the destination folders already take and for the same reason.
/// </para>
/// </remarks>
public sealed class MailboxChangeSubmission
{
    private readonly MailboxCustodianResolver custodians;
    private readonly IMailboxMutationRecordStore records;
    private readonly ILocalEmailStateStore states;
    private readonly IMailboxMutationAuditEntryStore auditEntries;
    private readonly ClientSignals signals;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the submission over the custodians and what a held erasure is completed through.</summary>
    /// <param name="custodians">Chooses the custodian a change is carried through.</param>
    /// <param name="records">Advances a held erasure's record once its window has passed.</param>
    /// <param name="states">Reads and erases the stored message a held erasure removes.</param>
    /// <param name="auditEntries">Appends a held erasure's audit entry in its own transaction.</param>
    /// <param name="signals">Announces a local change to the clients watching the account.</param>
    /// <param name="timeProvider">Stamps a held erasure's audit entry.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MailboxChangeSubmission(
        MailboxCustodianResolver custodians,
        IMailboxMutationRecordStore records,
        ILocalEmailStateStore states,
        IMailboxMutationAuditEntryStore auditEntries,
        ClientSignals signals,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(custodians);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(auditEntries);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.custodians = custodians;
        this.records = records;
        this.states = states;
        this.auditEntries = auditEntries;
        this.signals = signals;
        this.timeProvider = timeProvider;
    }

    /// <summary>Records or commits one change, inside the caller's transaction.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="request">The change asked for.</param>
    /// <param name="destination">Where a relocation or a copy files the message, resolved before the transaction opened; <see langword="null" /> for every other change.</param>
    /// <param name="heldUntil">How long a written record waits before a convergence pass may take it in hand.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record written, the local change committed, or why neither happened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A local change is announced only once the caller's transaction has committed, which is why this returns what to
    /// announce and <see cref="Announce" /> publishes it: a signal raised inside a transaction that then rolls back would
    /// send a client to read a change that never happened.
    /// </remarks>
    public Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination? destination,
        DateTimeOffset? heldUntil,
        CancellationToken cancellationToken) =>
        this.SubmitAsync(session, request, destination, heldUntil, preparedCopy: null, cancellationToken);

    /// <summary>Records or commits one change, inside the caller's transaction, with the copy a local custodian would need.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="request">The change asked for.</param>
    /// <param name="destination">Where a relocation or a copy files the message, resolved before the transaction opened; <see langword="null" /> for every other change.</param>
    /// <param name="heldUntil">How long a written record waits before a convergence pass may take it in hand.</param>
    /// <param name="preparedCopy">The payload a local copy was placed with, from <see cref="PrepareCopiesAsync" />; <see langword="null" /> for every other change and for an account whose source is the truth.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record written, the local change committed, or why neither happened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A caller hands the copy in rather than the submission placing one, because placing a payload must not happen with
    /// a transaction open across it and this is called inside one. Handing in nothing is what a caller that could not
    /// place one has, and a local copy then answers <see cref="MailboxChangeSubmissionOutcome.SourceContentMissing" />
    /// rather than committing a row naming a payload that was never placed.
    /// </remarks>
    public Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination? destination,
        DateTimeOffset? heldUntil,
        PreparedLocalCopy? preparedCopy,
        CancellationToken cancellationToken) =>
        this.SubmitAsync(
            session,
            new MailboxChange(
                request ?? throw new ArgumentNullException(nameof(request)),
                destination,
                heldUntil,
                PersonsMove: false,
                CrossesDraftsOrSentTreeOnTheServer: false,
                preparedCopy),
            cancellationToken);

    /// <summary>Places the payload of every copy a batch asks for, before the transaction that commits them opens.</summary>
    /// <param name="account">The account the batch acts on.</param>
    /// <param name="sources">The message each planned copy duplicates, one entry per copy, which may name one message twice.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The placed copies, or none at all where the account's source is the truth about its mailbox.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The custodian is chosen here as well as inside the transaction, and the two choices do different work: this one
    /// decides whether anything is worth placing, and the one inside decides what is committed.
    /// </remarks>
    public async Task<PreparedLocalCopies> PrepareCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var custodian = await this.custodians.ResolveAsync(account, cancellationToken);

        return await custodian.PrepareCopiesAsync(account, sources, cancellationToken);
    }

    /// <summary>Records or commits a move a person asked for, answering one into the folder the message is in as already there.</summary>
    /// <param name="session">The transaction the move commits in.</param>
    /// <param name="request">The move asked for.</param>
    /// <param name="destination">Where the move files the message, resolved before the transaction opened.</param>
    /// <param name="crossesDraftsOrSentTreeOnTheServer">Whether the move would carry the message across the edge of the drafts or the sent tree, judged by the folder it was stored under on the mail server.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The record written, the local move committed, or why neither happened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A rule's move into the folder a mirrored message is in is still recorded, as it always was, which is why the
    /// refusal is asked for rather than applied to every requester. Both refusals — a move into the folder the message is
    /// in, and one across the edge of the drafts or the sent tree — are the custodian's to answer rather than the
    /// caller's, because the folder a message is in is a different thing on each side of custody.
    /// </remarks>
    public Task<SubmittedMailboxChange> SubmitMoveAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        MailboxDestination destination,
        bool crossesDraftsOrSentTreeOnTheServer,
        CancellationToken cancellationToken) =>
        this.SubmitAsync(
            session,
            new MailboxChange(
                request ?? throw new ArgumentNullException(nameof(request)),
                destination,
                HeldUntil: null,
                PersonsMove: true,
                crossesDraftsOrSentTreeOnTheServer,
                PreparedCopy: null),
            cancellationToken);

    /// <summary>Runs a held erasure whose window has passed, removing the message and everything derived from it.</summary>
    /// <param name="session">The transaction the erasure commits in.</param>
    /// <param name="record">The erasure's record, which the cascade removes with the message.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What to announce once the transaction commits, or <see langword="null" /> where the message is already gone.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="record" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The audit entry is written from the record completed, so it names the record the person could have withdrawn and
    /// the moment they asked; it survives the cascade because the trail holds values rather than associations.
    /// </remarks>
    public async Task<AppliedMailboxChange?> EraseAsync(
        IPersistenceSession session,
        MailboxMutationRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(record);

        var account = record.Request.Account;
        var state = await this.states.ReadAsync(session, account, record.Request.StoredEmailId, cancellationToken);

        if (state is null)
        {
            return null;
        }

        // The record was read at the start of the pass, and a person may have withdrawn it since. Advancing it here is the
        // claim: a stage only moves forward, so a cancelled record refuses and the transaction ends before the cascade.
        await this.records.AdvanceAsync(session, record.Id, MailboxMutationStage.Completed, placement: null, cancellationToken);

        var now = this.timeProvider.GetUtcNow();

        if (record.IsAudited)
        {
            await this.auditEntries.AppendAsync(
                session,
                MailboxMutationAuditEntry.Of(
                    MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(now)),
                    record with { Stage = MailboxMutationStage.Completed, StageChangedAt = now },
                    state.SourceFolder,
                    now),
                cancellationToken);
        }

        // The erasing transaction also writes the source removal record naming the occurrence, so a later drain can
        // still remove the message from the source once this cascade has taken the row that said where it was.
        await this.states.EraseAsync(session, account, record.Request.StoredEmailId, cancellationToken);

        return new AppliedMailboxChange(account, state.Folder, record.Request.StoredEmailId, Flags: null);
    }

    /// <summary>Tells the clients watching an account about a local change that has committed.</summary>
    /// <param name="applied">What was committed.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="applied" /> is <see langword="null" />.</exception>
    public void Announce(AppliedMailboxChange applied)
    {
        ArgumentNullException.ThrowIfNull(applied);

        if (!this.signals.Reaches)
        {
            return;
        }

        this.signals.Publish(applied.Flags is { } flags
            ? ClientSignal.MailFlagsChanged(applied.Account, applied.Folder, [flags])
            : ClientSignal.MailChanged(applied.Account, applied.Folder, [applied.Email]));
    }

    private async Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var custodian = await this.custodians.ResolveAsync(session, change.Request.Account, cancellationToken);

        return await custodian.SubmitAsync(session, change, cancellationToken);
    }
}


/// <summary>What became of one submitted change.</summary>
public enum MailboxChangeSubmissionOutcome
{
    /// <summary>A durable record was written, which a convergence pass carries.</summary>
    Recorded = 0,

    /// <summary>The change was committed to stored state with its audit entry, and carries a record only where a restoring account owes its source one.</summary>
    Applied = 1,

    /// <summary>The held account no longer stores the message, so there was nothing to change.</summary>
    MessageMissing = 2,

    /// <summary>The destination corresponds to no local folder a message can be filed into.</summary>
    DestinationMissing = 3,

    /// <summary>The message is already in the destination, so nothing was written.</summary>
    AlreadyInDestination = 4,

    /// <summary>The held account stores no payload for the message a copy would duplicate.</summary>
    /// <remarks>
    /// A copy is the one change that needs the message itself rather than only what is recorded about it, so it is the
    /// one a storage ceiling's deferral or an unreadable payload can refuse while every other change still commits.
    /// </remarks>
    SourceContentMissing = 5,

    /// <summary>A person's move would carry the message across the edge of the drafts or the sent tree into anything but the trash, so nothing was written.</summary>
    DestinationNotAllowed = 6,
}

/// <summary>The answer one submitted change produced.</summary>
/// <param name="Outcome">What became of the change.</param>
/// <param name="Record">The record written, which a change committed locally also carries where the account owes its source one.</param>
/// <param name="Change">What to announce once the transaction commits, for <see cref="MailboxChangeSubmissionOutcome.Applied" /> alone.</param>
/// <remarks>
/// The outcome names what the requester tells its caller and the record names what convergence will carry, and the two
/// stopped being the same question once an account could be restoring: a change committed to stored state there is
/// applied exactly as a held account's is, and still leaves a record behind for the source.
/// </remarks>
public sealed record SubmittedMailboxChange(
    MailboxChangeSubmissionOutcome Outcome,
    MailboxMutationRecord? Record,
    AppliedMailboxChange? Change)
{
    /// <summary>Describes a change written down as a durable record.</summary>
    /// <param name="record">The record.</param>
    /// <returns>The answer.</returns>
    public static SubmittedMailboxChange Recorded(MailboxMutationRecord record) =>
        new(MailboxChangeSubmissionOutcome.Recorded, record ?? throw new ArgumentNullException(nameof(record)), Change: null);

    /// <summary>Describes a change committed to stored state.</summary>
    /// <param name="change">What to announce.</param>
    /// <param name="record">The record the account's source is still owed, or <see langword="null" /> where there is nothing to carry.</param>
    /// <returns>The answer.</returns>
    public static SubmittedMailboxChange Applied(AppliedMailboxChange change, MailboxMutationRecord? record = null) =>
        new(MailboxChangeSubmissionOutcome.Applied, record, change ?? throw new ArgumentNullException(nameof(change)));

    /// <summary>Describes a change nothing was written for.</summary>
    /// <param name="outcome">Why.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> is one that writes something.</exception>
    public static SubmittedMailboxChange NotSubmitted(MailboxChangeSubmissionOutcome outcome) =>
        outcome is MailboxChangeSubmissionOutcome.Recorded or MailboxChangeSubmissionOutcome.Applied
            ? throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A change that wrote something carries what it wrote.")
            : new(outcome, Record: null, Change: null);
}

/// <summary>One change committed to stored state, as the clients watching its account are told about it.</summary>
/// <param name="Account">The account the message is stored for.</param>
/// <param name="Folder">The folder a client lists the message under, or <see langword="null" /> where the change moved it between two, which a client reads as any folder of the account.</param>
/// <param name="Email">The message.</param>
/// <param name="Flags">Where a flag change left the message, or <see langword="null" /> for a change that is not about a flag.</param>
public sealed record AppliedMailboxChange(
    MailAccountId Account,
    MailFolderId? Folder,
    StoredEmailId Email,
    SignalledEmailFlags? Flags);
