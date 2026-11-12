// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Net;
using System.Text;
using System.Text.Json;
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
using MailFathom.Application.Agent.Files;
using MailFathom.Application.Agent.Reports;
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
        var completion = await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000))
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Who confirmed the bays?"], completion.FollowUps.Select(static followUp => followUp.Value));
        Assert.Null(completion.Ceiling);
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
        var completion = await provider.AgentOver(plan)
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, provider.StandbyRequestCount);
        Assert.Empty(completion.FollowUps);
    }

    /// <summary>An automatic run is offered the move, and a move it makes is written into the conversation with the reason the model gave.</summary>
    [Fact]
    public async Task ComposeAsync_AnAutomaticRunThatAsksToGoDeep_RecordsTheMoveWithItsReason()
    {
        // Arrange
        using var provider = new ScriptedProvider(
            answering: [GoingDeep("It reaches back a year of supplier mail."), Answering("Prices rose twice.")],
            standby: []);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000))
            .ComposeAsync(this.Brief(AgentAnswerMode.Auto), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(AgentRunEffort.GoDeepToolName, provider.SentBodies[0], StringComparison.Ordinal);
        var deepened = Assert.Single(this.Written<AgentAnswerDeepened>());
        Assert.Equal("It reaches back a year of supplier mail.", deepened.Reason.Value);
    }

    /// <summary>A fast or a deep run is never offered the move: a fast one never moves on its own, and a deep one has nowhere to go.</summary>
    [Theory]
    [InlineData(AgentAnswerMode.Fast)]
    [InlineData(AgentAnswerMode.Deep)]
    public async Task ComposeAsync_ARunAskedFastOrDeep_IsNotOfferedTheMove(AgentAnswerMode mode)
    {
        // Arrange
        using var provider = new ScriptedProvider(answering: [Answering("Two bays were confirmed.")], standby: []);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000))
            .ComposeAsync(this.Brief(mode), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(AgentRunEffort.GoDeepToolName, provider.SentBodies[0], StringComparison.Ordinal);
    }

    /// <summary>A fast run's last call may call no tool and is told to answer and say it stopped, and the run is recorded as having answered at its ceiling.</summary>
    [Fact]
    public async Task ComposeAsync_AFastRunReachingItsLastCall_AnswersWithoutToolsAndReportsTheCeiling()
    {
        // Arrange
        var search = new RecordingEmailKnowledgeSearch()
            .Returning("invoices", KnowledgePassages.Create("Invoice 14 is due.", subject: "Invoice"));
        using var provider = new ScriptedProvider(
            answering: [Searching("invoices"), Answering("Invoice 14 is due; I stopped at the limit.")],
            standby: []);
        var bounds = new AgentRunBounds(MailAnsweringRunBounds.Create(20_000, 2, 160_000), AgentRunBounds.DefaultDeep);

        // Act
        var completion = await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search, bounds: bounds)
            .ComposeAsync(this.Brief(AgentAnswerMode.Fast), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain("\"tool_choice\":\"none\"", provider.SentBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"tool_choice\":\"none\"", provider.SentBodies[1], StringComparison.Ordinal);
        Assert.Equal(1, completion.Ceiling?.MessagesRead);
        Assert.Empty(this.Written<AgentAnswerDeepened>());
    }

    /// <summary>An automatic run that has not moved is offered the move alone on the call before its last fast one, so a question that needs more reading goes deep rather than stopping.</summary>
    [Fact]
    public async Task ComposeAsync_AnAutomaticRunNearingItsLastFastCall_IsOfferedOnlyTheMove()
    {
        // Arrange
        using var provider = new ScriptedProvider(
            answering: [Suggesting("Who sent it?"), GoingDeep("It needs every invoice this year."), Answering("Nine invoices arrived.")],
            standby: []);
        var bounds = new AgentRunBounds(MailAnsweringRunBounds.Create(20_000, 3, 160_000), AgentRunBounds.DefaultDeep);

        // Act
        var completion = await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), bounds: bounds)
            .ComposeAsync(this.Brief(AgentAnswerMode.Auto), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([AgentRunEffort.GoDeepToolName], OfferedTools(provider.SentBodies[1]));
        Assert.Single(this.Written<AgentAnswerDeepened>());
        Assert.Null(completion.Ceiling);
    }

    /// <summary>An automatic run whose reading the fast limit cut is told at its next call to go deep rather than answer with a caveat, and is told nothing once it has moved.</summary>
    [Fact]
    public async Task ComposeAsync_AnAutomaticRunWhoseReadingWasCutAtTheFastLimit_IsToldToGoDeepUntilItMoves()
    {
        // Arrange
        const string Told = "A reading above was cut at the limit of a quick answer.";
        var search = new RecordingEmailKnowledgeSearch()
            .Returning("invoices", KnowledgePassages.Create("Invoice 14 is due.", subject: "Invoice"));
        using var provider = new ScriptedProvider(
            answering: [Searching("invoices"), GoingDeep("It needs every invoice this year."), Answering("Nine invoices arrived.")],
            standby: []);
        var bounds = new AgentRunBounds(MailAnsweringRunBounds.Create(5, 10, 160_000), AgentRunBounds.DefaultDeep);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search, bounds: bounds)
            .ComposeAsync(this.Brief(AgentAnswerMode.Auto), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [false, true, false],
            provider.SentBodies.Select(static body => body.Contains(Told, StringComparison.Ordinal)));
        Assert.Superset(
            new HashSet<string> { ScopedMailKnowledgeRetrieval.SearchToolName, AgentRunEffort.GoDeepToolName },
            OfferedTools(provider.SentBodies[1]).ToHashSet());
        Assert.Single(this.Written<AgentAnswerDeepened>());
    }

    /// <summary>A fast run never moves on its own, so a reading the limit cut tells it nothing about going deep.</summary>
    [Fact]
    public async Task ComposeAsync_AFastRunWhoseReadingWasCutAtItsLimit_IsNotToldToGoDeep()
    {
        // Arrange
        var search = new RecordingEmailKnowledgeSearch()
            .Returning("invoices", KnowledgePassages.Create("Invoice 14 is due.", subject: "Invoice"));
        using var provider = new ScriptedProvider(
            answering: [Searching("invoices"), Answering("I stopped at the limit.")],
            standby: []);
        var bounds = new AgentRunBounds(MailAnsweringRunBounds.Create(5, 10, 160_000), AgentRunBounds.DefaultDeep);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search, bounds: bounds)
            .ComposeAsync(this.Brief(AgentAnswerMode.Fast), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.DoesNotContain(provider.SentBodies, static body => body.Contains("cut at the limit of a quick answer", StringComparison.Ordinal));
    }

    /// <summary>The last call offers no tool, the rating among them, so an answer written on it is drawn low rather than at a band nobody chose.</summary>
    [Fact]
    public async Task ComposeAsync_AnAnswerWrittenOnTheLastCallWithoutARating_IsComposedAtLowConfidence()
    {
        // Arrange
        var search = new RecordingEmailKnowledgeSearch()
            .Returning("invoices", KnowledgePassages.Create("Invoice 14 is due.", subject: "Invoice"));
        using var provider = new ScriptedProvider(
            answering: [Searching("invoices"), Answering("Invoice 14 is due; I stopped at the limit.")],
            standby: []);
        var bounds = new AgentRunBounds(MailAnsweringRunBounds.Create(20_000, 2, 160_000), AgentRunBounds.DefaultDeep);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search, bounds: bounds)
            .ComposeAsync(this.Brief(AgentAnswerMode.Fast), this.journal, TestContext.Current.CancellationToken);

        // Assert
        var block = Assert.IsType<AnswerBlock>(this.ComposedBlock());
        Assert.Equal(
            (PresentationSupport.Cited, PresentationConfidence.Low),
            (block.Evidence.Support, block.Confidence));
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

    /// <summary>A bound the model writes is the person's own wall clock, because that is the clock the turn stated to it.</summary>
    [Fact]
    public async Task ComposeAsync_ABoundWrittenForAPersonEastOfUtc_IsReadAtTheirZonesOffset()
    {
        // Arrange
        var search = new RecordingEmailKnowledgeSearch();
        using var provider = new ScriptedProvider(
            answering: [Searching("bays", receivedOnOrAfter: "2026-09-22T00:00"), Answering("Nothing came in about the bays today.")],
            standby: []);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), search, zoneId: "Europe/Warsaw")
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.FromHours(2)),
            Assert.Single(search.Calls).Query.ReceivedOnOrAfter);
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

    /// <summary>A search matches words rather than translating them, so the Agent is told which languages the accounts it names are kept in.</summary>
    [Fact]
    public async Task ComposeAsync_AnAccountKeptInPolish_NamesItsLanguageOnTheTurn()
    {
        // Arrange
        using var provider = new ScriptedProvider(answering: [Answering("The train leaves at 7:10.")], standby: []);
        var languages = new SyntheticAccountLanguages().Reading(Primary, MailAccountLanguage.Polish);

        // Act
        await provider.AgentOver(ChatDeclarations.Plan(maximumRequestCharacters: 100_000), new RecordingEmailKnowledgeSearch(), accountLanguages: languages)
            .ComposeAsync(this.Brief(), this.journal, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("The mailboxes you may search are kept in: Polish.", provider.SentBodies[0], StringComparison.Ordinal);
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

    private static string Searching(string query, string? receivedOnOrAfter = null) =>
        "{\"id\":\"chatcmpl-4\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"call-3\","
        + "\"type\":\"function\",\"function\":{\"name\":\"search_mail\","
        + "\"arguments\":\"{\\\"" + ScopedMailKnowledgeRetrieval.QueryArgumentName + "\\\":\\\"" + query + "\\\""
        + (receivedOnOrAfter is null ? string.Empty : ",\\\"receivedOnOrAfter\\\":\\\"" + receivedOnOrAfter + "\\\"")
        + "}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

    private static string GoingDeep(string reason) =>
        "{\"id\":\"chatcmpl-5\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"tool_calls\":[{\"id\":\"call-5\","
        + "\"type\":\"function\",\"function\":{\"name\":\"" + AgentRunEffort.GoDeepToolName + "\","
        + "\"arguments\":\"{\\\"reason\\\":\\\"" + reason + "\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}";

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

    private static IReadOnlyList<string> OfferedTools(string body)
    {
        using var request = JsonDocument.Parse(body);

        return [.. request.RootElement.GetProperty("tools").EnumerateArray().Select(static tool => tool.GetProperty("function").GetProperty("name").GetString()!)];
    }

    private IReadOnlyList<TEntry> Written<TEntry>()
        where TEntry : AgentConversationEntry =>
        [.. this.store.ReceivedCalls().SelectMany(static call => call.GetArguments()).OfType<TEntry>()];

    private AgentAnswerBrief Brief(AgentAnswerMode mode = AgentAnswerMode.Auto) => new(
        new AgentQuestion(
            this.conversation,
            SyntheticUser.Deployment,
            PresentationText.Create("How many bays were confirmed?"),
            Scope: null,
            this.answer,
            OpenedAt: 1,
            Now)
        {
            Mode = mode,
        },
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
                this.SentBodies.Add(body);
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

        /// <summary>Gets what each request carried, in the order they arrived, which is where a test reads the turn the model was sent.</summary>
        public List<string> SentBodies { get; } = [];

        public AgentConversationAgent AgentOver(
            ChatGenerationPlan plan,
            IEmailKnowledgeSearch? search = null,
            MailFolderIdentity[]? foldersWithheldFromAI = null,
            IMailAccountLanguages? accountLanguages = null,
            string zoneId = "UTC",
            AgentRunBounds? bounds = null,
            AgentConversationFiles? files = null)
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
                Timeline: null!,
                Census: null!,
                ContentReader: null!,
                ProvenanceReader: StubEmailProvenanceReader.AllKnownCorrespondents,
                AttachmentTexts: StoredAttachmentTexts.None,
                StateBrowser: null!,
                Calendar: null!,
                Tasks: null!,
                ResponseAuthoring: null!,
                search is null
                    ? AccessAuthorizations.ForCallerGranted(MailFathomPermission.AssistantUse)
                    : AccessAuthorizations.ForCallerGranted(MailFathomPermission.AssistantUse, MailFathomPermission.MailRead),
                UserClocks.Reading(Now, zoneId),
                MailFootings.SendingNone(),
                files ?? ConversationFiles.None,
                Substitute.For<IAgentReportTypesetter>());

            return new AgentConversationAgent(
                plan,
                bounds ?? AgentRunBounds.Default,
                Substitute.For<IMailAnsweringSpendLedger>(),
                credentialSource,
                new OpenAiCompatibleClientFactory(),
                transportFactory,
                operationRunner,
                Substitute.For<IAiProviderHealthRecorder>(),
                SensitiveContentEgressGuards.Inactive(),
                accountLanguages ?? new SyntheticAccountLanguages(),
                new EmptyAgentInstructionEnvelope(),
                readers,
                NullLoggerFactory.Instance,
                NullLogger<AgentConversationAgent>.Instance);
        }

        public void Dispose() => this.handler.Dispose();
    }
}
