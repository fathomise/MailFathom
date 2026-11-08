// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>
/// Proves what the enrichment selection hands a derivation about each message, against a real schema.
/// </summary>
/// <remarks>
/// The instant a relative date is resolved against is chosen from two the query projects, so only a real server says
/// whether both reach the choice — a message no receiving hop dated reaching the derivation dated by when it was sent
/// rather than not dated at all. Which of the two wins for mail the mailbox wrote is decided by
/// <see cref="EnrichableEmail.DatedAtOf" />, whose own tests cover it.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailEnrichmentSelectionTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "email-enrichment";

    /// <summary>
    /// Large enough to reach this class's messages behind whatever else in the shared database is awaiting a derivation.
    /// </summary>
    /// <remarks>
    /// The selection is ordered by identifier, and every other class in the collection may leave cut, evaluated mail
    /// behind that no derivation has reached, all of it older than what this class stores.
    /// </remarks>
    private const int EveryAwaitingMessage = int.MaxValue;

    /// <summary>
    /// One test over the three datings of mail somebody else wrote, because each is only meaningful beside the others:
    /// a message keeps its arrival over its sent instant, one no hop dated falls back to its sent instant, and one
    /// carrying neither stays undated rather than being given one.
    /// </summary>
    [Fact]
    public async Task GetEmailsAwaitingEnrichmentAsync_MessagesDatedEachWay_DatesEachByArrivalThenBySending()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var received = await StoreCutMessageAsync(
            services,
            uid: 9851,
            static extraction => extraction,
            cancellationToken);
        var undatedByAnyHop = await StoreCutMessageAsync(
            services,
            uid: 9852,
            static extraction => extraction with { ReceivedAt = null },
            cancellationToken);
        var undated = await StoreCutMessageAsync(
            services,
            uid: 9853,
            static extraction => extraction with { SentAt = null, ReceivedAt = null },
            cancellationToken);

        // Act
        var awaiting = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailEnrichmentStore>().GetEmailsAwaitingEnrichmentAsync(
                SyntheticMailAccount.AccountId,
                EveryAwaitingMessage,
                MailEnrichmentPass.MaximumPassagesPerEmail,
                token),
            cancellationToken);

        // Assert
        var datedAt = awaiting.ToDictionary(static email => email.StoredEmailId, static email => email.DatedAt);

        Assert.Equal(SyntheticEmail.ReceivedAt, datedAt[received]);
        Assert.Equal(SyntheticEmail.SentAt, datedAt[undatedByAnyHop]);
        Assert.Null(datedAt[undated]);
    }

    /// <summary>Stores one message, cut and evaluated, so nothing but the dating decides what the selection reads.</summary>
    private static async Task<StoredEmailId> StoreCutMessageAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        Func<ExtractedEmailMetadata, ExtractedEmailMetadata> dating,
        CancellationToken cancellationToken)
    {
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
        var subject = $"email-enrichment-{uid}";
        var storedEmailId = default(StoredEmailId);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) => storedEmailId = await scope
                .GetRequiredService<IEmailMetadataRepository>()
                .UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                    dating(SyntheticEmail.ExtractionOf(
                        occurrenceId,
                        subject,
                        SyntheticEmail.BodyTextContaining(subject, wordCount: 40),
                        "recipient@mailfathom.test")),
                    StoredEmailContentAvailability.Available,
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        await OrchestratedRuleEvaluationStamp.ApplyAsync(services, storedEmailId, SyntheticEmail.SentAt, cancellationToken);
        await OrchestratedPassages.CutAsync(services, storedEmailId, cancellationToken);

        return storedEmailId;
    }
}
