// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.ReadSource;

/// <summary>What one read of a message's raw source came to: the octets, nothing at all, or a refusal by the screen.</summary>
/// <remarks>
/// Three types rather than one shape with an optional payload, because a refusal this deployment took about its own
/// content is a different fact from there being nothing to serve, and a caller has to answer each apart.
/// </remarks>
public abstract record EmailSourceReadOutcome
{
    private protected EmailSourceReadOutcome()
    {
    }
}

/// <summary>A source that was read, possibly cut at <see cref="EmailSourceReader.MaximumServedOctets" />.</summary>
public sealed record EmailSourceServed : EmailSourceReadOutcome
{
    /// <summary>Initializes a source that was read.</summary>
    /// <param name="octets">The octets served, already cut to the bound.</param>
    /// <param name="wholeOctetCount">How many octets the whole message holds.</param>
    public EmailSourceServed(ReadOnlyMemory<byte> octets, long wholeOctetCount)
    {
        this.Octets = octets;
        this.WholeOctetCount = wholeOctetCount;
    }

    /// <summary>Gets the first octets of the message as received, at most <see cref="EmailSourceReader.MaximumServedOctets" />.</summary>
    public ReadOnlyMemory<byte> Octets { get; }

    /// <summary>Gets how many octets the whole message holds, which exceeds the length of <see cref="Octets" /> where the source was cut.</summary>
    public long WholeOctetCount { get; }

    /// <summary>Gets whether the source was cut at <see cref="EmailSourceReader.MaximumServedOctets" />.</summary>
    public bool IsCut => this.Octets.Length < this.WholeOctetCount;
}

/// <summary>A message this caller has no source for, whatever the reason.</summary>
public sealed record EmailSourceUnavailable : EmailSourceReadOutcome;

/// <summary>A source withheld because a switched-on scanner covers the mail this reader reads.</summary>
/// <remarks>
/// A read of the message is redacted value by value, and a raw source cannot be: its body sits in transfer encodings no
/// scanner reads, so serving it beside a redacted body would hand out exactly what the redaction withholds. The reader
/// has already been shown that the message exists, so saying the source is withheld discloses nothing more.
/// </remarks>
public sealed record EmailSourceWithheld : EmailSourceReadOutcome;
