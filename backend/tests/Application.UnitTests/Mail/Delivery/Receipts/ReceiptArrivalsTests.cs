// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Receipts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Receipts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Receipts;

/// <summary>Covers how an arriving report finds the account's own message and its recipient, and when a request is answerable.</summary>
public sealed class ReceiptArrivalsTests
{
    private const string OwnAddress = "owner@example.test";

    private static readonly MailAccountId Account = SyntheticMailAccount.Deployment;

    private static readonly DateTimeOffset ReportWrittenAt = new(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly OutgoingEmailId SentId = OutgoingEmailId.Create(Guid.Parse("0192a1b2-0000-7000-8000-000000000001"));

    private static readonly string[] OriginalMessageIds = ["sent-1@example.test"];

    private static readonly string[] ReferencedNearestFirst = ["parent@example.test", "newer@example.test", "older@example.test"];

    private static readonly ReportedOutgoingEmail Sent = new(SentId, "Lunch", ["anna@example.test", "bert@example.test"]);

    private readonly IReceiptReportStore store = Substitute.For<IReceiptReportStore>();

    private readonly ReceiptArrivals arrivals;

    /// <summary>Initializes the arrivals over a store that finds nothing until a test says otherwise.</summary>
    public ReceiptArrivalsTests()
    {
        var identities = Substitute.For<IOutgoingSenderIdentityReader>();
        identities.FindSenderIdentity(Account).Returns(OutgoingSenderIdentity.Create(Account, Address(OwnAddress)));

        this.arrivals = new ReceiptArrivals(this.store, identities, new FakeTimeProvider(ReportWrittenAt.AddDays(1)));
    }

    /// <summary>The envelope identifier is the send's own, so it decides before anything a report wrote about a message.</summary>
    [Fact]
    public async Task ReadAsync_ADeliveryReportNamingTheSendsEnvelope_MatchesThatSend()
    {
        // Arrange
        this.store.FindOutgoingEmailAsync(Account, SentId, Arg.Any<CancellationToken>()).Returns(Sent);
        var report = new ReceiptReport(
            ReceiptKind.Delivery,
            [new ReceiptReportRecipient("bert@example.test", null, ReceiptOutcome.Failed, "5.1.1", "mx.example.test")])
        {
            OriginalEnvelopeId = SentId.ToString(),
        };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, Arriving(report), isFiledCopy: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(Sent, receipts.ReportedOutgoingEmail);
        var update = Assert.Single(receipts.Outcomes);
        Assert.Equal(1, update.RecipientOrdinal);
        Assert.Equal(new RecordedReceiptOutcome(ReceiptOutcome.Failed, ReportWrittenAt, ReceiptOutcomeSource.Report, "5.1.1", "mx.example.test"), update.Outcome);
        await this.store.DidNotReceiveWithAnyArgs().FindOutgoingEmailByMessageIdAsync(default, default!, TestContext.Current.CancellationToken);
    }

    /// <summary>A read receipt names the message it is about, which is the next thing a report is matched by.</summary>
    [Fact]
    public async Task ReadAsync_AReadReceiptNamingTheOriginalMessage_MatchesBySentMessageId()
    {
        // Arrange
        this.store.FindOutgoingEmailByMessageIdAsync(Account, Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(OriginalMessageIds)), Arg.Any<CancellationToken>())
            .Returns(Sent);
        var report = new ReceiptReport(ReceiptKind.Read, [new ReceiptReportRecipient("anna@example.test", null, ReceiptOutcome.Displayed, null, null)])
        {
            OriginalMessageId = "sent-1@example.test",
        };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, Arriving(report), isFiledCopy: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(0, Assert.Single(receipts.Outcomes).RecipientOrdinal);
    }

    /// <summary>A report naming neither is matched by what it replies to, its nearest ancestor first.</summary>
    [Fact]
    public async Task ReadAsync_AReportNamingNoMessage_MatchesByWhatItRepliesTo()
    {
        // Arrange
        this.store.FindOutgoingEmailByMessageIdAsync(Account, Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(ReferencedNearestFirst)), Arg.Any<CancellationToken>())
            .Returns(Sent);
        var report = new ReceiptReport(ReceiptKind.Read, [new ReceiptReportRecipient("anna@example.test", null, ReceiptOutcome.Displayed, null, null)]);
        var metadata = Arriving(report) with
        {
            ThreadReferences = EmailThreadReferences.Create("report@example.test", "parent@example.test", ["older@example.test", "newer@example.test"]),
        };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, metadata, isFiledCopy: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(Sent, receipts.ReportedOutgoingEmail);
    }

    /// <summary>A report about nothing the account sent is still the report it is, and records no outcome against anybody.</summary>
    [Fact]
    public async Task ReadAsync_AReportMatchingNoSend_IsMarkedWithNoOutcome()
    {
        // Arrange
        var report = new ReceiptReport(ReceiptKind.Read, [new ReceiptReportRecipient("anna@example.test", null, ReceiptOutcome.Displayed, null, null)])
        {
            OriginalMessageId = "somebody-elses@example.test",
        };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, Arriving(report), isFiledCopy: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(report, receipts.Report);
        Assert.Null(receipts.ReportedOutgoingEmail);
        Assert.Empty(receipts.Outcomes);
    }

    /// <summary>The address the send was addressed to decides before the one it ended up at, and a stranger is nobody's.</summary>
    [Fact]
    public async Task ReadAsync_RecipientsTheReportNames_AreMatchedByTheAddressTheSendNamed()
    {
        // Arrange
        this.store.FindOutgoingEmailAsync(Account, SentId, Arg.Any<CancellationToken>()).Returns(Sent);
        var report = new ReceiptReport(
            ReceiptKind.Delivery,
            [
                new ReceiptReportRecipient("anna@forwarded.test", "ANNA@example.test", ReceiptOutcome.Delivered, null, null),
                new ReceiptReportRecipient("stranger@example.test", null, ReceiptOutcome.Delivered, null, null),
            ])
        {
            OriginalEnvelopeId = SentId.ToString(),
        };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, Arriving(report), isFiledCopy: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(0, Assert.Single(receipts.Outcomes).RecipientOrdinal);
    }

    /// <summary>A send to one person takes a report about an address it does not name as that person's, which is what a forward looks like.</summary>
    [Fact]
    public async Task ReadAsync_ASendToOnePersonAndAReportNamingAnotherAddress_RecordsItAgainstThatPerson()
    {
        // Arrange
        this.store.FindOutgoingEmailAsync(Account, SentId, Arg.Any<CancellationToken>())
            .Returns(Sent with { RecipientAddresses = ["anna@example.test"] });
        var report = new ReceiptReport(ReceiptKind.Delivery, [new ReceiptReportRecipient("anna@forwarded.test", null, ReceiptOutcome.Delivered, null, null)])
        {
            OriginalEnvelopeId = SentId.ToString(),
        };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, Arriving(report), isFiledCopy: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(0, Assert.Single(receipts.Outcomes).RecipientOrdinal);
    }

    /// <summary>A request somebody else's message makes is kept, so the person can answer it.</summary>
    [Fact]
    public async Task ReadAsync_AMessageAskingForAReadReceipt_KeepsTheRequest()
    {
        // Arrange
        var request = new ReadReceiptRequest("anna@example.test", NamesAnotherAddress: false);
        var metadata = Metadata("anna@example.test") with { ReadReceiptRequest = request };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, metadata, isFiledCopy: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(receipts);
        Assert.Equal(request, receipts.ReadReceiptRequest);
        Assert.Null(receipts.Report);
    }

    /// <summary>The account's own message asks nobody here for a receipt, however it came back into the mailbox.</summary>
    [Theory]
    [InlineData(OwnAddress, "anna@example.test", false)]
    [InlineData("anna@example.test", OwnAddress, false)]
    [InlineData("anna@example.test", "anna@example.test", true)]
    public async Task ReadAsync_TheAccountsOwnMessage_KeepsNoRequest(string from, string requestedAddress, bool isFiledCopy)
    {
        // Arrange
        var metadata = Metadata(from) with { ReadReceiptRequest = new ReadReceiptRequest(requestedAddress, NamesAnotherAddress: false) };

        // Act
        var receipts = await this.arrivals.ReadAsync(Account, metadata, isFiledCopy, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(receipts);
    }

    private static ExtractedEmailMetadata Arriving(ReceiptReport report) =>
        Metadata("mailer-daemon@relay.example.test") with { Report = report, SentAt = ReportWrittenAt };

    private static ExtractedEmailMetadata Metadata(string from) => new(
        Account,
        Subject: "Lunch",
        SentAt: null,
        ReceivedAt: null,
        Participants: [new EmailParticipant(EmailAddressRole.From, Address(from))],
        EmailThreadReferences.None,
        EmailAttachmentSummary.None,
        ExtractedEmailText.NoTextualBody,
        SenderAuthentication.NotEstablished());

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(null, address, out var created) ? created : throw new ArgumentException(address, nameof(address));
}
