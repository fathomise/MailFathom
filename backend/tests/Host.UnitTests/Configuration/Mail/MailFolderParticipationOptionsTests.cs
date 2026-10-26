// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers the three switches a folder mapping carries and how configuration answers for them.</summary>
public sealed class MailFolderParticipationOptionsTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    /// <summary>An account that configures no folder gets the inbox by role, and nothing about it is withheld.</summary>
    [Fact]
    public void GetParticipation_AnAccountConfiguringNoFolder_TakesPartInEverything()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var participation = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("INBOX"));

        // Assert
        Assert.Equal(MailFolderParticipation.Full, participation);
    }

    /// <summary>A folder that names its target and no switch behaves exactly as it did before the switches existed.</summary>
    [Fact]
    public void GetParticipation_AFolderConfiguringNoSwitch_TakesPartInEverything()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "archive",
            SpecialUse = "Archive",
        }));

        // Act
        var participation = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("ARCHIVE"));

        // Assert
        Assert.Equal(MailFolderParticipation.Full, participation);
    }

    /// <summary>Withholding a folder from AI keeps it mirrored and embeds nothing of it, since an embedding is a model's reading of the folder.</summary>
    [Fact]
    public void GetParticipation_AFolderWithheldFromAI_LeavesItInvisibleUnembeddedAndMirrored()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "private",
            RemotePath = "Private",
            VisibleToAI = false,
        }));

        // Act
        var participation = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("PRIVATE"));

        // Assert
        Assert.True(participation.IsSynchronized);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToAI);
    }

    /// <summary>A noisy folder can stay listed and filterable while costing no provider tokens.</summary>
    [Fact]
    public void GetParticipation_AFolderWithdrawnFromEmbedding_LeavesItUnembeddedAndKeepsItReadable()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "newsletters",
            RemotePath = "Newsletters",
            GenerateEmbeddings = false,
        }));

        // Act
        var participation = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("NEWSLETTERS"));

        // Assert
        Assert.False(participation.GeneratesEmbeddings);
        Assert.True(participation.IsVisibleToAI);
        Assert.True(participation.IsSynchronized);
    }

    /// <summary>A folder nothing mirrors takes part in nothing, so it is admitted to neither without either switch being configured.</summary>
    [Fact]
    public void GetParticipation_AFolderNothingMirrors_IsWithdrawnFromEmbeddingAndFromAI()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
        }));

        // Act
        var participation = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("JUNK"));

        // Assert
        Assert.Equal(MailFolderParticipation.MappedOnly, participation);
        Assert.True(participation.IsMapped);
        Assert.False(participation.IsVisibleToAI);
        Assert.False(participation.GeneratesEmbeddings);
    }

    /// <summary>A pass over stored mail runs against the folders a mapping mirrors, which is neither an unmirrored one nor an unmapped one.</summary>
    [Fact]
    public void GetParticipation_AnUnmirroredFolderBesideAMirroredOne_MirrorsOnlyTheMirroredOne()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(
            new MailFolderMappingOptions { Alias = "archive", RemotePath = "Archive" },
            new MailFolderMappingOptions { Alias = "junk", SpecialUse = "Junk", Synchronize = false }));

        // Act
        var archive = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("ARCHIVE"));
        var junk = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("JUNK"));

        // Assert
        Assert.True(archive.IsSynchronized);
        Assert.False(junk.IsSynchronized);
    }

    /// <summary>A folder no mapping names is a folder MailFathom does not have, so what it stored earlier takes part in nothing.</summary>
    [Fact]
    public void GetParticipation_AnAliasNothingMaps_TakesPartInNothingAndIsNotMapped()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
        }));

        // Act
        var participation = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("ARCHIVE"));

        // Assert
        Assert.Equal(MailFolderParticipation.Unmapped, participation);
        Assert.False(participation.IsMapped);
        Assert.False(participation.IsSynchronized);
        Assert.False(participation.GeneratesEmbeddings);
        Assert.False(participation.IsVisibleToAI);
    }

    /// <summary>Removing a mapping is not the same decision as switching a mapped folder off, so the two answers are not one value.</summary>
    [Fact]
    public void GetParticipation_AnUnmappedAliasBesideAnUnmirroredFolder_AnswersWithTwoDifferentValues()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
        }));

        // Act
        var unmirrored = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("JUNK"));
        var unmapped = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("ARCHIVE"));

        // Assert
        Assert.NotEqual(unmirrored, unmapped);
    }

    /// <summary>One account's decision is never another account's, which is what makes the identity a pair rather than an alias.</summary>
    [Fact]
    public void GetParticipation_TheSameAliasInTwoAccounts_WithholdsItOnlyFromTheAccountThatWithheldIt()
    {
        // Arrange
        var options = new MailSynchronizationOptions().Serving(
            CreateAccount(new MailFolderMappingOptions
            {
                Alias = "private",
                RemotePath = "Private",
                VisibleToAI = false,
            }),
            CreateAccount("secondary", new MailFolderMappingOptions
            {
                Alias = "private",
                RemotePath = "Private",
            }));

        // Act
        var withheld = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("PRIVATE"));
        var visible = options.Readers.FolderParticipation.GetParticipation(
            MailAccountId.Create("secondary"),
            MailFolderAlias.Create("PRIVATE"));

        // Assert
        Assert.False(withheld.IsVisibleToAI);
        Assert.True(visible.IsVisibleToAI);
    }

    /// <summary>Asking for something an unmirrored folder cannot do is refused where it binds, because the configuration would not do what it says.</summary>
    [Theory]
    [InlineData(true, null, "embeddings")]
    [InlineData(null, true, "visible to AI")]
    public void ValidateForSynchronization_AnUnmirroredFolderAskingForMore_IsRefusedNamingTheFolder(
        bool? generateEmbeddings,
        bool? visibleToAI,
        string expectedFragment)
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
            GenerateEmbeddings = generateEmbeddings,
            VisibleToAI = visibleToAI,
        }));

        // Act
        var messages = ConfiguredMailAccounts.Validate(options).Select(result => result.ErrorMessage!).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message.Contains("'junk'", StringComparison.Ordinal)
                && message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    /// <summary>Leaving a switch out is not asking for it, so the ordinary way to stop mirroring a folder is not a contradiction.</summary>
    [Fact]
    public void ValidateForSynchronization_AnUnmirroredFolderAskingForNothingElse_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            Synchronize = false,
        }));

        // Act
        var results = ConfiguredMailAccounts.Validate(options).ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>An embedding is computed by a provider a withheld folder never reaches, so asking for both is refused naming both keys.</summary>
    [Fact]
    public void ValidateForSynchronization_AFolderWithheldFromAIAskingForEmbeddings_IsRefusedNamingBothKeys()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "private",
            RemotePath = "Private",
            GenerateEmbeddings = true,
            VisibleToAI = false,
        }));

        // Act
        var messages = ConfiguredMailAccounts.Validate(options).Select(result => result.ErrorMessage!).ToArray();

        // Assert
        var message = Assert.Single(messages);
        Assert.Contains("'private'", message, StringComparison.Ordinal);
        Assert.Contains("'GenerateEmbeddings'", message, StringComparison.Ordinal);
        Assert.Contains("'VisibleToAI'", message, StringComparison.Ordinal);
    }

    /// <summary>Leaving embeddings out beside a folder withheld from AI is not asking for them, so the ordinary way to withhold a folder binds.</summary>
    [Fact]
    public void ValidateForSynchronization_AFolderWithheldFromAISayingNothingAboutEmbeddings_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "private",
            RemotePath = "Private",
            VisibleToAI = false,
        }));

        // Act
        var results = ConfiguredMailAccounts.Validate(options).ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// A folder that does not exist advertises no role, so creating one from a role would mean either an extension
    /// whose support is uneven or MailFathom inventing a name in somebody's own mailbox. Writing the path the folder is
    /// to be created at is one line of configuration, so the contradiction is refused where it binds.
    /// </summary>
    [Fact]
    public void ValidateForSynchronization_ARoleMappingAskingToBeCreated_IsRefusedNamingTheFolder()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            CreateIfMissing = true,
        }));

        // Act
        var messages = ConfiguredMailAccounts.Validate(options).Select(result => result.ErrorMessage!).ToArray();

        // Assert
        Assert.Contains(
            messages,
            message => message.Contains("'junk'", StringComparison.Ordinal)
                && message.Contains("RemotePath", StringComparison.Ordinal));
    }

    /// <summary>The switch belongs to a configured path, which is the only mapping that names where a folder would be created.</summary>
    [Fact]
    public void ValidateForSynchronization_APathMappingAskingToBeCreated_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "archive",
            RemotePath = "Archief/2026",
            CreateIfMissing = true,
        }));

        // Act
        var results = ConfiguredMailAccounts.Validate(options).ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>Explicitly declining a creation beside a role says nothing a role mapping cannot do, so it is not the contradiction above.</summary>
    [Fact]
    public void ValidateForSynchronization_ARoleMappingDecliningCreation_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
            CreateIfMissing = false,
        }));

        // Act
        var results = ConfiguredMailAccounts.Validate(options).ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>The junk role is read from what an operator configured, so a folder mapped to it is the one withheld.</summary>
    [Fact]
    public void IsJunkFolder_AFolderMappedToTheJunkRole_NamesThatFolder()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "junk",
            SpecialUse = "Junk",
        }));

        // Act, Assert
        Assert.True(options.Readers.JunkFolderCatalog.IsJunkFolder(Primary, MailFolderAlias.Create("JUNK")));
        Assert.False(options.Readers.JunkFolderCatalog.IsJunkFolder(Primary, MailFolderAlias.Create("INBOX")));
    }

    /// <summary>A deployment that maps no junk folder answers with nothing, and every mailbox read behaves as it did before.</summary>
    [Fact]
    public void IsJunkFolder_NoFolderMappedToTheJunkRole_NamesNothing()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "archive",
            SpecialUse = "Archive",
        }));

        // Act, Assert
        Assert.False(options.Readers.JunkFolderCatalog.IsJunkFolder(Primary, MailFolderAlias.Create("ARCHIVE")));
        Assert.False(options.Readers.JunkFolderCatalog.IsJunkFolder(Primary, MailFolderAlias.Create("INBOX")));
    }

    /// <summary>An account that configures no folder still runs with an inbox mapping, which is what classification defaults to.</summary>
    [Fact]
    public void InboxFolderAliases_AnAccountConfiguringNoFolder_NamesTheInboxMappingItRunsWith()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act, Assert
        Assert.Equal([MailFolderAlias.Create("INBOX")], ConfiguredMailFolders.InboxAliasesOf(options.DeclaredAccounts));
    }

    /// <summary>A server presenting the inbox under another name is configured by role, and the default scope follows the role.</summary>
    [Fact]
    public void InboxFolderAliases_AnInboxMappedUnderAnotherAlias_NamesTheConfiguredAlias()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "primary-mail",
            RemotePath = "Skrzynka odbiorcza",
            SpecialUse = "Inbox",
        }));

        // Act, Assert
        Assert.Equal([MailFolderAlias.Create("PRIMARY-MAIL")], ConfiguredMailFolders.InboxAliasesOf(options.DeclaredAccounts));
    }

    /// <summary>A folder is identified by its account beside its alias, so one user's folder never enters another user's scope.</summary>
    [Fact]
    public void GetParticipation_TwoUsersDeclaringTheSameAlias_KeepsEachFolderToItsOwnAccount()
    {
        // Arrange
        var options = new MailSynchronizationOptions().WithServedUsers(
        [
            User(SyntheticUser.Deployment, CreateAccount(new MailFolderMappingOptions { Alias = "private", RemotePath = "Private" })),
            User(
                SyntheticUser.Another,
                CreateAccount("secondary", new MailFolderMappingOptions
                {
                    Alias = "private",
                    RemotePath = "Private",
                    VisibleToAI = false,
                })),
        ]);

        // Act
        var visible = options.Readers.FolderParticipation.GetParticipation(Primary, MailFolderAlias.Create("PRIVATE"));
        var withheld = options.Readers.FolderParticipation.GetParticipation(
            MailAccountId.Create("secondary"),
            MailFolderAlias.Create("PRIVATE"));

        // Assert
        Assert.True(visible.IsVisibleToAI);
        Assert.False(withheld.IsVisibleToAI);
    }

    private static MailSynchronizationOptions OptionsFor(MailSynchronizationAccountOptions account) =>
        new MailSynchronizationOptions().Serving(account);

    private static ServedUser User(UserId user, MailSynchronizationAccountOptions account) =>
        new(user, "the user this deployment serves", [account]);

    private static MailSynchronizationAccountOptions CreateAccount(params MailFolderMappingOptions[] folders) =>
        CreateAccount("primary", folders);

    private static MailSynchronizationAccountOptions CreateAccount(
        string accountId,
        params MailFolderMappingOptions[] folders) => new()
        {
            AccountId = accountId,
            DisplayName = $"The {accountId} mailbox",
            Host = "imap.example.test",
            UserName = "mailfathom@example.test",
            Secrets = new MailAccountSecretOptions
            {
                Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
            },
            Folders = [.. folders],
        };
}
