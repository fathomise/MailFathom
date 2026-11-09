// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>A block of header lines a message typed into its own body, named on the first of the blocks that form it.</summary>
/// <param name="BlockCount">How many consecutive blocks, counted from the one carrying this and at least one, form it.</param>
/// <param name="Author">The name the block's author line gives, as the message wrote it.</param>
/// <param name="Date">The block's date line, as the message wrote it and never parsed.</param>
/// <remarks>
/// A forward or a quoted reply from another mail client carries such a block, and it says nothing MailFathom checked:
/// anybody can type <c>From: The CEO</c>. So this is a statement about blocks the document still carries, as
/// <see cref="MailDocumentFooter" /> is, which lets a pane fold them and unfold every original line, and a pane that
/// does not read it draws the message as it always did. The author and the date are the sender's words and are drawn as
/// message text, never as a verified header.
/// </remarks>
public sealed record MailWrittenHeaders(int BlockCount, string Author, string Date);
