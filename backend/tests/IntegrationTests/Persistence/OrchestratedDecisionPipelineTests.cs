// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Application.Chat;
using MailFathom.Infrastructure.Observability;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the decision pipeline as a deployment composes it, against the real period ledger and the real resilience pipelines.</summary>
/// <remarks>
/// <para>
/// Free: the endpoint answers from a script and nothing reaches a provider. What is real is everything above it — the
/// keyed registration a function resolves, the run's ceilings, the charge to the deployment's period row in PostgreSQL,
/// the span, and the <c>AiProviderInvocation</c> pipeline that repeats a transient failure. Each of those is unit tested
/// over a substitute; what only this suite can show is that they agree with each other once composed: that a decision's
/// tokens land in the same row a question's do, that a rate limit is repeated by the pipeline before it is classified,
/// and that a failure charges nothing.
/// </para>
/// <para>
/// Each test declares a model under an alias of its own, because the resilience pipeline's circuit is keyed by alias
/// and one test's failures must not open another's.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedDecisionPipelineTests(MailFathomOrchestrationFixture orchestration)
{
    private const string AnsweredBody = """
        {
          "id": "dec-1",
          "model": "vendor/decider-2026-10-01",
          "answers": { "spam": { "type": "noul", "noul": 0.97 } },
          "usage": { "input_tokens": 412, "output_tokens": 0, "cost": 0.0000041 }
        }
        """;

    // Both namespaces publish a ChatMessage, so the framework's is qualified rather than imported.
    private static readonly Microsoft.Extensions.AI.ChatMessage[] Input =
        [new(Microsoft.Extensions.AI.ChatRole.User, "Your parcel is waiting, pay the fee here.")];

    private static readonly DecisionQuestion[] Questions = [DecisionQuestion.YesNo("spam", "Is this unsolicited bulk or deceptive mail?")];

    /// <summary>A decision's tokens are charged to the period row every question is, so a deployment's answering spend includes them.</summary>
    [Fact]
    public async Task DecideAsync_AnAnsweredDecision_ChargesItsTokensToTheDeploymentsPeriodRow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var endpoint = new ScriptedDecisionEndpoint(Plan("decider-charged"), HttpStatusCode.OK, AnsweredBody);
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken, decisionEndpoint: endpoint);
        var tracker = await services.InScopeAsync((scope, _) => Task.FromResult(scope.GetRequiredService<MailAnsweringSpendTracker>()), cancellationToken);

        // A charge of nothing adds nothing and reads the row back, so the figure below is what the decision added rather
        // than what an earlier class left in the current period.
        await tracker.RecordSpendAsync(new ChatTokenUsage(0, 0), cancellationToken);
        var before = tracker.Read().Tokens;

        // Act
        var response = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredKeyedService<IDecisionClient>(DecisionFunction.SpamClassification)
                .DecideAsync(Input, Questions, cancellationToken: token),
            cancellationToken);

        // Assert
        Assert.Equal(DecisionQuestion.Yes, response.AnswerTo("spam").Value);
        Assert.Equal(412, response.Usage?.InputTokenCount);
        Assert.Equal(1, endpoint.Requests);
        Assert.Equal(before + 412, tracker.Read().Tokens);
    }

    /// <summary>
    /// A rate limit leaves the adapter as the raw status so the pipeline can repeat it, and only the attempt it ended on
    /// is classified — which is the arrangement a substitute runner cannot show, because it repeats nothing.
    /// </summary>
    [Fact]
    public async Task DecideAsync_ARateLimitedModel_IsRepeatedByThePipelineThenClassifiedAndChargesNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var endpoint = new ScriptedDecisionEndpoint(Plan("decider-limited"), HttpStatusCode.TooManyRequests, "{}");
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken, decisionEndpoint: endpoint);
        var tracker = await services.InScopeAsync((scope, _) => Task.FromResult(scope.GetRequiredService<MailAnsweringSpendTracker>()), cancellationToken);

        await tracker.RecordSpendAsync(new ChatTokenUsage(0, 0), cancellationToken);
        var before = tracker.Read().Tokens;

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(() => services.InScopeAsync(
            (scope, token) => scope.GetRequiredKeyedService<IDecisionClient>(DecisionFunction.SpamClassification)
                .DecideAsync(Input, Questions, cancellationToken: token),
            cancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.RateLimited, failure.Failure);
        Assert.Equal("decider-limited", failure.ModelAlias);
        Assert.True(endpoint.Requests > 1, $"The pipeline sent {endpoint.Requests} request(s); a rate limit is worth repeating.");
        Assert.Equal(before, tracker.Read().Tokens);
    }

    /// <summary>
    /// An answer that is not the contract's shape is the model having answered, so repeating it would pay for the same
    /// answer again — and the tokens it was billed are charged to the period row exactly as a readable answer's are.
    /// </summary>
    [Fact]
    public async Task DecideAsync_AnUnreadableAnswer_IsNotRepeatedAndChargesWhatItWasBilled()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var endpoint = new ScriptedDecisionEndpoint(
            Plan("decider-unreadable"),
            HttpStatusCode.OK,
            """{ "model": "vendor/decider-2026-10-01", "answers": { "spam": { "type": "noul", "noul": 7 } }, "usage": { "input_tokens": 9, "output_tokens": 0 } }""");
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken, decisionEndpoint: endpoint);
        var tracker = await services.InScopeAsync((scope, _) => Task.FromResult(scope.GetRequiredService<MailAnsweringSpendTracker>()), cancellationToken);

        await tracker.RecordSpendAsync(new ChatTokenUsage(0, 0), cancellationToken);
        var before = tracker.Read().Tokens;

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(() => services.InScopeAsync(
            (scope, token) => scope.GetRequiredKeyedService<IDecisionClient>(DecisionFunction.SpamClassification)
                .DecideAsync(Input, Questions, cancellationToken: token),
            cancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.AnswerUnreadable, failure.Failure);
        Assert.Equal(1, endpoint.Requests);
        Assert.Equal(before + 9, tracker.Read().Tokens);
    }

    private static DecisionModelPlan Plan(string alias) =>
        new(alias, new Uri("https://decisions.example.test"), "vendor/decider-2026-10-01", TimeSpan.FromSeconds(30), 32_000);
}
