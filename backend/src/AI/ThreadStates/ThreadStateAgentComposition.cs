// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Accounts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.ThreadStates;

/// <summary>Composes the agent that reads one conversation into where it stands.</summary>
/// <remarks>
/// It is composed the way every agent in this product is: one instruction carried as the agent's own, one declared tool
/// set, one name, and the registered instruction envelope wrapped around it. The tool set is empty, and that is the
/// capability rather than a description of one — the conversation is put to the agent in the turn, so an agent that
/// could look anything up would be an agent able to reach mail beyond the exchange it was asked about.
/// </remarks>
internal static class ThreadStateAgentComposition
{
    /// <summary>The name this agent is composed under.</summary>
    internal const string AgentName = "mailfathom-thread-state";

    /// <summary>Composes the thread-state agent over a chat client.</summary>
    /// <param name="chatClient">The client the one call is made through.</param>
    /// <param name="plan">The generation parameters this deployment configured.</param>
    /// <param name="language">The language the mailbox this derivation is of is read in, which its statements are written in.</param>
    /// <param name="thread">The conversation the agent is put, whose messages and kept topic the response schema is built from.</param>
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
        MailAccountLanguage language,
        DerivableThread thread,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(thread);

        var operation = new AgentOperation(
            AgentName,
            ThreadStateInstructions.TextFor(language, plan.StructuredOutputs),
            [],
            plan.StructuredOutputs ? ThreadStateSchema.For(thread.Messages.Count, topicShown: thread.KeptTopic is not null) : null);

        return AgentComposition.Compose(chatClient, plan, operation, instructionEnvelope, loggerFactory);
    }
}
