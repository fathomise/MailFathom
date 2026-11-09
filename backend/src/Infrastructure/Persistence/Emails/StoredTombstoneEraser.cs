// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core erasure of the rows that have been tombstones for longer than their account keeps them.</summary>
/// <remarks>
/// The rows are read tracked and removed through the change tracker, as every other erasure of a stored message is. That
/// is the claim as well: the delete is conditional on each row's concurrency token, so a tombstone a reconciliation
/// brought back after the read fails the commit rather than being erased, and the ledger movement and the released
/// objects stage nothing that survives the rollback.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredTombstoneEraser(TimeProvider timeProvider) : IStoredTombstoneEraser
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="retention" /> is <see langword="null" />.</exception>
    public async Task<int> EraseAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailTombstoneRetention retention,
        DateTimeOffset tombstonedBefore,
        bool isSourceDrained,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retention);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var aged = await Aged(sessionContext, account, retention, tombstonedBefore, limit).ToListAsync(cancellationToken);

        await this.EraseWithTheirContentAsync(session, sessionContext, aged, isSourceDrained, cancellationToken);

        return aged.Count;
    }

    /// <inheritdoc />
    public async Task<int> SettleKeptAfterAuthoredDeleteAsync(
        IPersistenceSession session,
        MailAccountId account,
        AuthoredDeleteEmailDisposition disposition,
        bool isSourceDrained,
        int limit,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var kept = await KeptAfterAuthoredDelete(sessionContext, account, limit).ToListAsync(cancellationToken);

        if (disposition is AuthoredDeleteEmailDisposition.EraseLocalCopy)
        {
            await this.EraseWithTheirContentAsync(session, sessionContext, kept, isSourceDrained, cancellationToken);
        }
        else
        {
            kept.ForEach(static email => email.IsRetainedAfterAuthoredDelete = false);
        }

        return kept.Count;
    }

    /// <summary>Composes the query for one batch, so a test can hold it to what PostgreSQL is asked.</summary>
    /// <remarks>Rows are taken in identifier order, which is stable and walks an existing index but follows no tombstone's age, so what a night's bound leaves behind is not the youngest.</remarks>
    /// <param name="context">The context the rows are read and tracked through.</param>
    /// <param name="account">The account whose tombstones are read.</param>
    /// <param name="retention">The folders left alone.</param>
    /// <param name="tombstonedBefore">The moment a row must have become a tombstone before.</param>
    /// <param name="limit">The most rows the batch takes.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<StoredEmailEntity> Aged(
        MailFathomDbContext context,
        MailAccountId account,
        MailTombstoneRetention retention,
        DateTimeOffset tombstonedBefore,
        int limit)
    {
        var accountId = account.Value;

        return AccountScopedMailFolders
            .OutsideRetentionExclusions(
                context.StoredEmails.Where(email => email.MailboxAccountId == accountId),
                retention.ExcludedFolders)
            .Where(StoredEmailTombstone.SettledBefore(tombstonedBefore))
            .Where(email => !context.MailboxMutations.Any(mutation => mutation.StoredEmailId == email.Id
                && mutation.Stage != MailboxMutationStage.Completed
                && mutation.Stage != MailboxMutationStage.Abandoned
                && mutation.Stage != MailboxMutationStage.Cancelled))
            .OrderBy(email => email.Id)
            .Take(limit);
    }

    /// <summary>Composes the query for one batch of rows still kept under the value that no longer exists.</summary>
    /// <param name="context">The context the rows are read and tracked through.</param>
    /// <param name="account">The account whose rows are read.</param>
    /// <param name="limit">The most rows the batch takes.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<StoredEmailEntity> KeptAfterAuthoredDelete(
        MailFathomDbContext context,
        MailAccountId account,
        int limit)
    {
        var accountId = account.Value;

        return context.StoredEmails
            .Where(email => email.MailboxAccountId == accountId
                && email.IsRetainedAfterAuthoredDelete
                && (email.RemoteExpungeObservedAt != null || email.AuthoredDeleteFlaggedAt != null)
                && !context.MailboxMutations.Any(mutation => mutation.StoredEmailId == email.Id
                    && mutation.Stage != MailboxMutationStage.Completed
                    && mutation.Stage != MailboxMutationStage.Abandoned
                    && mutation.Stage != MailboxMutationStage.Cancelled))
            .OrderBy(email => email.Id)
            .Take(limit);
    }

    private async Task EraseWithTheirContentAsync(
        IPersistenceSession session,
        MailFathomDbContext sessionContext,
        List<StoredEmailEntity> rows,
        bool isSourceDrained,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        Guid[] erasedIds = [.. rows.Select(static email => email.Id)];

        // A row the source was seen to have expunged names nothing the source still holds, so it owes the drain nothing.
        if (isSourceDrained)
        {
            MailboxSourceRemovalRecords.Stage(
                sessionContext,
                [.. rows.Where(static email => email.RemoteExpungeObservedAt is null)],
                timeProvider.GetUtcNow());
        }

        await AccountStoredContentLedger.RemoveAsync(sessionContext, erasedIds, cancellationToken);
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(session, erasedIds, cancellationToken);
        sessionContext.StoredEmails.RemoveRange(rows);
    }
}
