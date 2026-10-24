// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>Reads each folder of a scope — its place in its account's hierarchy and its role — and how much of it this deployment holds.</summary>
/// <remarks>
/// <para>
/// Three queries rather than one, because the halves are bounded by different things and a join would multiply them.
/// The folders are one row each, the paths are read from every live folder of the scope's accounts — a folder the scope
/// withholds still names the folders beneath it — and the counts are an aggregate over the mail the folders hold, so
/// PostgreSQL performs the reduction and what crosses the boundary is one row per folder either way.
/// </para>
/// <para>
/// The counts admit exactly the rows a mailbox read would return, through the same predicate those reads compose. A
/// count assembled from its own idea of what is readable would be a figure no listing of the folder could reproduce —
/// tombstoned mail counted into a folder that will not show it, or another user's rows counted into this user's
/// total.
/// </para>
/// <para>
/// The folder and count queries are composed by static methods rather than inline, so what they ask PostgreSQL for can
/// be read without a database: a filtered aggregate returns plausible rows when it translates to something else, and
/// this class runs against nothing a unit test has.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredMailFolderReader(MailFathomDbContext dbContext) : IStoredMailFolderReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredMailFolder>> ReadAsync(
        MailboxScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var folders = await FoldersIn(dbContext.MailFolders.AsNoTracking(), scope).ToArrayAsync(cancellationToken);
        var counts = await FolderCountsIn(dbContext, scope).ToArrayAsync(cancellationToken);
        var paths = await StoredMailFolderPaths.ReadAsync(
            dbContext.MailFolders,
            [.. folders.Select(static folder => folder.MailboxAccountId)],
            cancellationToken);
        var countsByFolder = counts.ToDictionary(static folder => folder.FolderId);

        return [.. folders.Select(folder => Describe(folder, paths, countsByFolder))];
    }

    /// <summary>Composes the query that reads the live folders a scope admits.</summary>
    /// <param name="folders">The folder rows, untracked.</param>
    /// <param name="scope">The accounts and folders the read is restricted to.</param>
    /// <returns>The composed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// A folder this read returns nothing from is withheld from every fact about it, not only from its mail. Naming a
    /// folder is publishing that it exists, which is exactly what a caller must not learn about a folder they may not
    /// read — and what would name the junk folder to a caller who did not ask for it.
    /// </remarks>
    internal static IQueryable<MailFolderEntity> FoldersIn(IQueryable<MailFolderEntity> folders, MailboxScope scope)
    {
        string[] accountIds = [.. scope.AccountIds.Select(static accountId => accountId.Value)];

        folders = folders.Where(folder => folder.ErasedAt == null && accountIds.Contains(folder.MailboxAccountId));
        folders = AccountScopedMailFolders.Selecting(folders, scope.SelectedFolders, scope.SelectedRoles);
        folders = AccountScopedMailFolders.Admitting(folders, scope.ReadableFolders);

        return AccountScopedMailFolders.Excluding(folders, scope.WithheldJunkFolders);
    }

    /// <summary>Composes the query that counts the readable mail of each folder in scope, and how much of it is unread.</summary>
    /// <param name="dbContext">The scoped context, which the read joins no transaction of.</param>
    /// <param name="scope">The accounts and folders the read is restricted to.</param>
    /// <returns>The composed query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// The unread figure is a second aggregate over the same group rather than a second query, so both counts describe
    /// one set of rows read once.
    /// </remarks>
    internal static IQueryable<StoredMailFolderCount> FolderCountsIn(
        MailFathomDbContext dbContext,
        MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(dbContext.StoredEmails.AsNoTracking(), scope)
            .GroupBy(email => email.FolderId)
            .Select(folder => new StoredMailFolderCount(
                folder.Key,
                folder.Count(),
                folder.Count(email => !email.IsRemotelySeen)));

    /// <summary>Rebuilds one folder from its row, its path, and whatever the counts said about it.</summary>
    /// <remarks>A folder whose mail is all tombstoned, or which holds none, appears in no count row and reads as nought of both.</remarks>
    private static StoredMailFolder Describe(
        MailFolderEntity folder,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> paths,
        IReadOnlyDictionary<Guid, StoredMailFolderCount> countsByFolder)
    {
        var counted = countsByFolder.GetValueOrDefault(folder.Id);

        return new StoredMailFolder(
            MailAccountId.Create(folder.MailboxAccountId),
            MailFolderId.Create(folder.Id),
            folder.Alias is null ? null : MailFolderAlias.Create(folder.Alias),
            folder.Role,
            paths.GetValueOrDefault(folder.Id) ?? [folder.Name],
            counted?.StoredEmailCount ?? 0,
            counted?.UnreadEmailCount ?? 0);
    }
}

internal sealed record StoredMailFolderCount(
    Guid FolderId,
    int StoredEmailCount,
    int UnreadEmailCount);
