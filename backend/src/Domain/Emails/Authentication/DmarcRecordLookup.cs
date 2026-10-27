// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What looking for the <c>From</c> domain's DMARC record found: a record, none, or no answer.</summary>
/// <remarks>
/// <para>
/// The three are kept apart because they decide DKIM alignment differently. A record narrows the relation where it
/// asks for strict alignment. A domain that publishes none keeps the relation, so an aligned signature is enough there.
/// A record that could not be read aligns nothing, because a resolver timing out says nothing about what the domain
/// asked for — and it says nothing against the sender either, which is why it leaves the author not established rather
/// than failed.
/// </para>
/// <para>
/// <see cref="NotReadable" /> is also what a reading is handed where nothing looked at all, because no lookup was
/// needed or this deployment makes none. Both read the same way: nothing is known about the record.
/// </para>
/// </remarks>
public sealed record DmarcRecordLookup
{
    private DmarcRecordLookup(DmarcRecord? record, bool isReadable)
    {
        this.Record = record;
        this.IsReadable = isReadable;
    }

    /// <summary>Gets the answer that the domain and every parent the walk asked for publish no record.</summary>
    public static DmarcRecordLookup NotPublished { get; } = new(record: null, isReadable: true);

    /// <summary>Gets the answer that the walk ended on a name it could not read, or never ran.</summary>
    public static DmarcRecordLookup NotReadable { get; } = new(record: null, isReadable: false);

    /// <summary>Gets the record the walk found, or <see langword="null" /> where it found none.</summary>
    public DmarcRecord? Record { get; }

    /// <summary>Gets a value indicating whether the walk reached an answer, a record or the absence of one.</summary>
    public bool IsReadable { get; }

    /// <summary>Records the record the walk found.</summary>
    /// <param name="record">The one DMARC record published at the first name that held one.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    public static DmarcRecordLookup Found(DmarcRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new DmarcRecordLookup(record, isReadable: true);
    }

    /// <summary>Answers whether a verified DKIM signature for one name is aligned with the <c>From</c> domain under this answer.</summary>
    /// <param name="signingDomain">The <c>d=</c> domain of a signature that verified.</param>
    /// <param name="fromDomain">The domain of the first <c>From</c> mailbox.</param>
    /// <returns><see langword="true" /> when the signature speaks for the displayed author.</returns>
    public bool AlignsDkim(SenderDomain signingDomain, SenderDomain fromDomain) =>
        this.IsReadable
        && (this.Record?.DkimAlignment == DkimAlignmentMode.Strict
            ? signingDomain == fromDomain
            : signingDomain.SharesBranchWith(fromDomain));
}
