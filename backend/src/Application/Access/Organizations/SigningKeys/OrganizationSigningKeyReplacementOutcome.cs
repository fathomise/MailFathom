// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>Why an attempt to give an organization a fresh active signing key did or did not mint one.</summary>
public enum OrganizationSigningKeyReplacementOutcome
{
    /// <summary>A key was minted and is now the organization's active key; the one it replaced, if any, is retired.</summary>
    Replaced = 0,

    /// <summary>The organization already holds an active key young enough to keep, so nothing changed.</summary>
    /// <remarks>This is also what the replica that lost a race to mint or rotate reads: the winner's key is the active one.</remarks>
    AlreadyCurrent = 1,

    /// <summary>The deployment configures no data-encryption key ring, so a private half could not be sealed and nothing was minted.</summary>
    NoKeyRing = 2,

    /// <summary>The deployment holds no such organization.</summary>
    UnknownOrganization = 3,
}
