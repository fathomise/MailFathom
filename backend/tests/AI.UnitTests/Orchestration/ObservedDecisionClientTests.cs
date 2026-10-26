// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics;
using System.Globalization;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers what a decision call publishes about itself: the model, the provider, the tokens, the latency, and the outcome, and nothing it carried.</summary>
/// <remarks>
/// Each test opens a source and a meter under a name of its own, so the listeners hear this test's publisher and no
/// other class's, whatever runs beside it.
/// </remarks>
public sealed class ObservedDecisionClientTests
{
    private const string Evidence = "Your parcel is waiting, pay the fee here.";
    private const string Instruction = "Is this unsolicited bulk or deceptive mail?";

    private static readonly ChatMessage[] Input = [new(ChatRole.User, Evidence)];

    private static readonly DecisionQuestion[] Questions = [DecisionQuestion.YesNo("spam", Instruction)];

    private static readonly DecisionModelPlan Plan = new("decider", Address: null, "vendor/decider-2026-10-01", TimeSpan.FromSeconds(30), 32_000);

    [Fact]
    public async Task DecideAsync_AnAnsweredCall_PublishesTheModelTheOutcomeTheTokensAndTheLatency()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();
        using var inner = ScriptedDecisionClient.AnsweringFirstOption(inputTokens: 412, outputTokens: 3, modelId: "vendor/decider-2026-10-01:b2");
        using var client = new ObservedDecisionClient(inner, published.Telemetry, Plan, new FakeTimeProvider());

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var span = Assert.Single(published.Spans);
        Assert.Equal(DecisionTelemetry.SpanName, span.OperationName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("decider", span.GetTagItem(DecisionTelemetry.EndpointTagName));
        Assert.Equal("vendor/decider-2026-10-01:b2", span.GetTagItem(DecisionTelemetry.ModelTagName));
        Assert.Equal("answered", span.GetTagItem(DecisionTelemetry.OutcomeTagName));
        Assert.Null(span.GetTagItem(DecisionTelemetry.FailureTagName));

        Assert.Equal([412L, 3L], published.Measurements("mailfathom.decisions.tokens").Select(measurement => (long)measurement.Value));
        Assert.Equal(
            ["input", "output"],
            published.Measurements("mailfathom.decisions.tokens").Select(measurement => measurement.Tags[DecisionTelemetry.DirectionTagName]));
        Assert.Single(published.Measurements("mailfathom.decisions.duration"));
    }

    [Fact]
    public async Task DecideAsync_AFailedCall_PublishesWhichFailureEndedItAsAnError()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();
        using var inner = ScriptedDecisionClient.Throwing(new DecisionFailedException("decider", DecisionFailure.RateLimited));
        using var client = new ObservedDecisionClient(inner, published.Telemetry, Plan, new FakeTimeProvider());

        // Act
        await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        var span = Assert.Single(published.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("failed", span.GetTagItem(DecisionTelemetry.OutcomeTagName));
        Assert.Equal(nameof(DecisionFailure.RateLimited), span.GetTagItem(DecisionTelemetry.FailureTagName));
        Assert.Equal("vendor/decider-2026-10-01", span.GetTagItem(DecisionTelemetry.ModelTagName));
        Assert.Empty(published.Measurements("mailfathom.decisions.tokens"));
    }

    /// <summary>The model ran and the provider billed it, so the counter states those tokens whatever the reader made of the answer.</summary>
    [Fact]
    public async Task DecideAsync_AnUnreadableAnswerThatReportedUsage_CountsTheTokensItWasBilled()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();
        using var inner = ScriptedDecisionClient.Throwing(new DecisionFailedException("decider", DecisionFailure.AnswerUnreadable)
        {
            Usage = new UsageDetails { InputTokenCount = 90, OutputTokenCount = 0 },
        });
        using var client = new ObservedDecisionClient(inner, published.Telemetry, Plan, new FakeTimeProvider());

        // Act
        await Assert.ThrowsAsync<DecisionFailedException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(nameof(DecisionFailure.AnswerUnreadable), Assert.Single(published.Spans).GetTagItem(DecisionTelemetry.FailureTagName));
        Assert.Equal([90L, 0L], published.Measurements("mailfathom.decisions.tokens").Select(measurement => (long)measurement.Value));
    }

    /// <summary>A request refused before it was sent is a failed call, so long mail over the bound does not vanish from the figures.</summary>
    [Fact]
    public async Task DecideAsync_ARequestRefusedBeforeItWasSent_PublishesAnUnclassifiedFailureAsAnError()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();
        using var inner = ScriptedDecisionClient.Throwing(new ArgumentException("The request is over its bound.", "input"));
        using var client = new ObservedDecisionClient(inner, published.Telemetry, Plan, new FakeTimeProvider());

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        var span = Assert.Single(published.Spans);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal("failed", span.GetTagItem(DecisionTelemetry.OutcomeTagName));
        Assert.Equal(DecisionTelemetry.UnclassifiedFailure, span.GetTagItem(DecisionTelemetry.FailureTagName));
        Assert.Equal("failed", Assert.Single(published.Measurements("mailfathom.decisions.duration")).Tags[DecisionTelemetry.OutcomeTagName]);
    }

    /// <summary>A caller that stopped waiting is this system stopping the work, which is not the provider failing.</summary>
    [Fact]
    public async Task DecideAsync_ACallTheCallerCancelled_PublishesItAsCancelledRatherThanAsAnError()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();
        using var inner = ScriptedDecisionClient.Stalling();
        using var client = new ObservedDecisionClient(inner, published.Telemetry, Plan, new FakeTimeProvider());
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var call = client.DecideAsync(Input, Questions, cancellationToken: caller.Token);
        await caller.CancelAsync();

        // Act
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);

        // Assert
        var span = Assert.Single(published.Spans);
        Assert.NotEqual(ActivityStatusCode.Error, span.Status);
        Assert.Equal("cancelled", span.GetTagItem(DecisionTelemetry.OutcomeTagName));
        Assert.Null(span.GetTagItem(DecisionTelemetry.FailureTagName));
    }

    /// <summary>The mail, the question, and the verdict are personal data, so none of them may become a span attribute or a dimension.</summary>
    [Fact]
    public async Task DecideAsync_AnyCall_PublishesNoInputNoQuestionAndNoProbability()
    {
        // Arrange
        using var published = new PublishedDecisionTelemetry();
        using var inner = ScriptedDecisionClient.AnsweringFirstOption(inputTokens: 5);
        using var client = new ObservedDecisionClient(inner, published.Telemetry, Plan, new FakeTimeProvider());

        // Act
        await client.DecideAsync(Input, Questions, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var values = published.Spans
            .SelectMany(span => span.TagObjects.Select(tag => tag.Value))
            .Concat(published.AllMeasurements.SelectMany(measurement => measurement.Tags.Values))
            .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture))
            .ToArray();
        Assert.NotEmpty(values);
        Assert.DoesNotContain(values, value => value is Evidence or Instruction or DecisionQuestion.Yes);
    }

}
