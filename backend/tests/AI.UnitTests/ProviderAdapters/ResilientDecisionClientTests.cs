// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.ProviderAdapters;

/// <summary>Covers the bounds every decision call passes through: its deadline, the classification of what the provider answered, and what the health state hears.</summary>
/// <remarks>
/// Composed over the real adapter and a fake transport where a status is what is being classified, because a status the
/// adapter raises and a status this reads are one contract and proving each half against a stand-in for the other would
/// prove neither.
/// </remarks>
public sealed class ResilientDecisionClientTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(200);

    private static readonly ChatMessage[] Input = [new(ChatRole.User, "Your parcel is waiting, pay the fee here.")];

    private static readonly DecisionQuestion[] Questions = [DecisionQuestion.YesNo("spam", "Is this spam?")];

    [Fact]
    public async Task DecideAsync_AModelThatAnswered_ReturnsTheAnswerAndReportsTheProviderWorking()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        using var client = ClientOver(inner, healthRecorder);

        // Act
        var response = await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DecisionQuestion.Yes, response.AnswerTo("spam").Value);
        healthRecorder.Received(1).RecordServed(AiProviderRole.Decision);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, DecisionFailure.CredentialRejected, false)]
    [InlineData(HttpStatusCode.Forbidden, DecisionFailure.CredentialRejected, false)]
    [InlineData(HttpStatusCode.TooManyRequests, DecisionFailure.RateLimited, true)]
    [InlineData(HttpStatusCode.BadRequest, DecisionFailure.RequestRefused, false)]
    [InlineData(HttpStatusCode.PaymentRequired, DecisionFailure.RequestRefused, false)]
    [InlineData(HttpStatusCode.NotFound, DecisionFailure.RequestRefused, false)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, DecisionFailure.RequestRefused, false)]
    [InlineData(HttpStatusCode.InternalServerError, DecisionFailure.TransportFaulted, true)]
    [InlineData(HttpStatusCode.BadGateway, DecisionFailure.TransportFaulted, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, DecisionFailure.TransportFaulted, true)]
    [InlineData((HttpStatusCode)524, DecisionFailure.TransportFaulted, true)]
    [InlineData((HttpStatusCode)529, DecisionFailure.TransportFaulted, true)]
    public async Task DecideAsync_AStatusTheProviderAnswered_IsClassifiedAndReportedAtThatGranularity(
        HttpStatusCode status,
        DecisionFailure expected,
        bool isWorthRepeating)
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var handler = FakeHttpMessageHandler.AlwaysResponding(() => OpenRouterDecisionClientTests.Json(status, "{}"));
        using var adapter = OpenRouterDecisionClientTests.ClientOver(handler);
        using var client = ClientOver(adapter, healthRecorder);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(expected, failure.Failure);
        Assert.Equal(isWorthRepeating, failure.IsWorthRepeating);

        if (isWorthRepeating)
        {
            healthRecorder.Received(1).RecordUnavailable(AiProviderRole.Decision);
        }
        else
        {
            healthRecorder.Received(1).RecordMisconfigured(AiProviderRole.Decision);
        }
    }

    /// <summary>An unreadable answer came from an endpoint that took the request and ran the model, so it is not reported as broken.</summary>
    [Fact]
    public async Task DecideAsync_AnUnreadableAnswer_IsReportedAsAWorkingProvider()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = ScriptedDecisionClient.Throwing(new DecisionFailedException("decider", DecisionFailure.AnswerUnreadable));
        using var client = ClientOver(inner, healthRecorder);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.DecisionAnswerUnreadable, failure.ErrorCode);
        healthRecorder.Received(1).RecordServed(AiProviderRole.Decision);
    }

    [Fact]
    public async Task DecideAsync_AModelThatNeverAnswers_EndsAsThisDeploymentsTimeout()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = ScriptedDecisionClient.Stalling();
        using var client = ClientOver(inner, healthRecorder);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.RequestTimedOut, failure.Failure);
        healthRecorder.Received(1).RecordUnavailable(AiProviderRole.Decision);
    }

    /// <summary>A caller who stopped waiting says nothing about the provider, so it must not be reported against it.</summary>
    [Fact]
    public async Task DecideAsync_ACallerThatCancelled_IsNotReportedAsAProviderFailure()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = ScriptedDecisionClient.Stalling();
        using var client = ClientOver(inner, healthRecorder);
        using var caller = new CancellationTokenSource();

        await caller.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.DecideAsync(Input, Questions, cancellationToken: caller.Token));

        // Assert
        Assert.Empty(healthRecorder.ReceivedCalls());
    }

    [Fact]
    public async Task DecideAsync_APipelineThatDeclinedTheCall_IsATransportFault()
    {
        // Arrange
        var healthRecorder = Substitute.For<IAiProviderHealthRecorder>();
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        using var client = ClientOver(inner, healthRecorder, declineTheCall: true);

        // Act
        var failure = await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(DecisionFailure.TransportFaulted, failure.Failure);
        Assert.Equal(MailFathomErrorCode.DecisionProviderUnavailable, failure.ErrorCode);
        Assert.Empty(inner.Calls);
    }

    [Fact]
    public async Task DecideAsync_AnyCall_RunsUnderTheProviderBudgetKeyedByTheModelsAlias()
    {
        // Arrange
        using var inner = ScriptedDecisionClient.AnsweringFirstOption();
        var operationRunner = PassThroughRunner();
        using var client = new ResilientDecisionClient(inner, Plan(), operationRunner, Substitute.For<IAiProviderHealthRecorder>(), NullLogger.Instance);

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        await operationRunner.Received(1).RunAsync(
            OutboundDependency.AiProviderInvocation,
            "decider",
            Arg.Any<Func<CancellationToken, Task<DecisionResponse>>>(),
            Arg.Any<CancellationToken>());
    }

    private static DecisionModelPlan Plan() => new("decider", Address: null, "vendor/decider-2026-10-01", Deadline, 32_000);

    private static ResilientDecisionClient ClientOver(
        IDecisionClient inner,
        IAiProviderHealthRecorder healthRecorder,
        bool declineTheCall = false) =>
        new(inner, Plan(), PassThroughRunner(declineTheCall), healthRecorder, NullLogger.Instance);

    private static IOutboundOperationRunner PassThroughRunner(bool declineTheCall = false)
    {
        var operationRunner = Substitute.For<IOutboundOperationRunner>();
        operationRunner
            .RunAsync(
                Arg.Any<OutboundDependency>(),
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<DecisionResponse>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (declineTheCall)
                {
                    throw new PipelineDeclinedTheOperation();
                }

                var operation = call.Arg<Func<CancellationToken, Task<DecisionResponse>>>()!;

                return operation(call.Arg<CancellationToken>());
            });

        return operationRunner;
    }

    /// <summary>Stands in for whatever the resilience boundary raises when a configured limit stopped an operation.</summary>
    [SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "A test double for one behavior of one test class, which nothing outside it raises or catches.")]
    private sealed class PipelineDeclinedTheOperation()
        : MailFathomException("An outbound resilience limit stopped the operation.")
    {
        public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.OutboundDependencyUnavailable;
    }
}
