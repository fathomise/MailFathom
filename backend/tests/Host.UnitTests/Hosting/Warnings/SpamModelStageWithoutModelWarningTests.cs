// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about an account asking for a spam model nobody declared.</summary>
/// <remarks>
/// The accounts are read from the served records, as a deployment holds them: the published settings carry no user, so
/// a warning reading those would find nobody and say nothing on every start.
/// </remarks>
public sealed class SpamModelStageWithoutModelWarningTests
{
    private const string AskingDocument = """{"SpamClassification":{"Enabled":true,"ModelStage":{"Enabled":true}}}""";

    private const string QuietDocument = """{"SpamClassification":{"Enabled":true}}""";

    private static readonly Guid Asking = new("0199a0c0-0000-7000-8000-0000000005a1");

    private static readonly Guid Quiet = new("0199a0c0-0000-7000-8000-0000000005a2");

    [Fact]
    public async Task StartAsync_AServedRecordAskingForTheModelStage_NamesTheAccountAndTheKeyToDeclare()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(Judge(isActive: false), logs, Record(Asking, AskingDocument), Record(Quiet, QuietDocument));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(Asking.ToString("D"), Assert.Contains("AccountId", record.Properties));
        Assert.Contains("Decisions:SpamClassification:Model", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_ModelStageOnWhileClassificationIsOff_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(
            Judge(isActive: false),
            logs,
            Record(Asking, """{"SpamClassification":{"Enabled":false,"ModelStage":{"Enabled":true}}}"""));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task StartAsync_AModelServesTheStage_ReadsNoRecordAndSaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var servedAccounts = Substitute.For<IServedMailAccountReader>();
        var warning = new SpamModelStageWithoutModelWarning(Judge(isActive: true), servedAccounts, Logger(logs));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
        await servedAccounts.DidNotReceive().ReadServedRecordsAsync(Arg.Any<CancellationToken>());
    }

    private static SpamModelStageWithoutModelWarning WarningOver(
        ISpamModelJudge judge,
        RecordingLoggerProvider logs,
        params MailAccountRecord[] records)
    {
        var servedAccounts = Substitute.For<IServedMailAccountReader>();
        servedAccounts.ReadServedRecordsAsync(Arg.Any<CancellationToken>()).Returns(records);

        return new SpamModelStageWithoutModelWarning(judge, servedAccounts, Logger(logs));
    }

    private static ISpamModelJudge Judge(bool isActive)
    {
        var judge = Substitute.For<ISpamModelJudge>();
        judge.IsActive.Returns(isActive);

        return judge;
    }

    private static ILogger<SpamModelStageWithoutModelWarning> Logger(RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return loggerFactory.CreateLogger<SpamModelStageWithoutModelWarning>();
    }

    private static MailAccountRecord Record(Guid accountId, string document) =>
        new(accountId, "alex@example.test", "Alex at work", ServableMailAccountDocuments.Completing(document), Version: 1);
}
