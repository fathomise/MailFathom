// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Presence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the statements presence is held by: what one replica renews another reads, the bound on a member's clients, the bounded sweep, and who an organization's change is told to.</summary>
/// <remarks>
/// <para>
/// Every rule here is a statement rather than the process: the bound is the count inside the insert, the removal of a
/// member's own expired rows runs in the same batch ahead of it, and the sweep is a limited delete over the expiry
/// index. A substitute counting its own dictionary would pass every one of them and is the per-process shape ADR 0045
/// refused.
/// </para>
/// <para>
/// Instants are this class's own and lie far from any other class's, because the sweep reads the whole deployment's
/// table. Every user and organization arranged here is removed in a <c>finally</c>, which takes their presence rows
/// with them.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPresenceStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A client one replica renewed, the last seen it recorded, and a choice it stored are what another replica reads back, and a cleared choice reads as none.</summary>
    [Fact]
    public async Task ReadAsync_WhatAnotherHostRenewedAndChose_ReadsTheClientTheLastSeenAndTheChoice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var renewingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var readingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();
        var member = UserId.Create(user);

        await OrchestratedForeignUser.ProvisionAsync(renewingHost, user, cancellationToken);

        try
        {
            // Arrange
            var renewing = await StoreOfAsync(renewingHost, cancellationToken);
            var reading = await StoreOfAsync(readingHost, cancellationToken);
            Assert.True(await renewing.RenewClientAsync(member, Guid.NewGuid(), ClientActivity.Idle, Instant, Instant, cancellationToken));
            await renewing.ChooseAsync(member, new PresenceChoice(PresenceState.Busy, PresenceDuration.OneHour, Instant.AddHours(1)), cancellationToken);

            // Act
            var chosen = (await reading.ReadAsync([member], cancellationToken))[member];
            await renewing.ChooseAsync(member, choice: null, cancellationToken);
            var cleared = (await reading.ReadAsync([member], cancellationToken))[member];

            // Assert
            Assert.Equal(
                [new OpenPresenceClient(ClientActivity.Idle, Instant + MemberPresence.ClientLifetime)],
                chosen.Clients);
            Assert.Equal(new PresenceChoice(PresenceState.Busy, PresenceDuration.OneHour, Instant.AddHours(1)), chosen.Choice);
            Assert.Equal(Instant, chosen.LastSeen);
            Assert.Null(cleared.Choice);
            Assert.Equal(Instant, cleared.LastSeen);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(renewingHost, user);
        }
    }

    /// <summary>
    /// A member at the bound has a new client refused and a held one renewed, and once every client they hold has
    /// expired the next renewal finds room, because the batch removes the member's own expired rows before it counts.
    /// </summary>
    [Fact]
    public async Task RenewClientAsync_AtTheMostOpenClients_RefusesANewClientAndRenewsAHeldOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();
        var member = UserId.Create(user);

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            Guid[] held = [.. Enumerable.Range(0, MemberPresence.MostOpenClients).Select(_ => Guid.NewGuid())];

            foreach (var client in held)
            {
                Assert.True(await store.RenewClientAsync(member, client, ClientActivity.Active, Instant, lastSeen: null, cancellationToken));
            }

            // Act
            var newClient = await store.RenewClientAsync(member, Guid.NewGuid(), ClientActivity.Active, Instant, lastSeen: null, cancellationToken);
            var heldClient = await store.RenewClientAsync(member, held[0], ClientActivity.Idle, Instant, lastSeen: null, cancellationToken);
            var afterExpiry = await store.RenewClientAsync(
                member,
                Guid.NewGuid(),
                ClientActivity.Active,
                Instant + MemberPresence.ClientLifetime,
                lastSeen: null,
                cancellationToken);

            // Assert
            Assert.False(newClient);
            Assert.True(heldClient);
            Assert.True(afterExpiry);
            Assert.Single((await store.ReadAsync([member], cancellationToken))[member].Clients);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>The sweep removes expired clients and no open one, and never more than the batch it was given.</summary>
    [Fact]
    public async Task RemoveExpiredClientsAsync_ExpiredAndOpenClients_RemovesAtMostTheBatchOfExpiredOnes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();
        var member = UserId.Create(user);
        var sweptAt = Instant + MemberPresence.ClientLifetime;

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);

            // The open client first: a renewal removes its member's own expired rows, so writing it after the other
            // three would leave the sweep nothing to remove.
            Assert.True(await store.RenewClientAsync(member, Guid.NewGuid(), ClientActivity.Active, sweptAt, lastSeen: null, cancellationToken));

            foreach (var _ in Enumerable.Range(0, 3))
            {
                Assert.True(await store.RenewClientAsync(member, Guid.NewGuid(), ClientActivity.Active, Instant, lastSeen: null, cancellationToken));
            }

            // Act
            await store.RemoveExpiredClientsAsync(sweptAt, mostRemoved: 2, cancellationToken);
            var afterOneBatch = (await store.ReadAsync([member], cancellationToken))[member].Clients;
            await store.RemoveExpiredClientsAsync(sweptAt, mostRemoved: 2, cancellationToken);
            var afterTwoBatches = (await store.ReadAsync([member], cancellationToken))[member].Clients;

            // Assert
            Assert.Equal(2, afterOneBatch.Count);
            Assert.Equal(
                [new OpenPresenceClient(ClientActivity.Active, sweptAt + MemberPresence.ClientLifetime)],
                afterTwoBatches);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>The users a change is told to are the organization's own, and a user of another organization is not among them.</summary>
    [Fact]
    public async Task ReadOrganizationUsersAsync_TwoOrganizations_ReadsOnlyTheNamedOnesUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var organization = Guid.CreateVersion7();
        var otherOrganization = Guid.CreateVersion7();
        Guid[] members = [Guid.CreateVersion7(), Guid.CreateVersion7()];
        var outsider = Guid.CreateVersion7();

        try
        {
            // Arrange
            await HoldOrganizationAsync(host, organization, cancellationToken);
            await HoldOrganizationAsync(host, otherOrganization, cancellationToken);

            foreach (var member in members)
            {
                await ProvisionAsync(host, member, organization, cancellationToken);
            }

            await ProvisionAsync(host, outsider, otherOrganization, cancellationToken);

            // Act
            var told = await (await StoreOfAsync(host, cancellationToken)).ReadOrganizationUsersAsync(organization, cancellationToken);

            // Assert
            Assert.Equal(members.Order(), told.Select(user => user.Value).Order());
        }
        finally
        {
            foreach (var user in members.Append(outsider))
            {
                await OrchestratedForeignUser.EraseAsync(host, user);
            }

            foreach (var held in new[] { organization, otherOrganization })
            {
                await host.InScopeAsync(
                    (scope, token) => scope.GetRequiredService<IOrganizationStore>().DeleteAsync(held, token),
                    CancellationToken.None);
            }
        }
    }

    /// <summary>Erasing a user takes their clients and their chosen presence with them, so nothing about a person outlives their record.</summary>
    [Fact]
    public async Task EraseAsync_AUserHoldingPresence_TakesItWithThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();
        var member = UserId.Create(user);

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var store = await StoreOfAsync(host, cancellationToken);
            Assert.True(await store.RenewClientAsync(member, Guid.NewGuid(), ClientActivity.Active, Instant, Instant, cancellationToken));
            await store.ChooseAsync(member, new PresenceChoice(PresenceState.Hidden, Duration: null, EndsAt: null), cancellationToken);

            // Act
            await OrchestratedForeignUser.EraseAsync(host, user);

            // Assert
            var left = (await store.ReadAsync([member], cancellationToken))[member];
            Assert.Empty(left.Clients);
            Assert.Null(left.Choice);
            Assert.Null(left.LastSeen);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    private static async Task HoldOrganizationAsync(
        OrchestratedMailFathomServices host,
        Guid organization,
        CancellationToken cancellationToken)
    {
        var created = await host.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOrganizationStore>().CreateAsync(
                organization,
                $"Presence {organization:N}",
                OrganizationShortName.Create($"{organization:N}"),
                Instant,
                token),
            cancellationToken);

        Assert.Equal(OrganizationWriteOutcome.Written, created.Outcome);
    }

    private static async Task ProvisionAsync(
        OrchestratedMailFathomServices host,
        Guid user,
        Guid organization,
        CancellationToken cancellationToken) => Assert.Equal(
            UserProvisioningResult.Provisioned,
            await host.InScopeAsync(
                (scope, token) => scope.GetRequiredService<IUserProvisioning>().ProvisionAsync(
                    UserId.Create(user),
                    $"presence-{user:N}",
                    organization,
                    token),
                cancellationToken));

    private static Task<IPresenceStore> StoreOfAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<IPresenceStore>()),
            cancellationToken);
}
