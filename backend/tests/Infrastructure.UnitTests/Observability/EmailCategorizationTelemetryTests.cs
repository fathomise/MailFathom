// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Decisions;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an operator reads to tell why messages went without a category.</summary>
public sealed class EmailCategorizationTelemetryTests
{
    private const string ConsultationsInstrumentName = "mailfathom.email.categorization.consultations";

    private const string OutcomeTagName = "mailfathom.email.categorization.outcome";

    [Theory]
    [InlineData(DecisionModelOutcome.Answered, "answered")]
    [InlineData(DecisionModelOutcome.NotActivated, "not_activated")]
    [InlineData(DecisionModelOutcome.BudgetExhausted, "budget_exhausted")]
    [InlineData(DecisionModelOutcome.ProviderUnavailable, "provider_unavailable")]
    [InlineData(DecisionModelOutcome.AnswerUnreadable, "answer_unreadable")]
    [InlineData(DecisionModelOutcome.InputTooLarge, "input_too_large")]
    [InlineData(DecisionModelOutcome.ScannerUnavailable, "scanner_unavailable")]
    [InlineData(DecisionModelOutcome.ContentUnavailable, "content_unavailable")]
    public void RecordConsultation_EachOutcome_IsCountedUnderItsOwnTagAndNothingElse(DecisionModelOutcome outcome, string expectedTag)
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(ConsultationsInstrumentName);
        var telemetry = new EmailCategorizationTelemetry();

        // Act
        telemetry.RecordConsultation(outcome);

        // Assert
        Assert.Equal([expectedTag], measurements.DimensionOf(ConsultationsInstrumentName, OutcomeTagName));
        Assert.Equal([1d], measurements.ValuesOf(ConsultationsInstrumentName));
    }
}
