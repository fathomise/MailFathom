// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>The footer a message sent now from one account by one user carries.</summary>
/// <param name="Source">Whether the footer is the user's own or the organization's.</param>
/// <param name="Html">The footer's markup, sanitized and filled, referring to its images as <c>cid:</c> followed by their names.</param>
/// <param name="PlainText">The footer as the message's text part carries it, derived from the markup.</param>
/// <param name="Images">The images the markup refers to.</param>
/// <remarks>It carries a person's name, title, and phone number, so none of it reaches a log line or a failure message.</remarks>
public sealed record MailFooter(
    MailFooterSource Source,
    string Html,
    string PlainText,
    IReadOnlyList<MailFooterImage> Images);
