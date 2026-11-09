// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Reads and writes the stored state a held account's change commits to, inside the change's own transaction.</summary>
/// <remarks>
/// The row is read tracked rather than projected, because one call can commit several changes to one message — a person
/// marking a message read and flagging it at once — and each has to see what the one before it staged. Every read and
/// write is scoped to the account as well as the identity, so a change can never reach another user's mail by naming it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class LocalEmailStateStore(TimeProvider timeProvider) : ILocalEmailStateStore
{
    /// <inheritdoc />
    public async Task<LocalEmailState?> ReadAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(session, account, email, cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new LocalEmailState(
            MailFolderBindingResolver.ToResolution(row.MailFolderBinding),

            // Both columns are cleared together when the drain observes its expunge, so either one answers; reading the
            // UID alone would say nothing about which UIDVALIDITY it belonged to. A local-only row keeps a UID its server
            // expunged, which holds nothing.
            row is { UidValidity: not null, Uid: not null, IsRetainedAfterRemoteDelete: false },
            MailFolderId.Create(row.FolderId),
            row.IsMarkedSeen,
            row.IsFlagged,
            EmailKeywords.Create(row.Keywords),
            row.IsRetainedAfterRemoteDelete);
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        LocalEmailState state,
        bool isHeldFolder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);

        var row = await FindAsync(session, account, email, cancellationToken)
            ?? throw new InvalidOperationException("A local change is written only to a message its own read found.");

        if (state.Folder is { } folder)
        {
            row.FolderId = folder.Value;

            if (isHeldFolder)
            {
                row.LocalMailFolderId = folder.Value;
            }
        }

        if (row.FlagsObservedAt is null)
        {
            row.FieldsSetBeforeFirstObservation |= FieldsChangedBy(row, state);
        }

        row.IsMarkedSeen = state.IsSeen;
        row.IsFlagged = state.IsFlagged;
        row.Keywords = [.. state.Keywords.Values];
    }

    /// <inheritdoc />
    public async Task MakeTombstoneAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        if (await FindAsync(session, account, email, cancellationToken) is { } row)
        {
            row.IsRetainedAfterRemoteDelete = false;
        }
    }

    /// <inheritdoc />
    public async Task EraseAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var row = await FindAsync(session, account, email, cancellationToken);

        if (row is null)
        {
            return;
        }

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // This is the one erasure path a person's own delete takes on a held account, so it owes the source removal
        // record every other erasure path owes: the row about to go is the only thing that still says where the
        // message is on the source, and the drain may not have reached it yet. A local-only row's source lost it already.
        if (!row.IsRetainedAfterRemoteDelete)
        {
            MailboxSourceRemovalRecords.Stage(sessionContext, [row], timeProvider.GetUtcNow());
        }

        await RemoveWithItsContentAsync(session, sessionContext, row, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MailFolderId?> ForgetAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var emailId = email.Value;
        var accountId = account.Value;

        // Not narrowed to what a reader may see, because the delete being settled is what has hidden this row.
        var row = await sessionContext.StoredEmails
            .SingleOrDefaultAsync(stored => stored.Id == emailId && stored.MailboxAccountId == accountId, cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (row.Uid is not null)
        {
            await this.RememberAsync(sessionContext, account, occurrence, cancellationToken);
        }

        await RemoveWithItsContentAsync(session, sessionContext, row, cancellationToken);

        return MailFolderId.Create(row.FolderId);
    }

    private async Task RememberAsync(
        MailFathomDbContext sessionContext,
        MailAccountId account,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        var binding = await MailFolderBindingResolver.GetRequiredAsync(
            sessionContext,
            account,
            occurrence.FolderResolutionId,
            cancellationToken);
        var uidValidity = occurrence.UidValidity.Value;
        var uid = occurrence.Uid.Value;

        if (await sessionContext.MailboxForgottenOccurrences.AnyAsync(
            forgotten => forgotten.MailFolderBindingId == binding.Id
                && forgotten.UidValidity == uidValidity
                && forgotten.Uid == uid,
            cancellationToken))
        {
            return;
        }

        sessionContext.MailboxForgottenOccurrences.Add(new MailboxForgottenOccurrenceEntity
        {
            Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
            MailboxAccountId = account.Value,
            MailFolderBindingId = binding.Id,
            UidValidity = uidValidity,
            Uid = uid,
        });
    }

    /// <summary>The cascade every stored message's erasure runs, in the order LocalMailFolderStore states it.</summary>
    /// <remarks>The user's storage figure and the content objects are read from the row the removal below only stages.</remarks>
    private static async Task RemoveWithItsContentAsync(
        IPersistenceSession session,
        MailFathomDbContext sessionContext,
        StoredEmailEntity row,
        CancellationToken cancellationToken)
    {
        Guid[] removedIds = [row.Id];

        await AccountStoredContentLedger.RemoveAsync(sessionContext, removedIds, cancellationToken);
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(session, removedIds, cancellationToken);
        sessionContext.StoredEmails.Remove(row);
    }

    private static EmailFieldsSetBeforeFirstObservation FieldsChangedBy(StoredEmailEntity row, LocalEmailState state) =>
        (row.IsMarkedSeen != state.IsSeen ? EmailFieldsSetBeforeFirstObservation.Seen : EmailFieldsSetBeforeFirstObservation.None)
        | (row.IsFlagged != state.IsFlagged ? EmailFieldsSetBeforeFirstObservation.Flagged : EmailFieldsSetBeforeFirstObservation.None)
        | (EmailKeywords.Create(row.Keywords) != state.Keywords ? EmailFieldsSetBeforeFirstObservation.Keywords : EmailFieldsSetBeforeFirstObservation.None);

    private static async Task<StoredEmailEntity?> FindAsync(
        IPersistenceSession session,
        MailAccountId account,
        StoredEmailId email,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var emailId = email.Value;
        var accountId = account.Value;

        return await sessionContext.StoredEmails
            .Include(static row => row.MailFolderBinding)
            .Where(row => row.Id == emailId && row.MailboxAccountId == accountId)
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
