// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Proves the order changes to one message reach the server in, and which of them a newer change replaces, against real PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The outstanding read decides the order in one query: a correlated subquery over the same table, compared on the
/// instant each change was recorded, the stage the earlier one stands at, and the mutation each of the two is. A
/// substitute would be written to agree with it, so what is asserted here is the translated SQL and the rows it ranks.
/// </para>
/// <para>
/// Supersession is the store's own write in the transaction that records the newer change, so it is proved through the
/// store and read back through the batched reader. Every test owns a UID of its own, because a change recorded against
/// one message is what decides whether another waits.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxMutationOrderingTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "mutation-ordering";

    private static readonly RemoteFolderPath FirstArchive = RemoteFolderPath.Create("OrderingFirstArchive", hierarchyDelimiter: '.');

    private static readonly RemoteFolderPath SecondArchive = RemoteFolderPath.Create("OrderingSecondArchive", hierarchyDelimiter: '.');

    /// <summary>
    /// A move waits for whatever was asked before it, and anything asked after a move waits for the move, because the
    /// move changes the occurrence every later command names. Another message's change is not held back by either.
    /// </summary>
    [Fact]
    public async Task ReadOutstandingAsync_AMoveAfterAnotherChangeToTheSameMessage_HoldsBackTheMoveAndEverythingAfterItWhileOtherMessagesConverge()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var (message, occurrence) = await StoreAsync(services, binding, uid: 8001U, cancellationToken);
        var (otherMessage, otherOccurrence) = await StoreAsync(services, binding, uid: 8002U, cancellationToken);

        var requester = MailboxMutationRequester.Command("ordering-move");
        var seen = await OpenAsync(services, MailboxMutationRequest.SetSeen(message, occurrence, requester, isSeen: true), cancellationToken);
        var move = await OpenAsync(services, MailboxMutationRequest.Relocate(message, occurrence, requester, FirstArchive), cancellationToken);
        var flagged = await OpenAsync(services, MailboxMutationRequest.SetFlagged(message, occurrence, requester, isFlagged: true), cancellationToken);
        var other = await OpenAsync(services, MailboxMutationRequest.SetSeen(otherMessage, otherOccurrence, requester, isSeen: true), cancellationToken);
        MailboxMutationRecordId[] opened = [seen, move, flagged, other];

        // Act
        var beforeTheFirstCompletes = await OverlayingMutationRecords.ReadEligibleAsync(services, opened, cancellationToken);
        await AdvanceAsync(services, seen, MailboxMutationStage.Completed, cancellationToken);
        var afterTheFirstCompletes = await OverlayingMutationRecords.ReadEligibleAsync(services, opened, cancellationToken);

        // Assert
        Assert.Equal([seen, other], beforeTheFirstCompletes);
        Assert.Equal([move, other], afterTheFirstCompletes);
    }

    /// <summary>
    /// Two moves of one message where the first is being retried. The server has declined the first twice and it sits at
    /// the stage the server was asked at, and the second does not overtake it until the first has completed or been given
    /// up on.
    /// </summary>
    [Theory]
    [InlineData(MailboxMutationStage.Completed)]
    [InlineData(MailboxMutationStage.Abandoned)]
    public async Task ReadOutstandingAsync_TwoMovesOfOneMessageWhereTheFirstIsRetried_ReleasesTheSecondOnlyOnceTheFirstHasEnded(
        MailboxMutationStage endingOfTheFirst)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var (message, occurrence) = await StoreAsync(services, binding, 8100U + (uint)endingOfTheFirst, cancellationToken);

        var first = await OpenAsync(
            services,
            MailboxMutationRequest.Relocate(message, occurrence, MailboxMutationRequester.Command("ordering-first-move"), FirstArchive),
            cancellationToken);
        var second = await OpenAsync(
            services,
            MailboxMutationRequest.Relocate(message, occurrence, MailboxMutationRequester.Command("ordering-second-move"), SecondArchive),
            cancellationToken);

        await services.CommitAsync(
            async (scope, session, token) =>
            {
                var store = scope.GetRequiredService<IMailboxMutationRecordStore>();

                await store.RecordRefusalAsync(session, first, MailFathomErrorCode.MailboxMutationDeclined, token);
                await store.RecordRefusalAsync(session, first, MailFathomErrorCode.MailboxMutationDeclined, token);
                await store.RecordPlacementIssuedAsync(session, first, requiresSourceRemoval: false, token);
            },
            cancellationToken);

        // Act
        var whileTheFirstIsRetried = await OverlayingMutationRecords.ReadEligibleAsync(services, [first, second], cancellationToken);
        await AdvanceAsync(services, first, endingOfTheFirst, cancellationToken);
        var onceTheFirstHasEnded = await OverlayingMutationRecords.ReadEligibleAsync(services, [first, second], cancellationToken);

        // Assert
        Assert.Equal([first], whileTheFirstIsRetried);
        Assert.Contains(second, onceTheFirstHasEnded);
    }

    /// <summary>
    /// A newer change to a field replaces an older one nothing has been asked of the server for, and carries the older
    /// one's last observation of the field forward, so abandoning the newer one returns the value the server holds
    /// rather than the value the withdrawn change wrote.
    /// </summary>
    [Fact]
    public async Task OpenOverlayAsync_ANewerChangeToAFieldWhileTheOlderIsStillRecorded_WithdrawsTheOlderAndCarriesItsObservationForward()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var (message, occurrence) = await StoreAsync(services, binding, uid: 8201U, cancellationToken);

        var older = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.SetSeen(message, occurrence, MailboxMutationRequester.Command("ordering-older-seen"), isSeen: true),
            OverlayingMutationRecords.NothingObserved with { ObservedSeenState = false },
            cancellationToken);

        // Act
        // The stored value after the older act is what the caller hands over as the replaced one, and it is not what the
        // server holds: the store has to take the older record's reading instead.
        var newer = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.SetSeen(message, occurrence, MailboxMutationRequester.Command("ordering-newer-seen"), isSeen: false),
            OverlayingMutationRecords.NothingObserved with { ObservedSeenState = true },
            cancellationToken);

        // Assert
        Assert.True(newer.IsNew);
        Assert.Equal(MailboxMutationStage.Cancelled, (await OverlayingMutationRecords.ReadAsync(services, older.Record.Id, cancellationToken)).Stage);

        var newerRecord = await OverlayingMutationRecords.ReadAsync(services, newer.Record.Id, cancellationToken);
        Assert.Equal(MailboxMutationStage.Recorded, newerRecord.Stage);
        Assert.Equal(false, newerRecord.Overlay!.ObservedSeenState);
        Assert.Equal(
            [newer.Record.Id],
            await OverlayingMutationRecords.ReadEligibleAsync(services, [older.Record.Id, newer.Record.Id], cancellationToken));
    }

    /// <summary>
    /// A change the server has already been asked for is not withdrawn by a newer one to the same field. The newer one
    /// is recorded, takes the reading the older one holds, and waits behind it so the two reach the server in order.
    /// </summary>
    [Fact]
    public async Task OpenOverlayAsync_ANewerChangeToAFieldWhileTheOlderIsAlreadyIssued_LeavesTheOlderAndWaitsBehindIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var (message, occurrence) = await StoreAsync(services, binding, uid: 8202U, cancellationToken);

        var older = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.SetSeen(message, occurrence, MailboxMutationRequester.Command("ordering-issued-seen"), isSeen: true),
            OverlayingMutationRecords.NothingObserved with { ObservedSeenState = false },
            cancellationToken);
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .RecordPlacementIssuedAsync(session, older.Record.Id, requiresSourceRemoval: false, token),
            cancellationToken);

        // Act
        var newer = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.SetSeen(message, occurrence, MailboxMutationRequester.Command("ordering-waiting-seen"), isSeen: false),
            OverlayingMutationRecords.NothingObserved with { ObservedSeenState = true },
            cancellationToken);

        // Assert
        Assert.Equal(MailboxMutationStage.PlacementIssued, (await OverlayingMutationRecords.ReadAsync(services, older.Record.Id, cancellationToken)).Stage);

        var newerRecord = await OverlayingMutationRecords.ReadAsync(services, newer.Record.Id, cancellationToken);
        Assert.Equal(MailboxMutationStage.Recorded, newerRecord.Stage);
        Assert.Equal(false, newerRecord.Overlay!.ObservedSeenState);
        Assert.Equal(
            [older.Record.Id],
            await OverlayingMutationRecords.ReadEligibleAsync(services, [older.Record.Id, newer.Record.Id], cancellationToken));
    }

    private static async Task<(StoredEmailId Message, EmailOccurrenceId Occurrence)> StoreAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        CancellationToken cancellationToken)
    {
        var occurrence = SyntheticEmail.OccurrenceIn(binding, uid);

        return (await StoredSyntheticEmail.MetadataOnlyAsync(services, occurrence, $"ordering-{uid}", cancellationToken), occurrence);
    }

    private static Task<MailboxMutationRecordId> OpenAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            async (scope, session, token) => (await scope.GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(session, request, heldUntil: null, MailboxMutationLocalChange.None, token)).Id,
            cancellationToken);

    private static Task<PersistenceCommitResult> AdvanceAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRecordId recordId,
        MailboxMutationStage stage,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .AdvanceAsync(session, recordId, stage, placement: null, token),
            cancellationToken);
}
