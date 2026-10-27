// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads stored email summaries out of PostgreSQL by their primary key.</summary>
/// <remarks>
/// <para>
/// The lookup is a projection rather than a <c>FindAsync</c>, which is the privacy control the listing query applies for
/// the same reason: the query names the columns a summary publishes, so nothing here can reach the stored raw MIME, and
/// no entity enters the change tracker on a path that only reads.
/// </para>
/// <para>
/// A tombstone answers as an absent email, which is what keeps the content read consistent with the listing that led a
/// caller to it. It also makes the identifier of a deleted email indistinguishable from one that never existed, on the
/// same terms as an account this deployment no longer serves. A message still settling answers the same way, because
/// nothing has decided where it belongs yet and no listing has offered it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailSummaryReader(MailFathomDbContext dbContext) : IStoredEmailSummaryReader
{
    /// <inheritdoc />
    public async Task<EmailSummary?> FindAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken)
    {
        var row = await Readable(dbContext.StoredEmails.AsNoTracking())
            .Where(email => email.Id == storedEmailId.Value)
            .Select(StoredEmailSummaryRow.Projection)
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : (await StoredEmailSummaryRow.ToSummariesAsync([row], dbContext, cancellationToken))[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<StoredEmailId, EmailSummary>> ReadSummariesAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count is 0)
        {
            return new Dictionary<StoredEmailId, EmailSummary>();
        }

        var identities = storedEmailIds.Select(static storedEmailId => storedEmailId.Value).ToArray();

        var rows = await Readable(dbContext.StoredEmails.AsNoTracking())
            .Where(email => identities.Contains(email.Id))
            .Select(StoredEmailSummaryRow.Projection)
            .ToArrayAsync(cancellationToken);

        return (await StoredEmailSummaryRow.ToSummariesAsync(rows, dbContext, cancellationToken))
            .ToDictionary(static summary => summary.StoredEmailId);
    }

    /// <summary>Narrows to the rows a reader may be handed by identifier: still held, and settled.</summary>
    /// <param name="emails">The rows to narrow.</param>
    /// <returns>The narrowed rows.</returns>
    internal static IQueryable<StoredEmailEntity> Readable(IQueryable<StoredEmailEntity> emails) =>
        emails.Where(StoredEmailTombstone.IsNotTombstoned).Where(StoredEmailSettlement.IsSettled);
}
