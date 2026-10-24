// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Synchronization;

/// <summary>
/// Covers what the folder tree's two queries ask PostgreSQL for, which the C# they are written in does not show. Both
/// shapes return plausible rows when they translate to something else — a correlated maximum evaluated per row in this
/// process still answers, and an aggregate that lost its filter still counts — so the generated command is what is read
/// rather than the result.
/// </summary>
public sealed class StoredMailFolderReaderCommandTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>The whole read has to reach the database, which is what a query that fell back to this process would not do.</summary>
    [Fact]
    public void FoldersIn_AScopeNamingOneAccount_TranslatesToOneCommandOverTheFolderRows()
    {
        // Act
        var command = CommandFor(static (context, scope) => StoredMailFolderReader.FoldersIn(context.MailFolders, scope));

        // Assert
        Assert.Contains("local_mail_folders", command, StringComparison.Ordinal);
        Assert.Contains(nameof(MailFolderEntity.ParentId), command, StringComparison.Ordinal);
    }

    /// <summary>The scope is what keeps a caller out of another user's folders, so it has to be in the command rather than applied afterwards.</summary>
    [Fact]
    public void FoldersIn_AScopeNamingOneAccount_NarrowsToItsLiveFoldersInTheCommand()
    {
        // Act
        var command = CommandFor(static (context, scope) => StoredMailFolderReader.FoldersIn(context.MailFolders, scope));

        // Assert
        Assert.Contains(nameof(MailFolderEntity.MailboxAccountId), NarrowingIn(command), StringComparison.Ordinal);
        Assert.Contains(nameof(MailFolderEntity.ErasedAt), NarrowingIn(command), StringComparison.Ordinal);
    }

    /// <summary>The counts are one grouped aggregate over the mail, which is the whole reason this is a query and not a walk.</summary>
    [Fact]
    public void FolderCountsIn_Always_GroupsTheMailByFolderInPostgreSql()
    {
        // Act
        var command = CommandFor(StoredMailFolderReader.FolderCountsIn);

        // Assert
        Assert.Contains("GROUP BY", command, StringComparison.Ordinal);
        Assert.Contains("count(*)", command, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The unread figure is the second aggregate over the same group. A translation that lost the filter would report
    /// every stored email as unread, which reads as a plausible badge on a folder nobody has opened.
    /// </summary>
    [Fact]
    public void FolderCountsIn_Always_CountsTheUnreadMailByFilteringTheSameGroup()
    {
        // Act
        var command = CommandFor(StoredMailFolderReader.FolderCountsIn);

        // Assert
        Assert.Contains(nameof(StoredEmailEntity.IsRemotelySeen), ProjectionIn(command), StringComparison.Ordinal);
    }

    /// <summary>
    /// The counts admit exactly what a listing of the folder would return, and the tombstone rule is the part of that a
    /// count is easiest to write without. A figure including tombstoned mail is one no listing could reproduce.
    /// </summary>
    [Fact]
    public void FolderCountsIn_Always_LeavesTombstonedMailOutOfTheCount()
    {
        // Act
        var command = CommandFor(StoredMailFolderReader.FolderCountsIn);

        // Assert
        Assert.Contains(
            nameof(StoredEmailEntity.RemoteExpungeObservedAt),
            NarrowingIn(command),
            StringComparison.Ordinal);
        Assert.Contains(
            nameof(StoredEmailEntity.IsRetainedAfterAuthoredDelete),
            NarrowingIn(command),
            StringComparison.Ordinal);
    }

    /// <summary>Generates one of the two commands, without opening a connection.</summary>
    private static string CommandFor<TRow>(
        Func<MailFathomDbContext, MailboxScope, IQueryable<TRow>> compose)
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return compose(context, ScopeOverOneAccount()).ToQueryString();
    }

    /// <summary>Builds the scope every caller-facing read resolves to, over one account and its one admitted folder.</summary>
    private static MailboxScope ScopeOverOneAccount()
    {
        var inbox = MailFolderId.Create(Guid.Parse("caadbcff-ec90-7811-8a56-42fc15e5aa67"));

        return MailboxScope.Create([Work], [inbox]);
    }

    /// <summary>Returns the select list, which is where an aggregate sits and where a narrowing does not.</summary>
    private static string ProjectionIn(string command)
    {
        var fromIndex = command.IndexOf("FROM", StringComparison.Ordinal);

        return fromIndex < 0 ? string.Empty : command[..fromIndex];
    }

    private static string NarrowingIn(string command)
    {
        var whereIndex = command.IndexOf("WHERE", StringComparison.Ordinal);

        return whereIndex < 0 ? string.Empty : command[whereIndex..];
    }
}
