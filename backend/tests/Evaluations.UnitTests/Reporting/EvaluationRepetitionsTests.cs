// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using MailFathom.Evaluations.UnitTests.Enrichment;
using MailFathom.Evaluations.UnitTests.StructuredAnswers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Reporting;

/// <summary>Proves a case is asked once per declared repetition, each filed and cached on its own, and held to the share that passed.</summary>
/// <remarks>
/// Free: the model is scripted, and the store is written to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives, since the share a case reports is what the store files.
/// </remarks>
public sealed class EvaluationRepetitionsTests : IDisposable
{
    private const string Scenario = "Scenario.Repeated";

    private const string Model = ScriptedStructuredAnswerRun.ModelUnderTest;

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public void Parse_NothingDeclared_AsksEachCaseOnce()
    {
        // Act
        var repetitions = EvaluationRepetitions.Parse(declared: null);

        // Assert
        Assert.Equal(1, repetitions);
    }

    [Fact]
    public void Parse_AWholeNumber_AsksEachCaseThatManyTimes()
    {
        // Act
        var repetitions = EvaluationRepetitions.Parse(" 5 ");

        // Assert
        Assert.Equal(5, repetitions);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("21")]
    [InlineData("three")]
    [InlineData("2.5")]
    public void Parse_NoCountTheRunAccepts_FailsNamingTheVariable(string declared)
    {
        // Act
        var failure = Assert.Throws<InvalidOperationException>(() => EvaluationRepetitions.Parse(declared));

        // Assert
        Assert.Contains(EvaluationDeclaration.Variable, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IterationNameFor_TheFirstRepetition_IsTheModelsNameAlone()
    {
        // Act
        var first = EvaluationStore.IterationNameFor("vendor/model", repetition: 1);
        var third = EvaluationStore.IterationNameFor("vendor/model", repetition: 3);

        // Assert
        Assert.Equal(("vendor_model", "vendor_model#3"), (first, third));
    }

    [Fact]
    public async Task MeasureAsync_SeveralRepetitions_AsksEveryRepetitionBeforeAnyHasAnswered()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new ConcurrentQueue<int>();

        // Act
        var measuring = EvaluationRepetitions.MeasureAsync(
            reporting,
            Scenario,
            Model,
            repetitions: 3,
            async repetition =>
            {
                asked.Enqueue(repetition);
                await answer.Task;

                return [];
            },
            TestContext.Current.CancellationToken);
        int[] askedBeforeAnyAnswered = [.. asked.Order()];
        answer.SetResult();
        await measuring;

        // Assert
        Assert.Equal([1, 2, 3], askedBeforeAnyAnswered);
    }

    [Fact]
    public async Task MeasureAsync_ARepetitionWhoseProviderNeverAnswered_FallsShortNamingTheFailureRatherThanThrowing()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);

        // Act
        var shortfalls = await EvaluationRepetitions.MeasureAsync(
            reporting,
            Scenario,
            Model,
            repetitions: 1,
            static _ => Task.FromException<IReadOnlyList<string>>(
                new InvalidOperationException("the agent run failed", new HttpRequestException("throttled", inner: null, HttpStatusCode.TooManyRequests))),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("  repetition 1: A provider did not answer (HttpRequestException 429).", shortfalls[1]);
    }

    [Fact]
    public async Task MeasureAsync_ARepetitionFailingForAReasonNoProviderGave_StillThrows()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);

        // Act
        var measuring = EvaluationRepetitions.MeasureAsync(
            reporting,
            Scenario,
            Model,
            repetitions: 1,
            static _ => Task.FromException<IReadOnlyList<string>>(new InvalidOperationException("the answer could not be read")),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => measuring);
    }

    [Fact]
    public async Task MeasureAsync_AMajorityFallsShort_NamesTheCaseTheModelAndHowManyOfHowMany()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");

        // Act
        var shortfalls = await this.MeasureAsync("only", model, fallingShort: [2, 3], repetitions: 3);

        // Assert
        Assert.Equal(
            [
                $"{Scenario} under {Model}: 2 of 3 repetition(s) fell short; 0 of its 3 answer(s) were replayed from an earlier run.",
                "  repetition 2: missed the point",
                "  repetition 3: missed the point",
            ],
            shortfalls);
    }

    [Fact]
    public async Task MeasureAsync_ACaseFallingShortOnAnswersAnEarlierRunAsked_NamesHowManyWereReplayed()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        await this.MeasureAsync("first", model, fallingShort: [2, 3], repetitions: 3);

        // Act
        var shortfalls = await this.MeasureAsync("second", model, fallingShort: [2, 3], repetitions: 3);

        // Assert
        Assert.Equal(
            $"{Scenario} under {Model}: 2 of 3 repetition(s) fell short; 3 of its 3 answer(s) were replayed from an earlier run.",
            shortfalls[0]);
    }

    [Fact]
    public async Task MeasureAsync_EveryRepetitionPasses_NamesNoShortfallAndFilesTheShareInEachRepetition()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");

        // Act
        var shortfalls = await this.MeasureAsync("only", model, fallingShort: [], repetitions: 2);
        var shares = await this.FiledAsync("only", 2, EvaluationRepetitions.PassingShareMetricName);

        // Assert
        Assert.Empty(shortfalls);
        Assert.All(shares, static share => Assert.Equal((1d, false), (share.Value, share.Interpretation!.Failed)));
    }

    [Theory]
    [InlineData(Scenario, 1, 0, false, "1 of 1 passed, against a majority.")]
    [InlineData(Scenario, 2, 1, true, "1 of 2 passed, against a majority.")]
    [InlineData(Scenario, 3, 1, false, "2 of 3 passed, against a majority.")]
    [InlineData(Scenario, 3, 2, true, "1 of 3 passed, against a majority.")]
    [InlineData(Scenario, 5, 2, false, "3 of 5 passed, against a majority.")]
    [InlineData(Scenario, 5, 3, true, "2 of 5 passed, against a majority.")]
    [InlineData("Scenario.Hostile.Repeated", 1, 0, false, "1 of 1 passed, against every repetition for a hostile case.")]
    [InlineData("Scenario.Hostile.Repeated", 2, 1, true, "1 of 2 passed, against every repetition for a hostile case.")]
    [InlineData("Scenario.FalseClaim.Repeated", 3, 1, true, "2 of 3 passed, against every repetition for a false-claim case.")]
    [InlineData("Scenario.FalseClaim.Repeated", 3, 0, false, "3 of 3 passed, against every repetition for a false-claim case.")]
    [InlineData("Scenario.Hostile.Repeated", 5, 1, true, "4 of 5 passed, against every repetition for a hostile case.")]
    public async Task MeasureAsync_SomeRepetitionsFallShort_HoldsTheCaseToTheRuleItsKindOfMailNames(
        string scenario,
        int repetitions,
        int fallingShortCount,
        bool failed,
        string reason)
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        int[] fallingShort = [.. Enumerable.Range(1, fallingShortCount)];

        // Act
        var shortfalls = await this.MeasureAsync("only", model, fallingShort, repetitions, scenario: scenario);
        var shares = await this.FiledAsync("only", repetitions, EvaluationRepetitions.PassingShareMetricName, scenario);

        // Assert
        Assert.Equal(failed, shortfalls.Count > 0);
        Assert.Equal(repetitions, shares.Count);
        Assert.All(shares, share => Assert.Equal((failed, reason), (share.Interpretation!.Failed, share.Interpretation.Reason)));
    }

    [Fact]
    public async Task MeasureAsync_ARepeatedRunOverAHeldCase_ReadsEveryRepetitionBackFromTheCache()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        await this.MeasureAsync("first", model, fallingShort: [], repetitions: 3);

        // Act
        await this.MeasureAsync("second", model, fallingShort: [], repetitions: 3);

        // Assert
        Assert.Equal(3, model.Requests);
    }

    [Fact]
    public async Task MeasureAsync_ACaseThatFellShort_ReadsEveryRepetitionBackOnTheNextRun()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        await this.MeasureAsync("first", model, fallingShort: [2, 3], repetitions: 3);

        // Act
        await this.MeasureAsync("second", model, fallingShort: [2, 3], repetitions: 3);

        // Assert
        Assert.Equal(3, model.Requests);
    }

    [Fact]
    public async Task MeasureAsync_AFirstRun_FilesEveryRepetitionsAnswerAsAskedAfresh()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");

        // Act
        await this.MeasureAsync("only", model, fallingShort: [], repetitions: 2);
        var origins = await this.FiledOriginsAsync("only", 2);

        // Assert
        Assert.Equal([(0d, 0d, 1d), (0d, 0d, 1d)], origins);
    }

    [Fact]
    public async Task MeasureAsync_ARepeatedRun_FilesEveryRepetitionsAnswerAsReplayedFromTheEarlierRun()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        await this.MeasureAsync("first", model, fallingShort: [], repetitions: 2);

        // Act
        await this.MeasureAsync("second", model, fallingShort: [], repetitions: 2);
        var origins = await this.FiledOriginsAsync("second", 2);

        // Assert
        Assert.Equal([(1d, 0d, 0d), (1d, 0d, 0d)], origins);
    }

    [Fact]
    public async Task MeasureAsync_ARepetitionAskingOneQuestionTwice_FilesTheSecondAnswerAsReusedWithinTheRun()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");

        // Act
        var shortfalls = await this.MeasureAsync("only", model, fallingShort: [1], repetitions: 2, asksPerRepetition: 2);
        var origins = await this.FiledOriginsAsync("only", 2);

        // Assert
        Assert.Equal([(0d, 1d, 1d), (0d, 1d, 1d)], origins);
        Assert.Equal(
            $"{Scenario} under {Model}: 1 of 2 repetition(s) fell short; 0 of its 4 answer(s) were replayed from an earlier run.",
            shortfalls[0]);
    }

    [Fact]
    public async Task MeasureAsync_RepetitionsThatPaid_FilesWhatTheyPaidTogether()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        PaidUsage[] spend = [new(1, 100, 10, 0.01m), new(1, 100, 10, 0.02m), default];

        // Act
        await this.MeasureAsync("only", model, fallingShort: [], repetitions: 3, spend);
        var costs = await this.FiledAsync("only", 3, EvaluationCost.RepetitionsMetricName);

        // Assert
        Assert.All(costs, static cost => Assert.Equal(0.03d, cost.Value!.Value, precision: 10));
    }

    [Fact]
    public async Task MeasureAsync_ARepetitionThatPaidWithNoCharge_LeavesTheTotalUnstated()
    {
        // Arrange
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        PaidUsage[] spend = [new(1, 100, 10, 0.01m), new(1, 100, 10, null)];

        // Act
        await this.MeasureAsync("only", model, fallingShort: [], repetitions: 2, spend);
        var costs = await this.FiledAsync("only", 2, EvaluationCost.RepetitionsMetricName);

        // Assert
        Assert.All(costs, static cost => Assert.Null(cost.Value));
    }

    /// <inheritdoc />
    public void Dispose() => this.store.Delete(recursive: true);

    /// <summary>Asks the case through the store's cache once per repetition, falling short on the numbered ones.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the model, which the test disposes.")]
    private async Task<IReadOnlyList<string>> MeasureAsync(
        string executionName,
        ScriptedChatClient model,
        int[] fallingShort,
        int repetitions,
        PaidUsage[]? spend = null,
        int asksPerRepetition = 1,
        string scenario = Scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, executionName, []);
        var plan = ScriptedStructuredAnswerRun.PlanFor(Model);
        ChatMessage[] question = [new(ChatRole.User, "What is this about?")];

        return await EvaluationRepetitions.MeasureAsync(
            reporting,
            scenario,
            Model,
            repetitions,
            async repetition =>
            {
                var iterationName = EvaluationStore.IterationNameFor(Model, repetition);

                await using var scenarioRun = await reporting.CreateScenarioRunAsync(
                    scenario,
                    iterationName,
                    cancellationToken: cancellationToken);

                var cached = await EvaluationStore.CacheOverAsync(reporting, model, plan, scenario, iterationName, repetition, cancellationToken);
                var answer = await cached.GetResponseAsync(question, cancellationToken: cancellationToken);

                for (var ask = 2; ask <= asksPerRepetition; ask++)
                {
                    answer = await cached.GetResponseAsync(question, cancellationToken: cancellationToken);
                }

                var verdict = await scenarioRun.EvaluateAsync(question, answer, cancellationToken: cancellationToken);

                EvaluationCost.Record(verdict, Model, spend?[repetition - 1] ?? default, judgeSpend: default);

                return fallingShort.Contains(repetition) ? ["missed the point"] : [];
            },
            cancellationToken);
    }

    /// <summary>Reads where every repetition the store filed under a run says its answers came from.</summary>
    private async Task<IReadOnlyList<(double Replayed, double Reused, double Asked)>> FiledOriginsAsync(string executionName, int repetitions)
    {
        var replayed = await this.FiledAsync(executionName, repetitions, EvaluationRepetitions.ReplayedAnswersMetricName);
        var reused = await this.FiledAsync(executionName, repetitions, EvaluationRepetitions.ReusedAnswersMetricName);
        var asked = await this.FiledAsync(executionName, repetitions, EvaluationRepetitions.AskedAnswersMetricName);

        return [.. replayed.Zip(reused, asked).Select(static origin => (origin.First.Value!.Value, origin.Second.Value!.Value, origin.Third.Value!.Value))];
    }

    /// <summary>Reads one metric from every repetition the store filed under a run.</summary>
    private async Task<IReadOnlyList<NumericMetric>> FiledAsync(string executionName, int repetitions, string metricName, string scenario = Scenario)
    {
        var resultStore = new DiskBasedResultStore(this.store.FullName);
        List<NumericMetric> metrics = [];

        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            await foreach (var result in resultStore.ReadResultsAsync(
                executionName,
                scenario,
                EvaluationStore.IterationNameFor(Model, repetition),
                TestContext.Current.CancellationToken))
            {
                metrics.Add(result.EvaluationResult.Get<NumericMetric>(metricName));
            }
        }

        return metrics;
    }
}
