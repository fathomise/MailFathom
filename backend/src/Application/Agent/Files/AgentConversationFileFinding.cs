// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>One passage a search of a conversation's files found.</summary>
/// <param name="Passage">Where it stands.</param>
/// <param name="FileName">The name of the file it stands in, where it has one.</param>
/// <param name="Text">Its words.</param>
/// <param name="FoundBy">How the search reached it.</param>
public sealed record AgentConversationFileFinding(
    AgentConversationFilePassage Passage,
    string? FileName,
    string Text,
    AgentConversationFileFoundBy FoundBy);
