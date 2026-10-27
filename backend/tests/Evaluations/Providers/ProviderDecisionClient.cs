// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.Application.Resilience;
using MailFathom.Evaluations.Costing;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.Providers;

/// <summary>A decision model's client opened the way a deployment opens one, holding the transport it speaks over.</summary>
/// <remarks>
/// <para>
/// The adapter a deployment uses, under the resilience middleware a deployment composes over it, so the request is the
/// one a deployment sends and a refusal is classified as a deployment classifies it — which is what the spam model stage
/// reads an outcome from. What it leaves out is what <see cref="ProviderChatClient" /> leaves out: the deployment's
/// retry budget and ledgers, because a scenario is not a deployment's spend. The middleware runs each call once, and a
/// failure it calls worth repeating — a rate limit, a timeout, a transport fault — is asked again here after a jittered
/// delay, the way <see cref="TransientProviderRetryChatClient" /> asks a chat model again.
/// </para>
/// <para>
/// Every answer passes through <see cref="ProviderSpendHandler" />, beneath the run's response cache, so an answer the
/// cache served is charged nothing.
/// </para>
/// </remarks>
internal sealed class ProviderDecisionClient : DelegatingDecisionClient
{
    private readonly HttpMessageHandler transport;

    private ProviderDecisionClient(IDecisionClient innerClient, HttpMessageHandler transport)
        : base(innerClient) =>
        this.transport = transport;

    /// <summary>Opens a client over one decision model.</summary>
    /// <param name="plan">Which model is asked, where, and under which bounds.</param>
    /// <param name="apiKey">The key the provider authenticates the requests with.</param>
    /// <param name="spend">What every answer's tokens and charge are recorded against.</param>
    /// <returns>The client, which the caller disposes.</returns>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The returned client owns the metering handler and disposes it, and the handler disposes the connection it was given.")]
    public static ProviderDecisionClient Open(DecisionModelPlan plan, string apiKey, SpendMeter spend)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var transport = new ProviderSpendHandler(spend, new SocketsHttpHandler { AllowAutoRedirect = false });
        var adapter = new OpenRouterDecisionClient(plan, new RunCredentialSource(apiKey), new SharedTransportFactory(transport));
        var classified = new ResilientDecisionClient(
            adapter,
            plan,
            new SingleAttemptRunner(),
            new AiProviderHealthTracker(TimeProvider.System, NullLogger<AiProviderHealthTracker>.Instance),
            NullLogger.Instance);

        return new ProviderDecisionClient(new TransientRetryDecisionClient(classified, TimeProvider.System), transport);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.transport.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>Presents the run's key on every request, a fresh credential each time since the adapter releases each one.</summary>
    private sealed class RunCredentialSource(string apiKey) : IProviderEndpointCredentialSource
    {
        public Task<ProviderEndpointCredential> ResolveAsync(string endpointAlias, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderEndpointCredential.FromApiKey(apiKey, resolvedMaterial: null));
    }

    /// <summary>Hands out clients over the one metered handler, which outlives each of them.</summary>
    private sealed class SharedTransportFactory(HttpMessageHandler transport) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(transport, disposeHandler: false);
    }

    /// <summary>Runs an operation once, leaving retries to the run and the deadline to the resilience middleware.</summary>
    private sealed class SingleAttemptRunner : IOutboundOperationRunner
    {
        public Task<TResult> RunAsync<TResult>(
            OutboundDependency dependency,
            string remoteInstance,
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }

    /// <summary>Asks again when the classified failure says the same request may succeed later, and never otherwise.</summary>
    private sealed class TransientRetryDecisionClient(IDecisionClient innerClient, TimeProvider timeProvider)
        : DelegatingDecisionClient(innerClient)
    {
        public override async Task<DecisionResponse> DecideAsync(
            IEnumerable<ChatMessage> input,
            IReadOnlyList<DecisionQuestion> questions,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var asked = input as IReadOnlyList<ChatMessage> ?? [.. input];

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await base.DecideAsync(asked, questions, options, cancellationToken);
                }
                catch (DecisionFailedException failure) when (attempt < TransientProviderRetryChatClient.MaxAttempts && failure.IsWorthRepeating)
                {
                    await Task.Delay(
                        JitteredRetryBackoff.DelayBeforeNextAttempt(
                            TransientProviderRetryChatClient.BaseDelay,
                            TransientProviderRetryChatClient.MaxDelay,
                            minimumDelay: TimeSpan.Zero,
                            attempt),
                        timeProvider,
                        cancellationToken);
                }
            }
        }
    }
}
