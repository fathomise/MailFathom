// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves the sweep over pre-existing mail against a real schema: which messages it selects, that it cuts one with no
/// passages before embedding it, and that the position it commits is what a later run resumes past.
/// </summary>
/// <remarks>
/// None of it is reachable without a real server. What the walk selects is a disjunction of two correlated
/// <c>NOT EXISTS</c> forms over the passage and vector tables plus a keyset comparison PostgreSQL evaluates under its
/// own <c>uuid</c> ordering, and a substitute for the database would report whatever the test told it to whatever the
/// translation actually produced. The vectors come from the deterministic generator, so the whole path costs no
/// provider call.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailEmbeddingBackfillTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "embedding-backfill";

    /// <summary>A window long enough that a passage cut by this run is inside it and one dated to the synthetic send is not.</summary>
    private static readonly TimeSpan LiveEmbeddingWindow = TimeSpan.FromMinutes(10);

    /// <summary>Bounds the sweep loop. A run that has not finished by then is a defect rather than a slow database.</summary>
    private const int MaximumRunsPerSweep = 200;

    /// <summary>The rule set before the one this build cuts to, which is what a boundary-rule change leaves passages on.</summary>
    private static readonly int EarlierRuleSetVersion = EmailChunkingRules.Current.RuleSetVersion - 1;

    /// <summary>The rule set after the one this build cuts to, which is what a newer build of a rolling upgrade writes.</summary>
    private static readonly int LaterRuleSetVersion = EmailChunkingRules.Current.RuleSetVersion + 1;

    /// <summary>
    /// The whole walk over a real schema. A message stored before chunking existed carries extracted text and no
    /// passages, and this is what gives it both — in that order, because the vectors it ends up with can only have come
    /// from passages this run cut. A message that was already current is not selected again, which is what makes a
    /// repeated sweep cost queries rather than a provider bill.
    /// </summary>
    [Fact]
    public async Task Sweeping_MailStoredBeforeTheProfileExisted_CutsWhatHasNoPassagesAndEmbedsEverythingOutstanding()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var storedBeforeChunking = await StoreOneMessageAsync(services, uid: 9301, cancellationToken);
        var storedBeforeTheProfile = await StoreOneMessageAsync(services, uid: 9302, cancellationToken);

        // The two rows the sweep's two groups are: the first is what storing leaves behind — text, a search document,
        // and no passages — and the second is a message an account run already cut, outstanding only for its vectors.
        await OrchestratedPassages.CutAsync(services, storedBeforeTheProfile, cancellationToken);

        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        var sweep = await SweepAsync(services, cancellationToken);
        var repeat = await RunOnceAsync(services, cancellationToken);

        // Assert
        Assert.True(sweep.ChunkedEmailCount > 0);

        // Counted rather than compared alone: the message the sweep had to cut ends with passages and a vector for each
        // of them, and two equal zeroes would report a sweep that never reached it as one that finished it.
        var cutPassageCount = await CountPassagesAsync(services, storedBeforeChunking, cancellationToken);
        Assert.True(cutPassageCount > 0);
        Assert.Equal(
            cutPassageCount,
            await CountVectorsAsync(services, storedBeforeChunking, profileId, cancellationToken));
        Assert.Equal(
            await CountPassagesAsync(services, storedBeforeTheProfile, cancellationToken),
            await CountVectorsAsync(services, storedBeforeTheProfile, profileId, cancellationToken));

        // A sweep that has just finished has nothing in front of it, and the message it cut is not cut again.
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, repeat.Outcome);
        Assert.Equal(0, repeat.ChunkedEmailCount);
        Assert.Equal(0, repeat.EmbeddedChunkCount);
    }

    /// <summary>
    /// A boundary-rule change reaches an attachment's passages: the ones cut to an earlier rule set are cut again from the
    /// words the reading stored and embedded anew, while the message's body and its other attachment keep every passage
    /// and every vector they had — which is what reconciling per attachment buys over replacing a message whole.
    /// </summary>
    /// <remarks>
    /// The earlier cut is simulated rather than reproduced, since one build carries one rule set: the passages of the
    /// first attachment are given the previous version and a digest no current rule produces, which is the state a rule
    /// change leaves them in, because the version is part of every digest. The reading marker is read before and after,
    /// because a re-cut that reopened the file would be one that took the attachment stage's work over.
    /// </remarks>
    [Fact]
    public async Task Sweeping_AttachmentPassagesCutToEarlierRules_CutsThemAgainFromTheStoredReadingAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var message = await StoreOneMessageAsync(services, uid: 9361, cancellationToken);
        await OrchestratedPassages.CutAsync(services, message, cancellationToken);
        await SaveAttachmentReadingsAsync(services, message, [Document(0, "contract"), Document(1, "invoice")], cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);
        await SweepAsync(services, cancellationToken);

        var untouched = await ReadUntouchedPassageIdsAsync(services, message, cancellationToken);
        var cutToEarlierRules = await MarkCutToRulesAsync(
            services,
            message,
            attachmentPosition: 0,
            EarlierRuleSetVersion,
            cancellationToken);
        var readAt = await ReadAttachmentTextDerivedAtAsync(services, message, cancellationToken);

        // Act
        var sweep = await SweepAsync(services, cancellationToken);

        // Assert
        Assert.True(cutToEarlierRules.Count > 0);
        var recut = await ReadPassageIdsAsync(services, message, attachmentPosition: 0, cancellationToken);
        Assert.Equal(cutToEarlierRules.Count, recut.Count);
        Assert.Empty(recut.Intersect(cutToEarlierRules));

        // Only the passages that moved were paid for, and every passage the message holds now carries a vector.
        Assert.Equal(recut.Count, sweep.EmbeddedChunkCount);
        Assert.Equal(untouched, await ReadUntouchedPassageIdsAsync(services, message, cancellationToken));
        Assert.Equal(
            await CountPassagesAsync(services, message, cancellationToken),
            await CountVectorsAsync(services, message, profileId, cancellationToken));
        Assert.Equal(readAt, await ReadAttachmentTextDerivedAtAsync(services, message, cancellationToken));
    }

    /// <summary>
    /// The same selection reaches a message's body: its passages cut to an earlier rule set are cut again from the stored
    /// extraction and embedded anew, while the attachment beside it keeps every passage and vector it had.
    /// </summary>
    [Fact]
    public async Task Sweeping_BodyPassagesCutToEarlierRules_CutsThemAgainAndLeavesTheAttachmentAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var message = await StoreOneMessageAsync(services, uid: 9371, cancellationToken);
        await OrchestratedPassages.CutAsync(services, message, cancellationToken);
        await SaveAttachmentReadingsAsync(services, message, [Document(0, "agreement")], cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);
        await SweepAsync(services, cancellationToken);

        var attachmentPassages = await ReadPassageIdsAsync(services, message, attachmentPosition: 0, cancellationToken);
        var cutToEarlierRules = await MarkCutToRulesAsync(
            services,
            message,
            attachmentPosition: null,
            EarlierRuleSetVersion,
            cancellationToken);

        // Act
        var sweep = await SweepAsync(services, cancellationToken);

        // Assert
        Assert.True(cutToEarlierRules.Count > 0);
        var recut = await ReadPassageIdsAsync(services, message, attachmentPosition: null, cancellationToken);
        Assert.Equal(cutToEarlierRules.Count, recut.Count);
        Assert.Empty(recut.Intersect(cutToEarlierRules));
        Assert.Equal(recut.Count, sweep.EmbeddedChunkCount);
        Assert.Equal(
            attachmentPassages.Order(),
            (await ReadPassageIdsAsync(services, message, attachmentPosition: 0, cancellationToken)).Order());
        Assert.Equal(
            await CountPassagesAsync(services, message, cancellationToken),
            await CountVectorsAsync(services, message, profileId, cancellationToken));
    }

    /// <summary>
    /// Passages a newer build cut are left to that build: the walk does not select them, cut them, or embed anything for
    /// them, which is what keeps two builds of a rolling upgrade from cutting each other's passages back and forth.
    /// </summary>
    /// <remarks>
    /// The digest is replaced along with the version, so a walk that selected on a different version rather than an
    /// earlier one would find passages the current rules do not reproduce and replace them — the failure this pins.
    /// </remarks>
    [Fact]
    public async Task Sweeping_PassagesCutToLaterRules_LeavesThemAndTheirVectorsAlone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var message = await StoreOneMessageAsync(services, uid: 9391, cancellationToken);
        await OrchestratedPassages.CutAsync(services, message, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);
        await SweepAsync(services, cancellationToken);
        var cutByANewerBuild = await MarkCutToRulesAsync(
            services,
            message,
            attachmentPosition: null,
            LaterRuleSetVersion,
            cancellationToken);
        var vectorCount = await CountVectorsAsync(services, message, profileId, cancellationToken);

        // Act
        var selected = await SelectAwaitingEmbeddingAsync(services, profileId, TimeSpan.Zero, cancellationToken);
        var sweep = await SweepAsync(services, cancellationToken);

        // Assert
        Assert.True(cutByANewerBuild.Count > 0);
        Assert.DoesNotContain(message, selected);
        Assert.Equal(0, sweep.ChunkedEmailCount);
        Assert.Equal(
            cutByANewerBuild.Order(),
            (await ReadPassageIdsAsync(services, message, attachmentPosition: null, cancellationToken)).Order());
        Assert.Equal(vectorCount, await CountVectorsAsync(services, message, profileId, cancellationToken));
    }

    /// <summary>
    /// A message the walk cuts for the first time also gains the passages its stored attachment readings yield, so a
    /// reading whose passages are missing is completed from the row rather than by reading the file again.
    /// </summary>
    /// <remarks>
    /// The missing attachment passages are arranged by removing the ones the reading's own commit cut, since the account
    /// run's stage cuts them in the same transaction as it stores the reading and no ordinary path leaves one without
    /// the other — which is what lets this assert a count rather than a presence.
    /// </remarks>
    [Fact]
    public async Task Sweeping_FirstCutOfAMessageWithStoredReadings_CutsItsAttachmentsFromTheReadings()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var message = await StoreOneMessageAsync(services, uid: 9381, cancellationToken);
        await SaveAttachmentReadingsAsync(services, message, [Document(0, "statement")], cancellationToken);
        var cutByTheStage = (await ReadPassageIdsAsync(services, message, attachmentPosition: 0, cancellationToken)).Count;
        await RemovePassagesAsync(services, message, attachmentPosition: 0, cancellationToken);
        await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.True(cutByTheStage > 0);
        Assert.Equal(
            cutByTheStage,
            (await ReadPassageIdsAsync(services, message, attachmentPosition: 0, cancellationToken)).Count);
        Assert.NotEmpty(await ReadPassageIdsAsync(services, message, attachmentPosition: null, cancellationToken));
    }

    /// <summary>
    /// A run that spends its batch budget leaves a position behind, and the next run continues past it rather than
    /// starting again — and reaching the end removes the row, so the sweep after that starts from the beginning and
    /// picks up whatever a refused call left without a vector.
    /// </summary>
    [Fact]
    public async Task ARunThatSpendsItsBatchBudget_LeavesAPositionTheNextRunResumesPastAndClearsItAtTheEnd()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);
        await SweepAsync(services, cancellationToken);

        // Three messages behind everything already embedded, so the walk has exactly this much left to find.
        var messages = new List<StoredEmailId>();
        foreach (var uid in (uint[])[9311, 9312, 9313])
        {
            messages.Add(await StoreOneMessageAsync(services, uid, cancellationToken));
        }

        // Act
        var first = await RunOnceAsync(services, cancellationToken, batchSize: 1, maxBatchesPerRun: 1);
        var positionAfterFirst = await ReadResumePositionAsync(services, cancellationToken);
        var rest = await SweepAsync(services, cancellationToken, batchSize: 1, maxBatchesPerRun: 1);
        var positionAfterSweep = await ReadResumePositionAsync(services, cancellationToken);

        // Assert
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent, first.Outcome);
        Assert.Equal(messages[0], positionAfterFirst);
        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, rest.Outcome);
        Assert.Null(positionAfterSweep);
    }

    /// <summary>
    /// Mail stored under an alias no mapping names is outside the walk, so a folder an operator withdrew never becomes
    /// a bill: nothing of it is cut and nothing of it is embedded, however long the deployment runs.
    /// </summary>
    /// <remarks>
    /// The walk is where this has to be proved rather than at the cut, because the two answer different halves. A
    /// message with a body and no passages is exactly what the sweep's second group selects, so a folder left out only
    /// at the cut would be found outstanding on every sweep for the rest of the deployment's life. The mapped message
    /// beside it is the control: without it, zero passages would report a query that selected nothing at all.
    /// </remarks>
    [Fact]
    public async Task Sweeping_MailInAFolderNoMappingNames_CutsNothingAndEmbedsNothingOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unmapped = await StoreOneMessageAsync(
            services,
            uid: 9321,
            cancellationToken,
            SyntheticMailAccount.UnmappedFolderAlias);
        var mapped = await StoreOneMessageAsync(services, uid: 9322, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.Equal(0, await CountPassagesAsync(services, unmapped, cancellationToken));
        Assert.Equal(0, await CountVectorsAsync(services, unmapped, profileId, cancellationToken));
        Assert.True(await CountPassagesAsync(services, mapped, cancellationToken) > 0);
        Assert.True(await CountVectorsAsync(services, mapped, profileId, cancellationToken) > 0);
    }

    /// <summary>
    /// The sweep obeys the arrival pipeline's order rather than a version of it: a message the user's rules have not
    /// reached is not cut here, however long it has been sitting uncut.
    /// </summary>
    /// <remarks>
    /// This is the ordering that is easiest to lose, because the sweep runs on its own interval while an account run is
    /// still fetching a mailbox — so without it a first synchronization would have its mail cut by whichever of the two
    /// got there first, before a rule had read any of it. The stamped message beside it is the control: without one,
    /// zero passages would report a sweep that found nothing at all rather than one that passed this message over.
    /// </remarks>
    [Fact]
    public async Task Sweeping_MailTheRulesHaveNotReachedYet_CutsNothingAndEmbedsNothingOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var unevaluated = await StoreOneMessageAsync(services, uid: 9331, cancellationToken);
        var evaluated = await StoreOneMessageAsync(services, uid: 9332, cancellationToken);
        await OrchestratedRuleEvaluationStamp.ClearAsync(services, unevaluated, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.Equal(0, await CountPassagesAsync(services, unevaluated, cancellationToken));
        Assert.Equal(0, await CountVectorsAsync(services, unevaluated, profileId, cancellationToken));
        Assert.True(await CountPassagesAsync(services, evaluated, cancellationToken) > 0);
        Assert.True(await CountVectorsAsync(services, evaluated, profileId, cancellationToken) > 0);
    }

    /// <summary>
    /// A message a rule has filed elsewhere is passed over too, until the move has actually landed: the stamp says the
    /// rules finished, and the record beside it says where the message is going.
    /// </summary>
    /// <remarks>
    /// A rule declares a move rather than performing one, and the account's next run carries it to the server, so
    /// between the two the message sits in a folder it is leaving. This sweep runs on its own interval inside that
    /// window, and passages are not undone by the message moving afterwards — so cutting it here would describe it under
    /// the mapping it is about to leave, permanently. The settled message beside it is the control: without one, zero
    /// passages would report a sweep that found nothing rather than one that passed this message over.
    /// </remarks>
    [Fact]
    public async Task Sweeping_MailARuleIsStillMoving_CutsNothingAndEmbedsNothingOfIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var relocating = await StoreOneMessageAsync(services, uid: 9341, cancellationToken);
        var settled = await StoreOneMessageAsync(services, uid: 9342, cancellationToken);
        await RecordConvergingRelocationAsync(services, relocating, uid: 9341, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // Act
        await SweepAsync(services, cancellationToken);

        // Assert
        Assert.Equal(0, await CountPassagesAsync(services, relocating, cancellationToken));
        Assert.Equal(0, await CountVectorsAsync(services, relocating, profileId, cancellationToken));
        Assert.True(await CountPassagesAsync(services, settled, cancellationToken) > 0);
        Assert.True(await CountVectorsAsync(services, settled, profileId, cancellationToken) > 0);
    }

    /// <summary>
    /// The window that leaves freshly cut mail to the live path is a predicate over the passages still lacking a vector,
    /// read against the instant PostgreSQL holds for each cut. A message whose only outstanding passages were cut inside
    /// it is left; one whose outstanding passages were cut before it is walked, and so is one whose fresh passage the
    /// live path has already embedded while an older passage still lacks a vector. The unwindowed read is the control
    /// that would report the first message all the same.
    /// </summary>
    [Fact]
    public async Task SelectingWithALiveEmbeddingWindow_LeavesOnlyMailWithAnUnembeddedPassageCutInsideIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var justCut = await StoreOneMessageAsync(services, uid: 9351, cancellationToken);
        var cutEarlier = await StoreOneMessageAsync(services, uid: 9352, cancellationToken);
        var justCutAndEmbedded = await StoreOneMessageAsync(services, uid: 9353, cancellationToken);
        var profileId = await OrchestratedEmbeddingProfile.EnsureActiveDeterministicAsync(services, cancellationToken);

        // A loop rather than a projection, because each cut is a commit of its own.
        foreach (var message in (StoredEmailId[])[justCut, cutEarlier, justCutAndEmbedded])
        {
            await OrchestratedPassages.CutAsync(services, message, cancellationToken);
        }

        Assert.True(await CountPassagesAsync(services, justCutAndEmbedded, cancellationToken) > 1);

        await DatePassagesBeforeTheWindowAsync(services, cutEarlier, fromOrdinal: 0, cancellationToken);

        // The live path embeds the whole message; its later passages are then dated before the window and lose their
        // vectors, which leaves the first passage fresh and embedded and the rest older and outstanding.
        await EmbedIntoTheServingGenerationAsync(services, justCutAndEmbedded, cancellationToken);
        await DatePassagesBeforeTheWindowAsync(services, justCutAndEmbedded, fromOrdinal: 1, cancellationToken);
        await RemoveVectorsFromOrdinalAsync(services, justCutAndEmbedded, fromOrdinal: 1, cancellationToken);

        // Act
        var windowed = await SelectAwaitingEmbeddingAsync(services, profileId, LiveEmbeddingWindow, cancellationToken);
        var unwindowed = await SelectAwaitingEmbeddingAsync(services, profileId, TimeSpan.Zero, cancellationToken);

        // Assert
        Assert.DoesNotContain(justCut, windowed);
        Assert.Contains(cutEarlier, windowed);
        Assert.Contains(justCutAndEmbedded, windowed);
        Assert.Contains(justCut, unwindowed);
    }

    /// <summary>Writes down a relocation nothing has carried to the mail server yet.</summary>
    /// <remarks>
    /// Opened through the port the rule pass writes through rather than by inserting a row, so the record carries the
    /// stage a freshly authored move actually has — <see cref="MailboxMutationStage.Recorded" />, which is neither
    /// completed nor abandoned and is therefore exactly what the sweep has to pass over.
    /// </remarks>
    private static async Task RecordConvergingRelocationAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        uint uid,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);

        var record = default(MailboxMutationRecord);
        var commitResult = await services.CommitAsync(
            async (scope, session, token) => record = await scope
                .GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(
                    session,
                    MailboxMutationRequest.Relocate(
                        storedEmailId,
                        occurrenceId,
                        MailboxMutationRequester.Rule("file-the-newsletters", "1"),
                        RemoteFolderPath.Create("Archive")),
                    heldUntil: null,
                    MailboxMutationLocalChange.None,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
        Assert.Equal(MailboxMutationStage.Recorded, record?.Stage);
    }

    /// <summary>Runs the backfill until the sweep ends, and answers with the last run that did any work.</summary>
    private static async Task<StoredEmailEmbeddingBackfillResult> SweepAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        int batchSize = 20,
        int maxBatchesPerRun = 25)
    {
        var chunkedEmailCount = 0;
        var embeddedEmailCount = 0;
        var embeddedChunkCount = 0;
        StoredEmailEmbeddingBackfillResult run;
        var attempt = 0;

        do
        {
            run = await RunOnceAsync(services, cancellationToken, batchSize, maxBatchesPerRun);
            chunkedEmailCount += run.ChunkedEmailCount;
            embeddedEmailCount += run.EmbeddedEmailCount;
            embeddedChunkCount += run.EmbeddedChunkCount;
            attempt++;
        }
        while (run.MoreWorkIsWorthTryingSoon && attempt < MaximumRunsPerSweep);

        Assert.Equal(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, run.Outcome);

        return run with
        {
            ChunkedEmailCount = chunkedEmailCount,
            EmbeddedEmailCount = embeddedEmailCount,
            EmbeddedChunkCount = embeddedChunkCount,
        };
    }

    /// <summary>
    /// Builds the backfill by hand so a test chooses its own bounds, over the real store, generator, and retry policy
    /// the container resolves.
    /// </summary>
    private static Task<StoredEmailEmbeddingBackfillResult> RunOnceAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken,
        int batchSize = 20,
        int maxBatchesPerRun = 25) => services.InScopeAsync(
            async (scope, token) =>
            {
                var generations = await scope.GetRequiredService<IEmbeddingGenerationStore>()
                    .ReadGenerationsAsync(token);

                // No live path runs beside this harness, so nothing is left to one: mail it has just cut is walked at once.
                return await new StoredEmailEmbeddingBackfill(
                    scope.GetRequiredService<IStoredEmailEmbeddingBackfillStore>(),
                    scope.GetRequiredService<StoredEmailEmbeddingGenerator>(),
                    scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>(),
                    scope.GetRequiredService<IDerivedWorkGateTelemetry>(),
                    new StoredEmailEmbeddingBackfillOptions
                    {
                        BatchSize = batchSize,
                        MaxBatchesPerRun = maxBatchesPerRun,
                    })
                    .RunAsync(Assert.IsType<RegisteredEmbeddingProfile>(generations.Target), serving: null, token);
            },
            cancellationToken);

    private static Task<StoredEmailId?> ReadResumePositionAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailEmbeddingBackfillStore>()
                .FindResumePositionAsync(token),
            cancellationToken);

    /// <summary>Stores one synthetic message with no passages, as an instance that predates chunking holds it.</summary>
    /// <remarks>
    /// The metadata write cuts nothing — the cut is a step of the account run rather than something a store does on its
    /// way past — so the message is stamped as one the rules have finished with, which is the state every cutting path
    /// waits for and the state a run would have left it in. What this seeds is therefore exactly the sweep's first
    /// group: text, no passages, and nothing still to happen to the message before it may be cut.
    /// </remarks>
    private static async Task<StoredEmailId> StoreOneMessageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken,
        string folderAlias = FolderAlias)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, folderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"embedding-backfill-{uid}";
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session, SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    SyntheticEmail.ExtractionOf(
                        occurrenceId,
                        subject,
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 400),
                        "recipient@mailfathom.test"),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        await OrchestratedRuleEvaluationStamp.ApplyAsync(services, storedEmailId, SyntheticEmail.SentAt, cancellationToken);

        return storedEmailId;
    }

    /// <summary>Builds one document reading long enough to be cut into a passage of its own.</summary>
    private static DerivedAttachmentText Document(int position, string term) => DerivedAttachmentText.FromExtraction(
        position,
        "application/pdf",
        $"{term}.pdf",
        AttachmentTextExtractionResult.Extracted(new ExtractedAttachmentText(
            SyntheticEmail.BodyTextContaining(term, wordCount: 400),
            PageCount: 1,
            [],
            [new AttachmentTextSegment(AttachmentTextSegmentKind.Page, 1, Label: null, StartOffset: 0)])));

    /// <summary>Commits what the attachment stage would have read off a message, which also cuts its passages.</summary>
    private static async Task SaveAttachmentReadingsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        IReadOnlyList<DerivedAttachmentText> attachments,
        CancellationToken cancellationToken)
    {
        var result = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailAttachmentTextStore>()
                .SaveAttachmentTextAsync(
                    session,
                    storedEmailId,
                    new EmailAttachmentTextDerivation(attachments, RedactedUnder: null),
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, result);
    }

    /// <summary>Reads the identities of one text's passages, the body's being the ones with no attachment position.</summary>
    private static Task<IReadOnlyList<Guid>> ReadPassageIdsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        int? attachmentPosition,
        CancellationToken cancellationToken) => services.InScopeAsync<IReadOnlyList<Guid>>(
            async (scope, token) => await scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .AsNoTracking()
                .Where(chunk => chunk.StoredEmailId == storedEmailId.Value
                    && chunk.AttachmentPosition == attachmentPosition)
                .Select(chunk => chunk.Id)
                .ToArrayAsync(token),
            cancellationToken);

    /// <summary>Reads the identities of the passages the re-cut must leave alone: the body's, and the second attachment's.</summary>
    private static Task<Guid[]> ReadUntouchedPassageIdsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .AsNoTracking()
                .Where(chunk => chunk.StoredEmailId == storedEmailId.Value
                    && (chunk.AttachmentPosition == null || chunk.AttachmentPosition == 1))
                .OrderBy(chunk => chunk.Id)
                .Select(chunk => chunk.Id)
                .ToArrayAsync(token),
            cancellationToken);

    private static Task<int> RemovePassagesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        int? attachmentPosition,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .Where(chunk => chunk.StoredEmailId == storedEmailId.Value
                    && chunk.AttachmentPosition == attachmentPosition)
                .ExecuteDeleteAsync(token),
            cancellationToken);

    /// <summary>Leaves one text's passages as another rule set would: its version, and a digest the current rules do not produce.</summary>
    private static async Task<IReadOnlyList<Guid>> MarkCutToRulesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        int? attachmentPosition,
        int ruleSetVersion,
        CancellationToken cancellationToken)
    {
        var marked = await ReadPassageIdsAsync(services, storedEmailId, attachmentPosition, cancellationToken);

        await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .Where(chunk => marked.Contains(chunk.Id))
                .ExecuteUpdateAsync(
                    chunk => chunk
                        .SetProperty(row => row.RuleSetVersion, ruleSetVersion)
                        .SetProperty(row => row.ContentHash, new string('0', 64)),
                    token),
            cancellationToken);

        return marked;
    }

    private static Task<DateTimeOffset?> ReadAttachmentTextDerivedAtAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().StoredEmails
                .AsNoTracking()
                .Where(email => email.Id == storedEmailId.Value)
                .Select(email => email.AttachmentTextDerivedAt)
                .SingleAsync(token),
            cancellationToken);

    private static Task<int> CountPassagesAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .AsNoTracking()
                .CountAsync(chunk => chunk.StoredEmailId == storedEmailId.Value, token),
            cancellationToken);

    private static Task<int> CountVectorsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailEmbeddings
                .AsNoTracking()
                .CountAsync(
                    embedding => embedding.EmbeddingProfileId == profileId.Value
                        && embedding.EmailChunk!.StoredEmailId == storedEmailId.Value,
                    token),
            cancellationToken);

    private static Task<IReadOnlyList<StoredEmailId>> SelectAwaitingEmbeddingAsync(
        OrchestratedMailFathomServices services,
        EmbeddingProfileId profileId,
        TimeSpan liveEmbeddingWindow,
        CancellationToken cancellationToken) => services.InScopeAsync<IReadOnlyList<StoredEmailId>>(
            async (scope, token) =>
            {
                var batch = await scope.GetRequiredService<IStoredEmailEmbeddingBackfillStore>()
                    .GetEmailsAwaitingEmbeddingAsync(
                        resumeAfter: null,
                        profileId,
                        liveEmbeddingWindow,
                        batchSize: 10_000,
                        token);

                return [.. batch.Select(email => email.StoredEmailId)];
            },
            cancellationToken);

    private static Task<StoredEmailEmbeddingRun> EmbedIntoTheServingGenerationAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var generations = await scope.GetRequiredService<IEmbeddingGenerationStore>()
                    .ReadGenerationsAsync(token);
                var run = await scope.GetRequiredService<StoredEmailEmbeddingGenerator>().EmbedAsync(
                    storedEmailId,
                    Assert.IsType<RegisteredEmbeddingProfile>(generations.Serving),
                    token);

                Assert.Equal(StoredEmailEmbeddingOutcome.Embedded, run.Outcome);

                return run;
            },
            cancellationToken);

    /// <summary>Moves the instant a message's passages were cut back to the synthetic send, long before any window.</summary>
    private static Task<int> DatePassagesBeforeTheWindowAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        int fromOrdinal,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>().EmailChunks
                .Where(chunk => chunk.StoredEmailId == storedEmailId.Value && chunk.Ordinal >= fromOrdinal)
                .ExecuteUpdateAsync(chunk => chunk.SetProperty(row => row.DerivedAt, SyntheticEmail.SentAt), token),
            cancellationToken);

    private static Task<int> RemoveVectorsFromOrdinalAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        int fromOrdinal,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var context = scope.GetRequiredService<MailFathomDbContext>();
                var chunkIds = await context.EmailChunks
                    .AsNoTracking()
                    .Where(chunk => chunk.StoredEmailId == storedEmailId.Value && chunk.Ordinal >= fromOrdinal)
                    .Select(chunk => chunk.Id)
                    .ToArrayAsync(token);

                return await context.EmailEmbeddings
                    .Where(embedding => chunkIds.Contains(embedding.EmailChunkId))
                    .ExecuteDeleteAsync(token);
            },
            cancellationToken);
}
