// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>The words one attachment yielded, beside what reading it came to.</summary>
/// <param name="Reading">What reading it came to.</param>
/// <param name="Text">The words, already cut to the ceiling the reading applied, and empty where it yielded none.</param>
/// <param name="Segments">Where each page, slide, or sheet begins in <paramref name="Text" />, in ascending order.</param>
/// <remarks>
/// The words are mail content — or, where <see cref="AttachmentTextReading.Kind" /> is a description, a machine's
/// account of mail content — and reach no log, metric, trace, or error message.
/// </remarks>
public sealed record AttachmentTextContent(
    AttachmentTextReading Reading,
    string Text,
    IReadOnlyList<AttachmentTextSegment> Segments);
