// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that the classification write refuses to replace the person's own verdict, against the row it reads.</summary>
/// <remarks>
/// The refusal is the only thing standing between a stage's result and a person's verdict recorded after that stage
/// read the row, and it is decided inside the session against what the database holds. The unit suite drives the
/// classifier over an in-memory store that restates the same rule, so only a real write shows that this store applies
/// it — and that a refused write leaves the person's row and its signals exactly as they were committed.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailSpamClassificationStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "spam-classifications";

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A stage's classification of a message the person already judged is refused and stages nothing.</summary>
    [Fact]
    public async Task SaveAsync_AStageClassificationOverThePersonsVerdict_RefusesAndLeavesTheRowAsCommitted()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, uid: 9901),
            "persons-verdict-kept",
            cancellationToken);

        var personsVerdict = Classification(storedEmailId, SpamVerdict.NotSpam, SpamClassificationStage.Person, []);
        Assert.True(await SaveAsync(services, personsVerdict, cancellationToken));

        var stageVerdict = Classification(
            storedEmailId,
            SpamVerdict.Spam,
            SpamClassificationStage.Deterministic,
            [HeaderSignal("X-Spam-Flag")]);

        // Act
        var replaced = await SaveAsync(services, stageVerdict, cancellationToken);

        // Assert
        var recorded = await FindAsync(services, storedEmailId, cancellationToken);

        Assert.False(replaced);
        Assert.NotNull(recorded);
        Assert.Equal(SpamClassificationStage.Person, recorded.DecidedBy);
        Assert.Equal(SpamVerdict.NotSpam, recorded.Verdict);
        Assert.Empty(recorded.Signals);
    }

    /// <summary>
    /// The person's verdict replaces a stage's, signals included — the control that shows the refusal above is the
    /// rule rather than a write that replaces nothing.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ThePersonsVerdictOverAStageClassification_ReplacesItAndItsSignals()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            SyntheticEmail.OccurrenceIn(binding, uid: 9902),
            "persons-verdict-replaces",
            cancellationToken);

        var stageVerdict = Classification(
            storedEmailId,
            SpamVerdict.Spam,
            SpamClassificationStage.Deterministic,
            [HeaderSignal("X-Spam-Flag")]);
        Assert.True(await SaveAsync(services, stageVerdict, cancellationToken));

        var recordedByTheStage = await FindAsync(services, storedEmailId, cancellationToken);
        var personsVerdict = Classification(storedEmailId, SpamVerdict.NotSpam, SpamClassificationStage.Person, []);

        // Act
        var replaced = await SaveAsync(services, personsVerdict, cancellationToken);

        // Assert
        var recorded = await FindAsync(services, storedEmailId, cancellationToken);

        Assert.True(replaced);
        Assert.NotNull(recordedByTheStage);
        Assert.Single(recordedByTheStage.Signals);
        Assert.NotNull(recorded);
        Assert.Equal(SpamClassificationStage.Person, recorded.DecidedBy);
        Assert.Equal(SpamVerdict.NotSpam, recorded.Verdict);
        Assert.Empty(recorded.Signals);
    }

    private static SpamClassification Classification(
        StoredEmailId storedEmailId,
        SpamVerdict verdict,
        SpamClassificationStage decidedBy,
        IReadOnlyList<SpamSignal> signals) => SpamClassification.Create(
        storedEmailId,
        verdict,
        decidedBy,
        assessment: null,
        SpamClassificationProfile.Create(),
        signals,
        EvaluatedAt);

    private static SpamSignal HeaderSignal(string name) => SpamSignal.Create(
        SpamSignalKind.ProviderSpamVerdict,
        name,
        observation: null,
        SpamSignalProvenance.FromMessageHeader(name));

    private static Task<bool> SaveAsync(
        OrchestratedMailFathomServices services,
        SpamClassification classification,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailSpamClassificationStore>()
                .SaveAsync(session, classification, token),
            cancellationToken);

    private static Task<SpamClassification?> FindAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IEmailSpamClassificationStore>().FindAsync(storedEmailId, token),
            cancellationToken);
}
