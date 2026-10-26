// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Host.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings;

/// <summary>Covers the refusal of a record asking for the spam model stage on a deployment with no model to ask.</summary>
public sealed class UserAccountSpamModelStageErrorsTests
{
    [Fact]
    public void FindSpamModelStageErrors_NoModelServesSpamClassification_RefusesEachAccountAskingNamingItsPositionAndTheModels()
    {
        // Arrange
        var record = RecordOf(Account("quiet", modelStage: false), Account("asking", modelStage: true));

        // Act
        var refusal = Assert.Single(record.FindSpamModelStageErrors(modelServesSpamClassification: false));

        // Assert
        Assert.StartsWith($"MailAccounts:1:{MailAccountSpamModelStageOptions.EnabledPath}", refusal.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Decisions:Models", refusal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void FindSpamModelStageErrors_AModelServesSpamClassification_RefusesNothing()
    {
        // Arrange
        var record = RecordOf(Account("asking", modelStage: true));

        // Act
        var refusals = record.FindSpamModelStageErrors(modelServesSpamClassification: true);

        // Assert
        Assert.Empty(refusals);
    }

    private static UserAccountOptions RecordOf(params MailSynchronizationAccountOptions[] accounts) =>
        new() { MailAccounts = [.. accounts] };

    private static MailSynchronizationAccountOptions Account(string accountId, bool modelStage)
    {
        var account = SynchronizationTestHost.CreateAccount(accountId, "INBOX");
        account.SpamClassification = new MailAccountSpamClassificationOptions
        {
            Enabled = true,
            ModelStage = new MailAccountSpamModelStageOptions { Enabled = modelStage },
        };

        return account;
    }
}
