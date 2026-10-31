// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.AI.Chat;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using MailFathom.Evaluations.Providers;
using MailFathom.Evaluations.Reporting;
using MailFathom.Evaluations.UnitTests.Enrichment;
using MailFathom.Evaluations.UnitTests.StructuredAnswers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.Reporting;

/// <summary>Proves an answer is read back only under the request it was given for, and that each answer is tallied by where it came from.</summary>
/// <remarks>
/// Free: the model is scripted, and the store is written to a temporary directory for the reason
/// <see cref="EmailEnrichmentScenarioTests" /> gives, since what a later run reads back is what the store kept.
/// </remarks>
public sealed class EvaluationStoreTests : IDisposable
{
    private const string Held = "Scenario.Held";

    private const string SearchSchema = """{"type":"object","properties":{"query":{"type":"string"}}}""";

    private readonly DirectoryInfo store = Directory.CreateTempSubdirectory("mailfathom-evaluations-");

    [Fact]
    public async Task CacheOverAsync_AQuestionAskedTwiceInOneRun_TalliesOneAnswerAskedAfreshAndOneReusedWithinTheRun()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var iterationName = EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1);

        // Act
        await ReachesTheModelAsync(reporting, Held);
        await ReachesTheModelAsync(reporting, Held);
        var tally = EvaluationStore.TallyOf(reporting, Held, iterationName);

        // Assert
        Assert.Equal((0, 1, 1), (tally.Replayed, tally.Reused, tally.Asked));
    }

    [Fact]
    public async Task CacheOverAsync_AnswersAskedThroughSeveralHandlesOnOneRun_TalliesThemTogetherUnderEveryHandle()
    {
        // Arrange
        var asking = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var askingAgain = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var reading = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var iterationName = EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1);

        // Act
        await ReachesTheModelAsync(asking, Held);
        await ReachesTheModelAsync(askingAgain, Held);
        var tally = EvaluationStore.TallyOf(reading, Held, iterationName);

        // Assert
        Assert.Equal((0, 1, 1), (tally.Replayed, tally.Reused, tally.Asked));
    }

    [Fact]
    public async Task CacheOverAsync_AQuestionAnEarlierRunAsked_TalliesTheAnswerAsReplayed()
    {
        // Arrange
        await ReachesTheModelAsync(EvaluationStore.OpenUnjudgedAt(this.store.FullName, "first", []), Held);
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "second", []);
        var iterationName = EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1);

        // Act
        await ReachesTheModelAsync(reporting, Held);
        await ReachesTheModelAsync(reporting, Held);
        var tally = EvaluationStore.TallyOf(reporting, Held, iterationName);

        // Assert
        Assert.Equal((2, 0, 0), (tally.Replayed, tally.Reused, tally.Asked));
    }

    [Fact]
    public async Task CacheOverAsync_ARunDeclaringAReasoningEffort_AsksAgainRatherThanReadingTheAnswerCachedWithoutOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var withoutEffort = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest);
        var withEffort = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest, "high");

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, withoutEffort),
            await ReachesTheModelAsync(reporting, Held, withEffort),
            await ReachesTheModelAsync(reporting, Held, withEffort),
            await ReachesTheModelAsync(reporting, Held, withoutEffort),
        ];

        // Assert
        Assert.Equal([true, true, false, false], reached);
    }

    [Fact]
    public async Task CacheOverAsync_ARunDeclaringAnAdditionalRequestMember_AsksAgainRatherThanReadingTheAnswerCachedWithoutOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var withoutMember = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest);
        var withMember = Assert.Single(EvaluationDeclaration.Parse(
            $"{{MainModel: {{Model: {ScriptedStructuredAnswerRun.ModelUnderTest}, AdditionalProperties: {{top_k: 40}}}}}}",
            fallbackMainModel: null,
            address: null).ModelsFor(ChatCapability.Enrichment)).Plan;

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, withoutMember),
            await ReachesTheModelAsync(reporting, Held, withMember),
            await ReachesTheModelAsync(reporting, Held, withMember),
            await ReachesTheModelAsync(reporting, Held, withoutMember),
        ];

        // Assert
        Assert.Equal([true, true, false, false], reached);
    }

    [Fact]
    public async Task CacheOverAsync_ARunDeclaringAnotherRepetitionCount_AsksAgainRatherThanReadingTheAnswerThatCountPaidFor()
    {
        // Arrange
        var declaringTwo = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "two", [], repetitions: 2);
        var declaringThree = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "three", [], repetitions: 3);
        var declaringOne = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "one", [], repetitions: 1);

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(declaringTwo, Held),
            await ReachesTheModelAsync(declaringThree, Held),
            await ReachesTheModelAsync(declaringThree, Held),
            await ReachesTheModelAsync(declaringTwo, Held),
            await ReachesTheModelAsync(declaringOne, Held),
        ];

        // Assert
        Assert.Equal([true, true, false, false, true], reached);
    }

    [Fact]
    public async Task CacheOverAsync_TheSameRequestUnderAnotherAlias_ReadsBackTheAnswerRatherThanAskingAgain()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var unnamed = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest);
        var renamed = Assert.Single(EvaluationDeclaration.Parse(
            $"{{MainModel: {{Model: {ScriptedStructuredAnswerRun.ModelUnderTest}, Alias: renamed}}}}",
            fallbackMainModel: null,
            address: null).ModelsFor(ChatCapability.Enrichment)).Plan;

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, unnamed),
            await ReachesTheModelAsync(reporting, Held, renamed),
        ];

        // Assert
        Assert.Equal([true, false], reached);
    }

    [Fact]
    public async Task CacheOverAsync_TheSameRequestUnderAnotherAliasInOneRun_TalliesTheAliasReadingItBackAsReused()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var unnamed = ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest);
        var renamed = Assert.Single(EvaluationDeclaration.Parse(
            $"{{MainModel: {{Model: {ScriptedStructuredAnswerRun.ModelUnderTest}, Alias: renamed}}}}",
            fallbackMainModel: null,
            address: null).ModelsFor(ChatCapability.Enrichment)).Plan;

        // Act
        await ReachesTheModelAsync(reporting, Held, unnamed);
        await ReachesTheModelAsync(reporting, Held, renamed);
        var tally = EvaluationStore.TallyOf(reporting, Held, EvaluationStore.IterationNameFor(renamed.Endpoint.Alias, repetition: 1));

        // Assert
        Assert.Equal((0, 1, 0), (tally.Replayed, tally.Reused, tally.Asked));
    }

    [Fact]
    public async Task CacheOverAsync_AToolWhoseDescriptionChanged_AsksTheModelAgainRatherThanReadingTheAnswerGivenUnderTheOldOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        AITool[] before = [SearchTool("Finds mail.", SearchSchema)];
        AITool[] after = [SearchTool("Finds mail by its sender.", SearchSchema)];

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: after),
        ];

        // Assert
        Assert.Equal([true, false, true], reached);
    }

    [Fact]
    public async Task CacheOverAsync_AToolWhoseNameChanged_AsksTheModelAgainRatherThanReadingTheAnswerGivenUnderTheOldOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        AITool[] before = [SearchTool("Finds mail.", SearchSchema)];
        AITool[] after = [SearchTool("Finds mail.", SearchSchema, name: "find_messages")];

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: after),
        ];

        // Assert
        Assert.Equal([true, false, true], reached);
    }

    [Fact]
    public async Task CacheOverAsync_AToolWhoseParameterSchemaChanged_AsksTheModelAgainRatherThanReadingTheAnswerGivenUnderTheOldOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        AITool[] before = [SearchTool("Finds mail.", SearchSchema)];
        AITool[] after =
        [
            SearchTool("Finds mail.", """{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer"}}}"""),
        ];

        // Act
        bool[] reached =
        [
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: before),
            await ReachesTheModelAsync(reporting, Held, tools: after),
        ];

        // Assert
        Assert.Equal([true, false, true], reached);
    }

    [Fact]
    public async Task CacheOverAsync_ARequestOfferedNoTools_ReadsBackTheAnswerCachedUnderTheKeyItWasFiledUnderBefore()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var iterationName = EvaluationStore.IterationNameFor(ScriptedStructuredAnswerRun.ModelUnderTest, repetition: 1);
        var cache = await reporting.ResponseCacheProvider!.GetCacheAsync(Held, iterationName, TestContext.Current.CancellationToken);
        using var earlier = ScriptedStructuredAnswerRun.Model("{}");
        using var filedBefore = new DistributedCachingChatClient(earlier, cache)
        {
            CacheKeyAdditionalValues = [ScriptedStructuredAnswerRun.ModelUnderTest, string.Empty],
        };
        await filedBefore.GetResponseAsync([Question], cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var reached = await ReachesTheModelAsync(reporting, Held);

        // Assert
        Assert.False(reached);
    }

    /// <summary>A decision is filed under the questions it answered, so rewording one asks the model again.</summary>
    [Fact]
    public async Task CacheDecisionsOverAsync_AChangedQuestion_AsksTheModelAgainRatherThanReadingTheAnswerGivenToTheOldOne()
    {
        // Arrange
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        DecisionQuestion[] asked = [DecisionQuestion.YesNo("spam", "Is this spam?")];
        DecisionQuestion[] reworded = [DecisionQuestion.YesNo("spam", "Would the recipient call this spam?")];

        // Act
        bool[] reached =
        [
            await DecisionReachesTheModelAsync(reporting, asked),
            await DecisionReachesTheModelAsync(reporting, asked),
            await DecisionReachesTheModelAsync(reporting, reworded),
        ];

        // Assert
        Assert.Equal([true, false, true], reached);
    }

    /// <summary>
    /// The model, the address it is reached at, and the run's repetition count each decide which answer a key stands for,
    /// so changing any of them asks the model again rather than reading back what another configuration answered.
    /// </summary>
    [Fact]
    public async Task CacheDecisionsOverAsync_AChangedModelAddressOrRepetitionCount_AsksTheModelAgain()
    {
        // Arrange
        DecisionQuestion[] asked = [DecisionQuestion.YesNo("spam", "Is this spam?")];
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "only", []);
        var declaringTwo = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "two", [], repetitions: 2);
        var plan = DeciderPlan();

        // Act
        bool[] reached =
        [
            await DecisionReachesTheModelAsync(reporting, asked, plan),
            await DecisionReachesTheModelAsync(reporting, asked, plan with { Model = "vendor/decider-2026-11-01" }),
            await DecisionReachesTheModelAsync(reporting, asked, plan with { Address = new Uri("https://eu.openrouter.ai") }),
            await DecisionReachesTheModelAsync(declaringTwo, asked, plan),
            await DecisionReachesTheModelAsync(reporting, asked, plan),
        ];

        // Assert
        Assert.Equal([true, true, true, true, false], reached);
    }

    /// <summary>A decision an earlier run paid for is read back and tallied the way a chat answer is.</summary>
    [Fact]
    public async Task CacheDecisionsOverAsync_ADecisionAnEarlierRunAsked_TalliesItAsReplayed()
    {
        // Arrange
        DecisionQuestion[] asked = [DecisionQuestion.YesNo("spam", "Is this spam?")];
        await DecisionReachesTheModelAsync(EvaluationStore.OpenUnjudgedAt(this.store.FullName, "first", []), asked);
        var reporting = EvaluationStore.OpenUnjudgedAt(this.store.FullName, "second", []);

        // Act
        var reached = await DecisionReachesTheModelAsync(reporting, asked);
        var tally = EvaluationStore.TallyOf(reporting, Held, "decider");

        // Assert
        Assert.False(reached);
        Assert.Equal((1, 0, 0), (tally.Replayed, tally.Reused, tally.Asked));
    }

    [Theory]
    [InlineData("Agent.Answers.Polish.PromisedPaymentDay", "family:Agent", "mail:polish")]
    [InlineData("MailAnswering.Mixed.EnglishQuestionAboutPolishMail", "family:MailAnswering", "mail:mixed-language")]
    [InlineData("Agent.Calendar.Hostile.EntryTitle", "family:Agent", "mail:hostile")]
    [InlineData("DiscoveryComposition.FalseClaim.ChangedBankDetails", "family:DiscoveryComposition", "mail:false-claim")]
    [InlineData("Retrieval.PostgreSQL.Hybrid", "family:Retrieval", null)]
    [InlineData("SpamClassification", "family:SpamClassification", null)]
    public void TagsFor_AScenarioName_NamesTheModelTheFamilyAndTheKindOfMailItsSegmentsCarry(
        string scenarioName,
        string family,
        string? kindOfMail)
    {
        // Arrange
        string[] expected = kindOfMail is null ? ["model:vendor/model-2026", family] : ["model:vendor/model-2026", family, kindOfMail];

        // Act
        var tags = EvaluationStore.TagsFor(scenarioName, "vendor/model-2026");

        // Assert
        Assert.Equal(expected, tags);
    }

    /// <inheritdoc />
    public void Dispose() => this.store.Delete(recursive: true);

    /// <summary>Asks one decision through the run's cache and says whether it reached the model.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the model, which this method already disposes.")]
    private static async Task<bool> DecisionReachesTheModelAsync(
        ReportingConfiguration reporting,
        DecisionQuestion[] questions,
        DecisionModelPlan? plan = null)
    {
        using var model = new CountingDecisionClient();
        var cached = await EvaluationStore.CacheDecisionsOverAsync(
            reporting,
            model,
            plan ?? DeciderPlan(),
            Held,
            "decider",
            repetition: 1,
            TestContext.Current.CancellationToken);

        await cached.DecideAsync([Question], questions, cancellationToken: TestContext.Current.CancellationToken);

        return model.Requests > 0;
    }

    private static DecisionModelPlan DeciderPlan() =>
        new("decider", Address: null, "vendor/decider-2026-10-01", TimeSpan.FromSeconds(30), 32_000);

    /// <summary>Asks one scenario through the run's cache and says whether the question reached the model.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Disposing the caching wrapper would dispose the model, which this method already disposes.")]
    private static async Task<bool> ReachesTheModelAsync(
        ReportingConfiguration reporting,
        string scenarioName,
        ChatGenerationPlan? plan = null,
        AITool[]? tools = null)
    {
        using var model = ScriptedStructuredAnswerRun.Model("{}");
        var asked = plan ?? ScriptedStructuredAnswerRun.PlanFor(ScriptedStructuredAnswerRun.ModelUnderTest);
        var cached = await EvaluationStore.CacheOverAsync(
            reporting,
            model,
            asked,
            scenarioName,
            EvaluationStore.IterationNameFor(asked.Endpoint.Alias, repetition: 1),
            repetition: 1,
            TestContext.Current.CancellationToken);

        await cached.GetResponseAsync(
            [Question],
            tools is null ? null : new ChatOptions { Tools = tools },
            TestContext.Current.CancellationToken);

        return model.Requests > 0;
    }

    private static ChatMessage Question => new(ChatRole.User, "What is this about?");

    private static AIFunctionDeclaration SearchTool(string description, string schema, string name = "search_mail") =>
        AIFunctionFactory.CreateDeclaration(name, description, JsonElement.Parse(schema));

    /// <summary>A decision model answering yes to everything, counting how often it was asked.</summary>
    private sealed class CountingDecisionClient : IDecisionClient
    {
        public int Requests { get; private set; }

        public Task<DecisionResponse> DecideAsync(
            IEnumerable<ChatMessage> input,
            IReadOnlyList<DecisionQuestion> questions,
            DecisionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            this.Requests++;

            return Task.FromResult(new DecisionResponse(
                [.. questions.Select(question => new DecisionAnswer(
                    question.Id,
                    DecisionQuestion.Yes,
                    new Dictionary<string, double> { [DecisionQuestion.Yes] = 1, [DecisionQuestion.No] = 0 }))]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing is held.
        }
    }
}
