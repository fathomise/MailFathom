// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Organizations;

/// <summary>One page of an organization's signing keys, in the order they were minted, as the deployment lists them.</summary>
/// <param name="OrganizationId">The organization.</param>
/// <param name="DisplayName">Its current display name.</param>
/// <param name="Keys">The page's keys, oldest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> when this page is the last.</param>
internal sealed record OrganizationSigningKeyList(
    [property: JsonPropertyName("organizationId")] Guid OrganizationId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("keys")] IReadOnlyList<OrganizationSigningKeyEntry>? Keys,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>One signing key as a listing reports it, which never carries its material.</summary>
/// <param name="KeyId">The key's identifier, which its signatures name as their selector.</param>
/// <param name="State"><c>active</c>, <c>retired</c>, or <c>revoked</c>.</param>
/// <param name="CreatedAt">When it was minted.</param>
/// <param name="RetiredAt">When a rotation replaced it.</param>
/// <param name="RevokedAt">When it was revoked.</param>
internal sealed record OrganizationSigningKeyEntry(
    [property: JsonPropertyName("keyId")] Guid KeyId,
    [property: JsonPropertyName("state")] string? State,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("retiredAt")] DateTimeOffset? RetiredAt,
    [property: JsonPropertyName("revokedAt")] DateTimeOffset? RevokedAt);

/// <summary>What a rotation left an organization signing with.</summary>
/// <param name="ActiveKeyId">The organization's active key now.</param>
/// <param name="RetiredKeyId">The key it retired, when it had one.</param>
internal sealed record OrganizationSigningKeyRotated(
    [property: JsonPropertyName("activeKeyId")] Guid ActiveKeyId,
    [property: JsonPropertyName("retiredKeyId")] Guid? RetiredKeyId);

/// <summary>What a revocation did.</summary>
/// <param name="RevokedKeyId">The key revoked.</param>
/// <param name="WasActive">Whether it was the key the organization signed with.</param>
/// <param name="ActiveKeyId">The replacement minted for a revoked active key, when one could be.</param>
internal sealed record OrganizationSigningKeyRevoked(
    [property: JsonPropertyName("revokedKeyId")] Guid RevokedKeyId,
    [property: JsonPropertyName("wasActive")] bool WasActive,
    [property: JsonPropertyName("activeKeyId")] Guid? ActiveKeyId);
