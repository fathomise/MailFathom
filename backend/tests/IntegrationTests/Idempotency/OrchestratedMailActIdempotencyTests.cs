// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Idempotency;

/// <summary>Proves the two claims a mirrored account's locally committed acts make about being reached twice at once.</summary>
/// <remarks>
/// <para>
/// A change committed locally first is written beside a durable record, and a client may retry it under a key of its own.
/// Both are reached by several replicas at the same moment, where none can see another's uncommitted row: the record is
/// decided by the unique index over the message, the requester, and the mutation, and the key by the unique index over
/// the person, the key, and the message. The loser of either is reported as a conflict its caller retries from a fresh
/// read, so the claim is that the rows left behind are one, however many callers arrived.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailActIdempotencyTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "mutation-overlay-idempotency";

    /// <summary>How many callers race for one effect, enough that a caller reliably loses.</summary>
    private const int ConcurrentWriters = 6;

    private const uint OverlaidUid = 9701;

    private const uint KeyedUid = 9702;

    /// <summary>
    /// One change offered by every replica at once leaves one record. The identity index refuses the callers that lose,
    /// and a caller that read the winner's record first commits nothing, so neither can leave a second record on the
    /// message or withdraw the first.
    /// </summary>
    [Fact]
    public async Task OpenOverlayAsync_ManyReplicasReachingOneRequest_LeavesOneRecord()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(binding, OverlaidUid);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(services, occurrence, "overlay-race", cancellationToken);
        var request = MailboxMutationRequest.SetSeen(
            storedEmailId,
            occurrence,
            MailboxMutationRequester.Command("overlay-race"),
            isSeen: true);
        var replaced = new MailboxMutationOverlay(ObservedSeenState: false, null, null, null, null);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(IMailboxMutationRecordStore)}.{nameof(IMailboxMutationRecordStore.OpenOverlayAsync)}",
            ConcurrentWriters,
            (_, token) => services.InScopeAsync(
                async (scope, scopeToken) =>
                {
                    await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                        .BeginSessionAsync(scopeToken);

                    await scope.GetRequiredService<IMailboxMutationRecordStore>()
                        .OpenOverlayAsync(session, request, heldUntil: null, replaced, scopeToken);

                    return await session.CommitAsync(scopeToken);
                },
                token),
            cancellationToken);

        // Assert
        var recordedChanges = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().MailboxMutations
                .AsNoTracking()
                .CountAsync(mutation => mutation.StoredEmailId == storedEmailId.Value, token),
            cancellationToken);

        attempts.AssertSingleEffect(recordedChanges);
        Assert.Contains(PersistenceCommitResult.Committed, attempts.Results);
    }

    /// <summary>
    /// One client key claimed by every replica at once leaves one entry. The unique index refuses the callers that lose
    /// their insert, which is how they learn the first got there, and the entry that remains is the winner's.
    /// </summary>
    [Fact]
    public async Task StageAsync_ManyReplicasClaimingOneClientKey_LeavesOneEntry()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(binding, KeyedUid);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(services, occurrence, "key-race", cancellationToken);
        var user = SyntheticMailAccount.User;
        var key = MailActIdempotencyKey.Create($"key-race-{Guid.CreateVersion7():N}", "set-seen", [storedEmailId.ToString()]);
        var outcome = new MailActOutcome("recorded", [], []);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(IMailActKeyStore)}.{nameof(IMailActKeyStore.StageAsync)}",
            ConcurrentWriters,
            (_, token) => services.InScopeAsync(
                async (scope, scopeToken) =>
                {
                    await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                        .BeginSessionAsync(scopeToken);

                    await scope.GetRequiredService<IMailActKeyStore>().StageAsync(
                        session,
                        user,
                        key,
                        storedEmailId,
                        outcome,
                        TimeProvider.System.GetUtcNow(),
                        scopeToken);

                    return await session.CommitAsync(scopeToken);
                },
                token),
            cancellationToken);

        // Assert
        var keyValue = key.Value;
        var entries = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().MailActKeys
                .AsNoTracking()
                .CountAsync(row => row.UserId == user.Value && row.Key == keyValue, token),
            cancellationToken);

        attempts.AssertSingleEffect(entries);
        Assert.Contains(PersistenceCommitResult.Committed, attempts.Results);

        var entry = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailActKeyStore>().FindAsync(user, keyValue, storedEmailId, token),
            cancellationToken);
        Assert.Equal(key.Fingerprint, entry!.Fingerprint);
    }
}
