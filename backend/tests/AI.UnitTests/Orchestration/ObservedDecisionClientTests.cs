// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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
        using var published = new PublishedTelemetry();
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
        using var published = new PublishedTelemetry();
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

    /// <summary>The mail, the question, and the verdict are personal data, so none of them may become a span attribute or a dimension.</summary>
    [Fact]
    public async Task DecideAsync_AnyCall_PublishesNoInputNoQuestionAndNoProbability()
    {
        // Arrange
        using var published = new PublishedTelemetry();
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

    private sealed record Measured(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

    private sealed class PublishedTelemetry : IDisposable
    {
        private readonly string name = $"MailFathom.Tests.Decisions.{Guid.NewGuid():N}";
        private readonly ActivitySource source;
        private readonly Meter meter;
        private readonly ActivityListener activityListener;
        private readonly MeterListener meterListener;
        private readonly ConcurrentQueue<Activity> spans = new();
        private readonly ConcurrentQueue<Measured> measurements = new();

        public PublishedTelemetry()
        {
            this.activityListener = new ActivityListener
            {
                ShouldListenTo = candidate => candidate.Name == this.name,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = this.spans.Enqueue,
            };
            ActivitySource.AddActivityListener(this.activityListener);

            this.meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, subscription) =>
                {
                    if (instrument.Meter.Name == this.name)
                    {
                        subscription.EnableMeasurementEvents(instrument);
                    }
                },
            };
            this.meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => this.Record(instrument, value, tags));
            this.meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => this.Record(instrument, value, tags));
            this.meterListener.Start();

            this.source = new ActivitySource(this.name);
            this.meter = new Meter(this.name);
            this.Telemetry = new DecisionTelemetry(this.source, this.meter);
        }

        public DecisionTelemetry Telemetry { get; }

        public IReadOnlyList<Activity> Spans => [.. this.spans];

        public IReadOnlyList<Measured> AllMeasurements => [.. this.measurements];

        public IReadOnlyList<Measured> Measurements(string instrument) =>
            [.. this.measurements.Where(measurement => measurement.Instrument == instrument)];

        public void Dispose()
        {
            this.meterListener.Dispose();
            this.activityListener.Dispose();
            this.meter.Dispose();
            this.source.Dispose();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            this.measurements.Enqueue(new Measured(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
    }
}
