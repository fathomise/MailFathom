// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Receipts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Receipts;

/// <summary>Covers which kind of report an outcome belongs to and which recorded outcome a later report may replace.</summary>
public sealed class ReceiptOutcomesTests
{
    /// <summary>Delivery reports and read receipts say different things, and each outcome belongs to exactly one of them.</summary>
    [Theory]
    [InlineData(ReceiptOutcome.Delivered, ReceiptKind.Delivery)]
    [InlineData(ReceiptOutcome.Relayed, ReceiptKind.Delivery)]
    [InlineData(ReceiptOutcome.Expanded, ReceiptKind.Delivery)]
    [InlineData(ReceiptOutcome.Failed, ReceiptKind.Delivery)]
    [InlineData(ReceiptOutcome.Delayed, ReceiptKind.Delivery)]
    [InlineData(ReceiptOutcome.Displayed, ReceiptKind.Read)]
    [InlineData(ReceiptOutcome.Deleted, ReceiptKind.Read)]
    [InlineData(ReceiptOutcome.Dispatched, ReceiptKind.Read)]
    [InlineData(ReceiptOutcome.Processed, ReceiptKind.Read)]
    public void KindOf_AnOutcome_NamesTheReportThatStatesIt(ReceiptOutcome outcome, ReceiptKind expected)
    {
        // Act
        var kind = ReceiptOutcomes.KindOf(outcome);

        // Assert
        Assert.Equal(expected, kind);
    }

    /// <summary>A delay is the one interim answer, so only it gives way to what a later delivery report says.</summary>
    [Theory]
    [InlineData(ReceiptOutcome.Delayed, ReceiptOutcome.Delivered, true)]
    [InlineData(ReceiptOutcome.Delayed, ReceiptOutcome.Failed, true)]
    [InlineData(ReceiptOutcome.Delayed, ReceiptOutcome.Delayed, true)]
    [InlineData(ReceiptOutcome.Delivered, ReceiptOutcome.Failed, false)]
    [InlineData(ReceiptOutcome.Failed, ReceiptOutcome.Delivered, false)]
    [InlineData(ReceiptOutcome.Delivered, ReceiptOutcome.Delayed, false)]
    [InlineData(ReceiptOutcome.Displayed, ReceiptOutcome.Deleted, false)]
    public void Replaces_ALaterReport_ReplacesOnlyARecordedDelay(
        ReceiptOutcome recorded,
        ReceiptOutcome reported,
        bool expected)
    {
        // Act
        var replaces = ReceiptOutcomes.Replaces(
            Outcome(recorded, ReceiptOutcomeSource.Report),
            Outcome(reported, ReceiptOutcomeSource.Report));

        // Assert
        Assert.Equal(expected, replaces);
    }

    /// <summary>What this deployment observed between two accounts of one organization is proof, so it replaces any report.</summary>
    [Theory]
    [InlineData(ReceiptOutcome.Delivered, ReceiptOutcome.Delivered)]
    [InlineData(ReceiptOutcome.Failed, ReceiptOutcome.Delivered)]
    [InlineData(ReceiptOutcome.Delayed, ReceiptOutcome.Delivered)]
    [InlineData(ReceiptOutcome.Displayed, ReceiptOutcome.Displayed)]
    [InlineData(ReceiptOutcome.Deleted, ReceiptOutcome.Displayed)]
    public void Replaces_AnOrganizationOutcomeAfterAReport_ReplacesIt(ReceiptOutcome recorded, ReceiptOutcome observed)
    {
        // Act
        var replaces = ReceiptOutcomes.Replaces(
            Outcome(recorded, ReceiptOutcomeSource.Report),
            Outcome(observed, ReceiptOutcomeSource.Organization));

        // Assert
        Assert.True(replaces);
    }

    /// <summary>Nothing replaces an organization outcome: not a report arriving later, and not the same observation made again.</summary>
    [Theory]
    [InlineData(ReceiptOutcome.Delivered, ReceiptOutcome.Failed, ReceiptOutcomeSource.Report)]
    [InlineData(ReceiptOutcome.Delivered, ReceiptOutcome.Delayed, ReceiptOutcomeSource.Report)]
    [InlineData(ReceiptOutcome.Displayed, ReceiptOutcome.Deleted, ReceiptOutcomeSource.Report)]
    [InlineData(ReceiptOutcome.Delivered, ReceiptOutcome.Delivered, ReceiptOutcomeSource.Organization)]
    [InlineData(ReceiptOutcome.Displayed, ReceiptOutcome.Displayed, ReceiptOutcomeSource.Organization)]
    public void Replaces_AnythingAfterAnOrganizationOutcome_LeavesIt(
        ReceiptOutcome recorded,
        ReceiptOutcome reported,
        ReceiptOutcomeSource reportedSource)
    {
        // Act
        var replaces = ReceiptOutcomes.Replaces(
            Outcome(recorded, ReceiptOutcomeSource.Organization),
            Outcome(reported, reportedSource));

        // Assert
        Assert.False(replaces);
    }

    private static RecordedReceiptOutcome Outcome(ReceiptOutcome outcome, ReceiptOutcomeSource source) =>
        new(outcome, DateTimeOffset.UnixEpoch, source, Status: null, ReportingServer: null);
}
