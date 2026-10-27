// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>An administrative act on an organization's signing keys.</summary>
public enum OrganizationSigningKeyAct
{
    /// <summary>A fresh key was minted ahead of the schedule, retiring the active one.</summary>
    Rotated = 0,

    /// <summary>A key was revoked, so it confirms nothing verified from then on.</summary>
    Revoked = 1,
}
