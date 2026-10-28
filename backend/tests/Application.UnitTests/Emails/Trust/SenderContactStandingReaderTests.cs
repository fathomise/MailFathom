// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Trust;
using MailFathom.Application.Mail;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Trust;

/// <summary>Covers what a message's sender is to the person reading it: whose contact recognized them, and whether the reader may write them down.</summary>
public sealed class SenderContactStandingReaderTests
{
    private const string Sender = "anna@partner.example";

    private const string OwnAddress = "me@home.example";

    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("0199f000-0000-7000-8000-0000000000a1");

    private static readonly Guid Organization = Guid.Parse("0199f000-0000-7000-8000-0000000000f1");

    /// <summary>A sender nobody holds is offered as a new contact.</summary>
    [Fact]
    public async Task ReadAsync_SenderNoBookHolds_OffersANewContact()
    {
        // Arrange
        var reader = ReaderOver(new InMemoryContactBookStore());

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(Sender));

        // Assert
        Assert.NotNull(standing.ContactAddition);
        Assert.Null(standing.ContactAddition.RestatedContactId);
    }

    /// <summary>The owner's own address is never offered, because it is already theirs.</summary>
    [Fact]
    public async Task ReadAsync_SenderIsAnOwnAddress_OffersNothing()
    {
        // Arrange
        var reader = ReaderOver(new InMemoryContactBookStore());

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(OwnAddress));

        // Assert
        Assert.Null(standing.ContactAddition);
    }

    /// <summary>An address the reader wrote down themselves, or an administrator wrote into their book, already counts.</summary>
    /// <param name="writer">Who wrote the address into the reader's own book.</param>
    [Theory]
    [InlineData(ContactAddressWriterKind.Person)]
    [InlineData(ContactAddressWriterKind.Administrator)]
    public async Task ReadAsync_SenderTheReadersOwnBookHoldsUnderATrustGrantingWriter_OffersNothing(ContactAddressWriterKind writer)
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        store.Hold(ContactBookHolder.Of(SyntheticUser.Deployment), ContactOf(Sender), WriterOf(writer));
        var reader = ReaderOver(store);

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(Sender));

        // Assert
        Assert.Null(standing.ContactAddition);
    }

    /// <summary>An address the reader's organization holds under an administrator already counts for every member.</summary>
    [Fact]
    public async Task ReadAsync_SenderTheOrganizationBookHoldsUnderAnAdministrator_OffersNothing()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        store.Hold(
            ContactBookHolder.OfOrganization(Organization),
            ContactOf(Sender, Organization),
            WriterOf(ContactAddressWriterKind.Administrator));
        var reader = ReaderOver(store, new StatedAdministrativeTargets().WithUser(SyntheticUser.Deployment, Organization));

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(Sender));

        // Assert
        Assert.Null(standing.ContactAddition);
    }

    /// <summary>
    /// The reader's own contact holding the address under a writer whose word grants nothing is named, so the act states
    /// the address on it again rather than recording a second person.
    /// </summary>
    /// <param name="writer">Who wrote the address into the reader's own book.</param>
    [Theory]
    [InlineData(ContactAddressWriterKind.Agent)]
    [InlineData(ContactAddressWriterKind.Unrecorded)]
    public async Task ReadAsync_SenderTheReadersOwnBookHoldsUnderAWriterGrantingNothing_OffersToRestateThatContact(
        ContactAddressWriterKind writer)
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var held = ContactOf(Sender);
        store.Hold(ContactBookHolder.Of(SyntheticUser.Deployment), held, WriterOf(writer));
        var reader = ReaderOver(store);

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(Sender));

        // Assert
        Assert.Equal(held.Id, standing.ContactAddition?.RestatedContactId);
    }

    /// <summary>Writing a contact down needs both contact grants, because the act reads the book and writes it.</summary>
    /// <param name="holdsRead">Whether the one contact grant the caller holds beside the mail read is the reading one rather than the writing one.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAsync_CallerWithoutBothContactGrants_OffersNothing(bool holdsRead)
    {
        // Arrange
        var permission = holdsRead ? MailFathomPermission.ContactsRead : MailFathomPermission.ContactsWrite;
        var reader = ReaderOver(
            new InMemoryContactBookStore(),
            authorization: AccessAuthorizations.ForUserGranted(SyntheticUser.Deployment, MailFathomPermission.MailRead, permission));

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(Sender));

        // Assert
        Assert.Null(standing.ContactAddition);
    }

    /// <summary>A personal contact the reader's own book holds is theirs.</summary>
    [Fact]
    public async Task ReadAsync_RecognizingContactInTheReadersOwnBook_NamesTheReader()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var held = ContactOf(Sender);
        store.Hold(ContactBookHolder.Of(SyntheticUser.Deployment), held, WriterOf(ContactAddressWriterKind.Person));
        var reader = ReaderOver(store);

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(Sender, recognizedBy: held.Id));

        // Assert
        Assert.Equal(RecognizingContactHolder.Reader, standing.RecognizingContactHolder);
    }

    /// <summary>A personal contact another user of the mailbox holds is answered as theirs and as nothing more, even to a reader without contact grants.</summary>
    [Fact]
    public async Task ReadAsync_RecognizingContactInAnotherUsersBook_NamesAnotherUser()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var held = ContactOf(Sender);
        store.Hold(ContactBookHolder.Of(SyntheticUser.Another), held, WriterOf(ContactAddressWriterKind.Person));
        var reader = ReaderOver(
            store,
            authorization: AccessAuthorizations.ForUserGranted(SyntheticUser.Deployment, MailFathomPermission.MailRead));

        // Act
        var standing = await StandingOfAsync(reader, MessageFrom(Sender, recognizedBy: held.Id));

        // Assert
        Assert.Equal(RecognizingContactHolder.AnotherUser, standing.RecognizingContactHolder);
        Assert.Null(standing.ContactAddition);
    }

    /// <summary>A verdict no personal contact reached says nothing about whose contact it was.</summary>
    [Fact]
    public async Task ReadAsync_VerdictAnOrganizationContactReached_NamesNoHolder()
    {
        // Arrange
        var reader = ReaderOver(new InMemoryContactBookStore());
        var message = MessageFrom(Sender, recognizedBy: ContactId.Create(Guid.CreateVersion7()), SenderTrustSource.OrganizationContact);

        // Act
        var standing = await StandingOfAsync(reader, message);

        // Assert
        Assert.Null(standing.RecognizingContactHolder);
    }

    /// <summary>
    /// A drawn conversation is answered from one lookup of its senders' holdings and one of the contacts its verdicts
    /// name, whatever number of its messages share a sender or arrive twice, and every distinct message is answered.
    /// </summary>
    [Fact]
    public async Task ReadAsync_SeveralMessagesSharingASenderAndOneArrivingTwice_AnswersEachFromTwoLookups()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var held = ContactOf(Sender);
        store.Hold(ContactBookHolder.Of(SyntheticUser.Deployment), held, WriterOf(ContactAddressWriterKind.Agent));
        var reader = ReaderOver(store);

        var unrecognized = MessageFrom(Sender);
        var recognized = MessageFrom(Sender, recognizedBy: held.Id);
        var fromAStranger = MessageFrom("bob@elsewhere.example");

        // Act
        var standings = await reader.ReadAsync(
            [unrecognized, recognized, fromAStranger, unrecognized],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, store.BatchedLookupCount);
        Assert.Equal(3, standings.Count);
        Assert.Equal(held.Id, standings[unrecognized.StoredEmailId].ContactAddition?.RestatedContactId);
        Assert.Equal(held.Id, standings[recognized.StoredEmailId].ContactAddition?.RestatedContactId);
        Assert.Equal(RecognizingContactHolder.Reader, standings[recognized.StoredEmailId].RecognizingContactHolder);
        Assert.Null(standings[unrecognized.StoredEmailId].RecognizingContactHolder);
        Assert.NotNull(standings[fromAStranger.StoredEmailId].ContactAddition);
        Assert.Null(standings[fromAStranger.StoredEmailId].ContactAddition?.RestatedContactId);
    }

    /// <summary>A message that displays no author is answered with nothing, and costs no read of the books.</summary>
    [Fact]
    public async Task ReadAsync_MessageWithoutAFromMailbox_AnswersNothing()
    {
        // Arrange
        var store = new InMemoryContactBookStore();
        var reader = ReaderOver(store);

        // Act
        var standing = await StandingOfAsync(reader, MessageWith([]));

        // Assert
        Assert.Same(SenderContactStanding.Unanswered, standing);
        Assert.Equal(0, store.BatchedLookupCount);
    }

    private static async Task<SenderContactStanding> StandingOfAsync(SenderContactStandingReader reader, ReadEmailContent message)
    {
        var standings = await reader.ReadAsync([message], TestContext.Current.CancellationToken);

        return standings[message.StoredEmailId];
    }

    private static SenderContactStandingReader ReaderOver(
        InMemoryContactBookStore store,
        StatedAdministrativeTargets? placements = null,
        AccessAuthorization? authorization = null)
    {
        var granted = authorization ?? AccessAuthorizations.ForUserGranted(
            SyntheticUser.Deployment,
            MailFathomPermission.MailRead,
            MailFathomPermission.ContactsRead,
            MailFathomPermission.ContactsWrite);
        var policies = Substitute.For<ISenderTrustPolicyReader>();
        policies.GetTrustPolicy(Account).Returns(SenderTrustPolicy.Create([Address(OwnAddress)], []));

        return new SenderContactStandingReader(
            granted,
            ContactBookOwnerships.For(granted, new StubMailAccountAssignments(), placements ?? new StatedAdministrativeTargets()),
            store,
            policies);
    }

    private static ReadEmailContent MessageFrom(
        string author,
        ContactId? recognizedBy = null,
        SenderTrustSource source = SenderTrustSource.PersonalContact) =>
        MessageWith([new EmailParticipant(EmailAddressRole.From, Address(author))]) with
        {
            SenderVerification = new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = recognizedBy is null ? SenderTrustLevel.Unknown : SenderTrustLevel.Trusted,
                DeploymentTrustGrantedBy = recognizedBy is null ? SenderTrustSource.None : source,
                RecognizingContactId = recognizedBy,
            },
        };

    private static ReadEmailContent MessageWith(IReadOnlyList<EmailParticipant> participants) => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = Account,
        Folder = new StoredEmailFolder(MailFolderId.Create(Guid.CreateVersion7()), "INBOX", Alias: null),
        Headers = new EmailContentHeaders(
            "Invoice",
            SentAt: null,
            ReceivedAt: null,
            participants,
            EmailThreadReferences.Create(messageId: null, inReplyTo: null, references: null)),
        Body = EmailContentBody.EncryptedNotReadableLocally,
        Attachments = [],
        RemoteFlags = RemoteEmailFlagSnapshot.NeverObserved,
        SenderVerification = SenderVerification.NotEstablished,
        SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
    };

    private static Contact ContactOf(string address, Guid? organization = null) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create("Anna Kowalska"),
            [Address(address)],
            Address(address),
            note: null,
            ContactOrigin.Asserted,
            Now,
            Now,
            organization);

    private static ContactAddressWriter WriterOf(ContactAddressWriterKind kind) =>
        new(kind, kind is ContactAddressWriterKind.Person ? SyntheticUser.Deployment : null, Credential: null);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
}
