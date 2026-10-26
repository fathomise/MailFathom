// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using Org.BouncyCastle.Bcpg.OpenPgp;

namespace MailFathom.Infrastructure.Mail.Signatures;

/// <summary>Judges configured OpenPGP key material without handing the OpenPGP library's types to whoever configured it.</summary>
public static class OpenPgpPublicKeyMaterial
{
    /// <summary>Reports whether material holds at least one OpenPGP public key, armored or binary.</summary>
    /// <param name="material">The resolved material of one configured key.</param>
    /// <returns><see langword="true" /> when a verification could trust a key from it; otherwise <see langword="false" />.</returns>
    public static bool IsReadable(ReadOnlySpan<byte> material)
    {
        try
        {
            return OpenPgpSignatureVerifier.ReadPublicKeys(material).Count > 0;
        }
        catch (Exception exception) when (exception is PgpException or IOException or ArgumentException)
        {
            return false;
        }
    }
}
