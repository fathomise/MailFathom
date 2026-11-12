// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Answering;
using MailFathom.Domain.Access;

namespace MailFathom.AI.AgentConversations;

/// <summary>What a run needs to write a report: the conversation it is stored into, the language its frame is set in, and the files a statement in it may cite.</summary>
/// <param name="Question">The question, which names the conversation and its person and the files it carries.</param>
/// <param name="Language">The person's language, which the report's frame is set in.</param>
/// <param name="Files">What the turn was given of the conversation's files, which names the ones the question carries.</param>
internal sealed record AgentConversationReporting(AgentQuestion Question, UserLanguage Language, AgentTurnFiles Files);
