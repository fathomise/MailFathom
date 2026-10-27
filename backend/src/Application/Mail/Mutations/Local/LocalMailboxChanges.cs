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

        if (mutation == MailboxMutation.SetSeen || mutation == MailboxMutation.SetFlagged
            || mutation == MailboxMutation.AddKeywords || mutation == MailboxMutation.RemoveKeywords
            || mutation == MailboxMutation.SetKeywords)
        {
            var flagged = mutation == MailboxMutation.SetSeen ? state with { IsSeen = request.DesiredSeenState!.Value }
                : mutation == MailboxMutation.SetFlagged ? state with { IsFlagged = request.DesiredFlaggedState!.Value }
                : state with { Keywords = KeywordsAfter(request, state.Keywords) };

            return await this.WriteAsync(session, request, state, flagged, carriesToSource, cancellationToken);
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

    /// <summary>Builds the keyword set a keyword change leaves on a message that carried the earlier one.</summary>
    private static RemoteEmailKeywords KeywordsAfter(MailboxMutationRequest request, RemoteEmailKeywords carried)
    {
        var named = request.Keywords!.Values;

        if (request.Mutation == MailboxMutation.SetKeywords)
        {
            return RemoteEmailKeywords.Create(named);
        }

        if (request.Mutation == MailboxMutation.AddKeywords)
        {
            // Past MaximumKeywords the set gives up its ordinally greatest value, which on a held account is
            // the only copy of it; refuse an add that would pass the bound once the four requesters share an outcome for it.
            return RemoteEmailKeywords.Create(carried.Values.Concat(named));
        }

        var removed = named.Select(RemoteEmailKeywords.Normalized).ToHashSet(StringComparer.Ordinal);

        return RemoteEmailKeywords.Create(carried.Values.Where(keyword => !removed.Contains(keyword)));
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
    /// </remarks>
    private async Task<SubmittedMailboxChange> WriteAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        LocalEmailState before,
        LocalEmailState after,
        bool carriesToSource,
        CancellationToken cancellationToken)
    {
        await this.AuditAsync(session, request, before, cancellationToken);
        await this.states.WriteAsync(session, request.Account, request.StoredEmailId, after, cancellationToken);

        var carried = carriesToSource && before.HoldsSourceOccurrence
            ? await this.records.OpenAsync(
                session,
                request,
                heldUntil: null,
                MailboxMutationLocalChange.AlreadyCommitted,
                cancellationToken)
            : null;

        var flags = request.Mutation == MailboxMutation.SetSeen || request.Mutation == MailboxMutation.SetFlagged
            ? new SignalledEmailFlags(request.StoredEmailId, request.DesiredSeenState, request.DesiredFlaggedState)
            : (SignalledEmailFlags?)null;

        return SubmittedMailboxChange.Applied(
            new AppliedMailboxChange(
                request.Account,
                before.Folder == after.Folder ? after.Folder : null,
                request.StoredEmailId,
                flags),
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
