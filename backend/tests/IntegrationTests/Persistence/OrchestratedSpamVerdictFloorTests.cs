// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Floor;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the spam floor's candidate query and its conditional write against a real schema.</summary>
/// <remarks>
/// <para>
/// The query restates <see cref="DeterministicSpamClassifier.LiftToFloor" /> in SQL — keyword array containment, an
/// exemption that reads the deployment's own verdict acts, and the classification wait — and the unit suite reaches the
/// pass through an in-memory reader that ignores its terms. A query that disagreed with the rule would select, on every
/// run, mail the pass then refuses, until those rows used up the pass's whole bound; only a real query can show that it
/// agrees. Every account-wide read here meets the rows other classes left in the shared database, so each case is read
/// as whether this class's own message is selected rather than as the whole answer.
/// </para>
/// <para>
/// The write is conditional on the row the session reads, and two replicas raising one message at once is the ordinary
/// case rather than a race to treat as unlikely, so it is proved concurrently.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedSpamVerdictFloorTests(MailFathomOrchestrationFixture orchestration)
{
    private const string JunkFolderAlias = "spam-floor-junk";

    private const string ArchiveFolderAlias = "spam-floor-archive";

    private const int ConcurrentPasses = 6;

    private const int CandidateBatchSize = 500;

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset LongBeforeAnyMessageWasStored = DateTimeOffset.UnixEpoch;

    private static readonly DateTimeOffset LongAfterEveryMessageWasStored = new(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create(JunkFolderAlias);

    private static readonly MailFolderAlias Archive = MailFolderAlias.Create(ArchiveFolderAlias);

    /// <summary>
    /// Every combination of placement, <c>$Junk</c>, <c>$NotJunk</c>, and the deployment's own <c>$Junk</c> is
    /// selected exactly when the rule raises it.
    /// </summary>
    [Fact]
    public async Task GetCandidatesAsync_EveryPlacementAndKeywordCase_SelectsExactlyWhatTheRuleRaises()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        FloorCase[] cases =
        [
            new(9701, JunkFolderAlias, [], OwnJunkKeyword: false),
            new(9702, ArchiveFolderAlias, ["$Junk"], OwnJunkKeyword: false),
            new(9703, ArchiveFolderAlias, ["$Junk"], OwnJunkKeyword: true),
            new(9704, ArchiveFolderAlias, ["$Junk", "$NotJunk"], OwnJunkKeyword: false),
            new(9705, JunkFolderAlias, ["$NotJunk"], OwnJunkKeyword: false),
            new(9706, JunkFolderAlias, ["$Junk", "$NotJunk"], OwnJunkKeyword: false),
            new(9707, JunkFolderAlias, ["$Junk", "$NotJunk"], OwnJunkKeyword: true),
            new(9708, ArchiveFolderAlias, ["$Label1"], OwnJunkKeyword: false),
        ];
        var stored = new Dictionary<FloorCase, StoredEmailId>();

        foreach (var floorCase in cases)
        {
            stored[floorCase] = await StoreWithKeywordsAsync(
                services,
                floorCase.FolderAlias,
                floorCase.Uid,
                StoredEmailContentAvailability.ExceededSizeLimit,
                floorCase.Keywords,
                cancellationToken);

            if (floorCase.OwnJunkKeyword)
            {
                await RecordOwnActAsync(services, stored[floorCase], SpamVerdictAct.MarkedJunk, cancellationToken);
            }
        }

        // Act
        var candidates = await AllCandidatesAsync(services, Terms(classified: []), cancellationToken);

        // Assert
        var classifier = new DeterministicSpamClassifier();
        var expected = cases.ToDictionary(
            floorCase => floorCase.Uid,
            floorCase => classifier.LiftToFloor(
                DeterministicSpamReading.Nothing,
                floorCase.FolderAlias == JunkFolderAlias ? (MailFolderAlias?)Junk : null,
                RemoteEmailKeywords.Create(floorCase.Keywords),
                floorCase.OwnJunkKeyword ? new HashSet<SpamVerdictAct> { SpamVerdictAct.MarkedJunk } : []).Verdict
                is SpamVerdict.Spam);
        var selected = cases.ToDictionary(
            floorCase => floorCase.Uid,
            floorCase => candidates.Any(candidate => candidate.Id == stored[floorCase]));

        Assert.Equal(expected, selected);
        Assert.Equal(
            new uint[] { 9701, 9702, 9703, 9706 },
            expected.Where(static entry => entry.Value).Select(static entry => entry.Key).Order());
    }

    /// <summary>
    /// A verdict below spam that a stage wrote is selected, while spam already recorded and the person's own verdict are
    /// not — so the floor never takes a subcategory away and never overrides the person.
    /// </summary>
    [Fact]
    public async Task GetCandidatesAsync_JunkFolderMessagesWithAVerdict_SelectsOnlyTheOneAStageLeftBelowSpam()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var belowSpam = await StoreWithKeywordsAsync(
            services,
            JunkFolderAlias,
            9711,
            StoredEmailContentAvailability.ExceededSizeLimit,
            [],
            cancellationToken);
        var alreadySpam = await StoreWithKeywordsAsync(
            services,
            JunkFolderAlias,
            9712,
            StoredEmailContentAvailability.ExceededSizeLimit,
            [],
            cancellationToken);
        var personsVerdict = await StoreWithKeywordsAsync(
            services,
            JunkFolderAlias,
            9713,
            StoredEmailContentAvailability.ExceededSizeLimit,
            [],
            cancellationToken);
        await SaveAsync(
            services,
            Classification(belowSpam, SpamVerdict.NotSpam, SpamClassificationStage.Deterministic),
            cancellationToken);
        await SaveAsync(
            services,
            Classification(alreadySpam, SpamVerdict.Spam, SpamClassificationStage.Deterministic),
            cancellationToken);
        await SaveAsync(
            services,
            Classification(personsVerdict, SpamVerdict.NotSpam, SpamClassificationStage.Person),
            cancellationToken);

        // Act
        var candidates = await AllCandidatesAsync(services, Terms(classified: []), cancellationToken);

        // Assert
        Assert.Contains(candidates, candidate => candidate.Id == belowSpam);
        Assert.DoesNotContain(candidates, candidate => candidate.Id == alreadySpam);
        Assert.DoesNotContain(candidates, candidate => candidate.Id == personsVerdict);
    }

    /// <summary>
    /// A message whose content classification will read is left to the arrival job while it is within its wait, and is
    /// selected once the wait has run out; a message with no content stored awaits nothing and is selected either way.
    /// </summary>
    [Fact]
    public async Task GetCandidatesAsync_AMessageAwaitingClassification_IsLeftToItUntilTheWaitRunsOut()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var awaiting = await StoreWithKeywordsAsync(
            services,
            ArchiveFolderAlias,
            9721,
            StoredEmailContentAvailability.Available,
            ["$Junk"],
            cancellationToken);
        var withoutContent = await StoreWithKeywordsAsync(
            services,
            ArchiveFolderAlias,
            9722,
            StoredEmailContentAvailability.ExceededSizeLimit,
            ["$Junk"],
            cancellationToken);

        // Act
        var withinTheWait = await AllCandidatesAsync(
            services,
            Terms(classified: [Archive], awaitedWhenStoredAfter: LongBeforeAnyMessageWasStored),
            cancellationToken);
        var afterTheWait = await AllCandidatesAsync(
            services,
            Terms(classified: [Archive], awaitedWhenStoredAfter: LongAfterEveryMessageWasStored),
            cancellationToken);

        // Assert
        Assert.DoesNotContain(withinTheWait, candidate => candidate.Id == awaiting);
        Assert.Contains(withinTheWait, candidate => candidate.Id == withoutContent);
        Assert.Contains(afterTheWait, candidate => candidate.Id == awaiting);
        Assert.Contains(afterTheWait, candidate => candidate.Id == withoutContent);
    }

    /// <summary>
    /// Every replica raising one message at once writes one verdict: each reads the row inside its own session, the
    /// losers conflict on the key or the row version and read again, and the spam they then find refuses them.
    /// </summary>
    [Fact]
    public async Task LiftToSpamAsync_ManyPassesRaisingOneMessage_WritesOneVerdict()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var storedEmailId = await StoreWithKeywordsAsync(
            services,
            JunkFolderAlias,
            9731,
            StoredEmailContentAvailability.ExceededSizeLimit,
            [],
            cancellationToken);
        var floorVerdict = Classification(storedEmailId, SpamVerdict.Spam, SpamClassificationStage.Deterministic);

        // Act
        var attempts = await ConcurrentIdempotency.RunAsync(
            $"{nameof(IEmailSpamClassificationStore)}.{nameof(IEmailSpamClassificationStore.LiftToSpamAsync)}",
            ConcurrentPasses,
            (_, token) => LiftAsync(services, floorVerdict, token),
            cancellationToken);

        // Assert
        attempts.AssertSingleEffect(attempts.Results.Count(static written => written));

        var recorded = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailSpamClassificationStore>().FindAsync(storedEmailId, token),
            cancellationToken);

        Assert.NotNull(recorded);
        Assert.Equal(SpamVerdict.Spam, recorded.Verdict);
    }

    private static SpamVerdictFloorTerms Terms(
        IReadOnlyList<MailFolderAlias> classified,
        DateTimeOffset? awaitedWhenStoredAfter = null) =>
        new([Junk], classified, awaitedWhenStoredAfter ?? LongBeforeAnyMessageWasStored);

    private static async Task<IReadOnlyList<SpamVerdictFloorCandidate>> AllCandidatesAsync(
        OrchestratedMailFathomServices services,
        SpamVerdictFloorTerms terms,
        CancellationToken cancellationToken) => await services.InScopeAsync(
            async (scope, token) =>
            {
                var reader = scope.GetRequiredService<ISpamVerdictFloorCandidateReader>();
                var candidates = new List<SpamVerdictFloorCandidate>();
                var resumeAfter = default(StoredEmailId?);
                IReadOnlyList<SpamVerdictFloorCandidate> batch;

                do
                {
                    batch = await reader.GetCandidatesAsync(
                        SyntheticMailAccount.AccountId,
                        terms,
                        resumeAfter,
                        CandidateBatchSize,
                        token);
                    candidates.AddRange(batch);
                    resumeAfter = batch.Count > 0 ? batch[^1].Id : resumeAfter;
                }
                while (batch.Count == CandidateBatchSize);

                return (IReadOnlyList<SpamVerdictFloorCandidate>)candidates;
            },
            cancellationToken);

    private static async Task<StoredEmailId> StoreWithKeywordsAsync(
        OrchestratedMailFathomServices services,
        string folderAlias,
        uint uid,
        StoredEmailContentAvailability contentAvailability,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, folderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var storedEmailId = await services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrenceId, $"spam-floor-{uid}"),
                extractedMetadata: null,
                contentAvailability,
                token),
            cancellationToken);

        await ObserveKeywordsAsync(services, storedEmailId, keywords, cancellationToken);

        return storedEmailId;
    }

    /// <summary>Records the keywords a server reported for one stored email, through the reconciliation write.</summary>
    private static async Task ObserveKeywordsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        IReadOnlyList<string> keywords,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            async (scope, session, token) => await scope
                .GetRequiredService<IStoredEmailReconciliationStore>()
                .ApplyReconciliationOutcomeAsync(
                    session,
                    new ReconciledFolderOutcome(
                        [
                            new ObservedEmailFlags(
                                storedEmailId,
                                RemoteEmailFlagSnapshot.NeverObserved with
                                {
                                    ObservedAt = EvaluatedAt,
                                    Keywords = RemoteEmailKeywords.Create(keywords),
                                }),
                        ],
                        ConfirmedUnchanged: [],
                        Disappeared: [],
                        RemovedByOwnMutation: [],
                        RemotelyDeletedEmailDisposition.RetainTombstone,
                        AppliesRemoteDeletions: true,
                        EvaluatedAt),
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static async Task RecordOwnActAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        SpamVerdictAct act,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<ISpamVerdictActStore>()
                .RecordAsync(session, storedEmailId, act, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static Task<bool> SaveAsync(
        OrchestratedMailFathomServices services,
        SpamClassification classification,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailSpamClassificationStore>()
                .SaveAsync(session, classification, token),
            cancellationToken);

    private static Task<bool> LiftAsync(
        OrchestratedMailFathomServices services,
        SpamClassification classification,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>().CommitAsync(
                (session, attemptToken) => scope.GetRequiredService<IEmailSpamClassificationStore>()
                    .LiftToSpamAsync(session, classification, attemptToken),
                token),
            cancellationToken);

    private static SpamClassification Classification(
        StoredEmailId storedEmailId,
        SpamVerdict verdict,
        SpamClassificationStage decidedBy) => SpamClassification.Create(
        storedEmailId,
        verdict,
        decidedBy,
        assessment: null,
        SpamClassificationProfile.Create(),
        [],
        EvaluatedAt);

    /// <summary>One message the floor is asked about: where it is, what it carries, and whether its <c>$Junk</c> is the deployment's own.</summary>
    private sealed record FloorCase(uint Uid, string FolderAlias, IReadOnlyList<string> Keywords, bool OwnJunkKeyword);
}
