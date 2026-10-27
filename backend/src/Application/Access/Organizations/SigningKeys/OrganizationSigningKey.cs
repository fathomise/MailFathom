// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Access.Organizations.SigningKeys;

/// <summary>One key an organization signs its accounts' mail with, as an administrator lists it.</summary>
/// <param name="KeyId">The key's identifier, which is also the selector every signature made with it names.</param>
/// <param name="CreatedAt">When the key was minted.</param>
/// <param name="RetiredAt">When a rotation replaced it, or <see langword="null" /> while no rotation has.</param>
/// <param name="RevokedAt">When an administrator revoked it, or <see langword="null" /> while nobody has.</param>
/// <remarks>
/// It carries no key material, neither half: what an administrator decides from a listing is which key to revoke and
/// whether rotation is keeping up, and neither needs the key itself. A retired or revoked key keeps its public half and
/// has lost its private half, which is what
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md">ADR 0042</see>
/// decides.
/// </remarks>
public sealed record OrganizationSigningKey(
    Guid KeyId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RetiredAt,
    DateTimeOffset? RevokedAt)
{
    /// <summary>Gets whether the organization signs with this key now: neither retired nor revoked.</summary>
    public bool IsActive => this.RetiredAt is null && this.RevokedAt is null;
}
