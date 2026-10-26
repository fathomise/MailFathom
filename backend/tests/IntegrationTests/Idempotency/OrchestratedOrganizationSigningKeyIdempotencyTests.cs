// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Access.Organizations.SigningKeys;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves that an organization holds one active signing key however many replicas reach it at once.</summary>
/// <remarks>
/// Nothing in the application reads before it writes here: every replica mints and seals a key, retires the active one,
/// and inserts its own, and the partial unique index over active keys is the only thing deciding which insert stands.
/// The losers' inserts write nothing, they roll their retirement back, and each answers with the winner's key — which
/// is what these assert, against the raw statements no unit test runs.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOrganizationSigningKeyIdempotencyTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many replicas race for one organization, for the reason the other classes here give.</summary>
    private const int ConcurrentReplicas = 6;

    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RotatedAt = RecordedAt.AddDays(1);

    [Fact]
    public async Task ReplaceActiveKeyAsync_ManyReplicasRotatingOneOrganization_LeavesOneActiveKeyEveryReplicaNames()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        await CreateOrganizationAsync(services, organization, cancellationToken);

        try
        {
            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(IOrganizationSigningKeyStore)}.{nameof(IOrganizationSigningKeyStore.ReplaceActiveKeyAsync)}",
                ConcurrentReplicas,
                (_, token) => ReplaceAsync(services, organization, RecordedAt, token),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect(await CountKeysAsync(services, organization, created: RotatedAt, cancellationToken));
            Assert.Single(attempts.Results, replacement => replacement.Outcome == OrganizationSigningKeyReplacementOutcome.Replaced);
            Assert.Equal(1, await CountActiveKeysAsync(services, organization, cancellationToken));
            Assert.Single(attempts.Results.Select(replacement => replacement.ActiveKeyId).Distinct());
        }
        finally
        {
            await DeleteOrganizationAsync(services, organization);
        }
    }

    /// <summary>An organization holding no key — one recorded before the deployment had a key ring — is given exactly one.</summary>
    [Fact]
    public async Task ReplaceActiveKeyAsync_ManyReplicasMintingForAnOrganizationWithoutAKey_MintsOne()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        await CreateOrganizationAsync(services, organization, cancellationToken);
        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().OrganizationSigningKeys
                .Where(key => key.OrganizationId == organization)
                .ExecuteDeleteAsync(token),
            cancellationToken);

        try
        {
            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                $"{nameof(IOrganizationSigningKeyStore)}.{nameof(IOrganizationSigningKeyStore.ReplaceActiveKeyAsync)}",
                ConcurrentReplicas,
                (_, token) => ReplaceAsync(services, organization, RecordedAt, token),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect(await CountKeysAsync(services, organization, created: RotatedAt, cancellationToken));
            Assert.Equal(1, await CountActiveKeysAsync(services, organization, cancellationToken));
            Assert.Single(attempts.Results.Select(replacement => replacement.ActiveKeyId).Distinct());
        }
        finally
        {
            await DeleteOrganizationAsync(services, organization);
        }
    }

    private static Task<OrganizationSigningKeyReplacement> ReplaceAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        DateTimeOffset replaceKeyCreatedAtOrBefore,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationSigningKeyStore>().ReplaceActiveKeyAsync(
                organization,
                replaceKeyCreatedAtOrBefore,
                RotatedAt,
                token),
            cancellationToken);

    private static Task<OrganizationWriteResult> CreateOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                organization,
                $"Organization {organization:N}",
                OrganizationShortName.Create($"{organization:N}"),
                RecordedAt,
                token),
            cancellationToken);

    private static Task<int> CountKeysAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        DateTimeOffset created,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().OrganizationSigningKeys
                .CountAsync(key => key.OrganizationId == organization && key.CreatedAt == created, token),
            cancellationToken);

    private static Task<int> CountActiveKeysAsync(
        OrchestratedMailFathomServices services,
        Guid organization,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().OrganizationSigningKeys
                .CountAsync(
                    key => key.OrganizationId == organization && key.RetiredAt == null && key.RevokedAt == null,
                    token),
            cancellationToken);

    private static Task<OrganizationWriteResult> DeleteOrganizationAsync(
        OrchestratedMailFathomServices services,
        Guid organization) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(organization, token),
            CancellationToken.None);
}
