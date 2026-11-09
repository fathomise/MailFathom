// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Filing;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Carries every act on the mailbox of an account whose source server is the truth.</summary>
/// <remarks>
/// <para>
/// A folder act reaches the mail server first and writes the declaration and the folder row only once the server has
/// answered, through <see cref="MirroredMailFolderEditor" />, which acts on the folder declared under the row's alias. A
/// folder no alias feeds — one between declared folders on the server — is answered as one the account does not have,
/// and the folder the act left is reported by its identity rather than by the alias no surface accepts back.
/// </para>
/// <para>
/// A change on a message is committed to stored state in the caller's transaction, beside the durable record the
/// account's convergence pass carries to the server under ADR 0007; the fields it wrote overlay what synchronization
/// observes until that record ends. Where it cannot be committed locally first — during a rolling upgrade, or a delete,
/// a move or a copy on a server that does not name where a message lands — it is recorded alone, and a copy there is a
/// command for the server rather than a second stored message. Drafts and sent copies are appended to the server by the
/// filing passes; where the server last named where it puts an appended message, each is stored first, in the
/// transaction that saves the revision or settles the delivery, and the append that follows is bound onto it.
/// </para>
/// </remarks>
public sealed class MirroredMailboxCustodian : IMailboxCustodian
{
    private const int MaximumInspectedLeases = 512;

    private readonly MirroredMailFolderEditor folderEditor;
    private readonly ILocalMailFolderStore folders;
    private readonly IMailboxMutationRecordStore records;
    private readonly OnDemandMailboxDestinationResolver onDemandDestinations;
    private readonly LocalMailFiler localFiler;
    private readonly MailDraftFiler draftFiler;
    private readonly OutgoingMailFilingPass outgoingFilings;
    private readonly LocalMailboxChanges changes;
    private readonly IMailboxServerCapabilityStore capabilities;
    private readonly IWorkLeaseStore leases;
    private bool? everyReplicaKnowsTheOverlay;

    /// <summary>Initializes the custodian from the collaborators that already do each act against a source server.</summary>
    /// <param name="folderEditor">Acts on the account's folders on its mail server.</param>
    /// <param name="folders">Reads the folder rows synchronization projects the source hierarchy into.</param>
    /// <param name="records">Opens the durable record a change on a message is carried by.</param>
    /// <param name="onDemandDestinations">Resolves a destination the account maps but does not synchronize.</param>
    /// <param name="localFiler">Prepares a draft revision or a sent copy that is stored before it is appended.</param>
    /// <param name="draftFiler">Appends a draft's revisions to the drafts folder and withdraws its copies.</param>
    /// <param name="outgoingFilings">Appends the server copies of the account's sends and removes the provider's own.</param>
    /// <param name="changes">Commits a change on a message to stored state beside the record that carries it to the server.</param>
    /// <param name="capabilities">Reads what the account's server last advertised, which decides whether a delete, a move or a copy can be committed locally first.</param>
    /// <param name="leases">Reads every live work lease, which says whether every replica knows the overlay a local commit leaves.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MirroredMailboxCustodian(
        MirroredMailFolderEditor folderEditor,
        ILocalMailFolderStore folders,
        IMailboxMutationRecordStore records,
        OnDemandMailboxDestinationResolver onDemandDestinations,
        LocalMailFiler localFiler,
        MailDraftFiler draftFiler,
        OutgoingMailFilingPass outgoingFilings,
        LocalMailboxChanges changes,
        IMailboxServerCapabilityStore capabilities,
        IWorkLeaseStore leases)
    {
        ArgumentNullException.ThrowIfNull(folderEditor);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(onDemandDestinations);
        ArgumentNullException.ThrowIfNull(localFiler);
        ArgumentNullException.ThrowIfNull(draftFiler);
        ArgumentNullException.ThrowIfNull(outgoingFilings);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(leases);

        this.folderEditor = folderEditor;
        this.folders = folders;
        this.records = records;
        this.onDemandDestinations = onDemandDestinations;
        this.localFiler = localFiler;
        this.draftFiler = draftFiler;
        this.outgoingFilings = outgoingFilings;
        this.changes = changes;
        this.capabilities = capabilities;
        this.leases = leases;
    }

    /// <inheritdoc />
    public bool PlacesMailInLocalFolders => false;

    /// <inheritdoc />
    public async Task<MailFolderManagement?> DescribeFoldersAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (await this.folders.ReadAsync(account, cancellationToken) is not { } holding)
        {
            return null;
        }

        return Identified(
            await this.folderEditor.ReadAsync(account, cancellationToken),
            IdsByAlias(holding.Folders));
    }

    /// <inheritdoc />
    public Task<MailFolderActOutcome> CreateFolderAsync(
        MailAccountId account,
        MailFolderId? parent,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            rows => TryReadParentAlias(rows, parent, out var parentAlias)
                ? this.folderEditor.CreateAsync(account, parentAlias, name, role, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <inheritdoc />
    public Task<MailFolderActOutcome> RenameFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        string? name,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folder,
            (alias, _) => this.folderEditor.RenameAsync(account, alias, name, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<MailFolderActOutcome> MoveFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        MailFolderId? parent,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folder,
            (alias, rows) => TryReadParentAlias(rows, parent, out var parentAlias)
                ? this.folderEditor.MoveAsync(account, alias, parentAlias, cancellationToken)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.ParentMissing)),
            cancellationToken);

    /// <inheritdoc />
    public Task<MailFolderActOutcome> DeleteFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        CancellationToken cancellationToken) =>
        this.ActOnFolderAsync(
            account,
            folder,
            (alias, _) => this.folderEditor.DeleteAsync(account, alias, cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<MailboxDestinationResolution> ResolveUnsynchronizedDestinationAsync(
        MailAccountId account,
        MailFolderMapping mapping,
        CancellationToken cancellationToken) =>
        this.onDemandDestinations.ResolveAsync(account, mapping, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// A copy is placed only where it will be committed locally first: every replica knows the overlay and the server
    /// last advertised what names where a copy lands. Elsewhere a copy is a command for the server, and its second
    /// stored message is the destination's own discovery.
    /// </remarks>
    public async Task<PreparedLocalCopies> PrepareCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> sources,
        CancellationToken cancellationToken) =>
        await this.CommitsLocallyFirstAsync(account, MailboxMutation.Copy, cancellationToken)
            ? await this.changes.PrepareCopiesAsync(account, sources, cancellationToken)
            : PreparedLocalCopies.None;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A change is committed to stored state in the caller's transaction, beside the record that carries it to the
    /// server, wherever <see cref="CommitsLocallyFirstAsync" /> allows it; otherwise it is recorded and the stored state
    /// follows synchronization's observation.
    /// </para>
    /// <para>
    /// A person's move across the edge of the drafts or the sent tree is refused here, judged by the folder the message
    /// was stored under on the mail server. A person's move into the folder the message is in is refused by the local
    /// commit against the folder it is stored in, and by the folder its occurrence names where the move is recorded
    /// alone; a rule's move into the folder a message is in is still recorded, as it always was.
    /// </para>
    /// </remarks>
    public async Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var request = change.Request;

        if (change.PersonsMove && change.CrossesDraftsOrSentTreeOnTheServer)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.DestinationNotAllowed);
        }

        if (await this.changes.CommitOnLocalOnlyAsync(session, change, cancellationToken) is { } localOnly)
        {
            return localOnly;
        }

        // A delete that leaves the server untouched asks the server for nothing, so what it advertised decides nothing.
        var commitsLocallyFirst = request.LeavesServerUntouched
            ? await this.EveryReplicaKnowsTheOverlayAsync(cancellationToken)
            : await this.CommitsLocallyFirstAsync(request.Account, request.Mutation, cancellationToken);

        if (commitsLocallyFirst
            && await this.changes.CommitOverlaidAsync(session, change, cancellationToken) is { } committed)
        {
            return committed;
        }

        if (change.PersonsMove
            && request.Mutation == MailboxMutation.Relocate
            && change.Destination?.Alias == request.Occurrence.FolderResolutionId.Alias)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
        }

        var record = await this.records.OpenAsync(
            session,
            request,
            change.HeldUntil,
            MailboxMutationLocalChange.None,
            cancellationToken);

        return SubmittedMailboxChange.Recorded(record);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Carried only where both halves can be local-first: the destination's copy is bound from <c>APPENDUID</c> and the
    /// source's removal is a <c>UID EXPUNGE</c> naming the one message, so a server last seen without <c>UIDPLUS</c> is
    /// refused rather than filed server-first, and so is every account while a replica does not know the overlay.
    /// </remarks>
    public async Task<CrossAccountFilingHalf> TakesCrossAccountFilingAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        if (!await this.EveryReplicaKnowsTheOverlayAsync(cancellationToken))
        {
            return CrossAccountFilingHalf.RefusedDuringUpgrade;
        }

        return (await this.capabilities.ReadAsync(account, cancellationToken)).NamesWhereMessagesLand
            ? CrossAccountFilingHalf.Carried
            : CrossAccountFilingHalf.RefusedWithoutUidPlus;
    }

    /// <inheritdoc />
    public Task<LocalMailCopy?> PrepareDraftCopyAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) =>
        this.localFiler.PrepareAheadOfAppendAsync(account, OutgoingMailFiling.Draft, rawMime, cancellationToken);

    /// <inheritdoc />
    public Task<MailDraftFilingResult> SettleDraftAsync(MailDraftRecord draft, CancellationToken cancellationToken) =>
        this.draftFiler.SettleOnSourceAsync(draft, cancellationToken);

    /// <inheritdoc />
    public Task<LocalMailCopy?> PrepareSentCopyAsync(OutgoingEmailRecord send, CancellationToken cancellationToken) =>
        this.localFiler.PrepareStoredSentCopyAheadOfAppendAsync(send, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutgoingMailFilingResult>> SettleFiledCopiesAsync(
        OutgoingEmailRecord send,
        CancellationToken cancellationToken) =>
        this.outgoingFilings.SettleFiledCopiesOnSourceAsync(send, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutgoingMailFilingResult>> RemoveProviderSentCopiesAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.outgoingFilings.RemoveProviderSentCopiesOnSourceAsync(account, cancellationToken);

    /// <summary>Answers whether a change of one kind on the account is committed locally first.</summary>
    /// <remarks>
    /// <para>
    /// Only while every live work lease names a build that knows the overlay, because an older replica's synchronization
    /// writes every observation and would write over a field a local commit left outstanding; a rolling upgrade is
    /// therefore recorded alone until it ends, with nobody switching anything. The reading is taken once per scope, and
    /// a page of leases that fills its bound answers no, since an older build's lease may be in the part not read.
    /// </para>
    /// <para>
    /// A move and a copy create an occurrence, and a delete removes one by <c>UID EXPUNGE</c>, so they are committed
    /// locally first only where the server last advertised <c>UIDPLUS</c> or <c>IMAP4rev2</c>: that is what names where
    /// a message lands and removes exactly the one asked for. Elsewhere a delete hidden at once would only reappear when
    /// the server refused it. See
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0039-one-folder-model-and-one-implementation-per-custody-phase.md">ADR 0039</see>
    /// axis E.
    /// </para>
    /// </remarks>
    private async Task<bool> CommitsLocallyFirstAsync(
        MailAccountId account,
        MailboxMutation mutation,
        CancellationToken cancellationToken)
    {
        if (!await this.EveryReplicaKnowsTheOverlayAsync(cancellationToken))
        {
            return false;
        }

        return (mutation != MailboxMutation.Relocate && mutation != MailboxMutation.Copy && mutation != MailboxMutation.Delete)
            || (await this.capabilities.ReadAsync(account, cancellationToken)).NamesWhereMessagesLand;
    }

    private async Task<bool> EveryReplicaKnowsTheOverlayAsync(CancellationToken cancellationToken)
    {
        if (this.everyReplicaKnowsTheOverlay is { } known)
        {
            return known;
        }

        var held = await this.leases.ReadEveryHeldAsync(MaximumInspectedLeases, cancellationToken);
        var knows = held.Count < MaximumInspectedLeases && held.All(static lease => lease.Holder.KnowsMailboxOverlay);

        this.everyReplicaKnowsTheOverlay = knows;

        return knows;
    }

    // Absence of a parent is what names the top of the hierarchy, so an identity that names no declared folder must not
    // be read as it: the folder would be created or moved somewhere the request never asked for.
    private static bool TryReadParentAlias(
        IReadOnlyList<LocalMailFolder> rows,
        MailFolderId? parent,
        out MailFolderAlias? parentAlias)
    {
        parentAlias = parent is { } id ? AliasIn(rows, id) : null;

        return parentAlias is not null || parent is null;
    }

    private static MailFolderAlias? AliasIn(IReadOnlyList<LocalMailFolder> rows, MailFolderId folder) =>
        rows.FirstOrDefault(row => row.Id == folder)?.SourceFolderAlias;

    private static Dictionary<MailFolderAlias, string> IdsByAlias(IEnumerable<LocalMailFolder> rows) =>
        rows
            .Where(static row => row.SourceFolderAlias is not null)
            .GroupBy(static row => (MailFolderAlias)row.SourceFolderAlias!)
            .ToDictionary(static group => group.Key, static group => group.Last().Id.ToString());

    /// <summary>Names the mirrored editor's folders by their identities, leaving out one whose row the account's next run writes.</summary>
    private static MailFolderManagement Identified(
        MailFolderManagement management,
        Dictionary<MailFolderAlias, string> ids) =>
        management with
        {
            Folders = [.. management.Folders.Select(folder => Identified(folder, ids)).OfType<ManagedMailFolder>()],
        };

    private static ManagedMailFolder? Identified(ManagedMailFolder folder, Dictionary<MailFolderAlias, string> ids) =>
        MailFolderAlias.TryCreate(folder.Id, out var alias) && ids.TryGetValue(alias, out var id)
            ? folder with
            {
                Id = id,
                ParentId = MailFolderAlias.TryCreate(folder.ParentId, out var parent)
                    && ids.TryGetValue(parent, out var parentId)
                        ? parentId
                        : null,
            }
            : null;

    private Task<MailFolderActOutcome> ActOnFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        Func<MailFolderAlias, IReadOnlyList<LocalMailFolder>, Task<MailFolderActOutcome>> act,
        CancellationToken cancellationToken) =>
        this.ActAsync(
            account,
            rows => AliasIn(rows, folder) is { } alias
                ? act(alias, rows)
                : Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.FolderMissing)),
            cancellationToken);

    /// <summary>Carries out a folder act, and reports the folder it left by the identity a client names it by.</summary>
    /// <remarks>
    /// <para>
    /// The rows are read on both sides of the act: a created folder has its row only afterwards, and a deleted one only
    /// before. The second read ignores the caller's cancellation, because the server has already acted by then and the
    /// answer is what tells the caller so.
    /// </para>
    /// <para>
    /// A folder with no row on either side is one whose row a competing writer kept this act from declaring. It is
    /// reported as that conflict rather than by its alias, which no surface accepts back.
    /// </para>
    /// </remarks>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the act committed and no row names the folder it left.</exception>
    private async Task<MailFolderActOutcome> ActAsync(
        MailAccountId account,
        Func<IReadOnlyList<LocalMailFolder>, Task<MailFolderActOutcome>> act,
        CancellationToken cancellationToken)
    {
        if (await this.folders.ReadAsync(account, cancellationToken) is not { } holding)
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.AccountMissing);
        }

        var before = holding.Folders;
        var outcome = await act(before);

        if (outcome.Folder is not { } folder)
        {
            return outcome;
        }

        var after = await this.folders.ReadFoldersAsync(account, CancellationToken.None);
        var identified = Identified(folder, IdsByAlias([.. before, .. after]))
            ?? throw new PersistenceConcurrencyConflictException(
                $"A folder act on account {account.Value} committed, and another writer kept its folder from being given a row.");

        return outcome with { Folder = identified };
    }
}
