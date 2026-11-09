// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Reporting;

/// <summary>How a scenario records a structural check beside the judge's ratings, holds a rating to its threshold, and names what failed.</summary>
/// <remarks>
/// Every check and every rating lands in the verdict as a metric carrying its own interpretation, so the report shows
/// each model's result on each of them and a failed run names the metric rather than the answer.
/// </remarks>
internal static class EvaluationMetrics
{
    /// <summary>Records a structural check in the verdict, failed where it did not hold.</summary>
    /// <param name="verdict">The verdict the check is recorded in.</param>
    /// <param name="name">The name the check is reported under.</param>
    /// <param name="held">Whether it held.</param>
    /// <param name="reason">What was found, in words a failed run can be read by.</param>
    public static void Record(EvaluationResult verdict, string name, bool held, string reason) =>
        verdict.Metrics[name] = new BooleanMetric(name, held, reason)
        {
            Interpretation = new EvaluationMetricInterpretation(
                held ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: !held,
                reason),
        };

    /// <summary>Holds one of the judge's ratings to the lowest score a scenario records for it, where it records one.</summary>
    /// <param name="verdict">The verdict the rating is in.</param>
    /// <param name="metricName">The name the judge's evaluator reports the rating under.</param>
    /// <param name="minimum">The lowest rating, from one to five, a model may score, or <see langword="null" /> where the scenario does not ask the judge for it.</param>
    public static void HoldToThreshold(EvaluationResult verdict, string metricName, int? minimum)
    {
        if (minimum is { } held)
        {
            HoldToThreshold(verdict, metricName, held);
        }
    }

    /// <summary>Holds one of the judge's ratings to the lowest score a scenario records for it.</summary>
    /// <param name="verdict">The verdict the rating is in.</param>
    /// <param name="metricName">The name the judge's evaluator reports the rating under.</param>
    /// <param name="minimum">The lowest rating, from one to five, a model may score.</param>
    /// <remarks>
    /// A rating the judge did not give fails, because a verdict nobody reached proves nothing about the answer. That holds
    /// whatever shape the missing rating takes — a number with no value, a metric of another type, which is how the
    /// evaluation library records an evaluator that threw, or no metric at all — and the reason names the first error the
    /// metric carries, so a judge or provider that did not answer reads as that rather than as the model's shortfall.
    /// </remarks>
    public static void HoldToThreshold(EvaluationResult verdict, string metricName, int minimum)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        verdict.Metrics.TryGetValue(metricName, out var metric);

        if (metric is NumericMetric { Value: { } value } rating)
        {
            rating.Interpretation = new EvaluationMetricInterpretation(
                value >= minimum ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                failed: value < minimum,
                $"Rated {value} against the scenario's threshold of {minimum}.");

            return;
        }

        var unrated = metric ?? (verdict.Metrics[metricName] = new NumericMetric(metricName));
        var firstError = unrated.Diagnostics?.FirstOrDefault(static diagnostic => diagnostic.Severity is EvaluationDiagnosticSeverity.Error);

        unrated.Interpretation = new EvaluationMetricInterpretation(
            EvaluationRating.Inconclusive,
            failed: true,
            firstError is null ? "The judge did not rate it." : $"The judge did not rate it. {firstError.Message}");
    }

    /// <summary>Names every check and rating the verdict falls short on.</summary>
    /// <param name="verdict">The verdict one model's run of the scenario produced.</param>
    /// <returns>One line per shortfall, naming the metric; the caller's header names the scenario and the model.</returns>
    public static IEnumerable<string> ShortfallsOf(EvaluationResult verdict) =>
        verdict.Metrics.Values
            .Where(static metric => metric.Interpretation is { Failed: true })
            .Select(static metric => $"{metric.Name} — {metric.Interpretation!.Reason ?? metric.Reason}");
}
