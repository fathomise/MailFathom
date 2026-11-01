// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Everything that decides the footer one user's message from one account carries.</summary>
/// <param name="Organization">The organization holding the account, or <see langword="null" /> for none.</param>
/// <param name="Template">That organization's template, or <see langword="null" /> where it set none.</param>
/// <param name="Own">The user's own footer for the account, or <see langword="null" /> where they wrote none.</param>
public sealed record MailFooterSources(Guid? Organization, MailFooterContent? Template, MailFooterContent? Own)
{
    /// <summary>Gets whether the organization's template is what the account's messages carry, which leaves no room for the user's own.</summary>
    public bool IsSetByOrganization => this.Organization is not null && this.Template is not null;
}
