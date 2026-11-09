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

namespace MailFathom.Evaluations.EmailCategorization;

/// <summary>Measures categorization under every declared decision model, against the labelled messages.</summary>
/// <remarks>
/// The whole set is measured as one row, because every figure here is a rate over the set: each default's precision and
/// recall, the false matches on own categories, and what a request is counted at under each number of own categories.
/// The models and each model's repetitions are measured at the same time and every shortfall is collected before the test
/// fails, as <see cref="RelevanceFilter.RelevanceFilterEvaluations" /> does. No judge is declared, read, or opened, so the
/// run's only provider calls are the decision model's own.
/// </remarks>
public sealed class EmailCategorizationEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task JudgeAsync_LabelledMail_EveryDeclaredDecisionModelNamesEachDefaultAndMatchesOwnCategoriesAsLabelled()
    {
        // Act
        var shortfalls = await MeasureEveryDeclaredModelAsync(
            EmailCategorizationScenario.Name,
            EmailCategorizationScenario.Evaluators,
            (reporting, model, plan, repetition, modelSpend) => EmailCategorizationScenario.RunAsync(
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
            [.. declaration.DecisionModelsFor(DecisionFunction.EmailCategorization).Select(plan => MeasureAsync(scenarioName, evaluators, run, plan, apiKey, declaration.Repetitions))]);

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
