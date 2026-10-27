// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Rules;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Spam;

/// <summary>Covers what an account's model stage block refuses at the write, and the settings it produces.</summary>
public sealed class MailAccountSpamModelStageOptionsTests
{
    [Fact]
    public void FindRefusals_ModelStageOnOverFoldersAIMayRead_IsAccepted()
    {
        // Arrange
        var options = Classifying(new MailAccountSpamModelStageOptions { Enabled = true, Threshold = 0.8 });

        // Act
        var refusals = options.FindRefusals(AccountOf(Folder("INBOX", "Inbox"))).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    [Fact]
    public void FindRefusals_ModelStageOnWhileTheDefaultInboxIsWithheldFromAI_IsRefusedNamingTheAccountAndFolder()
    {
        // Arrange
        var options = Classifying(new MailAccountSpamModelStageOptions { Enabled = true });

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf(Folder("INBOX", "Inbox", visibleToAI: false))));

        // Assert
        Assert.Contains("'INBOX'", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("'work'", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(MailAccountSpamModelStageOptions.EnabledPath, refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindRefusals_AScannedFolderWithheldFromAI_IsRefusedAndAWithheldFolderNotScannedIsNot()
    {
        // Arrange
        var options = Classifying(new MailAccountSpamModelStageOptions { Enabled = true }, scannedFolders: ["INBOX", "news"]);
        var account = AccountOf(
            Folder("INBOX", "Inbox"),
            Folder("news", specialUse: null, visibleToAI: false),
            Folder("private", specialUse: null, visibleToAI: false));

        // Act
        var refusal = Assert.Single(options.FindRefusals(account));

        // Assert
        Assert.Contains("'NEWS'", refusal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindRefusals_ModelStageOffOverAWithheldFolder_IsAccepted()
    {
        // Arrange
        var options = Classifying(new MailAccountSpamModelStageOptions { Enabled = false });

        // Act
        var refusals = options.FindRefusals(AccountOf(Folder("INBOX", "Inbox", visibleToAI: false))).ToArray();

        // Assert
        Assert.Empty(refusals);
    }

    [Fact]
    public void FindRefusals_ModelStageOnWhileClassificationIsOff_IsRefused()
    {
        // Arrange
        var options = new MailAccountSpamClassificationOptions
        {
            Enabled = false,
            ModelStage = new MailAccountSpamModelStageOptions { Enabled = true },
        };

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf(Folder("INBOX", "Inbox"))));

        // Assert
        Assert.Equal([nameof(MailAccountSpamModelStageOptions.Enabled)], refusal.MemberNames);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void FindRefusals_AThresholdThatIsNoProbabilityAboveZero_IsRefused(double threshold)
    {
        // Arrange
        var options = Classifying(new MailAccountSpamModelStageOptions { Enabled = true, Threshold = threshold });

        // Act
        var refusal = Assert.Single(options.FindRefusals(AccountOf(Folder("INBOX", "Inbox"))));

        // Assert
        Assert.Equal([nameof(MailAccountSpamModelStageOptions.Threshold)], refusal.MemberNames);
    }

    [Fact]
    public void ToSettings_StageOnUnderAClassifyingAccount_IsConsultedOnItsTerms()
    {
        // Arrange
        var options = new MailAccountSpamModelStageOptions
        {
            Enabled = true,
            NameSubcategoriesOfDeterministicSpam = true,
            Threshold = 0.65,
        };

        // Act
        var settings = options.ToSettings(classificationEnabled: true);

        // Assert
        Assert.Equal(SpamModelStageSettings.Consulted(true, 0.65), settings);
    }

    [Fact]
    public void ToSettings_StageOnWhileClassificationIsOff_IsNotConsulted()
    {
        // Arrange
        var options = new MailAccountSpamModelStageOptions { Enabled = true };

        // Act
        var settings = options.ToSettings(classificationEnabled: false);

        // Assert
        Assert.Equal(SpamModelStageSettings.NotConsulted, settings);
    }

    [Fact]
    public void NewBlock_WritesNothing_IsOffDoesNotNameDeterministicSpamAndHoldsTheDefaultThreshold()
    {
        // Arrange, Act
        var options = new MailAccountSpamModelStageOptions();

        // Assert
        Assert.False(options.Enabled);
        Assert.False(options.NameSubcategoriesOfDeterministicSpam);
        Assert.Equal(SpamModelStageSettings.DefaultThreshold, options.Threshold);
    }

    private static MailAccountSpamClassificationOptions Classifying(
        MailAccountSpamModelStageOptions modelStage,
        string[]? scannedFolders = null) =>
        new()
        {
            Enabled = true,
            ScannedFolders = scannedFolders,
            ModelStage = modelStage,
        };

    private static MailFolderMappingOptions Folder(string alias, string? specialUse, bool visibleToAI = true) =>
        new() { Alias = alias, SpecialUse = specialUse, VisibleToAI = visibleToAI };

    private static DeclaredMailAccount AccountOf(params MailFolderMappingOptions[] folders) =>
        DeclaredMailAccounts.ReadFrom(
        [
            new MailSynchronizationAccountOptions
            {
                AccountId = "work",
                DisplayName = "The mailbox",
                Host = "imap.example.test",
                UserName = "mailfathom@example.test",
                Secrets = new MailAccountSecretOptions
                {
                    Password = new ConfiguredSecret
                    {
                        SecretReference = "systemd-credential:imap-password",
                    },
                },
                Folders = [.. folders],
            },
        ]).Single();
}
