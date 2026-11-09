// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;

namespace MailFathom.Application.Mail.Mutations.Local;

/// <summary>Commits a change on a message to stored state, where MailFathom's own copy of the mailbox is the truth.</summary>
/// <remarks>
/// <para>
/// There is no server to carry the change to, so it is committed together with its audit entry in the caller's
/// transaction, and no record is written — a transaction either happened or it did not, and there is no sequence for a
/// crash to interrupt. The one exception is erasure: a delete of a message already in the trash opens a record marked
/// as the local erasure it is, held for the caller's window, so the existing withdrawal and release routes keep acting
/// on it and the cascade runs only once the window has passed.
/// </para>
/// <para>
/// It serves both custodians whose local folders are the truth. What tells them apart is whether the source is owed the
/// act as well, which the caller states rather than this type reading it: a held account's source is drained and owed
/// nothing, while a restoring account's source is being filled back up and is owed every act on a message it already
/// holds.
/// </para>
/// <para>
/// A copy is the one local change that takes two phases, because it creates a second stored message with a payload of
/// its own and a payload is placed with no transaction open across the placement. The caller places one through
/// <see cref="PrepareCopiesAsync" /> before it opens its transaction and hands it back in.
/// </para>
/// </remarks>
public sealed class LocalMailboxChanges
{
    private readonly ILocalMailFolderStore localFolders;
    private readonly IMailboxMutationRecordStore records;
    private readonly ILocalEmailStateStore states;
    private readonly LocalMailCopier copier;
    private readonly IMailboxMutationAuditSettingsReader auditSettings;
    private readonly IMailboxMutationAuditEntryStore auditEntries;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the local commit from what a local change is written, audited, and copied through.</summary>
    /// <param name="localFolders">Reads and saves the local folders a change is judged against.</param>
    /// <param name="records">Opens a local erasure's record, and the record a restoring account's source is owed.</param>
    /// <param name="states">Reads and writes the stored state a local change commits to.</param>
    /// <param name="copier">Places and writes the second stored message a local copy produces.</param>
    /// <param name="auditSettings">Answers whether the account keeps an audit trail.</param>
    /// <param name="auditEntries">Appends a local change's audit entry in the change's own transaction.</param>
    /// <param name="timeProvider">Stamps a local change's audit entry and identity.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public LocalMailboxChanges(
        ILocalMailFolderStore localFolders,
        IMailboxMutationRecordStore records,
        ILocalEmailStateStore states,
        LocalMailCopier copier,
        IMailboxMutationAuditSettingsReader auditSettings,
        IMailboxMutationAuditEntryStore auditEntries,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(localFolders);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(states);
        ArgumentNullException.ThrowIfNull(copier);
        ArgumentNullException.ThrowIfNull(auditSettings);
        ArgumentNullException.ThrowIfNull(auditEntries);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.localFolders = localFolders;
        this.records = records;
        this.states = states;
        this.copier = copier;
        this.auditSettings = auditSettings;
        this.auditEntries = auditEntries;
        this.timeProvider = timeProvider;
    }

    /// <summary>Places the payload of every copy a batch asks for, before the transaction that commits them opens.</summary>
    /// <param name="account">The account the batch acts on.</param>
    /// <param name="sources">The message each planned copy duplicates, one entry per copy, which may name one message twice.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The placed copies.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sources" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An account that stops being local in between has copies placed for it that nothing commits, which is the orphaned
    /// object the content reclamation pass takes; the reverse ordering would be a copy the transaction cannot make.
    /// </remarks>
    public async Task<PreparedLocalCopies> PrepareCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> sources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return PreparedLocalCopies.None;
        }

        var placed = new Dictionary<StoredEmailId, IReadOnlyList<PreparedLocalCopy>>();

        foreach (var source in sources)
        {
            if (await this.copier.PrepareAsync(account, source, cancellationToken) is not { } copy)
            {
                continue;
            }

            placed[source] = placed.TryGetValue(source, out var already) ? [.. already, copy] : [copy];
        }

        return new PreparedLocalCopies(placed);
    }

    /// <summary>Commits one change to stored state, inside the caller's transaction.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="change">The change.</param>
    /// <param name="carriesToSource">Whether the account's source is owed the act too, wherever the message holds an occurrence there.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The local change committed, the erasure recorded, or why neither happened.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="change" /> is <see langword="null" />.</exception>
    public async Task<SubmittedMailboxChange> CommitAsync(
        IPersistenceSession session,
        MailboxChange change,
        bool carriesToSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(change);

        var request = change.Request;
        var holding = await this.localFolders.ReadAsync(session, request.Account, cancellationToken);
        var state = await this.states.ReadAsync(session, request.Account, request.StoredEmailId, cancellationToken);

        if (holding is null || state is null)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.MessageMissing);
        }

        var mutation = request.Mutation;

        if (IsFlagChange(mutation))
        {
            return await this.WriteAsync(
                session,
                request,
                state,
                FlaggedAfter(request, state),
                carriesToSource,
                isHeldFolder: true,
                cancellationToken);
        }

        // Every other change moves the message, so the hierarchy is saved with it: a save is what makes the commit
        // conditional on the hierarchy it read, and a folder erased by an edit committing meanwhile would otherwise
        // receive a message after its erasure pass ended — the reason LocalMailFolderArrivals saves on every arrival.
        var found = holding.ToTree();
        var missing = found.MissingProtectedFolders(this.MintFolderId);
        var tree = found.With(missing);

        await this.localFolders.SaveAsync(session, request.Account, missing, [], cancellationToken);

        if (mutation == MailboxMutation.Delete)
        {
            return await this.DeleteAsync(session, change, tree, state, carriesToSource, cancellationToken);
        }

        if (change.Destination is not { } destination || CorrespondingFolder(tree, destination) is not { } target)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.DestinationMissing);
        }

        if (state.Folder == target.Id)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
        }

        if (change.PersonsMove && tree.CrossesDraftsOrSentTree(state.Folder, target.Id))
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.DestinationNotAllowed);
        }

        // A copy answers the missing destination and the folder the message is already in exactly as a move does,
        // which is why it is asked after them rather than before: filing a second message into the folder it is
        // already in, or into a folder the account does not have, is the same nothing either way. The edge of the
        // drafts or the sent tree is a person's move alone, and no copy is one, so a copy never meets that refusal.
        if (mutation == MailboxMutation.Copy)
        {
            return change.PreparedCopy is not { } preparedCopy
                ? SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.SourceContentMissing)
                : await this.CopyAsync(session, request, state, target.Id, preparedCopy, cancellationToken);
        }

        return await this.WriteAsync(
            session,
            request,
            state,
            state with { Folder = target.Id },
            carriesToSource,
            isHeldFolder: true,
            cancellationToken);
    }

    /// <summary>Commits one change to stored state on a mirrored account, beside the record that carries it to the server.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="change">The change, already cleared of the refusals the custodian answers from the server's folders.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The local change committed with its record, why nothing was written, or <see langword="null" /> where the change cannot be committed locally and is to be recorded server-first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="change" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The fields the act writes overlay what synchronization observes for as long as the record has not ended, and the
    /// record starts out holding the values the act replaced, so withdrawing or giving it up returns each field to the
    /// server's. No local audit entry is written: the record is audited when it ends, as every record carried to a
    /// server is. A request already recorded commits nothing a second time.
    /// </para>
    /// <para>
    /// A replacement of the keywords opens the removal and the addition its difference from the stored keywords needs,
    /// and no record where there is none. A delete writes no field: the record hides its message from every reader
    /// while it is outstanding, and the disposition the delete carries retires it once the server has removed it. A
    /// move and a copy commit locally only
    /// into a folder the account synchronizes, and a copy only with its payload placed beforehand; anything else is
    /// answered with <see langword="null" />. A person's move into the folder the message is stored in is refused as
    /// already there, and a rule's is answered with <see langword="null" />, which records it as it always was.
    /// </para>
    /// </remarks>
    public async Task<SubmittedMailboxChange?> CommitOverlaidAsync(
        IPersistenceSession session,
        MailboxChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(change);

        var request = change.Request;
        var state = await this.states.ReadAsync(session, request.Account, request.StoredEmailId, cancellationToken);

        if (state is null)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.MessageMissing);
        }

        var mutation = request.Mutation;

        if (mutation == MailboxMutation.SetKeywords)
        {
            return await this.CommitKeywordDifferenceAsync(session, change, state, cancellationToken);
        }

        if (IsFlagChange(mutation) || mutation == MailboxMutation.Delete)
        {
            var after = mutation == MailboxMutation.Delete ? state : FlaggedAfter(request, state);

            return await this.WriteOverlaidAsync(session, change, state, after, cancellationToken);
        }

        var holding = await this.localFolders.ReadAsync(session, request.Account, cancellationToken);
        var target = change.Destination is { } destination
            ? holding?.Folders.FirstOrDefault(folder => folder.SourceFolderAlias == destination.Alias)
            : null;

        if (target is null)
        {
            return null;
        }

        // Judged by the folder the message is stored in rather than by the one its occurrence names, because a move
        // committed locally first has already put it somewhere the server does not show yet.
        if (state.Folder == target.Id)
        {
            return change.PersonsMove
                ? SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination)
                : null;
        }

        if (mutation != MailboxMutation.Copy)
        {
            return await this.WriteOverlaidAsync(session, change, state, state with { Folder = target.Id }, cancellationToken);
        }

        return change.PreparedCopy is { } preparedCopy
            ? await this.CopyOverlaidAsync(session, change, state, target.Id, preparedCopy, cancellationToken)
            : null;
    }

    /// <summary>Commits one change on a mirrored account's local-only message, which no server is owed.</summary>
    /// <param name="session">The transaction the change commits in.</param>
    /// <param name="change">The change, already cleared of the refusals the custodian answers from the server's folders.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The local change committed, the erasure recorded, or why neither happened; <see langword="null" /> where the message is not local only.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="change" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A flag or a move is written with its audit entry and no record, because a transaction either happened or it did
    /// not. A person's delete is a record of a local erasure held for their window, which the converger runs whatever
    /// the account's phase, and which leaves a tombstone instead where the account's disposition asks for one. A rule or
    /// a verdict never erases a message an archive kept.
    /// </para>
    /// <para>
    /// A copy is refused: its second message would hold no occurrence at all, and an act addressed by occurrence could
    /// never reach it again. Upgrade path: give an act a target that does not need an occurrence.
    /// </para>
    /// </remarks>
    public async Task<SubmittedMailboxChange?> CommitOnLocalOnlyAsync(
        IPersistenceSession session,
        MailboxChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(change);

        var request = change.Request;

        if (await this.states.ReadAsync(session, request.Account, request.StoredEmailId, cancellationToken) is not { IsLocalOnly: true } state)
        {
            return null;
        }

        var mutation = request.Mutation;

        if (IsFlagChange(mutation))
        {
            return await this.WriteAsync(
                session,
                request,
                state,
                FlaggedAfter(request, state),
                carriesToSource: false,
                isHeldFolder: false,
                cancellationToken);
        }

        if (mutation == MailboxMutation.Delete)
        {
            if (request.Requester.Origin != MailboxMutationOrigin.Command)
            {
                return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
            }

            return SubmittedMailboxChange.Recorded(await this.records.OpenAsync(
                session,
                request,
                change.HeldUntil,
                MailboxMutationLocalChange.Erasure,
                cancellationToken));
        }

        if (mutation == MailboxMutation.Copy)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.SourceContentMissing);
        }

        var holding = await this.localFolders.ReadAsync(session, request.Account, cancellationToken);

        if ((change.Destination is { } destination
                ? holding?.Folders.FirstOrDefault(folder => folder.SourceFolderAlias == destination.Alias)
                : null) is not { } target)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.DestinationMissing);
        }

        return state.Folder == target.Id
            ? SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination)
            : await this.WriteAsync(
                session,
                request,
                state,
                state with { Folder = target.Id },
                carriesToSource: false,
                isHeldFolder: false,
                cancellationToken);
    }

    /// <summary>Finds the local folder a destination resolved on the source corresponds to.</summary>
    /// <remarks>
    /// The correspondence is the one arrivals follow: a protected role is that role's own local folder, and any other
    /// folder is the local folder its source folder created. A folder whose source has sent nothing yet has no local
    /// folder, and one a person deleted into the trash is not a place to file mail into, so both answer as missing.
    /// </remarks>
    private static LocalMailFolder? CorrespondingFolder(LocalMailFolderTree tree, MailboxDestination destination) =>
        destination.Role is { } role && LocalMailFolderTree.ProtectedRoles.Contains(role)
            ? tree.Folders.FirstOrDefault(folder => folder.Role == role)
            : tree.Folders.FirstOrDefault(folder =>
                folder.SourceFolderAlias == destination.Alias && !tree.IsInTrash(folder.Id));

    private static bool IsFlagChange(MailboxMutation mutation) =>
        mutation == MailboxMutation.SetSeen
        || mutation == MailboxMutation.SetFlagged
        || MailboxMutationRequest.IsKeywordChange(mutation);

    /// <summary>Builds the state a flag or keyword change leaves the message in.</summary>
    private static LocalEmailState FlaggedAfter(MailboxMutationRequest request, LocalEmailState state) =>
        request.Mutation == MailboxMutation.SetSeen ? state with { IsSeen = request.DesiredSeenState!.Value }
        : request.Mutation == MailboxMutation.SetFlagged ? state with { IsFlagged = request.DesiredFlaggedState!.Value }
        : state with { Keywords = KeywordsAfter(request, state.Keywords) };

    private static MailboxMutationOverlay Replaced(LocalEmailState before) =>
        new(before.IsSeen, before.IsFlagged, before.Keywords, before.Folder, LocalCopy: null);

    private static SignalledEmailFlags? SignalledFlagsOf(MailboxMutationRequest request) =>
        request.Mutation == MailboxMutation.SetSeen || request.Mutation == MailboxMutation.SetFlagged
            ? new SignalledEmailFlags(request.StoredEmailId, request.DesiredSeenState, request.DesiredFlaggedState)
            : null;

    /// <summary>Opens the overlaying record and writes the state it overlays, unless the same request was already recorded.</summary>
    private async Task<SubmittedMailboxChange> WriteOverlaidAsync(
        IPersistenceSession session,
        MailboxChange change,
        LocalEmailState before,
        LocalEmailState after,
        CancellationToken cancellationToken)
    {
        var request = change.Request;
        var opened = await this.records.OpenOverlayAsync(
            session,
            request,
            change.HeldUntil,
            Replaced(before),
            cancellationToken);

        if (opened.IsNew && after != before)
        {
            await this.states.WriteAsync(
                session,
                request.Account,
                request.StoredEmailId,
                after,
                isHeldFolder: false,
                cancellationToken);
        }

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(
                request.Account,
                before.Folder == after.Folder ? after.Folder : null,
                request.StoredEmailId,
                SignalledFlagsOf(request)),
            opened.Record);
    }

    /// <summary>Commits a replacement of a mirrored message's keywords as the removal and the addition it differs from the stored keywords by.</summary>
    /// <remarks>
    /// <para>
    /// Each record names only what the act changed, so a keyword another client sets while the act is outstanding stays
    /// on the message, and where both change one keyword the command last to reach the server decides it. The stored
    /// keywords are the base because they are read in the transaction that commits the act, which an observation on a
    /// record is not: reconciliation overwrites that.
    /// </para>
    /// <para>
    /// A replacement that changes nothing opens nothing.
    /// </para>
    /// </remarks>
    private async Task<SubmittedMailboxChange> CommitKeywordDifferenceAsync(
        IPersistenceSession session,
        MailboxChange change,
        LocalEmailState before,
        CancellationToken cancellationToken)
    {
        var request = change.Request;
        var opened = new List<OpenedMailboxMutationRecord>(2);
        var difference = KeywordDifferenceOf(request, before.Keywords);

        foreach (var half in difference)
        {
            opened.Add(await this.records.OpenOverlayAsync(session, half, change.HeldUntil, Replaced(before), cancellationToken));
        }

        if (opened.Exists(static half => half.IsNew))
        {
            await this.states.WriteAsync(
                session,
                request.Account,
                request.StoredEmailId,
                before with { Keywords = difference.Aggregate(before.Keywords, static (carried, half) => KeywordsAfter(half, carried)) },
                isHeldFolder: false,
                cancellationToken);
        }

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(request.Account, before.Folder, request.StoredEmailId, Flags: null),
            [.. opened.Select(static half => half.Record)]);
    }

    /// <summary>Builds the removal and the addition that take a message from the keywords it carries to the ones a replacement names.</summary>
    /// <remarks>
    /// A carried keyword that is not an IMAP atom came from a server and cannot be named in a command, so it stays where
    /// it is rather than failing the replacement.
    /// </remarks>
    private static List<MailboxMutationRequest> KeywordDifferenceOf(MailboxMutationRequest request, EmailKeywords carried)
    {
        var named = request.Keywords!.Values;
        var kept = named.Select(EmailKeywords.Normalized).ToHashSet(StringComparer.Ordinal);
        var removed = AuthoredMailKeywords.Create(
            carried.Values.Where(keyword => !kept.Contains(keyword) && AuthoredMailKeywords.IsWritable(keyword)));
        var added = AuthoredMailKeywords.Create(
            named.Where(keyword => !carried.Values.Contains(EmailKeywords.Normalized(keyword), StringComparer.Ordinal)));
        var difference = new List<MailboxMutationRequest>(2);

        if (!removed.IsEmpty)
        {
            difference.Add(MailboxMutationRequest.RemoveKeywords(request.StoredEmailId, request.Occurrence, request.Requester, removed));
        }

        if (!added.IsEmpty)
        {
            difference.Add(MailboxMutationRequest.AddKeywords(request.StoredEmailId, request.Occurrence, request.Requester, added));
        }

        return difference;
    }

    /// <summary>Writes the second stored message a copy produces on a mirrored account, beside the record whose placement binds it.</summary>
    /// <remarks>
    /// The record is opened first, so a request already recorded writes no second copy; the copy's row is then named on
    /// the record, which is what the destination's forward pass binds the server's occurrence to.
    /// </remarks>
    private async Task<SubmittedMailboxChange> CopyOverlaidAsync(
        IPersistenceSession session,
        MailboxChange change,
        LocalEmailState state,
        MailFolderId target,
        PreparedLocalCopy preparedCopy,
        CancellationToken cancellationToken)
    {
        var request = change.Request;
        var opened = await this.records.OpenOverlayAsync(
            session,
            request,
            change.HeldUntil,
            Replaced(state),
            cancellationToken);

        if (!opened.IsNew)
        {
            return SubmittedMailboxChange.Applied(
                new AppliedMailboxChange(request.Account, target, opened.Record.Overlay?.LocalCopy ?? request.StoredEmailId, Flags: null),
                opened.Record);
        }

        var copied = await this.copier.CommitAsync(
            session,
            preparedCopy,
            state.SourceFolder.Id,
            target,
            new CopiedMailFlags(state.IsSeen, state.IsFlagged, state.Keywords),
            cancellationToken);

        // Nothing written means the copied message's own binding went while this ran. The record still stands, so the
        // copy reaches the server and comes back as the destination's own discovery, as a copy did before it was local.
        if (copied is not { } copy)
        {
            return SubmittedMailboxChange.Recorded(opened.Record);
        }

        var record = await this.records.RecordLocalCopyAsync(session, opened.Record.Id, copy, cancellationToken);

        return SubmittedMailboxChange.Applied(new AppliedMailboxChange(request.Account, target, copy, Flags: null), record);
    }

    /// <summary>Builds the keyword set a keyword change leaves on a message that carried the earlier one.</summary>
    private static EmailKeywords KeywordsAfter(MailboxMutationRequest request, EmailKeywords carried)
    {
        var named = request.Keywords!.Values;

        if (request.Mutation == MailboxMutation.SetKeywords)
        {
            return EmailKeywords.Create(named);
        }

        if (request.Mutation == MailboxMutation.AddKeywords)
        {
            // Past MaximumKeywords the set gives up its ordinally greatest value, which on a held account is
            // the only copy of it; refuse an add that would pass the bound once the four requesters share an outcome for it.
            return EmailKeywords.Create(carried.Values.Concat(named));
        }

        var removed = named.Select(EmailKeywords.Normalized).ToHashSet(StringComparer.Ordinal);

        return EmailKeywords.Create(carried.Values.Where(keyword => !removed.Contains(keyword)));
    }

    /// <summary>Moves a message into the trash, or records the erasure of one already there.</summary>
    private async Task<SubmittedMailboxChange> DeleteAsync(
        IPersistenceSession session,
        MailboxChange change,
        LocalMailFolderTree tree,
        LocalEmailState state,
        bool carriesToSource,
        CancellationToken cancellationToken)
    {
        var request = change.Request;

        if (state.Folder is { } current && tree.IsInTrash(current))
        {
            // Erasure is the one irreversible act, so it is only ever a person's second delete, held for their
            // window; a rule or a verdict meeting its own earlier delete again finds nothing left to do.
            if (request.Requester.Origin != MailboxMutationOrigin.Command)
            {
                return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
            }

            var record = await this.records.OpenAsync(
                session,
                request,
                change.HeldUntil,
                MailboxMutationLocalChange.Erasure,
                cancellationToken);

            return SubmittedMailboxChange.Recorded(record);
        }

        var trash = tree.Folders.First(folder => folder.Role == MailFolderSpecialUse.Trash);

        return await this.WriteAsync(
            session,
            request,
            state,
            state with { Folder = trash.Id },
            carriesToSource,
            isHeldFolder: true,
            cancellationToken);
    }

    /// <summary>Writes the second stored message a copy produces, from the payload placed before this transaction.</summary>
    /// <remarks>
    /// <para>
    /// The audit entry names the copied message and the folder it was copied into, exactly as a move's does, because the
    /// act the trail records is the one somebody asked for rather than the row it produced. What is announced is the
    /// copy: a client told about the copied message would re-read a message that has not changed.
    /// </para>
    /// <para>
    /// It is appended after the copy is written rather than before it, which is where a move's differs. A local act's
    /// entry is always a performed change, and this is the one local act that can decline to write anything while its
    /// caller commits the transaction anyway — a rule records the refusal and carries on — so auditing first would
    /// leave the trail permanently stating a copy that produced no row.
    /// </para>
    /// <para>
    /// It is the one local change a source is owed no record for. A record carries an act to a message the source
    /// already holds, and the copy is a message the source has never seen: it holds no occurrence, which is exactly what
    /// a restore appends. Asking for a record beside it would ask the source twice.
    /// </para>
    /// </remarks>
    private async Task<SubmittedMailboxChange> CopyAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState state,
        MailFolderId target,
        PreparedLocalCopy preparedCopy,
        CancellationToken cancellationToken)
    {
        var copied = await this.copier.CommitAsync(
            session,
            preparedCopy,
            state.SourceFolder.Id,
            target,
            new CopiedMailFlags(state.IsSeen, state.IsFlagged, state.Keywords),
            cancellationToken);

        // Nothing written means the copied message's own binding went while this ran, which is the message going rather
        // than the destination: answering for the destination would send a rule's author to correct a name that is right.
        if (copied is null)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.MessageMissing);
        }

        await this.AuditAsync(session, request, state, cancellationToken);

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(request.Account, target, copied.Value, Flags: null));
    }

    /// <summary>Writes the change onto the stored message, and beside it the record the source is owed where it is owed one.</summary>
    /// <remarks>
    /// <para>
    /// The record is opened in the same transaction as the commit, so a message acted on during a restore cannot end up
    /// committed locally with nothing to carry the act to the source — which is what would leave it arriving back in a
    /// mirrored mailbox in the state the source last had. It is opened against the occurrence the requester resolved,
    /// because a restore appends only a message that holds none and therefore never moves the occurrence this request
    /// names.
    /// </para>
    /// <para>
    /// It carries no hold, and that is the whole difference between it and the record a mirrored account opens for the
    /// same request. A hold buys a person the stretch in which they may still change their mind, and there is nothing
    /// here to change their mind about: the message has already moved. Holding it would only widen the stretch in which
    /// the source is wrong, and it is opened as
    /// <see cref="MailboxMutationLocalChange.AlreadyCommitted" /> so that withdrawal refuses it rather than cancelling
    /// the only thing left to tell the source with.
    /// </para>
    /// <para>
    /// A delete that leaves its server untouched opens none, since it owes the source nothing to carry.
    /// </para>
    /// </remarks>
    private async Task<SubmittedMailboxChange> WriteAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState before,
        LocalEmailState after,
        bool carriesToSource,
        bool isHeldFolder,
        CancellationToken cancellationToken)
    {
        await this.AuditAsync(session, request, before, cancellationToken);
        await this.states.WriteAsync(
            session,
            request.Account,
            request.StoredEmailId,
            after,
            isHeldFolder,
            cancellationToken);

        var carried = carriesToSource && before.HoldsSourceOccurrence && !request.LeavesServerUntouched
            ? await this.records.OpenAsync(
                session,
                request,
                heldUntil: null,
                MailboxMutationLocalChange.AlreadyCommitted,
                cancellationToken)
            : null;

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(
                request.Account,
                before.Folder == after.Folder ? after.Folder : null,
                request.StoredEmailId,
                SignalledFlagsOf(request)),
            carried);
    }

    /// <summary>Appends the audit entry one local act owes, where the account keeps a trail.</summary>
    private async Task AuditAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState before,
        CancellationToken cancellationToken)
    {
        if (!this.auditSettings.GetAuditSettings(request.Occurrence.AccountId).IsEnabled)
        {
            return;
        }

        var now = this.timeProvider.GetUtcNow();

        await this.auditEntries.AppendAsync(
            session,
            MailboxMutationAuditEntry.OfLocalAct(
                MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(now)),
                MailboxMutationRecordId.Create(Guid.CreateVersion7(now)),
                request,
                before.SourceFolder,
                now),
            cancellationToken);
    }

    private MailFolderId MintFolderId() => MailFolderId.Mint(this.timeProvider.GetUtcNow());
}
