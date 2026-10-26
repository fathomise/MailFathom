// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Org.BouncyCastle.Bcpg.OpenPgp;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Judges configured OpenPGP key material without handing the OpenPGP library's types to whoever configured it.</summary>
public static class OpenPgpPublicKeyMaterial
{
    /// <summary>Reads the primary key IDs the material holds, armored or binary.</summary>
    /// <param name="material">The resolved material of one configured key.</param>
    /// <returns>
    /// Each primary key's ID as sixteen hexadecimal digits, so two configured entries holding one key can be told apart
    /// from two holding different keys; <see langword="null" /> when the material holds no OpenPGP public key a
    /// verification could trust.
    /// </returns>
    public static IReadOnlyList<string>? PrimaryKeyIdsOf(ReadOnlySpan<byte> material)
    {
        try
        {
            return [.. OpenPgpSignatureVerifier.ReadPublicKeys(material).Select(ring => $"{ring.GetPublicKey().KeyId:X16}")];
        }
        catch (Exception exception) when (exception is PgpException or IOException or ArgumentException)
        {
            return null;
        }
    }
}
