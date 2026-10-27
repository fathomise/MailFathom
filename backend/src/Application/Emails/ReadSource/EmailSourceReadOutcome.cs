// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.ReadSource;

/// <summary>What one read of a message's raw source came to: the octets, nothing at all, or a refusal by the screen.</summary>
/// <remarks>
/// Three answers rather than a nullable payload, for the reason <see cref="Screened" /> states: a refusal this deployment
/// took about its own content is a different fact from there being nothing to serve, and the two are answered apart.
/// </remarks>
public sealed record EmailSourceReadOutcome
{
    private EmailSourceReadOutcome(ReadOnlyMemory<byte>? octets, long wholeOctetCount, bool screened)
    {
        this.Octets = octets;
        this.WholeOctetCount = wholeOctetCount;
        this.Screened = screened;
    }

    /// <summary>Gets the first octets of the message as received, at most <see cref="EmailSourceReader.MaximumServedOctets" />, or <see langword="null" /> where nothing is served.</summary>
    public ReadOnlyMemory<byte>? Octets { get; }

    /// <summary>Gets how many octets the whole message holds, which exceeds the length of <see cref="Octets" /> where the source was cut.</summary>
    public long WholeOctetCount { get; }

    /// <summary>Gets whether the source was cut at <see cref="EmailSourceReader.MaximumServedOctets" />.</summary>
    public bool IsCut => this.Octets is { } octets && octets.Length < this.WholeOctetCount;

    /// <summary>Gets whether a switched-on scanner covers this message's mail, which is why its source is not served.</summary>
    /// <remarks>
    /// A read of the message is redacted value by value, and a raw source cannot be: its body sits in transfer encodings
    /// no scanner reads, so serving it beside a redacted body would hand out exactly what the redaction withholds. The
    /// reader has already been shown that the message exists, so saying the source is withheld discloses nothing more.
    /// </remarks>
    public bool Screened { get; }

    /// <summary>Describes a source that was read.</summary>
    /// <param name="octets">The octets served, already cut to the bound.</param>
    /// <param name="wholeOctetCount">How many octets the whole message holds.</param>
    /// <returns>The outcome.</returns>
    public static EmailSourceReadOutcome Served(ReadOnlyMemory<byte> octets, long wholeOctetCount) =>
        new(octets, wholeOctetCount, screened: false);

    /// <summary>Describes a message this caller has no source for, whatever the reason.</summary>
    /// <returns>The outcome.</returns>
    public static EmailSourceReadOutcome NothingToServe() => new(octets: null, wholeOctetCount: 0, screened: false);

    /// <summary>Describes a source withheld because a scanner covers its mail.</summary>
    /// <returns>The outcome.</returns>
    public static EmailSourceReadOutcome ScreenedOut() => new(octets: null, wholeOctetCount: 0, screened: true);
}
