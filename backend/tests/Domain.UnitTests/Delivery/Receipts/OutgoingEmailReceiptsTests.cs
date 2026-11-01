// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Receipts;

/// <summary>Covers when a sent message has receipts to show at all.</summary>
public sealed class OutgoingEmailReceiptsTests
{
    private static readonly RecordedReceiptOutcome Delivered = new(
        ReceiptOutcome.Delivered,
        new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero),
        ReceiptOutcomeSource.Report,
        "2.0.0",
        "mx.example.test");

    /// <summary>A message that asked for nothing and heard nothing carries nothing, so absence is never a row of blanks.</summary>
    [Fact]
    public void Of_NoRequestAndNoOutcome_IsNothing()
    {
        // Act
        var receipts = OutgoingEmailReceipts.Of(OutgoingReceiptRequests.None, [new RecipientReceipts("anna@example.test", null, null)]);

        // Assert
        Assert.Null(receipts);
    }

    /// <summary>A request with nothing back yet still says it was asked, which is the waiting a reader is shown.</summary>
    [Fact]
    public void Of_ARequestWithNothingBack_CarriesTheRequest()
    {
        // Act
        var receipts = OutgoingEmailReceipts.Of(
            new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: false),
            [new RecipientReceipts("anna@example.test", null, null)]);

        // Assert
        Assert.NotNull(receipts);
        Assert.True(receipts.Requests.DeliveryNotification);
        Assert.Null(Assert.Single(receipts.Recipients).Delivery);
    }

    /// <summary>A report that came back unasked is still what happened, so it is shown.</summary>
    [Fact]
    public void Of_AnOutcomeWithNoRequest_CarriesTheOutcome()
    {
        // Act
        var receipts = OutgoingEmailReceipts.Of(
            OutgoingReceiptRequests.None,
            [new RecipientReceipts("anna@example.test", Delivered, null)]);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(Delivered, Assert.Single(receipts.Recipients).Delivery);
    }
}
