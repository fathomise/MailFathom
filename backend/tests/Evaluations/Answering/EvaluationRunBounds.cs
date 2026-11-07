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
/// The within-bounds metric still holds a run to the default bounds, read from what its ledger spent, so the empty
/// answer a deployment would have returned stays visible in the report.
/// </para>
/// </remarks>
internal static class EvaluationRunBounds
{
    /// <summary>Gets the bounds a scenario's run is held to.</summary>
    public static MailAnsweringRunBounds Ceiling { get; } = MailAnsweringRunBounds.Create(
        MailAnsweringRunBounds.Default.MaximumRetrievedCharacters,
        maximumProviderCalls: 16,
        maximumTokens: 160_000);

    /// <summary>Records whether a run answered within the bounds a deployment applies by default, naming each ceiling it passed.</summary>
    /// <param name="verdict">The verdict the metric is recorded on.</param>
    /// <param name="metricName">The scenario's name for the within-bounds metric.</param>
    /// <param name="answer">The run's answer, or <see langword="null" /> when <see cref="Ceiling" /> stopped it first.</param>
    /// <param name="spend">What the run's ledger spent.</param>
    /// <remarks>
    /// A deployment checks its token ceiling before each call, so the call that crosses it is still paid for and answered.
    /// A run whose last call alone took it past the default token ceiling therefore fails here although a deployment would
    /// have answered it — a spend total cannot tell the two apart, and failing such a run errs towards showing the problem.
    /// </remarks>
    public static void RecordWithinDefault(EvaluationResult verdict, string metricName, ChatResponse? answer, MailAnsweringRunSpend spend)
    {
        var production = MailAnsweringRunBounds.Default;
        List<string> passed = [];

        if (spend.ProviderCalls > production.MaximumProviderCalls)
        {
            passed.Add($"{spend.ProviderCalls} calls, more than the {production.MaximumProviderCalls} a deployment allows by default");
        }

        if (spend.Tokens > production.MaximumTokens)
        {
            passed.Add($"{spend.Tokens} tokens, more than the {production.MaximumTokens} a deployment allows by default");
        }

        EvaluationMetrics.Record(
            verdict,
            metricName,
            answer is not null && passed.Count is 0,
            (answer, passed.Count) switch
            {
                (null, _) => $"The run reached {Ceiling} before it answered.",
                (_, 0) => $"The run stayed within {production}.",
                _ => $"The run answered only after {string.Join(" and ", passed)}.",
            });
    }
}
