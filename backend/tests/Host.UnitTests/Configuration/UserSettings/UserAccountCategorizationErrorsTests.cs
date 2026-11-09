// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Categories;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers the refusal of a record switching categorization on where no decision model could answer it.</summary>
public sealed class UserAccountCategorizationErrorsTests
{
    [Fact]
    public void FindCategorizationWithoutModelErrors_NoModelDeclared_RefusesEachAccountAskingNamingItsKeyAndTheModels()
    {
        // Arrange
        var record = RecordOf(Account("quiet", categorizes: false), Account("asking", categorizes: true));

        // Act
        var refusal = Assert.Single(record.FindCategorizationWithoutModelErrors(decisionModelDeclared: false));

        // Assert
        Assert.StartsWith($"MailAccounts:1:{MailAccountCategorizationOptions.EnabledPath}", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Decisions:Models", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindCategorizationWithoutModelErrors_AModelDeclared_RefusesNothing()
    {
        // Arrange
        var record = RecordOf(Account("asking", categorizes: true));

        // Act
        var refusals = record.FindCategorizationWithoutModelErrors(decisionModelDeclared: true);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>The account's own rules run wherever its declaration is judged, so a bad block is refused with the account named.</summary>
    [Fact]
    public void ValidateForSynchronization_AnUnusableBlock_IsRefusedNamingTheAccount()
    {
        // Arrange
        var account = Account("asking", categorizes: true);
        account.Categorization.DisabledDefaults = ["nothing"];

        // Act
        var refusals = account.ValidateForSynchronization(synchronizationEnabled: true);

        // Assert
        Assert.Contains(
            refusals,
            refusal => refusal.ErrorMessage!.StartsWith("Account 'asking': ", StringComparison.Ordinal)
                && refusal.ErrorMessage.Contains("DisabledDefaults:0", StringComparison.Ordinal));
    }

    private static UserAccountOptions RecordOf(params MailSynchronizationAccountOptions[] accounts) =>
        new() { MailAccounts = [.. accounts] };

    private static MailSynchronizationAccountOptions Account(string accountId, bool categorizes)
    {
        var account = SynchronizationTestHost.CreateAccount(accountId, "INBOX");
        account.Categorization = new MailAccountCategorizationOptions { Enabled = categorizes };

        return account;
    }
}
