// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using DnsClient;
using MailFathom.Application.Mail;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Finds a displayed domain's DMARC record by RFC 9989's DNS tree walk, one bounded lookup per name.</summary>
/// <remarks>
/// <para>
/// The walk asks for <c>_dmarc.&lt;name&gt;</c> at each name <see cref="DmarcRecord.DiscoveryNames" /> lists and stops at
/// the first that answers with a record or cannot be read. <c>NXDOMAIN</c>, an empty answer, and a name holding no
/// DMARC record or more than one all mean <em>no record here, keep walking</em>; a timeout, <c>SERVFAIL</c>, or any
/// other error ends the walk as not readable, because carrying on past a name that may hold the record would read the
/// wrong one.
/// </para>
/// <para>
/// Answers share <see cref="DnsRecordCache" /> with the DKIM keys, keyed by the queried name: a record for its own
/// time-to-live, an absence for the cache's shorter time, and an error not at all, so a nameserver that recovers is
/// asked again on the next message.
/// </para>
/// <para>
/// Nothing raises and nothing is logged: a queried name carries a domain a message displays, which is personal data.
/// The caller's own cancellation is the one thing that propagates.
/// </para>
/// </remarks>
internal sealed class DnsDmarcRecordResolver : IDmarcRecordResolver
{
    private readonly IDnsQuery lookup;
    private readonly DnsRecordCache cache;
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan lookupDeadline;

    /// <summary>Initializes a resolver over one DNS client and the cache its answers are held in.</summary>
    /// <param name="lookup">Queries the resolver the operating system configured.</param>
    /// <param name="cache">Holds what was resolved for as long as it stays good.</param>
    /// <param name="timeProvider">Measures the deadline one lookup is given.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public DnsDmarcRecordResolver(IDnsQuery lookup, DnsRecordCache cache, TimeProvider timeProvider)
        : this(lookup, cache, timeProvider, DnsDkimPublicKeyRecordResolver.LookupDeadline)
    {
    }

    /// <summary>Initializes a resolver whose deadline is supplied, so a test need not state the real one.</summary>
    /// <param name="lookup">Queries a resolver.</param>
    /// <param name="cache">Holds what was resolved for as long as it stays good.</param>
    /// <param name="timeProvider">Measures the deadline one lookup is given.</param>
    /// <param name="lookupDeadline">How long one name is waited for.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal DnsDmarcRecordResolver(
        IDnsQuery lookup,
        DnsRecordCache cache,
        TimeProvider timeProvider,
        TimeSpan lookupDeadline)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.lookup = lookup;
        this.cache = cache;
        this.timeProvider = timeProvider;
        this.lookupDeadline = lookupDeadline;
    }

    /// <inheritdoc />
    public async Task<DmarcRecordLookup> ResolveAsync(SenderDomain fromDomain, CancellationToken cancellationToken)
    {
        foreach (var name in DmarcRecord.DiscoveryNames(fromDomain))
        {
            var answer = await this.ReadNameAsync(name, cancellationToken);

            if (answer != DmarcRecordLookup.NotPublished)
            {
                return answer;
            }
        }

        return DmarcRecordLookup.NotPublished;
    }

    /// <summary>Reads the one DMARC record a name holds, from the cache where it still holds an answer.</summary>
    /// <remarks>
    /// A name past what a resolver accepts once <c>_dmarc.</c> is prepended cannot exist in DNS, so it publishes nothing
    /// and the walk moves to its shorter parent.
    /// </remarks>
    private async Task<DmarcRecordLookup> ReadNameAsync(SenderDomain name, CancellationToken cancellationToken)
    {
        if (!SenderDomain.TryCreate($"{DmarcRecord.RecordLabel}.{name.Value}", out var recordName))
        {
            return DmarcRecordLookup.NotPublished;
        }

        if (this.cache.TryRead(recordName.NormalizedValue, out var cachedText))
        {
            return cachedText is not null && DmarcRecord.TryParse(cachedText, out var cachedRecord)
                ? DmarcRecordLookup.Found(cachedRecord)
                : DmarcRecordLookup.NotPublished;
        }

        if (await this.QueryAsync(recordName.Value, cancellationToken) is not { } response)
        {
            return DmarcRecordLookup.NotReadable;
        }

        return this.Hold(recordName.NormalizedValue, response);
    }

    /// <summary>Reads an answer by RFC 9989's rule, holding what it found: exactly one DMARC record, or none.</summary>
    private DmarcRecordLookup Hold(string recordName, IDnsQueryResponse response)
    {
        if (response.HasError && response.Header.ResponseCode != DnsHeaderResponseCode.NotExistentDomain)
        {
            return DmarcRecordLookup.NotReadable;
        }

        var published = response.Answers.TxtRecords()
            .Select(static record => (Text: string.Concat(record.Text), TimeToLive: record.InitialTimeToLive))
            .Where(static record => record.Text.Length <= DnsDkimPublicKeyRecordResolver.MaximumRecordLength)
            .Select(static record => (record.Text, record.TimeToLive, Record: DmarcRecord.TryParse(record.Text, out var parsed) ? parsed : null))
            .Where(static record => record.Record is not null)
            .Take(2)
            .ToArray();

        if (published is not [(var text, var timeToLive, { } record)])
        {
            this.cache.StoreAbsence(recordName);

            return DmarcRecordLookup.NotPublished;
        }

        this.cache.Store(recordName, text, TimeSpan.FromSeconds(timeToLive));

        return DmarcRecordLookup.Found(record);
    }

    /// <summary>Asks for one name, answering with nothing where the network does not answer in time.</summary>
    private async Task<IDnsQueryResponse?> QueryAsync(string recordName, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(this.lookupDeadline, this.timeProvider);
        using var query = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            return await this.lookup.QueryAsync(recordName, QueryType.TXT, QueryClass.IN, query.Token);
        }
        catch (DnsResponseException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
