// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>Which of a document's top-level blocks are the footer MailFathom appended to a message the reader sent.</summary>
/// <param name="FirstBlock">The position of the footer's first block in <see cref="MailDocument.Blocks" />.</param>
/// <param name="BlockCount">How many consecutive top-level blocks the footer is, at least one.</param>
/// <remarks>
/// A statement about blocks the document still carries rather than a removal of them, so a pane can fold the footer and
/// unfold it again, and a pane that does not read the statement draws the message as it always did.
/// </remarks>
public sealed record MailDocumentFooter(int FirstBlock, int BlockCount);
