// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Runtime.CompilerServices;
using MailFathom.Application.Mail.Export;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Exports;

/// <summary>Reads what an account's stored mail would produce as an export, from the metadata rather than from any payload.</summary>
/// <remarks>
/// <para>
/// An account's folders are its folder rows, whatever custody it is in, and each one is written under its path: the
/// folder names from the top of the account down to it. The inbox is the folder playing that role. A message is written
/// under the folder it is in, so an archive of a mailbox switched into <c>Held</c> and back describes the same folders.
/// </para>
/// <para>
/// Only a message whose payload this deployment holds is counted or walked. A message recorded without content — one
/// that exceeded the size limit, or one still waiting for storage headroom — is not mail MailFathom stores, so leaving
/// it out is what makes the measurement and the archive describe the same set.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxExportReader(MailFathomDbContext dbContext) : IMailboxExportReader
{
    /// <summary>How many messages one page of the walk reads, which bounds what the walk holds at once.</summary>
    /// <remarks>The rows are metadata rather than payloads — an identity, a folder, four flags, and a length — so a page of this size is kilobytes, and the payload each of them names is fetched and let go one at a time above.</remarks>
    private const int WalkPageSize = 500;

    /// <summary>The delimiter a folder path is written with, which is MailFathom's own rather than any server's.</summary>
    private const char PathDelimiter = '/';

    /// <inheritdoc />
    public async Task<MailboxExportMeasurement> MeasureAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);

        var folders = await this.ReadFolderMapAsync(account, cancellationToken);

        var totals = await this.ExportableMessages(account)
            .GroupBy(message => message.FolderId)
            .Select(group => new
            {
                FolderId = group.Key,
                MessageCount = group.LongCount(),
                ByteCount = group.Sum(message => message.Content!.MimeByteLength),
            })
            .ToArrayAsync(cancellationToken);

        var measured = totals
            .Select(total => new
            {
                Folder = folders.Resolve(total.FolderId),
                total.MessageCount,
                total.ByteCount,
            })
            .Where(total => folderPath is null || total.Folder.Path == folderPath)
            .GroupBy(total => total.Folder.Path, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new MailboxExportFolderMeasurement(
                group.Key,
                group.Sum(total => total.MessageCount),
                group.Sum(total => total.ByteCount)));

        return new MailboxExportMeasurement([.. measured]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxExportFolder>> ReadFoldersAsync(
        MailAccountId account,
        string? folderPath,
        CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);

        var folders = await this.ReadFolderMapAsync(account, cancellationToken);

        return
        [
            .. folders.Declared
                .Where(folder => folderPath is null || folder.Path == folderPath)
                .OrderBy(folder => folder.Path, StringComparer.Ordinal),
        ];
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ExportableMessage> WalkAsync(
        MailAccountId account,
        string? folderPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        RequireNamedAccount(account);

        var folders = await this.ReadFolderMapAsync(account, cancellationToken);
        var after = Guid.Empty;

        while (true)
        {
            var page = await this.ExportableMessages(account)
                .Where(message => message.Id.CompareTo(after) > 0)
                .OrderBy(message => message.Id)
                .Take(WalkPageSize)
                .Select(message => new WalkedRow(
                    message.Id,
                    message.FolderId,
                    message.ReceivedAt,
                    message.Content!.MimeByteLength,
                    message.IsRemotelySeen,
                    message.IsRemotelyFlagged,
                    message.IsRemotelyAnswered,
                    message.IsRemotelyDraft,
                    message.RemoteKeywords,
                    message.StoredAt))
                .ToArrayAsync(cancellationToken);

            if (page.Length == 0)
            {
                yield break;
            }

            foreach (var row in page)
            {
                var folder = folders.Resolve(row.FolderId);

                if (folderPath is not null && folder.Path != folderPath)
                {
                    continue;
                }

                yield return new ExportableMessage(
                    StoredEmailId.Create(row.Id),
                    folder,
                    row.ReceivedAt ?? row.StoredAt,
                    row.ByteLength,
                    FlagsOf(row),
                    row.Keywords);
            }

            after = page[^1].Id;
        }
    }

    /// <summary>Names the messages of one account that an export carries: those this deployment holds the payload of.</summary>
    private IQueryable<StoredEmailEntity> ExportableMessages(MailAccountId account) => dbContext.StoredEmails
        .AsNoTracking()
        .Where(message => message.MailboxAccountId == account.Value)
        .Where(message => message.Content != null);

    private static MaildirFlagSet FlagsOf(WalkedRow row)
    {
        var flags = MaildirFlagSet.None;

        if (row.IsSeen)
        {
            flags |= MaildirFlagSet.Seen;
        }

        if (row.IsFlagged)
        {
            flags |= MaildirFlagSet.Flagged;
        }

        if (row.IsAnswered)
        {
            flags |= MaildirFlagSet.Answered;
        }

        if (row.IsDraft)
        {
            flags |= MaildirFlagSet.Draft;
        }

        return flags;
    }

    private async Task<ExportFolderMap> ReadFolderMapAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var rows = await dbContext.MailFolders
            .AsNoTracking()
            .Where(folder => folder.MailboxAccountId == account.Value && folder.ErasedAt == null)
            .Select(folder => new FolderRow(folder.Id, folder.ParentId, folder.Name, folder.Role))
            .ToArrayAsync(cancellationToken);

        return ExportFolderMap.Compose(rows);
    }

    private static void RequireNamedAccount(MailAccountId account)
    {
        if (string.IsNullOrEmpty(account.Value))
        {
            throw new ArgumentException(
                "An export is measured and walked for a named account, so an account naming nothing cannot be read.",
                nameof(account));
        }
    }

    private sealed record WalkedRow(
        Guid Id,
        Guid FolderId,
        DateTimeOffset? ReceivedAt,
        long ByteLength,
        bool IsSeen,
        bool IsFlagged,
        bool IsAnswered,
        bool IsDraft,
        IReadOnlyList<string> Keywords,
        DateTimeOffset StoredAt);

    private sealed record FolderRow(Guid Id, Guid? ParentId, string Name, MailFolderSpecialUse? Role);

    /// <summary>Turns the folder a message is in into the folder the archive writes it under.</summary>
    private sealed class ExportFolderMap
    {
        private readonly Dictionary<Guid, MailboxExportFolder> folders;

        private ExportFolderMap(Dictionary<Guid, MailboxExportFolder> folders) => this.folders = folders;

        /// <summary>Gets the folders the archive carries a Maildir for, whether or not they hold anything.</summary>
        public IReadOnlyList<MailboxExportFolder> Declared => [.. this.folders.Values];

        public static ExportFolderMap Compose(IReadOnlyList<FolderRow> rows)
        {
            var paths = StoredMailFolderPaths.Compose([.. rows.Select(static row => new StoredMailFolderNode(row.Id, row.ParentId, row.Name))]);

            return new ExportFolderMap(rows.ToDictionary(
                static row => row.Id,
                row => new MailboxExportFolder(
                    string.Join(PathDelimiter, paths[row.Id]),
                    paths[row.Id],
                    row.Role == MailFolderSpecialUse.Inbox)));
        }

        /// <summary>Names the folder one message is written under.</summary>
        public MailboxExportFolder Resolve(Guid folderId) =>
            this.folders.TryGetValue(folderId, out var folder) ? folder : UnknownFolder;

        /// <summary>The folder a message whose own folder has gone is written under, so a walk never drops a message.</summary>
        /// <remarks>
        /// Reachable only where a folder was erased between the folder read and the walk. The name is MailFathom's own
        /// and carries nothing a person or a server chose, which is what keeps it safe to write as a path.
        /// </remarks>
        private static MailboxExportFolder UnknownFolder { get; } = new("unfiled", ["unfiled"], IsInbox: false);
    }
}
