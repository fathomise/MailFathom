// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the capability record survives its columns, that a stale recording cannot replace it, and that an unreached account reads as advertising nothing.</summary>
/// <remarks>
/// The ordering of two replicas' recordings is a <c>WHERE</c> clause only PostgreSQL evaluates, and the flag set is
/// stored by name through a conversion only a real column exercises, so neither is reachable from a unit test.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxServerCapabilityStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string RefreshedAccount = "capabilities-refreshed";

    private const string StragglerAccount = "capabilities-straggler";

    private const string UnreachedAccount = "capabilities-unreached";

    [Fact]
    public async Task RecordAsync_TwoSessionsInTurn_LeavesWhatTheLaterOneObserved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create(RefreshedAccount);
        await AddAccountRowAsync(services, RefreshedAccount, cancellationToken);

        // Act
        await RecordAsync(services, account, MailboxServerCapabilities.None, cancellationToken);
        await RecordAsync(services, account, MailboxServerCapabilities.UidPlus | MailboxServerCapabilities.Imap4Rev2, cancellationToken);
        var observation = await ReadAsync(services, account, cancellationToken);

        // Assert
        Assert.Equal(MailboxServerCapabilities.UidPlus | MailboxServerCapabilities.Imap4Rev2, observation.Advertised);
        Assert.NotNull(observation.ObservedAt);
    }

    /// <summary>
    /// Moving the stored instant forward is what a later session on another replica leaves behind, and it is the only
    /// way to make the next real recording arrive stale without a clock this suite does not control.
    /// </summary>
    [Fact]
    public async Task RecordAsync_AnObservationOlderThanTheStoredOne_LeavesTheNewerOneInPlace()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create(StragglerAccount);
        await AddAccountRowAsync(services, StragglerAccount, cancellationToken);
        await RecordAsync(services, account, MailboxServerCapabilities.UidPlus, cancellationToken);

        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().Database.ExecuteSqlAsync(
                $"""
                 UPDATE mailbox_accounts
                 SET "ServerCapabilitiesObservedAt" = "ServerCapabilitiesObservedAt" + INTERVAL '1 hour'
                 WHERE "Id" = {StragglerAccount}
                 """,
                token),
            cancellationToken);

        // Act
        await RecordAsync(services, account, MailboxServerCapabilities.None, cancellationToken);
        var observation = await ReadAsync(services, account, cancellationToken);

        // Assert
        Assert.Equal(MailboxServerCapabilities.UidPlus, observation.Advertised);
    }

    [Fact]
    public async Task ReadAsync_AnAccountNoSessionReached_ReadsAsNeverObserved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await AddAccountRowAsync(services, UnreachedAccount, cancellationToken);

        // Act
        var observation = await ReadAsync(services, MailAccountId.Create(UnreachedAccount), cancellationToken);

        // Assert
        Assert.Same(MailboxServerCapabilityObservation.NeverObserved, observation);
    }

    private static Task<int> AddAccountRowAsync(
        OrchestratedMailFathomServices services,
        string accountId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();

                if (await dbContext.MailboxAccounts.AnyAsync(row => row.Id == accountId, token))
                {
                    return 0;
                }

                dbContext.MailboxAccounts.Add(new MailboxAccountEntity { Id = accountId });

                return await dbContext.SaveChangesAsync(token);
            },
            cancellationToken);

    private static Task<bool> RecordAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        MailboxServerCapabilities advertised,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IMailboxServerCapabilityStore>().RecordAsync(account, advertised, token);

                return true;
            },
            cancellationToken);

    private static Task<MailboxServerCapabilityObservation> ReadAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxServerCapabilityStore>().ReadAsync(account, token),
            cancellationToken);
}
