// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Puts a footer into MIME, which is where the mail library stops and nothing above names a MIME type.</summary>
public interface IMailFooterComposer
{
    /// <summary>The comment written in front of the quotation in the markup alternative, where the author's own words end.</summary>
    /// <remarks>
    /// The footer goes in when the message is sent, from the stored MIME a draft or a recurring declaration carries, so
    /// that is where the end of the author's words has to be found again. A comment changes nothing a reader sees and
    /// nothing a reduction draws, and none can come from the quotation itself: the quoted markup is sanitized, which
    /// removes every comment, and a message quoted as text is encoded. So the last one in a body is always this one.
    /// </remarks>
    const string HtmlQuotationMarker = "<!--mailfathom-quotation-->";

    /// <summary>Appends a footer to a composed message, once, in its markup and its text alike.</summary>
    /// <param name="rawMime">The message as it was composed, without a footer.</param>
    /// <param name="footer">The footer to append.</param>
    /// <returns>The message carrying the footer, or the message unchanged where it has no body a footer belongs in.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="footer" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The footer goes under the author's own words and above any message they quote. In the markup it is wrapped in a
    /// <c>footer</c> element, which the sanitizer keeps and a later reading of the sent copy finds it by; in the text it
    /// opens with the <c>-- </c> line RFC 3676 names a signature by.
    /// </para>
    /// <para>
    /// Each image becomes an inline part of the message, in a <c>multipart/related</c> beside the markup, referred to by
    /// a content identifier minted for this message. A message with no markup carries the text alone, which has no
    /// image to show.
    /// </para>
    /// <para>
    /// A message that is not something an author wrote — a report a receipt is sent as — is returned unchanged.
    /// </para>
    /// </remarks>
    ReadOnlyMemory<byte> Append(ReadOnlyMemory<byte> rawMime, MailFooter footer);

    /// <summary>Composes a message carrying nothing but a footer, for a reader to see it as a message would show it.</summary>
    /// <param name="footer">The footer.</param>
    /// <returns>The message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="footer" /> is <see langword="null" />.</exception>
    ReadOnlyMemory<byte> ComposeAlone(MailFooter footer);
}
