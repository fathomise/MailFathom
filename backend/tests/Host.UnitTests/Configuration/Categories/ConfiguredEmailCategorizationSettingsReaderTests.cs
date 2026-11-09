// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Categories;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Categories;

/// <summary>Covers how an account's record becomes what the categorization pass reads.</summary>
public sealed class ConfiguredEmailCategorizationSettingsReaderTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    [Fact]
    public void SettingsFor_AnAccountSwitchedOn_ReadsItsOwnCategories()
    {
        // Arrange
        var account = new MailSynchronizationAccountOptions { AccountId = "work" };
        account.Categorization.Enabled = true;
        account.Categorization.OwnCategories.Add(new MailAccountOwnCategoryOptions { Name = "Heron", Description = "The Heron project." });

        // Act
        var settings = Reader(account).SettingsFor(Work);

        // Assert
        Assert.True(settings.IsEnabled);
        Assert.Equal([new OwnMailCategory("Heron", "The Heron project.")], settings.OwnCategories);
    }

    [Fact]
    public void SettingsFor_AnAccountSwitchedOff_IsDisabledWhateverItNames()
    {
        // Arrange
        var account = new MailSynchronizationAccountOptions { AccountId = "work" };
        account.Categorization.OwnCategories.Add(new MailAccountOwnCategoryOptions { Name = "Heron", Description = "The Heron project." });

        // Act
        var settings = Reader(account).SettingsFor(Work);

        // Assert
        Assert.Same(EmailCategorizationSettings.Disabled, settings);
    }

    [Fact]
    public void SettingsFor_AnAccountThisDeploymentDoesNotDeclare_IsDisabled()
    {
        // Act
        var settings = Reader().SettingsFor(Work);

        // Assert
        Assert.Same(EmailCategorizationSettings.Disabled, settings);
    }

    private static ConfiguredEmailCategorizationSettingsReader Reader(params MailSynchronizationAccountOptions[] accounts) =>
        new(new MailSynchronizationOptions().WithServedUsers(
            [.. accounts.Select(static account => new ServedUser(UserId.Create(Guid.CreateVersion7()), account.AccountId, [account]))]));
}
