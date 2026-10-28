// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What a domain's published DMARC record says about how a DKIM signature aligns with it, and whether it only monitors.</summary>
/// <remarks>
/// <para>
/// MailFathom reads a DMARC record to learn two things: the alignment mode, and whether the policy that applies to the
/// displayed domain is monitoring only. It evaluates no policy and computes no DMARC result. Every other tag is passed
/// over, an unknown one included, because RFC 9989 has a reader ignore what it does not understand.
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

    /// <summary>Gets what the record asks for mail failing at the domain it is published for, read from <c>p</c>.</summary>
    public DmarcPolicyReading PolicyAtDomain { get; init; } = DmarcPolicyReading.Enforcing;

    /// <summary>Gets what the record asks for mail failing at a name beneath the domain it is published for.</summary>
    /// <remarks>
    /// Read from <c>sp</c> where present and otherwise from <c>p</c>, and from <c>np</c> as well where present, because
    /// MailFathom does not ask whether the displayed name exists. It is monitoring only where every one of those reads
    /// <c>none</c>.
    /// </remarks>
    public DmarcPolicyReading PolicyBelowDomain { get; init; } = DmarcPolicyReading.Enforcing;

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
    /// <para>
    /// Every name is cut from the A-label form a comparison reads, written in lower case as DNS names usually are, so a
    /// domain displayed with another script's full stop is split into the same labels the comparison sees.
    /// </para>
    /// </remarks>
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The names are asked of DNS and compared as SenderDomain, never as these strings; they are ASCII by this point, where the two casings are a bijection, and lower case is how a queried name is written.")]
    public static IReadOnlyList<SenderDomain> DiscoveryNames(SenderDomain fromDomain)
    {
        var labels = fromDomain.NormalizedValue.ToLowerInvariant().Split('.');
        var firstParentLength = Math.Min(labels.Length - 1, LongestWalkedName);

        return
        [
            DomainOf(labels),
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

        var strict = string.Equals(TagValue(tags, "adkim"), "s", StringComparison.OrdinalIgnoreCase);
        var domainPolicy = TagValue(tags, "p");
        var subdomainPolicy = TagValue(tags, "sp") ?? domainPolicy;
        var nonexistentDomainPolicy = TagValue(tags, "np");

        return new DmarcRecord(strict ? DkimAlignmentMode.Strict : DkimAlignmentMode.Relaxed)
        {
            PolicyAtDomain = PolicyReadingOf(IsNone(domainPolicy)),
            PolicyBelowDomain = PolicyReadingOf(
                IsNone(subdomainPolicy) && (nonexistentDomainPolicy is null || IsNone(nonexistentDomainPolicy))),
        };
    }

    /// <summary>Reads the first value a tag was given, or nothing where the record does not carry it.</summary>
    private static string? TagValue((string Name, string? Value)[] tags, string name) =>
        tags.Where(tag => tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(static tag => tag.Value)
            .FirstOrDefault();

    /// <summary>Answers whether a policy value is <c>none</c>; a missing or unrecognized one is not, so it keeps a failure a failure.</summary>
    private static bool IsNone(string? policy) => string.Equals(policy, "none", StringComparison.OrdinalIgnoreCase);

    private static DmarcPolicyReading PolicyReadingOf(bool monitoringOnly) =>
        monitoringOnly ? DmarcPolicyReading.MonitoringOnly : DmarcPolicyReading.Enforcing;

    private static SenderDomain DomainOf(string[] labels) =>
        SenderDomain.TryCreate(string.Join('.', labels), out var domain)
            ? domain
            : throw new InvalidOperationException("A parent of a usable domain is itself a usable domain.");
}
