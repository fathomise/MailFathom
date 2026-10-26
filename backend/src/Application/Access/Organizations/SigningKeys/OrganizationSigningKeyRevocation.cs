// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>What revoking one of an organization's signing keys did.</summary>
/// <param name="Outcome">Whether the key was revoked, and why not when it was not.</param>
/// <param name="WasActive">Whether the revoked key was the one the organization signed with.</param>
/// <param name="ReplacementKeyId">The organization's active key after revoking its active one; <see langword="null" /> when the revoked key was retired, or when no key ring let a replacement be minted.</param>
public sealed record OrganizationSigningKeyRevocation(
    OrganizationSigningKeyRevocationOutcome Outcome,
    bool WasActive,
    Guid? ReplacementKeyId)
{
    /// <summary>Describes a revocation that changed nothing.</summary>
    /// <param name="outcome">Why nothing changed.</param>
    /// <returns>The result, naming no key.</returns>
    public static OrganizationSigningKeyRevocation Of(OrganizationSigningKeyRevocationOutcome outcome) =>
        new(outcome, WasActive: false, ReplacementKeyId: null);
}
