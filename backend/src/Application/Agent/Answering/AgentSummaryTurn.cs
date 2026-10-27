// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Answering;

/// <summary>A compaction's summary of the conversation before the turns that follow it.</summary>
/// <param name="Summary">The summary the compaction kept.</param>
/// <remarks>
/// Its own kind of turn rather than one the Agent wrote, because a summary is a model's account of what earlier turns
/// said, and those turns quoted mail: replayed as the Agent's own words, whatever a quoted message slipped into it would
/// come back as something the Agent concluded.
/// </remarks>
public sealed record AgentSummaryTurn(string Summary) : AgentHistoryTurn
{
    /// <inheritdoc />
    internal override int Length => this.Summary.Length;
}
