// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>What a search of a conversation's files found, and whether it could search them by meaning at all.</summary>
/// <param name="Findings">The passages, best first.</param>
/// <param name="SearchedByMeaning">Whether the active vector space placed the query, which leaves the search by words alone where it could not.</param>
public sealed record AgentConversationFileSearchResult(
    IReadOnlyList<AgentConversationFileFinding> Findings,
    bool SearchedByMeaning);
