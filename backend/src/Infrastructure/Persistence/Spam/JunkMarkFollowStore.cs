// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Following;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>EF Core state for what following the source's junk marks still owes.</summary>
[RequiresIntegrationCoverage]
internal sealed class JunkMarkFollowStore(MailFathomDbContext dbContext) : IJunkMarkFollowStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<OwedJunkMarkChange>> GetOwedAsync(
        MailAccountId account,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var mailboxAccountId = account.Value;
        var rows = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == mailboxAccountId && email.JunkMarksChangedAt != null)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new { email.Id, ObservedAt = email.JunkMarksChangedAt!.Value })
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(static row => new OwedJunkMarkChange(StoredEmailId.Create(row.Id), row.ObservedAt))];
    }

    /// <inheritdoc />
    public async Task SettleAsync(OwedJunkMarkChange change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var storedEmailId = change.EmailId.Value;

        await dbContext.StoredEmails
            .Where(email => email.Id == storedEmailId && email.JunkMarksChangedAt == change.ObservedAt)
            .ExecuteUpdateAsync(
                update => update.SetProperty(email => email.JunkMarksChangedAt, (DateTimeOffset?)null),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task ForgetJunkFolderDeparturesAsync(
        MailAccountId account,
        DateTimeOffset before,
        CancellationToken cancellationToken)
    {
        var mailboxAccountId = account.Value;

        await dbContext.EmailJunkFolderDepartures
            .Where(departure => departure.MailboxAccountId == mailboxAccountId && departure.DepartedAt < before)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
