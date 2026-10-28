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
    private DmarcRecordLookup(DmarcRecord? record, bool isReadable, bool publishedAtFromDomain = false)
    {
        this.Record = record;
        this.IsReadable = isReadable;
        this.Policy = record switch
        {
            null => DmarcPolicyReading.NotEstablished,
            _ when publishedAtFromDomain => record.PolicyAtDomain,
            _ => record.PolicyBelowDomain,
        };
    }

    /// <summary>Gets the answer that the domain and every parent the walk asked for publish no record.</summary>
    public static DmarcRecordLookup NotPublished { get; } = new(record: null, isReadable: true);

    /// <summary>Gets the answer that the walk ended on a name it could not read, or never ran.</summary>
    public static DmarcRecordLookup NotReadable { get; } = new(record: null, isReadable: false);

    /// <summary>Gets the record the walk found, or <see langword="null" /> where it found none.</summary>
    public DmarcRecord? Record { get; }

    /// <summary>Gets a value indicating whether the walk reached an answer, a record or the absence of one.</summary>
    public bool IsReadable { get; }

    /// <summary>Gets what the record asks for the displayed domain's failing mail, under the tags that apply where it was found.</summary>
    /// <remarks>
    /// <see cref="DmarcPolicyReading.NotEstablished" /> wherever no record was found, because a domain that publishes
    /// none cannot be read as one that asks only to be monitored.
    /// </remarks>
    public DmarcPolicyReading Policy { get; }

    /// <summary>Records the record the walk found at the displayed domain itself.</summary>
    /// <param name="record">The one DMARC record published at the displayed domain.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    public static DmarcRecordLookup Found(DmarcRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new DmarcRecordLookup(record, isReadable: true, publishedAtFromDomain: true);
    }

    /// <summary>Records the record the walk found at a parent of the displayed domain.</summary>
    /// <param name="record">The one DMARC record published at the first parent that held one.</param>
    /// <returns>The answer, whose <see cref="Policy" /> reads the tags a record applies beneath its own domain.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    public static DmarcRecordLookup FoundAtParent(DmarcRecord record)
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
