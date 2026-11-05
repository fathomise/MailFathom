// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Host.Hosting.Warnings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told when the configuration still declares rules nothing reads any more.</summary>
public sealed class ConfiguredMailRulesIgnoredWarningTests
{
    [Fact]
    public async Task StartAsync_ASectionStillDeclaringRules_StatesTheCountAndTheImportAndNoRuleContent()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(
            new Dictionary<string, string?>
            {
                ["MailRules:Rules:0:Name"] = "invoices-from-acme",
                ["MailRules:Rules:0:Condition"] = "from == 'billing@acme.example'",
                ["MailRules:Rules:1:Name"] = "newsletters",
                ["MailRules:Rules:1:Condition"] = "isSeen",
            },
            logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(2, Assert.Contains("DeclaredRuleCount", record.Properties));
        Assert.Contains("mfctl rules import --account", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("acme", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("newsletters", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_NoRulesDeclared_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(new Dictionary<string, string?> { ["MailRules:MaxConditionLength"] = "500" }, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static ConfiguredMailRulesIgnoredWarning WarningOver(
        IDictionary<string, string?> settings,
        RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new ConfiguredMailRulesIgnoredWarning(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            loggerFactory.CreateLogger<ConfiguredMailRulesIgnoredWarning>());
    }
}
