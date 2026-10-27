// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;

namespace MailFathom.Application.Agent.Answering;

/// <summary>An earlier turn the person or the Agent wrote.</summary>
/// <param name="Author">Who wrote it.</param>
/// <param name="Text">What was written: a question, the Agent's own line, or the text of an answer it composed.</param>
public sealed record AgentWrittenTurn(AgentMessageAuthor Author, string Text) : AgentHistoryTurn
{
    /// <inheritdoc />
    internal override int Length => this.Text.Length;
}
