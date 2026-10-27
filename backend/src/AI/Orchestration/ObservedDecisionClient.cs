// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Orchestration;

/// <summary>Spans and measures each decision call by the model, the provider, the tokens, the latency, and the outcome, and by nothing it carried.</summary>
/// <remarks>
/// <para>
/// No input, no question, no option, and no probability reaches a span or a measurement. The input is somebody's mail,
/// the questions say what is being decided about it, and a probability is the verdict itself — each would open a time
/// series per message and put a classification of a person's mail into a span store. What is left is what an operator
/// reads a provider by: how long, how much, which build, and whether it failed.
/// </para>
/// <para>
/// Composed outside the resilience middleware, so one span covers a call with all its attempts and ends with the
/// failure the attempts ended on, which is the one an operator acts on.
/// </para>
/// </remarks>
internal sealed class ObservedDecisionClient : DelegatingDecisionClient
{
    private readonly DecisionTelemetry telemetry;
    private readonly DecisionModelPlan plan;
    private readonly TimeProvider timeProvider;
    private readonly string providerName;

    /// <summary>Initializes the middleware over the client one model is asked through.</summary>
    /// <param name="innerClient">The client every call is passed to.</param>
    /// <param name="telemetry">The process's span source and instruments.</param>
    /// <param name="plan">The model asked, whose alias and identifier name it.</param>
    /// <param name="timeProvider">Measures how long a call took.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal ObservedDecisionClient(
        IDecisionClient innerClient,
        DecisionTelemetry telemetry,
        DecisionModelPlan plan,
        TimeProvider timeProvider)
        : base(innerClient)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.telemetry = telemetry;
        this.plan = plan;
        this.timeProvider = timeProvider;
        this.providerName = innerClient.GetService<DecisionClientMetadata>()?.ProviderName ?? "unknown";
    }

    /// <inheritdoc />
    public override async Task<DecisionResponse> DecideAsync(
        IEnumerable<ChatMessage> input,
        IReadOnlyList<DecisionQuestion> questions,
        DecisionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var span = this.telemetry.ActivitySource.StartActivity(DecisionTelemetry.SpanName, ActivityKind.Client);
        var startedAt = this.timeProvider.GetTimestamp();
        var asked = options?.ModelId ?? this.plan.Model;

        try
        {
            var response = await base.DecideAsync(input, questions, options, cancellationToken);

            this.Record(span, startedAt, response.ModelId ?? asked, "answered", failure: null);
            this.CountTokens(response.ModelId ?? asked, response.Usage);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this.Record(span, startedAt, asked, "cancelled", failure: null);

            throw;
        }
        catch (DecisionFailedException failure)
        {
            this.Record(span, startedAt, asked, "failed", failure.Failure.ToString());

            // An answer refused as unreadable was still billed, and the counter states what the provider counted.
            this.CountTokens(asked, failure.Usage);

            throw;
        }
        catch (Exception)
        {
            // A request refused before it was sent — larger than its bound, an input the adapter cannot carry — is a
            // failed call too, and leaving it out would make exactly the mail that was too long vanish from the figures.
            this.Record(span, startedAt, asked, "failed", DecisionTelemetry.UnclassifiedFailure);

            throw;
        }
    }

    private void Record(Activity? span, long startedAt, string model, string outcome, string? failure)
    {
        TagList tags = new()
        {
            { DecisionTelemetry.EndpointTagName, this.plan.Alias },
            { DecisionTelemetry.ModelTagName, model },
            { DecisionTelemetry.ProviderTagName, this.providerName },
            { DecisionTelemetry.OutcomeTagName, outcome },
        };

        if (failure is not null)
        {
            tags.Add(DecisionTelemetry.FailureTagName, failure);
        }

        this.telemetry.Duration.Record(this.timeProvider.GetElapsedTime(startedAt).TotalSeconds, tags);

        if (span is null)
        {
            return;
        }

        foreach (var tag in tags)
        {
            span.SetTag(tag.Key, tag.Value);
        }

        if (failure is not null)
        {
            span.SetStatus(ActivityStatusCode.Error);
        }
    }

    private void CountTokens(string model, UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        this.telemetry.Tokens.Add(usage.InputTokenCount ?? 0, this.TokenTags(model, "input"));
        this.telemetry.Tokens.Add(usage.OutputTokenCount ?? 0, this.TokenTags(model, "output"));
    }

    private TagList TokenTags(string model, string direction) => new()
    {
        { DecisionTelemetry.EndpointTagName, this.plan.Alias },
        { DecisionTelemetry.ModelTagName, model },
        { DecisionTelemetry.ProviderTagName, this.providerName },
        { DecisionTelemetry.DirectionTagName, direction },
    };
}
