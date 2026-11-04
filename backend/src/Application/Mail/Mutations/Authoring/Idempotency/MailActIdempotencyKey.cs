// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations.Authoring.Idempotency;

/// <summary>The key a client generated once for one act on mail, with a digest of what the whole request asked.</summary>
/// <remarks>
/// The digest is what tells a transport retry from a key somebody reused: a repeat that asks for what the first
/// attempt asked for is answered, and one that asks for anything else under the same key is refused. It covers the
/// act and, for every message the request names, the message and what it was asked to become, in request order —
/// so it is the request's and not a message's, and every message's row carries the same one.
/// </remarks>
public sealed record MailActIdempotencyKey
{
    private MailActIdempotencyKey(string value, string fingerprint)
    {
        this.Value = value;
        this.Fingerprint = fingerprint;
    }

    /// <summary>Gets the text the client generated for the act.</summary>
    public string Value { get; }

    /// <summary>Gets the lower-case hexadecimal SHA-256 digest of what the whole request asked.</summary>
    public string Fingerprint { get; }

    /// <summary>Builds the key of one request.</summary>
    /// <param name="value">The text the client generated, at most <see cref="MailboxMutationRequester.MaximumIdentityLength" /> characters.</param>
    /// <param name="act">The route's act, such as the flags route or the moves route.</param>
    /// <param name="asked">What the request asked for each message it names, in request order.</param>
    /// <returns>The key carrying the digest of the act and everything asked.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank or longer than the bound, or <paramref name="act" /> is blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="asked" /> is <see langword="null" />.</exception>
    public static MailActIdempotencyKey Create(string value, string act, IEnumerable<string> asked)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(act);
        ArgumentNullException.ThrowIfNull(asked);

        if (value.Length > MailboxMutationRequester.MaximumIdentityLength)
        {
            throw new ArgumentException(
                $"A key carries at most {MailboxMutationRequester.MaximumIdentityLength} characters.",
                nameof(value));
        }

        // Each part is length-prefixed so two different requests cannot concatenate to the same text.
        var text = new StringBuilder();

        foreach (var part in asked.Prepend(act))
        {
            text.Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);
        }

        return new MailActIdempotencyKey(
            value,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))));
    }
}
