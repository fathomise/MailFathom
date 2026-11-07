// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Xunit;

namespace MailFathom.Evaluations.SpamClassification;

/// <summary>Measures the spam model stage under every declared decision model, against the labelled messages.</summary>
/// <remarks>
/// <para>
/// Each message is a row of its own, so the report and the run's pass rate show which message a model miscalled, and the
/// whole set is measured once more beside them for the figures only the set carries: the false-positive rate on legitimate
/// mail and each kind's precision and recall. A row is a theory over the cases rather than over the models for the reason
/// <c>DiscoveryPlanningEvaluations</c> gives.
/// </para>
/// <para>
/// In both, the models and each model's repetitions are measured at the same time and every shortfall is collected before
/// the test fails, as <see cref="RelevanceFilter.RelevanceFilterEvaluations" /> does. No judge is declared, read, or
/// opened, so the run's only provider calls are the decision model's own.
/// </para>
/// </remarks>
public sealed class SpamClassificationEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    /// <summary>Gets every case, by the name it is filed under.</summary>
    public static TheoryData<string> Cases { get; } = new(SpamClassificationCase.All.Select(static scenario => scenario.Name));

    [Theory(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    [MemberData(nameof(Cases))]
    public async Task DecideAsync_ALabelledMessage_EveryDeclaredDecisionModelRecordsItsLabelAndObeysNothingInIt(string caseName)
    {
        // Arrange
        var scenario = SpamClassificationCase.Named(caseName);

        // Act
        var shortfalls = await MeasureEveryDeclaredModelAsync(
            SpamClassificationScenario.NameOf(scenario),
            SpamClassificationScenario.CaseEvaluators,
            (reporting, model, plan, repetition, modelSpend) => SpamClassificationScenario.RunCaseAsync(
                reporting,
                model,
                plan,
                scenario,
                repetition,
                modelSpend,
                TestContext.Current.CancellationToken));

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls);
    }

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task DecideAsync_LabelledMail_EveryDeclaredDecisionModelNamesEachKindAndLeavesLegitimateMailAlone()
    {
        // Act
        var shortfalls = await MeasureEveryDeclaredModelAsync(
            SpamClassificationScenario.Name,
            SpamClassificationScenario.Evaluators,
            (reporting, model, plan, repetition, modelSpend) => SpamClassificationScenario.RunAsync(
                reporting,
                model,
                plan,
                repetition,
                modelSpend,
                TestContext.Current.CancellationToken));

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls);
    }

    /// <summary>Measures every declared decision model at the same time, and collects what each fell short on.</summary>
    private static async Task<IEnumerable<string>> MeasureEveryDeclaredModelAsync(
        string scenarioName,
        IReadOnlyList<IEvaluator> evaluators,
        Func<ReportingConfiguration, IDecisionClient, DecisionModelPlan, int, SpendMeter, Task<EvaluationResult>> run)
    {
        var declaration = EvaluationDeclaration.Read();
        var apiKey = DecisionEndpoint.ApiKey();

        var shortfalls = await Task.WhenAll(
            [.. declaration.DecisionModelsFor(DecisionFunction.SpamClassification).Select(plan => MeasureAsync(scenarioName, evaluators, run, plan, apiKey, declaration.Repetitions))]);

        return shortfalls.SelectMany(static modelShortfalls => modelShortfalls);
    }

    /// <summary>Measures one model, each repetition over a client and a meter of its own, which is what lets the models and the repetitions run at once.</summary>
    private static Task<IReadOnlyList<string>> MeasureAsync(
        string scenarioName,
        IReadOnlyList<IEvaluator> evaluators,
        Func<ReportingConfiguration, IDecisionClient, DecisionModelPlan, int, SpendMeter, Task<EvaluationResult>> run,
        DecisionModelPlan plan,
        string apiKey,
        int repetitions)
    {
        var reporting = EvaluationStore.OpenUnjudged(evaluators);

        return EvaluationRepetitions.MeasureAsync(
            reporting,
            scenarioName,
            plan.Alias,
            repetitions,
            async repetition =>
            {
                var modelSpend = new SpendMeter();

                using var model = ProviderDecisionClient.Open(plan, apiKey, modelSpend);

                return [.. EvaluationMetrics.ShortfallsOf(await run(reporting, model, plan, repetition, modelSpend))];
            },
            TestContext.Current.CancellationToken);
    }
}
