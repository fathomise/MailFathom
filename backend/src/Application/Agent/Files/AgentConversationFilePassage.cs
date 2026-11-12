// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Agent.Files;

/// <summary>One passage of a document a conversation holds, named by where it stands in the document's words.</summary>
/// <param name="File">The document.</param>
/// <param name="Ordinal">The passage's place among the document's passages, from zero.</param>
/// <param name="StartOffset">Where it begins in the document's words.</param>
/// <param name="EndOffset">Where it ends, exclusive.</param>
/// <param name="Segment">The page, slide, or sheet it lies on, or <see langword="null" /> where nothing marked one.</param>
/// <remarks>A passage never crosses from one page, slide, or sheet into the next, so the place it names is the whole of where it came from.</remarks>
public sealed record AgentConversationFilePassage(
    AgentConversationFileId File,
    int Ordinal,
    int StartOffset,
    int EndOffset,
    AttachmentTextSegment? Segment);
