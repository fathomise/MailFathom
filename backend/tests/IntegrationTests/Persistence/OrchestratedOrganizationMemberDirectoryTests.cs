// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the statements an organization's member directory is read from apply its rules, and the filter a colleague's address outranks a record by.</summary>
/// <remarks>
/// <para>
/// Every rule here is decided by a statement rather than by the process: which users are an organization's people,
/// which of the accounts assigned to one of them are theirs alone, which documents are within what this build binds,
/// and which records a colleague's address hides beside the reader's own book. A substitute would report whatever rule
/// it was written with, so only PostgreSQL can answer any of them.
/// </para>
/// <para>
/// Every user, account, contact, and organization arranged here is removed in a <c>finally</c>, accounts and their
/// assignments first, because a second user row left behind breaks every later start in this collection rather than
/// the test that wrote it. Every address is in a domain no other class writes into, because an account's address is
/// unique across the deployment.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOrganizationMemberDirectoryTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>Enough writers, half in each direction, that one of them reliably reads before another commits.</summary>
    private const int ConcurrentManagerWrites = 8;

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero);

    private static readonly ContactAddressWriter PersonWriting = new(ContactAddressWriterKind.Person, User: null, Credential: null);

    /// <summary>
    /// An organization's people are its users of type person, and each one's addresses are the accounts of the same
    /// organization assigned to them and nobody else: a service account, a user of another organization, an account of
    /// another organization, and a shared mailbox are each left out.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnOrganizationWithEveryKindOfUserAndAccount_ReadsItsPeopleAndTheirOwnAddresses()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var arrangement = new Arrangement(services);

        try
        {
            var people = await arrangement.HoldAsync(cancellationToken);

            // Act
            var read = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationMemberReader>().ReadAsync(people.Organization, 10, token),
                cancellationToken);

            // Assert
            Assert.NotNull(read);
            Assert.Equal(arrangement.OrganizationName, read.OrganizationName);
            Assert.Equal(
                new[] { people.Anna, people.Piotr }.Order(),
                read.Members.Select(member => member.User.Value).Order());
            Assert.Equal(
                [people.AnnasOwnAddress],
                read.Members.Single(member => member.User.Value == people.Anna).Addresses);
            Assert.Empty(read.Members.Single(member => member.User.Value == people.Piotr).Addresses);
            Assert.Equal("Controller", read.Members.Single(member => member.User.Value == people.Anna).JobTitle);
        }
        finally
        {
            await arrangement.RemoveAsync();
        }
    }

    /// <summary>One member is read under exactly the rules every member is, and nobody else is: a service account, a user of another organization, and an organization nobody holds each answer as no member.</summary>
    [Fact]
    public async Task ReadMemberAsync_EachKindOfUser_ReadsAPersonOfTheOrganizationAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var arrangement = new Arrangement(services);

        try
        {
            var people = await arrangement.HoldAsync(cancellationToken);

            // Act
            var answers = await services.InScopeAsync(
                async (scope, token) =>
                {
                    var reader = scope.GetRequiredService<IOrganizationMemberReader>();

                    return (
                        Anna: await reader.ReadMemberAsync(people.Organization, UserId.Create(people.Anna), token),
                        Robot: await reader.ReadMemberAsync(people.Organization, UserId.Create(people.Robot), token),
                        Outsider: await reader.ReadMemberAsync(people.Organization, UserId.Create(people.Outsider), token),
                        Nowhere: await reader.ReadMemberAsync(Guid.CreateVersion7(), UserId.Create(people.Anna), token));
                },
                cancellationToken);

            // Assert
            var anna = Assert.Single(answers.Anna!.Members);
            Assert.Equal(people.Anna, anna.User.Value);
            Assert.Equal([people.AnnasOwnAddress], anna.Addresses);
            Assert.Empty(answers.Robot!.Members);
            Assert.Empty(answers.Outsider!.Members);
            Assert.Null(answers.Nowhere);
        }
        finally
        {
            await arrangement.RemoveAsync();
        }
    }

    /// <summary>A document past what this build binds is measured by PostgreSQL and left behind, and its member is still read.</summary>
    [Fact]
    public async Task ReadMemberAsync_ADocumentPastTheBound_ReadsTheMemberWithoutTheirRecord()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var arrangement = new Arrangement(services);

        try
        {
            var people = await arrangement.HoldAsync(cancellationToken);
            var padding = new string('a', UserSettingsDocument.MaximumOctets);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>().UserAccounts
                    .Where(user => user.Id == people.Anna)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Document, $$"""{"Pad": "{{padding}}"}"""), token),
                cancellationToken);

            // Act
            var read = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationMemberReader>()
                    .ReadMemberAsync(people.Organization, UserId.Create(people.Anna), token),
                cancellationToken);

            // Assert
            var anna = Assert.Single(read!.Members);
            Assert.Null(anna.Json);
            Assert.Equal([people.AnnasOwnAddress], anna.Addresses);
        }
        finally
        {
            await arrangement.RemoveAsync();
        }
    }

    /// <summary>
    /// A manager is a person of the user's own organization who does not already report to them: the store accepts one
    /// such manager and refuses the user themselves, a service account, a user of another organization, and the user's
    /// own report, each naming its rule — and the member directory reads the one it accepted.
    /// </summary>
    [Fact]
    public async Task SetManagerAsync_EachKindOfManager_WritesAPersonOfTheOrganizationAndRefusesTheRest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var arrangement = new Arrangement(services);

        try
        {
            var people = await arrangement.HoldAsync(cancellationToken);
            var anna = UserId.Create(people.Anna);
            var piotr = UserId.Create(people.Piotr);

            // Act
            var answers = await services.InScopeAsync(
                async (scope, token) =>
                {
                    var provisioning = scope.GetRequiredService<IUserProvisioning>();

                    return (
                        Accepted: await provisioning.SetManagerAsync(piotr, anna, token),
                        Cycle: await provisioning.SetManagerAsync(anna, piotr, token),
                        Own: await provisioning.SetManagerAsync(anna, anna, token),
                        Robot: await provisioning.SetManagerAsync(anna, UserId.Create(people.Robot), token),
                        Outsider: await provisioning.SetManagerAsync(anna, UserId.Create(people.Outsider), token),
                        Nobody: await provisioning.SetManagerAsync(anna, UserId.Create(Guid.CreateVersion7()), token));
                },
                cancellationToken);
            var piotrRead = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationMemberReader>().ReadMemberAsync(people.Organization, piotr, token),
                cancellationToken);

            // Assert
            Assert.Equal(ManagerAssignmentResult.Assigned, answers.Accepted);
            Assert.Equal(ManagerAssignmentResult.ReportingCycle, answers.Cycle);
            Assert.Equal(ManagerAssignmentResult.OwnManager, answers.Own);
            Assert.Equal(ManagerAssignmentResult.ManagerNotAPerson, answers.Robot);
            Assert.Equal(ManagerAssignmentResult.ManagerOutsideOrganization, answers.Outsider);
            Assert.Equal(ManagerAssignmentResult.UnknownManager, answers.Nobody);
            Assert.Equal(anna, Assert.Single(piotrRead!.Members).Manager);
        }
        finally
        {
            await arrangement.RemoveAsync();
        }
    }

    /// <summary>
    /// Two writers naming each other's manager at once cannot close a loop between them: neither sees the other's
    /// uncommitted line, so only the lock on the organization's row leaves the second to read the first and refuse it.
    /// </summary>
    [Fact]
    public async Task SetManagerAsync_TwoUsersNamedEachOthersManagerAtOnce_StoresOneLineAndRefusesTheOther()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var arrangement = new Arrangement(services);

        try
        {
            var people = await arrangement.HoldAsync(cancellationToken);
            var anna = UserId.Create(people.Anna);
            var piotr = UserId.Create(people.Piotr);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Naming each other's manager",
                ConcurrentManagerWrites,
                (ordinal, token) => services.InScopeAsync(
                    async (scope, scopeToken) =>
                    {
                        var annaReportsToPiotr = ordinal % 2 == 0;
                        var answer = annaReportsToPiotr
                            ? await scope.GetRequiredService<IUserProvisioning>().SetManagerAsync(anna, piotr, scopeToken)
                            : await scope.GetRequiredService<IUserProvisioning>().SetManagerAsync(piotr, anna, scopeToken);

                        return (AnnaReportsToPiotr: annaReportsToPiotr, Answer: answer);
                    },
                    token),
                cancellationToken);
            var annasManager = await ManagerOfAsync(services, people.Anna, cancellationToken);
            var piotrsManager = await ManagerOfAsync(services, people.Piotr, cancellationToken);

            // Assert
            Assert.Empty(attempts.Failures);
            Assert.True(annasManager is null ^ piotrsManager is null, "Exactly one of Anna and Piotr should report to the other.");
            var storedDirection = annasManager is not null;
            Assert.All(
                attempts.Results,
                attempt => Assert.Equal(
                    attempt.AnnaReportsToPiotr == storedDirection ? ManagerAssignmentResult.Assigned : ManagerAssignmentResult.ReportingCycle,
                    attempt.Answer));
        }
        finally
        {
            await arrangement.RemoveAsync();
        }
    }

    /// <summary>
    /// A manager who leaves the organization is nobody's manager there any more, and neither is their own manager theirs:
    /// moving Anna out clears both her line to Piotr and the robot's line to her, and erasing a manager clears the line of
    /// whoever named them.
    /// </summary>
    [Fact]
    public async Task SetUserOrganizationAsync_AManagerLeavingOrErased_ClearsTheLinesThatNamedThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var arrangement = new Arrangement(services);

        try
        {
            var people = await arrangement.HoldAsync(cancellationToken);
            var anna = UserId.Create(people.Anna);
            var piotr = UserId.Create(people.Piotr);
            var robot = UserId.Create(people.Robot);
            var otherOrganization = await arrangement.HoldOrganizationAsync(cancellationToken);
            await services.InScopeAsync(
                async (scope, token) =>
                {
                    var provisioning = scope.GetRequiredService<IUserProvisioning>();
                    Assert.Equal(ManagerAssignmentResult.Assigned, await provisioning.SetManagerAsync(anna, piotr, token));
                    Assert.Equal(ManagerAssignmentResult.Assigned, await provisioning.SetManagerAsync(robot, anna, token));

                    // A user holding an account outside the organization they move into is refused before anything is
                    // written, so Anna gives up her accounts for the move to reach the lines it clears.
                    return await scope.GetRequiredService<MailFathomDbContext>().MailAccountAssignments
                        .Where(assignment => assignment.UserId == people.Anna)
                        .ExecuteDeleteAsync(token);
                },
                cancellationToken);

            // Act
            var moved = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().SetUserOrganizationAsync(anna, otherOrganization, token),
                cancellationToken);
            var annasAfterTheMove = await ManagerOfAsync(services, people.Anna, cancellationToken);
            var robotsAfterTheMove = await ManagerOfAsync(services, people.Robot, cancellationToken);
            var reassigned = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserProvisioning>().SetManagerAsync(robot, piotr, token),
                cancellationToken);
            await OrchestratedForeignUser.EraseAsync(services, people.Piotr);
            var robotsAfterTheErasure = await ManagerOfAsync(services, people.Robot, cancellationToken);

            // Assert
            Assert.Equal(OrganizationWriteOutcome.Written, moved.Outcome);
            Assert.Null(annasAfterTheMove);
            Assert.Null(robotsAfterTheMove);
            Assert.Equal(ManagerAssignmentResult.Assigned, reassigned);
            Assert.Null(robotsAfterTheErasure);
        }
        finally
        {
            await arrangement.RemoveAsync();
        }
    }

    /// <summary>
    /// A colleague's address hides an organization's record of it from a page read beside the directory, and never the
    /// reader's own record of it, which the filter keeps by the book it is in.
    /// </summary>
    [Fact]
    public async Task ReadPageAsync_AnOutrankedAddress_HidesTheOrganizationsRecordAndKeepsTheReadersOwn()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var arrangement = new Arrangement(services);
        var colleague = $"colleague-{Guid.CreateVersion7():N}@outranked.members.test";
        var supplier = $"supplier-{Guid.CreateVersion7():N}@outranked.members.test";
        var servedUser = services.ServedUser;
        Contact? own = null;

        try
        {
            var organization = await arrangement.HoldOrganizationAsync(cancellationToken);
            var outranked = ContactOf("Outranked Colleague", colleague, organization);
            var kept = ContactOf("Outranked Supplier", supplier, organization);
            own = ContactOf("Outranked Own", colleague, organization: null);
            var ours = own;

            foreach (var (holder, contact) in new[]
            {
                (ContactBookHolder.OfOrganization(organization), outranked),
                (ContactBookHolder.OfOrganization(organization), kept),
                (ContactBookHolder.Of(servedUser), ours),
            })
            {
                Assert.Equal(
                    PersistenceCommitResult.Committed,
                    await services.CommitAsync(
                        (scope, session, token) => scope.GetRequiredService<IContactStore>().AddAsync(session, holder, contact, PersonWriting, token),
                        cancellationToken));
            }

            // Act
            var page = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IContactDirectory>().ReadPageAsync(
                    ContactBookScope.Of(servedUser, organization, []),
                    ContactQuery.Create(origin: null, ContactSearch.Create("outranked"), pageSize: 50, cursor: null).Outranking([Address(colleague)]),
                    token),
                cancellationToken);

            // Assert
            Assert.Equal(
                ["Outranked Own", "Outranked Supplier"],
                page.Contacts.Select(contact => contact.DisplayName.Value).Order(StringComparer.Ordinal));
        }
        finally
        {
            if (own is not null)
            {
                var written = own;
                await services.CommitAsync(
                    (scope, session, token) => scope.GetRequiredService<IContactStore>()
                        .EraseAsync(session, ContactBookScope.OfOwnBookAlone(servedUser), written.Id, token),
                    CancellationToken.None);
            }

            await arrangement.RemoveAsync();
        }
    }

    private static Task<Guid?> ManagerOfAsync(OrchestratedMailFathomServices services, Guid user, CancellationToken cancellationToken) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().UserAccounts
                .AsNoTracking()
                .Where(account => account.Id == user)
                .Select(account => account.ManagerId)
                .SingleAsync(token),
            cancellationToken);

    private static Contact ContactOf(string displayName, string address, Guid? organization) => Contact.Create(
        ContactId.Create(Guid.CreateVersion7(RecordedAt)),
        ContactDisplayName.Create(displayName),
        [Address(address)],
        Address(address),
        note: null,
        ContactOrigin.Asserted,
        RecordedAt,
        RecordedAt,
        organization);

    private static EmailAddress Address(string address) =>
        EmailAddress.TryCreate(displayName: null, address, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"The test address '{address}' names no mailbox.");

    /// <summary>The users, accounts, and organizations one test arranges, and the removal of all of them.</summary>
    private sealed class Arrangement(OrchestratedMailFathomServices services)
    {
        private readonly List<Guid> organizations = [];
        private readonly List<Guid> users = [];
        private readonly List<Guid> accounts = [];

        internal string OrganizationName { get; } = $"Members {Guid.CreateVersion7():N}";

        /// <summary>Holds an organization with no members.</summary>
        internal async Task<Guid> HoldOrganizationAsync(CancellationToken cancellationToken)
        {
            var organization = Guid.CreateVersion7();
            this.organizations.Add(organization);

            var created = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    this.OrganizationName,
                    OrganizationShortName.Create($"{organization:N}"),
                    RecordedAt,
                    token),
                cancellationToken);

            Assert.Equal(OrganizationWriteOutcome.Written, created.Outcome);

            return organization;
        }

        /// <summary>
        /// Holds an organization of two people and a service account beside a user of another organization. Anna is
        /// assigned an account of her own, an account of the other organization, and a mailbox she shares with Piotr;
        /// Piotr is assigned only the shared one.
        /// </summary>
        internal async Task<People> HoldAsync(CancellationToken cancellationToken)
        {
            var organization = await this.HoldOrganizationAsync(cancellationToken);
            var otherOrganization = await this.HoldOrganizationAsync(cancellationToken);

            var anna = await this.ProvisionAsync(organization, UserType.User, cancellationToken);
            var piotr = await this.ProvisionAsync(organization, UserType.User, cancellationToken);
            var robot = await this.ProvisionAsync(organization, UserType.ServiceAccount, cancellationToken);
            var outsider = await this.ProvisionAsync(otherOrganization, UserType.User, cancellationToken);

            Assert.True(await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserProvisioning>().SetJobTitleAsync(UserId.Create(anna), "Controller", token),
                cancellationToken));

            var own = await this.HoldAccountAsync(organization, [anna], cancellationToken);
            await this.HoldAccountAsync(otherOrganization, [anna], cancellationToken);
            await this.HoldAccountAsync(organization, [anna, piotr], cancellationToken);
            await this.HoldAccountAsync(organization, [robot], cancellationToken);

            return new People(organization, anna, piotr, robot, outsider, own);
        }

        /// <summary>Removes every account, user, and organization this arrangement held, whatever the test did.</summary>
        internal async Task RemoveAsync()
        {
            await services.InScopeAsync(
                async (scope, token) =>
                {
                    var context = scope.GetRequiredService<MailFathomDbContext>();

                    return await context.MailAccountAssignments.Where(assignment => this.accounts.Contains(assignment.MailAccountId)).ExecuteDeleteAsync(token)
                        + await context.MailAccountRecords.Where(record => this.accounts.Contains(record.Id)).ExecuteDeleteAsync(token);
                },
                CancellationToken.None);

            foreach (var user in this.users)
            {
                await OrchestratedForeignUser.EraseAsync(services, user);
            }

            foreach (var organization in this.organizations)
            {
                await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                    CancellationToken.None);
            }
        }

        private async Task<Guid> ProvisionAsync(Guid organization, UserType type, CancellationToken cancellationToken)
        {
            var user = Guid.CreateVersion7();
            this.users.Add(user);

            Assert.Equal(
                UserProvisioningResult.Provisioned,
                await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IUserProvisioning>().ProvisionAsync(
                        UserId.Create(user),
                        $"member-{user:N}",
                        organization,
                        token),
                    cancellationToken));
            Assert.True(await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserProvisioning>().SetTypeAsync(UserId.Create(user), type, token),
                cancellationToken));

            return user;
        }

        /// <summary>Holds one account of an organization assigned to the users named, and answers its address.</summary>
        private async Task<string> HoldAccountAsync(Guid organization, Guid[] assignees, CancellationToken cancellationToken)
        {
            var account = Guid.CreateVersion7();
            var address = $"{account:N}@directory.members.test";
            this.accounts.Add(account);

            Assert.Equal(
                PersistenceCommitResult.Committed,
                await services.CommitAsync(
                    async (_, session, token) =>
                    {
                        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);
                        context.MailAccountRecords.Add(new MailAccountRecordEntity
                        {
                            Id = account,
                            DisplayName = $"Account {account:N}",
                            EmailAddress = address,
                            NormalizedEmailAddress = address.ToUpperInvariant(),
                            OrganizationId = organization,
                            Document = "{}",
                            Version = 1,
                            CreatedAt = RecordedAt,
                            UpdatedAt = RecordedAt,
                        });
                        context.MailAccountAssignments.AddRange(assignees.Select(user => new MailAccountAssignmentEntity
                        {
                            UserId = user,
                            MailAccountId = account,
                            AssignedAt = RecordedAt,
                        }));
                        await context.SaveChangesAsync(token);
                    },
                    cancellationToken));

            return address;
        }
    }

    /// <summary>Who one arrangement holds.</summary>
    private sealed record People(Guid Organization, Guid Anna, Guid Piotr, Guid Robot, Guid Outsider, string AnnasOwnAddress);
}
