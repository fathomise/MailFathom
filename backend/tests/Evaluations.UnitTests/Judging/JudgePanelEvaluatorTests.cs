// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Evaluations.Judging;
using MailFathom.Evaluations.Reporting;
using MailFathom.Evaluations.UnitTests.Enrichment;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Judging;

/// <summary>Proves which judge of the panel a rating is kept from, what the kept rating records, and that each judge's verdicts are cached apart.</summary>
/// <remarks>
/// Free: every judge is scripted, and each scenario runs through a real store written to a temporary directory, because
/// the panel names a judge on a request beneath the run's own response cache and only that cache can show the names
/// keep the verdicts apart.
/// </remarks>
public sealed class JudgePanelEvaluatorTests : IDisposable
{
    private const string Scenario = "Scenario.Panel";
    private const string RatingName = "Rating";
    private const string ApiKey = "planted-judge-key";

    private static readonly Uri JudgeAddress = new("https://planted-judge-host.invalid/v1/");

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task EvaluateAsync_EveryJurorPasses_KeepsTheLowestRatingAndNeverAsksTheArbiter()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("4");
        using var arbiter = Judge("1");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Equal(4, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal(JudgePanelEvaluator.NotNeeded, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal("juror 2", rating.Metadata?[JudgePanelEvaluator.DecidedByMetadata]);
        Assert.Equal(("5", "4", false), (rating.Metadata?["judge-juror-1"], rating.Metadata?["judge-juror-2"], rating.Metadata!.ContainsKey("judge-arbiter")));
        Assert.Equal(0, arbiter.Requests);
    }

    [Fact]
    public async Task EvaluateAsync_AJurorBelowTheThreshold_KeepsTheArbitersRatingAndRecordsEveryJudgesAnswer()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("3");
        using var arbiter = Judge("4");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Equal(4, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal(JudgePanelEvaluator.Escalated, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal("arbiter", rating.Metadata?[JudgePanelEvaluator.DecidedByMetadata]);
        Assert.Equal(("5", "3", "4"), (rating.Metadata?["judge-juror-1"], rating.Metadata?["judge-juror-2"], rating.Metadata?["judge-arbiter"]));
        Assert.Equal(
            ["juror 1 rated 5: rated 5", "juror 2 rated 3: rated 3", "arbiter rated 4: rated 4"],
            rating.Diagnostics?.Where(static diagnostic => diagnostic.Severity is EvaluationDiagnosticSeverity.Informational).Select(static diagnostic => diagnostic.Message));
    }

    [Fact]
    public async Task EvaluateAsync_AJurorBelowTheThresholdAndNoArbiter_KeepsTheLowestRatingAndRecordsTheEscalationItNeeded()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("3");

        // Act
        var rating = await this.RateAsync(JurorsAlone(), [first, second], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Equal(3, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal(JudgePanelEvaluator.NeededWithoutArbiter, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal("juror 2", rating.Metadata?[JudgePanelEvaluator.DecidedByMetadata]);
    }

    [Theory]
    [InlineData("5", "5", JudgePanelEvaluator.NotNeeded)]
    [InlineData("5", "4", JudgePanelEvaluator.Escalated)]
    [InlineData("true", "true", JudgePanelEvaluator.NotNeeded)]
    [InlineData("true", "false", JudgePanelEvaluator.Escalated)]
    public async Task EvaluateAsync_ARatingNoThresholdIsStatedFor_EscalatesExactlyWhenTheJurorsDisagreeOrOneRefuses(
        string firstAnswer,
        string secondAnswer,
        string expectedOutcome)
    {
        // Arrange
        using var first = Judge(firstAnswer);
        using var second = Judge(secondAnswer);
        using var arbiter = Judge(firstAnswer);

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], thresholds: null);

        // Assert
        Assert.Equal(expectedOutcome, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
    }

    [Fact]
    public async Task EvaluateAsync_AnArbiterDeclaredAfterAJurorOnlyRun_AsksTheArbiterAloneAndReadsEveryJurorBack()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("3");
        using var arbiter = Judge("4");

        await this.RateAsync(JurorsAlone(), [first, second], new JudgeThresholds((RatingName, 4)), executionName: "jurors");

        // Act
        await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)), executionName: "arbitrated");

        // Assert
        Assert.Equal((1, 1, 1), (first.Requests, second.Requests, arbiter.Requests));
    }

    [Fact]
    public async Task GetResponseAsync_ARequestNamingAJudge_ReachesThatJudgeWithoutCarryingTheName()
    {
        // Arrange
        var panel = WithArbiter();
        using var first = Judge("5");
        using var second = Judge("4");
        using var arbiter = Judge("3");
        using var panelJudge = panel.Present([first, second, arbiter]);
        using var asking = JudgePanelChatClient.Asking(panelJudge, panel.Jurors[1]);

        // Act
        var answer = await asking.GetResponseAsync("Rate.", new ChatOptions(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(("4", "judge (juror 2)"), (answer.Text, answer.ModelId));
        Assert.Equal((0, 1, 0), (first.Requests, second.Requests, arbiter.Requests));
        Assert.Empty(Assert.Single(second.ReceivedOptions)?.AdditionalProperties ?? []);
    }

    [Fact]
    public async Task GetResponseAsync_ARequestNamingNoJudge_IsRefusedRatherThanSentToADefaultOne()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("4");
        using var panelJudge = JurorsAlone().Present([first, second]);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => panelJudge.GetResponseAsync("Rate.", new ChatOptions(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("without naming which judge", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, first.Requests);
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static JudgePanel JurorsAlone() =>
        JudgePanel.Of(JudgeAddress, ["juror-model-a", "juror-model-b"], arbiterModel: null, ApiKey, reasoningEffort: null);

    private static JudgePanel WithArbiter() =>
        JudgePanel.Of(JudgeAddress, ["juror-model-a", "juror-model-b"], "arbiter-model", ApiKey, reasoningEffort: null);

    private static ScriptedChatClient Judge(string answer) =>
        new(answer, new ChatClientMetadata("planted-provider", JudgeAddress, "planted-model"));

    private async Task<EvaluationMetric> RateAsync(
        JudgePanel panel,
        IReadOnlyList<IChatClient> judges,
        JudgeThresholds? thresholds,
        string executionName = "only")
    {
        using var panelJudge = panel.Present(judges);
        var reporting = EvaluationStore.OpenAt(this.store.FullName, executionName, panelJudge, [new RatingEvaluator()]);

        await using var run = await reporting.CreateScenarioRunAsync(Scenario, cancellationToken: TestContext.Current.CancellationToken);
        var verdict = await run.EvaluateAsync(
            [new ChatMessage(ChatRole.User, "Answer the question.")],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "An answer.")),
            thresholds is null ? null : [thresholds],
            TestContext.Current.CancellationToken);

        return verdict.Metrics[RatingName];
    }

    /// <summary>Asks the judge for a rating and reads its answer as a number or as a yes or no, as the quality evaluators do with theirs.</summary>
    private sealed class RatingEvaluator : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames => [RatingName];

        public async ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
        {
            var answer = await chatConfiguration!.ChatClient.GetResponseAsync(
                "Rate the answer.",
                new ChatOptions { MaxOutputTokens = 800 },
                cancellationToken);
            var reason = $"rated {answer.Text}";

            EvaluationMetric rating = bool.TryParse(answer.Text, out var held)
                ? new BooleanMetric(RatingName, held, reason)
                : new NumericMetric(RatingName, double.Parse(answer.Text, CultureInfo.InvariantCulture), reason);

            return new EvaluationResult(rating);
        }
    }
}
