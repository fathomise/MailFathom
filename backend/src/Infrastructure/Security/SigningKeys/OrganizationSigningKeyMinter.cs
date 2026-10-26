// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using MailFathom.Infrastructure.DataEncryption;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace MailFathom.Infrastructure.Security.SigningKeys;

/// <summary>Generates an organization's Ed25519 signing key and seals its private half under the data-encryption key ring.</summary>
/// <remarks>
/// <para>
/// The key is drawn from BouncyCastle's <see cref="SecureRandom" />, the cryptographically secure generator ADR 0042
/// names, and the private half leaves this type only sealed: bound to the key's own identifier under
/// <see cref="DataEncryptionPurpose.OrganizationSigningKey" />, so a sealed half moved onto another key's row fails to
/// open. The plaintext copy this type holds is erased before it returns.
/// </para>
/// <para>
/// A deployment without a key ring cannot seal, so it mints nothing; <see cref="CanMint" /> says so before any work is
/// spent, and every caller treats it as a reason to leave the organization without a key rather than as a failure.
/// </para>
/// </remarks>
internal sealed class OrganizationSigningKeyMinter
{
    private readonly DataEncryptionKeyRing keyRing;
    private readonly FieldEncryptor fieldEncryptor;

    /// <summary>Initializes the minter over the deployment's key ring.</summary>
    /// <param name="keyRing">The ring, read for whether it configures any key.</param>
    /// <param name="fieldEncryptor">What seals the private half under the ring's active key.</param>
    public OrganizationSigningKeyMinter(DataEncryptionKeyRing keyRing, FieldEncryptor fieldEncryptor)
    {
        ArgumentNullException.ThrowIfNull(keyRing);
        ArgumentNullException.ThrowIfNull(fieldEncryptor);

        this.keyRing = keyRing;
        this.fieldEncryptor = fieldEncryptor;
    }

    /// <summary>Gets whether the deployment configures a key ring a private half can be sealed under.</summary>
    public bool CanMint => this.keyRing.IsConfigured;

    /// <summary>Composes what an organization signing key's private half is bound to.</summary>
    /// <param name="keyId">The signing key's identifier.</param>
    /// <returns>The binding every seal and open of that private half uses.</returns>
    public static DataEncryptionBinding BindingFor(Guid keyId) =>
        DataEncryptionBinding.Create(DataEncryptionPurpose.OrganizationSigningKey, keyId.ToString("D"));

    /// <summary>Generates a key pair and seals its private half.</summary>
    /// <param name="keyId">The identifier the key will be stored under, which the seal is bound to.</param>
    /// <param name="cancellationToken">Cancels resolving the ring's active key.</param>
    /// <returns>The public half and the sealed private half.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the ring's active key cannot be resolved.</exception>
    public async Task<MintedOrganizationSigningKey> MintAsync(Guid keyId, CancellationToken cancellationToken)
    {
        var privateKey = new Ed25519PrivateKeyParameters(new SecureRandom());
        var privateHalf = privateKey.GetEncoded();

        try
        {
            var sealedPrivateHalf = await this.fieldEncryptor.SealAsync(BindingFor(keyId), privateHalf, cancellationToken);

            return new MintedOrganizationSigningKey(keyId, privateKey.GeneratePublicKey().GetEncoded(), sealedPrivateHalf);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateHalf);
        }
    }
}
