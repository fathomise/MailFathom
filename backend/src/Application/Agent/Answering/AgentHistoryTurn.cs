// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Answering;

/// <summary>One earlier turn of the conversation, as the model is shown it.</summary>
/// <remarks>
/// <para>
/// A turn is what was said rather than the entries it was written as, because what an earlier answer means to the next
/// question is what it said, not how it was laid out.
/// </para>
/// <para>
/// A closed set of three, because the three are written to a model differently and a turn that cannot say which it is
/// would have to be written as text somebody said: <see cref="AgentWrittenTurn" /> is what the person or the Agent
/// wrote, <see cref="AgentProposedMessageTurn" /> is an act the Agent offered, and <see cref="AgentSummaryTurn" /> is
/// a compaction's account of the turns before it, which nobody in the conversation said at all.
/// </para>
/// </remarks>
public abstract record AgentHistoryTurn
{
    private protected AgentHistoryTurn()
    {
    }

    /// <summary>Gets how many characters of text the turn carries, which is what its share of a turn's budget is estimated from.</summary>
    internal abstract int Length { get; }
}
