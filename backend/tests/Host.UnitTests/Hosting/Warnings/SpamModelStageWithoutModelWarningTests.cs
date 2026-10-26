// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about an account asking for a spam model nobody declared.</summary>
public sealed class SpamModelStageWithoutModelWarningTests
{
    [Fact]
    public async Task StartAsync_AnAccountAskingForTheModelStage_NamesTheAccountAndTheKeyToDeclare()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(
            Account("asking", modelStage: true),
            Account("quiet", modelStage: false),
            logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("asking", Assert.Contains("AccountId", record.Properties));
        Assert.Contains("Decisions:SpamClassification:Model", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_ModelStageOnWhileClassificationIsOff_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var account = Account("off", modelStage: true);
        account.SpamClassification.Enabled = false;
        var warning = WarningOver(account, Account("quiet", modelStage: false), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

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

    private static SpamModelStageWithoutModelWarning WarningOver(
        MailSynchronizationAccountOptions first,
        MailSynchronizationAccountOptions second,
        RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new SpamModelStageWithoutModelWarning(
            new StubSettingsSnapshot<MailSynchronizationOptions>(SynchronizationTestHost.CreateOptions(true, first, second)),
            loggerFactory.CreateLogger<SpamModelStageWithoutModelWarning>());
    }
}
