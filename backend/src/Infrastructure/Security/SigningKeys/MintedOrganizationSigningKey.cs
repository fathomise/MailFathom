// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.DataEncryption;

namespace MailFathom.Infrastructure.Security.SigningKeys;

/// <summary>A freshly generated organization signing key, ready to be stored: its public half, and its private half sealed.</summary>
/// <param name="KeyId">The identifier the private half's seal is bound to.</param>
/// <param name="PublicKey">The 32-byte Ed25519 public key, stored as it is.</param>
/// <param name="SealedPrivateKey">The private key sealed under the ring's active key.</param>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The public key goes straight into a bytea parameter, which takes an array.")]
internal sealed record MintedOrganizationSigningKey(Guid KeyId, byte[] PublicKey, SealedValue SealedPrivateKey);
