// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Delivery.Footers;

/// <summary>Whether a footer or a template was saved, and why not where it was not.</summary>
public enum MailFooterWriteOutcome
{
    /// <summary>It was saved.</summary>
    Saved = 0,

    /// <summary>The account is not one the user is assigned.</summary>
    AccountNotAssigned = 1,

    /// <summary>The organization holding the account sets every member's footer on it.</summary>
    SetByOrganization = 2,

    /// <summary>This deployment holds no such organization.</summary>
    OrganizationUnknown = 3,

    /// <summary>What was written is not a footer this deployment carries, for the reason the refusal names.</summary>
    Refused = 4,
}
