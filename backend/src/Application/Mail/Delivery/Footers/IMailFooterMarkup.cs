// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Reads the markup somebody wrote for a footer, which is untrusted input like any message body.</summary>
public interface IMailFooterMarkup
{
    /// <summary>Sanitizes a footer's markup by the allow-list incoming mail is sanitized by, keeping references to its own images.</summary>
    /// <param name="html">The markup as it was written.</param>
    /// <param name="imageNames">The names of the images uploaded with it.</param>
    /// <returns>The sanitized markup, or the refusal that stopped it.</returns>
    /// <remarks>
    /// An image the markup refers to by an address or a <c>data:</c> URI is refused rather than dropped, and so is one
    /// referring by <c>cid:</c> to a name nothing was uploaded under: either is a footer that would not look the way its
    /// writer meant, and the writer can only fix what they are told about.
    /// </remarks>
    MailFooterMarkupReading Sanitize(string html, IReadOnlySet<string> imageNames);

    /// <summary>Derives the text a message's plain-text part carries for a footer.</summary>
    /// <param name="html">Sanitized footer markup.</param>
    /// <returns>The footer's words, line by line.</returns>
    string ReadPlainText(string html);
}
