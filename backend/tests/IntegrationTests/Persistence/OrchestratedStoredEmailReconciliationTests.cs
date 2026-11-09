// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Synchronization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the backward pass reads its window from PostgreSQL and writes both endings of a remote deletion.</summary>
/// <remarks>
/// <para>
/// Three claims no unit test reaches. The window is two bounded queries whose sizes the budget decides and whose order
/// the server produces, so what a folder actually offers is a property of the translated SQL. The tombstone is a column
/// three read models filter on and the next window filters on again, and nothing else in the suite ever sets it — every
/// one of those clauses could be deleted and the rest of the suite would stay green. And erasure is a single
/// <c>RemoveRange</c> that leans on <c>ON DELETE CASCADE</c> to take the raw MIME, the search document, and the repair
/// request with it, which is a guarantee the database gives rather than one the code makes.
/// </para>
/// <para>
/// The outcomes are stated rather than produced by a synchronization run against the mail server. What the server said
/// and how <c>MailboxReconciler</c> turns it into a window is decided without a database and is covered in the unit
/// suite; what is under test here is the effect of applying that window to real rows. Each test owns its own folder
/// alias, because a tombstone one of them writes is exactly what would change what another one finds.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredEmailReconciliationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The word every seeded body carries, so one search reaches every row a folder holds.</summary>
    private const string ReconciledTerm = "reconciled";

    /// <summary>The moment an earlier pass recorded, which every window below is ordered against.</summary>
    private static readonly DateTimeOffset EarlierObservation = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The moment the window under test reports, later than what the rows already carry.</summary>
    private static readonly DateTimeOffset LaterObservation = EarlierObservation.AddHours(1);

    /// <summary>The window reserves half of itself for mail somebody has already asked the server about.</summary>
    /// <remarks>
    /// Both halves are ordered by the server and both are capped by it, so the sequence this returns is the whole of
    /// what the reservation means: never-observed mail enters in UID order, previously observed mail enters oldest
    /// observation first, and neither group can crowd the other out of a window it has to share.
    /// </remarks>
    [Fact]
    public async Task GetReconciliationWindowAsync_AFolderMixingObservedAndNeverObservedMail_ReservesHalfOfItForTheObservedOldestFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-window", cancellationToken);

        var neverObservedUids = new uint[] { 205, 201, 203 };
        var observedUidsOldestFirst = new uint[] { 202, 204, 200 };

        await StoreEmailsAsync(services, binding, [.. neverObservedUids, .. observedUidsOldestFirst], cancellationToken);
        await ObserveAsync(services, binding, observedUidsOldestFirst, cancellationToken);

        // Act
        var wholeWindow = await ReadWindowAsync(services, binding, maxEmailCount: 4, cancellationToken);
        var halvedWindow = await ReadWindowAsync(services, binding, maxEmailCount: 2, cancellationToken);

        // Assert
        // Two of four, because three previously observed rows can fill the half reserved for them. The never-observed
        // pair is the lowest two UIDs rather than the two stored first, and the observed pair is the two oldest
        // observations rather than the two lowest UIDs — 200 carries the newest and is left behind by both orders.
        Assert.Equal([201u, 203u, 202u, 204u], wholeWindow);

        // The reserve is a share of the window rather than a fixed count, so halving the window halves both groups.
        Assert.Equal([201u, 202u], halvedWindow);
    }

    /// <summary>Applies a window that reports flags, confirms mail, and loses one message, then reads back all three.</summary>
    /// <remarks>
    /// One test rather than four, because the four claims are one window's outcome and separating them would pay for
    /// four seeded folders to assert what one application produces. The tombstone is the reason the reads are here at
    /// all: <c>StoredEmailSelectionPredicate</c>, the summary lookup, and the window query each exclude an expunged row
    /// through a clause of their own, and this is the only row in the suite that has ever been one.
    /// </remarks>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_AWindowReportingFlagsAndOneDisappearance_WritesEachOutcomeAndHidesTheTombstone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-tombstone", cancellationToken);

        const uint FlaggedUid = 300;
        const uint ConfirmedUid = 301;
        const uint DisappearedUid = 302;

        var storedEmailIds = await StoreEmailsAsync(
            services,
            binding,
            [FlaggedUid, ConfirmedUid, DisappearedUid],
            cancellationToken);

        // An earlier pass, so the flags the second window must leave alone were written by the production path rather
        // than staged into the row by the arrangement.
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                [
                    new ObservedEmailFlags(storedEmailIds[FlaggedUid], SeenAt(EarlierObservation)),
                    new ObservedEmailFlags(storedEmailIds[ConfirmedUid], FlaggedAt(EarlierObservation)),
                ],
                ConfirmedUnchanged: [],
                Disappeared: [],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                SourceIsTheTruth: true,
                EarlierObservation),
            cancellationToken);

        // Act
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                StillPresent: [],
                ConfirmedUnchanged: [storedEmailIds[ConfirmedUid]],
                Disappeared: [storedEmailIds[DisappearedUid]],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                SourceIsTheTruth: true,
                LaterObservation),
            cancellationToken);

        // Assert
        var rows = await ReadRowsAsync(services, binding, cancellationToken);

        var flagged = rows[FlaggedUid];
        Assert.True(flagged.IsMarkedSeen);
        Assert.Equal(EarlierObservation, flagged.FlagsObservedAt);
        Assert.Null(flagged.RemoteExpungeObservedAt);

        // The server said nothing changed, so the flags stay as they were read and only the queue position moves.
        var confirmed = rows[ConfirmedUid];
        Assert.True(confirmed.IsFlagged);
        Assert.False(confirmed.IsMarkedSeen);
        Assert.Equal(LaterObservation, confirmed.FlagsObservedAt);
        Assert.Null(confirmed.RemoteExpungeObservedAt);

        // The observation timestamp is written alongside the tombstone deliberately: it is what takes a row the server
        // will never answer about again out of every later window.
        var tombstoned = rows[DisappearedUid];
        Assert.Equal(LaterObservation, tombstoned.RemoteExpungeObservedAt);
        Assert.Equal(LaterObservation, tombstoned.FlagsObservedAt);

        var timeline = await ReadTimelineAsync(services, binding, cancellationToken);
        Assert.Equal(new[] { FlaggedUid, ConfirmedUid }, StoredUidsOf(timeline, rows));

        var matches = await SearchAsync(services, binding, cancellationToken);
        Assert.Equal(2, matches.Count);
        Assert.DoesNotContain(tombstoned.Id, matches.Select(match => match.Summary.StoredEmailId.Value));

        var tombstonedSummary = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailSummaryReader>()
                .FindAsync(storedEmailIds[DisappearedUid], token),
            cancellationToken);
        Assert.Null(tombstonedSummary);

        Assert.DoesNotContain(
            DisappearedUid,
            await ReadWindowAsync(services, binding, maxEmailCount: 10, cancellationToken));
    }

    /// <summary>
    /// Writes what a window reported about keywords and about <c>\Flagged</c>, then narrows a listing by each.
    /// </summary>
    /// <remarks>
    /// One test rather than two, for the reason the test above gives: both filters are one window's outcome over one
    /// seeded folder. Both halves need PostgreSQL. The keyword write crosses a <c>text[]</c> column, so a mapping that
    /// dropped an element or reordered the set would round-trip through a substitute unnoticed, and the read narrows
    /// through the containment operator the column's GIN index serves, which no in-memory selection evaluates. Each
    /// filter is read from both sides against a folder holding mail on both sides, because a comparison inverted or
    /// pinned to one value returns a plausible page when only one side is ever asked for.
    /// </remarks>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_AWindowReportingKeywordsAndStars_StoresBothAndNarrowsAListingByEach()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-keywords", cancellationToken);

        const uint LabelledUid = 500;
        const uint StarredUid = 501;
        const uint PlainUid = 502;

        var storedEmailIds = await StoreEmailsAsync(
            services,
            binding,
            [LabelledUid, StarredUid, PlainUid],
            cancellationToken);

        // Act
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                [
                    new ObservedEmailFlags(
                        storedEmailIds[LabelledUid],
                        KeywordedAt(EarlierObservation, "$Junk", "Invoices")),
                    new ObservedEmailFlags(storedEmailIds[StarredUid], FlaggedAt(EarlierObservation)),
                    new ObservedEmailFlags(storedEmailIds[PlainUid], SeenAt(EarlierObservation)),
                ],
                ConfirmedUnchanged: [],
                Disappeared: [],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                SourceIsTheTruth: true,
                EarlierObservation),
            cancellationToken);

        // Assert
        var rows = await ReadRowsAsync(services, binding, cancellationToken);

        // Upper-cased and ordered by the domain type, so the column holds the one form a filter is compared against.
        Assert.Equal(["$JUNK", "INVOICES"], rows[LabelledUid].Keywords);
        Assert.Empty(rows[StarredUid].Keywords);
        Assert.Empty(rows[PlainUid].Keywords);

        var narrowed = await ReadTimelineAsync(services, binding, cancellationToken, keyword: "$junk");
        Assert.Equal([LabelledUid], StoredUidsOf(narrowed, rows));

        // A keyword nobody reported selects nothing rather than everything, which is what a filter dropped in
        // translation would do.
        var unmatched = await ReadTimelineAsync(services, binding, cancellationToken, keyword: "Receipts");
        Assert.Empty(unmatched);

        var starred = await ReadTimelineAsync(services, binding, cancellationToken, isFlagged: true);
        Assert.Equal([StarredUid], StoredUidsOf(starred, rows));

        var unstarred = await ReadTimelineAsync(services, binding, cancellationToken, isFlagged: false);
        Assert.Equal([LabelledUid, PlainUid], [.. StoredUidsOf(unstarred, rows).Order()]);
    }

    /// <summary>Erases what the account asked to erase, and refuses to erase what a fresher pass has since seen.</summary>
    /// <remarks>
    /// The cascade is the point of running this against PostgreSQL. Nothing loads the content row, the search document,
    /// or the repair request before the removal, so if the foreign keys did not carry the delete they would be left
    /// referencing a row that no longer exists — which a substitute could not tell anyone. The spared row is asserted in
    /// the same test because it is the same code path reaching the opposite conclusion, and it is the failure that
    /// matters most: a window replayed after a commit conflict must not delete mail a later pass has proved is there.
    /// </remarks>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_UnderEraseLocalCopy_RemovesEveryCopyCascadedFromTheRowAndSparesAFresherObservation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-erasure", cancellationToken);

        const uint ErasedUid = 400;
        const uint SparedUid = 401;

        var storedEmailIds = await StoreEmailsAsync(services, binding, [ErasedUid, SparedUid], cancellationToken);
        var erasedId = storedEmailIds[ErasedUid];

        var contentCommit = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                session,
                erasedId,
                SyntheticEmail.OccurrenceIn(binding, ErasedUid),
                PlacedEmailContent.InDatabase(
                    SyntheticEmail.RawMimeOf("reconciliation-erasure", totalByteCount: 4096)),
                token),
            cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, contentCommit);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmailContentRepairRequestStore>().RecordAsync(
                    new EmailContentRepairRequest(erasedId, EmailContentDefect.HashMismatch),
                    token);

                return true;
            },
            cancellationToken);

        await ObserveAsync(services, binding, [SparedUid], cancellationToken, observedAt: LaterObservation);

        // Act
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                StillPresent: [],
                ConfirmedUnchanged: [],
                Disappeared: [erasedId, storedEmailIds[SparedUid]],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.EraseLocalCopy,
                SourceIsTheTruth: true,
                EarlierObservation),
            cancellationToken);

        // Assert
        var derivedCopyCounts = await ReadDerivedCopyCountsAsync(services, erasedId, cancellationToken);
        Assert.Equal(new DerivedCopyCounts(0, 0, 0, 0), derivedCopyCounts);

        var rows = await ReadRowsAsync(services, binding, cancellationToken);
        Assert.DoesNotContain(ErasedUid, rows.Keys);

        var spared = rows[SparedUid];
        Assert.Equal(LaterObservation, spared.FlagsObservedAt);
        Assert.Null(spared.RemoteExpungeObservedAt);
    }

    /// <summary>
    /// A window reporting two fields of a message, one of which a change committed locally first still overlays. The
    /// overlaid field keeps the value the act wrote, so the person's own act is not undone by the server not yet having
    /// heard of it, while the field no change holds follows the server — and the reading of the overlaid one is written
    /// onto the outstanding record, which is what restores it if that record ends without reaching the server.
    /// </summary>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_AWindowReportingAFieldAnOutstandingChangeOverlays_LeavesItAndRecordsTheObservation()
    {
        // Arrange
        const uint OverlaidUid = 700;

        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-overlay", cancellationToken);
        var storedEmailId = (await StoreEmailsAsync(services, binding, [OverlaidUid], cancellationToken))[OverlaidUid];

        var opened = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.SetSeen(
                storedEmailId,
                SyntheticEmail.OccurrenceIn(binding, OverlaidUid),
                MailboxMutationRequester.Command("reconciliation-overlay-seen"),
                isSeen: true),
            OverlayingMutationRecords.NothingObserved with { ObservedSeenState = false },
            cancellationToken);
        await OverlayingMutationRecords.SetStoredStateAsync(
            services,
            storedEmailId,
            row => row.IsMarkedSeen = true,
            cancellationToken);

        // Act
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                [new ObservedEmailFlags(storedEmailId, FlaggedAt(LaterObservation))],
                ConfirmedUnchanged: [],
                Disappeared: [],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                SourceIsTheTruth: true,
                LaterObservation),
            cancellationToken);

        // Assert
        var row = (await ReadRowsAsync(services, binding, cancellationToken))[OverlaidUid];
        Assert.True(row.IsMarkedSeen);
        Assert.True(row.IsFlagged);
        Assert.Equal(LaterObservation, row.FlagsObservedAt);

        var record = await OverlayingMutationRecords.ReadAsync(services, opened.Record.Id, cancellationToken);
        Assert.Equal(MailboxMutationStage.Recorded, record.Stage);
        Assert.Equal(false, record.Overlay!.ObservedSeenState);
    }

    /// <summary>
    /// A keyword the server reports while an addition of another is outstanding. The stored set holds the server's
    /// keywords and the one the act added, the record's last reading is the server's set alone, and once the record is
    /// abandoned the act's keyword leaves the stored set while the server's stays — so the stored message ends showing
    /// what the server holds rather than a change it will never be told about.
    /// </summary>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_AKeywordSetDifferingFromTheActsWhileItIsOutstanding_RestoresTheObservedSetOnceTheRecordIsAbandoned()
    {
        // Arrange
        const uint KeywordedUid = 701;

        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-overlay", cancellationToken);
        var storedEmailId = (await StoreEmailsAsync(services, binding, [KeywordedUid], cancellationToken))[KeywordedUid];

        var opened = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.AddKeywords(
                storedEmailId,
                SyntheticEmail.OccurrenceIn(binding, KeywordedUid),
                MailboxMutationRequester.Command("reconciliation-overlay-keywords"),
                AuthoredMailKeywords.Create(["act-keyword"])),
            OverlayingMutationRecords.NothingObserved with { ObservedKeywords = EmailKeywords.None },
            cancellationToken);
        await OverlayingMutationRecords.SetStoredStateAsync(
            services,
            storedEmailId,
            row => row.Keywords = ["act-keyword"],
            cancellationToken);

        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                [new ObservedEmailFlags(storedEmailId, KeywordedAt(LaterObservation, "server-keyword"))],
                ConfirmedUnchanged: [],
                Disappeared: [],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                SourceIsTheTruth: true,
                LaterObservation),
            cancellationToken);

        // The arrangement is asserted: the act's keyword is kept beside the server's, and the record has the server's set.
        var whileOutstanding = (await ReadRowsAsync(services, binding, cancellationToken))[KeywordedUid];
        Assert.Equal(["act-keyword", "server-keyword"], whileOutstanding.Keywords.Order());
        var record = await OverlayingMutationRecords.ReadAsync(services, opened.Record.Id, cancellationToken);
        Assert.Equal(["server-keyword"], record.Overlay!.ObservedKeywords!.Values);

        // Act
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>().AdvanceAsync(
                session,
                opened.Record.Id,
                MailboxMutationStage.Abandoned,
                placement: null,
                token),
            cancellationToken);

        // Assert
        var restored = (await ReadRowsAsync(services, binding, cancellationToken))[KeywordedUid];
        Assert.Equal(["server-keyword"], restored.Keywords);
    }

    /// <summary>
    /// A replacement carried as a removal and an addition holds only the keywords it changed: another client's keyword
    /// shows beside the act's while both are outstanding, and giving up the addition takes back only what it named. The
    /// row holds keywords folded and the records as they were written, which is the comparison this has to get right.
    /// </summary>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_AReplacementCarriedAsADifference_KeepsAnotherClientsKeywordAndRevertsOnlyWhatARefusedHalfNamed()
    {
        // Arrange
        const uint KeywordedUid = 702;

        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-difference", cancellationToken);
        var storedEmailId = (await StoreEmailsAsync(services, binding, [KeywordedUid], cancellationToken))[KeywordedUid];
        var occurrence = SyntheticEmail.OccurrenceIn(binding, KeywordedUid);
        var requester = MailboxMutationRequester.Command("reconciliation-difference-keywords");
        var replaced = OverlayingMutationRecords.NothingObserved with { ObservedKeywords = EmailKeywords.Create(["OLD"]) };

        await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.RemoveKeywords(storedEmailId, occurrence, requester, AuthoredMailKeywords.Create(["OLD"])),
            replaced,
            cancellationToken);
        var addition = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.AddKeywords(storedEmailId, occurrence, requester, AuthoredMailKeywords.Create(["something1"])),
            replaced,
            cancellationToken);
        await OverlayingMutationRecords.SetStoredStateAsync(
            services,
            storedEmailId,
            row => row.Keywords = ["SOMETHING1"],
            cancellationToken);

        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                [new ObservedEmailFlags(storedEmailId, KeywordedAt(LaterObservation, "old", "something2"))],
                ConfirmedUnchanged: [],
                Disappeared: [],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                SourceIsTheTruth: true,
                LaterObservation),
            cancellationToken);

        // The arrangement is asserted: the removal holds OLD off, the addition holds its own, and the other client's shows.
        var whileOutstanding = (await ReadRowsAsync(services, binding, cancellationToken))[KeywordedUid];
        Assert.Equal(["SOMETHING1", "SOMETHING2"], whileOutstanding.Keywords.Order(StringComparer.Ordinal));

        // Act
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>().AdvanceAsync(
                session,
                addition.Record.Id,
                MailboxMutationStage.Abandoned,
                placement: null,
                token),
            cancellationToken);

        // Assert
        var reverted = (await ReadRowsAsync(services, binding, cancellationToken))[KeywordedUid];
        Assert.Equal(["SOMETHING2"], reverted.Keywords);
    }

    /// <summary>
    /// A delete committed locally first hides its message from every reader at once, through a column of the row, while
    /// the row stays; withdrawing the delete clears the column and the message is listed and read again.
    /// </summary>
    [Fact]
    public async Task ReadPageAsync_AMessageWithALocallyCommittedDelete_IsHiddenWhileTheDeleteIsOutstandingAndListedOnceItIsWithdrawn()
    {
        // Arrange
        const uint KeptUid = 702;
        const uint DeletedUid = 703;

        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-local-delete", cancellationToken);
        var storedEmailIds = await StoreEmailsAsync(services, binding, [KeptUid, DeletedUid], cancellationToken);

        var opened = await OverlayingMutationRecords.OpenAsync(
            services,
            MailboxMutationRequest.Delete(
                storedEmailIds[DeletedUid],
                SyntheticEmail.OccurrenceIn(binding, DeletedUid),
                MailboxMutationRequester.Command("reconciliation-local-delete"),
                AuthoredDeleteEmailDisposition.RetainTombstone,
                AuthoredDeleteServerDisposition.Expunge),
            OverlayingMutationRecords.NothingObserved,
            cancellationToken);

        // Act
        var rowsWhileHidden = await ReadRowsAsync(services, binding, cancellationToken);
        var timelineWhileHidden = await ReadTimelineAsync(services, binding, cancellationToken);
        var summaryWhileHidden = await FindSummaryAsync(services, storedEmailIds[DeletedUid], cancellationToken);

        await services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .WithdrawAsync(session, [SyntheticMailAccount.AccountId], [opened.Record.Id], token),
            cancellationToken);

        var rowsAfterWithdrawal = await ReadRowsAsync(services, binding, cancellationToken);
        var timelineAfterWithdrawal = await ReadTimelineAsync(services, binding, cancellationToken);
        var summaryAfterWithdrawal = await FindSummaryAsync(services, storedEmailIds[DeletedUid], cancellationToken);

        // Assert
        Assert.NotNull(rowsWhileHidden[DeletedUid].LocalDeleteCommittedAt);
        Assert.Equal(new[] { KeptUid }, StoredUidsOf(timelineWhileHidden, rowsWhileHidden));
        Assert.Null(summaryWhileHidden);

        Assert.Null(rowsAfterWithdrawal[DeletedUid].LocalDeleteCommittedAt);
        Assert.Equal(new[] { KeptUid, DeletedUid }, StoredUidsOf(timelineAfterWithdrawal, rowsAfterWithdrawal));
        Assert.NotNull(summaryAfterWithdrawal);
    }

    private static Task<EmailSummary?> FindSummaryAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailSummaryReader>().FindAsync(storedEmailId, token),
            cancellationToken);

    /// <summary>A window describing a held message after a person acted on it leaves their act standing and still moves the row through the queue.</summary>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_MailFathomIsTheTruthAndTheRowWasObservedBefore_KeepsTheValueAHeldActWrote()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-held-act", cancellationToken);
        const uint HeldUid = 900;
        var storedEmailIds = await StoreEmailsAsync(services, binding, [HeldUid], cancellationToken);
        var email = storedEmailIds[HeldUid];

        await ApplyOutcomeAsync(services, HeldWindow(email, FlaggedAt(EarlierObservation), EarlierObservation), cancellationToken);
        await WriteActAsync(services, email, state => state with { IsSeen = true, IsFlagged = false }, cancellationToken);

        // Act
        await ApplyOutcomeAsync(services, HeldWindow(email, FlaggedAt(LaterObservation), LaterObservation), cancellationToken);

        // Assert
        var row = (await ReadRowsAsync(services, binding, cancellationToken))[HeldUid];
        Assert.True(row.IsMarkedSeen);
        Assert.False(row.IsFlagged);
        Assert.Equal(LaterObservation, row.FlagsObservedAt);
    }

    /// <summary>A new arrival's flags have no other writer, so its first reading is written, around whatever a person had already decided.</summary>
    /// <remarks>Read and then unread leaves the column where it began, so only the record of the act tells it from a message nobody touched.</remarks>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_FirstObservationAfterAHeldAct_WritesTheRestAndDoesNotUndoTheAct()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-held-first", cancellationToken);
        const uint HeldUid = 910;
        var storedEmailIds = await StoreEmailsAsync(services, binding, [HeldUid], cancellationToken);
        var email = storedEmailIds[HeldUid];

        await WriteActAsync(services, email, state => state with { IsSeen = true }, cancellationToken);
        await WriteActAsync(services, email, state => state with { IsSeen = false }, cancellationToken);

        var sourceReading = new RemoteEmailFlagSnapshot(
            LaterObservation,
            IsSeen: true,
            IsAnswered: true,
            IsFlagged: true,
            IsDraft: false,
            IsDeleted: true,
            EmailKeywords.None);

        // Act
        await ApplyOutcomeAsync(services, HeldWindow(email, sourceReading, LaterObservation), cancellationToken);

        // Assert
        var row = (await ReadRowsAsync(services, binding, cancellationToken))[HeldUid];
        Assert.False(row.IsMarkedSeen);
        Assert.True(row.IsFlagged);
        Assert.True(row.IsRemotelyAnswered);
        Assert.False(row.IsRemotelyDeleted);
        Assert.Equal(LaterObservation, row.FlagsObservedAt);
    }

    /// <summary>A move carried to its destination clears the observation, and the window there is still not the message's first reading.</summary>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_MailFathomIsTheTruthAndTheRowWasCarriedToANewOccurrence_KeepsWhatTheRowHeld()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-held-carried", cancellationToken);
        const uint HeldUid = 930;
        var storedEmailIds = await StoreEmailsAsync(services, binding, [HeldUid], cancellationToken);
        var email = storedEmailIds[HeldUid];

        await ApplyOutcomeAsync(services, HeldWindow(email, FlaggedAt(EarlierObservation), EarlierObservation), cancellationToken);
        await WriteActAsync(services, email, state => state with { IsFlagged = false }, cancellationToken);
        await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().TryCarryToOccurrenceAsync(
                session,
                email,
                SyntheticEmail.OccurrenceIn(binding, uid: 931),
                token),
            cancellationToken);

        // Act
        await ApplyOutcomeAsync(services, HeldWindow(email, FlaggedAt(LaterObservation), LaterObservation), cancellationToken);

        // Assert
        var row = await ReadRowByIdAsync(services, email, cancellationToken);
        Assert.NotNull(row);
        Assert.False(row.IsFlagged);
        Assert.Equal(LaterObservation, row.FlagsObservedAt);
    }

    /// <summary>A message gone from a held account's source keeps its row, unless its content was never stored and MailFathom therefore never held it.</summary>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_MailFathomIsTheTruthAndMessagesDisappear_ClearsAHeldRowAndErasesOneWithNoContent()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-held-gone", cancellationToken);
        const uint StoredUid = 920;
        var stored = (await StoreEmailsAsync(services, binding, [StoredUid], cancellationToken))[StoredUid];
        var neverHeld = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, uid: 921),
            "never-held",
            cancellationToken);

        // Act
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                StillPresent: [],
                ConfirmedUnchanged: [],
                Disappeared: [stored, neverHeld],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.EraseLocalCopy,
                SourceIsTheTruth: false,
                LaterObservation),
            cancellationToken);

        // Assert
        var keptRow = await ReadRowByIdAsync(services, stored, cancellationToken);
        Assert.NotNull(keptRow);
        Assert.Null(keptRow.Uid);
        Assert.Null(keptRow.UidValidity);
        Assert.Null(await ReadRowByIdAsync(services, neverHeld, cancellationToken));
    }

    private static ReconciledFolderOutcome HeldWindow(
        StoredEmailId email,
        RemoteEmailFlagSnapshot snapshot,
        DateTimeOffset observedAt) => new(
            [new ObservedEmailFlags(email, snapshot)],
            ConfirmedUnchanged: [],
            Disappeared: [],
            RemovedByOwnMutation: [],
            RemotelyDeletedEmailDisposition.RetainTombstone,
            SourceIsTheTruth: false,
            observedAt);

    /// <summary>Commits an act on a stored message the way a held account's local change does.</summary>
    private static async Task WriteActAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        Func<LocalEmailState, LocalEmailState> act,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var states = scope.GetRequiredService<ILocalEmailStateStore>();
                var state = await states.ReadAsync(session, SyntheticMailAccount.Account, email, token);

                Assert.NotNull(state);
                await states.WriteAsync(session, SyntheticMailAccount.Account, email, act(state), isHeldFolder: true, token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static Task<StoredEmailEntity?> ReadRowByIdAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var id = email.Value;

        return services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == id, token),
            cancellationToken);
    }

    private static RemoteEmailFlagSnapshot SeenAt(DateTimeOffset observedAt) => new(
        observedAt,
        IsSeen: true,
        IsAnswered: false,
        IsFlagged: false,
        IsDraft: false,
        IsDeleted: false,
        Keywords: EmailKeywords.None);

    private static RemoteEmailFlagSnapshot FlaggedAt(DateTimeOffset observedAt) => new(
        observedAt,
        IsSeen: false,
        IsAnswered: false,
        IsFlagged: true,
        IsDraft: false,
        IsDeleted: false,
        Keywords: EmailKeywords.None);

    private static RemoteEmailFlagSnapshot KeywordedAt(DateTimeOffset observedAt, params string[] keywords) => new(
        observedAt,
        IsSeen: false,
        IsAnswered: false,
        IsFlagged: false,
        IsDraft: false,
        IsDeleted: false,
        EmailKeywords.Create(keywords));

    /// <summary>Writes one email per UID through the production upsert, indexed so a test can name a row by its UID.</summary>
    private static async Task<IReadOnlyDictionary<uint, StoredEmailId>> StoreEmailsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        IReadOnlyList<uint> uids,
        CancellationToken cancellationToken)
    {
        var storedEmailIds = new Dictionary<uint, StoredEmailId>();

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var uid in uids)
                {
                    var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
                    var subject = $"reconciled-{uid}";

                    storedEmailIds[uid] = await repository.UpsertMetadataAsync(
                        session, SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                        SyntheticEmail.ExtractionOf(
                            occurrenceId,
                            subject,
                            SyntheticEmail.BodyTextContaining(ReconciledTerm, wordCount: 10),
                            "recipient@mailfathom.test"),
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        await OrchestratedSettlement.ApplyAsync(
            services,
            storedEmailIds.Values,
            SyntheticEmail.ReceivedAt,
            cancellationToken);

        return storedEmailIds;
    }

    /// <summary>Records an observation against each named UID, one window per UID so their timestamps are ordered.</summary>
    /// <remarks>
    /// Separate windows rather than one, because the queue order this arranges is the order of the observations
    /// themselves: a single window would write one timestamp to every row it named and leave the ordering the
    /// reservation is read through undefined.
    /// </remarks>
    private static async Task ObserveAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        IReadOnlyList<uint> uidsOldestFirst,
        CancellationToken cancellationToken,
        DateTimeOffset? observedAt = null)
    {
        var rows = await ReadRowsAsync(services, binding, cancellationToken);
        var firstObservation = observedAt ?? EarlierObservation;

        foreach (var (index, uid) in uidsOldestFirst.Index())
        {
            var recordedAt = firstObservation.AddMinutes(index);

            await ApplyOutcomeAsync(
                services,
                new ReconciledFolderOutcome(
                    [new ObservedEmailFlags(StoredEmailId.Create(rows[uid].Id), SeenAt(recordedAt))],
                    ConfirmedUnchanged: [],
                    Disappeared: [],
                    RemovedByOwnMutation: [],
                    RemotelyDeletedEmailDisposition.RetainTombstone,
                    SourceIsTheTruth: true,
                    recordedAt),
                cancellationToken);
        }
    }

    private static async Task ApplyOutcomeAsync(
        OrchestratedMailFathomServices services,
        ReconciledFolderOutcome outcome,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailReconciliationStore>()
                .ApplyReconciliationOutcomeAsync(session, outcome, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static async Task<IReadOnlyList<uint>> ReadWindowAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        var window = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailReconciliationStore>()
                .GetReconciliationWindowAsync(
                    SyntheticMailAccount.Account,
                    binding.Id,
                    ImapUidValidity.Create(SyntheticEmail.UidValidity),
                    maxEmailCount,
                    token),
            cancellationToken);

        return [.. window.Select(awaiting => awaiting.Uid.Value)];
    }

    private static Task<IReadOnlyList<EmailSummary>> ReadTimelineAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken,
        string? keyword = null,
        bool? isFlagged = null) => services.AsCallerInScopeAsync(
            async (scope, token) => await scope.GetRequiredService<IStoredEmailTimelineReader>().ReadPageAsync(
                EmailTimelineFilter.Create(
                    await ReadScopeAsync(scope, binding, token),
                    senderAddress: null,
                    recipientAddress: null,
                    subjectFragment: null,
                    receivedOnOrAfter: null,
                    receivedBefore: null,
                    isMarkedSeen: null,
                    isFlagged,
                    keyword,
                    hasAttachments: null,
                    EmailTimelineDirection.NewestFirst),
                continueAfter: null,
                limit: 50,
                token),
            [],
            cancellationToken);

    private static Task<IReadOnlyList<EmailSearchMatch>> SearchAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken) => services.AsCallerInScopeAsync(
            async (scope, token) =>
            {
                var reader = scope.GetRequiredService<IEmailSearchIndexReader>();
                var selection = MailboxEmailSelection.Create(
                    await ReadScopeAsync(scope, binding, token),
                    senderAddress: null,
                    recipientAddress: null,
                    subjectFragment: null,
                    receivedOnOrAfter: null,
                    receivedBefore: null,
                    isMarkedSeen: null,
                    isFlagged: null,
                    keyword: null,
                    hasAttachments: null);
                var queryText = EmailSearchQueryText.Create(ReconciledTerm);

                var candidates = await reader.ReadRankedCandidatesAsync(selection, queryText, limit: 50, token);

                return await reader.ReadMatchesAsync(
                    selection,
                    queryText,
                    EmailSearchSnippetBounds.Default,
                    candidates,
                    token);
            },
            [],
            cancellationToken);

    private static Task<MailboxScope> ReadScopeAsync(
        IServiceProvider scope,
        MailFolderResolution binding,
        CancellationToken cancellationToken) =>
        OrchestratedMailboxScope.ReadableAsync(scope, [binding.Alias.Value], cancellationToken);

    /// <summary>Reads every row one folder holds, keyed by UID so an assertion names a message rather than an identifier.</summary>
    private static async Task<IReadOnlyDictionary<uint, StoredEmailEntity>> ReadRowsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        var rows = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(email => email.MailFolderBinding.Alias == alias && email.MailFolderBinding.ResolutionGeneration == generation)
                .ToArrayAsync(token),
            cancellationToken);

        return rows.ToDictionary(row => row.Uid!.Value);
    }

    /// <summary>Counts what the database should have removed along with one stored email.</summary>
    private static Task<DerivedCopyCounts> ReadDerivedCopyCountsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var id = storedEmailId.Value;

                return new DerivedCopyCounts(
                    await dbContext.StoredEmails.AsNoTracking().CountAsync(email => email.Id == id, token),
                    await dbContext.EmailMessageContents.AsNoTracking()
                        .CountAsync(content => content.StoredEmailId == id, token),
                    await dbContext.EmailSearchDocuments.AsNoTracking()
                        .CountAsync(document => document.StoredEmailId == id, token),
                    await dbContext.EmailContentRepairRequests.AsNoTracking()
                        .CountAsync(request => request.StoredEmailId == id, token));
            },
            cancellationToken);

    private static IEnumerable<uint> StoredUidsOf(
        IReadOnlyList<EmailSummary> summaries,
        IReadOnlyDictionary<uint, StoredEmailEntity> rows)
    {
        var uidsById = rows.ToDictionary(row => row.Value.Id, row => row.Key);

        return summaries.Select(summary => uidsById[summary.StoredEmailId.Value]).Order();
    }

    /// <summary>The four rows one erased email is reachable from, so a missing cascade names which one survived.</summary>
    private sealed record DerivedCopyCounts(
        int StoredEmails,
        int MessageContents,
        int SearchDocuments,
        int RepairRequests);
}
