// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers the schema a composition asks a model for: that a provider accepts it, that it cites only what the run declared, and that nothing it admits is withheld.</summary>
public sealed class DiscoveryCompositionSchemaTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly DateTimeOffset ObservedAt = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(PresentationEvidence.MaxCitations)]
    public void For_AnyNumberOfDeclaredSources_KeepsToWhatStrictModeAccepts(int sourceCount)
    {
        // Act
        var schema = DiscoveryCompositionSchema.For(Sources(sourceCount)).Schema!.Value;

        // Assert
        Assert.Empty(StrictSchemaSubset.ViolationsOf(schema));
    }

    [Fact]
    public void For_DeclaredSources_CitesTheirMintedNamesAndColumnsFromTheCatalogue()
    {
        // Act
        var schema = DiscoveryCompositionSchema.For(Sources(3)).Schema!.Value;

        // Assert
        var properties = schema.GetProperty("properties");

        Assert.Equal(["s1", "s2", "s3"], Enumerated(properties.GetProperty("sources").GetProperty("items")));
        Assert.Equal(["s1", "s2", "s3"], Enumerated(Items(properties, "rows").GetProperty("properties").GetProperty("cells").GetProperty("items").GetProperty("properties").GetProperty("sources").GetProperty("items")));
        Assert.Equal(FactTableColumn.All.Select(static column => column.Identity), Enumerated(properties.GetProperty("columns").GetProperty("items")));
        Assert.Equal(["high", "moderate", "low"], Enumerated(properties.GetProperty("confidence")));
    }

    /// <summary>A turn that declared no source has no name to enumerate, so a citation is a plain string the reading resolves to nothing.</summary>
    [Fact]
    public void For_NoDeclaredSource_CitesAPlainStringInstead()
    {
        // Act
        var schema = DiscoveryCompositionSchema.For([]).Schema!.Value;

        // Assert
        var cited = schema.GetProperty("properties").GetProperty("sources").GetProperty("items");

        Assert.False(cited.TryGetProperty("enum", out _));
    }

    /// <summary>
    /// Nothing the schema admits is withheld: a document citing a declared source composes the block its intent opens with,
    /// at either end of every enumeration, and the one citing nothing is the honest answer that the mail does not answer.
    /// </summary>
    [Theory]
    [MemberData(nameof(Intents))]
    public void Read_EveryDocumentTheSchemaAdmits_ComposesWhatItsIntentOpensWith(string intentIdentity)
    {
        // Arrange
        Assert.True(DiscoveryIntent.TryParse(intentIdentity, out var intent));

        var sources = Sources(3);
        var documents = StrictSchemaSamples.DocumentsAdmittedBy(DiscoveryCompositionSchema.For(sources).Schema!.Value, TextFor);

        // Act
        var openings = documents.Select(document => Read(document, intent, sources).Blocks[0]).ToArray();

        // Assert
        Assert.True(openings.Length > 1);
        Assert.Single(openings, static opening => opening.Evidence.Support is PresentationSupport.Unsupported);
        Assert.All(
            openings.Where(static opening => opening.Evidence.Support is not PresentationSupport.Unsupported),
            opening => Assert.Equal(OpeningBlockOf(intent), opening.GetType()));
    }

    /// <summary>
    /// Every answer a model wrote to this agent in the last evaluation run, replayed through the reading. There is one
    /// reading whether or not the request carried a schema, so this is the replay under both: an answer that reads and
    /// cites a source the run declared rests on it.
    /// </summary>
    [Fact]
    public void Read_EveryAnswerRecordedInTheLastEvaluationRun_RestsOnWhatItCites()
    {
        // Arrange
        var recorded = RecordedAnswers.Of("DiscoveryComposition");
        var sources = Sources(PresentationEvidence.MaxCitations);

        // Act
        var withheld = recorded
            .Where(answer => AgentJsonAnswer.Read(answer.Answer, DiscoveryResultJsonContext.Default.DiscoveryResultDocument) is not { } document
                || (document.Sources is [_, ..]
                    && Read(answer.Answer, DiscoveryIntent.FindFact, sources).Blocks[0].Evidence.Support is PresentationSupport.Unsupported))
            .Select(static answer => answer.Scenario);

        // Assert
        Assert.NotEmpty(recorded);
        Assert.Empty(withheld);
    }

    /// <summary>The intents whose opening block a model's material decides, by their published identities.</summary>
    public static TheoryData<string> Intents =>
    [
        DiscoveryIntent.FindFactIdentity,
        DiscoveryIntent.TrackChangeIdentity,
        DiscoveryIntent.CompareTermsIdentity,
    ];

    private static Type OpeningBlockOf(DiscoveryIntent intent) => intent.OpensWith.Identity switch
    {
        PresentationBlockType.TimelineIdentity => typeof(TimelineBlock),
        PresentationBlockType.FactTableIdentity => typeof(FactTableBlock),
        _ => typeof(AnswerBlock),
    };

    private static string? TextFor(string? member) => member switch
    {
        "occurredAt" => "2026-07-01T09:00:00+02:00",
        _ => null,
    };

    private static JsonElement Items(JsonElement properties, string member) => properties.GetProperty(member).GetProperty("items");

    private static string?[] Enumerated(JsonElement node) =>
        [.. node.GetProperty("enum").EnumerateArray().Select(static value => value.GetString())];

    private static PresentationPlan Read(string answerText, DiscoveryIntent intent, IReadOnlyList<DiscoveryComposedSource> sources) =>
        DiscoveryCompositionReading.Read(
            answerText,
            DiscoveryRunPlan.Compose(
                intent,
                RetrievalPlan.Create(EmailKnowledgeBounds.Default, [EmailKnowledgeQuery.ForText("quotation")], sufficientPassages: 5)),
            sources,
            new DiscoveryEvidence(
                [.. sources.Select(source => Passage(source.Extract))],
                EmailSearchRetrievalMode.Hybrid,
                LookupsRun: 1,
                LookupsRefused: 0,
                RetrievalTruncated: false),
            [new AccountCoverage(PresentationText.Create(Work.Value), PresentationFreshness.CurrentAt(ObservedAt), earliestReceivedAt: null, latestReceivedAt: null)],
            UserLanguage.English);

    private static IReadOnlyList<DiscoveryComposedSource> Sources(int count) =>
        DiscoveryComposedSources.Declare([.. Enumerable.Range(1, count).Select(static number => Passage($"the quoted price is {number} hundred"))]);

    private static EmailKnowledgePassage Passage(string text) => new()
    {
        StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
        AccountId = Work,
        Folder = new StoredEmailFolder(MailFolderId.Create(Guid.Parse("83ecb521-bf32-7f67-8287-e8b923809bb3")), "INBOX", MailFolderAlias.Create("INBOX")),
        Subject = null,
        ReceivedAt = null,
        Provenance = StubEmailProvenanceReader.KnownCorrespondent,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = text,
    };
}
