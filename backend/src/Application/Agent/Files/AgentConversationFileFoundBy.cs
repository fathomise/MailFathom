// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>How a search of a conversation's files reached a passage.</summary>
public enum AgentConversationFileFoundBy
{
    /// <summary>The passage holds words of the query.</summary>
    Words = 0,

    /// <summary>The passage sits near the query in the active vector space.</summary>
    Meaning = 1,
}
