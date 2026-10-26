// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Mail.Signing;

/// <summary>Identifies why a configured OpenPGP secret key could not sign an account's mail.</summary>
/// <remarks>The identity is the whole of what a refusal may carry: never the reference target, the passphrase, a key identifier, or any part of the material.</remarks>
public enum OpenPgpSigningKeyFailure
{
    /// <summary>The configured reference, or the reference to the passphrase, produced no material.</summary>
    /// <remarks>The resolution failure itself is reported by the startup check that walks every secret-bearing setting.</remarks>
    SecretNotResolvable = 0,

    /// <summary>The material is not an ASCII-armored OpenPGP private key block.</summary>
    /// <remarks>An armored public key is this failure too: it names the key and holds nothing to sign with.</remarks>
    NotAnArmoredSecretKey = 1,

    /// <summary>The material is armored as a private key block but does not parse as one.</summary>
    MaterialNotReadable = 2,

    /// <summary>The material holds more than one secret key, so which one the account signs with would depend on parse order.</summary>
    MaterialCarriesSeveralKeys = 3,

    /// <summary>The key, or every signing-capable key in it, carries a revocation.</summary>
    KeyRevoked = 4,

    /// <summary>The key, or every signing-capable key in it, has passed its expiry.</summary>
    KeyExpired = 5,

    /// <summary>No user ID of the key is the address the account sends from.</summary>
    /// <remarks>A recipient matches the signer against the message's author, so a key for another address signs mail its readers cannot attribute to this account.</remarks>
    UserIdDoesNotMatchAddress = 6,

    /// <summary>Neither the primary key nor any subkey is able and permitted to sign, or none holds its secret part.</summary>
    SigningKeyMissing = 7,

    /// <summary>The signing key is protected and no nested password block was configured for its passphrase.</summary>
    PassphraseMissing = 8,

    /// <summary>The signing key did not open with the passphrase the nested block supplied.</summary>
    PassphraseIncorrect = 9,
}
