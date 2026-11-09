// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Filing;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Keeps the append half of every message filed into another account, in PostgreSQL.</summary>
[RequiresIntegrationCoverage]
internal sealed class CrossAccountFilingStore(MailFathomDbContext readContext, TimeProvider timeProvider)
    : ICrossAccountFilingStore
{
    /// <inheritdoc />
    public async Task<CrossAccountFilingId> RecordAsync(
        IPersistenceSession session,
        NewCrossAccountFiling filing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filing);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var recordedAt = timeProvider.GetUtcNow();
        var entity = new CrossAccountFilingEntity
        {
            Id = Guid.CreateVersion7(recordedAt),
            DestinationAccountId = filing.DestinationAccountId.Value,
            DestinationStoredEmailId = filing.DestinationEmailId.Value,
            DestinationFolderPath = filing.DestinationFolderPath.Value,
            SourceAccountId = filing.SourceAccountId.Value,
            SourceStoredEmailId = filing.SourceEmailId.Value,
            RemovalMutationId = filing.RemovalRecordId?.Value,
            ErasesHeldSource = filing.ErasesHeldSource,
            Stage = CrossAccountFilingStage.Recorded,
            RecordedAt = recordedAt,
            StageChangedAt = recordedAt,
        };

        writeContext.CrossAccountFilings.Add(entity);

        if (filing.ErasesHeldSource
            && await writeContext.StoredEmails.FindAsync([filing.SourceEmailId.Value], cancellationToken) is { } source)
        {
            source.LocalDeleteCommittedAt ??= recordedAt;
        }

        return CrossAccountFilingId.Create(entity.Id);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutstandingCrossAccountFiling>> ReadOutstandingAsync(
        MailAccountId destination,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        var accountValue = destination.Value;

        var rows = await readContext.CrossAccountFilings
            .AsNoTracking()
            .Where(filing => filing.DestinationAccountId == accountValue
                && (filing.Stage == CrossAccountFilingStage.Recorded || filing.Stage == CrossAccountFilingStage.AppendIssued))
            .OrderBy(filing => filing.RecordedAt)
            .ThenBy(filing => filing.Id)
            .Take(maximumCount)
            .Select(filing => new
            {
                Filing = filing,
                Copy = readContext.StoredEmails
                    .Where(email => email.Id == filing.DestinationStoredEmailId)
                    .Select(email => new CopyRow(
                        email.MailFolderBinding,
                        email.IsMarkedSeen,
                        email.IsRemotelyAnswered,
                        email.IsFlagged,
                        email.IsDraft,
                        email.Keywords,
                        email.ReceivedAt,
                        email.SentAt,
                        email.StoredAt))
                    .FirstOrDefault(),
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new OutstandingCrossAccountFiling(
                ToRecord(row.Filing),
                row.Copy is null ? null : MailFolderBindingResolver.ToResolution(row.Copy.Binding),
                row.Copy?.State,
                row.Copy?.InternalDate ?? row.Filing.RecordedAt)),
        ];
    }

    /// <inheritdoc />
    public async Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken)
    {
        var entity = await RequireAsync(session, filing, cancellationToken);

        this.MoveTo(entity, CrossAccountFilingStage.AppendIssued);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        RemoteEmailPlacement placement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placement);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var entity = await RequireAsync(session, filing, cancellationToken);

        this.MoveTo(entity, CrossAccountFilingStage.Appended);

        if (await writeContext.StoredEmails.FindAsync([entity.DestinationStoredEmailId], cancellationToken) is not { } copy)
        {
            return;
        }

        if (placement is not { UidValidity: { } uidValidity, Uid: { } uid }
            || await writeContext.StoredEmails.AnyAsync(
                email => email.Id != copy.Id
                    && email.MailFolderBindingId == copy.MailFolderBindingId
                    && email.UidValidity == uidValidity.Value
                    && email.Uid == uid.Value,
                cancellationToken))
        {
            await LocalCopyErasure.EraseAsync(session, writeContext, copy.Id, cancellationToken);

            return;
        }

        copy.UidValidity = uidValidity.Value;
        copy.Uid = uid.Value;
    }

    /// <inheritdoc />
    public async Task AbandonAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var entity = await RequireAsync(session, filing, cancellationToken);

        this.MoveTo(entity, CrossAccountFilingStage.Abandoned);

        await LocalCopyErasure.EraseAsync(session, writeContext, entity.DestinationStoredEmailId, cancellationToken);

        if (entity.RemovalMutationId is { } removalId
            && await writeContext.MailboxMutations.FindAsync([removalId], cancellationToken) is { Stage: MailboxMutationStage.Recorded } removal)
        {
            removal.Stage = MailboxMutationStage.Cancelled;
            removal.StageChangedAt = entity.StageChangedAt;
        }

        await ShowSourceAgainAsync(writeContext, entity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CrossAccountFilingRecord?> FindByRemovalAsync(
        MailboxMutationRecordId removal,
        CancellationToken cancellationToken)
    {
        var removalValue = removal.Value;
        var entity = await readContext.CrossAccountFilings
            .AsNoTracking()
            .FirstOrDefaultAsync(filing => filing.RemovalMutationId == removalValue, cancellationToken);

        return entity is null ? null : ToRecord(entity);
    }

    /// <inheritdoc />
    public Task<bool> IsFiledFromOutgoingRecordAsync(StoredEmailId email, CancellationToken cancellationToken)
    {
        var emailValue = email.Value;

        return readContext.StoredEmails.AnyAsync(
            stored => stored.Id == emailValue && stored.FiledFromOutgoingEmailId != null,
            cancellationToken);
    }

    /// <summary>Shows the source where it was, unless another delete still outstanding on it keeps it hidden.</summary>
    private static async Task ShowSourceAgainAsync(
        MailFathomDbContext writeContext,
        CrossAccountFilingEntity filing,
        CancellationToken cancellationToken)
    {
        if (await writeContext.StoredEmails.FindAsync([filing.SourceStoredEmailId], cancellationToken) is not { } source)
        {
            return;
        }

        var deleteName = MailboxMutation.Delete.Name;
        var stillDeleted = await writeContext.MailboxMutations.AnyAsync(
            mutation => mutation.StoredEmailId == filing.SourceStoredEmailId
                && mutation.Id != filing.RemovalMutationId
                && mutation.Mutation == deleteName
                && mutation.Stage != MailboxMutationStage.Completed
                && mutation.Stage != MailboxMutationStage.Abandoned
                && mutation.Stage != MailboxMutationStage.Cancelled,
            cancellationToken);

        if (!stillDeleted)
        {
            source.LocalDeleteCommittedAt = null;
        }
    }

    private static async Task<CrossAccountFilingEntity> RequireAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        return await writeContext.CrossAccountFilings.FindAsync([filing.Value], cancellationToken)
            ?? throw new InvalidOperationException($"No cross-account filing carries the identifier {filing}.");
    }

    private static CrossAccountFilingRecord ToRecord(CrossAccountFilingEntity entity) => new()
    {
        Id = CrossAccountFilingId.Create(entity.Id),
        DestinationAccountId = MailAccountId.Create(entity.DestinationAccountId),
        DestinationEmailId = StoredEmailId.Create(entity.DestinationStoredEmailId),
        DestinationFolderPath = RemoteFolderPath.Create(entity.DestinationFolderPath),
        SourceAccountId = MailAccountId.Create(entity.SourceAccountId),
        SourceEmailId = StoredEmailId.Create(entity.SourceStoredEmailId),
        RemovalRecordId = entity.RemovalMutationId is { } removal ? MailboxMutationRecordId.Create(removal) : null,
        ErasesHeldSource = entity.ErasesHeldSource,
        Stage = entity.Stage,
        RecordedAt = entity.RecordedAt,
        StageChangedAt = entity.StageChangedAt,
    };

    private void MoveTo(CrossAccountFilingEntity entity, CrossAccountFilingStage stage)
    {
        if (entity.Stage is CrossAccountFilingStage.Appended or CrossAccountFilingStage.Abandoned)
        {
            throw new InvalidOperationException(
                $"The cross-account filing {entity.Id} has already ended as {entity.Stage}, so it cannot move to {stage}.");
        }

        entity.Stage = stage;
        entity.StageChangedAt = timeProvider.GetUtcNow();
    }

    private sealed record CopyRow(
        MailFolderBindingEntity Binding,
        bool IsSeen,
        bool IsAnswered,
        bool IsFlagged,
        bool IsDraft,
        string[] Keywords,
        DateTimeOffset? ReceivedAt,
        DateTimeOffset? SentAt,
        DateTimeOffset StoredAt)
    {
        internal RestoredEmailState State =>
            new(this.IsSeen, this.IsAnswered, this.IsFlagged, this.IsDraft, EmailKeywords.Create(this.Keywords));

        internal DateTimeOffset InternalDate => this.ReceivedAt ?? this.SentAt ?? this.StoredAt;
    }
}
