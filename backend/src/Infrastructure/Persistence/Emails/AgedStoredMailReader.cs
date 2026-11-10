// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core read of the visible mail past the age its account keeps.</summary>
[RequiresIntegrationCoverage]
internal sealed class AgedStoredMailReader : IAgedStoredMailReader
{
    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="retention" /> is <see langword="null" />.</exception>
    public async Task<IReadOnlyList<AgedStoredMail>> ReadAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailMessageRetention retention,
        DateTimeOffset receivedBefore,
        StoredEmailId? resumeAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retention);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var rows = await Aged(sessionContext, account, retention, receivedBefore, resumeAfter, limit)
            .AsNoTracking()
            .Select(static email => new
            {
                email.Id,
                email.FolderId,
                email.UidValidity,
                email.Uid,
                email.MailFolderBinding.Alias,
                Generation = email.MailFolderBinding.ResolutionGeneration,
                email.MailFolderBinding.RemotePath,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row =>
            {
                var folder = new MailFolderResolution(
                    MailFolderAlias.Create(row.Alias),
                    MailFolderResolutionGeneration.Create(row.Generation),
                    RemoteFolderPath.Create(row.RemotePath));

                return new AgedStoredMail(
                    StoredEmailId.Create(row.Id),
                    MailFolderId.Create(row.FolderId),
                    folder.Alias,
                    row is { UidValidity: { } uidValidity, Uid: { } uid }
                        ? EmailOccurrenceId.Create(account, folder.Id, ImapUidValidity.Create(uidValidity), ImapUid.Create(uid))
                        : null);
            }),
        ];
    }

    /// <summary>Composes the query for one batch, so a test can hold it to what PostgreSQL is asked.</summary>
    /// <param name="context">The context the rows are read through.</param>
    /// <param name="account">The account whose mail is read.</param>
    /// <param name="retention">The folders left alone.</param>
    /// <param name="receivedBefore">The moment a message must have been received before.</param>
    /// <param name="resumeAfter">The message to read past, or <see langword="null" />.</param>
    /// <param name="limit">The most rows the batch takes.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    internal static IQueryable<StoredEmailEntity> Aged(
        MailFathomDbContext context,
        MailAccountId account,
        MailMessageRetention retention,
        DateTimeOffset receivedBefore,
        StoredEmailId? resumeAfter,
        int limit)
    {
        var accountId = account.Value;
        var resumeAfterId = resumeAfter?.Value;
        var delete = MailboxMutation.Delete.Name;

        return AccountScopedMailFolders
            .OutsideRetentionExclusions(
                context.StoredEmails.Where(email => email.MailboxAccountId == accountId),
                retention.ExcludedFolders)
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => (email.ReceivedAt ?? email.StoredAt) < receivedBefore)
            .Where(email => resumeAfterId == null || email.Id > resumeAfterId)
            .Where(email => !context.MailboxMutations.Any(mutation => mutation.StoredEmailId == email.Id
                && ((mutation.Stage != MailboxMutationStage.Completed
                        && mutation.Stage != MailboxMutationStage.Abandoned
                        && mutation.Stage != MailboxMutationStage.Cancelled)
                    || (mutation.Stage == MailboxMutationStage.Abandoned
                        && mutation.RequesterOrigin == MailboxMutationOrigin.Retention
                        && mutation.Mutation == delete
                        && mutation.UidValidity == email.UidValidity
                        && mutation.Uid == email.Uid))))
            .Where(email => email.FiledFromOutgoingEmailId == null
                || !context.OutgoingEmails.Any(send => send.Id == email.FiledFromOutgoingEmailId
                    && (send.Stage == OutgoingEmailStage.Recorded || send.Stage == OutgoingEmailStage.TransmissionBegun)))
            .Where(email => !context.MailDrafts.Any(draft => draft.MailboxAccountId == accountId
                && draft.DiscardedAt == null
                && (draft.PromotedToOutgoingEmailId == null
                    || context.OutgoingEmails.Any(send => send.Id == draft.PromotedToOutgoingEmailId
                        && (send.Stage == OutgoingEmailStage.Recorded || send.Stage == OutgoingEmailStage.TransmissionBegun)))
                && (draft.FiledStoredEmailId == email.Id
                    || context.MailDraftCopies.Any(copy => copy.MailDraftId == draft.Id
                        && copy.FolderAlias == email.MailFolderBinding.Alias
                        && copy.PlacementUidValidity == email.UidValidity
                        && copy.PlacementUid == email.Uid))))
            .OrderBy(email => email.Id)
            .Take(limit);
    }
}
