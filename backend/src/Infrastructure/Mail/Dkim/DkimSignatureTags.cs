// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Reads the tags of a DKIM signature value that the verification itself does not report back.</summary>
/// <remarks>
/// <para>
/// The verification itself is MimeKit's and reads every tag it needs; what it does not report back is the identity that
/// verified. RFC 6376 writes that as the <c>d=</c> tag, and the domain it names is the whole point of the check — so it
/// is read here, from the same header MimeKit was handed, rather than reconstructed from anything else on the message.
/// </para>
/// <para>
/// The header is attacker-controlled input like any other, and reading it is bounded accordingly: an over-long value is
/// passed over unread, and a tag naming a domain nothing can compare is refused rather than repaired. Either way the
/// signature contributes no identity, which is a verdict the reading above already has a value for.
/// </para>
/// <para>
/// Splitting on the semicolon is safe against every tag a signature carries, including the base64 ones: RFC 6376's
/// tag-value list reserves the semicolon as its separator and base64 does not use it.
/// </para>
/// </remarks>
internal static class DkimSignatureTags
{
    /// <summary>The tag naming the domain a signature was made for.</summary>
    private const string SigningDomainTag = "d";

    /// <summary>The tag naming the algorithm a signature was made with.</summary>
    private const string AlgorithmTag = "a";

    /// <summary>The longest header value read, past which the signature is passed over.</summary>
    /// <remarks>
    /// A signature holds a hash, a base64 signature, and a short list of signed header names, so a legitimate one sits
    /// well inside this. It matches the bound the trusted-header reading applies for the same reason: the value arrives
    /// from whoever wrote the message.
    /// </remarks>
    private const int MaximumHeaderValueLength = 4096;

    /// <summary>Reads the domain a signature was made for.</summary>
    /// <param name="headerValue">The <c>DKIM-Signature</c> header's value.</param>
    /// <param name="signingDomain">The domain the <c>d=</c> tag named, when it named a usable one.</param>
    /// <returns><see langword="true" /> when a usable signing domain was read; otherwise <see langword="false" />.</returns>
    public static bool TryReadSigningDomain(string headerValue, out SenderDomain signingDomain)
    {
        signingDomain = default;

        return TryReadTag(headerValue, SigningDomainTag, out var value)
            && SenderDomain.TryCreate(value, out signingDomain);
    }

    /// <summary>Answers whether a signature names one algorithm in its <c>a=</c> tag.</summary>
    /// <param name="headerValue">The signature header's value.</param>
    /// <param name="algorithm">The algorithm, as RFC 6376 and RFC 8463 write it.</param>
    /// <returns><see langword="true" /> when the tag names exactly that algorithm; otherwise <see langword="false" />.</returns>
    public static bool NamesAlgorithm(string headerValue, string algorithm) =>
        TryReadTag(headerValue, AlgorithmTag, out var value)
        && value.Equals(algorithm, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads one tag's value, unfolded, from a header value no longer than the bound.</summary>
    private static bool TryReadTag(string headerValue, string tagName, out string value)
    {
        value = string.Empty;

        if (headerValue.Length > MaximumHeaderValueLength)
        {
            return false;
        }

        foreach (var tag in headerValue.Split(';'))
        {
            var separatorIndex = tag.IndexOf('=', StringComparison.Ordinal);

            if (separatorIndex > 0 && tag[..separatorIndex].Trim().Equals(tagName, StringComparison.OrdinalIgnoreCase))
            {
                value = Unfolded(tag[(separatorIndex + 1)..]);

                return true;
            }
        }

        return false;
    }

    /// <summary>Removes the folding a long header may have been written across.</summary>
    /// <remarks>
    /// <para>
    /// RFC 6376 permits folding whitespace inside a tag's value, and a domain name can hold none of its own, so every
    /// whitespace character in the value is folding. Removing it is not repair: the whitespace was never part of the
    /// name, and the alternative is refusing perfectly ordinary signatures for where a mail transport broke the line.
    /// </para>
    /// <para>
    /// Treating it the same way the verifier's own tag reading does is the property that matters here, because the two
    /// readings must not disagree about which domain a signature was made for: this one decides the domain the verdict
    /// records, and MimeKit's decides the key the signature is checked against.
    /// </para>
    /// </remarks>
    private static string Unfolded(string value) =>
        string.Concat(value.Where(static character => !char.IsWhiteSpace(character)));
}
