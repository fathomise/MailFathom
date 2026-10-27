// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using MailFathom.AI.AgentConversations;
using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.Retrieval;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers which model's suggestions an answer carries once the composition has run over a real provider client.</summary>
/// <remarks>
/// The provider is a scripted transport, so what is exercised is the fall-through between two declared models and the
/// tools one run shares across both of them — which is where a failed model's suggestions would otherwise survive.
/// </remarks>
public sealed class AgentConversationAgentTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private readonly FakeTimeProvider clock = new(Now);

    private readonly AgentConversationId conversation = AgentConversationId.New();

    private readonly AgentMessageId answer = AgentMessageId.New();

    private readonly IAgentConversationStore store = Substitute.For<IAgentConversationStore>();

    private readonly ClientSignals signals;

    private readonly AgentAnswerJournal journal;

    /// <summary>Arranges a conversation that accepts every write and is still composing whenever it is read.</summary>
    public AgentConversationAgentTests()
    {
        var written = 1L;
        this.store
            .AppendAsync(this.conversation, SyntheticUser.Deployment, Arg.Any<AgentConversationEntry>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => (long?)++written);
        this.store
            .ReadAsync(this.conversation, SyntheticUser.Deployment, AgentConversationHistory.Visible, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AgentConversationReading(Title: null, Now, Composing: true, [], MoreFollows: false));

        this.signals = new ClientSignals([new RecordingClientSignalChannel()], this.clock);
        this.journal = new AgentAnswerJournal(
            this.conversation,
            SyntheticUser.Deployment,
            this.answer,
            openedAt: 1,
            this.store,
            this.signals,
            Substitute.For<IUserLanguages>(),
            this.clock);
    }

    [Fact]
    public async Task ComposeAsync_AModelSuggestingFollowUpsAndAnswering_ReturnsWhatItSuggested()
    {
        // Arrange
        using var provider = new ScriptedProvider(
            answering: [Suggesting("Who confirmed the bays?"), Answering("Two bays were confirmed.")],
            standby: []);

        // Act
        var followUps = await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000))
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Who confirmed the bays?"], followUps.Select(static followUp => followUp.Value));
    }

    [Fact]
    public async Task ComposeAsync_AModelThatSuggestedAndThenFailedOver_LeavesNothingForTheModelThatAnswered()
    {
        // Arrange
        using var provider = new ScriptedProvider(
            answering: [Suggesting("Who confirmed the bays?"), ScriptedProvider.RateLimited],
            standby: [Answering("Two bays were confirmed.")]);
        var plan = ChatDeclarations.Plan(maximumRequestCharacters: 100_000)
            .WithFallback(ChatDeclarations.Plan(
                ChatDeclarations.Endpoint("standby", routedModelName: ScriptedProvider.StandbyModel),
                maximumRequestCharacters: 100_000));

        // Act
        var followUps = await provider.AgentOver(plan)
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.StandbyRequestCount);
        Assert.Empty(followUps);
    }

    /// <summary>An answer that rests on nothing is low whatever the model rated it, because the band is capped by its support.</summary>
    [Fact]
    public async Task ComposeAsync_AModelRatingHighOverNoSource_ComposesAnUnsupportedAnswerAtLowConfidence()
    {
        // Arrange
        using var provider = new ScriptedProvider(
            answering: [Rating("high"), Answering("Two bays were confirmed.")],
            standby: []);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000))
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        var block = Assert.IsType<AnswerBlock>(this.ComposedBlock());
        Assert.Equal(
            (PresentationSupport.Unsupported, PresentationConfidence.Low),
            (block.Evidence.Support, block.Confidence));
    }

    /// <summary>An answer that names what it read is cited rather than supported, and keeps the band the model rated it at.</summary>
    [Fact]
    public async Task ComposeAsync_AModelRatingHighOverAMessageItSearched_ComposesACitedAnswerAtHighConfidence()
    {
        // Arrange
        var search = new RecordingEmailKnowledgeSearch()
            .Returning("bays", KnowledgePassages.Create("Four bays are confirmed.", subject: "Bays"));
        using var provider = new ScriptedProvider(
            answering: [Searching("bays"), Rating("high"), Answering("Four bays were confirmed.")],
            standby: []);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search)
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        var block = Assert.IsType<AnswerBlock>(this.ComposedBlock());
        Assert.Equal(
            (PresentationSupport.Cited, PresentationConfidence.High, 1),
            (block.Evidence.Support, block.Confidence, block.Evidence.Citations.Count));
    }

    /// <summary>A control character between a mark and its bracket, or inside a tag, cannot rebuild what the neutralising passed over.</summary>
    [Theory]
    [InlineData("See !\u0001[x](https://stranger.example.test/p.png).", "See [x](https://stranger.example.test/p.png).")]
    [InlineData("See <\u0001img src=\"https://stranger.example.test/p.png\">.", "See &lt;img src=\"https://stranger.example.test/p.png\">.")]
    public void Presentable_AControlCharacterSplittingMarkup_LeavesNoPictureOnceItIsDropped(string written, string expected)
    {
        // Act
        var presented = AgentConversationAgent.Presentable(written);

        // Assert
        Assert.Equal(expected, presented?.Value);
    }

    /// <summary>An answer's picture is published as a link, and every link says whether the mail the Agent read carries it.</summary>
    [Fact]
    public async Task ComposeAsync_AnAnswerWritingAPictureAndLinks_ComposesNoPictureAndTracesEveryLinkToWhatWasRead()
    {
        // Arrange
        var search = new RecordingEmailKnowledgeSearch()
            .Returning("bays", KnowledgePassages.Create("Book the bays at https://bays.example.test/book today.", subject: "Bays"));
        using var provider = new ScriptedProvider(
            answering: [Searching("bays"), Answering("Book at [bays](https://bays.example.test/book) or [here](https://other.example.test/). ![map](https://cdn.example.test/map.png)")],
            standby: []);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search)
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        var block = Assert.IsType<AnswerBlock>(this.ComposedBlock());
        Assert.DoesNotContain("![", block.Text.Value, StringComparison.Ordinal);
        Assert.Equal(
            [("https://bays.example.test/book", true), ("https://other.example.test/", false), ("https://cdn.example.test/map.png", false)],
            block.Links.Select(static link => (link.Link.Target, link.AppearsInCitedSources)));
    }

    /// <summary>A folder whose mapping withholds it from AI is outside the scope every search of the Agent's run reads.</summary>
    [Fact]
    public async Task ComposeAsync_AFolderWithheldFromAI_IsLeftOutOfTheScopeTheRunSearches()
    {
        // Arrange
        var withheld = new MailFolderIdentity(Primary, MailFolderAlias.Create("Private"));
        var search = new RecordingEmailKnowledgeSearch();
        using var provider = new ScriptedProvider(
            answering: [Searching("bays"), Answering("Nothing mentions the bays.")],
            standby: []);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search, foldersWithheldFromAI: [withheld])
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [new MailFolderIdentity(Primary, MailFolderAlias.Create("inbox"))],
            Assert.Single(search.Calls).Scope.ReadableFolders);
    }

    /// <summary>
    /// Every round of the tool loop is one request, and all of them belong to the conversation — so each carries the
    /// same session, and it is not the conversation's own identifier.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_AModelDeclaringStickySessionsOnOpenRouter_SendsTheConversationsSessionOnEveryRequest()
    {
        // Arrange
        using var provider = new ScriptedProvider(
            answering: [Suggesting("Who confirmed the bays?"), Answering("Two bays were confirmed.")],
            standby: []);
        var plan = ChatDeclarations.Plan(
            ChatDeclarations.Endpoint(address: "https://openrouter.ai/api/v1/", stickySessions: true),
            maximumRequestCharacters: 100_000);

        // Act
        await provider.AgentOver(plan).ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        var expected = AgentConversationAgent.StickySessionOf(this.conversation);
        Assert.Equal([expected, expected], provider.SentSessions);
        Assert.DoesNotContain(this.conversation.Value.ToString("N"), expected, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StickySessionOf_TheSameConversationTwice_IsTheSameKey()
    {
        // Act, Assert
        Assert.Equal(
            AgentConversationAgent.StickySessionOf(this.conversation),
            AgentConversationAgent.StickySessionOf(AgentConversationId.Create(this.conversation.Value)));
    }

    [Fact]
    public void StickySessionOf_TwoConversations_AreDifferentKeys()
    {
        // Act, Assert
        Assert.NotEqual(
            AgentConversationAgent.StickySessionOf(this.conversation),
            AgentConversationAgent.StickySessionOf(AgentConversationId.New()));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.journal.Dispose();

        return this.signals.DisposeAsync();
    }

    private static string Suggesting(string question) =>
        "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"call-1\","
        + "\"type\":\"function\",\"function\":{\"name\":\"suggest_follow_ups\","
        + "\"arguments\":\"{\\\"questions\\\":[\\\"" + question + "\\\"]}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

    private static string Rating(string band) =>
        "{\"id\":\"chatcmpl-3\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"call-2\","
        + "\"type\":\"function\",\"function\":{\"name\":\"rate_confidence\","
        + "\"arguments\":\"{\\\"band\\\":\\\"" + band + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

    private static string Searching(string query) =>
        "{\"id\":\"chatcmpl-4\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"call-3\","
        + "\"type\":\"function\",\"function\":{\"name\":\"search_mail\","
        + "\"arguments\":\"{\\\"" + ScopedMailKnowledgeRetrieval.QueryArgumentName + "\\\":\\\"" + query + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

    private static string Answering(string text) =>
        "{\"id\":\"chatcmpl-2\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"" + text + "\"},"
        + "\"finish_reason\":\"stop\"}]}";

    private PresentationBlock ComposedBlock() =>
        this.store.ReceivedCalls()
            .Select(static call => call.GetArguments())
            .Select(static arguments => arguments.OfType<AgentBlockComposed>().SingleOrDefault())
            .OfType<AgentBlockComposed>()
            .Single()
            .Block;

    private AgentAnswerBrief Brief() => new(
        new AgentQuestion(
            this.conversation,
            SyntheticUser.Deployment,
            PresentationText.Create("How many bays were confirmed?"),
            Scope: null,
            this.answer,
            OpenedAt: 1,
            Now),
        UserLanguage.English,
        History: []);

    /// <summary>Two endpoints answering from their own scripts, told apart by the model each request names.</summary>
    private sealed class ScriptedProvider : IDisposable
    {
        public const string StandbyModel = "a-standby-model";

        /// <summary>A script entry the endpoint answers with a throttle, which is a failure the next model may answer.</summary>
        public const string RateLimited = "";

        private readonly FakeHttpMessageHandler handler;

        public ScriptedProvider(IReadOnlyList<string> answering, IReadOnlyList<string> standby)
        {
            var answeringScript = new Queue<string>(answering);
            var standbyScript = new Queue<string>(standby);

            this.handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                var fromStandby = body.Contains(StandbyModel, StringComparison.Ordinal);
                this.SentSessions.Add(request.Headers.TryGetValues("x-session-id", out var sessions) ? sessions.Single() : null);
                this.StandbyRequestCount += fromStandby ? 1 : 0;
                var payload = (fromStandby ? standbyScript : answeringScript).Dequeue();

                return payload is RateLimited
                    ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent("{\"error\":{\"message\":\"slow down\"}}", Encoding.UTF8, "application/json"),
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                    };
            });
        }

        public int StandbyRequestCount { get; private set; }

        /// <summary>Gets the session each request carried, in the order they arrived, with <see langword="null" /> for one carrying none.</summary>
        public List<string?> SentSessions { get; } = [];

        public AgentConversationAgent AgentOver(
            ChatGenerationPlan plan,
            IEmailKnowledgeSearch? search = null,
            MailFolderIdentity[]? foldersWithheldFromAI = null)
        {
            var transportFactory = Substitute.For<IHttpClientFactory>();
            transportFactory
                .CreateClient(Arg.Any<string>())
                .Returns(_ => new HttpClient(this.handler, disposeHandler: false));

            var credentialSource = Substitute.For<IProviderEndpointCredentialSource>();
            credentialSource
                .ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(ProviderEndpointCredential.FromApiKey("a-configured-key", resolvedMaterial: null)));

            var operationRunner = Substitute.For<IOutboundOperationRunner>();
            operationRunner
                .RunAsync(
                    Arg.Any<OutboundDependency>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<Func<CancellationToken, Task<Microsoft.Extensions.AI.ChatResponse>>>()!(call.Arg<CancellationToken>()));

            var catalog = Substitute.For<ICallerMailAccountCatalog>();
            catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([SyntheticServedAccount.Of(Primary)]);
            catalog.User.Returns(SyntheticUser.Deployment);

            var participation = StubMailFolderParticipation.Mapping(new MailFolderIdentity(Primary, MailFolderAlias.Create("inbox")))
                .Hiding(foldersWithheldFromAI ?? []);
            var readers = new AgentConversationReaders(
                new MailboxScopeResolver(
                    catalog,
                    participation,
                    StubDeploymentMailFolders.Of(participation),
                    StubMailFolderMappings.ResolvingNothing),
                search ?? Substitute.For<IEmailKnowledgeSearch>(),
                ContentReader: null!,
                ProvenanceReader: StubEmailProvenanceReader.AllKnownCorrespondents,
                StateBrowser: null!,
                Calendar: null!,
                Tasks: null!,
                ResponseAuthoring: null!,
                search is null
                    ? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AssistantUse)
                    : AccessAuthorizations.ForCallerGranted(MailFathomPermission.AssistantUse, MailFathomPermission.MailRead));

            return new AgentConversationAgent(
                plan,
                MailAnsweringRunBounds.Default,
                Substitute.For<IMailAnsweringSpendLedger>(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                SensitiveContentEgressGuards.Inactive(),
                new EmptyAgentInstructionEnvelope(),
                readers,
                NullLoggerFactory.Instance,
                NullLogger<AgentConversationAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
