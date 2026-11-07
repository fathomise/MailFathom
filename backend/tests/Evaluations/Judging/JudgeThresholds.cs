// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.Judging;

/// <summary>The lowest rating a scenario accepts for each of the judge's metrics, handed to the panel beside the evaluators' own context.</summary>
/// <remarks>
/// A scenario holds a rating to its threshold only after the verdict comes back, while <see cref="JudgePanelEvaluator" />
/// has to know before then whether every juror passed, because that decides whether the arbiter is asked. A scenario
/// therefore states each threshold here, from the same value it holds the rating to afterwards. The quality evaluators
/// read only the context types they know, so this one passes them by.
/// </remarks>
internal sealed class JudgeThresholds : EvaluationContext
{
    private readonly Dictionary<string, double> minimums;

    /// <summary>Initializes a new instance of the <see cref="JudgeThresholds" /> class.</summary>
    /// <param name="minimums">Each metric's name and the lowest rating, from one to five, that passes it.</param>
    public JudgeThresholds(params IEnumerable<(string MetricName, double Minimum)> minimums)
        : this(minimums.ToDictionary(static threshold => threshold.MetricName, static threshold => threshold.Minimum, StringComparer.Ordinal))
    {
    }

    private JudgeThresholds(Dictionary<string, double> minimums)
        : base(
            "Judge thresholds",
            string.Join(
                Environment.NewLine,
                minimums.Select(static threshold => string.Create(CultureInfo.InvariantCulture, $"{threshold.Key}: {threshold.Value}"))))
    {
        this.minimums = minimums;
    }

    /// <summary>Reads the lowest rating that passes a metric.</summary>
    /// <param name="metricName">The metric's name, as its evaluator reports it.</param>
    /// <returns>The threshold, or <see langword="null" /> where the scenario states none for the metric.</returns>
    public double? MinimumFor(string metricName) =>
        this.minimums.TryGetValue(metricName, out var minimum) ? minimum : null;
}
