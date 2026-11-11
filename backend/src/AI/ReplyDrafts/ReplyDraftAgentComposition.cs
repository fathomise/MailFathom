// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.Domain.Access;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>Composes the agent that writes a reply out of the conversation it answers, and the one that rewrites passages of a draft.</summary>
/// <remarks>
/// It is composed the way every agent in this product is: one instruction carried as the agent's own, one declared tool
/// set, one name, and the registered instruction envelope wrapped around it. The tool set is empty, and that is the
/// capability rather than a description of one — the conversation is put to the agent in the turn, so an agent that
/// could look anything up would be an agent able to reach mail beyond the exchange it was asked to answer, and one
/// that could act would be a draft that sends itself.
/// </remarks>
internal static class ReplyDraftAgentComposition
{
    /// <summary>The name this agent is composed under.</summary>
    internal const string AgentName = "mailfathom-reply-draft";

    /// <summary>The name the agent rewriting passages of a draft is composed under.</summary>
    internal const string RewritingAgentName = "mailfathom-passage-rewrite";

    /// <summary>Composes the reply-drafting agent over a chat client.</summary>
    /// <param name="chatClient">The client the one call is made through.</param>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="language">The language the person writing reads, which the instruction states for a draft answering no correspondence.</param>
    /// <param name="turn">The turn the agent is put, whose numbers the response schema is built from.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the agent logs through.</param>
    /// <returns>The composed agent.</returns>
    /// <remarks>
    /// The schema and the instruction are decided by the same declaration: a model whose server honours a strict schema is
    /// sent one and told only what each field means, and any other model is sent none and told the shape in prose. A
    /// fallback is composed for its own declaration, so a chain mixing the two asks each model the way it can answer.
    /// </remarks>
    internal static ChatClientAgent Compose(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        UserLanguage language,
        ReplyDraftTurn turn,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(turn);

        var operation = new AgentOperation(
            AgentName,
            ReplyDraftInstructions.TextFor(language, plan.StructuredOutputs),
            [],
            plan.StructuredOutputs ? ReplyDraftSchema.For(turn) : null);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }

    /// <summary>Composes the agent that rewrites selected passages of a draft over a chat client.</summary>
    /// <param name="chatClient">The client the one call is made through.</param>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="instructionEnvelope">The preamble and postamble every agent here carries.</param>
    /// <param name="loggerFactory">The factory the agent logs through.</param>
    /// <returns>The composed agent.</returns>
    /// <remarks>
    /// A second agent of the same capability rather than the drafting agent asked differently, because its instruction and
    /// its answer are its own: one composed under its own name is one a trace, an evaluation, and a reader can tell apart.
    /// Its tool set is empty for the reason the drafting agent's is.
    /// </remarks>
    internal static ChatClientAgent ComposeRewriting(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var operation = new AgentOperation(
            RewritingAgentName,
            PassageRewriteInstructions.TextFor(plan.StructuredOutputs),
            [],
            plan.StructuredOutputs ? PassageRewriteSchema.ResponseFormat() : null);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
