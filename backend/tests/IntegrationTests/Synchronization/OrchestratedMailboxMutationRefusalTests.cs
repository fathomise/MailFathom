// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Proves what the mutation store writes and reads about refused and waiting changes against real PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The unit suite drives the refusal window and the waiting count through an in-memory store, which cannot show that a
/// first refusal survives a later one in the column, that an answered placement moves its row back, or that the two
/// reads translate — an ordered, paged read across a join and a grouped count with a minimum — and filter on the stages
/// the window and the count are defined over.
/// </para>
/// <para>
/// Each test writes under an account of its own, which nothing else in the shared database names, so what a read over
/// the whole account answers is exactly what the test wrote.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxMutationRefusalTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "mutation-refusals";

    /// <summary>How finely the database keeps an instant, which is coarser than the clock that produced it.</summary>
    private static readonly TimeSpan StoredInstantPrecision = TimeSpan.FromMicroseconds(1);

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Rule("file-to-archive", "1");

    private static readonly RemoteFolderPath ArchivePath = RemoteFolderPath.Create("Archive", hierarchyDelimiter: '.');

    /// <summary>
    /// A change refused, then issued as a placement, then refused again: the second answer returns the row to the stage
    /// the placement was issued from, counts a second attempt, and leaves the window measured from the first refusal.
    /// </summary>
    [Fact]
    public async Task RecordRefusalAsync_AnIssuedPlacementRefusedAgain_ReturnsToRecordedAndKeepsTheFirstRefusal()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create("mutation-refusal-recording");
        var binding = await OrchestratedFolderBinding.CommitAsync(services, account, FolderAlias, FolderAlias, cancellationToken);
        var recordId = await OpenRelocationAsync(services, account, binding, uid: 1U, cancellationToken);
        var firstRefusedAt = await RefuseAsync(services, recordId, cancellationToken);
        await CommitOrFailAsync(
            services,
            (store, session, token) => store.RecordPlacementIssuedAsync(session, recordId, requiresSourceRemoval: true, token),
            cancellationToken);

        // Act
        var answeredFirstRefusedAt = await RefuseAsync(services, recordId, cancellationToken);

        // Assert
        var record = await ReadAsync(services, account, recordId, cancellationToken);
        Assert.Equal(
            (MailboxMutationStage.Recorded, false, 2, (MailFathomErrorCode?)MailFathomErrorCode.MailboxMutationDeclined),
            (record.Stage, record.RequiresSourceRemoval, record.AttemptCount, record.LastFailure));
        Assert.True((record.FirstRefusedAt!.Value - firstRefusedAt).Duration() < StoredInstantPrecision);
        Assert.Equal(record.FirstRefusedAt, answeredFirstRefusedAt);
    }

    /// <summary>
    /// The deadline sweep reads an account's refused changes earliest first refusal first, and reads neither one that
    /// ended nor one whose placement went unanswered afterwards — that one may already be on the server, and its
    /// settlement rather than its window decides it.
    /// </summary>
    [Fact]
    public async Task ReadRefusedAsync_AnAccountsRefusedChanges_ReadsTheOutstandingOnesEarliestRefusalFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var account = MailAccountId.Create("mutation-refusal-reading");
        var binding = await OrchestratedFolderBinding.CommitAsync(services, account, FolderAlias, FolderAlias, cancellationToken);

        // Opened before the one refused first, so an order by identity would read them the other way round.
        var refusedSecond = await OpenRelocationAsync(services, account, binding, uid: 1U, cancellationToken);
        var refusedFirst = await OpenRelocationAsync(services, account, binding, uid: 2U, cancellationToken);
        var placedAfterRefusal = await OpenRelocationAsync(services, account, binding, uid: 3U, cancellationToken);
        var ended = await OpenRelocationAsync(services, account, binding, uid: 4U, cancellationToken);
        await OpenRelocationAsync(services, account, binding, uid: 5U, cancellationToken);

        await RefuseAsync(services, refusedFirst, cancellationToken);
        await RefuseAsync(services, refusedSecond, cancellationToken);
        await RefuseAsync(services, placedAfterRefusal, cancellationToken);
        await RefuseAsync(services, ended, cancellationToken);
        await CommitOrFailAsync(
            services,
            async (store, session, token) =>
            {
                await store.RecordPlacementIssuedAsync(session, placedAfterRefusal, requiresSourceRemoval: true, token);
                await store.AdvanceAsync(session, ended, MailboxMutationStage.Completed, placement: null, token);
            },
            cancellationToken);

        // Act
        var refused = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationRecordStore>().ReadRefusedAsync(account, limit: 10, token),
            cancellationToken);

        // Assert
        Assert.Equal([refusedFirst, refusedSecond], refused.Select(candidate => candidate.Record.Id));
        Assert.All(refused, candidate => Assert.Equal(binding.Id, candidate.Folder.Id));
    }

    /// <summary>
    /// The account list's waiting count is one grouped read: per account, the changes recorded and not yet ended, a
    /// local erasure left out because no mail server is ever asked for one, and the instant the oldest was committed.
    /// An account with nothing waiting is absent rather than counted at zero.
    /// </summary>
    [Fact]
    public async Task ReadWaitingChangesAsync_SeveralAccounts_CountsEachOnesUnendedChangesFromTheOldest()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var waitingAccount = MailAccountId.Create("mutation-waiting-several");
        var otherAccount = MailAccountId.Create("mutation-waiting-one");
        var quietAccount = MailAccountId.Create("mutation-waiting-none");
        var waitingBinding = await OrchestratedFolderBinding.CommitAsync(services, waitingAccount, FolderAlias, FolderAlias, cancellationToken);
        var otherBinding = await OrchestratedFolderBinding.CommitAsync(services, otherAccount, FolderAlias, FolderAlias, cancellationToken);
        await OrchestratedFolderBinding.CommitAsync(services, quietAccount, FolderAlias, FolderAlias, cancellationToken);

        var oldest = await OpenRelocationAsync(services, waitingAccount, waitingBinding, uid: 1U, cancellationToken);
        await OpenRelocationAsync(services, waitingAccount, waitingBinding, uid: 2U, cancellationToken);
        var erasure = await ErasureAsync(services, waitingAccount, waitingBinding, uid: 3U, cancellationToken);
        await OpenAsync(services, erasure, MailboxMutationLocalChange.Erasure, cancellationToken);
        var ended = await OpenRelocationAsync(services, waitingAccount, waitingBinding, uid: 4U, cancellationToken);
        await CommitOrFailAsync(
            services,
            (store, session, token) => store.AdvanceAsync(session, ended, MailboxMutationStage.Completed, placement: null, token),
            cancellationToken);
        var otherOnly = await OpenRelocationAsync(services, otherAccount, otherBinding, uid: 1U, cancellationToken);

        // Act
        var waiting = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .ReadWaitingChangesAsync([waitingAccount, otherAccount, quietAccount], token),
            cancellationToken);

        // Assert
        var oldestRecord = await ReadAsync(services, waitingAccount, oldest, cancellationToken);
        var otherRecord = await ReadAsync(services, otherAccount, otherOnly, cancellationToken);
        IReadOnlyDictionary<MailAccountId, MailboxWaitingChanges> expected = new Dictionary<MailAccountId, MailboxWaitingChanges>
        {
            [waitingAccount] = new(2, oldestRecord.RecordedAt),
            [otherAccount] = new(1, otherRecord.RecordedAt),
        };
        Assert.Equal(expected, waiting);
    }

    private static async Task<MailboxMutationRecordId> OpenRelocationAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken) =>
        await OpenAsync(services, await RelocationAsync(services, account, binding, uid, cancellationToken), cancellationToken);

    private static async Task<MailboxMutationRequest> RelocationAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken)
    {
        var occurrence = SyntheticEmail.OccurrenceIn(account, binding, uid);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            $"{account.Value}-{uid}",
            cancellationToken);

        return MailboxMutationRequest.Relocate(storedEmailId, occurrence, Requester, ArchivePath);
    }

    private static async Task<MailboxMutationRequest> ErasureAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken)
    {
        var occurrence = SyntheticEmail.OccurrenceIn(account, binding, uid);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            $"{account.Value}-{uid}",
            cancellationToken);

        return MailboxMutationRequest.Delete(
            storedEmailId,
            occurrence,
            Requester,
            AuthoredDeleteEmailDisposition.EraseLocalCopy,
            AuthoredDeleteServerDisposition.Expunge);
    }

    private static Task<MailboxMutationRecordId> OpenAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        CancellationToken cancellationToken) =>
        OpenAsync(services, request, MailboxMutationLocalChange.None, cancellationToken);

    private static Task<MailboxMutationRecordId> OpenAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        MailboxMutationLocalChange localChange,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            async (scope, session, token) => (await scope.GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(session, request, heldUntil: null, localChange, token)).Id,
            cancellationToken);

    private static Task<DateTimeOffset> RefuseAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .RecordRefusalAsync(session, recordId, MailFathomErrorCode.MailboxMutationDeclined, token),
            cancellationToken);

    private static async Task CommitOrFailAsync(
        OrchestratedMailFathomServices services,
        Func<IMailboxMutationRecordStore, IPersistenceSession, CancellationToken, Task> write,
        CancellationToken cancellationToken) =>
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => write(scope.GetRequiredService<IMailboxMutationRecordStore>(), session, token),
                cancellationToken));

    private static async Task<MailboxMutationRecord> ReadAsync(
        OrchestratedMailFathomServices services,
        MailAccountId account,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken) => Assert.Single(await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationRecordStore>().ReadAsync([account], [recordId], token),
            cancellationToken));
}
