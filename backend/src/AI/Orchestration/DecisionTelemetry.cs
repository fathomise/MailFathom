// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MailFathom.AI.Orchestration;

/// <summary>The span source and the instruments every decision call is measured with, created once per process.</summary>
/// <remarks>
/// <para>
/// One instance for the process rather than one per client, because a client is composed per scope and an instrument
/// created per composition would be a new metric stream per classified message.
/// </para>
/// <para>
/// The source and the meter are handed in rather than reached, because they are MailFathom's own and live in a project
/// this one does not reference; the host passes the ones every other publisher writes to.
/// </para>
/// </remarks>
internal sealed class DecisionTelemetry
{
    /// <summary>The name of the span one decision call opens.</summary>
    internal const string SpanName = "decide";

    /// <summary>The dimension naming the decision model by the alias the operator declared it under.</summary>
    internal const string EndpointTagName = "mailfathom.decision.endpoint";

    /// <summary>The dimension naming the model build that answered, or the one asked for where none did.</summary>
    internal const string ModelTagName = "mailfathom.decision.model";

    /// <summary>The dimension naming the provider the model is reached through.</summary>
    internal const string ProviderTagName = "mailfathom.decision.provider";

    /// <summary>The dimension naming how the call ended: <c>answered</c>, <c>failed</c>, or <c>cancelled</c>.</summary>
    internal const string OutcomeTagName = "mailfathom.decision.outcome";

    /// <summary>The dimension naming which failure ended a call that failed.</summary>
    internal const string FailureTagName = "mailfathom.decision.failure";

    /// <summary>The dimension naming whether counted tokens were sent or produced: <c>input</c> or <c>output</c>.</summary>
    internal const string DirectionTagName = "mailfathom.decision.direction";

    /// <summary>Initializes the instruments on the given meter.</summary>
    /// <param name="activitySource">The source a call's span is opened from.</param>
    /// <param name="meter">The meter the instruments are created on.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal DecisionTelemetry(ActivitySource activitySource, Meter meter)
    {
        ArgumentNullException.ThrowIfNull(activitySource);
        ArgumentNullException.ThrowIfNull(meter);

        this.ActivitySource = activitySource;
        this.Duration = meter.CreateHistogram<double>(
            "mailfathom.decisions.duration",
            unit: "s",
            description: "How long a decision call took, retries included, by model and outcome.");
        this.Tokens = meter.CreateCounter<long>(
            "mailfathom.decisions.tokens",
            unit: "{token}",
            description: "Tokens decision calls consumed, as the provider counted them, by model and direction.");
    }

    /// <summary>Gets the source a call's span is opened from.</summary>
    internal ActivitySource ActivitySource { get; }

    /// <summary>Gets the histogram a call's duration is recorded in.</summary>
    internal Histogram<double> Duration { get; }

    /// <summary>Gets the counter a call's tokens are added to.</summary>
    internal Counter<long> Tokens { get; }
}
