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
        var replaces = ReceiptOutcomes.Replaces(recorded, reported);

        // Assert
        Assert.Equal(expected, replaces);
    }
}
