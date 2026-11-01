// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>What an organization's footer template is filled with for the member sending.</summary>
/// <param name="DisplayName">The member's display name, or <see langword="null" /> where none could be read.</param>
/// <param name="JobTitle">The member's job title, or <see langword="null" /> for none.</param>
/// <param name="PhoneNumber">The member's preferred phone number, or <see langword="null" /> for none.</param>
public sealed record MailFooterSender(string? DisplayName, string? JobTitle, string? PhoneNumber);
