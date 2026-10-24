// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Xml.Linq;
using MailFathom.AI.AgentConversations;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Chat;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers how the earlier turns of an Agent conversation reach a model: as the turn a compaction reads, and as the turns the next question replays.</summary>
public sealed class AgentHistoryDocumentTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    /// <summary>A compaction reads each turn as an element attributed to whoever wrote it, beside the summary it folds in.</summary>
    [Fact]
    public void ComposeTurn_AnEarlierSummaryAndTurns_WritesEachAsAnElementAttributedToItsAuthor()
    {
        // Act
        var written = Parsed(AgentConversationSummaryInstructions.ComposeTurn(
            "They talked about racking.",
            [
                new AgentWrittenTurn(AgentMessageAuthor.Person, "Who sent the racking quote?"),
                new AgentWrittenTurn(AgentMessageAuthor.Agent, "Northwind sent it on Monday."),
                new AgentProposedMessageTurn("Racking quote", ["sales@northwind.example", "ops@northwind.example"]),
            ]));

        // Assert
        Assert.Equal("They talked about racking.", written.Element(AgentHistoryDocument.SummaryElementName)?.Value);

        var conversation = written.Element(AgentHistoryDocument.ConversationElementName)!;
        Assert.Equal(
            ["person:Who sent the racking quote?", "agent:Northwind sent it on Monday."],
            conversation.Elements(AgentHistoryDocument.TurnElementName)
                .Select(static turn => $"{turn.Attribute(AgentHistoryDocument.AuthorAttributeName)?.Value}:{turn.Value}"));

        var proposal = Assert.Single(conversation.Elements(AgentHistoryDocument.ProposedMessageElementName));
        Assert.Equal("Racking quote", proposal.Element(AgentHistoryDocument.SubjectElementName)?.Value);
        Assert.Equal(
            ["sales@northwind.example", "ops@northwind.example"],
            proposal.Elements(AgentHistoryDocument.RecipientElementName).Select(static recipient => recipient.Value));
    }

    /// <summary>A conversation never compacted before carries no earlier summary, so none is written as an empty one.</summary>
    [Fact]
    public void ComposeTurn_NoEarlierSummary_WritesNoSummaryElement()
    {
        // Act
        var written = Parsed(AgentConversationSummaryInstructions.ComposeTurn(
            previousSummary: null,
            [new AgentWrittenTurn(AgentMessageAuthor.Person, "Who sent the racking quote?")]));

        // Assert
        Assert.Null(written.Element(AgentHistoryDocument.SummaryElementName));
    }

    /// <summary>
    /// A summary is a model's account of turns that quoted mail, so it comes back as a quoted document in front of the
    /// conversation rather than as the Agent's own turn, where whatever a message slipped into it would read as the
    /// Agent's conclusion.
    /// </summary>
    [Fact]
    public void MessageOf_ASummary_IsAQuotedDocumentRatherThanAnAgentTurn()
    {
        // Act
        var message = AgentHistoryDocument.MessageOf(new AgentSummaryTurn("They asked who quoted; Northwind did."));

        // Assert
        Assert.Equal(ChatRole.User, message.Role);
        Assert.StartsWith(AgentHistoryDocument.SummaryStatement, message.Text, StringComparison.Ordinal);
        Assert.Equal(
            "They asked who quoted; Northwind did.",
            Parsed(message.Text).Element(AgentHistoryDocument.SummaryElementName)?.Value);
    }

    /// <summary>A proposal is the Agent's own act, replayed as the values it offered rather than a sentence built around a subject a sender wrote.</summary>
    [Fact]
    public void MessageOf_AProposal_IsTheAgentsTurnQuotingTheSubjectAndRecipients()
    {
        // Act
        var message = AgentHistoryDocument.MessageOf(new AgentProposedMessageTurn("Parking permits", ["ingrid@brightwater.test"]));

        // Assert
        Assert.Equal(ChatRole.Assistant, message.Role);
        var proposal = Parsed(message.Text).Element(AgentHistoryDocument.ProposedMessageElementName)!;
        Assert.Equal("Parking permits", proposal.Element(AgentHistoryDocument.SubjectElementName)?.Value);
        Assert.Equal("ingrid@brightwater.test", proposal.Element(AgentHistoryDocument.RecipientElementName)?.Value);
    }

    /// <summary>What the person and the Agent wrote are the conversation's own turns, replayed in the role of whoever wrote them, unchanged.</summary>
    [Theory]
    [InlineData(AgentMessageAuthor.Person, ChatRole.User)]
    [InlineData(AgentMessageAuthor.Agent, ChatRole.Assistant)]
    public void MessageOf_AWrittenTurn_KeepsItsAuthorsRoleAndText(AgentMessageAuthor author, ChatRole role)
    {
        // Act
        var message = AgentHistoryDocument.MessageOf(new AgentWrittenTurn(author, "Who sent the <quote>?"));

        // Assert
        Assert.Equal(new ChatMessage(role, "Who sent the <quote>?"), message);
    }

    /// <summary>Every value a turn carries is scanned as the value it is, a proposal's subject and each of its addresses included.</summary>
    [Fact]
    public async Task GuardAsync_TurnsCarryingASecret_GuardsEveryValueOfEveryTurn()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, new FakeTimeProvider());
        using var actingFor = egress.Guard.ActingFor(await egress.ReadUserPostureAsync(TestContext.Current.CancellationToken));
        AgentHistoryTurn[] turns =
        [
            new AgentWrittenTurn(AgentMessageAuthor.Person, $"The key is {Marker}."),
            new AgentProposedMessageTurn($"Rotate {Marker}", ["ops@example.test", $"{Marker}@example.test"]),
            new AgentSummaryTurn($"They shared {Marker}."),
        ];

        // Act
        var guarded = await AgentHistoryDocument.GuardAsync(egress.Guard, turns, TestContext.Current.CancellationToken);

        // Assert
        Assert.Collection(
            guarded,
            written => Assert.DoesNotContain(Marker, Assert.IsType<AgentWrittenTurn>(written).Text, StringComparison.Ordinal),
            proposal =>
            {
                var proposed = Assert.IsType<AgentProposedMessageTurn>(proposal);
                Assert.DoesNotContain(Marker, proposed.Subject, StringComparison.Ordinal);
                Assert.Equal("ops@example.test", proposed.Recipients[0]);
                Assert.DoesNotContain(Marker, proposed.Recipients[1], StringComparison.Ordinal);
            },
            summary => Assert.DoesNotContain(Marker, Assert.IsType<AgentSummaryTurn>(summary).Summary, StringComparison.Ordinal));
    }

    /// <summary>A deployment that withholds nothing hands every turn back as it was.</summary>
    [Fact]
    public async Task GuardAsync_AnInactiveGuard_HandsEveryTurnBackUnchanged()
    {
        // Arrange
        var proposal = new AgentProposedMessageTurn("Racking quote", ["sales@northwind.example"]);
        AgentHistoryTurn[] turns = [new AgentWrittenTurn(AgentMessageAuthor.Agent, "Northwind."), proposal];

        // Act
        var guarded = await AgentHistoryDocument.GuardAsync(
            SensitiveContentEgressGuards.Inactive(),
            turns,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(turns[0], guarded[0]);
        var replayed = Assert.IsType<AgentProposedMessageTurn>(guarded[1]);
        Assert.Equal(proposal.Subject, replayed.Subject);
        Assert.Equal(proposal.Recipients, replayed.Recipients);
    }

    private static XElement Parsed(string turn) => XElement.Parse($"<turn>{turn}</turn>", LoadOptions.PreserveWhitespace);
}
