// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that a user recorded into an organization enters it in the statement that records them, and that a relabel writes their row alone.</summary>
/// <remarks>
/// The organization is selected inside the insert rather than written as a value, so an organization the deployment
/// does not hold leaves no row rather than raising its foreign key's violation. A relabel is an update selected by the
/// user's identifier and guarded by nothing else, so what it must not do — reach a second row, or report a user the
/// deployment does not hold as relabelled — is only visible against the table itself. Each is a statement only
/// PostgreSQL can answer. Every user and
/// organization arranged here is removed in a <c>finally</c>, because a second user row left behind breaks every later
/// start in this collection rather than the test that wrote it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedUserProvisioningTests(MailFathomOrchestrationFixture orchestration)
{
    [Fact]
    public async Task ProvisionAsync_AnOrganizationTheDeploymentHolds_RecordsTheUserInsideIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();
        var organization = Guid.CreateVersion7();

        try
        {
            var created = await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                    organization,
                    $"Organization {organization:N}",
                    OrganizationShortName.Create($"{organization:N}"),
                    DateTimeOffset.UnixEpoch,
                    token),
                cancellationToken);

            Assert.Equal(OrganizationWriteOutcome.Written, created.Outcome);

            // Act
            var outcome = await ProvisionAsync(services, user, organization, cancellationToken);

            // Assert
            Assert.Equal(UserProvisioningResult.Provisioned, outcome);
            Assert.Equal(organization, await ReadOrganizationOfAsync(services, user, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
            await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProvisionAsync_AnOrganizationTheDeploymentDoesNotHold_RecordsNobody()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.CreateVersion7();

        try
        {
            // Act
            var outcome = await ProvisionAsync(services, user, Guid.CreateVersion7(), cancellationToken);

            // Assert
            Assert.Equal(UserProvisioningResult.UnknownOrganization, outcome);
            Assert.False(await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                    .UserAccounts
                    .AsNoTracking()
                    .AnyAsync(record => record.Id == user, token),
                cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, user);
        }
    }

    /// <summary>A label another user carries is put on the user named, and the user carrying it keeps theirs.</summary>
    [Fact]
    public async Task RelabelAsync_ALabelAnotherUserCarries_RelabelsTheUserNamedAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var relabelled = Guid.CreateVersion7();
        var namesake = Guid.CreateVersion7();
        var shared = $"shared-{namesake:N}";

        try
        {
            Assert.Equal(
                UserProvisioningResult.Provisioned,
                await ProvisionAsync(services, relabelled, $"provisioned-{relabelled:N}", cancellationToken));
            Assert.Equal(
                UserProvisioningResult.Provisioned,
                await ProvisionAsync(services, namesake, shared, cancellationToken));

            // Act
            var held = await RelabelAsync(services, relabelled, shared, cancellationToken);

            // Assert
            Assert.True(held);
            Assert.Equal(shared, await ReadLabelOfAsync(services, relabelled, cancellationToken));
            Assert.Equal(shared, await ReadLabelOfAsync(services, namesake, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, relabelled);
            await OrchestratedForeignUser.EraseAsync(services, namesake);
        }
    }

    /// <summary>A relabel of an identifier no row carries writes nothing and says the deployment does not hold them.</summary>
    [Fact]
    public async Task RelabelAsync_AUserTheDeploymentDoesNotHold_ReportsThemUnheld()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var label = $"nobody-{Guid.CreateVersion7():N}";

        // Act
        var held = await RelabelAsync(services, Guid.CreateVersion7(), label, cancellationToken);

        // Assert
        Assert.False(held);
        Assert.False(await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .AnyAsync(record => record.DisplayName == label, token),
            cancellationToken));
    }

    /// <summary>A title and a type are written on the user named alone, a title is taken off again, and a user nobody holds is reported unheld.</summary>
    [Fact]
    public async Task SetJobTitleAndTypeAsync_AUserAndABystander_WriteTheNamedRowAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var written = Guid.CreateVersion7();
        var bystander = Guid.CreateVersion7();

        try
        {
            Assert.Equal(
                UserProvisioningResult.Provisioned,
                await ProvisionAsync(services, written, $"provisioned-{written:N}", cancellationToken));
            Assert.Equal(
                UserProvisioningResult.Provisioned,
                await ProvisionAsync(services, bystander, $"provisioned-{bystander:N}", cancellationToken));

            // Act
            var answers = await services.InScopeAsync(
                async (scope, token) =>
                {
                    var provisioning = scope.GetRequiredService<IUserProvisioning>();

                    return (
                        Titled: await provisioning.SetJobTitleAsync(UserId.Create(written), "Controller", token),
                        Typed: await provisioning.SetTypeAsync(UserId.Create(written), UserType.ServiceAccount, token),
                        Unheld: await provisioning.SetJobTitleAsync(UserId.Create(Guid.CreateVersion7()), "Nobody", token)
                            || await provisioning.SetTypeAsync(UserId.Create(Guid.CreateVersion7()), UserType.ServiceAccount, token));
                },
                cancellationToken);
            var afterWriting = await ReadTitleAndTypeOfAsync(services, written, cancellationToken);
            var untouched = await ReadTitleAndTypeOfAsync(services, bystander, cancellationToken);

            Assert.True(await services.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserProvisioning>().SetJobTitleAsync(UserId.Create(written), jobTitle: null, token),
                cancellationToken));
            var afterTakingOff = await ReadTitleAndTypeOfAsync(services, written, cancellationToken);

            // Assert
            Assert.True(answers.Titled);
            Assert.True(answers.Typed);
            Assert.False(answers.Unheld);
            Assert.Equal(("Controller", UserType.ServiceAccount), afterWriting);
            Assert.Equal(((string?)null, UserType.User), untouched);
            Assert.Equal(((string?)null, UserType.ServiceAccount), afterTakingOff);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(services, written);
            await OrchestratedForeignUser.EraseAsync(services, bystander);
        }
    }

    private static Task<(string? JobTitle, UserType Type)> ReadTitleAndTypeOfAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var row = await scope.GetRequiredService<MailFathomDbContext>()
                    .UserAccounts
                    .AsNoTracking()
                    .Where(record => record.Id == user)
                    .Select(record => new { record.JobTitle, record.Type })
                    .SingleAsync(token);

                return (row.JobTitle, row.Type);
            },
            cancellationToken);

    private static Task<UserProvisioningResult> ProvisionAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        string label,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IUserProvisioning>().ProvisionAsync(
                UserId.Create(user),
                label,
                organizationId: null,
                token),
            cancellationToken);

    private static Task<bool> RelabelAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        string label,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IUserProvisioning>().RelabelAsync(UserId.Create(user), label, token),
            cancellationToken);

    private static Task<string> ReadLabelOfAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .Where(record => record.Id == user)
                .Select(record => record.DisplayName)
                .SingleAsync(token),
            cancellationToken);

    private static Task<UserProvisioningResult> ProvisionAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        Guid organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IUserProvisioning>().ProvisionAsync(
                UserId.Create(user),
                $"provisioned-{user:N}",
                organization,
                token),
            cancellationToken);

    private static Task<Guid?> ReadOrganizationOfAsync(
        OrchestratedMailFathomServices services,
        Guid user,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .UserAccounts
                .AsNoTracking()
                .Where(record => record.Id == user)
                .Select(record => record.OrganizationId)
                .SingleAsync(token),
            cancellationToken);
}
