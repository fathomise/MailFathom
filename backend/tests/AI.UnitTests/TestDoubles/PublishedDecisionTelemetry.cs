// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.AI.Orchestration;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>One measurement a decision instrument took, with the dimensions it carried.</summary>
/// <param name="Instrument">The instrument's name.</param>
/// <param name="Value">The measured value.</param>
/// <param name="Tags">The dimensions the measurement carried.</param>
internal sealed record PublishedMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);

/// <summary>Opens decision telemetry over a source and a meter of its own and records everything published on them.</summary>
/// <remarks>
/// A name of its own per instance, so a listener hears this test's publisher and no other class's whatever runs beside
/// it — which is what lets a test assert on counts as well as on names.
/// </remarks>
internal sealed class PublishedDecisionTelemetry : IDisposable
{
    private readonly string name = $"MailFathom.Tests.Decisions.{Guid.NewGuid():N}";
    private readonly ActivitySource source;
    private readonly Meter meter;
    private readonly ActivityListener activityListener;
    private readonly MeterListener meterListener;
    private readonly ConcurrentQueue<Activity> spans = new();
    private readonly ConcurrentQueue<PublishedMeasurement> measurements = new();
    private readonly ConcurrentDictionary<string, byte> instrumentNames = new(StringComparer.Ordinal);

    /// <summary>Subscribes before the instruments exist, so every one of them is announced to the listener.</summary>
    public PublishedDecisionTelemetry()
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
                    this.instrumentNames.TryAdd(instrument.Name, 0);
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

    /// <summary>Gets the telemetry a client under test publishes through.</summary>
    public DecisionTelemetry Telemetry { get; }

    /// <summary>Gets every span that ended, in the order they ended.</summary>
    public IReadOnlyList<Activity> Spans => [.. this.spans];

    /// <summary>Gets every measurement taken, in the order the instruments took them.</summary>
    public IReadOnlyList<PublishedMeasurement> AllMeasurements => [.. this.measurements];

    /// <summary>Gets the name of every instrument created on the meter.</summary>
    public IReadOnlyCollection<string> InstrumentNames => [.. this.instrumentNames.Keys];

    /// <summary>Gets every span name, instrument name, and dimension key published.</summary>
    public IReadOnlyCollection<string> EmittedNames =>
    [
        .. this.InstrumentNames,
        .. this.Spans.Select(span => span.OperationName),
        .. this.Spans.SelectMany(span => span.TagObjects.Select(tag => tag.Key)),
        .. this.AllMeasurements.SelectMany(measurement => measurement.Tags.Keys),
    ];

    /// <summary>Gets every dimension published, whether on a span or on a measurement.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> EmittedTags =>
    [
        .. this.Spans.SelectMany(span => span.TagObjects),
        .. this.AllMeasurements.SelectMany(measurement => measurement.Tags),
    ];

    /// <summary>Gets the measurements one instrument took.</summary>
    /// <param name="instrument">The instrument's name.</param>
    /// <returns>Its measurements, in order.</returns>
    public IReadOnlyList<PublishedMeasurement> Measurements(string instrument) =>
        [.. this.measurements.Where(measurement => measurement.Instrument == instrument)];

    /// <inheritdoc />
    public void Dispose()
    {
        this.meterListener.Dispose();
        this.activityListener.Dispose();
        this.meter.Dispose();
        this.source.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        this.measurements.Enqueue(new PublishedMeasurement(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
}
