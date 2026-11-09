// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.AI.UnitTests.Enrichment;

/// <summary>Covers what the enrichment agent is composed as: its name, its instruction, and what it may reach.</summary>
/// <remarks>
/// Run over a substituted chat client rather than described, so what is proved is the composition the framework actually
/// received.
/// </remarks>
public sealed class EmailEnrichmentAgentCompositionTests
{
    private const string Answer = """{ "sense": { "text": "a quotation", "reason": "it attaches one", "passages": [0] } }""";

    /// <summary>The message is put to the agent in the turn, so a tool would be a way to reach mail beyond it.</summary>
    [Fact]
    public async Task Compose_TheEnrichmentAgent_OffersNoToolAtAll()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient);

        // Act
        await agent.RunAsync(
            "Subject: the racking quotation",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(chatClient.Calls, call => Assert.True(call.Options?.Tools is null or []));
    }

    /// <summary>
    /// Over every language, so that composing the instruction for one and sending another is a failure here rather
    /// than something only the pure instruction tests would have noticed.
    /// </summary>
    [Theory]
    [InlineData(MailAccountLanguage.English)]
    [InlineData(MailAccountLanguage.Polish)]
    public async Task Compose_TheEnrichmentAgent_CarriesItsOwnInstructionInsideTheEnvelope(MailAccountLanguage language)
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient, language);

        // Act
        await agent.RunAsync(
            "Subject: the racking quotation",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.All(
            chatClient.Calls,
            call => Assert.Equal(AgentComposition.InstructionFor(EmailEnrichmentInstructions.TextFor(language)), call.Options?.Instructions));
    }

    /// <summary>A model declaring structured outputs is handed the schema and the instruction that leaves the shape to it.</summary>
    [Fact]
    public async Task Compose_AModelDeclaringStructuredOutputs_AsksForTheSchemaAndDropsTheShapeFromTheInstruction()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering(Answer);
        var agent = AgentOver(chatClient, plan: ChatDeclarations.Plan(structuredOutputs: true));

        // Act
        await agent.RunAsync(
            "Subject: the racking quotation",
            session: null,
            options: null,
            TestContext.Current.CancellationToken);

        // Assert
        var call = Assert.Single(chatClient.Calls);
        var format = Assert.IsType<ChatResponseFormatJson>(call.Options?.ResponseFormat);

        Assert.Equal(EmailEnrichmentSchema.Name, format.SchemaName);
        Assert.Equal(
            AgentComposition.InstructionFor(EmailEnrichmentInstructions.TextFor(MailAccountLanguage.English, constrainedBySchema: true)),
            call.Options?.Instructions);
    }

    /// <summary>The name is what every mark records as its origin, so it is this agent's alone.</summary>
    [Fact]
    public void Compose_TheEnrichmentAgent_IsNamedApartFromEveryOtherAgent()
    {
        // Arrange
        using var chatClient = ScriptedChatClient.Answering("{}");

        // Act
        var agent = AgentOver(chatClient);

        // Assert
        Assert.Equal(EmailEnrichmentAgentComposition.AgentName, agent.Name);
        Assert.NotEqual(MailAnsweringAgentComposition.AgentName, agent.Name);
    }

    private static EnrichableEmail Enrichable() =>
        new(
            StoredEmailId.Create(Guid.CreateVersion7()),
            "The racking quotation",
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            [new EnrichablePassage(EmailChunkId.Create(Guid.CreateVersion7()), 0, "the quotation is attached")],
            StubEmailProvenanceReader.KnownCorrespondent);

    private static ChatClientAgent AgentOver(
        ScriptedChatClient chatClient,
        MailAccountLanguage language = MailAccountLanguage.English,
        ChatGenerationPlan? plan = null) =>
        EmailEnrichmentAgentComposition.Compose(
            chatClient,
            plan ?? ChatDeclarations.Plan(),
            language,
            Enrichable(),
            new EmptyAgentInstructionEnvelope(),
            NullLoggerFactory.Instance);
}
