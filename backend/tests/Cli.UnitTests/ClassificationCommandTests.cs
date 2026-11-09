// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the classification commands ask for and what they report.</summary>
/// <remarks>
/// The dry run is the subject of most of it. A run over an inbox with filing switched on is the largest single thing this
/// feature does to somebody's mail, so what is asserted is that acting is something an operator says rather than something
/// they fail to switch off, and that a run reporting what it would do says how to ask for the other one.
/// </remarks>
public sealed class ClassificationCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string Account = "work";

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Without <c>--apply</c> the command asks for a run that changes nothing, and says how to ask for one that does.</summary>
    [Fact]
    public async Task Run_NoApplyAsked_AsksForADryRunAndSaysHowToCarryItOut()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "classification", "run", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.ClassificationRunRequestCount());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("has been asked for", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("dry run", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("--apply", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("classification run-status", StringComparison.Ordinal));
    }

    /// <summary>The deployment refuses one category beside <c>--apply</c>, so the command must not advise adding it.</summary>
    [Fact]
    public async Task Run_OneCategoryAsked_SaysTheMailboxIsLeftAloneWithoutAdvisingApply()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false, category: "Project Heron")));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "classification",
            "run",
            "--account",
            Account,
            "--category",
            "Project Heron",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("takes no action on the mailbox", StringComparison.Ordinal));
        Assert.DoesNotContain(this.harness.Console.Lines, line => line.Contains("--apply", StringComparison.Ordinal));
    }

    /// <summary>Naming no folder is sent as naming none, because the deployment refuses an empty list as a request that named nothing.</summary>
    [Fact]
    public async Task Run_NoFolderNamed_SendsNoFoldersSoTheDeploymentWalksItsConfiguredScope()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "classification", "run", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var body = deployment.RecordedRequests
            .Single(request => request.Method == HttpMethod.Post)
            .ContentAsUtf8String();

        Assert.Contains("\"folders\": null", body, StringComparison.Ordinal);
    }

    /// <summary>What the terminal asked for reaches the deployment as the request body, which is the whole agreement.</summary>
    [Fact]
    public async Task Run_ApplyAndFoldersNamed_SendsThemAsTheTermsOfTheRun()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: true)));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "classification",
            "run",
            "--account",
            Account,
            "--folder",
            "INBOX",
            "--folder",
            "ARCHIVE",
            "--apply",
            "--rescore",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var body = deployment.RecordedRequests
            .Single(request => request.Method == HttpMethod.Post)
            .ContentAsUtf8String();

        Assert.Contains("\"apply\": true", body, StringComparison.Ordinal);
        Assert.Contains("\"rescore\": true", body, StringComparison.Ordinal);
        Assert.Contains("\"INBOX\"", body, StringComparison.Ordinal);
        Assert.Contains("\"ARCHIVE\"", body, StringComparison.Ordinal);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("yes —", StringComparison.Ordinal));
    }

    /// <summary>Asking twice is asking once, and the operator is told the terms they sent were not applied.</summary>
    [Fact]
    public async Task Run_ARunAlreadyUnderWay_SaysNothingWasStartedAndThatItsOwnTermsStand()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: false, acting: false)));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "classification",
            "run",
            "--account",
            Account,
            "--apply",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("already under way", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("were not applied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_NoAccountNamed_IsRefusedWithoutAskingTheDeploymentForAnything()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "classification", "run", "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ClassificationRunRequestCount());
    }

    /// <summary>A reading starts nothing, which is what makes it safe to ask repeatedly while a mailbox is walked.</summary>
    [Fact]
    public async Task RunStatus_ARunUnderWay_ReportsItsProgressWithoutStartingAnything()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(runState: $$"""
            {"account":"{{Account}}","run":{{Run(ended: false, acting: false)}}}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "classification",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ClassificationRunRequestCount());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("under way", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("120 scored", StringComparison.Ordinal));
    }

    /// <summary>What a dry run found is the decision an operator is about to make, so the report names how to make it.</summary>
    [Fact]
    public async Task RunStatus_ADryRunThatFoundJunk_SaysNothingChangedAndHowToCarryItOut()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(runState: $$"""
            {"account":"{{Account}}","run":{{Run(ended: true, acting: false)}}}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "classification",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("would be acted on", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Nothing has been changed on the mail server", StringComparison.Ordinal));
    }

    /// <summary>An account nobody has asked for a run is an answer rather than an error, and names the command that asks.</summary>
    [Fact]
    public async Task RunStatus_AnAccountNobodyHasAskedForARun_SaysSoAndNamesTheCommandThatStartsOne()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(runState: $$"""
            {"account":"{{Account}}","run":null}
            """);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "classification",
            "run-status",
            "--account",
            Account,
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("classification run --account work", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_OneCategoryNamed_SendsItAsTheTermOfTheRun()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false)));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "classification",
            "run",
            "--account",
            Account,
            "--category",
            "Project Heron",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            "\"category\": \"Project Heron\"",
            deployment.RecordedRequests.Single(request => request.Method == HttpMethod.Post).ContentAsUtf8String(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunStatus_ARunOverBothHalves_ReportsTheCountsOfEachHalf()
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(runState: $$"""
            {"account":"{{Account}}","run":{{Run(ended: false, acting: false)}}}
            """);

        // Act
        await this.RunAsync(deployment, "classification", "run-status", "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("120 scored", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("30 asked, 90 already current, 3 unanswered, 5 cleared as spam", StringComparison.Ordinal));
    }

    /// <summary>Removed without a deprecation window, so the old words reach nothing rather than something else.</summary>
    [Theory]
    [InlineData("run")]
    [InlineData("run-status")]
    public async Task Spam_TheRunCommandsThatWereReplaced_DoNotExist(string verb)
    {
        // Arrange
        using var deployment = FakeSpamRoutes.Answering(
            runStart: (HttpStatusCode.OK, RunStart(started: true, acting: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "spam", verb, "--account", Account, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ClassificationRunRequestCount());
    }

    private static string RunStart(bool started, bool acting, string? category = null) => $$"""
        {"started":{{(started ? "true" : "false")}},"run":{{Run(ended: false, acting, category)}}}
        """;

    private static string Run(bool ended, bool acting, string? category = null) => $$"""
        {
          "requestedAt": "2026-08-12T11:00:00+00:00",
          "spamFolders": ["INBOX"],
          "categoryFolders": ["INBOX", "ARCHIVE"],
          "category": {{(category is null ? "null" : $"\"{category}\"")}},
          "posture": "{{(acting ? "Acting" : "DryRun")}}",
          "rescores": false,
          "profile": "a1b2c3d4e5f6",
          "classifiedEmailCount": 120,
          "spamEmailCount": 7,
          "undeterminedEmailCount": 2,
          "skippedEmailCount": 4,
          "unclassifiableEmailCount": 1,
          "actedEmailCount": 7,
          "categorizedEmailCount": 30,
          "categoriesCurrentEmailCount": 90,
          "categoriesUnansweredEmailCount": 3,
          "categoriesClearedEmailCount": 5,
          "endedAt": {{(ended ? "\"2026-08-12T11:30:00+00:00\"" : "null")}},
          "ending": {{(ended ? "\"Completed\"" : "null")}}
        }
        """;

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
