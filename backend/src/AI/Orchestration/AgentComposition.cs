// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.ProviderAdapters;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MailFathom.AI.Orchestration;

/// <summary>Composes an AI operation as an Agent Framework agent, and is the one place any agent here is composed.</summary>
/// <remarks>
/// <para>
/// One composition rather than a chat call written beside each feature, because what makes an operation safe is a
/// property of the composition rather than of its prose. An instruction cannot be reached from a tool result because of
/// where each is placed; the tool set <em>is</em> the capability, so an operation that only reads composes no mutating
/// tool; every call passes through whichever client the caller wrapped, which is where a run's spend is counted; and
/// what a run reports about cost, cancellation, and the model is what
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
/// says. A feature composing its own call would re-decide all four in silence, once per feature.
/// </para>
/// <para>
/// The instruction is carried as the agent's own rather than written into a message, which is what keeps it in a
/// position no tool result can be placed in. It is the operation's own text followed by
/// <see cref="AgentTurnDocument.DataClause" />, which is how the rule about quoted text is stated once for every
/// operation rather than once per instruction, and the envelope's two halves are placed around that. Nothing else
/// happens to any part: there is no substitution syntax and no placeholder, so an empty envelope composes exactly what
/// <see cref="InstructionFor" /> returns.
/// </para>
/// <para>
/// The envelope is asked at composition rather than read once at start, so an implementation whose answer varies per
/// person or per request changes what a run sends without any operation changing.
/// </para>
/// <para>
/// What comes here is an operation composed as an agent, which is not the same set as every call this product makes to
/// a model. A single judgement carrying its own instruction, offering no tool, and running no loop composes no agent at
/// all and reaches the chat client directly — <see cref="Retrieval.ModelJudgedKnowledgeSearch" /> is that shape today.
/// </para>
/// <para>
/// What the envelope adds rides inside the same instruction every turn carries, so it is sent through the client the
/// caller composed and counted by whatever that client counts. There is no path by which the envelope reaches a
/// provider outside what the run reports as spent.
/// </para>
/// </remarks>
internal static class AgentComposition
{
    /// <summary>Composes the instruction one operation runs under, before any envelope is placed around it.</summary>
    /// <param name="operationInstruction">What the operation tells the model about its task.</param>
    /// <returns>The operation's instruction followed by the data clause every operation carries.</returns>
    /// <remarks>
    /// Also what the relevance judgement, a call that composes no agent, sends as its system instruction, and what an
    /// operation recording which policy a run was conducted under digests. The image describer is the other call that
    /// composes no agent, and it still sends its own instruction alone.
    /// </remarks>
    internal static string InstructionFor(string operationInstruction)
    {
        ArgumentNullException.ThrowIfNull(operationInstruction);

        return $"{operationInstruction}\n\n{AgentTurnDocument.DataClause}";
    }

    /// <summary>Composes one operation's agent over one chat client.</summary>
    /// <param name="chatClient">The provider-neutral client every turn of the run is sent through, already carrying whatever the caller wrapped it in.</param>
    /// <param name="plan">The validated declaration: the generation parameters one call runs with.</param>
    /// <param name="operation">The operation being composed: its name, its own instruction, and its tool set.</param>
    /// <param name="instructionEnvelope">Supplies the text placed before and after the operation's instruction.</param>
    /// <param name="loggerFactory">Creates the loggers the framework's own components record through.</param>
    /// <returns>The composed agent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static ChatClientAgent Compose(
        IChatClient chatClient,
        ChatGenerationPlan plan,
        AgentOperation operation,
        IAgentInstructionEnvelope instructionEnvelope,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(instructionEnvelope);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var chatOptions = ChatGenerationParameterMapping.ToChatOptions(plan);

        chatOptions.Instructions = string.Concat(
            instructionEnvelope.Preamble,
            InstructionFor(operation.Instruction),
            instructionEnvelope.Postamble);
        chatOptions.Tools = [.. operation.Tools];

        var options = new ChatClientAgentOptions
        {
            Name = operation.Name,
            ChatOptions = chatOptions,
        };

        return new ChatClientAgent(chatClient, options, loggerFactory);
    }
}
