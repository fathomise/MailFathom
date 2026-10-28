// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Trust;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Trust;

/// <summary>Covers which contacts a receiving account's books hold for a sender count, and how a batch of messages is judged.</summary>
public sealed class SenderTrustJudgeTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("0199f000-0000-7000-8000-0000000000a1");

    private static readonly MailAccountId OtherAccount = MailAccountId.Create("0199f000-0000-7000-8000-0000000000a2");

    private static readonly ContactId FirstContact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c1"));

    private static readonly ContactId SecondContact = ContactId.Create(Guid.Parse("0199f000-0000-7000-8000-0000000000c2"));

    /// <summary>An address a person or an administrator wrote down makes its authenticated sender trusted, under the book that holds it.</summary>
    [Theory]
    [InlineData(ContactAddressWriterKind.Person, SenderTrustBook.Personal, SenderTrustSource.PersonalContact)]
    [InlineData(ContactAddressWriterKind.Administrator, SenderTrustBook.Personal, SenderTrustSource.PersonalContact)]
    [InlineData(ContactAddressWriterKind.Administrator, SenderTrustBook.Organization, SenderTrustSource.OrganizationContact)]
    [InlineData(ContactAddressWriterKind.Person, SenderTrustBook.Organization, SenderTrustSource.OrganizationContact)]
    public async Task JudgeAsync_AddressAPersonOrAnAdministratorWrote_TrustsTheSender(
        ContactAddressWriterKind writer,
        SenderTrustBook book,
        SenderTrustSource expected)
    {
        // Arrange
        var judge = JudgeHolding(Holding(Account, "alice@partner.example", FirstContact, book, writer));

        // Act
        var judged = await JudgeOneAsync(judge, MessageFrom("alice@partner.example"));

        // Assert
        Assert.Equal(expected, judged.SenderTrust.GrantedBy);
        Assert.Equal(FirstContact, judged.SenderTrust.RecognizingContactId);
    }

    /// <summary>
    /// An address an agent wrote, or one backfilled from before writers were recorded, is a record and never a grant, so
    /// it leaves the sender unknown however the book holding it was written otherwise.
    /// </summary>
    [Theory]
    [InlineData(ContactAddressWriterKind.Agent)]
    [InlineData(ContactAddressWriterKind.Unrecorded)]
    public async Task JudgeAsync_AddressNobodyWithAuthorityWrote_LeavesTheSenderUnknown(ContactAddressWriterKind writer)
    {
        // Arrange
        var judge = JudgeHolding(Holding(Account, "alice@partner.example", FirstContact, SenderTrustBook.Personal, writer));

        // Act
        var judged = await JudgeOneAsync(judge, MessageFrom("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, judged.SenderTrust.Level);
        Assert.Null(judged.SenderTrust.RecognizingContactId);
    }

    /// <summary>
    /// An address nobody with authority wrote never makes a sender known either, so a relaxed account leaves a message
    /// nothing authenticated unknown where only such an address holds its sender.
    /// </summary>
    [Theory]
    [InlineData(ContactAddressWriterKind.Agent)]
    [InlineData(ContactAddressWriterKind.Unrecorded)]
    public async Task JudgeAsync_AddressNobodyWithAuthorityWroteAndNothingAuthenticated_LeavesTheSenderUnknown(ContactAddressWriterKind writer)
    {
        // Arrange
        var judge = JudgeHolding(Holding(Account, "alice@partner.example", FirstContact, SenderTrustBook.Personal, writer));
        var message = MessageWith(
            SenderAuthentication.NotEstablished(),
            [new EmailParticipant(EmailAddressRole.From, AddressOf("alice@partner.example"))]);

        // Act
        var judged = await JudgeOneAsync(judge, message);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, judged.SenderTrust.Level);
    }

    /// <summary>On a relaxed account, a person's contact makes a sender nothing authenticated known, under the contact that holds them.</summary>
    [Fact]
    public async Task JudgeAsync_PersonsContactAndNothingAuthenticated_KnowsTheSender()
    {
        // Arrange
        var judge = JudgeHolding(Holding(Account, "alice@partner.example", FirstContact, SenderTrustBook.Personal, ContactAddressWriterKind.Person));
        var message = MessageWith(
            SenderAuthentication.NotEstablished(),
            [new EmailParticipant(EmailAddressRole.From, AddressOf("Alice@Partner.example"))]);

        // Act
        var judged = await JudgeOneAsync(judge, message);

        // Assert
        Assert.Equal(SenderTrustLevel.Known, judged.SenderTrust.Level);
        Assert.Equal(FirstContact, judged.SenderTrust.RecognizingContactId);
    }

    /// <summary>A contact in a book another account reaches says nothing about mail this account received.</summary>
    [Fact]
    public async Task JudgeAsync_ContactInABookOnlyAnotherAccountReaches_LeavesTheSenderUnknown()
    {
        // Arrange
        var judge = JudgeHolding(Holding(OtherAccount, "alice@partner.example", FirstContact, SenderTrustBook.Personal, ContactAddressWriterKind.Person));

        // Act
        var judged = await JudgeOneAsync(judge, MessageFrom("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, judged.SenderTrust.Level);
    }

    /// <summary>Two contacts holding the address in one book recognize the sender as the same one whichever row arrived first.</summary>
    [Fact]
    public async Task JudgeAsync_TwoContactsHoldTheAddressInOneBook_NamesTheSameOneEitherWay()
    {
        // Arrange
        var first = Holding(Account, "alice@partner.example", FirstContact, SenderTrustBook.Personal, ContactAddressWriterKind.Person);
        var second = Holding(Account, "alice@partner.example", SecondContact, SenderTrustBook.Personal, ContactAddressWriterKind.Person);

        // Act
        var inOrder = await JudgeOneAsync(JudgeHolding(first, second), MessageFrom("alice@partner.example"));
        var reversed = await JudgeOneAsync(JudgeHolding(second, first), MessageFrom("alice@partner.example"));

        // Assert
        Assert.Equal(FirstContact, inOrder.SenderTrust.RecognizingContactId);
        Assert.Equal(FirstContact, reversed.SenderTrust.RecognizingContactId);
    }

    /// <summary>A contact is matched against the mailbox <c>From</c> displays, and never against whoever <c>Sender</c> names.</summary>
    [Fact]
    public async Task JudgeAsync_ContactHoldsTheSubmittingSender_LeavesTheDisplayedAuthorUnknown()
    {
        // Arrange
        var judge = JudgeHolding(Holding(Account, "assistant@partner.example", FirstContact, SenderTrustBook.Personal, ContactAddressWriterKind.Person));
        var message = MessageWith(
            WrittenBy("partner.example"),
            [
                new EmailParticipant(EmailAddressRole.Sender, AddressOf("assistant@partner.example")),
                new EmailParticipant(EmailAddressRole.From, AddressOf("mallory@partner.example")),
            ]);

        // Act
        var judged = await JudgeOneAsync(judge, message);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, judged.SenderTrust.Level);
    }

    /// <summary>A batch locks and reads the books once, for every distinct first <c>From</c> mailbox and receiving account.</summary>
    [Fact]
    public async Task JudgeAsync_SeveralMessages_ReadsTheBooksOnceForEveryDistinctSender()
    {
        // Arrange
        var books = BooksHolding();
        var judge = new SenderTrustJudge(PoliciesRecognizingNobody(), books);
        ExtractedEmailMetadata[] messages =
        [
            MessageFrom("alice@partner.example"),
            MessageFrom("ALICE@partner.example"),
            MessageFrom("bob@partner.example", OtherAccount),
        ];

        // Act
        var judged = await judge.JudgeAsync(Substitute.For<IPersistenceSession>(), messages, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(messages.Select(static message => message.AccountId), judged.Select(static message => message.AccountId));
        await books.Received(1).LockAndReadAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<IReadOnlyCollection<MailAccountId>>(accounts => accounts.Count == 2),
            Arg.Is<IReadOnlyCollection<EmailAddress>>(senders => senders.Count == 2),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A batch whose messages name no author takes no lock and reads no book, and still records a verdict for each.</summary>
    [Fact]
    public async Task JudgeAsync_NoMessageNamesAnAuthor_ReadsNoBook()
    {
        // Arrange
        var books = BooksHolding();
        var judge = new SenderTrustJudge(PoliciesRecognizingNobody(), books);

        // Act
        var judged = await JudgeOneAsync(judge, MessageWith(WrittenBy("partner.example"), []));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, judged.SenderTrust.Level);
        await books.DidNotReceiveWithAnyArgs().LockAndReadAsync(default!, default!, default!, TestContext.Current.CancellationToken);
    }

    private static async Task<ExtractedEmailMetadata> JudgeOneAsync(SenderTrustJudge judge, ExtractedEmailMetadata message)
    {
        var judged = await judge.JudgeAsync(
            Substitute.For<IPersistenceSession>(),
            [message],
            TestContext.Current.CancellationToken);

        return Assert.Single(judged);
    }

    private static SenderTrustJudge JudgeHolding(params SenderAddressHolding[] holdings) =>
        new(PoliciesRecognizingNobody(), BooksHolding(holdings));

    private static ISenderTrustBooks BooksHolding(params SenderAddressHolding[] holdings)
    {
        var books = Substitute.For<ISenderTrustBooks>();

        books.LockAndReadAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<IReadOnlyCollection<MailAccountId>>(),
                Arg.Any<IReadOnlyCollection<EmailAddress>>(),
                Arg.Any<CancellationToken>())
            .Returns(holdings);

        return books;
    }

    private static ISenderTrustPolicyReader PoliciesRecognizingNobody()
    {
        var policies = Substitute.For<ISenderTrustPolicyReader>();

        policies.GetTrustPolicy(Arg.Any<MailAccountId>()).Returns(SenderTrustPolicy.RecognizingNobody);

        return policies;
    }

    private static SenderAddressHolding Holding(
        MailAccountId account,
        string address,
        ContactId contact,
        SenderTrustBook book,
        ContactAddressWriterKind writer) =>
        new(account, AddressOf(address).NormalizedAddress, contact, book, writer);

    private static ExtractedEmailMetadata MessageFrom(string from, MailAccountId? account = null) =>
        MessageWith(
            WrittenBy(from[(from.IndexOf('@', StringComparison.Ordinal) + 1)..]),
            [new EmailParticipant(EmailAddressRole.From, AddressOf(from))],
            account);

    private static ExtractedEmailMetadata MessageWith(
        SenderAuthentication authentication,
        IReadOnlyList<EmailParticipant> participants,
        MailAccountId? account = null) =>
        new(
            account ?? Account,
            Subject: "Subject",
            SentAt: null,
            ReceivedAt: null,
            participants,
            EmailThreadReferences.None,
            EmailAttachmentSummary.None,
            ExtractedEmailText.FromPlainTextBody("body", "body"),
            authentication);

    private static SenderAuthentication WrittenBy(string domain)
    {
        Assert.True(SenderDomain.TryCreate(domain, out var author));

        return SenderAuthentication.Authenticated([author], spfDomains: [], author, DmarcOutcome.Pass, DmarcRecordLookup.NotPublished);
    }

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }
}
