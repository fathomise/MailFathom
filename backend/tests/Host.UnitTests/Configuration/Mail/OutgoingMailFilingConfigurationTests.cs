// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Mail;

/// <summary>Covers what configuration decides about the copies MailFathom puts into an account's own folders.</summary>
public sealed class OutgoingMailFilingConfigurationTests
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    /// <summary>
    /// Each account's outbox is MailFathom's own and no folder plays it any more, so a mapping naming the role is refused
    /// where it binds — with or without a path, however it is cased — and the refusal says which mapping to remove.
    /// </summary>
    [Theory]
    [InlineData("Outbox", null)]
    [InlineData("Outbox", "INBOX.Outbox")]
    [InlineData("outbox", "INBOX.Outbox")]
    public void ValidateForSynchronization_AMappingNamingTheRetiredOutboxRole_IsRefusedNamingTheMappingToRemove(
        string specialUse,
        string? remotePath)
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "waiting",
            RemotePath = remotePath,
            SpecialUse = specialUse,
        }));

        // Act
        var messages = ConfiguredMailAccounts.Validate(options).Select(result => result.ErrorMessage!).ToArray();

        // Assert
        var message = Assert.Single(messages);
        Assert.Contains("'waiting'", message, StringComparison.Ordinal);
        Assert.Contains("Remove this folder mapping", message, StringComparison.Ordinal);
    }

    /// <summary>A provider folder merely named like an outbox is an ordinary folder, because nothing here reads a folder's name.</summary>
    [Fact]
    public void ValidateForSynchronization_AFolderMerelyNamedLikeAnOutbox_ReportsNoError()
    {
        // Arrange
        var options = OptionsFor(CreateAccount(new MailFolderMappingOptions
        {
            Alias = "outbox",
            RemotePath = "INBOX.Outbox",
        }));

        // Act
        var results = ConfiguredMailAccounts.Validate(options).ToArray();

        // Assert
        Assert.Empty(results);
    }

    /// <summary>
    /// An account that says nothing about it files the copy, because a submission server files nothing and a deployment
    /// that appended nothing would leave the user with mail they sent and no record of it.
    /// </summary>
    [Fact]
    public void FilesSentCopy_AnAccountThatSaysNothing_FilesTheCopy()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var filesSentCopy = options.Readers.OutgoingMailFilingPolicies.FilesSentCopy(Primary);

        // Assert
        Assert.True(filesSentCopy);
    }

    /// <summary>Turning it off is the account whose provider files the copy itself, which is configured and never detected.</summary>
    [Fact]
    public void FilesSentCopy_AnAccountThatTurnsItOff_FilesNothing()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = new MailAccountDeliveryOptions
        {
            Host = "smtp.example.test",
            FileSentCopy = false,
        };

        // Act
        var filesSentCopy = OptionsFor(account).Readers.OutgoingMailFilingPolicies.FilesSentCopy(Primary);

        // Assert
        Assert.False(filesSentCopy);
    }

    /// <summary>Nothing can send as an account nobody configured, so the answer is reached only by a caller asking about a message that cannot exist.</summary>
    [Fact]
    public void FilesSentCopy_AnAccountNothingConfigures_AnswersAsThoughItFiles()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var filesSentCopy = options.Readers.OutgoingMailFilingPolicies.FilesSentCopy(MailAccountId.Create("withdrawn"));

        // Assert
        Assert.True(filesSentCopy);
    }

    /// <summary>An account that says nothing ends with one copy of each message it sent, whatever its provider does.</summary>
    [Fact]
    public void WithdrawsDuplicateSentCopy_AnAccountThatSaysNothing_TakesItsOwnCopyBackOut()
    {
        // Arrange
        var options = OptionsFor(CreateAccount());

        // Act
        var withdrawsDuplicate = options.Readers.OutgoingMailFilingPolicies.WithdrawsDuplicateSentCopy(Primary);

        // Assert
        Assert.True(withdrawsDuplicate);
    }

    /// <summary>Turning it off keeps both copies, which is the account whose provider files something worth keeping.</summary>
    [Fact]
    public void WithdrawsDuplicateSentCopy_AnAccountThatTurnsItOff_KeepsBothCopies()
    {
        // Arrange
        var account = CreateAccount();
        account.Delivery = new MailAccountDeliveryOptions
        {
            Host = "smtp.example.test",
            WithdrawDuplicateSentCopy = false,
        };

        // Act
        var withdrawsDuplicate =
            OptionsFor(account).Readers.OutgoingMailFilingPolicies.WithdrawsDuplicateSentCopy(Primary);

        // Assert
        Assert.False(withdrawsDuplicate);
    }

    private static MailSynchronizationOptions OptionsFor(MailSynchronizationAccountOptions account) =>
        new MailSynchronizationOptions().Serving(account);

    private static MailSynchronizationAccountOptions CreateAccount(params MailFolderMappingOptions[] folders) => new()
    {
        AccountId = "primary",
        DisplayName = "The primary mailbox",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
        },
        Folders = [.. folders],
    };
}
