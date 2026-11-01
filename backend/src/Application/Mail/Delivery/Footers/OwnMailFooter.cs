// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>A user's own footer for one of their accounts, as they would edit it.</summary>
/// <param name="Footer">The footer they wrote, or <see langword="null" /> where they wrote none.</param>
/// <param name="IsSetByOrganization">Whether the organization holding the account sets the footer instead, which leaves the user's own unwritable and unsent.</param>
public sealed record OwnMailFooter(MailFooterContent? Footer, bool IsSetByOrganization);
