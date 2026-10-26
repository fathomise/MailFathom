// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using Xunit;

namespace MailFathom.Evaluations.SpamClassification;

/// <summary>Measures the spam model stage under every declared decision model, against the labelled messages.</summary>
/// <remarks>
/// One test over the whole model list, the models and each model's repetitions measured at the same time and every
/// shortfall collected before the test fails, as <see cref="RelevanceFilter.RelevanceFilterEvaluations" /> does. No judge
/// is declared, read, or opened, so the run's only provider calls are the decision model's own.
/// </remarks>
public sealed class SpamClassificationEvaluations
{
    /// <summary>Gets whether an evaluation run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool EvaluationsRequested => AiEvaluationRun.Requested;

    [Fact(Skip = AiEvaluationRun.SkipReason, SkipUnless = nameof(EvaluationsRequested))]
    public async Task DecideAsync_LabelledMail_EveryDeclaredDecisionModelNamesEachKindAndLeavesLegitimateMailAlone()
    {
        // Arrange
        var declaration = EvaluationDeclaration.Read();
        var apiKey = DecisionEndpoint.ApiKey();

        // Act
        var shortfalls = await Task.WhenAll(
            [.. declaration.DecisionModelsFor(DecisionFunction.SpamClassification).Select(plan => MeasureAsync(plan, apiKey, declaration.Repetitions))]);

        // Assert
        AiEvaluationRun.AssertNoShortfalls(shortfalls.SelectMany(static modelShortfalls => modelShortfalls));
    }

    /// <summary>Measures one model, each repetition over a client and a meter of its own, which is what lets the models and the repetitions run at once.</summary>
    private static Task<IReadOnlyList<string>> MeasureAsync(DecisionModelPlan plan, string apiKey, int repetitions)
    {
        var reporting = EvaluationStore.OpenUnjudged(SpamClassificationScenario.Evaluators);

        return EvaluationRepetitions.MeasureAsync(
            reporting,
            SpamClassificationScenario.Name,
            plan.Alias,
            repetitions,
            async repetition =>
            {
                var modelSpend = new SpendMeter();

                using var model = ProviderDecisionClient.Open(plan, apiKey, modelSpend);

                return [.. EvaluationMetrics.ShortfallsOf(await SpamClassificationScenario.RunAsync(
                    reporting,
                    model,
                    plan,
                    repetition,
                    modelSpend,
                    TestContext.Current.CancellationToken))];
            },
            TestContext.Current.CancellationToken);
    }
}
