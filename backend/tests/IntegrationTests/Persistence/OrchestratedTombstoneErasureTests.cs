// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves against real PostgreSQL which rows the nightly pass erases and what goes with them.</summary>
/// <remarks>
/// Every case runs on an account of its own, because the database is shared with the rest of the collection. Which
/// custody phase runs the pass, the batching, and the night's bound are decided without a database and stay in the unit
/// suite.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedTombstoneErasureTests(MailFathomOrchestrationFixture orchestration)
{
    private const int NoBound = 1000;

    private static readonly DateTimeOffset Cutoff = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset BeforeCutoff = Cutoff.AddDays(-1);
    private static readonly DateTimeOffset AfterCutoff = Cutoff.AddDays(1);

    [Fact]
    public async Task EraseAsync_TombstonesOfOneAccount_ErasesOnlyAgedSettledOnesOutsideTheExcludedFolders()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create("tombstone-erasure-selection");
        var neighbour = MailAccountId.Create("tombstone-erasure-neighbour");
        var inbox = await BindAsync(services, account, "tombstone-erasure-inbox", cancellationToken);
        var kept = await BindAsync(services, account, "tombstone-erasure-kept", cancellationToken);
        var neighbourInbox = await BindAsync(services, neighbour, "tombstone-erasure-neighbour-inbox", cancellationToken);

        var aged = await StoreAsync(services, account, inbox, 9400, cancellationToken);
        var young = await StoreAsync(services, account, inbox, 9401, cancellationToken);
        var committedLocally = await StoreAsync(services, account, inbox, 9402, cancellationToken);
        var outstanding = await StoreAsync(services, account, inbox, 9403, cancellationToken);
        var excluded = await StoreAsync(services, account, kept, 9404, cancellationToken);
        var retained = await StoreAsync(services, account, inbox, 9405, cancellationToken);
        var live = await StoreAsync(services, account, inbox, 9406, cancellationToken);
        var ofTheNeighbour = await StoreAsync(services, neighbour, neighbourInbox, 9407, cancellationToken);

        await SetAsync(services, aged, expungedAt: BeforeCutoff, cancellationToken);
        await SetAsync(services, young, expungedAt: AfterCutoff, cancellationToken);
        await SetAsync(services, outstanding, expungedAt: BeforeCutoff, cancellationToken);
        await SetAsync(services, excluded, expungedAt: BeforeCutoff, cancellationToken);
        await SetAsync(services, retained, expungedAt: BeforeCutoff, cancellationToken, isRetained: true);
        await SetAsync(services, ofTheNeighbour, expungedAt: BeforeCutoff, cancellationToken);
        await HideLocallyAsync(services, committedLocally, cancellationToken);
        await RecordOutstandingChangeAsync(services, outstanding, cancellationToken);

        // Act
        var erased = await EraseAsync(services, account, ["tombstone-erasure-kept"], isSourceDrained: false, cancellationToken);

        // Assert
        Assert.Equal(1, erased);
        Assert.Equal(
            new[] { young, committedLocally, outstanding, excluded, retained, live }.Select(email => email.Value).Order(),
            await StoredIdsAsync(services, account, cancellationToken));
        Assert.Single(await StoredIdsAsync(services, neighbour, cancellationToken));
    }

    [Fact]
    public async Task EraseAsync_AHeldAccount_RecordsOnlyWhatTheSourceStillHoldsForTheDrain()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create("tombstone-erasure-held");
        var inbox = await BindAsync(services, account, "tombstone-erasure-held-inbox", cancellationToken);
        var flagged = await StoreAsync(services, account, inbox, 9410, cancellationToken);
        var expunged = await StoreAsync(services, account, inbox, 9411, cancellationToken);

        await SetAsync(services, flagged, expungedAt: null, cancellationToken, flaggedAt: BeforeCutoff);
        await SetAsync(services, expunged, expungedAt: BeforeCutoff, cancellationToken);

        // Act
        var erased = await EraseAsync(services, account, [], isSourceDrained: true, cancellationToken);

        // Assert
        Assert.Equal(2, erased);
        Assert.Empty(await StoredIdsAsync(services, account, cancellationToken));

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var removal = await scope.GetRequiredService<MailFathomDbContext>().MailboxSourceRemovals
                    .SingleAsync(entry => entry.MailboxAccountId == account.Value, token);

                Assert.Equal(9410u, removal.Uid);

                return true;
            },
            cancellationToken);
    }

    [Fact]
    public async Task EraseAsync_MoreTombstonesThanTheLimit_ErasesTheLimitAndLeavesTheRest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create("tombstone-erasure-bounded");
        var inbox = await BindAsync(services, account, "tombstone-erasure-bounded-inbox", cancellationToken);

        await SetAsync(services, await StoreAsync(services, account, inbox, 9420, cancellationToken), BeforeCutoff, cancellationToken);
        await SetAsync(services, await StoreAsync(services, account, inbox, 9421, cancellationToken), BeforeCutoff, cancellationToken);

        // Act
        var erased = await EraseAsync(services, account, [], isSourceDrained: false, cancellationToken, limit: 1);

        // Assert
        Assert.Equal(1, erased);
        Assert.Single(await StoredIdsAsync(services, account, cancellationToken));
    }

    private static async Task<int> EraseAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        IReadOnlyList<string> excludedFolders,
        bool isSourceDrained,
        CancellationToken cancellationToken,
        int limit = NoBound)
    {
        var erased = 0;
        var retention = new MailTombstoneRetention(IsEnabled: true, TimeSpan.FromDays(30), excludedFolders);

        var result = await services.CommitAsync(
            async (scope, session, token) => erased = await scope.GetRequiredService<IStoredTombstoneEraser>()
                .EraseAsync(session, account, retention, Cutoff, isSourceDrained, limit, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, result);

        return erased;
    }

    private static Task<MailFolderResolution> BindAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        string alias,
        CancellationToken cancellationToken) =>
        OrchestratedFolderBinding.CommitAsync(services, account, alias, $"INBOX.{alias}", cancellationToken);

    private static async Task<StoredEmailId> StoreAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken)
    {
        StoredEmailId stored = default;

        var committed = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var occurrenceId = SyntheticEmail.OccurrenceIn(account, binding, uid);
                var subject = $"tombstone-erasure-{uid}";

                stored = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, isRemotelySeen: false),
                    SyntheticEmail.ExtractionOf(occurrenceId, subject, SyntheticEmail.BodyTextContaining("tombstone", wordCount: 12)),
                    StoredEmailContentAvailability.Available,
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, committed);

        return stored;
    }

    private static Task<bool> SetAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        DateTimeOffset? expungedAt,
        CancellationToken cancellationToken,
        DateTimeOffset? flaggedAt = null,
        bool isRetained = false) => services.InScopeAsync(
        async (scope, token) =>
        {
            await scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .Where(row => row.Id == email.Value)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(row => row.RemoteExpungeObservedAt, expungedAt)
                        .SetProperty(row => row.AuthoredDeleteFlaggedAt, flaggedAt)
                        .SetProperty(row => row.IsRetainedAfterRemoteDelete, isRetained),
                    token);

            return true;
        },
        cancellationToken);

    private static Task<bool> HideLocallyAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            await scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .Where(row => row.Id == email.Value)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LocalDeleteCommittedAt, BeforeCutoff), token);

            return true;
        },
        cancellationToken);

    private static Task<bool> RecordOutstandingChangeAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            var stored = await context.StoredEmails
                .Include(row => row.MailFolderBinding)
                .SingleAsync(row => row.Id == email.Value, token);

            context.MailboxMutations.Add(new MailboxMutationEntity
            {
                Id = Guid.CreateVersion7(),
                StoredEmailId = stored.Id,
                StoredEmail = stored,
                MailboxAccountId = stored.MailboxAccountId,
                MailFolderBinding = stored.MailFolderBinding,
                UidValidity = stored.UidValidity!.Value,
                Uid = stored.Uid!.Value,
                Mutation = MailboxMutation.Delete.Name,
                RequesterIdentity = "tombstone-erasure",
                Stage = MailboxMutationStage.Recorded,
                RecordedAt = BeforeCutoff,
                StageChangedAt = BeforeCutoff,
            });
            await context.SaveChangesAsync(token);

            return true;
        },
        cancellationToken);

    private static Task<Guid[]> StoredIdsAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        CancellationToken cancellationToken) => services.InScopeAsync(
        (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
            .Where(row => row.MailboxAccountId == account.Value)
            .OrderBy(row => row.Id)
            .Select(row => row.Id)
            .ToArrayAsync(token),
        cancellationToken);
}
