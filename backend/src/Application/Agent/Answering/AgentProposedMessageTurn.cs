// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Answering;

/// <summary>A message the Agent proposed earlier, reduced to what it offered.</summary>
/// <param name="Subject">The subject the proposed message carries.</param>
/// <param name="Recipients">The addresses it would be sent to, in the order the proposal named them.</param>
/// <remarks>
/// The subject and the addresses rather than the proposal itself, so a model is never shown an act in a form it could
/// mistake for one it may repeat without asking — and as values rather than a sentence, because a subject is whatever
/// the mail it was drawn from said and is written to a model as quoted text.
/// </remarks>
public sealed record AgentProposedMessageTurn(string Subject, IReadOnlyList<string> Recipients) : AgentHistoryTurn
{
    /// <inheritdoc />
    internal override int Length => this.Subject.Length + this.Recipients.Sum(static recipient => recipient.Length);
}
