// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Xml.Linq;
using MailFathom.AI.AgentConversations;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the delivery and read outcomes an agent is shown beside a message the account sent.</summary>
public sealed class AgentReceiptReadingsTests
{
    private static readonly DateTimeOffset Reported = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Each recipient's outcome is an attribute the service writes, so the model reads what was recorded and nothing a report said.</summary>
    [Fact]
    public void Thread_ASentMessageWithOutcomes_CarriesEachRecipientsDeliveryAndRead()
    {
        // Arrange
        var receipts = new OutgoingEmailReceipts(
            new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: true),
            [
                new RecipientReceipts(
                    "anna@example.test",
                    new RecordedReceiptOutcome(ReceiptOutcome.Delivered, Reported, ReceiptOutcomeSource.Report, "2.0.0", null),
                    new RecordedReceiptOutcome(ReceiptOutcome.Displayed, Reported.AddHours(1), ReceiptOutcomeSource.Report, null, null)),
                new RecipientReceipts("bert@example.test", null, null),
            ]);

        // Act
        var element = ReceiptsOf(receipts);

        // Assert
        Assert.NotNull(element);
        Assert.Equal("true", element.Attribute(AgentReceiptReadings.DeliveryRequestedAttributeName)?.Value);
        Assert.Equal("true", element.Attribute(AgentReceiptReadings.ReadRequestedAttributeName)?.Value);
        var recipients = element.Elements(AgentReceiptReadings.RecipientElementName).ToArray();
        Assert.Equal(
            [("anna@example.test", "delivered", "displayed", "report"), ("bert@example.test", null, null, null)],
            recipients.Select(recipient => (
                recipient.Attribute(AgentReceiptReadings.AddressAttributeName)?.Value,
                recipient.Attribute(AgentReceiptReadings.DeliveryAttributeName)?.Value,
                recipient.Attribute(AgentReceiptReadings.ReadAttributeName)?.Value,
                recipient.Attribute(AgentReceiptReadings.DeliverySourceAttributeName)?.Value)));
    }

    /// <summary>A message with nothing to say about delivery carries no element, rather than one a model would read as an answer.</summary>
    [Fact]
    public void Thread_AMessageWithNoReceipts_CarriesNoReceiptsElement()
    {
        // Act
        var element = ReceiptsOf(receipts: null);

        // Assert
        Assert.Null(element);
    }

    /// <summary>A recipient's address passes the guard on its way to a model, as a sender's does.</summary>
    [Fact]
    public async Task GuardedAsync_Receipts_GuardEveryAddress()
    {
        // Arrange
        var receipts = new OutgoingEmailReceipts(
            new OutgoingReceiptRequests(DeliveryNotification: true, ReadReceipt: false),
            [new RecipientReceipts("anna@example.test", null, null)]);

        // Act
        var guarded = await AgentReceiptReadings.GuardedAsync(
            SensitiveContentEgressGuards.Inactive(),
            receipts,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(receipts.Recipients, guarded?.Recipients);
    }

    private static XElement? ReceiptsOf(OutgoingEmailReceipts? receipts) =>
        XElement.Parse(AgentConversationReadings.Thread(
            [
                new GuardedThreadReading(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    PresentationCitationId.Create("m1"),
                    "owner@example.test",
                    Reported,
                    StubEmailProvenanceReader.KnownCorrespondent,
                    "Lunch",
                    "Shall we?",
                    BodyCut: false,
                    receipts),
            ]))
            .Descendants(AgentReceiptReadings.ReceiptsElementName)
            .SingleOrDefault();
}
