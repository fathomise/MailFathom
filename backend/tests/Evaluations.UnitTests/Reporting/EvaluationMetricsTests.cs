// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Evaluations.Reporting;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Reporting;

/// <summary>Proves a judge's rating is held to its threshold, and that a rating nobody gave fails legibly whatever shape it arrives in.</summary>
public sealed class EvaluationMetricsTests
{
    private const string RatingName = "Task Adherence";

    [Fact]
    public void HoldToThreshold_ARatingAtTheThreshold_Holds()
    {
        // Arrange
        var verdict = new EvaluationResult(new NumericMetric(RatingName, 4));

        // Act
        EvaluationMetrics.HoldToThreshold(verdict, RatingName, minimum: 4);

        // Assert
        Assert.Empty(EvaluationMetrics.ShortfallsOf(verdict));
    }

    [Fact]
    public void HoldToThreshold_NoMetricOfThatName_RecordsItAsUnratedAmongTheShortfalls()
    {
        // Arrange
        var verdict = new EvaluationResult();

        // Act
        EvaluationMetrics.HoldToThreshold(verdict, RatingName, minimum: 4);

        // Assert
        Assert.Equal($"{RatingName} — The judge did not rate it.", Assert.Single(EvaluationMetrics.ShortfallsOf(verdict)));
        Assert.Equal(EvaluationRating.Inconclusive, verdict.Metrics[RatingName].Interpretation?.Rating);
    }

    [Fact]
    public void HoldToThreshold_AMetricOfAnotherTypeCarryingAnError_RecordsItAsUnratedNamingTheError()
    {
        // Arrange
        var thrown = new EvaluationMetric(RatingName);
        thrown.AddDiagnostics(
            EvaluationDiagnostic.Informational("The evaluator was asked."),
            EvaluationDiagnostic.Error("The judge (juror 2) did not answer (ClientResultException 429)."));
        var verdict = new EvaluationResult(thrown);

        // Act
        EvaluationMetrics.HoldToThreshold(verdict, RatingName, minimum: 4);

        // Assert
        Assert.Equal(
            $"{RatingName} — The judge did not rate it. The judge (juror 2) did not answer (ClientResultException 429).",
            Assert.Single(EvaluationMetrics.ShortfallsOf(verdict)));
    }

    [Fact]
    public void HoldToThreshold_ANumberWithNoValueCarryingAnError_RecordsItAsUnratedNamingTheError()
    {
        // Arrange
        var unrated = new NumericMetric(RatingName);
        unrated.AddDiagnostics(EvaluationDiagnostic.Error("The judge (juror 1) refused to answer."));
        var verdict = new EvaluationResult(unrated);

        // Act
        EvaluationMetrics.HoldToThreshold(verdict, RatingName, minimum: 4);

        // Assert
        Assert.Equal(
            $"{RatingName} — The judge did not rate it. The judge (juror 1) refused to answer.",
            Assert.Single(EvaluationMetrics.ShortfallsOf(verdict)));
    }
}
