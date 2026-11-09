// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Answering;

/// <summary>The run bounds every answering scenario runs under, and the verdict on whether a run stayed within the bounds a deployment applies by default.</summary>
/// <remarks>
/// <para>
/// Wider than <see cref="MailAnsweringRunBounds.Default" /> in calls and tokens, so a run that needs more of either than
/// a deployment allows still writes an answer for the judge to grade, rather than ending empty and failing every content
/// metric with it. The retrieved-character ceiling stays at the default, because it bounds which mail reaches the model
/// and so what an answer can rest on.
/// </para>
/// <para>
/// The within-bounds metric still holds a run to the default bounds, checked the way a deployment checks them: against
/// what the run's ledger held as each call started. So the empty answer a deployment would have returned stays visible in
/// the report, and a run a deployment would have answered is not failed for the tokens its last call spent.
/// </para>
/// </remarks>
internal static class EvaluationRunBounds
{
    /// <summary>Gets the bounds a scenario's run is held to.</summary>
    public static MailAnsweringRunBounds Ceiling { get; } = MailAnsweringRunBounds.Create(
        MailAnsweringRunBounds.Default.MaximumRetrievedCharacters,
        maximumProviderCalls: 16,
        maximumTokens: 160_000);

    /// <summary>Records whether a run answered within the bounds a deployment applies by default, naming each ceiling a call started past.</summary>
    /// <param name="verdict">The verdict the metric is recorded on.</param>
    /// <param name="metricName">The scenario's name for the within-bounds metric.</param>
    /// <param name="answer">The run's answer, or <see langword="null" /> when <see cref="Ceiling" /> stopped it first.</param>
    /// <param name="atLastCallStart">
    /// The run's ledger as its last call started, counting that call among the calls and none of its tokens, or
    /// <see langword="null" /> where the run made no call.
    /// </param>
    /// <remarks>
    /// <see cref="MailAnsweringRunLedger.RequireAllowanceForNextCall" /> refuses a call once the run has made every call it
    /// may make or the tokens spent before it reach the ceiling, and both counts only grow, so a run a deployment would
    /// have stopped is one whose last call it would have refused. The call that crosses the token ceiling is still answered there, and here.
    /// </remarks>
    public static void RecordWithinDefault(
        EvaluationResult verdict,
        string metricName,
        ChatResponse? answer,
        MailAnsweringRunSpend? atLastCallStart)
    {
        var production = MailAnsweringRunBounds.Default;
        var lastCall = atLastCallStart ?? MailAnsweringRunSpend.Nothing;
        List<string> refused = [];

        if (lastCall.ProviderCalls > production.MaximumProviderCalls)
        {
            refused.Add($"call {lastCall.ProviderCalls}, past the {production.MaximumProviderCalls} calls it allows by default");
        }

        if (lastCall.Tokens >= production.MaximumTokens)
        {
            refused.Add($"a call that started at {lastCall.Tokens} tokens, at or past the {production.MaximumTokens} it allows by default");
        }

        EvaluationMetrics.Record(
            verdict,
            metricName,
            answer is not null && refused.Count is 0,
            (answer, refused.Count) switch
            {
                (null, _) => $"The run reached {Ceiling} before it answered.",
                (_, 0) => $"The run stayed within {production}.",
                _ => $"A deployment would have refused {string.Join(" and ", refused)}.",
            });
    }
}
