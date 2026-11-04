// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the wait between a delivery being acknowledged and the attempt at its sent copy ending.</summary>
public sealed class MailDeliveryTelemetryTests
{
    private const string SentCopyDelayInstrumentName = "mailfathom.mail.filing.sent_copy_delay";

    private static readonly MailAccountId Account = MailAccountId.Create("sent-copy-wait");

    private readonly MailDeliveryTelemetry telemetry =
        new(new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero)));

    /// <summary>The wait an attempt carries is recorded in seconds, under its outcome and nothing else.</summary>
    [Fact]
    public void Report_AFilingThatCarriesAWait_RecordsItInSecondsUnderTheOutcomeAlone()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(SentCopyDelayInstrumentName);

        // Act
        this.telemetry.Report(Account, ReportFiling(OutgoingMailFilingOutcome.Filed, TimeSpan.FromSeconds(90)));

        // Assert
        var published = Assert.Single(measurements.Read(SentCopyDelayInstrumentName));
        Assert.Equal(90, published.Value);
        Assert.Equal(["mailfathom.mail.filing.outcome"], published.Tags.Keys);
        Assert.Equal("filed", published.Tags["mailfathom.mail.filing.outcome"]);
    }

    /// <summary>A result carrying no wait is not the ending of a sent copy, so nothing is recorded for it.</summary>
    [Fact]
    public void Report_AFilingThatCarriesNoWait_RecordsNothing()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(SentCopyDelayInstrumentName);

        // Act
        this.telemetry.Report(Account, ReportFiling(OutgoingMailFilingOutcome.AlreadyFiled, waitedAfterDelivery: null));

        // Assert
        Assert.Empty(measurements.Read(SentCopyDelayInstrumentName));
    }

    /// <summary>A wait read against a clock behind the delivery's is recorded as none rather than as a negative one.</summary>
    [Fact]
    public void Report_AWaitBelowZero_RecordsZero()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(SentCopyDelayInstrumentName);

        // Act
        this.telemetry.Report(Account, ReportFiling(OutgoingMailFilingOutcome.Failed, TimeSpan.FromSeconds(-3)));

        // Assert
        Assert.Equal([0d], measurements.ValuesOf(SentCopyDelayInstrumentName));
    }

    private static MailOutboxPassReport ReportFiling(OutgoingMailFilingOutcome outcome, TimeSpan? waitedAfterDelivery) =>
        new(
            [],
            [
                new OutgoingMailFilingResult(
                    OutgoingEmailId.Create(Guid.CreateVersion7()),
                    OutgoingMailFiling.Sent,
                    outcome,
                    Failure: null,
                    waitedAfterDelivery),
            ],
            [],
            MarkedUnknownCount: 0,
            BatchFilled: false,
            OutstandingByStage: []);
}
