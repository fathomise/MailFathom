// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Settlement;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the settling queue and the settlement write against real PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Every read a person or an agent makes admits settled mail alone, so what this store answers decides whether an
/// arriving message is ever shown. Both halves fail silently: a read of the queue that translated its probe of the
/// mutation records wrongly would hold a message back until the wait bound released it, or show it in the folder it is
/// about to leave; and a settlement whose condition did not hold in the statement would report a message settled twice,
/// which is an arrival announced twice. Neither is observable against a substitute, which answers whatever it was handed.
/// </para>
/// <para>
/// The queue is the account's rather than this class's folder's, and every other class in the collection leaves
/// unsettled mail on it, so a message is looked for by its identity across the whole walk rather than expected at the head.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailSettlementStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "mail-settlement";

    private const string DestinationFolderName = "MailSettlementArchive";

    private const uint SettledUid = 6101;

    private const uint RelocatedUid = 6201;

    private const uint StillSettlingUid = 6111;

    private const uint SettledOnArrivalUid = 6112;

    private const uint FiledFromOutgoingUid = 6113;

    private const long FiledSizeOctets = 2048;

    /// <summary>How many settling messages one read of the queue asks for while a test walks it for its own message.</summary>
    private const int QueueBatchSize = 500;

    /// <summary>The UIDVALIDITY the destination folder reported when it answered the copy.</summary>
    private static readonly ImapUidValidity PlacementUidValidity = ImapUidValidity.Create(4_200_000_011);

    /// <summary>The UID the destination folder assigned the copy.</summary>
    private static readonly ImapUid PlacementUid = ImapUid.Create(6301);

    private static readonly RemoteFolderPath DestinationPath =
        RemoteFolderPath.Create(DestinationFolderName, hierarchyDelimiter: '.');

    /// <summary>The instant written as the settlement, stated so the write is the same on every run.</summary>
    private static readonly DateTimeOffset SettledAt = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The instant a run writes down that it recognized the relocated copy.</summary>
    private static readonly DateTimeOffset ObservedAt = new(2026, 5, 4, 9, 5, 0, TimeSpan.Zero);

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("file-settling-arrival", "1");

    /// <summary>
    /// A message stored by synchronization waits on the queue and is withheld from a read by its identity; settling it
    /// takes it off the queue and admits it to that same read, which is the control the earlier absence needs. Settling
    /// it a second time reports nothing, because the condition is the statement's own rather than a read before it.
    /// </summary>
    [Fact]
    public async Task SettleAsync_AStoredArrival_MovesItFromTheQueueToTheReadsAndSettlesItOnce()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, SettledUid),
            "settling arrival",
            cancellationToken);

        var settlingBefore = await FindSettlingAsync(services, storedEmailId, cancellationToken);
        var readableBefore = await FindSummaryAsync(services, storedEmailId, cancellationToken);

        // Act
        var firstSettlement = await SettleAsync(services, storedEmailId, cancellationToken);
        var secondSettlement = await SettleAsync(services, storedEmailId, cancellationToken);

        var settlingAfter = await FindSettlingAsync(services, storedEmailId, cancellationToken);
        var readableAfter = await FindSummaryAsync(services, storedEmailId, cancellationToken);

        // Assert
        Assert.NotNull(settlingBefore);
        Assert.Equal(SyntheticMailAccount.Account, settlingBefore.AccountId);
        Assert.Equal(MailFolderAlias.Create(FolderAlias), settlingBefore.FolderAlias);
        Assert.Equal(StoredEmailContentAvailability.ExceededSizeLimit, settlingBefore.ContentAvailability);
        Assert.Null(settlingBefore.Verdict);
        Assert.False(settlingBefore.AreRulesFinished);
        Assert.False(settlingBefore.IsTombstoned);
        Assert.False(settlingBefore.AwaitsPlacement);
        Assert.Null(readableBefore);

        Assert.Equal(storedEmailId, Assert.Single(firstSettlement));
        Assert.Empty(secondSettlement);

        Assert.Null(settlingAfter);
        Assert.NotNull(readableAfter);
        Assert.Equal(storedEmailId, readableAfter.StoredEmailId);
    }

    /// <summary>
    /// Four messages are stored already settled, because none of them is an arrival: a rule's copy and a message stored
    /// again after a delete was undone, which share one stamp; the person's own sent message joined to its outgoing
    /// record; and the two a deployment writes with no occurrence, a filed draft and a held account's local copy. Each is
    /// readable at once and never on the queue, where it would otherwise be settled and announced as news. A message
    /// stored beside them without a stamp is the control that the same reads would report a missing one.
    /// </summary>
    [Fact]
    public async Task StoreAsync_MessagesThatAreNoArrival_AreStoredSettledWhileAnArrivalBesideThemWaits()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);

        // Act
        var stillSettling = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, StillSettlingUid),
            "arrival beside the settled ones",
            cancellationToken);
        var settledOnArrival = await StoreAndStampAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, SettledOnArrivalUid),
            "copy a rule made",
            (repository, session, storedEmailId, token) =>
                repository.RecordSettledOnArrivalAsync(session, storedEmailId, token),
            cancellationToken);
        var filedFromOutgoing = await StoreAndStampAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, FiledFromOutgoingUid),
            "the person's own sent message",
            (repository, session, storedEmailId, token) => repository.RecordFiledFromOutgoingAsync(
                session,
                storedEmailId,
                OutgoingEmailId.Create(Guid.CreateVersion7()),
                token),
            cancellationToken);
        var filedDraft = await services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().StoreFiledEmailAsync(
                session,
                SyntheticMailAccount.Account,
                binding.Id,
                extractedMetadata: null,
                FiledSizeOctets,
                AppendedMailFlags.Draft,
                filedFrom: null,
                token),
            cancellationToken);
        var localCopy = await services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().StoreLocalCopyAsync(
                session,
                SyntheticMailAccount.Account,
                binding.Id,
                extractedMetadata: null,
                FiledSizeOctets,
                new CopiedMailFlags(IsSeen: true, IsFlagged: false, RemoteEmailKeywords.None),
                token),
            cancellationToken);

        StoredEmailId[] storedSettled =
            [settledOnArrival, filedFromOutgoing, Assert.NotNull(filedDraft), Assert.NotNull(localCopy)];
        var settlingAfterStore = await Task.WhenAll(
            storedSettled.Select(storedEmailId => FindSettlingAsync(services, storedEmailId, cancellationToken)));
        var readableAfterStore = await Task.WhenAll(
            storedSettled.Select(storedEmailId => FindSummaryAsync(services, storedEmailId, cancellationToken)));

        // Assert
        Assert.NotNull(await FindSettlingAsync(services, stillSettling, cancellationToken));
        Assert.Null(await FindSummaryAsync(services, stillSettling, cancellationToken));

        Assert.All(settlingAfterStore, Assert.Null);
        Assert.All(readableAfterStore, Assert.NotNull);
        Assert.Equal(storedSettled, readableAfterStore.Select(summary => summary!.StoredEmailId));
    }

    /// <summary>
    /// A message a rule is moving waits until the move has reached its row, and not merely until the server finished it:
    /// a completed relocation still leaves the row in the folder it is leaving until synchronization recognizes the copy,
    /// so the probe has to read the observation rather than the stage. Writing the observation down is what releases it.
    /// </summary>
    [Fact]
    public async Task ReadSettlingAsync_AnArrivalARuleIsRelocating_AwaitsPlacementUntilThePlacementIsObserved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(binding, RelocatedUid);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            "relocated arrival",
            cancellationToken);
        var recordId = await services.CommitProducingAsync(
            async (scope, session, token) => (await scope.GetRequiredService<IMailboxMutationRecordStore>().OpenAsync(
                session,
                MailboxMutationRequest.Relocate(storedEmailId, occurrence, Requester, DestinationPath),
                heldUntil: null,
                MailboxMutationLocalChange.None,
                token)).Id,
            cancellationToken);

        // Act
        var whileRecorded = await FindSettlingAsync(services, storedEmailId, cancellationToken);

        await CompleteRelocationAsync(services, recordId, cancellationToken);
        var whileCompleted = await FindSettlingAsync(services, storedEmailId, cancellationToken);

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IMailboxMutationReconciliationStore>()
                    .RecordPlacementObservedAsync(session, recordId, ObservedAt, token),
                cancellationToken));
        var onceObserved = await FindSettlingAsync(services, storedEmailId, cancellationToken);

        // Assert
        Assert.True(whileRecorded?.AwaitsPlacement);
        Assert.True(whileCompleted?.AwaitsPlacement);
        Assert.False(onceObserved?.AwaitsPlacement);
    }

    /// <summary>Walks the account's settling queue in the batches a pass reads it in, until the message turns up or the queue ends.</summary>
    private static Task<SettlingEmail?> FindSettlingAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var store = scope.GetRequiredService<IMailSettlementStore>();
                StoredEmailId? resumeAfter = null;

                while (true)
                {
                    var batch = await store.ReadSettlingAsync(
                        SyntheticMailAccount.Account,
                        resumeAfter,
                        QueueBatchSize,
                        token);

                    if (batch.Count == 0)
                    {
                        return null;
                    }

                    var found = batch.FirstOrDefault(settling => settling.Id == storedEmailId);

                    if (found is not null)
                    {
                        return found;
                    }

                    resumeAfter = batch[^1].Id;
                }
            },
            cancellationToken);

    /// <summary>Stores an occurrence and writes a stamp on it in the same session, as synchronization does.</summary>
    private static Task<StoredEmailId> StoreAndStampAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrence,
        string subject,
        Func<IEmailMetadataRepository, IPersistenceSession, StoredEmailId, CancellationToken, Task> stamp,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();
                var storedEmailId = await repository.UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrence, subject),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.ExceededSizeLimit,
                    token);

                await stamp(repository, session, storedEmailId, token);

                return storedEmailId;
            },
            cancellationToken);

    private static Task<EmailSummary?> FindSummaryAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailSummaryReader>().FindAsync(storedEmailId, token),
            cancellationToken);

    private static Task<IReadOnlyList<StoredEmailId>> SettleAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailSettlementStore>().SettleAsync([storedEmailId], SettledAt, token),
            cancellationToken);

    /// <summary>Drives a relocation to the stage a finished move leaves, through the production store.</summary>
    /// <remarks>
    /// Every stage is written by the store rather than onto the row, so the record the queue's probe meets is the one a
    /// performer produces, and completion is the only stage an observation is ever written from.
    /// </remarks>
    private static async Task CompleteRelocationAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken) => Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (scope, session, token) =>
                {
                    var store = scope.GetRequiredService<IMailboxMutationRecordStore>();

                    await store.RecordPlacementIssuedAsync(session, recordId, requiresSourceRemoval: true, token);
                    await store.AdvanceAsync(
                        session,
                        recordId,
                        MailboxMutationStage.PlacementConfirmed,
                        RemoteEmailPlacement.Reported(PlacementUidValidity, PlacementUid),
                        token);
                    await store.AdvanceAsync(session, recordId, MailboxMutationStage.Completed, placement: null, token);
                },
                cancellationToken));
}
