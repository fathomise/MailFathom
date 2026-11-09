// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Chat;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace MailFathom.Evaluations.StructuredAnswers;

/// <summary>One case put to an agent that answers with a structure, and how its answer is held to the case.</summary>
/// <param name="ScenarioName">The name the case is filed and cached under.</param>
/// <param name="Instruction">
/// The instruction the agent is composed with, which the judge grades the answer against, for whether the model under
/// test declares <c>StructuredOutputs</c> — an agent asking for a strict schema is told the shape by the schema rather than
/// in prose.
/// </param>
/// <param name="ComposeTurnAsync">
/// Composes the turn a deployment sends for the case to the model a plan names, through the agent's own composition of
/// it — guarded, and cut to what one request to that model may carry.
/// </param>
/// <param name="Compose">Composes the agent over the model under test, through the composition a deployment uses.</param>
/// <param name="Shortfall">
/// Reads the answer the way a deployment reads it and names what it gets wrong, or answers <see langword="null" /> when it
/// gets nothing wrong, given whether the model under test declares <c>StructuredOutputs</c>.
/// </param>
/// <param name="ExpectationMetricName">The name the deterministic verdict is recorded under.</param>
/// <param name="Evaluators">What the judge is asked on this case, which is nothing unless the case reads two ways.</param>
internal sealed record StructuredAnswerRequest(
    string ScenarioName,
    Func<bool, string> Instruction,
    Func<ChatGenerationPlan, CancellationToken, Task<string>> ComposeTurnAsync,
    Func<IChatClient, ChatGenerationPlan, ChatClientAgent> Compose,
    Func<string, bool, string?> Shortfall,
    string ExpectationMetricName,
    IReadOnlyList<IEvaluator> Evaluators);
