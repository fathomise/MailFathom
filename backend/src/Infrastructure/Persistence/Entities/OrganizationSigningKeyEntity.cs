// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One Ed25519 key an organization signs its accounts' mail with.</summary>
/// <remarks>
/// The public half is stored as it is, because nothing about it is secret. The private half is sealed under the
/// deployment's data-encryption key ring, bound to this row's identifier, and is deleted the moment the key is retired
/// or revoked; a check constraint refuses a retired or revoked row that still holds one. The organization's name is
/// never stored here: the key reaches it through <see cref="OrganizationId" />.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "EF Core materializes this entity through the DbSet and model metadata.")]
[RequiresIntegrationCoverage]
internal sealed class OrganizationSigningKeyEntity
{
    /// <summary>The table these rows live in, named here because a composed statement reads it.</summary>
    internal const string TableName = "organization_signing_keys";

    /// <summary>The key's identifier, a version 7 value, which is also the selector a signature names.</summary>
    public Guid Id { get; set; }

    /// <summary>The organization the key belongs to.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>The 32-byte Ed25519 public key.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "EF Core maps bytea columns to byte arrays.")]
    public required byte[] PublicKey { get; set; }

    /// <summary>The private key sealed under the key ring, or <see langword="null" /> once the key is retired or revoked.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "EF Core maps bytea columns to byte arrays.")]
    public byte[]? SealedPrivateKey { get; set; }

    /// <summary>The identifier of the ring key that sealed the private half, present exactly when the private half is.</summary>
    public string? DataEncryptionKeyId { get; set; }

    /// <summary>When the key was minted.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a rotation replaced the key, or <see langword="null" /> while none has.</summary>
    public DateTimeOffset? RetiredAt { get; set; }

    /// <summary>When an administrator revoked the key, or <see langword="null" /> while nobody has.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
