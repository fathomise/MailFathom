// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves a mail account in no organization ends up assigned to one user, whoever else asks at the same moment.</summary>
/// <remarks>
/// <para>
/// The rule is a count read under a row lock, so it holds or fails in PostgreSQL and nowhere else: two assignments
/// that each read the account as unassigned and then both insert is the defect, and neither a substitute nor a
/// sequential repeat can produce it. The move out of every organization is the other write that could leave such an
/// account shared, and it is here for the same reason — its refusal is a count taken under the lock an assignment
/// waits on. A move of a user with the accounts they hold alone is the third, and is here beside them: what it moves
/// and what it refuses over is decided under the same locks, against the same rows. Beneath all three stands the index
/// that holds the rule for whatever writes the row, which is a claim about PostgreSQL and nothing else.
/// </para>
/// <para>
/// This class shares a database with every other in the collection, so every row it writes names a user, an account,
/// or an organization it created for the test alone, and all of them are removed in a <c>finally</c>.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSoleAssignmentOutsideOrganizationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many users are assigned one account at once: enough that some reliably lose, and few enough to cost seconds.</summary>
    private const int ConcurrentAssignments = 6;

    /// <summary>The version <see cref="OrchestratedForeignUser.ProvisionAsync" /> writes a user record at.</summary>
    private const long ProvisionedUserVersion = 1;

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 10, 8, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// One account in no organization assigned to several users at once is assigned to one of them, and every other
    /// attempt is answered that a mailbox is shared only inside an organization.
    /// </summary>
    [Fact]
    public async Task AssignAsync_ManyUsersAssignedOneAccountInNoOrganizationAtOnce_AssignsItToOneOfThem()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = Guid.CreateVersion7();
        Guid[] users = [.. Enumerable.Range(0, ConcurrentAssignments).Select(_ => Guid.CreateVersion7())];

        try
        {
            foreach (var user in users)
            {
                Assert.Equal(
                    PersistenceCommitResult.Committed,
                    await OrchestratedForeignUser.ProvisionAsync(services, user, cancellationToken));
            }

            await SeedAsync(services, [Record(account, organizationId: null)], [], cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(IMailAccountRecordStore)}.{nameof(IMailAccountRecordStore.AssignAsync)}",
                ConcurrentAssignments,
                (ordinal, attemptToken) => services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IMailAccountRecordStore>()
                        .AssignAsync(account, UserId.Create(users[ordinal]), ProvisionedUserVersion, token),
                    attemptToken),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect((await ReadAssignedUsersAsync(services, account, cancellationToken)).Length);
            Assert.Empty(attempts.Failures);
            Assert.Equal(
                [
                    MailAccountWriteResult.Committed,
                    .. Enumerable.Repeat(MailAccountWriteResult.SharedOnlyInOrganization, ConcurrentAssignments - 1),
                ],
                attempts.Results.Select(write => write.Result).Order());
        }
        finally
        {
            await RemoveAccountAsync(services, account);

            foreach (var user in users)
            {
                await OrchestratedForeignUser.EraseAsync(services, user);
            }
        }
    }

    /// <summary>
    /// A shared account asked to leave every organization stays where it is with both of its users, and leaves once
    /// it is one person's — which is the control showing the refusal was the rule's and not the move's.
    /// </summary>
    [Fact]
    public async Task SetMailAccountOrganizationAsync_ASharedAccountLeavingEveryOrganization_IsRefusedUntilItIsOnePersons()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var holder = Guid.CreateVersion7();
        var colleague = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, holder, cancellationToken));
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, colleague, cancellationToken));
            Assert.Equal(
                OrganizationWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                        organization,
                        $"Organization {organization:N}",
                        OrganizationShortName.Create($"{organization:N}"),
                        RecordedAt,
                        token),
                    cancellationToken)).Outcome);

            // Both users are in no organization, so nothing but the account being shared stands in the move's way.
            await SeedAsync(
                services,
                [Record(account, organization)],
                [Assignment(account, holder), Assignment(account, colleague)],
                cancellationToken);

            // Act
            var refused = await MoveOutOfEveryOrganizationAsync(services, account, cancellationToken);
            var stillShared = await ReadAssignedUsersAsync(services, account, cancellationToken);
            var stillIn = await ReadOrganizationAsync(services, account, cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .MailAccountAssignments
                    .Where(assignment => assignment.MailAccountId == account && assignment.UserId == colleague)
                    .ExecuteDeleteAsync(token),
                cancellationToken);
            var moved = await MoveOutOfEveryOrganizationAsync(services, account, cancellationToken);

            // Assert
            Assert.Equal(OrganizationWriteOutcome.SharedOnlyInOrganization, refused.Outcome);
            Assert.Equal(2, refused.StandingAssignments);
            Assert.Equal(new[] { holder, colleague }.Order(), stillShared.Order());
            Assert.Equal(organization, stillIn);
            Assert.Equal(OrganizationWriteOutcome.Written, moved.Outcome);
            Assert.Null(await ReadOrganizationAsync(services, account, cancellationToken));
        }
        finally
        {
            await RemoveAccountAsync(services, account);
            await OrchestratedForeignUser.EraseAsync(services, holder);
            await OrchestratedForeignUser.EraseAsync(services, colleague);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    /// <summary>
    /// A user moving with their accounts is refused, moving nothing, while one of them is shared with somebody staying
    /// behind, and once it is theirs alone the user and both accounts arrive together with every assignment and no
    /// mail erased — and can leave again the same way.
    /// </summary>
    [Fact]
    public async Task SetUserOrganizationAsync_WithMailAccounts_MovesTheAccountsTheyHoldAloneAndIsRefusedWhileOneIsShared()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var holder = Guid.CreateVersion7();
        var colleague = Guid.CreateVersion7();
        var standing = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        var soleAccount = Guid.CreateVersion7();
        var sharedAccount = Guid.CreateVersion7();

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, holder, cancellationToken));
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, colleague, cancellationToken));
            Assert.Equal(
                OrganizationWriteOutcome.Written,
                (await services.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                        organization,
                        $"Organization {organization:N}",
                        OrganizationShortName.Create($"{organization:N}"),
                        RecordedAt,
                        token),
                    cancellationToken)).Outcome);

            // Sharing a mailbox is something an organization does, so the colleagues share one in the organization they
            // both stand in, and the holder has a second there that is theirs alone.
            await CreateOrganizationAsync(services, standing, cancellationToken);
            Assert.Equal(
                OrganizationWriteOutcome.Written,
                (await MoveUserAsync(services, holder, standing, withMailAccounts: false, cancellationToken)).Outcome);
            Assert.Equal(
                OrganizationWriteOutcome.Written,
                (await MoveUserAsync(services, colleague, standing, withMailAccounts: false, cancellationToken)).Outcome);
            await SeedAsync(
                services,
                [Record(soleAccount, standing), Record(sharedAccount, standing)],
                [Assignment(soleAccount, holder), Assignment(sharedAccount, holder), Assignment(sharedAccount, colleague)],
                cancellationToken);

            // Act
            var withoutAccounts = await MoveUserAsync(services, holder, organization, withMailAccounts: false, cancellationToken);
            var whileShared = await MoveUserAsync(services, holder, organization, withMailAccounts: true, cancellationToken);
            var heldBack = await ReadOrganizationsAsync(services, holder, [soleAccount, sharedAccount], cancellationToken);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .MailAccountAssignments
                    .Where(assignment => assignment.MailAccountId == sharedAccount && assignment.UserId == colleague)
                    .ExecuteDeleteAsync(token),
                cancellationToken);
            var moved = await MoveUserAsync(services, holder, organization, withMailAccounts: true, cancellationToken);
            var arrived = await ReadOrganizationsAsync(services, holder, [soleAccount, sharedAccount], cancellationToken);
            var movedBack = await MoveUserAsync(services, holder, organizationId: null, withMailAccounts: true, cancellationToken);
            var returned = await ReadOrganizationsAsync(services, holder, [soleAccount, sharedAccount], cancellationToken);

            // Assert
            Assert.Equal(OrganizationWriteOutcome.AssignmentsOutsideOrganization, withoutAccounts.Outcome);
            Assert.Equal(2, withoutAccounts.StandingAssignments);
            Assert.Equal(OrganizationWriteOutcome.AssignmentsOutsideOrganization, whileShared.Outcome);
            Assert.Equal(1, whileShared.StandingAssignments);
            Assert.Equal([standing, standing, standing], heldBack);
            Assert.Equal(OrganizationWriteOutcome.Written, moved.Outcome);
            Assert.Equal([organization, organization, organization], arrived);
            Assert.Equal(OrganizationWriteOutcome.Written, movedBack.Outcome);
            Assert.Equal([null, null, null], returned);
            Assert.Equal([holder], await ReadAssignedUsersAsync(services, soleAccount, cancellationToken));
            Assert.Equal([holder], await ReadAssignedUsersAsync(services, sharedAccount, cancellationToken));
        }
        finally
        {
            await RemoveAccountAsync(services, soleAccount);
            await RemoveAccountAsync(services, sharedAccount);
            await OrchestratedForeignUser.EraseAsync(services, holder);
            await OrchestratedForeignUser.EraseAsync(services, colleague);
            await DeleteOrganizationAsync(services, standing);
            await DeleteOrganizationAsync(services, organization);
        }
    }

    /// <summary>
    /// PostgreSQL itself refuses a second user for an account in no organization, whatever writes the row: a second
    /// assignment inserted past the store, and a shared account moved out of its organization past the store, both
    /// fail on the index — while the same second assignment stands once the account is in an organization.
    /// </summary>
    [Fact]
    public async Task SoleUserIndex_WritesPastTheStoreThatWouldShareAnAccountInNoOrganization_AreRefusedByTheDatabase()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var holder = Guid.CreateVersion7();
        var colleague = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();
        var account = Guid.CreateVersion7();
        FormattableString assignColleague =
            $"""INSERT INTO mail_account_assignments ("UserId", "MailAccountId", "AssignedAt") VALUES ({colleague}, {account}, now())""";

        try
        {
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, holder, cancellationToken));
            Assert.Equal(
                PersistenceCommitResult.Committed,
                await OrchestratedForeignUser.ProvisionAsync(services, colleague, cancellationToken));
            await CreateOrganizationAsync(services, organization, cancellationToken);
            await SeedAsync(services, [Record(account, organizationId: null)], [Assignment(account, holder)], cancellationToken);

            // Act
            var secondUser = await Assert.ThrowsAsync<PostgresException>(
                () => ExecuteAsync(services, assignColleague, cancellationToken));
            await ExecuteAsync(
                services,
                $"""UPDATE settings_mail_accounts SET "OrganizationId" = {organization} WHERE "Id" = {account}""",
                cancellationToken);
            await ExecuteAsync(services, assignColleague, cancellationToken);
            var leaving = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(
                services,
                $"""UPDATE settings_mail_accounts SET "OrganizationId" = NULL WHERE "Id" = {account}""",
                cancellationToken));

            // Assert
            Assert.Equal(PersistenceConstraintNames.MailAccountAssignmentSoleUserIndexName, secondUser.ConstraintName);
            Assert.Equal(PersistenceConstraintNames.MailAccountAssignmentSoleUserIndexName, leaving.ConstraintName);
            Assert.Equal(organization, await ReadOrganizationAsync(services, account, cancellationToken));
            Assert.Equal(
                new[] { holder, colleague }.Order(),
                (await ReadAssignedUsersAsync(services, account, cancellationToken)).Order());
        }
        finally
        {
            await RemoveAccountAsync(services, account);
            await OrchestratedForeignUser.EraseAsync(services, holder);
            await OrchestratedForeignUser.EraseAsync(services, colleague);
            await DeleteOrganizationAsync(services, organization);
        }
    }

    /// <summary>Runs one parameterized statement past every store, which is the writer the index exists for.</summary>
    private static Task<int> ExecuteAsync(
        OrchestratedMailFathomServices services,
        FormattableString statement,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(statement, token),
            cancellationToken);

    private static async Task CreateOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => Assert.Equal(
            OrganizationWriteOutcome.Written,
            (await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    RecordedAt,
                    token),
                cancellationToken)).Outcome);

    /// <summary>Removes an organization this class wrote.</summary>
    /// <remarks>Uncancellable for the reason <see cref="OrchestratedForeignUser.EraseAsync" /> is: it runs in a <c>finally</c>.</remarks>
    private static Task<OrganizationWriteResult> DeleteOrganizationAsync(OrchestratedMailFathomServices services, Guid organization) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
            CancellationToken.None);

    private static Task<OrganizationWriteResult> MoveUserAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        Guid? organizationId,
        bool withMailAccounts,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().SetUserOrganizationAsync(
                UserId.Create(user),
                organizationId,
                withMailAccounts,
                underANarrowerGrant: false,
                token),
            cancellationToken);

    /// <summary>Reads where one user stands and where each of the accounts named sits, the user first.</summary>
    private static async Task<Guid?[]> ReadOrganizationsAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        Guid[] accounts,
        CancellationToken cancellationToken)
    {
        var userOrganization = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .Where(record => record.Id == user)
                .Select(record => record.OrganizationId)
                .SingleAsync(token),
            cancellationToken);

        Guid?[] accountOrganizations = [.. await Task.WhenAll(accounts.Select(account => ReadOrganizationAsync(services, account, cancellationToken)))];

        return [userOrganization, .. accountOrganizations];
    }

    private static Task<OrganizationWriteResult> MoveOutOfEveryOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>()
                .SetMailAccountOrganizationAsync(account, organizationId: null, underANarrowerGrant: false, token),
            cancellationToken);

    private static Task<Guid[]> ReadAssignedUsersAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountAssignments
                .AsNoTracking()
                .Where(assignment => assignment.MailAccountId == account)
                .Select(assignment => assignment.UserId)
                .ToArrayAsync(token),
            cancellationToken);

    private static Task<Guid?> ReadOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountRecords
                .AsNoTracking()
                .Where(record => record.Id == account)
                .Select(record => record.OrganizationId)
                .SingleAsync(token),
            cancellationToken);

    private static async Task SeedAsync(
        OrchestratedMailFathomServices services,
        MailAccountRecordEntity[] accounts,
        MailAccountAssignmentEntity[] assignments,
        CancellationToken cancellationToken) => Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (_, session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    context.MailAccountRecords.AddRange(accounts);
                    context.MailAccountAssignments.AddRange(assignments);

                    await context.SaveChangesAsync(token);
                },
                cancellationToken));

    /// <summary>Removes an account this class wrote, and every assignment to it with it.</summary>
    /// <remarks>Uncancellable for the reason <see cref="OrchestratedForeignUser.EraseAsync" /> is: it runs in a <c>finally</c>.</remarks>
    private static Task<int> RemoveAccountAsync(OrchestratedMailFathomServices services, Guid account) =>
        services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .MailAccountRecords
                .Where(record => record.Id == account)
                .ExecuteDeleteAsync(token),
            CancellationToken.None);

    private static MailAccountRecordEntity Record(Guid id, Guid? organizationId) => new()
    {
        Id = id,
        DisplayName = $"account-{id:N}",
        Document = "{}",
        OrganizationId = organizationId,
        Version = 1,
        CreatedAt = RecordedAt,
        UpdatedAt = RecordedAt,
    };

    private static MailAccountAssignmentEntity Assignment(Guid account, Guid user) => new()
    {
        UserId = user,
        MailAccountId = account,
        AssignedAt = RecordedAt,
    };
}
