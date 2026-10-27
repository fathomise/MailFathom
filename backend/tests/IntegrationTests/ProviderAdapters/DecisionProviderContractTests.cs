// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using xRetry.v3;
using Xunit;

namespace MailFathom.IntegrationTests.ProviderAdapters;

/// <summary>
/// Proves the decision adapter against OpenRouter's real Decisions API: that it speaks the contract, authenticates,
/// classifies a real refusal, and reads a real answer into the shape the port publishes.
/// </summary>
/// <remarks>
/// <para>
/// Two tests, for the reason the chat and embedding adapters have two: those four claims are what only a real provider
/// can establish, and everything else — the request's composition, the strict reading, the budget, the charge, the
/// telemetry — is proved for nothing in the unit suite and against the orchestrated database by
/// <c>OrchestratedDecisionPipelineTests</c>. The contract is alpha and carries no stability commitment, which is the
/// further reason a real answer has to be read here: a shape the provider changed reaches this test as an unreadable
/// answer before it reaches a deployment as a stage that quietly classifies nothing.
/// </para>
/// <para>
/// The request carries the routing block every deployment's does, so the model a run names has to have an endpoint
/// that keeps nothing; one without is refused by the router, and that refusal is the finding rather than a flake.
/// </para>
/// <para>
/// Skipped unless somebody asks, through the one switch every provider-calling test shares, and retried for the
/// reasons <see cref="EmbeddingProviderContractTests" /> gives. See <see cref="AiProviderContractRun" />.
/// </para>
/// </remarks>
public sealed class DecisionProviderContractTests
{
    /// <summary>How many times a test is run before its failure is reported.</summary>
    private const int MaxAttempts = 3;

    /// <summary>How long to wait before running a test again.</summary>
    private const int DelayBetweenAttemptsMs = 5000;

    private static readonly ChatMessage[] Input =
        [new(ChatRole.User, "Congratulations! You have won a prize. Send your bank details to claim it today.")];

    private static readonly DecisionQuestion[] Questions =
    [
        DecisionQuestion.YesNo("spam", "Is this message unsolicited bulk or deceptive mail?"),
        DecisionQuestion.Choice(
            "kind",
            "What kind of message is it?",
            [new("phishing", "It asks for credentials, payment, or personal details."), new("ordinary", "It is ordinary correspondence.")]),
    ];

    /// <summary>Gets whether a provider-contract run was explicitly asked for.</summary>
    /// <remarks>Public and static because that is the shape xUnit reads a skip condition from.</remarks>
    public static bool ProviderContractTestsRequested => AiProviderContractRun.Requested;

    [RetryFact(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiProviderContractRun.SkipReason,
        SkipUnless = nameof(ProviderContractTestsRequested))]
    public async Task DecideAsync_AgainstTheRealProvider_ReadsAnAnswerToEveryQuestionAndTheTokensItCost()
    {
        // Arrange
        var plan = DecisionProviderContractSettings.Plan();
        using var composition = Compose(DecisionProviderContractSettings.ApiKey());
        using var adapter = AdapterOver(plan, composition);
        using var client = ClientOver(adapter, plan, composition);

        // Act
        var response = await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        // What the model decided is never asserted, only that it decided in the contract's shape. A test expecting a
        // verdict would fail on a model that judged differently, and judging is what the evaluation suite measures.
        Assert.All(response.Answers, answer =>
        {
            Assert.True(answer.IsAnswered, $"Question '{answer.QuestionId}' came back unanswered.");
            Assert.Contains(answer.Value!, answer.Probabilities.Keys);
            Assert.InRange(answer.Probabilities.Values.Sum(), 0.99, 1.01);
        });
        Assert.Equal(["spam", "kind"], response.Answers.Select(answer => answer.QuestionId));
        Assert.False(string.IsNullOrWhiteSpace(response.ModelId));

        // Tokens are what a decision is charged to the period in, so a provider that reports none is worth knowing
        // about here rather than when a deployment's ceiling quietly stops counting.
        Assert.NotNull(response.Usage);
        Assert.True(response.Usage.InputTokenCount > 0);
    }

    /// <summary>The classification has to match what the provider actually answers to a key it does not accept.</summary>
    /// <remarks>Cheap: a refused credential is answered before a model runs.</remarks>
    [RetryFact(
        MaxAttempts,
        DelayBetweenAttemptsMs,
        Skip = AiProviderContractRun.SkipReason,
        SkipUnless = nameof(ProviderContractTestsRequested))]
    public async Task DecideAsync_WithACredentialTheProviderRefuses_IsClassifiedAsSuch()
    {
        // Arrange
        var plan = DecisionProviderContractSettings.Plan();
        using var composition = Compose("mailfathom-contract-test-key-the-provider-will-refuse");
        using var adapter = AdapterOver(plan, composition);
        using var client = ClientOver(adapter, plan, composition);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.CredentialRejected, failure.Failure);
        Assert.False(failure.IsWorthRepeating);
    }

    private static ServiceProvider Compose(string apiKey)
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IProviderEndpointCredentialSource>(new FixedDecisionCredentialSource(apiKey));
        services.AddLogging();
        services.AddSingleton<IAiProviderHealthRecorder>(provider => new AiProviderHealthTracker(
            TimeProvider.System,
            provider.GetRequiredService<ILogger<AiProviderHealthTracker>>()));

        return services.BuildServiceProvider();
    }

    private static OpenRouterDecisionClient AdapterOver(DecisionModelPlan plan, IServiceProvider composition) =>
        new(
            plan,
            composition.GetRequiredService<IProviderEndpointCredentialSource>(),
            composition.GetRequiredService<IHttpClientFactory>());

    /// <summary>Puts the adapter under the resilience middleware, with the budget passed straight through.</summary>
    /// <remarks>
    /// The middleware rather than the bare adapter, because the adapter classifies nothing — a refused status leaves it
    /// raw — and the classification is one of the claims. The pass-through runner is for the reason the chat contract
    /// gives: a real pipeline would turn a refused credential into several paid attempts of the same refusal.
    /// </remarks>
    private static ResilientDecisionClient ClientOver(
        OpenRouterDecisionClient adapter,
        DecisionModelPlan plan,
        IServiceProvider composition) =>
        new(
            adapter,
            plan,
            new PassThroughOutboundOperationRunner(),
            composition.GetRequiredService<IAiProviderHealthRecorder>(),
            NullLogger.Instance);

    private sealed class FixedDecisionCredentialSource(string apiKey) : IProviderEndpointCredentialSource
    {
        public Task<ProviderEndpointCredential> ResolveAsync(string endpointAlias, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderEndpointCredential.FromApiKey(apiKey, resolvedMaterial: null));
    }

    private sealed class PassThroughOutboundOperationRunner : IOutboundOperationRunner
    {
        public Task<TResult> RunAsync<TResult>(
            OutboundDependency dependency,
            string remoteInstance,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
