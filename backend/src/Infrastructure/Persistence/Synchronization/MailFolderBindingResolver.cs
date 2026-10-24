// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>Resolves the row that carries one alias binding, which every folder-scoped write is attached to.</summary>
/// <remarks>
/// A binding row is created by folder resolution alone, in its own committed transaction, before anything is
/// synchronized under it. A write path therefore requires the row rather than creating one: a missing row would mean
/// occurrences were being attached to a generation nothing recorded, which is the state the generation exists to
/// prevent.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class MailFolderBindingResolver
{
    public static async Task<MailFolderBindingEntity?> FindAsync(
        MailFathomDbContext dbContext,
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        CancellationToken cancellationToken)
    {
        var accountId = account.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;

        // Looked up by its alternate key, so the change-tracker pass is explicit rather than handled by FindAsync. The
        // user leads the alternate key exactly as it leads the index, so a binding of another user's account carrying
        // the same identifier is not a candidate at all.
        return await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            dbContext.MailFolderBindings,
            dbContext.MailFolderBindings,
            candidate => candidate.MailboxAccountId == accountId
                && candidate.Alias == alias
                && candidate.ResolutionGeneration == generation,
            cancellationToken);
    }

    public static async Task<MailFolderBindingEntity> GetRequiredAsync(
        MailFathomDbContext dbContext,
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        CancellationToken cancellationToken) =>
        await FindAsync(dbContext, account, folderResolutionId, cancellationToken)
        ?? throw new InvalidOperationException(
            $"Folder alias {account.Value}/{folderResolutionId} has no recorded binding, so nothing can be stored under it.");

    public static async Task<MailFolderBindingEntity> AddAsync(
        MailFathomDbContext dbContext,
        MailAccountId account,
        MailFolderResolution resolution,
        CancellationToken cancellationToken)
    {
        // The account is keyed by the identity itself, so FindAsync already resolves a pending insert without a query.
        // The key is the user and the identifier in that order, which is the order MailAccountId states them in.
        var accountRow = await dbContext.MailboxAccounts.FindAsync(
            [account.Value],
            cancellationToken);

        // The row is composed rather than looked up when it is not there yet, and the user comes from the identity the
        // caller resolved the account through rather than from a read of the deployment's user record. That is what
        // makes the whole mail graph beneath this folder attributable without the account table being consulted at all.
        accountRow ??= AddedAccount(dbContext, account);

        var folder = new MailFolderBindingEntity
        {
            MailboxAccountId = account.Value,

            // Taken from the identity this binding was resolved under: the folder is the first row of the mail graph
            // and every row beneath it inherits the user from here.
            Alias = resolution.Alias.Value,
            ResolutionGeneration = resolution.Generation.Value,
            RemotePath = resolution.RemotePath.Value,
            HierarchyDelimiter = resolution.RemotePath.HierarchyDelimiter?.ToString(),
            MailboxAccount = accountRow,
        };

        dbContext.MailFolderBindings.Add(folder);

        return folder;
    }

    /// <summary>Gets the folder mail stored under a binding is filed in.</summary>
    /// <param name="dbContext">The session's context.</param>
    /// <param name="binding">The binding the mail arrived under.</param>
    /// <param name="mintFolderId">Mints the identifier of the inbox when the account has none yet.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The folder the binding names, or the account's inbox when it names none.</returns>
    /// <remarks>
    /// Synchronization points a mirrored account's binding at its folder before it stores anything under it, so the
    /// first answer is the usual one. A binding written before its folder was projected is matched to the live folder
    /// carrying its alias, and one no folder carries — an alias whose folder was erased on a held account — files into
    /// the inbox, which is created when the account has none yet so no message is ever left in no folder.
    /// </remarks>
    public static async Task<Guid> FolderForArrivalAsync(
        MailFathomDbContext dbContext,
        MailFolderBindingEntity binding,
        Func<Guid> mintFolderId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(mintFolderId);

        if (binding.FolderId is { } boundFolder)
        {
            return boundFolder;
        }

        var accountId = binding.MailboxAccountId;
        var alias = binding.Alias;
        var aliasFolder = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            dbContext.MailFolders,
            dbContext.MailFolders,
            folder => folder.MailboxAccountId == accountId && folder.Alias == alias && folder.ErasedAt == null,
            cancellationToken);

        if (aliasFolder is not null)
        {
            binding.FolderId = aliasFolder.Id;

            return aliasFolder.Id;
        }

        return (await InboxOfAsync(dbContext, accountId, mintFolderId, cancellationToken)).Id;
    }

    /// <summary>Gets the account's inbox, adopting a top-level folder named for it or creating one when there is none.</summary>
    private static async Task<MailFolderEntity> InboxOfAsync(
        MailFathomDbContext dbContext,
        string accountId,
        Func<Guid> mintFolderId,
        CancellationToken cancellationToken)
    {
        var inboxKey = LocalMailFolderName.Inbox.ComparisonKey;
        var inbox = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            dbContext.MailFolders,
            dbContext.MailFolders,
            folder => folder.MailboxAccountId == accountId
                && folder.Role == MailFolderSpecialUse.Inbox
                && folder.ErasedAt == null,
            cancellationToken)
            ?? await TrackedEntityLookup.SinglePendingOrPersistedAsync(
                dbContext.MailFolders,
                dbContext.MailFolders,
                folder => folder.MailboxAccountId == accountId
                    && folder.ParentId == null
                    && folder.NameKey == inboxKey
                    && folder.ErasedAt == null,
                cancellationToken);

        if (inbox is not null)
        {
            inbox.Role ??= MailFolderSpecialUse.Inbox;

            return inbox;
        }

        var created = new MailFolderEntity
        {
            Id = mintFolderId(),
            MailboxAccountId = accountId,
            Name = LocalMailFolderName.Inbox.Value,
            NameKey = inboxKey,
            Role = MailFolderSpecialUse.Inbox,
        };

        dbContext.MailFolders.Add(created);

        return created;
    }

    /// <summary>Writes down the account row a first binding needs, under the user the caller resolved it through.</summary>
    /// <remarks>
    /// An account MailFathom has never stored mail for has no row yet, and the binding below needs one for its foreign
    /// key. The user is not minted here and not read here either: it arrived with the account, so what an operator
    /// configured decides whose mail this is rather than the order in which folders happened to bind.
    /// </remarks>
    private static MailboxAccountEntity AddedAccount(MailFathomDbContext dbContext, MailAccountId account)
    {
        var added = new MailboxAccountEntity { Id = account.Value };

        dbContext.MailboxAccounts.Add(added);

        return added;
    }

    /// <summary>Rebuilds the alias binding one row states.</summary>
    /// <param name="entity">The binding row.</param>
    /// <returns>The resolution that row describes, including the remote path a session selects.</returns>
    /// <remarks>
    /// It is the exact inverse of what <see cref="AddAsync" /> writes, and it lives beside it so the two cannot drift:
    /// the delimiter in particular is stored as text and read back as the one character it holds, and a second reading
    /// of that would be a second chance to disagree about an empty string.
    /// </remarks>
    public static MailFolderResolution ToResolution(MailFolderBindingEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        return new MailFolderResolution(
            MailFolderAlias.Create(entity.Alias),
            MailFolderResolutionGeneration.Create(entity.ResolutionGeneration),
            ToRemotePath(entity.RemotePath, entity.HierarchyDelimiter));
    }

    /// <summary>Rebuilds the remote folder a binding row names, from the two columns that hold it.</summary>
    /// <param name="remotePath">The stored path.</param>
    /// <param name="hierarchyDelimiter">The stored delimiter, which is text because PostgreSQL pads a fixed-width character column.</param>
    /// <returns>The path the columns describe.</returns>
    /// <remarks>
    /// It takes the two columns rather than the row, so a read that projects them instead of materializing the entity —
    /// which every bounded read of these rows does — reaches the same reading. The delimiter is the one character the
    /// column holds, and an empty column is a folder whose server reported none; that is the reading
    /// <see cref="AddAsync" /> writes the inverse of, and it is stated here once so nothing gets a second chance to
    /// disagree about an empty string.
    /// </remarks>
    public static RemoteFolderPath ToRemotePath(string remotePath, string? hierarchyDelimiter) =>
        RemoteFolderPath.Create(
            remotePath,
            hierarchyDelimiter is { Length: > 0 } delimiter ? delimiter[0] : null);
}
