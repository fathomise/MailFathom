// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Spam;
using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the client is told about a sent message's receipts, a report, and a request for a read receipt.</summary>
public sealed class ClientMailReceiptResponsesTests
{
    private static readonly DateTimeOffset Reported = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A row's summary counts what a screen draws, with a relay or an expansion counted as delivered.</summary>
    [Fact]
    public void ReceiptSummaryFor_ASentMessage_CountsEachOutcomeAScreenDraws()
    {
        // Arrange
        var receipts = new OutgoingEmailReceipts(
            new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: true),
            [
                Recipient("a@example.test", ReceiptOutcome.Delivered, ReceiptOutcome.Displayed),
                Recipient("b@example.test", ReceiptOutcome.Relayed, ReceiptOutcome.Deleted),
                Recipient("c@example.test", ReceiptOutcome.Delayed, read: null),
                Recipient("d@example.test", ReceiptOutcome.Failed, read: null),
                new RecipientReceipts("e@example.test", null, null),
            ]);

        // Act
        var summary = ClientMailReceiptSummaryResponse.For(receipts);

        // Assert
        Assert.Equal(
            new ClientMailReceiptSummaryResponse(
                5,
                new ClientMailDeliverySummaryResponse(Requested: true, Delivered: 2, Delayed: 1, Failed: 1),
                new ClientMailReadSummaryResponse(Requested: true, Read: 1)),
            summary);
    }

    /// <summary>A message with no receipts is answered with nothing, never a summary of zeros.</summary>
    [Fact]
    public void ReceiptSummaryFor_NoReceipts_IsNothing()
    {
        // Act
        var summary = ClientMailReceiptSummaryResponse.For(receipts: null);

        // Assert
        Assert.Null(summary);
    }

    /// <summary>The opened message carries each recipient's outcome whole, with the evidence a delivery report gave.</summary>
    [Fact]
    public void ReceiptsFor_ASentMessage_CarriesEachOutcomeWhole()
    {
        // Arrange
        var receipts = new OutgoingEmailReceipts(
            new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: false),
            [
                new RecipientReceipts(
                    "a@example.test",
                    new RecordedReceiptOutcome(ReceiptOutcome.Failed, Reported, ReceiptOutcomeSource.Report, "5.1.1", "mx.example.test"),
                    null),
            ]);

        // Act
        var response = ClientMailReceiptsResponse.For(receipts);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(
            new ClientMailReceiptOutcomeResponse("Failed", Reported, "Report", "5.1.1", "mx.example.test"),
            Assert.Single(response.Recipients).Delivery);
    }

    /// <summary>A report names its kind, what it said of each recipient, and the subject of the send it matched.</summary>
    [Fact]
    public void ReportFor_AMatchedReport_NamesTheOwnMessage()
    {
        // Arrange
        var mark = new EmailReportMark(
            ReceiptKind.Read,
            [new EmailReportRecipient("a@example.test", ReceiptOutcome.Displayed)],
            Reported,
            NamesOwnMessage: true,
            OwnMessageSubject: "Lunch");

        // Act
        var response = ClientMailReportResponse.For(mark);

        // Assert
        Assert.NotNull(response);
        Assert.Equal("Read", response.Kind);
        Assert.Equal(new ClientMailReportRecipientResponse("a@example.test", "Displayed"), Assert.Single(response.Recipients));
        Assert.Equal("Lunch", response.OwnMessageSubject);
    }

    /// <summary>A request on mail held as spam is not offered, because answering it confirms the address to a spammer.</summary>
    [Theory]
    [InlineData(SpamVerdict.Spam, false)]
    [InlineData(SpamVerdict.NotSpam, true)]
    public void ReadReceiptRequestFor_AMessageByVerdict_OffersTheRequestOnlyOutsideSpam(SpamVerdict verdict, bool offered)
    {
        // Arrange
        var request = new EmailReadReceiptRequest("a@example.test", NamesAnotherAddress: true, SentAt: null);
        var spam = new EmailSpamVerdict(verdict, SpamClassificationStage.Model, Subcategory: null, PhishingRequest: null, ImpersonatedParty: null);

        // Act
        var response = ClientMailReadReceiptRequestResponse.For(request, spam);

        // Assert
        Assert.Equal(offered, response is { Address: "a@example.test", NamesAnotherAddress: true });
    }

    private static RecipientReceipts Recipient(string address, ReceiptOutcome delivery, ReceiptOutcome? read) => new(
        address,
        new RecordedReceiptOutcome(delivery, Reported, ReceiptOutcomeSource.Report, null, null),
        read is { } outcome ? new RecordedReceiptOutcome(outcome, Reported, ReceiptOutcomeSource.Report, null, null) : null);
}
