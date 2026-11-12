// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Agent.Files;

/// <summary>A document a conversation holds, with the words read out of it and the places they came from.</summary>
/// <param name="File">The file.</param>
/// <param name="Text">Its words, in reading order, and empty where nothing was read.</param>
/// <param name="Segments">Where each page, slide, or sheet begins in <paramref name="Text" />, in order.</param>
public sealed record AgentConversationFileText(
    AgentConversationFile File,
    string Text,
    IReadOnlyList<AttachmentTextSegment> Segments);
