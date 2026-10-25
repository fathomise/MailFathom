// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Scheduling;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Reads an outbox as an operator and as an account's user see it, and writes the three decisions about one send, in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Both listings are projections rather than entity loads, and the recipients are the reason. The operator's names no
/// address — an address is personal data and nothing on that surface reports one — so a projection keeps them from
/// being read at all rather than read and then dropped. An account's outbox names the recipients its user wrote to, and
/// reads them for one page of sends in one statement. The stored MIME is left unread by both, because a page of it
/// would pull every queued message's bytes into memory.
/// </para>
/// <para>
/// Every write is one conditional update. The condition is the state the decision applies at together with the absence
/// of a live lease, which makes the statement the whole of the exclusion: a decision and a delivery attempt racing for
/// the same record produce one change, and the caller reads the row afterwards to find out that the attempt got there
/// first. Nothing here takes a lease of its own, because a decision is not an attempt and inventing one would let it
/// hold a record no worker could then claim.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class OutboxOperationStore(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IOutboxOperationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The count covers every stage rather than the outstanding ones alone, because the summary is what an operator
    /// reads to see that mail is leaving as well as that it is stuck. It is therefore a grouped count over the whole of
    /// what the deployment has sent, which is why it is asked for by a command an operator types rather than published
    /// on a collector's interval; the level a dashboard graphs is measured over the outstanding rows alone.
    /// </remarks>
    public async Task<IReadOnlyList<OutboxStageCount>> CountByStageAsync(
        MailAccountId? account,
        CancellationToken cancellationToken)
    {
        var sends = dbContext.OutgoingEmails.AsNoTracking();

        if (account is { } named)
        {
            var accountValue = named.Value;

            sends = sends.Where(message => message.MailboxAccountId == accountValue);
        }

        var counted = await sends
            .GroupBy(message => message.Stage)
            .Select(group => new { Stage = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);

        return [.. counted.Select(group => new OutboxStageCount(group.Stage, group.Count))];
    }

    /// <inheritdoc />
    /// <remarks>The read takes one row past the page, for the reason <see cref="KeysetPageSplit" /> states.</remarks>
    public async Task<OutboxPage> ReadPageAsync(OutboxQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await this.Filter(query)
            .OrderByDescending(message => message.RecordedAt)
            .ThenByDescending(message => message.Id)
            .Take(query.PageSize + 1)
            .Select(message => new OutboxRow(
                message.Id,
                message.MailboxAccountId,
                message.Stage,
                message.RequesterOrigin,
                message.AttemptCount,
                message.MimeByteLength,
                message.RecordedAt,
                message.StageChangedAt,
                message.AvailableAt,
                message.LastFailureCode,
                message.LastReplyCode))
            .ToArrayAsync(cancellationToken);

        var (pageRows, hasMore) = KeysetPageSplit.Of(rows, query.PageSize);

        return new OutboxPage(
            [.. pageRows.Select(ToEntry)],
            hasMore
                ? OutboxCursor.After(
                    pageRows[^1].RecordedAt,
                    OutgoingEmailId.Create(pageRows[^1].Id),
                    query.FilterFingerprint)
                : null);
    }

    /// <inheritdoc />
    public async Task<OutboxDecisionOutcome> CancelAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var cancelledAt = timeProvider.GetUtcNow();
        var recordId = outgoingEmailId.Value;
        var recorded = nameof(OutgoingEmailStage.Recorded);
        var cancelled = nameof(OutgoingEmailStage.Cancelled);

        var cancelledRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE outgoing_emails
             SET "Stage" = {cancelled},
                 "StageChangedAt" = {cancelledAt}
             WHERE "Id" = {recordId}
               AND "Stage" = {recorded}
               AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {cancelledAt})
             """,
            cancellationToken);

        return cancelledRows == 1
            ? OutboxDecisionOutcome.Accepted
            : await this.ExplainRefusalAsync(recordId, CancellableStages, refusalRestated: true, cancelledAt, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OutboxDecisionOutcome> RequeueAsync(
        OutgoingEmailId outgoingEmailId,
        bool refusalRestated,
        bool reachesDismissedSend,
        CancellationToken cancellationToken)
    {
        var requeuedAt = timeProvider.GetUtcNow();
        var recordId = outgoingEmailId.Value;
        var recorded = nameof(OutgoingEmailStage.Recorded);
        var transmissionBegun = nameof(OutgoingEmailStage.TransmissionBegun);
        var refused = nameof(OutgoingEmailStage.Refused);

        // A refusal the caller has not restated, and a dismissal the caller may not reach, are excluded in the statement
        // rather than checked beforehand, so the decision and its conditions stay one write: a send refused or dismissed
        // between the check and the update would otherwise be offered again on the strength of a state it left.
        var requeuedRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE outgoing_emails
             SET "Stage" = {recorded},
                 "StageChangedAt" = {requeuedAt},
                 "AvailableAt" = {requeuedAt},
                 "AttemptCount" = 0,
                 "LeaseOwner" = NULL,
                 "LeaseExpiresAt" = NULL,
                 "DismissedAt" = NULL,
                 "DismissedByUserId" = NULL
             WHERE "Id" = {recordId}
               AND ("Stage" IN ({recorded}, {transmissionBegun})
                    OR ("Stage" = {refused} AND {refusalRestated}))
               AND ("DismissedAt" IS NULL OR {reachesDismissedSend})
               AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {requeuedAt})
             """,
            cancellationToken);

        return requeuedRows == 1
            ? OutboxDecisionOutcome.Accepted
            : await this.ExplainRefusalAsync(recordId, RequeueableStages, refusalRestated, requeuedAt, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccountOutboxPage> ReadAccountOutboxAsync(OutboxQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.AccountId is not { } account)
        {
            throw new ArgumentException("An account's outbox is read for one account.", nameof(query));
        }

        if (query.Stage is { } stage && !OutgoingEmailRecord.OutboxStages.Contains(stage))
        {
            throw new ArgumentException($"An account's outbox holds no send at {stage}.", nameof(query));
        }

        var readAt = timeProvider.GetUtcNow();
        var outboxStages = OutgoingEmailRecord.OutboxStages.ToArray();

        var rows = await this.Filter(query)
            .Where(message => message.DismissedAt == null && outboxStages.Contains(message.Stage))
            .OrderByDescending(message => message.RecordedAt)
            .ThenByDescending(message => message.Id)
            .Take(query.PageSize + 1)
            .Select(message => new AccountOutboxRow(
                message.Id,
                message.Stage,
                message.LeaseExpiresAt,
                message.Subject,
                message.RecordedAt,
                message.DueAt,
                message.DueZoneId,
                message.AvailableAt,
                message.AttemptCount,
                message.LastFailureCode,
                message.LastReplyCode))
            .ToArrayAsync(cancellationToken);

        var (pageRows, hasMore) = KeysetPageSplit.Of(rows, query.PageSize);
        var recipients = await this.ReadRecipientsAsync([.. pageRows.Select(row => row.Id)], cancellationToken);

        return new AccountOutboxPage(
            [.. pageRows.Select(row => ToAccountOutboxEntry(account, row, recipients[row.Id], readAt))],
            hasMore
                ? OutboxCursor.After(
                    pageRows[^1].RecordedAt,
                    OutgoingEmailId.Create(pageRows[^1].Id),
                    query.FilterFingerprint)
                : null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountOutboxCount>> CountAccountOutboxesAsync(
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var named = accounts.Distinct().ToArray();
        if (named.Length == 0)
        {
            return [];
        }

        var accountValues = named.Select(account => account.Value).ToArray();
        var outboxStages = OutgoingEmailRecord.OutboxStages.ToArray();

        var counted = await dbContext.OutgoingEmails
            .AsNoTracking()
            .Where(message => accountValues.Contains(message.MailboxAccountId)
                && message.DismissedAt == null
                && outboxStages.Contains(message.Stage))
            .GroupBy(message => new { message.MailboxAccountId, message.Stage })
            .Select(group => new { group.Key.MailboxAccountId, group.Key.Stage, Count = group.Count() })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. named.Select(account => new AccountOutboxCount(
                account,
                Waiting: counted
                    .Where(group => group.MailboxAccountId == account.Value && group.Stage != OutgoingEmailStage.Refused)
                    .Sum(group => group.Count),
                Refused: counted
                    .Where(group => group.MailboxAccountId == account.Value && group.Stage == OutgoingEmailStage.Refused)
                    .Sum(group => group.Count))),
        ];
    }

    /// <inheritdoc />
    public async Task<OutboxDecisionOutcome> DismissAsync(
        OutgoingEmailId outgoingEmailId,
        UserId dismissedBy,
        CancellationToken cancellationToken)
    {
        var dismissedAt = timeProvider.GetUtcNow();
        var recordId = outgoingEmailId.Value;
        var userId = dismissedBy.Value;
        var transmissionBegun = nameof(OutgoingEmailStage.TransmissionBegun);
        var refused = nameof(OutgoingEmailStage.Refused);

        // The stage and the lease are conditions of the statement rather than of a read before it, for the reason every
        // decision here is one write: a send whose attempt is claimed between a check and the update would otherwise be
        // dismissed while it is being transmitted.
        var dismissedRows = await dbContext.Database.ExecuteSqlAsync(
            $"""
             UPDATE outgoing_emails
             SET "DismissedAt" = {dismissedAt},
                 "DismissedByUserId" = {userId}
             WHERE "Id" = {recordId}
               AND "DismissedAt" IS NULL
               AND ("Stage" = {refused}
                    OR ("Stage" = {transmissionBegun}
                        AND ("LeaseExpiresAt" IS NULL OR "LeaseExpiresAt" <= {dismissedAt})))
             """,
            cancellationToken);

        return dismissedRows == 1
            ? OutboxDecisionOutcome.Accepted
            : await this.ExplainRefusalAsync(recordId, DismissibleStages, refusalRestated: true, dismissedAt, cancellationToken);
    }

    /// <summary>The stages a withdrawal may be written from.</summary>
    private static OutgoingEmailStage[] CancellableStages => [OutgoingEmailStage.Recorded];

    /// <summary>The headers a row of an account's outbox names recipients from, the first that names anybody winning.</summary>
    private static OutgoingRecipientRole[] RowNamingOrder =>
        [OutgoingRecipientRole.To, OutgoingRecipientRole.Cc, OutgoingRecipientRole.Bcc];

    /// <summary>The stages a dismissal may be written at, before the lease narrows the second of them.</summary>
    private static OutgoingEmailStage[] DismissibleStages =>
        [OutgoingEmailStage.TransmissionBegun, OutgoingEmailStage.Refused];

    /// <summary>The stages a send may be offered again from, before the restatement rule narrows them.</summary>
    private static OutgoingEmailStage[] RequeueableStages =>
        [OutgoingEmailStage.Recorded, OutgoingEmailStage.TransmissionBegun, OutgoingEmailStage.Refused];

    /// <summary>Applies the filters a query names, leaving the ordering and the page bound to the caller.</summary>
    private IQueryable<OutgoingEmailEntity> Filter(OutboxQuery query)
    {
        var sends = dbContext.OutgoingEmails.AsNoTracking();

        if (query.AccountId is { } account)
        {
            var accountValue = account.Value;

            sends = sends.Where(message => message.MailboxAccountId == accountValue);
        }

        if (query.Stage is { } stage)
        {
            sends = sends.Where(message => message.Stage == stage);
        }

        // The keyset boundary is the pair the order is taken on, so a send recorded in the same instant as the last one
        // of the previous page is served exactly once rather than skipped or repeated. The identifier comparison is
        // evaluated by PostgreSQL as a `uuid` comparison, so it never has to agree with how the CLR happens to compare
        // two `Guid` values.
        if (query.Cursor is { } cursor)
        {
            var boundaryRecordedAt = cursor.RecordedAt;
            var boundaryId = cursor.OutgoingEmailId.Value;

            sends = sends.Where(message =>
                message.RecordedAt < boundaryRecordedAt
                || (message.RecordedAt == boundaryRecordedAt && message.Id < boundaryId));
        }

        return sends;
    }

    /// <summary>Says why a conditional update wrote nothing, which is the one thing the row count cannot.</summary>
    /// <remarks>
    /// Asked only after a write that changed no row, so the ordinary path costs one statement. The answer may already
    /// be stale by the time it is read — an attempt can claim the record in between — and that is acceptable: every
    /// refusal tells the caller the same thing, which is that this decision was not the one that took effect.
    /// </remarks>
    private async Task<OutboxDecisionOutcome> ExplainRefusalAsync(
        Guid recordId,
        IReadOnlyList<OutgoingEmailStage> allowedStages,
        bool refusalRestated,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.OutgoingEmails
            .AsNoTracking()
            .Where(message => message.Id == recordId)
            .Select(message => new { message.Stage, message.LeaseExpiresAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return OutboxDecisionOutcome.RecordUnknown;
        }

        // The restatement is reported before the lease, because it is the one refusal the operator can act on by
        // repeating the command differently; a lease is something they wait out rather than something they answer.
        if (!refusalRestated && row.Stage == OutgoingEmailStage.Refused)
        {
            return OutboxDecisionOutcome.RefusalNotRestated;
        }

        if (!allowedStages.Contains(row.Stage))
        {
            return OutboxDecisionOutcome.StageDoesNotAllowIt;
        }

        return row.LeaseExpiresAt is { } expiresAt && expiresAt > decidedAt
            ? OutboxDecisionOutcome.AttemptUnderWay
            : OutboxDecisionOutcome.StageDoesNotAllowIt;
    }

    /// <summary>Reads the recipients of the sends one page names, in the order each message names them.</summary>
    /// <remarks>
    /// Every recipient of the page is read, at most a page of sends by the bound one message may be addressed to, and the
    /// row is chosen from them in memory. Choosing in the statement would take a window per send and role, which is
    /// worth writing only once a page measured slow.
    /// </remarks>
    private async Task<ILookup<Guid, OutgoingRecipient>> ReadRecipientsAsync(
        IReadOnlyCollection<Guid> recordIds,
        CancellationToken cancellationToken)
    {
        if (recordIds.Count == 0)
        {
            return Array.Empty<OutgoingRecipient>().ToLookup(_ => Guid.Empty);
        }

        var rows = await dbContext.OutgoingEmailRecipients
            .AsNoTracking()
            .Where(recipient => recordIds.Contains(recipient.OutgoingEmailId))
            .OrderBy(recipient => recipient.OutgoingEmailId)
            .ThenBy(recipient => recipient.Ordinal)
            .Select(recipient => new
            {
                recipient.OutgoingEmailId,
                recipient.Ordinal,
                recipient.Address,
                recipient.Role,
                recipient.ContactId,
            })
            .ToArrayAsync(cancellationToken);

        return rows.ToLookup(
            row => row.OutgoingEmailId,
            row => StoredOutgoingRecipient.ToRecipient(
                "Outgoing email record",
                row.OutgoingEmailId,
                row.Ordinal,
                row.Address,
                row.Role,
                row.ContactId));
    }

    /// <summary>Rebuilds the account's outbox entry one projected row describes, naming the recipients of its first header that names anybody.</summary>
    private static AccountOutboxEntry ToAccountOutboxEntry(
        MailAccountId account,
        AccountOutboxRow row,
        IEnumerable<OutgoingRecipient> recipients,
        DateTimeOffset readAt)
    {
        var all = recipients.ToArray();
        var namedRole = RowNamingOrder.FirstOrDefault(role => all.Any(recipient => recipient.Role == role));
        var named = all
            .Where(recipient => recipient.Role == namedRole)
            .Take(AccountOutboxEntry.MaximumNamedRecipients)
            .ToArray();

        return new AccountOutboxEntry
        {
            OutgoingEmailId = OutgoingEmailId.Create(row.Id),
            AccountId = account,
            Stage = row.Stage,
            IsHeldByAttempt = row.LeaseExpiresAt is { } expiresAt && expiresAt > readAt,
            NamedRecipients = named,
            OtherRecipientCount = all.Length - named.Length,
            Subject = row.Subject,
            RecordedAt = row.RecordedAt,
            DueAt = row is { DueAt: { } dueAt, DueZoneId: { Length: > 0 } zoneId }
                ? ZonedInstant.Restore(dueAt, zoneId)
                : null,
            AvailableAt = row.AvailableAt,
            AttemptCount = row.AttemptCount,
            LastFailure = StoredFailureCode.ToErrorCode(row.LastFailureCode),
            LastReplyCode = row.LastReplyCode,
        };
    }

    /// <summary>Rebuilds the listing entry one projected row describes.</summary>
    private static OutboxEntry ToEntry(OutboxRow row) => new()
    {
        OutgoingEmailId = OutgoingEmailId.Create(row.Id),
        AccountId = MailAccountId.Create(row.MailboxAccountId),
        Stage = row.Stage,
        Origin = row.RequesterOrigin,
        AttemptCount = row.AttemptCount,
        MimeByteLength = row.MimeByteLength,
        RecordedAt = row.RecordedAt,
        StageChangedAt = row.StageChangedAt,
        AvailableAt = row.AvailableAt,
        LastFailure = StoredFailureCode.ToErrorCode(row.LastFailureCode),
        LastReplyCode = row.LastReplyCode,
    };

    /// <summary>The columns one row of an account's outbox is read from, its recipients being read beside them.</summary>
    private sealed record AccountOutboxRow(
        Guid Id,
        OutgoingEmailStage Stage,
        DateTimeOffset? LeaseExpiresAt,
        string? Subject,
        DateTimeOffset RecordedAt,
        DateTimeOffset? DueAt,
        string? DueZoneId,
        DateTimeOffset AvailableAt,
        int AttemptCount,
        int? LastFailureCode,
        int? LastReplyCode);

    /// <summary>The columns one listing row is read from, which are the ones that name no person.</summary>
    private sealed record OutboxRow(
        Guid Id,
        string MailboxAccountId,
        OutgoingEmailStage Stage,
        OutgoingEmailOrigin RequesterOrigin,
        int AttemptCount,
        long MimeByteLength,
        DateTimeOffset RecordedAt,
        DateTimeOffset StageChangedAt,
        DateTimeOffset AvailableAt,
        int? LastFailureCode,
        int? LastReplyCode);
}
