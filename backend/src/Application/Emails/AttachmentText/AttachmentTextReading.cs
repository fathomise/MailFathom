// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>What reading one attachment came to, with nothing of what it read.</summary>
/// <param name="AttachmentPosition">The attachment's zero-based walk position, which a download and a reading of its words are addressed with.</param>
/// <param name="Kind">Whether its words are the file's own or a model's account of a picture.</param>
/// <param name="Outcome">What happened, named as <see cref="DerivedAttachmentText.Outcome" /> names it.</param>
/// <param name="PageCount">How many pages, slides, or sheets it was read as, which is zero for anything unread.</param>
/// <param name="PageKind">What those places are, or <see langword="null" /> where the reading recorded none.</param>
/// <param name="TextLength">How many characters it yielded, which is zero where it yielded none.</param>
public sealed record AttachmentTextReading(
    int AttachmentPosition,
    AttachmentTextKind Kind,
    string Outcome,
    int PageCount,
    AttachmentTextSegmentKind? PageKind,
    int TextLength)
{
    /// <summary>Gets whether the attachment yielded any words to read.</summary>
    public bool WasRead => this.TextLength > 0;
}
