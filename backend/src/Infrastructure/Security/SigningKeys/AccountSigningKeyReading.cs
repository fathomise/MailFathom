// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.DataEncryption;

namespace MailFathom.Infrastructure.Security.SigningKeys;

/// <summary>What a mail account signs its outgoing mail with now: nothing, because it belongs to no organization; nothing yet, because its organization holds no active key; or that key.</summary>
/// <remarks>
/// A closed set of three cases rather than a key that may be absent, because the two ways of holding no key are told
/// apart by what follows: mail from an account in no organization is sent and counted nowhere, while mail from an
/// organization without a key is sent and counted.
/// </remarks>
internal abstract record AccountSigningKeyReading
{
    private AccountSigningKeyReading()
    {
    }

    /// <summary>Gets the reading of an account in no organization, which signs nothing and is counted nowhere.</summary>
    public static AccountSigningKeyReading OutsideEveryOrganization { get; } = new Unaffiliated();

    /// <summary>Gets the reading of an account whose organization holds no active key, which sends unsigned and is counted.</summary>
    public static AccountSigningKeyReading WithoutActiveKey { get; } = new Keyless();

    /// <summary>Reads an organization's active key.</summary>
    /// <param name="keyId">The key's identifier.</param>
    /// <param name="sealedPrivateKey">The key's private half, sealed.</param>
    /// <returns>The reading.</returns>
    public static AccountSigningKeyReading Active(Guid keyId, SealedValue sealedPrivateKey) =>
        new SigningKey(keyId, sealedPrivateKey);

    /// <summary>The account belongs to no organization.</summary>
    internal sealed record Unaffiliated : AccountSigningKeyReading;

    /// <summary>The account's organization holds no active key.</summary>
    internal sealed record Keyless : AccountSigningKeyReading;

    /// <summary>One active organization signing key, as signing needs it.</summary>
    /// <param name="KeyId">The key's identifier, which is also the selector a signature names and what the seal is bound to.</param>
    /// <param name="SealedPrivateKey">The private half, sealed under the key ring.</param>
    internal sealed record SigningKey(Guid KeyId, SealedValue SealedPrivateKey) : AccountSigningKeyReading;
}
