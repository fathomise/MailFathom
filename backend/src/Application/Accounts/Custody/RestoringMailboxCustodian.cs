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

namespace MailFathom.Application.Accounts.Custody;

/// <summary>Carries every act on the mailbox of an account whose mailbox is being restored to its source.</summary>
/// <remarks>
/// <para>
/// Its local folders are still the truth while its source is being filled back up from them, so the account is in two
/// places at once and its acts are a combination no other phase has. An act on its folders is refused, and its folders
/// are described as allowing none, because an act on either side would be an act against a mailbox that is half in
/// each place; the acts come back when the restore ends.
/// </para>
/// <para>
/// A change on a message commits locally, as a held account's does, and in the same transaction opens the ordinary record
/// the converger carries under ADR 0007 wherever the message holds an occurrence on the source — never drained, or
/// already appended — so an act taken during the restore reaches the source instead of being undone by it. Where the
/// message holds none, the local commit is the whole of the act and the restore carries it when it appends the message.
/// </para>
/// <para>
/// Its filing is the mirrored account's: drafts and sent copies are appended to the source, which is not drained, by the
/// filing passes, and a destination it maps but does not synchronize is resolved against the source on demand.
/// </para>
/// </remarks>
public sealed class RestoringMailboxCustodian : IMailboxCustodian
{
    private readonly ILocalMailFolderStore folders;
    private readonly LocalMailboxChanges changes;
    private readonly OnDemandMailboxDestinationResolver onDemandDestinations;
    private readonly MailDraftFiler draftFiler;
    private readonly OutgoingMailFilingPass outgoingFilings;

    /// <summary>Initializes the custodian from the collaborators that already do each act.</summary>
    /// <param name="folders">Reads the account's own folders, which are still the truth.</param>
    /// <param name="changes">Commits a change on a message to stored state, with the record its source is owed.</param>
    /// <param name="onDemandDestinations">Resolves a destination the account maps but does not synchronize.</param>
    /// <param name="draftFiler">Appends a draft's revisions to the drafts folder and withdraws its copies.</param>
    /// <param name="outgoingFilings">Appends and withdraws the server copies of the account's sends.</param>
    /// <exception cref="ArgumentNullException">Thrown when a collaborator is <see langword="null" />.</exception>
    public RestoringMailboxCustodian(
        ILocalMailFolderStore folders,
        LocalMailboxChanges changes,
        OnDemandMailboxDestinationResolver onDemandDestinations,
        MailDraftFiler draftFiler,
        OutgoingMailFilingPass outgoingFilings)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(onDemandDestinations);
        ArgumentNullException.ThrowIfNull(draftFiler);
        ArgumentNullException.ThrowIfNull(outgoingFilings);

        this.folders = folders;
        this.changes = changes;
        this.onDemandDestinations = onDemandDestinations;
        this.draftFiler = draftFiler;
        this.outgoingFilings = outgoingFilings;
    }

    /// <inheritdoc />
    public bool PlacesMailInLocalFolders => false;

    /// <inheritdoc />
    public async Task<MailFolderManagement?> DescribeFoldersAsync(MailAccountId account, CancellationToken cancellationToken) =>
        await this.folders.ReadAsync(account, cancellationToken) is { } holding
            ? LocalMailFolderManagement.Describe(holding, allowsActs: false)
            : null;

    /// <inheritdoc />
    public Task<MailFolderActOutcome> CreateFolderAsync(
        MailAccountId account,
        MailFolderId? parent,
        string? name,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken) =>
        Refused();

    /// <inheritdoc />
    public Task<MailFolderActOutcome> RenameFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        string? name,
        CancellationToken cancellationToken) =>
        Refused();

    /// <inheritdoc />
    public Task<MailFolderActOutcome> MoveFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        MailFolderId? parent,
        CancellationToken cancellationToken) =>
        Refused();

    /// <inheritdoc />
    public Task<MailFolderActOutcome> DeleteFolderAsync(
        MailAccountId account,
        MailFolderId folder,
        CancellationToken cancellationToken) =>
        Refused();

    /// <inheritdoc />
    public Task<MailboxDestinationResolution> ResolveUnsynchronizedDestinationAsync(
        MailAccountId account,
        MailFolderMapping mapping,
        CancellationToken cancellationToken) =>
        this.onDemandDestinations.ResolveAsync(account, mapping, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Its stored mail is still what a local change commits against, so a copy there is a second stored message rather
    /// than a command for the source, and its payload is placed exactly as a held account's is.
    /// </remarks>
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
        this.changes.CommitAsync(session, change, carriesToSource: true, cancellationToken);

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
    public Task<IReadOnlyList<OutgoingMailFilingResult>> MirrorWaitingSendsAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.outgoingFilings.MirrorWaitingSendsOnSourceAsync(account, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutgoingMailFilingResult>> WithdrawDuplicatedSentCopiesAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.outgoingFilings.WithdrawDuplicatedSentCopiesOnSourceAsync(account, cancellationToken);

    private static Task<MailFolderActOutcome> Refused() =>
        Task.FromResult(MailFolderActOutcome.Refused(MailFolderActRefusal.AccountNotHeld));
}
