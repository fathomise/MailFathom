// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the commands changing and previewing a mail account's rules send, report, and refuse.</summary>
/// <remarks>
/// Every change states the version it was written against, so what is asserted beside the report is the body the command
/// composed: the account, the version — the operator's or the one the rules stood at — and the rule it names. A rule
/// object is read from standard input here, because a test reads no file.
/// </remarks>
public sealed class RuleChangeCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string Account = "work";
    private const string RuleId = "0199a0c0-0000-7000-8000-000000000009";

    private const string RuleObject = """
        { "Name": "flag-supplier", "Condition": "senderDomain == 'supplier.example'", "Actions": { "MarkAsFlagged": true } }
        """;

    private const string StoredRules = """
        {"account":"work","version":4,"revision":"a1b2c3d4e5f6","held":false,"rules":[]}
        """;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// A created rule acts on nothing until it is enabled, so the report says so and the notice names the two commands
    /// that come next: the preview, then the enable.
    /// </summary>
    [Fact]
    public async Task Create_ARuleObjectOnStandardInput_SendsItAgainstTheCurrentVersionAndSaysItIsDisabled()
    {
        // Arrange
        this.harness.Console.InputToSupply = RuleObject;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "create", "--account", Account, "--from-file", "-", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var sent = deployment.LastPosted(AdminEndpointRoutes.RulesPath)!.Value;
        Assert.Equal((Account, 4L), (sent.GetProperty("account").GetString(), sent.GetProperty("version").GetInt64()));
        Assert.Equal("flag-supplier", sent.GetProperty("declaration").GetProperty("Name").GetString());
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith($"Created rule 'flag-supplier' ({RuleId}) in work: it is disabled", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line == "The rules of work are now at version 5, revision 0a1b2c3d4e5f.");
        Assert.Contains(this.harness.Console.Errors, line => line.Contains($"rules preview {RuleId}", StringComparison.Ordinal) && line.Contains($"rules enable {RuleId}", StringComparison.Ordinal));
    }

    /// <summary>A version the operator states is sent as stated, so the change is judged against what they read rather than what is there now.</summary>
    [Fact]
    public async Task Edit_AnExpectedVersion_SendsThatVersionWithoutReadingTheRules()
    {
        // Arrange
        this.harness.Console.InputToSupply = RuleObject;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 3));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "edit", "flag-supplier", "--account", Account, "--from-file", "-", "--expected-version", "2", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var sent = deployment.LastPosted(AdminEndpointRoutes.RuleReplacementPath)!.Value;
        Assert.Equal((2L, "flag-supplier"), (sent.GetProperty("version").GetInt64(), sent.GetProperty("rule").GetString()));
        Assert.Null(FakeRuleDeployment.LastRulesQuery(deployment));
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("Replaced the declaration of rule 'flag-supplier'", StringComparison.Ordinal));
    }

    /// <summary>Each change naming an existing rule reaches the route of that change and reports it in the same two lines.</summary>
    /// <param name="command">The command.</param>
    /// <param name="path">The route it posts to.</param>
    /// <param name="reported">How the report opens.</param>
    [Theory]
    [InlineData("enable", AdminEndpointRoutes.RuleEnablementPath, "Enabled rule")]
    [InlineData("disable", AdminEndpointRoutes.RuleDisablementPath, "Disabled rule")]
    public async Task EnableOrDisable_ARuleByName_PostsToItsRouteAndReportsTheNewVersion(string command, string path, string reported)
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", command, "flag-supplier", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal("flag-supplier", deployment.LastPosted(path)!.Value.GetProperty("rule").GetString());
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith($"{reported} 'flag-supplier' ({RuleId}) in work", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("The rules of work are now at version 5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Move_APosition_SendsItAndReportsWhereTheRuleIsNow()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "move", RuleId, "--to", "0", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.LastPosted(AdminEndpointRoutes.RulePositionPath)!.Value.GetProperty("position").GetInt32());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("it is now at position 0", StringComparison.Ordinal));
    }

    /// <summary>Nothing restores a removed rule, so a refusal at the prompt removes nothing and says so.</summary>
    [Fact]
    public async Task Delete_TheOperatorDeclines_RemovesNothing()
    {
        // Arrange
        this.harness.Console.AnswerToGive = false;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "delete", "flag-supplier", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ChangeRequestCount());
        Assert.Contains(this.harness.Console.Questions, question => question.Contains("Remove rule 'flag-supplier' from work?", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Failures, line => line == "Nothing was removed.");
    }

    /// <summary>Nobody at the terminal is no agreement, so an unattended removal needs the flag that states one.</summary>
    [Fact]
    public async Task Delete_NobodyToConfirm_IsRefusedAndNamesTheFlag()
    {
        // Arrange
        this.harness.Console.AnswersQuestions = false;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "delete", "flag-supplier", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ChangeRequestCount());
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_Yes_RemovesTheRuleWithoutAsking()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "delete", "flag-supplier", "--account", Account, "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.harness.Console.Questions);
        Assert.Equal("flag-supplier", deployment.LastPosted(AdminEndpointRoutes.RuleRemovalPath)!.Value.GetProperty("rule").GetString());
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("Removed rule 'flag-supplier'", StringComparison.Ordinal));
    }

    /// <summary>A change written against an older version reports the conflict the deployment named, and fails.</summary>
    [Fact]
    public async Task Enable_AStaleVersion_FailsWithTheConflictTheDeploymentNamed()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(
            rules: StoredRules,
            change: (HttpStatusCode.Conflict, """{"detail":"This account's rules are at version 6, and the change was written against version 4, so nothing changed. Read the rules again and decide again."}"""));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "enable", "flag-supplier", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("at version 6", StringComparison.Ordinal) && line.Contains("version 4", StringComparison.Ordinal));
        Assert.DoesNotContain(this.harness.Console.Lines, line => line.StartsWith("Enabled", StringComparison.Ordinal));
    }

    /// <summary>A refused candidate fails with the deployment's reasons, and nothing the operator typed is printed back.</summary>
    [Fact]
    public async Task Create_ACandidateTheDeploymentRefuses_FailsWithItsReasonsWithoutEchoingTheRule()
    {
        // Arrange
        this.harness.Console.InputToSupply = RuleObject;
        using var deployment = FakeRuleDeployment.Answering(
            rules: StoredRules,
            change: (HttpStatusCode.BadRequest, """{"detail":"The change was refused, and nothing changed:\n- The condition of mail rule '0199a0c0-0000-7000-8000-000000000009' could not be parsed at position 12."}"""));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "create", "--account", Account, "--from-file", "-", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Failures, line => line.Contains("position 12", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("supplier.example", StringComparison.Ordinal));
    }

    /// <summary>What is not JSON is reported by where it stops being JSON, and never by what it says.</summary>
    [Fact]
    public async Task Create_InputThatIsNotJson_IsRefusedWithoutSendingOrEchoingIt()
    {
        // Arrange
        this.harness.Console.InputToSupply = "{ \"Name\": \"flag-supplier\", \"Condition\": secret-text }";
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "create", "--account", Account, "--from-file", "-", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ChangeRequestCount());
        Assert.DoesNotContain(this.harness.Console.Errors, line => line.Contains("secret-text", StringComparison.Ordinal));
    }

    /// <summary>Every change names its account, and none is sent without one.</summary>
    [Fact]
    public async Task Create_NoAccountNamed_IsRefusedWithoutSendingAnything()
    {
        // Arrange
        this.harness.Console.InputToSupply = RuleObject;
        using var deployment = FakeRuleDeployment.Answering(rules: StoredRules, change: Changed("flag-supplier", 5));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "create", "--from-file", "-", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ChangeRequestCount());
    }

    /// <summary>A preview says what was examined and what a match asks for, names the matches by identity, and says nothing changed.</summary>
    [Fact]
    public async Task Preview_AStoredRule_PrintsTheMatchesWhatTheyWouldAskForAndTheCursor()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(preview: (HttpStatusCode.OK, """
            {
              "account": "work",
              "examinedEmailCount": 1000,
              "failedEmailCount": 1,
              "matches": ["0199a0c0-0000-7000-8000-0000000000a1"],
              "requested": [{"position":0,"mutation":"set-flagged","destination":null,"desiredSeenState":null,"desiredFlaggedState":true,"keywords":null}],
              "continueAfter": "0199a0c0-0000-7000-8000-0000000000b2"
            }
            """));

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "preview", "flag-supplier", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var sent = deployment.LastPosted(AdminEndpointRoutes.RulePreviewPath)!.Value;
        Assert.Equal("flag-supplier", sent.GetProperty("rule").GetString());
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("Previewing rule 'flag-supplier' over work: 1000 messages examined, 1 would match, 1 gave", StringComparison.Ordinal) && line.EndsWith("Nothing was changed.", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("A match would ask for: ", StringComparison.Ordinal) && line.Contains("flagged", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("0199a0c0-0000-7000-8000-0000000000a1", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.EndsWith("--cursor 0199a0c0-0000-7000-8000-0000000000b2", StringComparison.Ordinal));
        Assert.Equal(0, deployment.ChangeRequestCount());
    }

    /// <summary>A candidate on standard input is previewed as an object, with the cursor a previous page printed.</summary>
    [Fact]
    public async Task Preview_ACandidateAndACursor_SendsBothAndNoStoredRule()
    {
        // Arrange
        this.harness.Console.InputToSupply = RuleObject;
        using var deployment = FakeRuleDeployment.Answering(preview: (HttpStatusCode.OK, """
            {"account":"work","examinedEmailCount":0,"failedEmailCount":0,"matches":[],"requested":[],"continueAfter":null}
            """));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "rules", "preview", "--account", Account, "--from-file", "-", "--cursor", "0199a0c0-0000-7000-8000-0000000000b2", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var sent = deployment.LastPosted(AdminEndpointRoutes.RulePreviewPath)!.Value;
        Assert.Equal("flag-supplier", sent.GetProperty("declaration").GetProperty("Name").GetString());
        Assert.Equal("0199a0c0-0000-7000-8000-0000000000b2", sent.GetProperty("after").GetString());
        Assert.False(sent.TryGetProperty("rule", out var rule) && rule.ValueKind != JsonValueKind.Null);
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("Previewing the rule object over work", StringComparison.Ordinal));
    }

    /// <summary>A preview previews one rule, so naming a stored one and a file together is refused before anything is sent.</summary>
    [Fact]
    public async Task Preview_BothARuleAndAFile_IsRefusedWithoutAskingTheDeployment()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "preview", "flag-supplier", "--account", Account, "--from-file", "-", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Null(deployment.LastPosted(AdminEndpointRoutes.RulePreviewPath));
    }

    /// <summary>The listing carries each rule's identifier and the version, which are what a change is written with.</summary>
    [Fact]
    public async Task List_AStoredRule_PrintsItsIdentifierAndTheSetsVersion()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: $$"""
            {"account":"work","version":4,"revision":"a1b2c3d4e5f6","held":false,"rules":[
              {"id":"{{RuleId}}","position":0,"name":"flag-supplier","enabled":false,"condition":"isSeen","actions":[],"stopWhenMatched":false,"triggers":[]}
            ]}
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "list", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line == "work — rule set a1b2c3d4e5f6, version 4");
        Assert.Contains(this.harness.Console.Lines, line => line.StartsWith("flag-supplier", StringComparison.Ordinal) && line.Contains(RuleId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Show_ARuleByItsIdentifier_IsFound()
    {
        // Arrange
        using var deployment = FakeRuleDeployment.Answering(rules: $$"""
            {"account":"work","version":4,"revision":"a1b2c3d4e5f6","held":false,"rules":[
              {"id":"{{RuleId}}","position":0,"name":"flag-supplier","enabled":false,"condition":"isSeen","actions":[],"stopWhenMatched":false,"triggers":[]}
            ]}
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "rules", "show", RuleId.ToUpperInvariant(), "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("flag-supplier", StringComparison.Ordinal));
    }

    public void Dispose() => this.harness.Dispose();

    private static (HttpStatusCode Status, string Body) Changed(string name, long version) => (HttpStatusCode.OK, $$"""
        {"account":"work","rule":"{{RuleId}}","name":"{{name}}","version":{{version}},"revision":"0a1b2c3d4e5f"}
        """);

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
