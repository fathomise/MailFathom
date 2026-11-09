// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Evaluations.Answering;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Answering;

/// <summary>Proves the within-bounds metric refuses exactly the calls a deployment's ledger refuses, read as each call starts.</summary>
public sealed class EvaluationRunBoundsTests
{
    private const string MetricName = "Stayed within its bounds";

    private static readonly ChatResponse Answer = new(new ChatMessage(ChatRole.Assistant, "It needed 42 Lantern Way."));

    [Fact]
    public void RecordWithinDefault_AnEighthCallStartingBelowTheTokenCeiling_Passes()
    {
        // Arrange
        var verdict = new EvaluationResult();
        var production = MailAnsweringRunBounds.Default;

        // Act
        EvaluationRunBounds.RecordWithinDefault(
            verdict,
            MetricName,
            Answer,
            new MailAnsweringRunSpend(production.MaximumProviderCalls, production.MaximumTokens - 1, 0, 0));

        // Assert
        Assert.True(verdict.Get<BooleanMetric>(MetricName).Value);
    }

    [Fact]
    public void RecordWithinDefault_ANinthCall_FailsNamingTheCall()
    {
        // Arrange
        var verdict = new EvaluationResult();
        var production = MailAnsweringRunBounds.Default;

        // Act
        EvaluationRunBounds.RecordWithinDefault(
            verdict,
            MetricName,
            Answer,
            new MailAnsweringRunSpend(production.MaximumProviderCalls + 1, 1_000, 0, 0));

        // Assert
        var withinBounds = verdict.Get<BooleanMetric>(MetricName);
        Assert.False(withinBounds.Value);
        Assert.Contains($"call {production.MaximumProviderCalls + 1},", withinBounds.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordWithinDefault_ACallStartingAtTheTokenCeiling_FailsNamingTheTokens()
    {
        // Arrange
        var verdict = new EvaluationResult();
        var production = MailAnsweringRunBounds.Default;

        // Act
        EvaluationRunBounds.RecordWithinDefault(
            verdict,
            MetricName,
            Answer,
            new MailAnsweringRunSpend(2, production.MaximumTokens, 0, 0));

        // Assert
        var withinBounds = verdict.Get<BooleanMetric>(MetricName);
        Assert.False(withinBounds.Value);
        Assert.Contains($"started at {production.MaximumTokens} tokens", withinBounds.Reason, StringComparison.Ordinal);
    }
}
