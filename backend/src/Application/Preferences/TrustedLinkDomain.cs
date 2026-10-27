// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Application.Preferences;

/// <summary>A domain a person trusts the links to, so that a link to it or to any subdomain of it opens without the client asking first.</summary>
/// <remarks>
/// <para>
/// It is held as the name a browser compares, which is lowercase ASCII with an internationalized name in its punycode
/// form, because that is what the client reads out of a link's address before it asks — so a stored entry and a link
/// to the same place are the same string, and an entry written in its own script still matches.
/// </para>
/// <para>
/// What it refuses is what would make the entry trust more than the person meant: a scheme, a path, a port, or a
/// wildcard (a domain already covers every subdomain of it, so a wildcard adds nothing but a second spelling), an
/// address rather than a name, a single label, and a bare public suffix, each of which would let a link to anybody's
/// site open unasked.
/// </para>
/// <para>
/// The public suffix check is a short list of the second-level labels registries hand out under a country's code —
/// <c>co.uk</c>, <c>com.pl</c>, <c>org.au</c> — rather than the Public Suffix List. It refuses the suffixes somebody is
/// likely to type and lets a rarer one through; adopting the list itself is a dependency with a licence and an update
/// cadence of its own, and the cost of the gap is one person over-trusting their own links rather than anybody else's.
/// </para>
/// </remarks>
public sealed record TrustedLinkDomain
{
    /// <summary>The longest name accepted, in either form, which is the longest name the domain name system resolves.</summary>
    public const int LongestName = 253;

    private const int LongestLabel = 63;

    // Under a two-letter country code, these are the labels registries sell names beneath rather than names somebody
    // owns, so `co.uk` is a suffix and `nordwind.uk` is not.
    private static readonly HashSet<string> SecondLevelRegistryLabels =
    [
        "ac", "biz", "co", "com", "edu", "gen", "go", "gob", "gov", "gv", "info", "int", "ltd", "me", "mil", "ne", "net",
        "nhs", "nic", "nom", "or", "org", "plc", "sch",
    ];

    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true };

    // What separates a domain from the rest of an address: a scheme, a port, a path, a query, a fragment, a user name.
    private static readonly SearchValues<char> AddressPunctuation = SearchValues.Create(":/?#@\\");

    private TrustedLinkDomain(string name) => this.Name = name;

    /// <summary>Gets the domain as a browser compares it: lowercase ASCII, an internationalized name in its punycode form.</summary>
    public string Name { get; }

    /// <summary>Reads one entry a person wrote, or says why it is not a domain this list can hold.</summary>
    /// <param name="written">The entry as it was written.</param>
    /// <param name="domain">The domain, normalized, when the entry is one.</param>
    /// <param name="refusal">A sentence saying what is wrong with the entry, when it is not one.</param>
    /// <returns><see langword="true" /> when the entry is a domain this list can hold.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="written" /> is <see langword="null" />.</exception>
    public static bool TryRead(
        string written,
        [NotNullWhen(true)] out TrustedLinkDomain? domain,
        [NotNullWhen(false)] out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(written);

        domain = null;
        refusal = RefusalOf(written, out var name);

        if (refusal is not null)
        {
            return false;
        }

        domain = new TrustedLinkDomain(name);

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => this.Name;

    private static string? RefusalOf(string written, out string name)
    {
        name = string.Empty;

        if (written.Length == 0)
        {
            return "is empty.";
        }

        if (written.Length > LongestName)
        {
            return $"is longer than the {LongestName} characters a domain name can be.";
        }

        if (written.Contains('*', StringComparison.Ordinal))
        {
            return "carries a wildcard, which it does not need: a trusted domain already covers every subdomain of it.";
        }

        if (written.AsSpan().IndexOfAny(AddressPunctuation) >= 0)
        {
            return "carries a scheme, a path, a port, or a user name. Write the domain alone, such as example.com.";
        }

        if (!TryAscii(written, out name))
        {
            return "is not a domain name.";
        }

        var labels = name.Split('.');

        if (name.Length > LongestName || labels.Any(static label => label.Length is 0 or > LongestLabel))
        {
            return "is not a domain name.";
        }

        if (labels[^1].All(char.IsAsciiDigit))
        {
            return "is an address rather than a domain name.";
        }

        return labels.Length < 2 || IsBarePublicSuffix(labels)
            ? "is a public suffix, which would trust every site registered under it."
            : null;
    }

    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "The stored form is the one a browser gives a link's host, which is lower-case ASCII, so the client compares the two without converting either; the string is ASCII by this point, where the two casings are a bijection.")]
    private static bool TryAscii(string written, out string name)
    {
        try
        {
            name = Idn.GetAscii(written).ToLowerInvariant();

            return true;
        }
        catch (ArgumentException)
        {
            name = string.Empty;

            return false;
        }
    }

    private static bool IsBarePublicSuffix(string[] labels) =>
        labels is [var second, { Length: 2 }] && SecondLevelRegistryLabels.Contains(second);
}
