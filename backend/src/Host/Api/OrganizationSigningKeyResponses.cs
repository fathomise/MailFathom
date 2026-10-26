// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access.Organizations.SigningKeys;

namespace MailFathom.Host.Api;

/// <summary>One page of an organization's signing keys, in the order they were minted, beside its current display name.</summary>
/// <param name="OrganizationId">The organization.</param>
/// <param name="DisplayName">Its current display name.</param>
/// <param name="Keys">The page's keys, oldest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> when this page is the last.</param>
internal sealed record OrganizationSigningKeyListResponse(
    Guid OrganizationId,
    string DisplayName,
    IReadOnlyList<OrganizationSigningKeyResponse> Keys,
    string? NextCursor)
{
    /// <summary>Maps a listing page to its transport shape.</summary>
    /// <param name="listing">The page.</param>
    /// <param name="nextCursor">The cursor the following page continues from.</param>
    /// <returns>The response.</returns>
    internal static OrganizationSigningKeyListResponse For(OrganizationSigningKeyListing listing, string? nextCursor) =>
        new(
            listing.OrganizationId,
            listing.DisplayName,
            [.. listing.Keys.Select(OrganizationSigningKeyResponse.For)],
            nextCursor);
}

/// <summary>One signing key as a listing reports it: never its material.</summary>
/// <param name="KeyId">The key's identifier, which is the selector its signatures name.</param>
/// <param name="State"><c>active</c>, <c>retired</c>, or <c>revoked</c>; a revoked key reads revoked whether or not it was retired first.</param>
/// <param name="CreatedAt">When it was minted.</param>
/// <param name="RetiredAt">When a rotation replaced it, or <see langword="null" />.</param>
/// <param name="RevokedAt">When it was revoked, or <see langword="null" />.</param>
internal sealed record OrganizationSigningKeyResponse(
    Guid KeyId,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RetiredAt,
    DateTimeOffset? RevokedAt)
{
    /// <summary>Maps a key to its transport shape.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The response.</returns>
    internal static OrganizationSigningKeyResponse For(OrganizationSigningKey key) => new(
        key.KeyId,
        key switch
        {
            { RevokedAt: not null } => "revoked",
            { RetiredAt: not null } => "retired",
            _ => "active",
        },
        key.CreatedAt,
        key.RetiredAt,
        key.RevokedAt);
}

/// <summary>What a rotation left the organization signing with.</summary>
/// <param name="ActiveKeyId">The organization's active key now.</param>
/// <param name="RetiredKeyId">The key it retired, or <see langword="null" /> when the organization had none active.</param>
internal sealed record OrganizationSigningKeyRotatedResponse(Guid ActiveKeyId, Guid? RetiredKeyId);

/// <summary>What a revocation did.</summary>
/// <param name="RevokedKeyId">The key revoked.</param>
/// <param name="WasActive">Whether it was the key the organization signed with.</param>
/// <param name="ActiveKeyId">The replacement minted for a revoked active key, or <see langword="null" /> when the revoked key was retired or no key ring let a replacement be minted.</param>
internal sealed record OrganizationSigningKeyRevokedResponse(Guid RevokedKeyId, bool WasActive, Guid? ActiveKeyId);
