// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Folders.Local;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
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
/// A change on a message is written down as the durable record the account's convergence pass carries under ADR 0007,
/// in the caller's transaction, and a copy there is a command for the server rather than a second stored message.
/// Drafts and sent copies are appended to the server by the filing passes rather than filed locally.
/// </para>
/// </remarks>
public sealed class MirroredMailboxCustodian : IMailboxCustodian
{
    private readonly MirroredMailFolderEditor folderEditor;
    private readonly ILocalMailFolderStore folders;
    private readonly IMailboxMutationRecordStore records;
    private readonly OnDemandMailboxDestinationResolver onDemandDestinations;
    private readonly MailDraftFiler draftFiler;
    private readonly OutgoingMailFilingPass outgoingFilings;

    /// <summary>Initializes the custodian from the collaborators that already do each act against a source server.</summary>
    /// <param name="folderEditor">Acts on the account's folders on its mail server.</param>
    /// <param name="folders">Reads the folder rows synchronization projects the source hierarchy into.</param>
    /// <param name="records">Opens the durable record a change on a message is carried by.</param>
    /// <param name="onDemandDestinations">Resolves a destination the account maps but does not synchronize.</param>
    /// <param name="draftFiler">Appends a draft's revisions to the drafts folder and withdraws its copies.</param>
    /// <param name="outgoingFilings">Appends and withdraws the server copies of the account's sends.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public MirroredMailboxCustodian(
        MirroredMailFolderEditor folderEditor,
        ILocalMailFolderStore folders,
        IMailboxMutationRecordStore records,
        OnDemandMailboxDestinationResolver onDemandDestinations,
        MailDraftFiler draftFiler,
        OutgoingMailFilingPass outgoingFilings)
    {
        ArgumentNullException.ThrowIfNull(folderEditor);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(onDemandDestinations);
        ArgumentNullException.ThrowIfNull(draftFiler);
        ArgumentNullException.ThrowIfNull(outgoingFilings);

        this.folderEditor = folderEditor;
        this.folders = folders;
        this.records = records;
        this.onDemandDestinations = onDemandDestinations;
        this.draftFiler = draftFiler;
        this.outgoingFilings = outgoingFilings;
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
    public Task<PreparedLocalCopies> PrepareCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> sources,
        CancellationToken cancellationToken) =>
        Task.FromResult(PreparedLocalCopies.None);

    /// <inheritdoc />
    /// <remarks>
    /// A person's move into the folder the message is in, and one across the edge of the drafts or the sent tree, are
    /// refused here, judged by the folder the message was stored under on the mail server; a rule's move into the folder
    /// a message is in is still recorded, as it always was.
    /// </remarks>
    public async Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var request = change.Request;

        if (change.PersonsMove
            && request.Mutation == MailboxMutation.Relocate
            && change.Destination?.Alias == request.Occurrence.FolderResolutionId.Alias)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.AlreadyInDestination);
        }

        if (change.PersonsMove && change.CrossesDraftsOrSentTreeOnTheServer)
        {
            return SubmittedMailboxChange.NotSubmitted(MailboxChangeSubmissionOutcome.DestinationNotAllowed);
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
    public Task<LocalMailCopy?> PrepareDraftCopyAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) =>
        Task.FromResult<LocalMailCopy?>(null);

    /// <inheritdoc />
    public Task<MailDraftFilingResult> SettleDraftAsync(MailDraftRecord draft, CancellationToken cancellationToken) =>
        this.draftFiler.SettleOnSourceAsync(draft, cancellationToken);

    /// <inheritdoc />
    public Task<LocalMailCopy?> PrepareSentCopyAsync(OutgoingEmailRecord send, CancellationToken cancellationToken) =>
        Task.FromResult<LocalMailCopy?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutgoingMailFilingResult>> SettleFiledCopiesAsync(
        OutgoingEmailRecord send,
        CancellationToken cancellationToken) =>
        this.outgoingFilings.SettleFiledCopiesOnSourceAsync(send, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutgoingMailFilingResult>> WithdrawDuplicatedSentCopiesAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.outgoingFilings.WithdrawDuplicatedSentCopiesOnSourceAsync(account, cancellationToken);

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
