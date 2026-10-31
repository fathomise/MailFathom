// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.StoredFiles;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts.Photos;

/// <summary>Covers which photo each address a read draws is drawn by, and who is told about one at all.</summary>
/// <remarks>
/// The reader's own book is the real in-memory one and the member directory a substitute answering as the host composes
/// it, so what is asserted is the order between the two and the boundaries around them.
/// </remarks>
public sealed class PersonPhotosTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Organization = AccessAuthorizations.ScopedOrganization;

    private static readonly UserId Reader = SyntheticUser.Deployment;

    private static readonly UserId Piotr = SyntheticUser.Another;

    private static readonly Guid ContactPhoto = new("0198f0aa-0000-7000-8000-00000000d0f1");

    private static readonly StoredFileId PiotrsPortrait = StoredFileId.Create(new Guid("0198f0aa-0000-7000-8000-00000000d0f2"));

    private static readonly StoredFileId ReadersPortrait = StoredFileId.Create(new Guid("0198f0aa-0000-7000-8000-00000000d0f3"));

    private readonly InMemoryContactBookStore store = new();

    private readonly IMemberDirectory members = Substitute.For<IMemberDirectory>();

    /// <summary>A person the reader wrote down with a photo is drawn by that photo, named by the contact and the stored file.</summary>
    [Fact]
    public async Task ResolveAsync_AnAddressTheReadersOwnContactHoldsWithAPhoto_IsDrawnByThatPhoto()
    {
        // Arrange
        var contact = ContactOf("Ewa Lis", "ewa@example.org", ContactPhoto);
        this.store.Hold(Reader, contact);

        // Act
        var photos = await this.PhotosFor(Organization).ResolveAsync([Address("EWA@example.org")], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new PersonPhoto(PersonPhotoSource.OwnContact, contact.Id.Value, ContactPhoto), photos[Address("ewa@example.org")]);
    }

    /// <summary>A colleague nobody wrote down is drawn by the portrait their own record holds.</summary>
    [Fact]
    public async Task ResolveAsync_AColleaguesAddress_IsDrawnByTheirPortrait()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, PiotrsPortrait, "piotr@nordwind.example"));

        // Act
        var photos = await this.PhotosFor(Organization).ResolveAsync([Address("piotr@nordwind.example")], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new PersonPhoto(PersonPhotoSource.Member, Piotr.Value, PiotrsPortrait.Value), photos[Address("piotr@nordwind.example")]);
    }

    /// <summary>The reader's own record of a colleague answers for them even without a photo, as their contact on the People screen does.</summary>
    [Fact]
    public async Task ResolveAsync_AColleagueTheReaderWroteDownWithoutAPhoto_IsDrawnByInitials()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, PiotrsPortrait, "piotr@nordwind.example"));
        this.store.Hold(Reader, ContactOf("Piotrek", "piotr@nordwind.example", photo: null));

        // Act
        var photos = await this.PhotosFor(Organization).ResolveAsync([Address("piotr@nordwind.example")], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(photos);
    }

    /// <summary>The organization's own book comes after the directory, so a contact of it naming a colleague's address leaves their portrait.</summary>
    [Fact]
    public async Task ResolveAsync_AColleagueTheOrganizationsBookAlsoHolds_IsStillDrawnByTheirPortrait()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, PiotrsPortrait, "piotr@nordwind.example"));
        this.store.Hold(
            ContactBookHolder.OfOrganization(Organization),
            Contact.Create(
                ContactId.Create(Guid.CreateVersion7(Now)),
                ContactDisplayName.Create("Piotr Zieliński"),
                [Address("piotr@nordwind.example")],
                Address("piotr@nordwind.example"),
                note: null,
                ContactOrigin.Asserted,
                Now,
                Now,
                organization: Organization));

        // Act
        var photos = await this.PhotosFor(Organization).ResolveAsync([Address("piotr@nordwind.example")], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PersonPhotoSource.Member, photos[Address("piotr@nordwind.example")].Source);
    }

    /// <summary>A colleague with no portrait, the reader themselves, and an address nobody holds are all drawn by initials.</summary>
    [Fact]
    public async Task ResolveAsync_NobodyWithAPhotoHoldingTheAddress_IsDrawnByInitials()
    {
        // Arrange
        this.Holding(
            MemberOf(Piotr, portrait: null, "piotr@nordwind.example"),
            MemberOf(Reader, ReadersPortrait, "anna@nordwind.example"));

        // Act
        var photos = await this.PhotosFor(Organization).ResolveAsync(
            [Address("piotr@nordwind.example"), Address("anna@nordwind.example"), Address("stranger@example.net")],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(photos);
    }

    /// <summary>A reader in no organization has no colleagues, so nothing is asked of a directory they are in none of.</summary>
    [Fact]
    public async Task ResolveAsync_AReaderInNoOrganization_ReadsNoDirectory()
    {
        // Act
        var photos = await this.PhotosFor(organization: null).ResolveAsync([Address("piotr@nordwind.example")], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(photos);
        await this.members.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A photo is named only to somebody who could then read it, so a caller without the reading grant is told of none and nothing is read.</summary>
    [Fact]
    public async Task ResolveAsync_ACallerWithoutTheReadingGrant_IsToldOfNoPhoto()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, PiotrsPortrait, "piotr@nordwind.example"));

        // Act
        var photos = await this.PhotosFor(Organization, MailFathomPermission.MailRead)
            .ResolveAsync([Address("piotr@nordwind.example")], TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(photos);
        await this.members.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>One resolution answers one read's worth of addresses, counted once each however often a read repeats them.</summary>
    [Fact]
    public async Task ResolveAsync_MoreDistinctAddressesThanOneReadDraws_IsRefused()
    {
        // Arrange
        EmailAddress[] repeated = [.. Enumerable.Repeat(Address("piotr@nordwind.example"), PersonPhotos.MaximumAddresses + 1)];
        EmailAddress[] distinct = [.. Enumerable.Range(0, PersonPhotos.MaximumAddresses + 1).Select(at => Address($"sender{at}@example.org"))];
        var photos = this.PhotosFor(Organization);

        // Act
        var answered = await photos.ResolveAsync(repeated, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(answered);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            photos.ResolveAsync(distinct, TestContext.Current.CancellationToken));
    }

    private static MemberContact MemberOf(UserId member, StoredFileId? portrait, params string[] addresses)
    {
        EmailAddress[] held = [.. addresses.Select(Address)];

        return new MemberContact(
            member,
            ContactDisplayName.Create("Member"),
            held,
            held.Length > 0 ? held[0] : null,
            [],
            JobTitle: null,
            "Nordwind",
            portrait,
            TimeZone: null,
            WorkingHours: null);
    }

    private static Contact ContactOf(string displayName, string address, Guid? photo) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create(displayName),
            [Address(address)],
            Address(address),
            note: null,
            ContactOrigin.Asserted,
            Now,
            Now,
            photo: photo);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");

    private void Holding(params MemberContact[] composed) =>
        this.members.ReadAsync(Organization, Arg.Any<CancellationToken>()).Returns(composed);

    private PersonPhotos PhotosFor(Guid? organization, params MailFathomPermission[] granted)
    {
        var authorization = AccessAuthorizations.ForUserGranted(
            Reader,
            granted.Length == 0 ? [MailFathomPermission.ContactsRead] : granted);
        var ownership = ContactBookOwnerships.For(
            authorization,
            new StubMailAccountAssignments(),
            new StatedAdministrativeTargets().WithUser(Reader, organization));

        return new PersonPhotos(this.store, this.members, ownership, authorization);
    }
}
