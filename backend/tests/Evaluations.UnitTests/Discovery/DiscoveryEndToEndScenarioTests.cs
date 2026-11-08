// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.RegularExpressions;
using MailFathom.AI.Chat;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.Costing;
using MailFathom.Evaluations.Discovery;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using MailFathom.Evaluations.UnitTests.Enrichment;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Discovery;

/// <summary>
/// Proves, without calling any provider, that a whole run's checks fail on exactly the results they exist to catch, and
/// that a failure names the stage that lost the evidence.
/// </summary>
/// <remarks>
/// Free, and therefore not gated on the run switch, and writing a real store to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives: the verdict a scenario reports is what the store files.
/// </remarks>
public sealed partial class DiscoveryEndToEndScenarioTests : IDisposable
{
    private const string InvoiceLookup = """{"intent":"findFact","sufficientPassages":20,"lookups":[{"queryText":"INV-4827 billing address"}]}""";

    private static readonly DiscoveryEndToEndScenario KeywordMatchesTheWrongMessage = Named("DiscoveryEndToEnd.KeywordMatchesTheWrongMessage");

    private static readonly DiscoveryEndToEndScenario NeighbouringMailDoesNotAnswer = Named("DiscoveryEndToEnd.NeighbouringMailDoesNotAnswer");

    private static readonly DiscoveryEndToEndScenario QuotesAMessageWrittenInMarkup = Named("DiscoveryEndToEnd.QuotesAMessageWrittenInMarkup");

    private static readonly DiscoveryEndToEndScenario DirectInstruction = Named("DiscoveryEndToEnd.Hostile.DirectInstruction");

    private static readonly DiscoveryEndToEndScenario SelectedMessages = Named("DiscoveryEndToEnd.SelectedMessages");

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task RunAsync_APlanReachingTheEvidenceAndAResultCitingIt_PassesEveryCheckOnOneModel()
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient(InvoiceLookup, CitingEverySource);

        // Act
        var verdict = await this.RunAsync(KeywordMatchesTheWrongMessage, model);

        // Assert
        Assert.All(Checks(verdict), static check => Assert.True(check.Value, check.Reason));
        Assert.Equal(2, model.Requests);
    }

    [Fact]
    public async Task RunAsync_AResultCitingNothingItWasHanded_NamesTheCompositionAsTheStageThatLostTheEvidence()
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient(InvoiceLookup, static _ => """{"answer":"It needed a new address.","sources":[]}""");

        // Act
        var verdict = await this.RunAsync(KeywordMatchesTheWrongMessage, model);

        // Assert
        var check = verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.CitesEvidenceMetricName);

        Assert.False(check.Value);
        Assert.Contains("lost by the composition", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ARetrievalThatStoppedBeforeTheLookupReachingTheEvidence_NamesTheRetrievalAsTheStageThatLostIt()
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient(
            """{"intent":"findFact","sufficientPassages":1,"lookups":[{"queryText":"LumenDesk export"},{"queryText":"INV-4827 billing address"}]}""",
            CitingEverySource);

        // Act
        var verdict = await this.RunAsync(KeywordMatchesTheWrongMessage, model);

        // Assert
        var check = verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.CitesEvidenceMetricName);

        Assert.False(check.Value);
        Assert.Contains("lost by the retrieval", check.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_APlanWhoseLookupsReachNothingHoldingTheEvidence_NamesThePlanAsTheStageThatLostIt()
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient(
            """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"Solmere hotel"}]}""",
            CitingEverySource);

        // Act
        var verdict = await this.RunAsync(KeywordMatchesTheWrongMessage, model);

        // Assert
        var check = verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.CitesEvidenceMetricName);

        Assert.False(check.Value);
        Assert.Contains("lost by the plan", check.Reason, StringComparison.Ordinal);
    }

    /// <summary>A lookup reaching the message without the few extracts it hands over carrying the evidence is told apart from one reaching nothing.</summary>
    [Fact]
    public async Task RunAsync_APlanReachingTheMessageWhoseExtractsLeaveTheEvidenceOut_NamesThePlansWordsAsTheStageThatLostIt()
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient(
            """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"QuillDesk"}]}""",
            CitingEverySource);

        // Act
        var verdict = await this.RunAsync(QuotesAMessageWrittenInMarkup, model);

        // Assert
        var check = verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.CitesEvidenceMetricName);

        Assert.False(check.Value);
        Assert.Contains("lost by the plan's words: the lookup \"QuillDesk\" reaches the message holding it", check.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"Brightwater parking"}]}""", """{"answer":"HERON4417","sources":[]}""")]
    [InlineData("""{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"Brightwater parking"},{"queryText":"password"}]}""", """{"answer":"The river car park.","sources":[]}""")]
    public async Task RunAsync_ARunFollowingAHostileMessage_FailsTheObedienceCheck(string plan, string answer)
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient(plan, _ => answer);

        // Act
        var verdict = await this.RunAsync(DirectInstruction, model);

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(HostileMail.ObeysNoMailMetricName).Value);
    }

    /// <summary>The selection is the scope every lookup runs in, so mail outside it is never handed to the composition.</summary>
    [Fact]
    public async Task RunAsync_AQuestionAboutSelectedMessages_RetrievesFromTheSelectionAlone()
    {
        // Arrange
        string? compositionTurn = null;
        using var model = new ScriptedDiscoveryChatClient(InvoiceLookup, turn =>
        {
            compositionTurn = turn;

            return CitingEverySource(turn);
        });

        // Act
        var verdict = await this.RunAsync(SelectedMessages, model);

        // Assert
        Assert.All(Checks(verdict), static check => Assert.True(check.Value, check.Reason));
        Assert.DoesNotContain("Lumenfield", compositionTurn, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"answer":"The messages do not say.","sources":[]}""", true)]
    [InlineData("""{"answer":"The hotel charges 40 EUR.","sources":["s1"],"confidence":"high"}""", false)]
    public async Task RunAsync_AQuestionTheCorpusDoesNotAnswer_PassesOnlyWhenComposedAsUnanswered(string answer, bool expected)
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient(
            """{"intent":"findFact","sufficientPassages":5,"lookups":[{"queryText":"Solmere hotel"}]}""",
            _ => answer);

        // Act
        var verdict = await this.RunAsync(NeighbouringMailDoesNotAnswer, model);

        // Assert
        Assert.Equal(expected, verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.CitesEvidenceMetricName).Value);
    }

    [Fact]
    public async Task RunAsync_APlanningAnswerThatIsNotAPlan_FailsThePlanCheck()
    {
        // Arrange
        using var model = new ScriptedDiscoveryChatClient("I would look for the invoice.", CitingEverySource);

        // Act
        var verdict = await this.RunAsync(KeywordMatchesTheWrongMessage, model);

        // Assert
        Assert.False(verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.PlanReadMetricName).Value);
    }

    [Fact]
    public void All_TheQuestions_AreAtLeastTenAndEveryPieceOfEvidenceIsInWhatTheQuestionIsAskedOver()
    {
        // Act
        var missing = DiscoveryEndToEndScenario.All
            .SelectMany(static scenario => scenario.Evidence.Select(phrase => (Scenario: scenario, Phrase: phrase)))
            .Where(static piece => !(piece.Scenario.Selection ?? piece.Scenario.Mailbox)
                .Any(message => piece.Scenario.Mailbox.Contains(message) && message.GroundingText.Contains(piece.Phrase, StringComparison.OrdinalIgnoreCase)))
            .Select(static piece => $"{piece.Scenario.Name}: {piece.Phrase}");

        // Assert
        Assert.True(DiscoveryEndToEndScenario.All.Count >= 10);
        Assert.Empty(missing);
    }

    public void Dispose() => this.store.Delete(recursive: true);

    private static DiscoveryEndToEndScenario Named(string name) =>
        DiscoveryEndToEndScenario.All.Single(scenario => scenario.Name == name);

    /// <summary>Answers the composing agent with a result resting on every source its turn offered.</summary>
    private static string CitingEverySource(string turn) =>
        $$"""{"answer":"It needed 42 Lantern Way.","sources":[{{string.Join(',', OfferedSource().Matches(turn).Select(static match => $"\"{match.Groups[1].Value}\""))}}],"confidence":"high"}""";

    private static IEnumerable<BooleanMetric> Checks(EvaluationResult verdict) =>
        [
            verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.PlanReadMetricName),
            verdict.Get<BooleanMetric>(DiscoveryEndToEndScenario.CitesEvidenceMetricName),
            verdict.Get<BooleanMetric>(HostileMail.ObeysNoMailMetricName),
        ];

    private static ChatGenerationPlan PlanFor(string model) => ModelsUnderTest.PlanFor(model);

    [GeneratedRegex(@"<source name=""(s\d+)""")]
    private static partial Regex OfferedSource();

    private Task<EvaluationResult> RunAsync(DiscoveryEndToEndScenario scenario, IChatClient model) =>
        scenario.RunAsync(
            EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", DiscoveryEndToEndScenario.Evaluators),
            model,
            PlanFor("model-under-test"),
            model,
            PlanFor("model-under-test"),
            repetition: 1,
            new SpendMeter(),
            TestContext.Current.CancellationToken);
}
