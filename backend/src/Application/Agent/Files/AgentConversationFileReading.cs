// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Agent.Files;

/// <summary>What reading one document's words came to, recorded once when it was taken in.</summary>
/// <param name="Outcome">What the attachment text reader said of it.</param>
/// <param name="PageCount">How many pages, slides, or sheets its words were read into, and zero where nothing marked any.</param>
/// <param name="PageKind">What those places are, or <see langword="null" /> where nothing marked any.</param>
/// <param name="TextLength">How many characters its words hold.</param>
public sealed record AgentConversationFileReading(
    AttachmentTextExtractionOutcome Outcome,
    int PageCount,
    AttachmentTextSegmentKind? PageKind,
    int TextLength)
{
    /// <summary>Gets whether the document yielded any words to read.</summary>
    public bool WasRead => this.TextLength > 0;

    /// <summary>Gets whether the document is too long to be given to a turn whole, so it is read a page at a time and searched instead.</summary>
    public bool IsLong => this.TextLength > AgentConversationFileBounds.MaximumWholeTextCharacters;
}
