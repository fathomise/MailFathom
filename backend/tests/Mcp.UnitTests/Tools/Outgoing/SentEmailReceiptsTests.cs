// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Mcp.Tools.Outgoing;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Outgoing;

/// <summary>Covers the receipts a tool returning a sent message publishes: with outcomes, with a request and nothing back, and with neither.</summary>
public sealed class SentEmailReceiptsTests
{
    private static readonly DateTimeOffset Reported = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Each recipient's recorded outcome is published with when it was reported and where it came from.</summary>
    [Fact]
    public void From_ASentMessageWithOutcomes_PublishesEachRecipientsOutcome()
    {
        // Arrange
        var receipts = new OutgoingEmailReceipts(
            new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: false),
            [
                new RecipientReceipts(
                    "anna@example.test",
                    new RecordedReceiptOutcome(ReceiptOutcome.Failed, Reported, ReceiptOutcomeSource.Report, "5.1.1", "mx.example.test"),
                    null),
            ]);

        // Act
        var published = SentEmailReceipts.From(receipts);

        // Assert
        Assert.NotNull(published);
        Assert.True(published.DeliveryRequested);
        Assert.False(published.ReadRequested);
        var recipient = Assert.Single(published.Recipients);
        Assert.Equal("anna@example.test", recipient.Address);
        Assert.Equal(
            new ReceiptOutcomeResult { Outcome = ReceiptOutcomeKind.Failed, ReportedAt = Reported, Source = ReceiptOutcomeOrigin.Report, Status = "5.1.1" },
            recipient.Delivery);
        Assert.Null(recipient.Read);
    }

    /// <summary>A request with nothing back says it was asked, with no outcome under any recipient.</summary>
    [Fact]
    public void From_ARequestWithNothingBack_PublishesTheRequestAlone()
    {
        // Arrange
        var receipts = new OutgoingEmailReceipts(
            new OutgoingReceiptRequests(DeliveryNotification: false, ReadReceipt: true),
            [new RecipientReceipts("anna@example.test", null, null)]);

        // Act
        var published = SentEmailReceipts.From(receipts);

        // Assert
        Assert.NotNull(published);
        Assert.True(published.ReadRequested);
        Assert.All(published.Recipients, recipient => Assert.True(recipient is { Delivery: null, Read: null }));
    }

    /// <summary>A message that asked for nothing and heard nothing publishes no field at all.</summary>
    [Fact]
    public void From_NoReceipts_PublishesNothing()
    {
        // Act
        var published = SentEmailReceipts.From(receipts: null);

        // Assert
        Assert.Null(published);
    }

    /// <summary>Every recorded outcome has a published spelling, so a new one cannot reach a caller unnamed.</summary>
    [Fact]
    public void From_EveryOutcome_HasAPublishedSpelling()
    {
        // Arrange
        var outcomes = Enum.GetValues<ReceiptOutcome>();

        // Act
        var published = outcomes
            .Select(outcome => ReceiptOutcomeResult.From(new RecordedReceiptOutcome(outcome, Reported, ReceiptOutcomeSource.Report, null, null))!.Outcome)
            .Select(kind => kind.ToString())
            .ToArray();

        // Assert
        Assert.Equal(outcomes.Select(outcome => outcome.ToString()), published);
    }
}
