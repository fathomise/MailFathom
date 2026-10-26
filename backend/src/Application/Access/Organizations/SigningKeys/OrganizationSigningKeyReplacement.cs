// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>What an attempt to give an organization a fresh active signing key did.</summary>
/// <param name="Outcome">Whether a key was minted, and why not when none was.</param>
/// <param name="ActiveKeyId">The organization's active key afterwards: the one minted, or the one that made minting unnecessary; <see langword="null" /> when it has none.</param>
/// <param name="RetiredKeyId">The key the new one retired, or <see langword="null" /> when the organization had no active key to retire or nothing was minted.</param>
public sealed record OrganizationSigningKeyReplacement(
    OrganizationSigningKeyReplacementOutcome Outcome,
    Guid? ActiveKeyId,
    Guid? RetiredKeyId)
{
    /// <summary>Describes an attempt that minted nothing.</summary>
    /// <param name="outcome">Why nothing was minted.</param>
    /// <returns>The result, naming no key.</returns>
    public static OrganizationSigningKeyReplacement Of(OrganizationSigningKeyReplacementOutcome outcome) =>
        new(outcome, ActiveKeyId: null, RetiredKeyId: null);
}
