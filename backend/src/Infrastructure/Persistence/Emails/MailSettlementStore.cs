// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Settlement;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads an account's settling messages off the settling queue and settles them with one conditional statement.</summary>
/// <remarks>
/// <para>
/// Where a move a rule or a verdict asked for is committed differs by custody phase, and the move's own record already
/// says which: a mirrored account's record carries the move to the server and the row reaches its destination when
/// synchronization observes the placement, while a held account commits the move with no record and a restoring one
/// commits it before opening a record marked as already committed. So a message waits on a record that commits nothing
/// locally, has not been abandoned or withdrawn, and has not yet reached the row — and the phase is never read.
/// </para>
/// <para>
/// A delete counts as a move here, because on most servers it is one into the trash and on every server it takes the
/// message out of the folder a reader would otherwise have seen it in first.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailSettlementStore(MailFathomDbContext dbContext) : IMailSettlementStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SettlingEmail>> ReadSettlingAsync(
        MailAccountId account,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var rows = await Settling(dbContext, account, resumeAfter, batchSize).ToArrayAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new SettlingEmail(
                StoredEmailId.Create(row.Id),
                account,
                MailFolderAlias.Create(row.Alias),
                row.StoredAt,
                row.ContentAvailability,
                row.Verdict,
                row.AreRulesFinished,
                row.IsTombstoned,
                row.AwaitsPlacement,
                row.IsSeen,
                row.ArrivedInInbox,
                row.FolderRole)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredEmailId>> SettleAsync(
        IReadOnlyList<StoredEmailId> emails,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(emails);

        if (emails.Count == 0)
        {
            return [];
        }

        var identities = emails.Select(static email => email.Value).ToArray();

        var settled = await dbContext.Database
            .SqlQuery<Guid>(MailSettlementStatement.Compose(identities, settledAt))
            .ToListAsync(cancellationToken);

        return [.. settled.Select(StoredEmailId.Create)];
    }

    /// <summary>Composes the read of one batch of an account's settling messages and what each still waits on.</summary>
    /// <param name="dbContext">The context the query is composed over.</param>
    /// <param name="account">The account whose arrivals are read.</param>
    /// <param name="resumeAfter">The last message the walk already read, or <see langword="null" /> to start at the head.</param>
    /// <param name="batchSize">The most messages the batch holds.</param>
    /// <returns>The query, which reads the settling queue's index and one probe of the mutation records per row.</returns>
    internal static IQueryable<SettlingEmailRow> Settling(
        MailFathomDbContext dbContext,
        MailAccountId account,
        StoredEmailId? resumeAfter,
        int batchSize)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var accountValue = account.Value;
        var resumeAfterId = resumeAfter?.Value;
        var relocate = MailboxMutation.Relocate.Name;
        var delete = MailboxMutation.Delete.Name;

        return dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.SettledAt == null
                && email.MailboxAccountId == accountValue
                && (resumeAfterId == null || email.Id > resumeAfterId))
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new SettlingEmailRow(
                email.Id,
                email.MailFolderBinding.Alias,
                email.StoredAt,
                email.ContentAvailability,
                email.SpamClassification == null ? null : email.SpamClassification.Verdict,
                email.RulesEvaluatedAt != null || email.FiledFromOutgoingEmailId != null,
                (email.RemoteExpungeObservedAt != null || email.AuthoredDeleteFlaggedAt != null)
                    && !email.IsRetainedAfterAuthoredDelete,
                dbContext.MailboxMutations.Any(mutation => mutation.StoredEmailId == email.Id
                    && (mutation.RequesterOrigin == MailboxMutationOrigin.Rule
                        || mutation.RequesterOrigin == MailboxMutationOrigin.Classification)
                    && mutation.LocalChange == MailboxMutationLocalChange.None
                    && mutation.Stage != MailboxMutationStage.Abandoned
                    && mutation.Stage != MailboxMutationStage.Cancelled
                    && ((mutation.Mutation == relocate && mutation.PlacementObservedAt == null)
                        || (mutation.Mutation == delete && mutation.Stage != MailboxMutationStage.Completed))),
                email.IsRemotelySeen,
                email.ArrivalFolder != null && email.ArrivalFolder.Role == MailFolderSpecialUse.Inbox,
                email.Folder.Role));
    }

    /// <summary>One settling message as the query reads it, before it is mapped onto the application's own type.</summary>
    internal sealed record SettlingEmailRow(
        Guid Id,
        string Alias,
        DateTimeOffset StoredAt,
        StoredEmailContentAvailability ContentAvailability,
        SpamVerdict? Verdict,
        bool AreRulesFinished,
        bool IsTombstoned,
        bool AwaitsPlacement,
        bool IsSeen,
        bool ArrivedInInbox,
        MailFolderSpecialUse? FolderRole);
}
