// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Persistence;
using MailFathom.Application.StoredFiles;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Presence;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Contacts.Members;

/// <summary>Covers what a member reads of their colleagues, and what keeping a copy of one writes.</summary>
/// <remarks>
/// The directory is a substitute answering with members as the host composes them, and the reader's own book is the
/// real in-memory one, so what is asserted is which members a reader is shown beside what their own book holds.
/// </remarks>
public sealed class ColleagueBookTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Organization = AccessAuthorizations.ScopedOrganization;

    private static readonly UserId Reader = SyntheticUser.Deployment;

    private static readonly UserId Piotr = SyntheticUser.Another;

    private static readonly UserId Zofia = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000c003"));

    private static readonly StoredFileId PiotrsPortrait = StoredFileId.Create(new Guid("0198f0aa-0000-7000-8000-00000000c0f1"));

    private readonly InMemoryContactBookStore store = new();

    private readonly IMemberDirectory members = Substitute.For<IMemberDirectory>();

    private readonly IStoredFileStore files = Substitute.For<IStoredFileStore>();

    private readonly InMemoryPresenceStore presence = new();

    /// <summary>The reader is one of the members the directory composes, and is never their own colleague.</summary>
    [Fact]
    public async Task ReadPageAsync_TheReadersOrganization_ListsEveryOtherMemberByName()
    {
        // Arrange
        this.Holding(
            MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"),
            MemberOf(Reader, "Anna Kowalska", "anna@nordwind.example"),
            MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"));

        // Act
        var page = await this.BookFor(Organization).ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Piotr, Zofia], page.Colleagues.Select(colleague => colleague.Contact.Member));
        Assert.Null(page.NextCursor);
    }

    /// <summary>A reader in no organization has no colleagues, and nothing is asked of a directory they are in none of.</summary>
    [Fact]
    public async Task ReadPageAsync_AReaderInNoOrganization_ReadsAnEmptyDirectory()
    {
        // Act
        var page = await this.BookFor(organization: null).ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Colleagues);
        await this.members.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>The reader's own record of a colleague answers for them, so the member contact is not listed beside it.</summary>
    [Fact]
    public async Task ReadPageAsync_AColleagueTheReadersOwnBookHolds_IsLeftOut()
    {
        // Arrange
        this.Holding(
            MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"),
            MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"));
        this.store.Hold(Reader, ContactOf("Piotrek", "PIOTR@nordwind.example"));

        // Act
        var page = await this.BookFor(Organization).ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Zofia], page.Colleagues.Select(colleague => colleague.Contact.Member));
    }

    /// <summary>The search matches an address as well as a name, as it does on the reader's own book.</summary>
    [Fact]
    public async Task ReadPageAsync_ASearchForAnAddress_ListsTheColleagueHoldingIt()
    {
        // Arrange
        this.Holding(
            MemberOf(Piotr, "Piotr Zieliński", "controlling@nordwind.example"),
            MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"));

        // Act
        var page = await this.BookFor(Organization).ReadPageAsync(
            new ContactPageRequest { Search = "controlling" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Piotr], page.Colleagues.Select(colleague => colleague.Contact.Member));
    }

    /// <summary>A page ends with a cursor the next page continues from, so walking the directory reaches everybody once.</summary>
    [Fact]
    public async Task ReadPageAsync_ADirectoryLongerThanAPage_ContinuesFromTheCursor()
    {
        // Arrange
        this.Holding(
            MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"),
            MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"));
        var book = this.BookFor(Organization);

        // Act
        var first = await book.ReadPageAsync(new ContactPageRequest { PageSize = 1 }, TestContext.Current.CancellationToken);
        var second = await book.ReadPageAsync(
            new ContactPageRequest { PageSize = 1, Cursor = first.NextCursor?.Encode() },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Piotr], first.Colleagues.Select(colleague => colleague.Contact.Member));
        Assert.Equal([Zofia], second.Colleagues.Select(colleague => colleague.Contact.Member));
        Assert.Null(second.NextCursor);
    }

    /// <summary>Reading colleagues is reading the books, so it takes the reading grant.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerWithoutTheReadingGrant_IsRefused()
    {
        // Act, Assert
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            this.BookFor(Organization, MailFathomPermission.ContactsWrite)
                .ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken));

        Assert.Equal(MailFathomPermission.ContactsRead, refusal.RequiredPermission);
    }

    /// <summary>A member who left the organization is no longer composed into it, so they are gone with nothing written anywhere.</summary>
    [Fact]
    public async Task FindAsync_AMemberWhoLeftTheOrganization_IsNoColleague()
    {
        // Arrange
        this.Holding(MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"));

        // Act
        var found = await this.BookFor(Organization).FindAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
        Assert.Equal(0, this.store.ContactCount);
    }

    /// <summary>The reader's own identifier names nobody in their directory, so it answers as nobody does.</summary>
    [Fact]
    public async Task FindAsync_TheReadersOwnIdentifier_IsNoColleague()
    {
        // Arrange
        this.Holding(MemberOf(Reader, "Anna Kowalska", "anna@nordwind.example"));

        // Act
        var found = await this.BookFor(Organization).FindAsync(Reader, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
        await this.members.DidNotReceiveWithAnyArgs().FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>One colleague is read as one member, so finding them never composes the rest of the organization.</summary>
    [Fact]
    public async Task FindAsync_AColleague_ReadsThatMemberAlone()
    {
        // Arrange
        this.Holding(
            MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"),
            MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"));

        // Act
        var found = await this.BookFor(Organization).FindAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Piotr, found?.Contact.Member);
        await this.members.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>A colleague who chose to hide is handed to a reader as offline with no last-seen time, which is the only presence a reader is ever given.</summary>
    [Fact]
    public async Task ReadPageAsync_AColleagueWhoChoseToHide_ReadsAsOfflineWithNoLastSeen()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"));
        this.presence
            .WithClient(Piotr, Guid.NewGuid(), ClientActivity.Active, Now.AddMinutes(2))
            .WithChoice(Piotr, new PresenceChoice(PresenceState.Hidden, Duration: null, EndsAt: null))
            .WithLastSeen(Piotr, Now.AddHours(-1));

        // Act
        var page = await this.BookFor(Organization).ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([new PresenceReading(PresenceState.Offline, LastSeen: null)], page.Colleagues.Select(colleague => colleague.Presence));
    }

    [Fact]
    public async Task FindAsync_AColleagueWithAClientOpen_CarriesTheirPresence()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"));
        this.presence.WithClient(Piotr, Guid.NewGuid(), ClientActivity.Idle, Now.AddMinutes(2));

        // Act
        var found = await this.BookFor(Organization).FindAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new PresenceReading(PresenceState.Away, LastSeen: null), found?.Presence);
    }

    /// <summary>
    /// A member is looked up within the reader's own organization only, so a user of another one — or a service account,
    /// which the directory never composes — answers as nobody.
    /// </summary>
    [Fact]
    public async Task FindAsync_AUserTheReadersOrganizationDoesNotCompose_IsNoColleague()
    {
        // Arrange
        this.Holding(MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"));

        // Act
        var found = await this.BookFor(Organization).FindAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
        await this.members.Received(1).FindAsync(Organization, Piotr, Arg.Any<CancellationToken>());
        await this.members.DidNotReceive().FindAsync(Arg.Is<Guid>(organization => organization != Organization), Arg.Any<UserId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A reader in no organization has no colleague to find, and no directory is asked.</summary>
    [Fact]
    public async Task FindAsync_AReaderInNoOrganization_IsNoColleague()
    {
        // Act
        var found = await this.BookFor(organization: null).FindAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
        await this.members.DidNotReceiveWithAnyArgs().FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>A colleague's portrait is read from their own files, under their identity rather than the reader's.</summary>
    [Fact]
    public async Task ReadPortraitAsync_AColleagueWithAPortrait_ReadsTheirFile()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example") with { Portrait = PiotrsPortrait });
        ReadOnlyMemory<byte> png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        this.files.ReadAsync(Piotr, PiotrsPortrait, Arg.Any<CancellationToken>()).Returns(png);

        // Act
        var portrait = await this.BookFor(Organization).ReadPortraitAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(portrait);
    }

    /// <summary>A colleague without a portrait answers with none, and no file is read for them.</summary>
    [Fact]
    public async Task ReadPortraitAsync_AColleagueWithoutAPortrait_IsNothing()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"));

        // Act
        var portrait = await this.BookFor(Organization).ReadPortraitAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(portrait);
        await this.files.DidNotReceiveWithAnyArgs().ReadAsync(default, default, TestContext.Current.CancellationToken);
    }

    /// <summary>Promoting a colleague writes a copy of them into the reader's own book, which then hides the member contact.</summary>
    [Fact]
    public async Task PromoteAsync_AColleague_CopiesThemIntoTheReadersOwnBook()
    {
        // Arrange
        var piotr = MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example", "p.zielinski@nordwind.example") with
        {
            PreferredAddress = Address("p.zielinski@nordwind.example"),
            PhoneNumbers = [ContactPhoneNumber.Create("+48 600 100 200", ContactPhoneLabel.Mobile)],
        };
        this.Holding(piotr);
        var book = this.BookFor(Organization);

        // Act
        var result = await book.PromoteAsync(Piotr, ContactAddressWriterKind.Person, TestContext.Current.CancellationToken);
        var page = await book.ReadPageAsync(new ContactPageRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.Written, result.Outcome);

        var copy = Assert.Single(this.store.ContactsOf(Reader));

        Assert.Equal("Piotr Zieliński", copy.DisplayName.Value);
        Assert.Equal(Address("p.zielinski@nordwind.example"), copy.PreferredAddress);
        Assert.Equal(2, copy.Addresses.Count);
        Assert.Equal("+48 600 100 200", Assert.Single(copy.PhoneNumbers).Number);
        Assert.Equal(ContactOrigin.Asserted, copy.Origin);
        Assert.Empty(page.Colleagues);
    }

    /// <summary>Copying a colleague writes the reader's own book, so the reading grant alone does not reach it.</summary>
    [Fact]
    public async Task PromoteAsync_ACallerWithoutTheWritingGrant_IsRefusedAndWritesNothing()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, "Piotr Zieliński", "piotr@nordwind.example"));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            this.BookFor(Organization, MailFathomPermission.ContactsRead)
                .PromoteAsync(Piotr, ContactAddressWriterKind.Person, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.ContactsWrite, refusal.RequiredPermission);
        Assert.Equal(0, this.store.ContactCount);
    }

    /// <summary>Somebody who is not the reader's colleague cannot be copied out of a directory the reader does not read.</summary>
    [Fact]
    public async Task PromoteAsync_NoSuchColleague_IsNotFoundAndWritesNothing()
    {
        // Arrange
        this.Holding(MemberOf(Zofia, "Zofia Nowak", "zofia@nordwind.example"));

        // Act
        var result = await this.BookFor(Organization).PromoteAsync(Piotr, ContactAddressWriterKind.Person, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ContactWriteOutcome.NotFound, result.Outcome);
        Assert.Equal(0, this.store.ContactCount);
    }

    /// <summary>A contact needs an address, so a colleague with no mailbox of their own cannot be copied into a book.</summary>
    [Fact]
    public async Task PromoteAsync_AColleagueWithNoAddress_IsRefusedAsARecordTheBookCannotHold()
    {
        // Arrange
        this.Holding(MemberOf(Piotr, "Piotr Zieliński"));

        // Act, Assert
        await Assert.ThrowsAsync<ContactRecordInvalidException>(() =>
            this.BookFor(Organization).PromoteAsync(Piotr, ContactAddressWriterKind.Person, TestContext.Current.CancellationToken));

        Assert.Equal(0, this.store.ContactCount);
    }

    /// <summary>The chain runs from the top of the organization down to the colleague's own manager, and the reports are every member naming the colleague, by name.</summary>
    [Fact]
    public async Task ReadStructureAsync_AColleagueInTheMiddleOfTheLine_DerivesTheManagersAboveAndTheReportsBelow()
    {
        // Arrange
        var marta = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000c004"));
        var adam = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000c005"));
        this.Holding(
            MemberOf(Zofia, "Zofia Nowak"),
            MemberOf(Piotr, "Piotr Zieliński") with { Manager = Zofia },
            MemberOf(marta, "Marta Nowak") with { Manager = Piotr },
            MemberOf(adam, "Adam Lewandowski") with { Manager = Piotr },
            MemberOf(Reader, "Anna Kowalska") with { Manager = marta });

        // Act
        var structure = await this.BookFor(Organization).ReadStructureAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(structure);
        Assert.Equal([Zofia], structure.Managers.Select(colleague => colleague.Contact.Member));
        Assert.Equal([adam, marta], structure.DirectReports.Select(colleague => colleague.Contact.Member));
        Assert.Equal(Reader, structure.Reader);
    }

    /// <summary>The reader stands in the line like anybody else, so a colleague the reader manages reads the reader above them.</summary>
    [Fact]
    public async Task ReadStructureAsync_AColleagueTheReaderManages_ListsTheReaderAboveThem()
    {
        // Arrange
        this.Holding(
            MemberOf(Zofia, "Zofia Nowak"),
            MemberOf(Reader, "Anna Kowalska") with { Manager = Zofia },
            MemberOf(Piotr, "Piotr Zieliński") with { Manager = Reader });

        // Act
        var structure = await this.BookFor(Organization).ReadStructureAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(structure);
        Assert.Equal([Zofia, Reader], structure.Managers.Select(colleague => colleague.Contact.Member));
        Assert.Empty(structure.DirectReports);
    }

    /// <summary>A manager the directory no longer lists — a service account — ends the line rather than leaving a hole in it.</summary>
    [Fact]
    public async Task ReadStructureAsync_AManagerTheDirectoryDoesNotList_EndsTheLineBelowThem()
    {
        // Arrange
        var serviceAccount = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000c006"));
        this.Holding(
            MemberOf(Zofia, "Zofia Nowak") with { Manager = serviceAccount },
            MemberOf(Piotr, "Piotr Zieliński") with { Manager = Zofia });

        // Act
        var structure = await this.BookFor(Organization).ReadStructureAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(structure);
        Assert.Equal([Zofia], structure.Managers.Select(colleague => colleague.Contact.Member));
    }

    /// <summary>Every member of the structure carries the presence a reader is handed, so a manager who chose to hide reads as offline there too.</summary>
    [Fact]
    public async Task ReadStructureAsync_AManagerWhoChoseToHideAndAnActiveReport_CarriesEachOnesPresenceAsOthersSeeIt()
    {
        // Arrange
        var marta = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000c004"));
        this.Holding(
            MemberOf(Zofia, "Zofia Nowak"),
            MemberOf(Piotr, "Piotr Zieliński") with { Manager = Zofia },
            MemberOf(marta, "Marta Nowak") with { Manager = Piotr });
        this.presence
            .WithClient(Zofia, Guid.NewGuid(), ClientActivity.Active, Now.AddMinutes(2))
            .WithChoice(Zofia, new PresenceChoice(PresenceState.Hidden, Duration: null, EndsAt: null))
            .WithClient(marta, Guid.NewGuid(), ClientActivity.Active, Now.AddMinutes(2));

        // Act
        var structure = await this.BookFor(Organization).ReadStructureAsync(Piotr, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(structure);
        Assert.Equal([new PresenceReading(PresenceState.Offline, LastSeen: null)], structure.Managers.Select(colleague => colleague.Presence));
        Assert.Equal([new PresenceReading(PresenceState.Active, LastSeen: null)], structure.DirectReports.Select(colleague => colleague.Presence));
    }

    /// <summary>The structure is read only for a colleague: the reader themselves and a member of no organization the reader shares are nobody here.</summary>
    [Fact]
    public async Task ReadStructureAsync_TheReaderOrAUserTheDirectoryDoesNotList_IsNothing()
    {
        // Arrange
        this.Holding(MemberOf(Reader, "Anna Kowalska"), MemberOf(Zofia, "Zofia Nowak"));
        var book = this.BookFor(Organization);

        // Act
        var own = await book.ReadStructureAsync(Reader, TestContext.Current.CancellationToken);
        var stranger = await book.ReadStructureAsync(Piotr, TestContext.Current.CancellationToken);
        var outside = await this.BookFor(organization: null).ReadStructureAsync(Zofia, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(own);
        Assert.Null(stranger);
        Assert.Null(outside);
    }

    private static MemberContact MemberOf(UserId member, string displayName, params string[] addresses)
    {
        EmailAddress[] held = [.. addresses.Select(Address)];

        return new MemberContact(
            member,
            ContactDisplayName.Create(displayName),
            held,
            held.Length > 0 ? held[0] : null,
            [],
            JobTitle: null,
            "Nordwind",
            Portrait: null,
            TimeZone: null,
            WorkingHours: null);
    }

    private static Contact ContactOf(string displayName, string address) =>
        Contact.Create(
            ContactId.Create(Guid.CreateVersion7(Now)),
            ContactDisplayName.Create(displayName),
            [Address(address)],
            Address(address),
            note: null,
            ContactOrigin.Asserted,
            Now,
            Now);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");

    /// <summary>Arranges the directory the way the host composes it: the whole organization on a listing, and one member on a lookup.</summary>
    private void Holding(params MemberContact[] composed)
    {
        this.members.ReadAsync(Organization, Arg.Any<CancellationToken>()).Returns(composed);

        foreach (var member in composed)
        {
            this.members.FindAsync(Organization, member.Member, Arg.Any<CancellationToken>()).Returns(member);
        }
    }

    private ColleagueBook BookFor(Guid? organization, params MailFathomPermission[] granted)
    {
        var authorization = AccessAuthorizations.ForUserGranted(
            Reader,
            granted.Length == 0 ? [MailFathomPermission.ContactsRead, MailFathomPermission.ContactsWrite] : granted);
        var ownership = ContactBookOwnerships.For(
            authorization,
            new StubMailAccountAssignments(),
            new StatedAdministrativeTargets().WithUser(Reader, organization));

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());
        var timeProvider = new FakeTimeProvider(Now);

        var writer = new ContactBookWriter(
            new ContactBook(
                this.store,
                this.store,
                Substitute.For<IStoredFileStore>(),
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), timeProvider),
                timeProvider,
                authorization),
            this.store,
            ownership,
            authorization);

        return new ColleagueBook(this.members, this.store, this.files, writer, ownership, authorization, this.presence, timeProvider);
    }

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
