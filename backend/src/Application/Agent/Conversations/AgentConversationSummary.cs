// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Conversations;

/// <summary>One line of a person's conversation history: what a conversation is called, when it last moved, and whether it waits on the agent or on them.</summary>
/// <param name="Id">What addresses the conversation.</param>
/// <param name="Title">What it is called, and <see langword="null" /> while nothing has named it.</param>
/// <param name="StartedAt">When it was started, in UTC.</param>
/// <param name="LastActivityAt">When anything was last written into it, in UTC, which is what the history is ordered by.</param>
/// <param name="Archived">Whether the person has put it away, which is what separates the history they are working in from the rest.</param>
/// <param name="Running">Whether an answer is being composed in it right now.</param>
/// <param name="Status">The status line the answer being composed last reported, and <see langword="null" /> while it has reported none or nothing is being composed.</param>
/// <param name="Unread">Whether it holds a finished reply the person has not had in front of them, which is never the case while an answer is being composed.</param>
/// <remarks>
/// It carries no part of the conversation's answers — no block, no source — because a history list draws a name, a
/// time, and where the conversation stands. The title and the status line are still the person's own material, the
/// first composed from their question and the second naming what the agent is reading, so this is sensitive exactly
/// as the rest of the record is.
/// </remarks>
public sealed record AgentConversationSummary(
    AgentConversationId Id,
    string? Title,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    bool Archived,
    bool Running,
    string? Status,
    bool Unread);
