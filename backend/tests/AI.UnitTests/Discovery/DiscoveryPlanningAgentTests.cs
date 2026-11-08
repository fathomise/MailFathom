// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.Chat;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Discovery;

/// <summary>Covers what one derivation sends, what it makes of the answer, and what it does when there is none.</summary>
/// <remarks>
/// The derivation goes over a real provider client and a scripted transport, so what is exercised is the client
/// construction, the credential resolution, and the turn that actually left this deployment.
/// </remarks>
public sealed class DiscoveryPlanningAgentTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in a question.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    /// <summary>A plan as a model held to the schema writes it, every filter written and null where it does not apply.</summary>
    private const string StructuredPlan = """
        {"intent": "compareTerms", "sufficientPassages": 6, "lookups": [{"queryText": "quotation", "senderAddress": null,
        "recipientAddress": null, "subjectFragment": null, "receivedOnOrAfter": null, "receivedBefore": null,
        "isRemotelySeen": null, "isRemotelyFlagged": null, "keyword": null, "hasAttachments": null}]}
        """;

    private static readonly MailboxScope WholeMailbox = MailboxScope.Create(
        [MailAccountId.Create("primary")],
        []);

    [Fact]
    public async Task DerivePlanAsync_AProviderThatAnsweredWithAPlan_DerivesThatPlan()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"compareTerms\", \"sufficientPassages\": 6, \"lookups\": [{\"queryText\": \"quotation\"}]}"""));
        var planner = provider.PlannerOver();

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.CompareTerms, plan.Intent);
        Assert.Equal(["quotation"], plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
        Assert.Equal(6, plan.Retrieval.SufficientPassages);
    }

    /// <summary>An answer nothing can be read out of leaves a run with the plan a deployment with no model would run.</summary>
    [Fact]
    public async Task DerivePlanAsync_AProviderThatAnsweredWithProse_FallsBackToTheQuestionsOwnWords()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("I am afraid I cannot help with that."));
        var planner = provider.PlannerOver();

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.Unclassified, plan.Intent);
        Assert.Equal(
            ["which supplier quoted least"],
            plan.Retrieval.Lookups.Select(lookup => lookup.QueryText));
    }

    /// <summary>A provider that refused the call is a worse plan rather than no answer, so the run goes on.</summary>
    [Fact]
    public async Task DerivePlanAsync_AProviderThatRefusedTheCall_StillProducesARunnablePlan()
    {
        // Arrange
        using var provider = ScriptedTransport.Refusing(HttpStatusCode.BadRequest);
        var planner = provider.PlannerOver();

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.Unclassified, plan.Intent);
        Assert.Single(plan.Retrieval.Lookups);
    }

    /// <summary>A key that would not resolve never reaches the endpoint, and is the same worse plan a refusal is.</summary>
    [Fact]
    public async Task DerivePlanAsync_ACredentialThatCannotBeResolved_StillProducesARunnablePlan()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}"""));
        var planner = provider.PlannerOver(credentialFailure: new InvalidOperationException(
            "The provider key of AI endpoint 'a-chat-endpoint' could not be resolved."));

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DiscoveryIntent.Unclassified, plan.Intent);
        Assert.Single(plan.Retrieval.Lookups);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A question is a prompt somebody wrote, so what a deployment withholds from a provider is withheld here too.</summary>
    [Fact]
    public async Task DerivePlanAsync_AQuestionCarryingASecret_SendsTheProviderTheGuardedText()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"key\"}]}"""));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.Guard.ActingFor(await egress.ReadUserPostureAsync(TestContext.Current.CancellationToken));
        var planner = provider.PlannerOver(egressGuard: egress.Guard);
        var question = new MailQuestion(
            MailQuestionText.Create($"what do I do about the key {Marker} a colleague sent"),
            WholeMailbox,
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2)));

        // Act
        await planner.DerivePlanAsync(question, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>The scope reaches the model as how much was selected, never as an identifier of what.</summary>
    [Fact]
    public async Task DerivePlanAsync_AQuestionAboutSelectedMessages_TellsTheModelHowManyAndNothingThatNamesThem()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}"""));
        var planner = provider.PlannerOver();
        var selected = StoredEmailId.Create(new Guid("99999999-9999-9999-9999-999999999999"));
        var question = new MailQuestion(
            MailQuestionText.Create("which supplier quoted least"),
            WholeMailbox.NarrowedToEmails([selected]),
            new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2)));

        // Act
        await planner.DerivePlanAsync(question, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("1 individually selected messages", provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain(selected.Value.ToString(), provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>No mail is shown to a derivation, so one call is the whole of what a question costs here.</summary>
    [Fact]
    public async Task DerivePlanAsync_AnyQuestion_ReachesTheProviderExactlyOnce()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}"""));
        var planner = provider.PlannerOver();

        // Act
        await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.RequestCount);
    }

    /// <summary>What a derivation cost is charged to the run that asked, which is what the run then reports having spent.</summary>
    [Fact]
    public async Task DerivePlanAsync_AProviderThatReportedItsUsage_ChargesTheCallToTheRunAndToThePeriod()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}""",
            inputTokens: 11,
            outputTokens: 7));
        var runLedger = new MailAnsweringRunLedger(MailAnsweringRunBounds.Default);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var planner = provider.PlannerOver(runLedger: runLedger, spendLedger: spendLedger);

        // Act
        await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new MailAnsweringRunSpend(1, 18, 0, 0), runLedger.Read());
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(11, 7), Arg.Any<CancellationToken>());
    }

    /// <summary>A run with no allowance left for another call is refused before anything leaves the deployment.</summary>
    /// <remarks>
    /// The one exception to the derivation's never-fail fallback: every other failure leaves a worse plan and the run
    /// goes on, while a spend ceiling is what the run is being stopped by and must reach the person who asked.
    /// </remarks>
    [Fact]
    public async Task DerivePlanAsync_ARunThatHasSpentItsCallAllowance_RefusesBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"findFact\", \"lookups\": [{\"queryText\": \"invoice\"}]}"""));
        var runLedger = new MailAnsweringRunLedger(MailAnsweringRunBounds.Create(20_000, maximumProviderCalls: 1, 80_000));
        runLedger.RequireAllowanceForNextCall();
        var planner = provider.PlannerOver(runLedger: runLedger);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(() =>
            planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, refusal.Scope);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>The follow-up adds only lookups the plan did not run, and is charged to the run and the period like the plan's own call.</summary>
    [Fact]
    public async Task FollowUpAsync_AProviderAnsweringWithFurtherLookups_AddsTheOnesNotRunAndChargesTheCall()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion(
            """{\"intent\": \"compareTerms\", \"lookups\": [{\"queryText\": \"invoice\"}, {\"queryText\": \"RX-58213\"}]}""",
            inputTokens: 11,
            outputTokens: 7));
        var runLedger = new MailAnsweringRunLedger(MailAnsweringRunBounds.Default);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var planner = provider.PlannerOver(runLedger: runLedger, spendLedger: spendLedger);

        // Act
        var lookups = await planner.FollowUpAsync(Question(), PlanOf("invoice"), EvidenceOf("the invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["RX-58213"], lookups.Select(lookup => lookup.QueryText));
        Assert.Equal(new MailAnsweringRunSpend(1, 18, 0, 0), runLedger.Read());
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(11, 7), Arg.Any<CancellationToken>());
    }

    /// <summary>The follow-up is the planning call shown mail, so what a deployment withholds from a provider is withheld from the extracts too.</summary>
    [Fact]
    public async Task FollowUpAsync_AnExtractCarryingASecret_SendsTheProviderTheGuardedExtract()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"lookups\": []}"""));
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        using var actingFor = egress.Guard.ActingFor(await egress.ReadUserPostureAsync(TestContext.Current.CancellationToken));
        var planner = provider.PlannerOver(egressGuard: egress.Guard);

        // Act
        await planner.FollowUpAsync(Question(), PlanOf("invoice"), EvidenceOf($"the key is {Marker}"), TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(Marker, provider.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains(DiscoveryPlanningInstructions.FollowUpOpening, provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>A follow-up the provider could not answer adds nothing rather than failing a run that already holds what the plan found.</summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, null)]
    [InlineData(HttpStatusCode.OK, "Search for the delivery date next.")]
    public async Task FollowUpAsync_AFailedOrUnreadableAnswer_AddsNoLookup(HttpStatusCode status, string? content)
    {
        // Arrange
        using var provider = content is null ? ScriptedTransport.Refusing(status) : ScriptedTransport.Answering(Completion(content));
        var planner = provider.PlannerOver();

        // Act
        var lookups = await planner.FollowUpAsync(Question(), PlanOf("invoice"), EvidenceOf("the invoice"), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(lookups);
    }

    /// <summary>A follow-up turn past the model's bound is never sent, and adds nothing rather than failing a run that already holds what the plan found.</summary>
    [Fact]
    public async Task FollowUpAsync_ATurnTooLargeForOneRequest_AddsNoLookupWithoutReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"lookups\": [{\"queryText\": \"RX-58213\"}]}"""));
        var planner = provider.PlannerOver(plan: ChatDeclarations.Plan(maximumRequestCharacters: 100));

        // Act
        var lookups = await planner.FollowUpAsync(Question(), PlanOf("invoice"), EvidenceOf(new string('x', 400)), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(lookups);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A run that spent its calls on the plan and the composition's share is refused the follow-up before anything leaves.</summary>
    [Fact]
    public async Task FollowUpAsync_ARunThatHasSpentItsCallAllowance_RefusesBeforeReachingTheProvider()
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(Completion("""{\"lookups\": []}"""));
        var runLedger = new MailAnsweringRunLedger(MailAnsweringRunBounds.Create(20_000, maximumProviderCalls: 1, 80_000));
        runLedger.RequireAllowanceForNextCall();
        var planner = provider.PlannerOver(runLedger: runLedger);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(() =>
            planner.FollowUpAsync(Question(), PlanOf("invoice"), EvidenceOf("the invoice"), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, refusal.Scope);
        Assert.Equal(0, provider.RequestCount);
    }

    /// <summary>A model whose server honours a strict schema is asked for one, its intents the catalogue's own.</summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions)]
    [InlineData(ChatProviderApi.Responses)]
    public async Task DerivePlanAsync_AModelDeclaringStructuredOutputs_AsksForAStrictSchemaOfTheCataloguesIntents(ChatProviderApi api)
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(StructuredRequests.AnswerOver(api, StructuredPlan));
        var planner = provider.PlannerOver(plan: ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: api), structuredOutputs: true));

        // Act
        var plan = await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        var format = StructuredRequests.SchemaFormatOf(provider.RequestBodies[0], api);
        var intents = format.Schema.GetProperty("properties").GetProperty("intent").GetProperty("enum");

        Assert.Equal(DiscoveryIntent.CompareTerms, plan.Intent);
        Assert.Equal(["quotation"], plan.Retrieval.Lookups.Select(static lookup => lookup.QueryText));
        Assert.Equal(("json_schema", DiscoveryPlanningSchema.Name, true), (format.Type, format.Name, format.Strict));
        Assert.Equal(DiscoveryIntent.All.Select(static intent => intent.Identity), intents.EnumerateArray().Select(static intent => intent.GetString()));
        Assert.DoesNotContain("whose only required field is", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    /// <summary>Without the declaration the request is the one every derivation sent before, the shape told in prose instead.</summary>
    [Theory]
    [InlineData(ChatProviderApi.ChatCompletions)]
    [InlineData(ChatProviderApi.Responses)]
    public async Task DerivePlanAsync_AModelDeclaringNoStructuredOutputs_SendsNoResponseFormat(ChatProviderApi api)
    {
        // Arrange
        using var provider = ScriptedTransport.Answering(StructuredRequests.AnswerOver(api, StructuredPlan));
        var planner = provider.PlannerOver(plan: ChatDeclarations.Plan(ChatDeclarations.Endpoint(api: api)));

        // Act
        await planner.DerivePlanAsync(Question(), TestContext.Current.CancellationToken);

        // Assert
        Assert.False(StructuredRequests.CarriesResponseFormat(provider.RequestBodies[0]));
        Assert.Contains("whose only required field is", provider.RequestBodies[0], StringComparison.Ordinal);
    }

    private static DiscoveryRunPlan PlanOf(params string[] queries) => DiscoveryRunPlan.Compose(
        DiscoveryIntent.FindFact,
        RetrievalPlan.Create(EmailKnowledgeBounds.Default, [.. queries.Select(EmailKnowledgeQuery.ForText)], sufficientPassages: 5));

    private static DiscoveryEvidence EvidenceOf(string text) => new(
        [
            new EmailKnowledgePassage
            {
                StoredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
                AccountId = MailAccountId.Create("primary"),
                Folder = new StoredEmailFolder(MailFolderId.Create(Guid.Parse("83ecb521-bf32-7f67-8287-e8b923809bb3")), "INBOX", MailFolderAlias.Create("INBOX")),
                Subject = "Invoice",
                ReceivedAt = null,
                Provenance = StubEmailProvenanceReader.KnownCorrespondent,
                MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
                Text = text,
            },
        ],
        EmailSearchRetrievalMode.Lexical,
        LookupsRun: 1,
        LookupsRefused: 0,
        RetrievalTruncated: false);

    private static MailQuestion Question() =>
        new(MailQuestionText.Create("which supplier quoted least"), WholeMailbox, new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2)));

    /// <summary>Builds the chat-completion payload a provider answers with.</summary>
    private static string Completion(string content, int? inputTokens = null, int? outputTokens = null) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\""
        + content
        + "\"},\"finish_reason\":\"stop\"}]"
        + (inputTokens is null
            ? string.Empty
            : $",\"usage\":{{\"prompt_tokens\":{inputTokens},\"completion_tokens\":{outputTokens},"
              + $"\"total_tokens\":{inputTokens + outputTokens}}}")
        + "}";

    /// <summary>A provider that answers from a script, so the derivation is exercised over a real client and no network.</summary>
    private sealed class ScriptedTransport : IDisposable
    {
        private readonly FakeHttpMessageHandler handler;
        private string payload = string.Empty;
        private HttpStatusCode status = HttpStatusCode.OK;

        private ScriptedTransport() =>
            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                this.RequestCount++;
                this.RequestBodies.Add(request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return new HttpResponseMessage(this.status)
                {
                    Content = new StringContent(this.payload, Encoding.UTF8, "application/json"),
                };
            });

        public int RequestCount { get; private set; }

        /// <summary>What the provider was actually sent, which is where a test reads what left this deployment.</summary>
        public List<string> RequestBodies { get; } = [];

        public static ScriptedTransport Answering(string payload) => new() { payload = payload };

        public static ScriptedTransport Refusing(HttpStatusCode status) =>
            new() { status = status, payload = "{\"error\":{\"message\":\"no\"}}" };

        public DiscoveryPlanningAgent PlannerOver(
            ChatGenerationPlan? plan = null,
            SensitiveContentEgressGuard? egressGuard = null,
            Exception? credentialFailure = null,
            MailAnsweringRunLedger? runLedger = null,
            IMailAnsweringSpendLedger? spendLedger = null)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => credentialFailure is null
                    ? Task.FromResult(ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null))
                    : Task.FromException<ProviderEndpointCredential>(credentialFailure));

            var operationRunner = Substitute.For<IOutboundOperationRunner>();
            operationRunner
                .RunAsync(
                    Arg.Any<OutboundDependency>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    // The substitute cannot know the argument is present, and it always is: this configuration matches
                    // the only overload the decorator calls.
                    var operation = call.Arg<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>()!;

                    return operation(call.Arg<CancellationToken>());
                });

            return new DiscoveryPlanningAgent(
                plan ?? ChatDeclarations.Plan(),
                EmailKnowledgeBounds.Default,
                runLedger ?? new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
                spendLedger ?? Substitute.For<IMailAnsweringSpendLedger>(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                egressGuard ?? SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance,
                NullLogger<DiscoveryPlanningAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
