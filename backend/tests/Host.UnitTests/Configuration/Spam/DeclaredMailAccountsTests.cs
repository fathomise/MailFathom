// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers which folders a claim inside one user's record may name, read off that user's own mailbox declarations.</summary>
public sealed class DeclaredMailAccountsTests
{
    /// <summary>A blank identifier is the record's own defect, so it is dropped rather than reported here under the wrong document.</summary>
    [Fact]
    public void ReadFrom_ADeclarationWithABlankIdentifier_LeavesItOut()
    {
        // Act
        var accounts = DeclaredMailAccounts.ReadFrom([Account("  primary  "), Account("   ")]);

        // Assert
        Assert.Equal(["primary"], Identifiers(accounts));
    }

    /// <summary>An account that configures no folder is run with the inbox mapping, so that is the folder a destination may name.</summary>
    [Fact]
    public void ReadFrom_AccountDeclaringNoFolder_MapsTheInbox()
    {
        // Act
        var account = Assert.Single(DeclaredMailAccounts.ReadFrom([Account("primary")]));

        // Assert
        Assert.Equal(["INBOX"], account.MappedFolders.Select(folder => folder.Alias.Value));
    }

    /// <summary>A folder is resolved when a change first files into it, so mapping one is all a destination needs.</summary>
    [Fact]
    public void ReadFrom_FolderTheAccountDoesNotMirror_IsStillADestination()
    {
        // Arrange
        var account = Account("primary");
        account.Folders =
        [
            new MailFolderMappingOptions { Alias = "inbox", SpecialUse = "Inbox" },
            new MailFolderMappingOptions { Alias = "spam", SpecialUse = "Junk", Synchronize = false },
        ];

        // Act
        var declared = Assert.Single(DeclaredMailAccounts.ReadFrom([account]));

        // Assert
        Assert.Equal(["INBOX", "SPAM"], declared.MappedFolders.Select(folder => folder.Alias.Value));
    }

    /// <summary>A folder that is not mirrored keeps nothing AI could read, so it is withheld from AI whether or not its mapping says so.</summary>
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(false, null, false)]
    [InlineData(null, false, false)]
    public void ReadFrom_AMappedFolder_IsVisibleToAIOnlyWhereItsParticipationAdmitsAI(
        bool? visibleToAI,
        bool? synchronize,
        bool expected)
    {
        // Arrange
        var account = Account("primary");
        account.Folders =
        [
            new MailFolderMappingOptions
            {
                Alias = "archive",
                RemotePath = "Archive",
                VisibleToAI = visibleToAI,
                Synchronize = synchronize,
            },
        ];

        // Act
        var accounts = DeclaredMailAccounts.ReadFrom([account]);

        // Assert
        var archive = Assert.Single(Assert.Single(accounts).MappedFolders, mapped => string.Equals(mapped.Alias.Value, "archive", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected, archive.IsVisibleToAI);
    }

    [Fact]
    public void ReadFrom_NoDeclarations_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => DeclaredMailAccounts.ReadFrom(null!));
    }

    private static MailSynchronizationAccountOptions Account(string accountId) => new() { AccountId = accountId };

    private static IReadOnlyList<string> Identifiers(IEnumerable<DeclaredMailAccount> accounts) =>
        [.. accounts.Select(account => account.AccountId)];
}
