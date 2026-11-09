// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Categories;

/// <summary>Reads the categories recorded on a page of stored messages, in one query.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailCategoryRecordReader(MailFathomDbContext dbContext) : IEmailCategoryRecordReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<StoredEmailId, IReadOnlyList<string>>> ReadAsync(
        IReadOnlyCollection<StoredEmailId> emails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);

        var identifiers = emails.Select(static email => email.Value).ToArray();
        var records = await dbContext.EmailCategories
            .AsNoTracking()
            .Where(record => identifiers.Contains(record.StoredEmailId))
            .Select(static record => new { record.StoredEmailId, record.Category })
            .ToArrayAsync(cancellationToken);

        return records
            .GroupBy(static record => record.StoredEmailId)
            .ToDictionary(
                static group => StoredEmailId.Create(group.Key),
                static IReadOnlyList<string> (group) =>
                    [.. group.Select(static record => record.Category).Order(StringComparer.Ordinal)]);
    }
}
