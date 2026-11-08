// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.AI.Discovery;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ReplyDrafts;
using MailFathom.AI.Retrieval;
using MailFathom.AI.ThreadStates;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the one composition every AI operation is built by: what it names the run, what it offers it, and what instruction it carries.</summary>
/// <remarks>
/// Each test runs the composed agent over a substituted chat client and reads what the client was asked to send, so what
/// is proved is the composition rather than a description of it.
/// </remarks>
public sealed class AgentCompositionTests
{
    private const string Instruction = "Answer the question you were asked.";

    /// <summary>
    /// The data rule is stated once, here, rather than once per instruction: whatever an operation says, the instruction
    /// it is composed with carries the clause after it, so no operation can ship without it.
    /// </summary>
    [Fact]
    public async Task Compose_AnyOperation_CarriesTheDataClauseAfterItsOwnInstruction()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, []),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            $"{Instruction}\n\n{AgentTurnDocument.DataClause}",
            Assert.Single(chatClient.Calls).Options?.Instructions);
    }

    /// <summary>
    /// Three agents wrote out the account an unverified sender asked money to go to, beside a warning. The rule is stated
    /// once, in the shared clause, so every agent reading mail is composed carrying it.
    /// </summary>
    /// <param name="agentName">The agent composed, as <see cref="MailReadingAgents" /> names it.</param>
    [Theory]
    [MemberData(nameof(MailReadingAgentNames))]
    public async Task Compose_EveryAgentReadingMail_CarriesTheUnverifiedPaymentRule(string agentName)
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = MailReadingAgents[agentName](chatClient);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            AgentTurnDocumentTests.UnverifiedPaymentRule,
            Assert.Single(chatClient.Calls).Options?.Instructions?.ReplaceLineEndings(" "),
            StringComparison.Ordinal);
    }

    /// <summary>The whole of what this build ships: a composition over the registered default is the operation's own instruction as composed.</summary>
    [Fact]
    public async Task Compose_TheEmptyEnvelope_CarriesTheComposedInstructionUnchanged()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, []),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AgentComposition.InstructionFor(Instruction), Assert.Single(chatClient.Calls).Options?.Instructions);
    }

    /// <summary>Concatenated and nothing else: the composed instruction sits between the two halves, with no separator of the composition's own.</summary>
    [Fact]
    public async Task Compose_AnEnvelopeSupplyingBothHalves_PlacesThemFirstAndLast()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, []),
            new StubAgentInstructionEnvelope("before. ", " after."),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            $"before. {AgentComposition.InstructionFor(Instruction)} after.",
            Assert.Single(chatClient.Calls).Options?.Instructions);
    }

    /// <summary>Either half stands on its own, so an implementation with something to say in one position says nothing in the other.</summary>
    [Theory]
    [InlineData("before. ", "")]
    [InlineData("", " after.")]
    public async Task Compose_AnEnvelopeSupplyingOneHalf_PlacesThatHalfAlone(string preamble, string postamble)
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, []),
            new StubAgentInstructionEnvelope(preamble, postamble),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            $"{preamble}{AgentComposition.InstructionFor(Instruction)}{postamble}",
            Assert.Single(chatClient.Calls).Options?.Instructions);
    }

    /// <summary>
    /// Asked at composition rather than read once at start, which is what lets an implementation vary its answer per
    /// person or per request without any operation changing.
    /// </summary>
    [Fact]
    public async Task Compose_AnEnvelopeAnsweringDifferentlyEachTime_IsConsultedOncePerComposition()
    {
        // Arrange
        var envelope = new CountingAgentInstructionEnvelope();
        using var firstClient = ScriptedChatClient.Answering("answered");
        using var secondClient = ScriptedChatClient.Answering("answered");
        var operation = new AgentOperation("an-operation", Instruction, []);

        // Act
        var first = AgentComposition.Compose(
            firstClient,
            ChatDeclarations.Plan(),
            operation,
            envelope,
            NullLoggerFactory.Instance);
        await first.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        var second = AgentComposition.Compose(
            secondClient,
            ChatDeclarations.Plan(),
            operation,
            envelope,
            NullLoggerFactory.Instance);
        await second.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"1. {AgentComposition.InstructionFor(Instruction)}", Assert.Single(firstClient.Calls).Options?.Instructions);
        Assert.Equal($"2. {AgentComposition.InstructionFor(Instruction)}", Assert.Single(secondClient.Calls).Options?.Instructions);
    }

    /// <summary>
    /// The name a run reports and the tool set that is the operation's whole capability, both taken from the operation
    /// rather than decided by the composition: an operation that only reads is one declaring no tool that mutates.
    /// </summary>
    [Fact]
    public async Task Compose_AnOperation_ReportsItsNameAndOffersItsOwnToolSet()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var tool = AIFunctionFactory.Create(() => "nothing", "count_nothing");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, [tool]),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);

        // Act
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("an-operation", agent.Name);
        Assert.Equal(
            ["count_nothing"],
            (Assert.Single(chatClient.Calls).Options?.Tools ?? []).Select(offered => offered.Name));
    }

    /// <summary>
    /// Every tool is held to its schema by the composition rather than by itself, so a key the model invented is refused
    /// before the tool runs, whichever operation offered it.
    /// </summary>
    [Fact]
    public async Task Compose_AToolCalledWithAnArgumentOutsideItsSchema_RefusesTheCallWithoutRunningTheTool()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("answered");
        var ran = false;
        var tool = AIFunctionFactory.Create((string word) => ran = word.Length > 0, "count_words");
        var agent = AgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new AgentOperation("an-operation", Instruction, [tool]),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
        await agent.RunAsync("a question", session: null, options: null, TestContext.Current.CancellationToken);
        var offered = Assert.IsType<AIFunction>(
            Assert.Single(Assert.Single(chatClient.Calls).Options?.Tools ?? []),
            exactMatch: false);

        // Act
        var answer = await offered.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?> { ["word"] = "invoice", ["receivedOnOrBefore"] = "2026-09-14T12:00" }),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "count_words takes no argument named \"receivedOnOrBefore\". Its arguments are word. Call it again without that one.",
            answer);
        Assert.False(ran);
    }

    /// <summary>An envelope with something to say in both positions.</summary>
    private sealed class StubAgentInstructionEnvelope(string preamble, string postamble) : IAgentInstructionEnvelope
    {
        public string Preamble => preamble;

        public string Postamble => postamble;
    }

    /// <summary>An envelope whose answer changes on every reading, which is what a per-person implementation would look like from here.</summary>
    private sealed class CountingAgentInstructionEnvelope : IAgentInstructionEnvelope
    {
        private int readings;

        public string Preamble => $"{++this.readings}. ";

        public string Postamble => string.Empty;
    }

    /// <summary>Gets the names of the agents <see cref="MailReadingAgents" /> composes, as theory data.</summary>
    public static TheoryData<string> MailReadingAgentNames => [.. MailReadingAgents.Keys];

    /// <summary>The agents that read mail and answer about it, each composed over a substituted client.</summary>
    private static Dictionary<string, Func<ScriptedChatClient, ChatClientAgent>> MailReadingAgents => new()
    {
        ["mail answering"] = static chatClient => MailAnsweringAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new ScopedMailKnowledgeRetrieval(
                new RecordingEmailKnowledgeSearch(),
                MailboxScope.Create([MailAccountId.Create("primary")], [MailFolderId.Create(Guid.Parse("83ecb521-bf32-7f67-8287-e8b923809bb3"))]),
                new MailAnsweringRunLedger(MailAnsweringRunBounds.Default),
                SensitiveContentEgressGuards.Inactive(),
                DateTimeOffset.UnixEpoch),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance),
        ["agent"] = static chatClient => AgentConversationComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            UserLanguage.English,
            [],
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance),
        ["discovery composition"] = static chatClient => DiscoveryCompositionAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance),
        ["reply draft"] = static chatClient => ReplyDraftAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            UserLanguage.English,
            new ReplyDraftTurn("a turn", [], [], []),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance),
        ["thread state"] = static chatClient => ThreadStateAgentComposition.Compose(
            chatClient,
            ChatDeclarations.Plan(),
            MailAccountLanguage.English,
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance),
    };
}
