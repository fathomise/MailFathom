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
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Carries every act on the mailbox of an account MailFathom holds alone, without reaching any mail server.</summary>
/// <remarks>
/// <para>
/// A folder act commits in one transaction of its own through <see cref="LocalMailFolderEditor" />, which asks the
/// resolver again inside it and refuses an account that stopped being held since the act was dispatched. A change on a
/// message is committed to stored state in the caller's transaction, and the source is owed nothing: it is drained.
/// </para>
/// <para>
/// A draft revision and a sent copy are prepared here and filed into the local folder playing their role, in the
/// transaction that writes the revision or records the delivery, because an append to a drained source would be drained
/// straight back and would make filing wait on IMAP. No copy of a send is kept on a server, so the passes that keep those copies in step have
/// nothing to do here.
/// </para>
/// </remarks>
public sealed class HeldMailboxCustodian : IMailboxCustodian
{
    private readonly LocalMailFolderEditor folderEditor;
    private readonly LocalMailboxChanges changes;
    private readonly LocalMailFiler localFiler;
    private readonly MailDraftFiler draftFiler;

    /// <summary>Initializes the custodian from the collaborators that already do each act against local state.</summary>
    /// <param name="folderEditor">Reads and edits the account's own folders.</param>
    /// <param name="changes">Commits a change on a message to stored state.</param>
    /// <param name="localFiler">Prepares and files a sent copy or a draft revision locally.</param>
    /// <param name="draftFiler">Files a draft's revisions and gives it up against the local drafts folder.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public HeldMailboxCustodian(
        LocalMailFolderEditor folderEditor,
        LocalMailboxChanges changes,
        LocalMailFiler localFiler,
        MailDraftFiler draftFiler)
    {
        ArgumentNullException.ThrowIfNull(folderEditor);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(localFiler);
        ArgumentNullException.ThrowIfNull(draftFiler);

        this.folderEditor = folderEditor;
        this.changes = changes;
        this.localFiler = localFiler;
        this.draftFiler = draftFiler;
    }

    /// <inheritdoc />
    public async Task<MailFolderManagement?> DescribeFoldersAsync(MailAccountId account, CancellationToken cancellationToken) =>
        await this.folderEditor.ReadAsync(account, cancellationToken) is { } holding
            ? LocalMailFolderManagement.Describe(holding, allowsActs: true)
            : null;

    /// <inheritdoc />
    public async Task<MailFolderActOutcome> CreateFolderAsync(
        MailAccountId account,
        MailFolderId? parent,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        // Every role a held account has a folder for is supplied the moment its hierarchy is read, so a creation naming
        // one is asking for a folder that is already there rather than for something this could make.
        if (role is not null)
        {
            return MailFolderActOutcome.Refused(MailFolderActRefusal.RoleAlreadyPlayed);
        }

        return LocalMailFolderManagement.Describe(
            await this.folderEditor.CreateAsync(account, parent, name, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<MailFolderActOutcome> RenameFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        string? name,
        CancellationToken cancellationToken) =>
        LocalMailFolderManagement.Describe(await this.folderEditor.RenameAsync(account, folder, name, cancellationToken));

    /// <inheritdoc />
    public async Task<MailFolderActOutcome> MoveFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        MailFolderId? parent,
        CancellationToken cancellationToken) =>
        LocalMailFolderManagement.Describe(await this.folderEditor.MoveAsync(account, folder, parent, cancellationToken));

    /// <inheritdoc />
    public async Task<MailFolderActOutcome> DeleteFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        CancellationToken cancellationToken) =>
        LocalMailFolderManagement.Describe(await this.folderEditor.DeleteAsync(account, folder, cancellationToken));

    /// <inheritdoc />
    /// <remarks>
    /// A held account's truth is local and a folder it does not synchronize has no local folder, so there is nothing to
    /// file into — and resolving one on demand would put an IMAP listing in front of a request that must not wait on one.
    /// </remarks>
    public Task<MailboxDestinationResolution> ResolveUnsynchronizedDestinationAsync(
        MailAccountId account,
        MailFolderMapping mapping,
        CancellationToken cancellationToken) =>
        Task.FromResult(MailboxDestinationResolution.Unmapped());

    /// <inheritdoc />
    public Task<PreparedLocalCopies> PrepareCopiesAsync(
        MailAccountId account,
        IReadOnlyCollection<StoredEmailId> sources,
        CancellationToken cancellationToken) =>
        this.changes.PrepareCopiesAsync(account, sources, cancellationToken);

    /// <inheritdoc />
    public Task<SubmittedMailboxChange> SubmitAsync(
        IPersistenceSession session,
        MailboxChange change,
        CancellationToken cancellationToken) =>
        this.changes.CommitAsync(session, change, carriesToSource: false, cancellationToken);

    /// <inheritdoc />
    public Task<LocalMailCopy?> PrepareDraftCopyAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken) =>
        this.localFiler.PrepareAsync(account, OutgoingMailFiling.Draft, rawMime, cancellationToken);

    /// <inheritdoc />
    public Task<MailDraftFilingResult> SettleDraftAsync(MailDraftRecord draft, CancellationToken cancellationToken) =>
        this.draftFiler.SettleLocallyAsync(draft, cancellationToken);

    /// <inheritdoc />
    public Task<LocalMailCopy?> PrepareSentCopyAsync(OutgoingEmailRecord send, CancellationToken cancellationToken) =>
        this.localFiler.PrepareStoredSentCopyAsync(send, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Its sent copy was filed into the local sent folder in the commit that recorded the delivery, an append to a
    /// drained source would be drained straight back, and an outbox copy appended while the account was still mirrored is
    /// left for the drain to remove rather than withdrawn over IMAP.
    /// </remarks>
    public Task<IReadOnlyList<OutgoingMailFilingResult>> SettleFiledCopiesAsync(
        OutgoingEmailRecord send,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OutgoingMailFilingResult>>([]);

    /// <inheritdoc />
    /// <remarks>A held account has no outbox mirror at all: its outgoing record is its outbox.</remarks>
    public Task<IReadOnlyList<OutgoingMailFilingResult>> MirrorWaitingSendsAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OutgoingMailFilingResult>>([]);

    /// <inheritdoc />
    /// <remarks>Its source is drained, and a copy appended there while it was still mirrored is the drain's to remove.</remarks>
    public Task<IReadOnlyList<OutgoingMailFilingResult>> WithdrawDuplicatedSentCopiesAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OutgoingMailFilingResult>>([]);
}
