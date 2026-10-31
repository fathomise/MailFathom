// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Members;
using MailFathom.Application.Persistence;
using MailFathom.Application.Presence;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Presence;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the colleague routes decide about a request and how a colleague is shaped for a client.</summary>
/// <remarks>Which members a reader sees is <c>ColleagueBook</c>'s, covered there; what is asserted here is the part above it.</remarks>
public sealed class ClientColleagueEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-00000000e101");

    private static readonly UserId Piotr = SyntheticUser.Another;

    private static readonly Guid PiotrsPortrait = new("0198f0aa-0000-7000-8000-00000000e1f1");

    private readonly IMemberDirectory members = Substitute.For<IMemberDirectory>();

    private readonly IContactDirectory directory = Substitute.For<IContactDirectory>();

    private readonly IContactStore store = Substitute.For<IContactStore>();

    /// <summary>The deployment's half of an agreement with a client that composes these paths from constants of its own.</summary>
    [Fact]
    public void ColleagueRoutes_ArePathsAClientComposes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("/contacts/colleagues", ClientColleagueEndpoints.ColleaguesRoute);
        Assert.Equal("/contacts/colleagues/{userId:guid}", ClientColleagueEndpoints.ColleagueRoute);
        Assert.Equal("/contacts/colleagues/{userId:guid}/photo", ClientColleagueEndpoints.ColleaguePhotoRoute);
        Assert.Equal("/contacts/colleagues/{userId:guid}/structure", ClientColleagueEndpoints.ColleagueStructureRoute);
        Assert.Equal("/contacts/colleagues/{userId:guid}/promotion", ClientColleagueEndpoints.ColleaguePromotionRoute);
    }

    /// <summary>A colleague is drawn as a member contact: the preferred address first, the portrait's identifier, the zone by its IANA name, the working hours on their own wall clock, and the end of an automatic reply that is running.</summary>
    [Fact]
    public async Task ReadPageAsync_AColleague_IsDescribedAsAMemberContact()
    {
        // Arrange
        UserTimeZone.TryRead("Europe/Warsaw", out var zone);
        this.Holding(new MemberContact(
            Piotr,
            ContactDisplayName.Create("Piotr Zieliński"),
            [Mailbox("piotr@nordwind.example"), Mailbox("p.zielinski@nordwind.example")],
            Mailbox("p.zielinski@nordwind.example"),
            [ContactPhoneNumber.Create("+48 600 100 200", ContactPhoneLabel.Mobile)],
            "Controller",
            "Nordwind",
            StoredFileId.Create(PiotrsPortrait),
            zone,
            WorkingHours.Create([DayOfWeek.Friday, DayOfWeek.Monday], new TimeOnly(8, 0), new TimeOnly(16, 30))));

        // Act
        var result = await ClientColleagueEndpoints.ReadPageAsync(
            search: null,
            pageSize: null,
            cursor: null,
            this.Colleagues(),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ColleaguePageResponse>>(result.Result);
        var colleague = Assert.Single(page.Value!.Contacts);
        Assert.Equal(Piotr.Value, colleague.Id);
        Assert.Equal("Member", colleague.Book);
        Assert.Equal(["p.zielinski@nordwind.example", "piotr@nordwind.example"], colleague.Addresses);
        Assert.Equal("p.zielinski@nordwind.example", colleague.PreferredAddress);
        Assert.Equal(new ContactPhoneNumberResponse("+48 600 100 200", "Mobile"), Assert.Single(colleague.PhoneNumbers));
        Assert.Equal("Controller", colleague.JobTitle);
        Assert.Equal("Nordwind", colleague.OrganizationName);
        Assert.Equal("Europe/Warsaw", colleague.TimeZone);
        Assert.Equal(["Monday", "Friday"], colleague.WorkingHours!.Days);
        Assert.Equal("08:00", colleague.WorkingHours.Start);
        Assert.Equal("16:30", colleague.WorkingHours.End);
        Assert.Equal(PiotrsPortrait, colleague.Photo);
        Assert.Equal("Offline", colleague.Presence);
        Assert.Null(colleague.LastSeen);
        Assert.Equal(Now.AddDays(2), colleague.OutOfOfficeUntil);
        Assert.Null(page.Value.NextCursor);
    }

    /// <summary>A search past the bound is refused with the rule rather than read, as it is on the reader's own book.</summary>
    [Fact]
    public async Task ReadPageAsync_ASearchOverTheBound_IsRefusedWithoutReading()
    {
        // Act
        var result = await ClientColleagueEndpoints.ReadPageAsync(
            new string('a', ContactSearch.MaximumLength + 1),
            pageSize: null,
            cursor: null,
            this.Colleagues(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await this.members.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    /// <summary>Nobody, a user outside the organization, and the identifier no user carries all answer that there is no such colleague.</summary>
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0198f0aa-0000-7000-8000-00000000e1aa")]
    public async Task FindAsync_NoSuchColleague_AnswersNotFound(string userId)
    {
        // Arrange
        this.Holding();

        // Act
        var result = await ClientColleagueEndpoints.FindAsync(Guid.Parse(userId), this.Colleagues(), TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A recipient's address reads the colleague it belongs to, as one colleague is described.</summary>
    [Fact]
    public async Task FindAtAsync_AColleaguesAddress_DescribesThatColleague()
    {
        // Arrange
        this.Holding(ColleagueNamed(Piotr, "Piotr Zieliński") with
        {
            Addresses = [Mailbox("piotr@nordwind.example")],
        });

        // Act
        var result = await ClientColleagueEndpoints.FindAtAsync(
            "Piotr@nordwind.example",
            this.Colleagues(),
            TestContext.Current.CancellationToken);

        // Assert
        var found = Assert.IsType<Ok<ColleagueResponse>>(result.Result);
        Assert.Equal(Piotr.Value, found.Value!.Id);
    }

    /// <summary>An address nobody in the organization holds answers that there is no such colleague, and text that is no address is refused without reading.</summary>
    [Fact]
    public async Task FindAtAsync_NobodysAddressOrNoAddress_AnswersNotFoundOrIsRefused()
    {
        // Arrange
        this.Holding(ColleagueNamed(Piotr, "Piotr Zieliński"));
        var colleagues = this.Colleagues();

        // Act
        var nobody = await ClientColleagueEndpoints.FindAtAsync("someone@contoso.example", colleagues, TestContext.Current.CancellationToken);
        var noAddress = await ClientColleagueEndpoints.FindAtAsync("not an address", colleagues, TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(nobody.Result);
        var refusal = Assert.IsType<ProblemHttpResult>(noAddress.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
    }

    /// <summary>The structure names the managers top-down, the reports below, and the reader, so the client knows which row leads nowhere.</summary>
    [Fact]
    public async Task ReadStructureAsync_AColleagueWithAManagerAndAReport_DescribesBothAndTheReader()
    {
        // Arrange
        var report = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000e1ab"));
        this.Holding(
            ColleagueNamed(SyntheticUser.Deployment, "Anna Kowalska"),
            ColleagueNamed(Piotr, "Piotr Zieliński") with { Manager = SyntheticUser.Deployment },
            ColleagueNamed(report, "Oliver Brandt") with { Manager = Piotr });

        // Act
        var result = await ClientColleagueEndpoints.ReadStructureAsync(Piotr.Value, this.Colleagues(), TestContext.Current.CancellationToken);

        // Assert
        var structure = Assert.IsType<Ok<ColleagueStructureResponse>>(result.Result).Value!;
        Assert.Equal([SyntheticUser.Deployment.Value], structure.Managers.Select(colleague => colleague.Id));
        Assert.Equal([report.Value], structure.DirectReports.Select(colleague => colleague.Id));
        Assert.Equal(SyntheticUser.Deployment.Value, structure.ReaderId);
    }

    /// <summary>The structure is answered as one colleague is: nobody, a stranger, and the reader all find no such colleague.</summary>
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0198f0aa-0000-7000-8000-00000000e1aa")]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    public async Task ReadStructureAsync_NoSuchColleague_AnswersNotFound(string userId)
    {
        // Arrange
        this.Holding(ColleagueNamed(SyntheticUser.Deployment, "Anna Kowalska"));

        // Act
        var result = await ClientColleagueEndpoints.ReadStructureAsync(Guid.Parse(userId), this.Colleagues(), TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A contact needs an address, so promoting a colleague with no mailbox of their own is a request the client reports.</summary>
    [Fact]
    public async Task PromoteAsync_AColleagueWithNoAddress_IsRefused()
    {
        // Arrange
        this.Holding(new MemberContact(
            Piotr,
            ContactDisplayName.Create("Piotr Zieliński"),
            [],
            PreferredAddress: null,
            [],
            JobTitle: null,
            "Nordwind",
            Portrait: null,
            TimeZone: null,
            WorkingHours: null));

        // Act
        var result = await ClientColleagueEndpoints.PromoteAsync(Piotr.Value, this.Colleagues(), TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(this.store.ReceivedCalls());
    }

    private static MemberContact ColleagueNamed(UserId member, string displayName) => new(
        member,
        ContactDisplayName.Create(displayName),
        [],
        PreferredAddress: null,
        [],
        JobTitle: null,
        "Nordwind",
        Portrait: null,
        TimeZone: null,
        WorkingHours: null);

    private static EmailAddress Mailbox(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var mailbox));

        return mailbox;
    }

    private void Holding(params MemberContact[] composed)
    {
        this.members.ReadAsync(Organization, Arg.Any<CancellationToken>()).Returns(composed);

        foreach (var member in composed)
        {
            this.members.FindAsync(Organization, member.Member, Arg.Any<CancellationToken>()).Returns(member);
        }

        this.directory
            .FindHoldersOfAsync(Arg.Any<ContactBookHolder>(), Arg.Any<IReadOnlyCollection<EmailAddress>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<EmailAddress, ContactId>());
    }

    private ColleagueBook Colleagues()
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.ContactsRead, MailFathomPermission.ContactsWrite);
        var ownership = ContactBookOwnerships.For(
            authorization,
            new StubMailAccountAssignments(),
            new StatedAdministrativeTargets().WithUser(SyntheticUser.Deployment, Organization));
        var clock = new FakeTimeProvider(Now);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var writer = new ContactBookWriter(
            new ContactBook(
                this.store,
                this.directory,
                Substitute.For<IStoredFileStore>(),
                new OptimisticConcurrencyRetryPolicy(sessionFactory, new PersistenceConcurrencyOptions(), clock),
                clock,
                authorization,
                ClientSignalPublishers.ReachingNobody),
            this.directory,
            ownership,
            authorization);

        var presence = Substitute.For<IPresenceStore>();
        presence.ReadAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<UserId, MemberPresence>());

        var automaticReplies = Substitute.For<IAutomaticReplyStore>();
        automaticReplies.ReadPeriodsAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<UserId, AutomaticReplyPeriod>
            {
                [Piotr] = new(Now.AddDays(-1), Now.AddDays(2), IsOn: true),
            });

        return new ColleagueBook(
            this.members,
            this.directory,
            Substitute.For<IStoredFileStore>(),
            writer,
            ownership,
            authorization,
            presence,
            automaticReplies,
            clock);
    }

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
