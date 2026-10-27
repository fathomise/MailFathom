// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>Keeps what this deployment's spam verdicts did to each stored email, one row per email and act.</summary>
/// <remarks>
/// Two replicas acting on one message at once both find no row and both insert; the loser violates the primary key,
/// which <see cref="PersistenceConcurrencyConflicts" /> names, and the retry finds the winner's row and records nothing.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailSpamVerdictActStore(MailFathomDbContext dbContext, TimeProvider timeProvider) : ISpamVerdictActStore
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<SpamVerdictAct>> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken)
    {
        var storedEmailId = emailId.Value;
        var acts = await dbContext.EmailSpamVerdictActs
            .AsNoTracking()
            .Where(act => act.StoredEmailId == storedEmailId)
            .Select(act => act.Act)
            .ToArrayAsync(cancellationToken);

        return acts.ToHashSet();
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        SpamVerdictAct act,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var storedEmailId = emailId.Value;

        // A primary-key lookup, so a row this session staged already is found rather than added twice.
        if (await sessionContext.EmailSpamVerdictActs.FindAsync([storedEmailId, act], cancellationToken) is not null)
        {
            return;
        }

        sessionContext.EmailSpamVerdictActs.Add(new EmailSpamVerdictActEntity
        {
            StoredEmailId = storedEmailId,
            Act = act,
            RecordedAt = timeProvider.GetUtcNow(),
        });
    }
}
