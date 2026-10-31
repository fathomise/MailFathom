// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Persistence;
using MailFathom.Application.Portraits;
using MailFathom.Application.StoredFiles;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts;

/// <summary>Covers what a member's view of the books owes the member directory beside them, and what a member may not do to the organization's book.</summary>
/// <remarks>
/// Precedence runs own book, member directory, organization contacts, collected contacts. The member directory's own
/// half of it is covered with the directory; what is asserted here is the half the books answer for: which of their
/// records a colleague outranks, and that the organization's records are read-only to a member and copied on promotion.
/// </remarks>
public sealed class OrganizationBookPrecedenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Organization = AccessAuthorizations.ScopedOrganization;

    private static readonly UserId Reader = SyntheticUser.Deployment;

    private static readonly UserId Colleague = SyntheticUser.Another;

    private readonly InMemoryContactBookStore store = new();

    private readonly IMemberDirectory members = Substitute.For<IMemberDirectory>();

    private readonly IStoredFileStore files = Substitute.For<IStoredFileStore>();

    private readonly StubMailAccountAssignments assignments = new();

    /// <summary>A colleague answers for their own address, so the organization's and a mailbox's record of it give way to them on a listing read beside the directory.</summary>
    [Fact]
    public async Task ReadPageAsync_BesideColleagues_LeavesOutTheOrganizationAndCollectedRecordsOfAColleaguesAddress()
    {
        // Arrange
        this.ColleagueHolds("piotr@nordwind.example");
        this.assignments.Assigning(Reader, SyntheticMailAccount.Deployment);
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), ContactOf("Piotr (biuro)", "piotr@nordwind.example", Organization));
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), ContactOf("Biuro Rachunkowe", "biuro@example.test", Organization));
        this.store.Hold(SyntheticMailAccount.Deployment, ContactOf("piotr@nordwind.example", "piotr@nordwind.example", origin: ContactOrigin.Collected));
        this.store.Hold(SyntheticMailAccount.Deployment, ContactOf("Anna Kowalska", "anna@example.test", origin: ContactOrigin.Collected));

        // Act
        var page = await this.BookReader()
            .ReadPageAsync(new ContactPageRequest { BesideColleagues = true }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Anna Kowalska", "Biuro Rachunkowe"], page.Contacts.Select(contact => contact.DisplayName.Value));
    }

    /// <summary>The reader is not their own colleague, so an address the directory reads as theirs outranks nobody's record of it.</summary>
    [Fact]
    public async Task ReadPageAsync_BesideColleagues_KeepsTheOrganizationsRecordOfTheReadersOwnAddress()
    {
        // Arrange
        this.Composing(MemberOf(Reader, "biuro@example.test"));
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), ContactOf("Biuro Rachunkowe", "biuro@example.test", Organization));

        // Act
        var page = await this.BookReader()
            .ReadPageAsync(new ContactPageRequest { BesideColleagues = true }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Biuro Rachunkowe"], page.Contacts.Select(contact => contact.DisplayName.Value));
    }

    /// <summary>What the reader wrote down themselves always wins, so their own record of a colleague stays on the listing.</summary>
    [Fact]
    public async Task ReadPageAsync_BesideColleagues_KeepsTheReadersOwnRecordOfAColleague()
    {
        // Arrange
        this.ColleagueHolds("piotr@nordwind.example");
        this.store.Hold(Reader, ContactOf("Piotrek", "piotr@nordwind.example"));

        // Act
        var page = await this.BookReader()
            .ReadPageAsync(new ContactPageRequest { BesideColleagues = true }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Piotrek"], page.Contacts.Select(contact => contact.DisplayName.Value));
    }

    /// <summary>A reader with no member directory beside them, such as an agent's listing, still reads every record of the organization's book.</summary>
    [Fact]
    public async Task ReadPageAsync_NotBesideColleagues_KeepsTheOrganizationsRecordOfAColleaguesAddress()
    {
        // Arrange
        this.ColleagueHolds("piotr@nordwind.example");
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), ContactOf("Piotr (biuro)", "piotr@nordwind.example", Organization));

        // Act
        var page = await this.BookReader().ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(page.Contacts);
        await this.members.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The organization's record is its administrators' to change, so a member's amendment is refused rather than answered as a contact they do not hold.</summary>
    [Fact]
    public async Task AmendAsync_AnOrganizationContact_IsRefusedAsReadOnly()
    {
        // Arrange
        var kept = ContactOf("Biuro Rachunkowe", "biuro@example.test", Organization);
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), kept);

        // Act, Assert
        var refusal = await Assert.ThrowsAsync<ContactReadOnlyException>(() =>
            this.Writer().AmendAsync(
                kept.Id,
                new ContactRecordDraft { DisplayName = "Biuro", Addresses = ["biuro@example.test"], PreferredAddress = "biuro@example.test" },
                [],
                ContactAddressWriterKind.Person,
                TestContext.Current.CancellationToken));

        Assert.Equal(51017, refusal.ErrorCode.Value);
        Assert.Equal("Biuro Rachunkowe", Assert.Single(this.store.Contacts).DisplayName.Value);
    }

    /// <summary>Erasing a person from the organization's book is its administrators' act, so a member's erasure removes nothing.</summary>
    [Fact]
    public async Task EraseAsync_AnOrganizationContact_IsRefusedAndRemovesNothing()
    {
        // Arrange
        var kept = ContactOf("Biuro Rachunkowe", "biuro@example.test", Organization);
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), kept);

        // Act, Assert
        await Assert.ThrowsAsync<ContactReadOnlyException>(() =>
            this.Writer().EraseAsync(kept.Id, TestContext.Current.CancellationToken));

        Assert.Equal(1, this.store.ContactCount);
    }

    /// <summary>Promoting an organization contact gives the member a copy of their own and leaves the organization's record where it is.</summary>
    [Fact]
    public async Task PromoteAsync_AnOrganizationContact_CopiesItIntoTheReadersOwnBook()
    {
        // Arrange
        var kept = Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create("Biuro Rachunkowe"),
            [Address("biuro@example.test"), Address("faktury@example.test")],
            Address("faktury@example.test"),
            ContactNote.Create("Invoices go to the second address."),
            ContactOrigin.Asserted,
            Now,
            Now,
            Organization,
            phoneNumbers: [ContactPhoneNumber.Create("+48 22 100 20 30", ContactPhoneLabel.Work)]);
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), kept);

        // Act
        var result = await this.Writer().PromoteAsync(kept.Id, ContactAddressWriterKind.Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);

        var copy = Assert.Single(this.store.ContactsOf(Reader));

        Assert.NotEqual(kept.Id, copy.Id);
        Assert.Null(copy.Organization);
        Assert.Equal("Biuro Rachunkowe", copy.DisplayName.Value);
        Assert.Equal(kept.Addresses, copy.Addresses);
        Assert.Equal(kept.PreferredAddress, copy.PreferredAddress);
        Assert.Equal(kept.Note, copy.Note);
        Assert.Equal(kept.PhoneNumbers, copy.PhoneNumbers);
        Assert.Equal(2, this.store.ContactCount);
    }

    /// <summary>A photo is part of the record, so replacing the organization's is refused before anything is stored.</summary>
    [Fact]
    public async Task ReplaceAsync_AnOrganizationContactsPhoto_IsRefusedBeforeAFileIsWritten()
    {
        // Arrange
        var kept = ContactOf("Biuro Rachunkowe", "biuro@example.test", Organization);
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), kept);
        var photo = Portrait.Of(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 })!;

        // Act, Assert
        await Assert.ThrowsAsync<ContactReadOnlyException>(() =>
            this.Photos().ReplaceAsync(kept.Id, photo, TestContext.Current.CancellationToken));

        await this.files.DidNotReceiveWithAnyArgs().WriteAsync(default, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Taking a photo off is as much a change to the record as putting one on, so removing the organization's is refused before any file is touched.</summary>
    [Fact]
    public async Task RemoveAsync_AnOrganizationContactsPhoto_IsRefusedWithoutTouchingTheFileStore()
    {
        // Arrange
        var kept = ContactOf("Biuro Rachunkowe", "biuro@example.test", Organization);
        this.store.Hold(ContactBookHolder.OfOrganization(Organization), kept);

        // Act, Assert
        await Assert.ThrowsAsync<ContactReadOnlyException>(() =>
            this.Photos().RemoveAsync(kept.Id, TestContext.Current.CancellationToken));

        Assert.Empty(this.files.ReceivedCalls());
    }

    private static Contact ContactOf(
        string displayName,
        string address,
        Guid? organization = null,
        ContactOrigin origin = ContactOrigin.Asserted) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create(displayName),
            [Address(address)],
            Address(address),
            note: null,
            origin,
            Now,
            Now,
            organization);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");

    private static OptimisticConcurrencyRetryPolicy CommitPolicy()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        return new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), new FakeTimeProvider(Now));
    }

    private static AccessAuthorization Authorization() =>
        AccessAuthorizations.ForUserGranted(Reader, MailFathomPermission.ContactsRead, MailFathomPermission.ContactsWrite);

    private static MemberContact MemberOf(UserId member, string address) =>
        new(
            member,
            ContactDisplayName.Create("Piotr Zieliński"),
            [Address(address)],
            Address(address),
            [],
            JobTitle: null,
            "Nordwind",
            Portrait: null,
            TimeZone: null,
            WorkingHours: null);

    private ContactBookOwnership Ownership(AccessAuthorization authorization) =>
        ContactBookOwnerships.For(
            authorization,
            this.assignments,
            new StatedAdministrativeTargets().WithUser(Reader, Organization).WithUser(Colleague, Organization));

    private void ColleagueHolds(string address) => this.Composing(MemberOf(Colleague, address));

    private void Composing(params MemberContact[] composed) =>
        this.members.ReadAsync(Organization, Arg.Any<CancellationToken>()).Returns(composed);

    private ContactBookReader BookReader()
    {
        var authorization = Authorization();

        return new ContactBookReader(this.store, this.members, this.Ownership(authorization), authorization);
    }

    private ContactBookWriter Writer()
    {
        var authorization = Authorization();
        var timeProvider = new FakeTimeProvider(Now);

        return new ContactBookWriter(
            new ContactBook(this.store, this.store, this.files, CommitPolicy(), timeProvider, authorization, ClientSignalPublishers.ReachingNobody),
            this.store,
            this.Ownership(authorization),
            authorization);
    }

    private ContactPhotos Photos()
    {
        var authorization = Authorization();

        return new ContactPhotos(authorization, this.Ownership(authorization), this.store, this.store, this.files, CommitPolicy(), ClientSignalPublishers.ReachingNobody);
    }

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
