// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>Why revoking an organization's signing key did or did not change it.</summary>
public enum OrganizationSigningKeyRevocationOutcome
{
    /// <summary>The key is revoked and its private half deleted.</summary>
    Revoked = 0,

    /// <summary>The key was already revoked, so nothing changed.</summary>
    AlreadyRevoked = 1,

    /// <summary>The organization holds no such key, or the deployment holds no such organization.</summary>
    UnknownKey = 2,
}
