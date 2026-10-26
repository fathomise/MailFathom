// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Model;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an operator reads to tell a model stage that is answering from one that is configured away, spent, or down.</summary>
/// <remarks>
/// Every outcome but answered leaves the deterministic verdict standing, so the record of a message looks the same
/// whichever of them happened; the outcome tag is the only place they are told apart, and the job and the outcome are
/// all an instrument here carries.
/// </remarks>
public sealed class SpamModelStageTelemetryTests
{
    private const string ConsultationsInstrumentName = "mailfathom.spam.model_stage.consultations";

    private const string JobTagName = "mailfathom.spam.model_stage.job";

    private const string OutcomeTagName = "mailfathom.spam.model_stage.outcome";

    private static readonly Dictionary<SpamModelJob, string> JobTags = new()
    {
        [SpamModelJob.DecideVerdict] = "decide_verdict",
        [SpamModelJob.NameSubcategory] = "name_subcategory",
    };

    private static readonly Dictionary<SpamModelOutcome, string> OutcomeTags = new()
    {
        [SpamModelOutcome.Answered] = "answered",
        [SpamModelOutcome.NotActivated] = "not_activated",
        [SpamModelOutcome.BudgetExhausted] = "budget_exhausted",
        [SpamModelOutcome.ProviderUnavailable] = "provider_unavailable",
        [SpamModelOutcome.AnswerUnreadable] = "answer_unreadable",
        [SpamModelOutcome.InputTooLarge] = "input_too_large",
        [SpamModelOutcome.ScannerUnavailable] = "scanner_unavailable",
    };

    private readonly SpamModelStageTelemetry telemetry = new();

    /// <summary>Gets every job beside every outcome, so a member added to either enumeration without a tag fails here.</summary>
    public static TheoryData<SpamModelJob, SpamModelOutcome> Consultations => new(
        Enum.GetValues<SpamModelJob>().SelectMany(job => Enum.GetValues<SpamModelOutcome>().Select(outcome => (job, outcome))));

    [Theory]
    [MemberData(nameof(Consultations))]
    public void RecordConsultation_OneConsultation_CountsItUnderItsJobAndItsOutcome(SpamModelJob job, SpamModelOutcome outcome)
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(ConsultationsInstrumentName);

        // Act
        this.telemetry.RecordConsultation(job, outcome);

        // Assert
        Assert.Equal([JobTags[job]], measurements.DimensionOf(ConsultationsInstrumentName, JobTagName));
        Assert.Equal([OutcomeTags[outcome]], measurements.DimensionOf(ConsultationsInstrumentName, OutcomeTagName));
        Assert.Equal([1d], measurements.ValuesOf(ConsultationsInstrumentName));
    }

    /// <summary>A value no member names is still counted, under a tag that says so rather than under a real series.</summary>
    [Fact]
    public void RecordConsultation_AJobAndAnOutcomeNoMemberNames_CountsItAsUnknown()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(ConsultationsInstrumentName);

        // Act
        this.telemetry.RecordConsultation((SpamModelJob)99, (SpamModelOutcome)99);

        // Assert
        Assert.Equal(["unknown"], measurements.DimensionOf(ConsultationsInstrumentName, JobTagName));
        Assert.Equal(["unknown"], measurements.DimensionOf(ConsultationsInstrumentName, OutcomeTagName));
    }
}
