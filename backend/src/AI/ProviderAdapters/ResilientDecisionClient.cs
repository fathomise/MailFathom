// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Puts this deployment's deadline, resilience budget, failure classification, and health reporting around each decision call.</summary>
/// <remarks>
/// <para>
/// The decision counterpart of <see cref="ResilientChatClient" />, keyed by the decision model's alias so a decision
/// outage opens a circuit of its own rather than the chat provider's.
/// </para>
/// <para>
/// One difference is deliberate. A failure leaves each attempt as the transport raised it — a status, a dropped
/// connection, or the deadline as a <see cref="TimeoutException" /> — so the pipeline's own transient classification
/// decides whether to repeat it, and only the failure the attempts end on is classified into a
/// <see cref="DecisionFailedException" />. A decision has no side effect, so repeating a transient failure is safe; a
/// failure classified inside each attempt would reach the pipeline as one it does not know and never be repeated.
/// </para>
/// <para>
/// There is no fallback model, for the reason <see cref="DecisionModelPlan" /> gives.
/// </para>
/// </remarks>
internal sealed class ResilientDecisionClient : DelegatingDecisionClient
{
    private readonly DecisionModelPlan plan;
    private readonly IOutboundOperationRunner operationRunner;
    private readonly IAiProviderHealthRecorder healthRecorder;
    private readonly ILogger logger;

    /// <summary>Initializes the middleware over the client one model is asked through.</summary>
    /// <param name="innerClient">The adapter every attempt is sent through.</param>
    /// <param name="plan">The model, whose alias keys the budget and names the failure and whose timeout bounds an attempt.</param>
    /// <param name="operationRunner">Applies the provider resilience budget.</param>
    /// <param name="healthRecorder">Records what each call established about the provider.</param>
    /// <param name="logger">Records the outcome without recording any input or answer.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal ResilientDecisionClient(
        IDecisionClient innerClient,
        DecisionModelPlan plan,
        IOutboundOperationRunner operationRunner,
        IAiProviderHealthRecorder healthRecorder,
        ILogger logger)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operationRunner);
        ArgumentNullException.ThrowIfNull(healthRecorder);
        ArgumentNullException.ThrowIfNull(logger);

        this.plan = plan;
        this.operationRunner = operationRunner;
        this.healthRecorder = healthRecorder;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="DecisionFailedException">Thrown when the call produced no answers, naming which kind of failure ended it.</exception>
    public override async Task<DecisionResponse> DecideAsync(
        IEnumerable<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await this.SendUnderBudgetAsync([.. input], questions, options, cancellationToken);

            this.healthRecorder.RecordServed(AiProviderRole.Decision);

            return response;
        }
        catch (DecisionFailedException failure)
        {
            DecisionProviderEvents.LogCallFailed(this.logger, this.plan.Alias, failure.Failure);

            this.RecordFailure(failure);

            throw;
        }
    }

    /// <summary>Runs the call under the resilience budget, and classifies the failure the attempts ended on.</summary>
    private async Task<DecisionResponse> SendUnderBudgetAsync(
        IReadOnlyList<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options,
        CancellationToken cancellationToken)
    {
        try
        {
            return await this.operationRunner.RunAsync(
                OutboundDependency.AiProviderInvocation,
                this.plan.Alias,
                attemptToken => this.SendAsync(input, questions, options, attemptToken),
                cancellationToken);
        }
        catch (MailFathomException rejection) when (ChatCallFailureMapping.IsEndpointNotCalled(rejection))
        {
            // The circuit is open or the concurrency budget is spent: nothing about the request was wrong, and the model
            // is worth asking again once the pipeline allows it.
            throw new DecisionFailedException(this.plan.Alias, DecisionFailure.TransportFaulted, rejection);
        }
        catch (Exception failure) when (ProviderCallFailureClassification.Classify(failure) is { } classified)
        {
            throw new DecisionFailedException(this.plan.Alias, ToDecisionFailure(classified), failure);
        }
    }

    /// <summary>Sends one attempt under this deployment's own deadline.</summary>
    private async Task<DecisionResponse> SendAsync(
        IReadOnlyList<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options,
        CancellationToken cancellationToken)
    {
        using var attemptDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptDeadline.CancelAfter(this.plan.RequestTimeout);

        try
        {
            return await this.InnerClient.DecideAsync(input, questions, options, attemptDeadline.Token);
        }
        catch (OperationCanceledException expired) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so the deadline did. A timeout is the dependency failing to answer, which the
            // pipeline repeats; a cancellation is this system stopping the work, which it must not.
            throw new TimeoutException(
                $"Decision model '{this.plan.Alias}' did not answer within the time configured for one request.",
                expired);
        }
    }

    /// <summary>Records what the failure established about the provider, at the granularity an operator acts on.</summary>
    /// <remarks>
    /// An unreadable answer came from an endpoint that took the request, authenticated it, and ran the model, so it is a
    /// working endpoint whatever went wrong with the shape; reporting it as misconfigured would send an operator after a
    /// declaration with nothing wrong in it.
    /// </remarks>
    private void RecordFailure(DecisionFailedException failure)
    {
        if (failure.Failure is DecisionFailure.AnswerUnreadable)
        {
            this.healthRecorder.RecordServed(AiProviderRole.Decision);

            return;
        }

        if (failure.IsWorthRepeating)
        {
            this.healthRecorder.RecordUnavailable(AiProviderRole.Decision);

            return;
        }

        this.healthRecorder.RecordMisconfigured(AiProviderRole.Decision);
    }

    private static DecisionFailure ToDecisionFailure(ProviderCallFailure failure) => failure switch
    {
        ProviderCallFailure.CredentialRejected => DecisionFailure.CredentialRejected,
        ProviderCallFailure.RateLimited => DecisionFailure.RateLimited,
        ProviderCallFailure.RequestTimedOut => DecisionFailure.RequestTimedOut,
        ProviderCallFailure.RequestRefused => DecisionFailure.RequestRefused,
        _ => DecisionFailure.TransportFaulted,
    };
}
