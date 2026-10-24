// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers the narrowing every read applies to the folders a configuration decision admitted or took out of it.</summary>
/// <remarks>
/// The predicate is composed here and evaluated by PostgreSQL, so what these tests establish is which rows it selects
/// rather than what SQL it becomes. That distinction is the whole point of the account-and-alias pair: a predicate
/// written over the alias alone would translate perfectly and withhold another account's mail.
/// </remarks>
public sealed class AccountScopedMailFoldersTests
{
    private static readonly MailFolderIdentity WorkPrivate = new(
        MailAccountId.Create("work"),
        MailFolderAlias.Create("PRIVATE"));

    /// <summary>A folder decision belongs to one account, so the same alias elsewhere is somebody else's folder.</summary>
    [Fact]
    public void Excluding_AFolderOfOneAccount_LeavesTheSameAliasOfAnotherAccountReadable()
    {
        // Arrange
        var emails = Emails(("work", "PRIVATE"), ("work", "INBOX"), ("home", "PRIVATE"));

        // Act
        var readable = AccountScopedMailFolders.Excluding(emails, [WorkPrivate]);

        // Assert
        Assert.Equal(
            [("work", "INBOX"), ("home", "PRIVATE")],
            readable.AsEnumerable().Select(email => (email.MailboxAccountId, email.MailFolderBinding.Alias)));
    }

    /// <summary>A deployment that withholds nothing pays for no clause at all, which is what keeps the switch free.</summary>
    [Fact]
    public void Excluding_NothingExcluded_LeavesTheQueryAsItWas()
    {
        // Arrange
        var emails = Emails(("work", "PRIVATE"), ("home", "INBOX"));

        // Act
        var readable = AccountScopedMailFolders.Excluding(emails, []);

        // Assert
        Assert.Same(emails, readable);
    }

    /// <summary>Several folders of one account are one clause, so none of them may survive it.</summary>
    [Fact]
    public void Excluding_SeveralFoldersOfOneAccount_LeavesNoneOfThemReadable()
    {
        // Arrange
        var emails = Emails(("work", "PRIVATE"), ("work", "SPAM"), ("work", "INBOX"));
        var excluded = new[]
        {
            WorkPrivate,
            new MailFolderIdentity(MailAccountId.Create("work"), MailFolderAlias.Create("SPAM")),
        };

        // Act
        var readable = AccountScopedMailFolders.Excluding(emails, excluded);

        // Assert
        Assert.Equal(["INBOX"], readable.Select(email => email.MailFolderBinding.Alias));
    }

    /// <summary>The freshness a tool reports is read from the bindings, so a withheld folder may not be named there either.</summary>
    [Fact]
    public void Excluding_FolderBindings_LeavesOutTheExcludedBindingOnly()
    {
        // Arrange
        var folders = new[]
        {
            Folder("work", "PRIVATE"),
            Folder("work", "INBOX"),
            Folder("home", "PRIVATE"),
        }.AsQueryable();

        // Act
        var readable = AccountScopedMailFolders.Excluding(folders, [WorkPrivate]);

        // Assert
        Assert.Equal(
            [("work", "INBOX"), ("home", "PRIVATE")],
            readable.AsEnumerable().Select(folder => (folder.MailboxAccountId, folder.Alias)));
    }

    /// <summary>A role selects the folder each account plays it with, so the same name elsewhere is not selected by it.</summary>
    [Fact]
    public void Selecting_ARole_KeepsTheFolderEachAccountPlaysItWith()
    {
        // Arrange
        var emails = EmailsIn(
            FolderRow("work", "SPAM", MailFolderSpecialUse.Junk),
            FolderRow("home", "SPAM", role: null),
            FolderRow("home", "JUNK", MailFolderSpecialUse.Junk));

        // Act
        var selectedEmails = AccountScopedMailFolders.Selecting(emails, [], [MailFolderSpecialUse.Junk]);

        // Assert
        Assert.Equal(
            [("work", "SPAM"), ("home", "JUNK")],
            selectedEmails.AsEnumerable().Select(email => (email.MailboxAccountId, email.Folder.Alias)));
    }

    /// <summary>A folder identity names one account's folder, so it keeps that folder and no other account's.</summary>
    [Fact]
    public void Selecting_AFolderIdentity_KeepsThatFolderAlone()
    {
        // Arrange
        var emails = Emails(("work", "ARCHIVE"), ("home", "ARCHIVE"), ("home", "INBOX"));

        // Act
        var selectedEmails = AccountScopedMailFolders.Selecting(emails, [IdOf("work", "ARCHIVE")], []);

        // Assert
        Assert.Equal(
            [("work", "ARCHIVE")],
            selectedEmails.AsEnumerable().Select(email => (email.MailboxAccountId, email.Folder.Alias)));
    }

    /// <summary>A folder no alias feeds is selected by its identity like any other, which is how a folder somebody made is read.</summary>
    [Fact]
    public void Selecting_AFolderNoAliasFeeds_IsKeptByItsIdentity()
    {
        // Arrange
        var made = FolderRow("work", alias: null, role: null);
        var emails = EmailsIn(made, FolderRow("work", "INBOX", MailFolderSpecialUse.Inbox));

        // Act
        var selectedEmails = AccountScopedMailFolders.Selecting(emails, [MailFolderId.Create(made.Id)], []);

        // Assert
        Assert.Equal([made.Id], selectedEmails.AsEnumerable().Select(email => email.FolderId));
    }

    /// <summary>A request naming no folder reads every folder of its accounts, and pays for no clause doing it.</summary>
    [Fact]
    public void Selecting_NoFolderSelected_LeavesTheQueryAsItWas()
    {
        // Arrange
        var emails = Emails(("work", "ARCHIVE"), ("home", "INBOX"));

        // Act
        var selectedEmails = AccountScopedMailFolders.Selecting(emails, [], []);

        // Assert
        Assert.Same(emails, selectedEmails);
    }

    /// <summary>The freshness a tool reports is read from the bindings, so a selection narrows those to the same folders.</summary>
    [Fact]
    public void Selecting_FolderBindings_KeepsTheSelectedFoldersOnly()
    {
        // Arrange
        var folders = new[]
        {
            Folder("work", "SPAM", MailFolderSpecialUse.Junk),
            Folder("home", "SPAM"),
            Folder("home", "ARCHIVE"),
        }.AsQueryable();

        // Act
        var selectedFolders = AccountScopedMailFolders.Selecting(
            folders,
            [IdOf("home", "ARCHIVE")],
            [MailFolderSpecialUse.Junk]);

        // Assert
        Assert.Equal(
            [("work", "SPAM"), ("home", "ARCHIVE")],
            selectedFolders.AsEnumerable().Select(folder => (folder.MailboxAccountId, folder.Alias)));
    }

    /// <summary>A folder no alias feeds is no folder configuration withheld, so withholding one never hides it.</summary>
    [Fact]
    public void Excluding_AFolderNoAliasFeeds_StaysReadable()
    {
        // Arrange
        var emails = EmailsIn(FolderRow("work", alias: null, role: null), FolderRow("work", "PRIVATE", role: null));

        // Act
        var readable = AccountScopedMailFolders.Excluding(emails, [WorkPrivate]);

        // Assert
        Assert.Equal([null], readable.AsEnumerable().Select(email => email.Folder.Alias));
    }

    /// <summary>A folder no alias feeds is one MailFathom keeps itself, so an admission of the configured folders still reads it.</summary>
    [Fact]
    public void Admitting_AFolderNoAliasFeeds_IsReadable()
    {
        // Arrange
        var emails = EmailsIn(FolderRow("work", alias: null, role: null), FolderRow("work", "REMOVED", role: null));

        // Act
        var readable = AccountScopedMailFolders.Admitting(emails, [WorkPrivate]);

        // Assert
        Assert.Equal([null], readable.AsEnumerable().Select(email => email.Folder.Alias));
    }

    /// <summary>An admission is the folders configuration maps, so a folder it stopped naming is no longer read.</summary>
    [Fact]
    public void Admitting_AFolderConfigurationNoLongerMaps_LeavesItsStoredMailUnreadable()
    {
        // Arrange
        var emails = Emails(("work", "INBOX"), ("work", "REMOVED"), ("home", "INBOX"));
        var admitted = new[]
        {
            new MailFolderIdentity(MailAccountId.Create("work"), MailFolderAlias.Create("INBOX")),
            new MailFolderIdentity(MailAccountId.Create("home"), MailFolderAlias.Create("INBOX")),
        };

        // Act
        var readable = AccountScopedMailFolders.Admitting(emails, admitted);

        // Assert
        Assert.Equal(
            [("work", "INBOX"), ("home", "INBOX")],
            readable.AsEnumerable().Select(email => (email.MailboxAccountId, email.MailFolderBinding.Alias)));
    }

    /// <summary>The empty admission is the one that separates this from a caller's filter: it admits nothing.</summary>
    [Fact]
    public void Admitting_NoFolderAdmitted_LeavesNothingReadable()
    {
        // Arrange
        var emails = Emails(("work", "INBOX"), ("home", "INBOX"));

        // Act
        var readable = AccountScopedMailFolders.Admitting(emails, []);

        // Assert
        Assert.Empty(readable);
    }

    /// <summary>An admission belongs to one account, so the same alias elsewhere is admitted by that account or not at all.</summary>
    [Fact]
    public void Admitting_AFolderOfOneAccount_LeavesOutTheSameAliasOfAnotherAccount()
    {
        // Arrange
        var emails = Emails(("work", "PRIVATE"), ("home", "PRIVATE"));

        // Act
        var readable = AccountScopedMailFolders.Admitting(emails, [WorkPrivate]);

        // Assert
        Assert.Equal(["work"], readable.AsEnumerable().Select(email => email.MailboxAccountId));
    }

    /// <summary>The freshness a tool reports is read from the bindings, so an unmapped folder may not be named there either.</summary>
    [Fact]
    public void Admitting_FolderBindings_KeepsTheAdmittedPairsOnly()
    {
        // Arrange
        var folders = new[]
        {
            Folder("work", "PRIVATE"),
            Folder("work", "REMOVED"),
            Folder("home", "PRIVATE"),
        }.AsQueryable();

        // Act
        var readable = AccountScopedMailFolders.Admitting(folders, [WorkPrivate]);

        // Assert
        Assert.Equal(
            [("work", "PRIVATE")],
            readable.AsEnumerable().Select(folder => (folder.MailboxAccountId, folder.Alias)));
    }

    /// <summary>A read that has already found one email asks about that pair, and an alias alone would answer for two accounts.</summary>
    [Theory]
    [InlineData("work", "PRIVATE", true)]
    [InlineData("home", "PRIVATE", false)]
    [InlineData("work", "INBOX", false)]
    public void Contains_APair_AnswersForThatAccountAndAliasTogether(string accountId, string alias, bool excluded)
    {
        // Arrange, Act
        var isExcluded = AccountScopedMailFolders.Contains([WorkPrivate], accountId, alias);

        // Assert
        Assert.Equal(excluded, isExcluded);
    }

    private static IQueryable<StoredEmailEntity> Emails(params (string AccountId, string Alias)[] folders) =>
        EmailsIn([.. folders.Select(static folder => FolderRow(folder.AccountId, folder.Alias, role: null))]);

    private static IQueryable<StoredEmailEntity> EmailsIn(params MailFolderEntity[] folders) => folders
        .Select(folder => new StoredEmailEntity
        {
            MailboxAccountId = folder.MailboxAccountId,
            MailFolderBinding = folder.Alias is { } alias ? Folder(folder.MailboxAccountId, alias) : null!,
            FolderId = folder.Id,
            Folder = folder,
        })
        .AsQueryable();

    private static MailFolderEntity FolderRow(string accountId, string? alias, MailFolderSpecialUse? role) => new()
    {
        Id = alias is null ? Guid.Parse("0199c1a2-0000-7000-8000-0000000000aa") : IdOf(accountId, alias).Value,
        MailboxAccountId = accountId,
        Alias = alias,
        Name = alias ?? "Made",
        NameKey = (alias ?? "Made").ToUpperInvariant(),
        Role = role,
    };

    private static MailFolderBindingEntity Folder(string accountId, string alias, MailFolderSpecialUse? role = null) => new()
    {
        MailboxAccountId = accountId,
        Alias = alias,
        RemotePath = alias,
        MailboxAccount = new MailboxAccountEntity { Id = accountId },
        FolderId = IdOf(accountId, alias).Value,
        Folder = FolderRow(accountId, alias, role),
    };

    private static MailFolderId IdOf(string accountId, string alias) =>
        MailFolderId.Create(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId}/{alias}")).AsSpan(0, 16)));
}
