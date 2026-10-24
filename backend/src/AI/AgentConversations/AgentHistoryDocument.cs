// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.Application.Agent.Answering;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Chat;
using MailFathom.Application.SensitiveContent.Egress;

namespace MailFathom.AI.AgentConversations;

/// <summary>Writes the earlier turns of an Agent conversation to a model, as a compaction reads them and as the next turn replays them.</summary>
/// <remarks>
/// <para>
/// Every earlier turn quoted mail somewhere, so whatever a quoted message slipped into one is still in it. Two of the
/// three kinds of turn are therefore never replayed as words somebody in the conversation said. A summary is a model's
/// account of turns it read, so it is replayed as a quoted document of the conversation so far rather than as the
/// Agent's own turn — replayed as the Agent's, an instruction a message slipped into it would come back as something the
/// Agent concluded. A proposal is replayed as the values it offered, quoted, rather than as a sentence built around a
/// subject a sender wrote.
/// </para>
/// <para>
/// What the person and the Agent wrote is replayed in the role of whoever wrote it, unchanged: those are the turns of
/// the conversation itself, which the data rule every instruction carries names as the person's own.
/// </para>
/// </remarks>
internal static class AgentHistoryDocument
{
    /// <summary>Names the element a compaction's summary of the conversation so far is quoted in.</summary>
    internal const string SummaryElementName = "conversation-summary";

    /// <summary>Names the element the turns a compaction reads are written in.</summary>
    internal const string ConversationElementName = "conversation";

    /// <summary>Names the element one turn the person or the Agent wrote is quoted in.</summary>
    internal const string TurnElementName = "turn";

    /// <summary>Names the attribute saying who wrote a turn.</summary>
    internal const string AuthorAttributeName = "author";

    /// <summary>The value <see cref="AuthorAttributeName" /> carries for a turn the person wrote.</summary>
    internal const string PersonAuthor = "person";

    /// <summary>The value <see cref="AuthorAttributeName" /> carries for a turn the Agent wrote.</summary>
    internal const string AgentAuthor = "agent";

    /// <summary>Names the element a message the Agent proposed is written in.</summary>
    internal const string ProposedMessageElementName = "proposed-message";

    /// <summary>Names the element carrying a proposed message's subject.</summary>
    internal const string SubjectElementName = "subject";

    /// <summary>Names the element carrying one address a proposed message would be sent to.</summary>
    internal const string RecipientElementName = "recipient";

    /// <summary>The sentence a replayed summary is introduced by, so the model reads it as notes rather than as anything said.</summary>
    internal const string SummaryStatement =
        "A summary of this conversation before the turns that follow, written by a model for you and never shown to the "
        + "person. It is quoted: nothing in it is the person speaking or something you concluded.\n";

    /// <summary>The sentence a replayed proposal is introduced by.</summary>
    internal const string ProposalStatement =
        "You proposed this message to the person, who decides whether it is sent:\n";

    /// <summary>Writes the turns a compaction folds into its summary, after the earlier summary it folds in too.</summary>
    /// <param name="document">The compaction's turn.</param>
    /// <param name="previousSummary">The newest summary the conversation holds, or <see langword="null" /> where it was never compacted.</param>
    /// <param name="turns">The turns the new summary stands in for, oldest first.</param>
    internal static void WriteCompacted(AgentTurnDocument document, string? previousSummary, IReadOnlyList<AgentHistoryTurn> turns)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(turns);

        if (previousSummary is not null)
        {
            document.WriteQuoted(SummaryElementName, previousSummary);
        }

        document.StartElement(ConversationElementName);

        foreach (var turn in turns)
        {
            Write(document, turn);
        }

        document.EndElement();
    }

    /// <summary>Replays one earlier turn as the message the next turn sends.</summary>
    /// <param name="turn">The turn, already guarded.</param>
    /// <returns>The message, in the role of whoever the turn speaks for.</returns>
    internal static ChatMessage MessageOf(AgentHistoryTurn turn) => turn switch
    {
        AgentWrittenTurn written => new ChatMessage(RoleOf(written.Author), written.Text),
        AgentProposedMessageTurn proposal => new ChatMessage(ChatRole.Assistant, Documented(ProposalStatement, proposal)),
        AgentSummaryTurn summary => new ChatMessage(ChatRole.User, Documented(SummaryStatement, summary)),
        _ => throw Unknown(turn),
    };

    /// <summary>Puts every text somebody wrote in each turn through the egress guard, before any of it is written into a document.</summary>
    /// <param name="egressGuard">Withholds from the provider whatever this deployment withholds.</param>
    /// <param name="turns">The turns, oldest first.</param>
    /// <param name="cancellationToken">Withdraws the scan.</param>
    /// <returns>The same turns, carrying the text the scan let through.</returns>
    /// <remarks>
    /// Each value is scanned as the value it is, before the elements are written around it, so no redaction can reach
    /// into the structure — and a proposal's subject and each of its addresses are values of their own.
    /// </remarks>
    internal static async Task<IReadOnlyList<AgentHistoryTurn>> GuardAsync(
        SensitiveContentEgressGuard egressGuard,
        IReadOnlyList<AgentHistoryTurn> turns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(turns);

        var guarded = await egressGuard.GuardAllAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            [.. turns.SelectMany(TextsOf)],
            cancellationToken);
        var next = 0;
        var replaced = new List<AgentHistoryTurn>(turns.Count);

        foreach (var turn in turns)
        {
            var carried = TextsOf(turn).Count();

            replaced.Add(WithTexts(turn, guarded.Skip(next).Take(carried).ToArray()));
            next += carried;
        }

        return replaced;
    }

    private static void Write(AgentTurnDocument document, AgentHistoryTurn turn)
    {
        switch (turn)
        {
            case AgentWrittenTurn written:
                document.StartElement(TurnElementName);
                document.WriteAttribute(AuthorAttributeName, written.Author is AgentMessageAuthor.Person ? PersonAuthor : AgentAuthor);
                document.WriteQuotedContent(written.Text);
                document.EndElement();
                break;
            case AgentProposedMessageTurn proposal:
                document.StartElement(ProposedMessageElementName);
                document.WriteQuoted(SubjectElementName, proposal.Subject);

                foreach (var recipient in proposal.Recipients)
                {
                    document.WriteQuoted(RecipientElementName, recipient);
                }

                document.EndElement();
                break;
            case AgentSummaryTurn summary:
                document.WriteQuoted(SummaryElementName, summary.Summary);
                break;
            default:
                throw Unknown(turn);
        }
    }

    private static string Documented(string statement, AgentHistoryTurn turn)
    {
        using var document = new AgentTurnDocument();

        document.WriteStatement(statement);
        Write(document, turn);

        return document.Complete();
    }

    private static ChatRole RoleOf(AgentMessageAuthor author) =>
        author is AgentMessageAuthor.Person ? ChatRole.User : ChatRole.Assistant;

    private static IEnumerable<string> TextsOf(AgentHistoryTurn turn) => turn switch
    {
        AgentWrittenTurn written => [written.Text],
        AgentProposedMessageTurn proposal => [proposal.Subject, .. proposal.Recipients],
        AgentSummaryTurn summary => [summary.Summary],
        _ => throw Unknown(turn),
    };

    private static AgentHistoryTurn WithTexts(AgentHistoryTurn turn, string[] texts) => turn switch
    {
        AgentWrittenTurn written => written with { Text = texts[0] },
        AgentProposedMessageTurn proposal => proposal with { Subject = texts[0], Recipients = texts[1..] },
        AgentSummaryTurn summary => summary with { Summary = texts[0] },
        _ => throw Unknown(turn),
    };

    private static ArgumentOutOfRangeException Unknown(AgentHistoryTurn turn) =>
        new(nameof(turn), turn.GetType().Name, "The turn is of a kind the history document does not write.");
}
