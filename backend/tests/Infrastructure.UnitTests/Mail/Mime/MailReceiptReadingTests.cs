// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Infrastructure.Mail.Mime;
using MimeKit;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers how a delivery report, a read receipt, and a request for one are read out of a message.</summary>
public sealed class MailReceiptReadingTests
{
    /// <summary>A delivery report names the send by its envelope identifier and says per recipient what became of it.</summary>
    [Fact]
    public void ReadReport_ADeliveryStatusNotification_ReadsTheEnvelopeIdentifierAndEachRecipient()
    {
        // Arrange
        var message = Parse(DeliveryReport(
            """
            Final-Recipient: rfc822; anna@example.test
            Original-Recipient: rfc822; Anna@Example.test
            Action: delivered
            Status: 2.0.0 (ok)
            Remote-MTA: dns; mx.example.test

            Final-Recipient: rfc822; bert@example.test
            Action: failed
            Status: 5.1.1
            """));

        // Act
        var report = MailReceiptReading.ReadReport(message);

        // Assert
        Assert.NotNull(report);
        Assert.Equal(ReceiptKind.Delivery, report.Kind);
        Assert.Equal("0192a1b2-0000-7000-8000-000000000001", report.OriginalEnvelopeId);
        Assert.Collection(
            report.Recipients,
            delivered =>
            {
                Assert.Equal("anna@example.test", delivered.Recipient);
                Assert.Equal("Anna@Example.test", delivered.OriginalRecipient);
                Assert.Equal(ReceiptOutcome.Delivered, delivered.Outcome);
                Assert.Equal("2.0.0", delivered.Status);
                Assert.Equal("mx.example.test", delivered.ReportingServer);
            },
            failed =>
            {
                Assert.Equal("bert@example.test", failed.Recipient);
                Assert.Equal(ReceiptOutcome.Failed, failed.Outcome);
                Assert.Equal("5.1.1", failed.Status);
                Assert.Equal("relay.example.test", failed.ReportingServer);
            });
    }

    /// <summary>An action RFC 3464 does not name records nothing rather than an outcome nobody stated.</summary>
    [Fact]
    public void ReadReport_ARecipientWithAnActionNoRfcNames_LeavesThatRecipientOut()
    {
        // Arrange
        var message = Parse(DeliveryReport(
            """
            Final-Recipient: rfc822; anna@example.test
            Action: vanished
            Status: 2.0.0
            """));

        // Act
        var report = MailReceiptReading.ReadReport(message);

        // Assert
        Assert.NotNull(report);
        Assert.Empty(report.Recipients);
    }

    /// <summary>A read receipt names the message it is about and the disposition its reader's mail app reported.</summary>
    [Theory]
    [InlineData("manual-action/MDN-sent-manually; displayed", ReceiptOutcome.Displayed)]
    [InlineData("automatic-action/MDN-sent-automatically; deleted", ReceiptOutcome.Deleted)]
    [InlineData("automatic-action/MDN-sent-automatically; processed/error", ReceiptOutcome.Processed)]
    public void ReadReport_ADispositionNotification_ReadsTheOriginalMessageAndTheDisposition(
        string disposition,
        ReceiptOutcome expected)
    {
        // Arrange
        var message = Parse(DispositionReport(disposition));

        // Act
        var report = MailReceiptReading.ReadReport(message);

        // Assert
        Assert.NotNull(report);
        Assert.Equal(ReceiptKind.Read, report.Kind);
        Assert.Equal("sent-1@example.test", report.OriginalMessageId);
        var recipient = Assert.Single(report.Recipients);
        Assert.Equal("anna@example.test", recipient.Recipient);
        Assert.Equal(expected, recipient.Outcome);
    }

    /// <summary>Only the report type decides, so a message that merely calls itself a receipt is ordinary mail.</summary>
    [Fact]
    public void ReadReport_AnOrdinaryMessageSubjectedAsAReceipt_IsNoReport()
    {
        // Arrange
        var message = Parse(
            """
            From: anna@example.test
            Subject: Read: Lunch

            Your message was read.
            """);

        // Act
        var report = MailReceiptReading.ReadReport(message);

        // Assert
        Assert.Null(report);
    }

    /// <summary>A request names where the receipt would go, and says when that is somebody the message did not come from.</summary>
    [Theory]
    [InlineData("anna@example.test", false)]
    [InlineData("ANNA@example.test", false)]
    [InlineData("bounces@example.test", false)]
    [InlineData("collector@elsewhere.test", true)]
    public void ReadRequest_AMessageAskingForAReadReceipt_SaysWhetherItNamesAnotherAddress(
        string requestedAddress,
        bool namesAnotherAddress)
    {
        // Arrange
        var message = Parse(
            $"""
            Return-Path: <bounces@example.test>
            From: Anna <anna@example.test>
            Disposition-Notification-To: <{requestedAddress}>
            Subject: Lunch

            Shall we?
            """);

        // Act
        var request = MailReceiptReading.ReadRequest(message);

        // Assert
        Assert.NotNull(request);
        Assert.Equal(requestedAddress, request.Address);
        Assert.Equal(namesAnotherAddress, request.NamesAnotherAddress);
    }

    /// <summary>A message with no usable request asks for nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Disposition-Notification-To: not an address\r\n")]
    public void ReadRequest_AMessageWithNoUsableRequest_AsksForNothing(string header)
    {
        // Arrange
        var message = Parse($"From: anna@example.test\r\n{header}Subject: Lunch\r\n\r\nShall we?\r\n");

        // Act
        var request = MailReceiptReading.ReadRequest(message);

        // Assert
        Assert.Null(request);
    }

    private static string DeliveryReport(string recipientGroups) =>
        $"""
        From: Mail Delivery System <mailer-daemon@relay.example.test>
        To: owner@example.test
        Subject: Delivery Status Notification
        MIME-Version: 1.0
        Content-Type: multipart/report; report-type=delivery-status; boundary="report"

        --report
        Content-Type: text/plain

        This is a delivery report.

        --report
        Content-Type: message/delivery-status

        Reporting-MTA: dns; relay.example.test
        Original-Envelope-Id: 0192a1b2-0000-7000-8000-000000000001

        {recipientGroups}

        --report--

        """;

    private static string DispositionReport(string disposition) =>
        $"""
        From: anna@example.test
        To: owner@example.test
        Subject: Read: Lunch
        MIME-Version: 1.0
        Content-Type: multipart/report; report-type=disposition-notification; boundary="report"

        --report
        Content-Type: text/plain

        Your message was displayed.

        --report
        Content-Type: message/disposition-notification

        Reporting-UA: mail.example.test; Example Mail
        Final-Recipient: rfc822; anna@example.test
        Original-Message-ID: <sent-1@example.test>
        Disposition: {disposition}

        --report--

        """;

    private static MimeMessage Parse(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\r\n")));

        return MimeMessage.Load(stream);
    }
}
