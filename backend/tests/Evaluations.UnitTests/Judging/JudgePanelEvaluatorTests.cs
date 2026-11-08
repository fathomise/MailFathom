// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Net;
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

    [Fact]
    public async Task EvaluateAsync_AnAnswerNoJurorCouldRate_RecordsItAsNotRatedAndNeverAsksTheArbiter()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("5");
        using var arbiter = Judge("5");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)), response: new ChatResponse([]));

        // Assert
        Assert.Null(Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal(JudgePanelEvaluator.NotRated, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal(("not rated", "not rated", false), (rating.Metadata?["judge-juror-1"], rating.Metadata?["judge-juror-2"], rating.Metadata!.ContainsKey("judge-arbiter")));
        Assert.Equal((0, 0, 0), (first.Requests, second.Requests, arbiter.Requests));
    }

    [Fact]
    public async Task EvaluateAsync_AnArbiterThatRefuses_KeepsTheLowestJurorRatingAndAsksItAgainNextRun()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("3");
        using var arbiter = new ScriptedChatClient(string.Empty, new ChatClientMetadata("planted-provider", JudgeAddress, "planted-model"), ChatFinishReason.ContentFilter);

        await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)), executionName: "first");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)), executionName: "second");

        // Assert
        Assert.Equal(3, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal(JudgePanelEvaluator.ArbiterDidNotRate, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal(("juror 2", "did not answer"), (rating.Metadata?[JudgePanelEvaluator.DecidedByMetadata], rating.Metadata?["judge-arbiter"]));
        Assert.Contains(rating.Diagnostics ?? [], static diagnostic => diagnostic.Message == "arbiter: The judge (arbiter) refused to answer.");
        Assert.Equal((1, 1, 2), (first.Requests, second.Requests, arbiter.Requests));
    }

    [Fact]
    public async Task EvaluateAsync_AJurorThatRefuses_EscalatesToTheArbiterAndAsksTheJurorAgainNextRun()
    {
        // Arrange
        using var first = new ScriptedChatClient(string.Empty, new ChatClientMetadata("planted-provider", JudgeAddress, "planted-model"), ChatFinishReason.ContentFilter);
        using var second = Judge("5");
        using var arbiter = Judge("5");

        await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)), executionName: "first");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)), executionName: "second");

        // Assert
        Assert.Equal(5, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal($"{JudgePanelEvaluator.Escalated} ({JudgePanelEvaluator.JurorDidNotAnswer})", rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Contains(rating.Diagnostics ?? [], static diagnostic => diagnostic.Message == "juror 1: The judge (juror 1) refused to answer.");
        Assert.Equal((2, 1), (first.Requests, arbiter.Requests));
    }

    [Fact]
    public async Task EvaluateAsync_AJurorThatDoesNotAnswer_EscalatesWhatItLeftUndecidedToTheArbiter()
    {
        // Arrange
        using var first = Judge("5");
        using var second = new SilentJudge();
        using var arbiter = Judge("4");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Equal(4, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal($"{JudgePanelEvaluator.Escalated} ({JudgePanelEvaluator.JurorDidNotAnswer})", rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal(("5", "did not answer", "4"), (rating.Metadata?["judge-juror-1"], rating.Metadata?["judge-juror-2"], rating.Metadata?["judge-arbiter"]));
        Assert.Contains(
            rating.Diagnostics ?? [],
            static diagnostic => diagnostic.Severity is EvaluationDiagnosticSeverity.Error
                && diagnostic.Message == "juror 2: The judge (juror 2) did not answer (HttpRequestException 429).");
    }

    [Fact]
    public async Task EvaluateAsync_NoJurorAnswering_KeepsTheArbitersRating()
    {
        // Arrange
        using var first = new SilentJudge();
        using var second = new SilentJudge();
        using var arbiter = Judge("5");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Equal(5, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal($"{JudgePanelEvaluator.Escalated} ({JudgePanelEvaluator.NoJurorAnswered})", rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
    }

    [Fact]
    public async Task EvaluateAsync_NoJurorAnsweringAndNoArbiter_RecordsItAsNotRatedNamingTheFailure()
    {
        // Arrange
        using var first = new SilentJudge();
        using var second = new SilentJudge();

        // Act
        var rating = await this.RateAsync(JurorsAlone(), [first, second], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Null(Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal($"{JudgePanelEvaluator.NotRated} ({JudgePanelEvaluator.NoJurorAnswered})", rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal(
            "The judge (juror 1) did not answer (HttpRequestException 429).",
            rating.Diagnostics?.First(static diagnostic => diagnostic.Severity is EvaluationDiagnosticSeverity.Error).Message);
    }

    [Fact]
    public async Task EvaluateAsync_NoJudgeAnsweringAtAll_RecordsItAsNotRated()
    {
        // Arrange
        using var first = new SilentJudge();
        using var second = new SilentJudge();
        using var arbiter = new SilentJudge();

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Null(Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal($"{JudgePanelEvaluator.NotRated} ({JudgePanelEvaluator.NoJurorAnswered})", rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Equal("did not answer", rating.Metadata?["judge-arbiter"]);
    }

    [Fact]
    public async Task EvaluateAsync_AnArbiterThatDoesNotAnswer_KeepsTheLowestJurorRating()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("3");
        using var arbiter = new SilentJudge();

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Equal(3, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal(JudgePanelEvaluator.ArbiterDidNotRate, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
        Assert.Contains(rating.Diagnostics ?? [], static diagnostic => diagnostic.Message == "arbiter: The judge (arbiter) did not answer (HttpRequestException 429).");
    }

    [Fact]
    public async Task EvaluateAsync_AnArbiterAnsweringWithoutARating_KeepsTheLowestJurorRating()
    {
        // Arrange
        using var first = Judge("5");
        using var second = Judge("3");
        using var arbiter = Judge("no score here");

        // Act
        var rating = await this.RateAsync(WithArbiter(), [first, second, arbiter], new JudgeThresholds((RatingName, 4)));

        // Assert
        Assert.Equal(3, Assert.IsType<NumericMetric>(rating).Value);
        Assert.Equal(JudgePanelEvaluator.ArbiterDidNotRate, rating.Metadata?[JudgePanelEvaluator.EscalationMetadata]);
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
        using var panelClient = panel.Present([first, second, arbiter]);
        using var asking = JudgePanelChatClient.Asking(panelClient, panel.Jurors[1]);

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
        using var panelClient = JurorsAlone().Present([first, second]);

        // Act
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => panelClient.GetResponseAsync("Rate.", new ChatOptions(), TestContext.Current.CancellationToken));

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
        string executionName = "only",
        ChatResponse? response = null)
    {
        using var panelClient = panel.Present(judges);
        var reporting = EvaluationStore.OpenAt(this.store.FullName, executionName, panelClient, [new RatingEvaluator()]);

        await using var run = await reporting.CreateScenarioRunAsync(Scenario, cancellationToken: TestContext.Current.CancellationToken);
        var verdict = await run.EvaluateAsync(
            [new ChatMessage(ChatRole.User, "Answer the question.")],
            response ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, "An answer.")),
            thresholds is null ? null : [thresholds],
            TestContext.Current.CancellationToken);

        return verdict.Metrics[RatingName];
    }

    /// <summary>Asks the judge for a rating and reads its answer as a number or as a yes or no, as the quality evaluators do with theirs, refusing an empty response before asking as they do too.</summary>
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
            if (modelResponse.Messages.Count is 0)
            {
                var refusal = new NumericMetric(RatingName);
                refusal.AddDiagnostics(EvaluationDiagnostic.Error("The response held no messages."));

                return new EvaluationResult(refusal);
            }

            var answer = await chatConfiguration!.ChatClient.GetResponseAsync(
                "Rate the answer.",
                new ChatOptions { MaxOutputTokens = 800 },
                cancellationToken);
            var reason = $"rated {answer.Text}";

            EvaluationMetric rating = bool.TryParse(answer.Text, out var held)
                ? new BooleanMetric(RatingName, held, reason)
                : new NumericMetric(
                    RatingName,
                    double.TryParse(answer.Text, CultureInfo.InvariantCulture, out var score) ? score : null,
                    reason);

            return new EvaluationResult(rating);
        }
    }

    /// <summary>A judge whose provider never answers, failing every request the way a provider still rate-limiting after every retry does.</summary>
    private sealed class SilentJudge : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ChatResponse>(
                new HttpRequestException("planted-model at planted-judge-host.invalid is rate limited", inner: null, HttpStatusCode.TooManyRequests));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
