// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What a domain's published DMARC record says about how a DKIM signature aligns with it.</summary>
/// <remarks>
/// <para>
/// MailFathom reads a DMARC record to learn the alignment mode and nothing else: it evaluates no policy and computes no
/// DMARC result. Every other tag is passed over, an unknown one included, because RFC 9989 has a reader ignore what it
/// does not understand.
/// </para>
/// <para>
/// The record is found by RFC 9989's DNS tree walk, which <see cref="DiscoveryNames" /> states, and which needs no public
/// suffix list. A record is one TXT string that begins with <c>v=DMARC1</c>, which <see cref="TryParse" /> decides; a
/// name holding two of them holds none.
/// </para>
/// </remarks>
/// <param name="DkimAlignment">The alignment mode the record's <c>adkim</c> tag asks for.</param>
public sealed record DmarcRecord(DkimAlignmentMode DkimAlignment)
{
    /// <summary>The label every DMARC record is published beneath, which RFC 9989 fixes.</summary>
    public const string RecordLabel = "_dmarc";

    /// <summary>The longest label count the walk starts its parents from, which RFC 9989 fixes at seven.</summary>
    private const int LongestWalkedName = 7;

    /// <summary>Reads one TXT string as a DMARC record.</summary>
    /// <param name="text">One TXT string a <c>_dmarc</c> name holds, already joined from its parts.</param>
    /// <param name="record">The record, when the text is one.</param>
    /// <returns><see langword="true" /> when the text begins with <c>v=DMARC1</c>; otherwise <see langword="false" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="text" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A name is read as publishing a record only where exactly one of its TXT strings is one. Two are discarded together
    /// rather than one being chosen, because RFC 9989 asks for that and because whichever a resolver listed first is not
    /// a choice the domain made; the caller holding the whole answer applies that rule.
    /// </remarks>
    public static bool TryParse(string text, [NotNullWhen(true)] out DmarcRecord? record)
    {
        ArgumentNullException.ThrowIfNull(text);

        record = TryRead(text);

        return record is not null;
    }

    /// <summary>Names every domain whose <c>_dmarc</c> name the walk asks for, in the order it asks.</summary>
    /// <param name="fromDomain">The domain of the first <c>From</c> mailbox.</param>
    /// <returns>The <c>From</c> domain first, then its parents down to and including the single-label name.</returns>
    /// <remarks>
    /// A domain of eight or more labels is shortened to its last seven before the parents are walked, so the walk asks
    /// for at most eight names whatever the message displays. It ends at the first name holding a record; the caller
    /// stops there, and at the first name that could not be read.
    /// </remarks>
    public static IReadOnlyList<SenderDomain> DiscoveryNames(SenderDomain fromDomain)
    {
        var labels = fromDomain.Value.Split('.');
        var firstParentLength = Math.Min(labels.Length - 1, LongestWalkedName);

        return
        [
            fromDomain,
            .. Enumerable.Range(1, firstParentLength)
                .Reverse()
                .Select(length => DomainOf(labels[^length..])),
        ];
    }

    /// <summary>Reads one TXT string as a DMARC record, or nothing where it does not begin with the version tag.</summary>
    private static DmarcRecord? TryRead(string text)
    {
        var tags = text.Split(';')
            .Select(static tag => tag.Split('=', 2))
            .Select(static parts => (Name: parts[0].Trim(), Value: parts.Length == 2 ? parts[1].Trim() : null))
            .ToArray();

        if (tags is not [("v" or "V", "DMARC1"), ..])
        {
            return null;
        }

        var strict = tags
            .Where(static tag => tag.Name.Equals("adkim", StringComparison.OrdinalIgnoreCase))
            .Select(static tag => string.Equals(tag.Value, "s", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        return new DmarcRecord(strict ? DkimAlignmentMode.Strict : DkimAlignmentMode.Relaxed);
    }

    private static SenderDomain DomainOf(string[] labels) =>
        SenderDomain.TryCreate(string.Join('.', labels), out var domain)
            ? domain
            : throw new InvalidOperationException("A parent of a usable domain is itself a usable domain.");
}
