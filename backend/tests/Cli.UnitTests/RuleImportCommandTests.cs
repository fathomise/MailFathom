// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what <c>rules export</c> writes and what <c>rules import</c> reads, sends, and reports.</summary>
/// <remarks>
/// The file is read from standard input here, because a test reads no file. What is asserted about an import is the body
/// the command composed — which rules it took from the file, without the keys the deployment does not read — and what it
/// told the operator about the rules that arrive disabled.
/// </remarks>
public sealed class RuleImportCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string Account = "work";
    private const string AddedRule = "0199a0c0-0000-7000-8000-00000000000a";
    private const string KeptRule = "0199a0c0-0000-7000-8000-00000000000b";

    private const string StoredRules = """
        {"account":"work","version":4,"revision":"a1b2c3d4e5f6","held":false,"rules":[]}
        """;

    private const string ExportedFile = """
        {
          "Rules": [
            { "Name": "flag-supplier", "Enabled": true, "Condition": "senderDomain == 'supplier.example'", "Triggers": [ "Arrival" ], "Actions": { "MarkAsFlagged": true }, "StopWhenMatched": false },
            { "Name": "file-invoices", "Enabled": false, "Condition": "attachmentCount > 0", "Triggers": [], "Actions": { "MoveTo": "invoices" }, "StopWhenMatched": true }
          ]
        }
        """;

    private const string ConfigurationFile = """
        {
          // The deployment's configuration, as an earlier release read it.
          "Logging": { "LogLevel": { "Default": "Information" } },
          "mailRules": {
            "MaxConditionLength": 2000,
            "Rules": [
              { "Name": "for-everybody", "Condition": "isSeen" },
              { "Name": "for-work", "Accounts": [ "work" ], "Enabled": false, "Condition": "isFlagged" },
              { "Name": "for-home", "Accounts": [ "home" ], "Condition": "isSeen" },
            ]
          }
        }
        """;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The export is the deployment's file, written out whole and as typed, so a condition's quotes reach the file unescaped.</summary>
    [Fact]
    public async Task Export_AnAccount_WritesTheRulesTheDeploymentExportedAsTyped()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(export: ExportedFile);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "export", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var written = Assert.Single(this.harness.Console.Lines);
        Assert.Contains("senderDomain == 'supplier.example'", written, StringComparison.Ordinal);
        using var file = JsonDocument.Parse(written);
        Assert.Equal(
            ["flag-supplier", "file-invoices"],
            file.RootElement.GetProperty("Rules").EnumerateArray().Select(rule => rule.GetProperty("Name").GetString()));
        Assert.Equal("account=work", deployment.RecordedRequests[^1].RequestUri!.Query.TrimStart('?'));
    }

    /// <summary>
    /// An exported file is sent as rule objects without <c>Enabled</c>, against the version the rules stand at; a rule
    /// the file states enabled that arrives disabled is reported with the commands that preview and enable it.
    /// </summary>
    [Fact]
    public async Task Import_AnExportedFile_SendsTheRuleObjectsAndNamesTheRulesThatArriveDisabled()
    {
        // Arrange
        this.harness.Console.InputToSupply = ExportedFile;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, import: Imported(committed: true));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "import", "--account", Account, "--from-file", "-", "--mode", "replace", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var sent = deployment.LastPosted(AdminEndpointRoutes.RuleImportPath)!.Value;
        Assert.Equal((Account, 4L, "replace"), (sent.GetProperty("account").GetString(), sent.GetProperty("version").GetInt64(), sent.GetProperty("mode").GetString()));
        var rules = sent.GetProperty("rules").EnumerateArray().ToArray();
        Assert.Equal(["flag-supplier", "file-invoices"], rules.Select(rule => rule.GetProperty("Name").GetString()));
        Assert.All(rules, rule => Assert.False(rule.TryGetProperty("Enabled", out _)));
        Assert.Null(deployment.LastPosted(AdminEndpointRoutes.RuleImportPreviewPath));
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("Imported 2 rules into work (replace). Its rules are now at version 5", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains($"The file states these rules enabled, and they are disabled: {AddedRule}.", StringComparison.Ordinal)
                && line.Contains("rules enable <rule> --account work", StringComparison.Ordinal));
        Assert.DoesNotContain(this.harness.Console.Errors, line => line.Contains(KeptRule, StringComparison.Ordinal));
    }

    /// <summary>
    /// A configuration file is read at its <c>MailRules</c> section, matched ignoring case and with its comments and
    /// trailing commas, and of its rules only the ones naming this account or no account are sent, without <c>Accounts</c>.
    /// </summary>
    [Fact]
    public async Task Import_AConfigurationFile_SendsTheRulesNamingTheAccountOrNoAccount()
    {
        // Arrange
        this.harness.Console.InputToSupply = ConfigurationFile;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, import: Imported(committed: true));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "import", "--account", Account, "--from-file", "-", "--mode", "merge", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var rules = deployment.LastPosted(AdminEndpointRoutes.RuleImportPath)!.Value.GetProperty("rules").EnumerateArray().ToArray();
        Assert.Equal(["for-everybody", "for-work"], rules.Select(rule => rule.GetProperty("Name").GetString()));
        Assert.All(rules, rule => Assert.Equal(["Name", "Condition"], rule.EnumerateObject().Select(property => property.Name)));
    }

    /// <summary>A preview asks the preview route alone, says nothing was written, and names the version to state when the import is made.</summary>
    [Fact]
    public async Task Import_APreview_AsksThePreviewRouteAndNamesTheVersionToImportAgainst()
    {
        // Arrange
        this.harness.Console.InputToSupply = ExportedFile;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, import: Imported(committed: false));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "import", "--account", Account, "--from-file", "-", "--mode", "replace", "--preview", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.NotNull(deployment.LastPosted(AdminEndpointRoutes.RuleImportPreviewPath));
        Assert.Null(deployment.LastPosted(AdminEndpointRoutes.RuleImportPath));
        Assert.Contains(this.harness.Console.Lines, line => line.EndsWith("against version 4. Nothing was written.", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("assigned on import", StringComparison.Ordinal) && line.Contains("added", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--mode replace --expected-version 4", StringComparison.Ordinal));
    }

    /// <summary>A refused import prints the deployment's reasons and fails with a sentence carrying none of them.</summary>
    [Fact]
    public async Task Import_AnImportTheDeploymentRefuses_PrintsTheReasonsAndFails()
    {
        // Arrange
        this.harness.Console.InputToSupply = ExportedFile;
        using var deployment = FakeRuleDeployment.Answering(
            rules: StoredRules,
            import: (HttpStatusCode.BadRequest, """{"detail":"The import was refused, and nothing changed:\n- Rule #2 of the import: The rule's MoveTo names no folder."}"""));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "import", "--account", Account, "--from-file", "-", "--mode", "merge", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("Rule #2 of the import", StringComparison.Ordinal));
        Assert.Equal([AdminApiClient.RuleRefused], this.harness.Console.Failures);
    }

    /// <summary>A replacement removes rules and a merge does not, so a mode the command does not know is refused before anything is sent.</summary>
    [Fact]
    public async Task Import_AModeItDoesNotKnow_IsRefusedWithoutSendingAnything()
    {
        // Arrange
        this.harness.Console.InputToSupply = ExportedFile;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, import: Imported(committed: true));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "import", "--account", Account, "--from-file", "-", "--mode", "overwrite", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Null(deployment.LastPosted(AdminEndpointRoutes.RuleImportPath));
    }

    /// <summary>A file with no rules list in either place is refused by what it lacks, and nothing it holds is printed.</summary>
    [Fact]
    public async Task Import_AFileHoldingNoRulesList_IsRefusedWithoutSendingOrEchoingIt()
    {
        // Arrange
        this.harness.Console.InputToSupply = """{ "MailRules": { "MaxConditionLength": 500 }, "Secret": "secret-text" }""";
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, import: Imported(committed: true));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "import", "--account", Account, "--from-file", "-", "--mode", "merge", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Null(deployment.LastPosted(AdminEndpointRoutes.RuleImportPath));
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("holds no rules list", StringComparison.Ordinal));
        Assert.DoesNotContain(this.harness.Console.Failures.Concat(this.harness.Console.Errors), line => line.Contains("secret-text", StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public void Dispose() => this.harness.Dispose();

    private static (HttpStatusCode Status, string Body) Imported(bool committed) => (HttpStatusCode.OK, $$"""
        {"account":"work","version":{{(committed ? 5 : 4)}},"revision":"0a1b2c3d4e5f","committed":{{(committed ? "true" : "false")}},"rules":[
          {"rule":{{(committed ? $"\"{AddedRule}\"" : "null")}},"name":"flag-supplier","effect":"Added","enabled":false},
          {"rule":"{{KeptRule}}","name":"file-invoices","effect":"Changed","enabled":false}
        ]}
        """);

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
