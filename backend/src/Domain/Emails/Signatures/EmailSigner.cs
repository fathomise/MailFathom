// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text;

namespace MailFathom.Domain.Emails.Signatures;

/// <summary>Who a signature names as its signer: an S/MIME certificate or an OpenPGP key, as far as it could be read.</summary>
/// <remarks>
/// <para>
/// Every field is personal data classified like the message itself, because a certificate or a user ID names a person.
/// None of it may be written to a log.
/// </para>
/// <para>
/// The text fields are bounded here rather than trusted to arrive small: they come out of a signature a stranger wrote,
/// and a value longer than its bound is cut to it instead of refusing the message. <see cref="KeyMaterial" /> is dropped
/// rather than cut, because half a certificate is not key material anybody can use.
/// </para>
/// </remarks>
public sealed record EmailSigner
{
    /// <summary>The longest name, issuer, or user ID kept.</summary>
    public const int MaximumNameLength = 1024;

    /// <summary>The longest address kept, which is RFC 5321's limit on a forward path.</summary>
    public const int MaximumAddressLength = 320;

    /// <summary>The longest serial number, fingerprint, key ID, or algorithm description kept.</summary>
    public const int MaximumIdentifierLength = 128;

    /// <summary>The largest certificate or public key kept as key material.</summary>
    public const int MaximumKeyMaterialLength = 64 * 1024;

    /// <summary>Gets the certificate's subject name, or the OpenPGP key's primary user ID.</summary>
    public string? Name { get; init => field = Bounded(value, MaximumNameLength); }

    /// <summary>Gets the address the certificate or key names — the one matching the displayed author where one does.</summary>
    public string? Address { get; init => field = Bounded(value, MaximumAddressLength); }

    /// <summary>Gets the name of the authority that issued the certificate; never present for an OpenPGP key.</summary>
    public string? Issuer { get; init => field = Bounded(value, MaximumNameLength); }

    /// <summary>Gets when the certificate became valid, or when the OpenPGP key was created.</summary>
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>Gets when the certificate stops being valid, or when the OpenPGP key expires; absent for a key that never does.</summary>
    public DateTimeOffset? ValidUntil { get; init; }

    /// <summary>Gets the certificate's serial number in hexadecimal; never present for an OpenPGP key.</summary>
    public string? SerialNumber { get; init => field = Bounded(value, MaximumIdentifierLength); }

    /// <summary>Gets the certificate's SHA-256 fingerprint, or the OpenPGP key's fingerprint, in hexadecimal.</summary>
    public string? Fingerprint { get; init => field = Bounded(value, MaximumIdentifierLength); }

    /// <summary>Gets the OpenPGP key ID in hexadecimal, which is all an unknown key's signature names; never present for S/MIME.</summary>
    public string? KeyId { get; init => field = Bounded(value, MaximumIdentifierLength); }

    /// <summary>Gets the public-key algorithm the signature was made with, for example <c>ECDSA P-256</c> or <c>Ed25519</c>.</summary>
    public string? PublicKeyAlgorithm
    {
        get;
        init => field = Bounded(value, MaximumIdentifierLength);
    }

    /// <summary>Gets the digest or hash algorithm the signature was made over, for example <c>SHA-256</c>.</summary>
    public string? DigestAlgorithm
    {
        get;
        init => field = Bounded(value, MaximumIdentifierLength);
    }

    /// <summary>Gets the signer's certificate in DER, or the OpenPGP transferable public key, empty where none was available.</summary>
    /// <remarks>
    /// Kept so the set of keys mail can later be encrypted to can be built from it, which is why it is the whole
    /// certificate or public key rather than a fingerprint of it.
    /// </remarks>
    public ReadOnlyMemory<byte> KeyMaterial
    {
        get;
        init => field = value.Length > MaximumKeyMaterialLength ? ReadOnlyMemory<byte>.Empty : value;
    }

    /// <summary>Makes text a certificate or key carried safe to store and bounds it.</summary>
    /// <param name="value">The text as the signature carried it.</param>
    /// <param name="maximumLength">The most UTF-16 code units kept.</param>
    /// <returns>The printable text cut on a text-element boundary, or <see langword="null" /> where nothing printable is left.</returns>
    /// <remarks>
    /// The text comes from whoever made the certificate, before anything trusts it. A control character, a formatting
    /// character, or an unpaired surrogate would either mislead whoever reads the signer or make PostgreSQL refuse the
    /// row, and a refused row is a message that is never stored. Walking scalars turns an unpaired surrogate into
    /// U+FFFD, and cutting on a text-element boundary never leaves half of a pair at the end.
    /// </remarks>
    internal static string? Bounded(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var printable = string.Concat(value.EnumerateRunes()
            .Where(scalar => !Rune.IsControl(scalar) && Rune.GetUnicodeCategory(scalar) is not UnicodeCategory.Format)
            .Select(scalar => scalar.ToString()))
            .Trim();

        var bounded = MailTextBounds.TruncateAtTextElementBoundary(printable, maximumLength);

        return bounded.Length == 0 ? null : bounded;
    }
}
