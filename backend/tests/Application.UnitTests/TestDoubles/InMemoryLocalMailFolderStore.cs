// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders.Local;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps one account's local folders and placements in memory, answering as the store does for any other account.</summary>
/// <remarks>
/// Folders are read back while the account holds its mailbox and while it is being restored, which is what the real store
/// loads, so a test cannot see a hierarchy the use case could not. An erased folder's source alias is kept and reported, as the real store keeps the
/// erased row, because it is what sends a later arrival from that source to the inbox. Writes naming another account
/// change nothing, as the real store's writes are scoped to the account they name.
/// </remarks>
internal sealed class InMemoryLocalMailFolderStore : ILocalMailFolderStore
{
    private readonly Dictionary<MailFolderId, LocalMailFolder> folders = [];
    private readonly HashSet<MailFolderId> erased = [];
    private readonly HashSet<MailFolderAlias> erasedSourceAliases = [];
    private readonly Dictionary<StoredEmailId, MailFolderId> placements = [];
    private readonly List<StoredEmailId> erasedEmails = [];

    internal InMemoryLocalMailFolderStore(MailAccountId account, MailAccountCustodyPhase phase)
    {
        this.Account = account;
        this.Phase = phase;
    }

    internal MailAccountId Account { get; }

    /// <summary>Gets or sets the account's custody phase, which a test moves to arrange an account drained after something was appended.</summary>
    internal MailAccountCustodyPhase Phase { get; set; }

    internal IReadOnlyCollection<LocalMailFolder> Folders => this.folders.Values;

    internal IReadOnlyCollection<MailFolderId> Erased => this.erased;

    internal IReadOnlyDictionary<StoredEmailId, MailFolderId> Placements => this.placements;

    internal IReadOnlyList<StoredEmailId> ErasedEmails => this.erasedEmails;

    internal int SaveCount { get; private set; }

    /// <summary>Gives the account a folder row outside any act, as synchronization's projection of the source would.</summary>
    internal void Add(LocalMailFolder folder) => this.folders[folder.Id] = folder;

    public Task<LocalMailFolderHolding?> ReadAsync(
        IPersistenceSession session,
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.ReadAsync(account, cancellationToken);

    public Task<LocalMailFolderHolding?> ReadAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(account == this.Account
            ? this.Phase is MailAccountCustodyPhase.Held or MailAccountCustodyPhase.Restoring
                ? new LocalMailFolderHolding(this.Phase, [.. this.folders.Values], [.. this.erasedSourceAliases])
                : new LocalMailFolderHolding(this.Phase, [], [])
            : null);

    public Task<IReadOnlyList<LocalMailFolder>> ReadFoldersAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LocalMailFolder>>(account == this.Account ? [.. this.folders.Values] : []);

    public Task SaveAsync(
        IPersistenceSession session,
        MailAccountId account,
        IReadOnlyCollection<LocalMailFolder> saved,
        IReadOnlyCollection<MailFolderId> erased,
        CancellationToken cancellationToken)
    {
        if (account != this.Account)
        {
            return Task.CompletedTask;
        }

        this.SaveCount++;

        foreach (var folder in saved)
        {
            this.folders[folder.Id] = folder;
        }

        foreach (var folder in erased)
        {
            if (this.folders.Remove(folder, out var removed) && removed.SourceFolderAlias is { } alias)
            {
                this.erasedSourceAliases.Add(alias);
            }

            this.erased.Add(folder);
        }

        return Task.CompletedTask;
    }

    public Task PlaceAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        MailFolderId folder,
        CancellationToken cancellationToken)
    {
        if (account == this.Account)
        {
            this.placements.TryAdd(email, folder);
        }

        return Task.CompletedTask;
    }

    /// <summary>Erases a placed message, answering with the folder it was placed in.</summary>
    public Task<MailFolderId?> EraseEmailAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        if (account != this.Account || !this.placements.Remove(email, out var placedIn))
        {
            return Task.FromResult<MailFolderId?>(null);
        }

        this.erasedEmails.Add(email);

        return Task.FromResult<MailFolderId?>(placedIn);
    }

    public Task<LocalMailFolderMailErasure> EraseMailOfErasedFoldersAsync(
        IPersistenceSession session,
        MailAccountId account,
        int maxEmails,
        CancellationToken cancellationToken) =>
        Task.FromResult(new LocalMailFolderMailErasure(ErasedEmailCount: 0, EmailsRemain: false));
}
