// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Records every change MailFathom asks a mail server to make, in PostgreSQL, before it is asked for.</summary>
/// <remarks>
/// <para>
/// The write paths use the context enlisted in the caller's session, so a record is only ever written inside the
/// transaction the caller opened; the read path uses the scoped context, because it joins no transaction.
/// </para>
/// <para>
/// The idempotency identity is not checked and then written. The check exists — a request that already has a record
/// reads it back rather than inserting a second — but it is the unique index that decides, because two callers can pass
/// any application-level check between reading and writing and only the constraint closes that window. A loser is
/// reported as an optimistic conflict, and the retry finds the winner's row.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxMutationRecordStore(
    MailFathomDbContext readContext,
    IMailboxMutationAuditSettingsReader auditSettingsReader,
    TimeProvider timeProvider)
    : IMailboxMutationRecordStore
{
    /// <inheritdoc />
    public async Task<MailboxMutationRecord> OpenAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        DateTimeOffset? heldUntil,
        MailboxMutationLocalChange localChange,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var folder = await MailFolderBindingResolver.GetRequiredAsync(
            writeContext,
            request.Account,
            request.Occurrence.FolderResolutionId,
            cancellationToken);

        var existing = await FindByIdentityAsync(writeContext, request, folder.Id, cancellationToken);
        if (existing is not null)
        {
            return MailboxMutationRecordMapping.ToRecord(existing, folder);
        }

        var storedEmail = await RequireStoredEmailAsync(writeContext, request, cancellationToken);
        var entity = this.NewEntity(request, folder, storedEmail, heldUntil, localChange);

        writeContext.MailboxMutations.Add(entity);

        return MailboxMutationRecordMapping.ToRecord(entity, folder);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The earlier changes are read tracked, beside the ones this transaction has already staged, because a batch that
    /// changes two values of one message in one commit has the first of them only in the change tracker.
    /// </remarks>
    public async Task<OpenedMailboxMutationRecord> OpenOverlayAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        DateTimeOffset? heldUntil,
        MailboxMutationOverlay replaced,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(replaced);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var folder = await MailFolderBindingResolver.GetRequiredAsync(
            writeContext,
            request.Account,
            request.Occurrence.FolderResolutionId,
            cancellationToken);

        if (await FindByIdentityAsync(writeContext, request, folder.Id, cancellationToken) is { } existing)
        {
            return new OpenedMailboxMutationRecord(MailboxMutationRecordMapping.ToRecord(existing, folder), IsNew: false);
        }

        var storedEmail = await RequireStoredEmailAsync(writeContext, request, cancellationToken);
        var earlier = await ReadOutstandingOverlaysAsync(writeContext, storedEmail.Id, cancellationToken);
        var entity = this.NewEntity(request, folder, storedEmail, heldUntil, MailboxMutationLocalChange.AlreadyCommitted);

        WriteObservations(entity, ObservationCarriedOver(request.Mutation, earlier, replaced));
        this.WithdrawSuperseded(request, earlier);

        if (request.Mutation == MailboxMutation.Delete
            && request.LocalDisposition is not AuthoredDeleteEmailDisposition.RetainLocalCopy)
        {
            storedEmail.LocalDeleteCommittedAt ??= entity.RecordedAt;
        }

        writeContext.MailboxMutations.Add(entity);

        return new OpenedMailboxMutationRecord(MailboxMutationRecordMapping.ToRecord(entity, folder), IsNew: true);
    }

    /// <inheritdoc />
    public async Task<MailboxMutationRecord> RecordLocalCopyAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        StoredEmailId copy,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        entity.LocalCopyStoredEmailId = copy.Value;

        return MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolderBinding);
    }

    private MailboxMutationEntity NewEntity(
        MailboxMutationRequest request,
        MailFolderBindingEntity folder,
        StoredEmailEntity storedEmail,
        DateTimeOffset? heldUntil,
        MailboxMutationLocalChange localChange)
    {
        var recordedAt = timeProvider.GetUtcNow();

        return new MailboxMutationEntity
        {
            Id = Guid.CreateVersion7(recordedAt),
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            MailboxAccountId = request.Occurrence.AccountId.Value,

            // Copied off the folder row this occurrence was resolved through, which is the account's own binding. A
            // change belongs to the user whose mailbox it was performed in.
            MailFolderBindingId = folder.Id,
            MailFolderBinding = folder,
            UidValidity = request.Occurrence.UidValidity.Value,
            Uid = request.Occurrence.Uid.Value,
            Mutation = request.Mutation.Name,
            RequesterOrigin = request.Requester.Origin,
            RequesterIdentity = request.Requester.Identity,
            DestinationFolderPath = request.DestinationPath?.Value,
            DestinationHierarchyDelimiter = request.DestinationPath?.HierarchyDelimiter?.ToString(),
            DesiredSeenState = request.DesiredSeenState,
            DesiredFlaggedState = request.DesiredFlaggedState,

            // Materialized rather than shared, because the domain value publishes a read-only view over a list this row
            // would otherwise hold a second reference to.
            Keywords = request.Keywords is { } keywords ? [.. keywords.Values] : null,
            LocalDisposition = request.LocalDisposition,
            ServerDisposition = request.ServerDisposition,

            // Resolved here, with the row, so a trail switched on or off while this mutation is in flight decides
            // nothing about a change already begun.
            AuditTrailEnabled = auditSettingsReader.GetAuditSettings(request.Occurrence.AccountId).IsEnabled,
            Stage = MailboxMutationStage.Recorded,
            LocalChange = localChange,
            RequiresSourceRemoval = false,
            AttemptCount = 0,
            HeldUntil = heldUntil,
            RecordedAt = recordedAt,
            StageChangedAt = recordedAt,
        };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read through the scoped context, because it joins no transaction, and narrowed by the local email first: the
    /// foreign key onto the email is indexed, and the handful of rows one email ever carries is what the two remaining
    /// comparisons run over. The mutation is compared by its stored name and the origin by the text its conversion
    /// writes, which is what keeps the predicate translatable rather than evaluated in this process.
    /// </remarks>
    public Task<bool> HasRecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutation mutation,
        MailboxMutationOrigin origin,
        CancellationToken cancellationToken)
    {
        if (!mutation.IsSpecified)
        {
            throw new ArgumentException("A mutation record is read by a permitted mutation.", nameof(mutation));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                origin,
                "The mutation requester origin is not one this system declares.");
        }

        var emailId = storedEmailId.Value;
        var mutationName = mutation.Name;

        return readContext.MailboxMutations
            .AsNoTracking()
            .AnyAsync(
                record => record.StoredEmailId == emailId
                    && record.Mutation == mutationName
                    && record.RequesterOrigin == origin,
                cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The caller's accounts are part of the predicate rather than a check over what came back, so the database never
    /// returns a row from a mailbox the caller is not assigned and there is nothing in this process for a later
    /// mistake to leak. The folder binding is included because rebuilding a record needs the alias and generation its
    /// occurrence identity is made of.
    /// </remarks>
    public async Task<IReadOnlyList<MailboxMutationRecord>> ReadAsync(
        IReadOnlyList<MailAccountId> accounts,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(recordIds);

        if (accounts.Count == 0 || recordIds.Count == 0)
        {
            return [];
        }

        var accountValues = AccountValuesOf(accounts);
        var identifiers = recordIds.Select(recordId => recordId.Value).Distinct().ToArray();

        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolderBinding)
            .Where(mutation => accountValues.Contains(mutation.MailboxAccountId)
                && identifiers.Contains(mutation.Id))
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(entity => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolderBinding))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The stage is written directly rather than through <see cref="AdvanceAsync" />, because that transition refuses
    /// every terminal stage and orders the rest, and a withdrawal is neither: it applies to one stage only and is
    /// refused from every other by the record's own answer rather than by the ordering.
    /// </remarks>
    public async Task<IReadOnlyList<MailboxMutationRecord>> WithdrawAsync(
        IPersistenceSession session,
        IReadOnlyList<MailAccountId> accounts,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(recordIds);

        if (accounts.Count == 0 || recordIds.Count == 0)
        {
            return [];
        }

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var identifiers = recordIds.Select(recordId => recordId.Value).Distinct().ToArray();
        var accountValues = AccountValuesOf(accounts);

        // One query for the whole call rather than one per record: a caller withdrawing a full batch is the ordinary
        // case here, and the commit this joins retries as a whole, so a round trip per record would be paid again on
        // every attempt. Tracked, because the stage this may write has to be part of the caller's commit, and joined to
        // the folder because rebuilding the record needs the binding its occurrence identity is made of. The caller's
        // accounts are part of the predicate so a row outside them is absent rather than read and then rejected.
        var entities = await writeContext.MailboxMutations
            .Include(mutation => mutation.MailFolderBinding)
            .Where(mutation => accountValues.Contains(mutation.MailboxAccountId)
                && identifiers.Contains(mutation.Id))
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .ToArrayAsync(cancellationToken);

        var withdrawnAt = timeProvider.GetUtcNow();

        foreach (var entity in entities)
        {
            // The local change a restoring account already committed is not one withdrawal can take back, so its record
            // is left where it stands exactly as a record the server has already heard about is. Cancelling it would
            // undo nothing and would drop the only thing left to tell the source with. A change a mirrored account
            // committed first is the exception: the server still holds what it last showed, so withdrawing it returns
            // the stored message there. A copy written that way is not withdrawn, because its second message is one
            // the server is about to be told to make and erasing it is not this route's to do.
            var withdrawable = entity.Stage is MailboxMutationStage.Recorded
                && (entity.LocalChange is not MailboxMutationLocalChange.AlreadyCommitted
                    || (entity.OverlaysStoredState && entity.LocalCopyStoredEmailId is null));

            if (withdrawable)
            {
                entity.Stage = MailboxMutationStage.Cancelled;
                entity.StageChangedAt = withdrawnAt;

                await RestoreObservedStateAsync(writeContext, entity, cancellationToken);
            }
        }

        return [.. entities.Select(entity => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolderBinding))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxMutationRecord>> ReleaseAsync(
        IPersistenceSession session,
        IReadOnlyList<MailAccountId> accounts,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(recordIds);

        if (accounts.Count == 0 || recordIds.Count == 0)
        {
            return [];
        }

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var identifiers = recordIds.Select(recordId => recordId.Value).Distinct().ToArray();
        var accountValues = AccountValuesOf(accounts);

        // Read exactly as a withdrawal reads, and for the same reasons: one query for the whole call, tracked so the
        // write is part of the caller's commit, joined to the folder because rebuilding the record needs the binding,
        // and narrowed by the caller's accounts so a row outside them is absent rather than read and then rejected.
        var entities = await writeContext.MailboxMutations
            .Include(mutation => mutation.MailFolderBinding)
            .Where(mutation => accountValues.Contains(mutation.MailboxAccountId)
                && identifiers.Contains(mutation.Id))
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .ToArrayAsync(cancellationToken);

        foreach (var entity in entities)
        {
            // The stage is left where it is. Releasing says the wait is over rather than that the change is any
            // further along, so a record already withdrawn stays withdrawn and one under way is untouched — and
            // clearing a hold that is already clear writes nothing, which is what makes the call safe to repeat.
            entity.HeldUntil = null;
        }

        return [.. entities.Select(entity => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolderBinding))];
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset> RecordRefusalAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);
        var refusedAt = timeProvider.GetUtcNow();

        entity.AttemptCount++;
        entity.LastFailureCode = failure.Value;
        entity.FirstRefusedAt ??= refusedAt;

        // A placement whose command the server answered with a refusal placed nothing. The stage that says its answer
        // never came would otherwise hold the change as an unknown outcome nobody may ask again, so the answer returns it
        // to the stage it was issued from; this is the one movement back a stage makes, and it is made on an answer.
        if (entity.Stage is MailboxMutationStage.PlacementIssued)
        {
            entity.Stage = MailboxMutationStage.Recorded;
            entity.RequiresSourceRemoval = false;
            entity.StageChangedAt = refusedAt;
        }

        return entity.FirstRefusedAt.Value;
    }

    /// <inheritdoc />
    public async Task RecordPlacementIssuedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        bool requiresSourceRemoval,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        RequireForwardMovement(entity, MailboxMutationStage.PlacementIssued);

        entity.Stage = MailboxMutationStage.PlacementIssued;
        entity.RequiresSourceRemoval = requiresSourceRemoval;
        entity.StageChangedAt = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        RequireForwardMovement(entity, stage);

        entity.Stage = stage;
        entity.StageChangedAt = timeProvider.GetUtcNow();

        if (stage == MailboxMutationStage.Abandoned)
        {
            var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

            await RestoreObservedStateAsync(writeContext, entity, cancellationToken);
            await EraseLocalCopyAsync(session, writeContext, entity, cancellationToken);
        }

        if (placement is not null)
        {
            entity.PlacementUidValidity = placement.UidValidity?.Value;
            entity.PlacementUid = placement.Uid?.Value;
        }
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        entity.LastFailureCode = failure.Value;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken) =>
        this.ReadOutstandingOfAsync(account, localErasuresOnly: false, limit, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingLocalErasuresAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken) =>
        this.ReadOutstandingOfAsync(account, localErasuresOnly: true, limit, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The moves followed are the message's completed ones that named a placement nothing has carried the stored message
    /// onto yet; one whose placement was carried already shows in the stored message's own occurrence, which is where
    /// the walk starts. Each step names the destination by the newest binding of the path the move filed into, which is
    /// the binding the forward pass meets the placement under. A move that named no placement leaves the walk where it
    /// stood, because the server did not say where the message went and nothing here guesses.
    /// </remarks>
    public async Task<OutstandingMailboxMutation> ResolveIssuedOccurrenceAsync(
        IPersistenceSession session,
        OutstandingMailboxMutation outstanding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outstanding);

        var entity = await RequireEntityAsync(session, outstanding.Record.Id, cancellationToken);

        if (entity.Stage != MailboxMutationStage.Recorded
            || await ReadUnissuedOccurrenceAsync(session, entity, cancellationToken) is not { } resolved
            || (resolved.Binding.Id == entity.MailFolderBindingId && resolved.UidValidity == entity.UidValidity && resolved.Uid == entity.Uid))
        {
            return outstanding;
        }

        entity.MailFolderBindingId = resolved.Binding.Id;
        entity.MailFolderBinding = resolved.Binding;
        entity.UidValidity = resolved.UidValidity;
        entity.Uid = resolved.Uid;

        return new OutstandingMailboxMutation(
            MailboxMutationRecordMapping.ToRecord(entity, resolved.Binding),
            MailFolderBindingResolver.ToResolution(resolved.Binding));
    }

    private static async Task<ResolvedOccurrence?> ReadUnissuedOccurrenceAsync(
        IPersistenceSession session,
        MailboxMutationEntity entity,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var relocate = MailboxMutation.Relocate.Name;

        var email = await writeContext.StoredEmails
            .Include(static row => row.MailFolderBinding)
            .SingleOrDefaultAsync(row => row.Id == entity.StoredEmailId, cancellationToken);

        if (email is null)
        {
            return null;
        }

        var moves = await writeContext.MailboxMutations
            .AsNoTracking()
            .Where(move => move.StoredEmailId == entity.StoredEmailId
                && move.Id != entity.Id
                && move.Mutation == relocate
                && move.Stage == MailboxMutationStage.Completed
                && move.PlacementUidValidity != null
                && move.PlacementUid != null
                && move.PlacementObservedAt == null)
            .ToArrayAsync(cancellationToken);

        var current = email is { UidValidity: { } uidValidity, Uid: { } uid }
            ? new ResolvedOccurrence(email.MailFolderBinding, uidValidity, uid)
            : new ResolvedOccurrence(entity.MailFolderBinding, entity.UidValidity, entity.Uid);

        // Each move is followed once at most, so a cycle of moves the server reported back and forth ends with the walk.
        for (var step = 0; step < moves.Length; step++)
        {
            var move = moves.FirstOrDefault(candidate => candidate.MailFolderBindingId == current.Binding.Id
                && candidate.UidValidity == current.UidValidity
                && candidate.Uid == current.Uid);

            if (move?.DestinationFolderPath is not { } path
                || await ReadNewestBindingAsync(writeContext, move.MailboxAccountId, path, cancellationToken) is not { } destination)
            {
                break;
            }

            current = new ResolvedOccurrence(destination, move.PlacementUidValidity!.Value, move.PlacementUid!.Value);
        }

        return current;
    }

    private static Task<MailFolderBindingEntity?> ReadNewestBindingAsync(
        MailFathomDbContext writeContext,
        string account,
        string remotePath,
        CancellationToken cancellationToken) =>
        writeContext.MailFolderBindings
            .Where(binding => binding.MailboxAccountId == account && binding.RemotePath == remotePath)
            .OrderByDescending(binding => binding.ResolutionGeneration)
            .FirstOrDefaultAsync(cancellationToken);

    private sealed record ResolvedOccurrence(MailFolderBindingEntity Binding, uint UidValidity, uint Uid);

    private async Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingOfAsync(
        MailAccountId account,
        bool localErasuresOnly,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accountValue = account.Value;

        // Read once and compared in the query, so every row in one page is judged against one instant rather than
        // against whatever the clock said when the provider reached it.
        var now = timeProvider.GetUtcNow();
        var relocate = MailboxMutation.Relocate.Name;
        var delete = MailboxMutation.Delete.Name;
        string[] keywordMutations =
            [MailboxMutation.AddKeywords.Name, MailboxMutation.RemoveKeywords.Name, MailboxMutation.SetKeywords.Name];

        // The binding is joined rather than copied onto the row, because it is what turns a folder key back into the
        // alias and generation an occurrence identity is made of, and the remote path a resumed attempt selects.
        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolderBinding)
            .Where(mutation => mutation.MailboxAccountId == accountValue &&
                mutation.Stage != MailboxMutationStage.Completed &&
                mutation.Stage != MailboxMutationStage.Cancelled &&
                (!localErasuresOnly || mutation.LocalChange == MailboxMutationLocalChange.Erasure) &&

                // A record still inside its withdrawal window is not work this pass can do, so it is left out of the
                // page rather than read and skipped — a page spent on records nothing may touch is a page the account's
                // real backlog does not get.
                (mutation.HeldUntil == null || mutation.HeldUntil <= now) &&

                // The removal half of a message filed into another account waits for that account's append, so the
                // message is in two mailboxes for a while and never in none.
                !readContext.CrossAccountFilings.Any(filing =>
                    filing.RemovalMutationId == mutation.Id &&
                    filing.Stage != CrossAccountFilingStage.Appended) &&

                // Changes to one message reach the server in the order they were made: a move or a delete changes the
                // occurrence every later command names, and two changes to one field must not overtake each other, so
                // such a change waits while an earlier one is outstanding. Changes to other messages are untouched.
                !readContext.MailboxMutations.Any(earlier =>
                    earlier.StoredEmailId == mutation.StoredEmailId &&
                    earlier.RecordedAt < mutation.RecordedAt &&
                    earlier.Stage != MailboxMutationStage.Completed &&
                    earlier.Stage != MailboxMutationStage.Abandoned &&
                    earlier.Stage != MailboxMutationStage.Cancelled &&
                    (earlier.Mutation == relocate || earlier.Mutation == delete ||
                        mutation.Mutation == relocate || mutation.Mutation == delete ||
                        earlier.Mutation == mutation.Mutation ||
                        (keywordMutations.Contains(earlier.Mutation) && keywordMutations.Contains(mutation.Mutation)))))
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. entities.Select(entity => new OutstandingMailboxMutation(
                MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolderBinding),
                MailFolderBindingResolver.ToResolution(entity.MailFolderBinding))),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxMutationLifecycleCount>> ReadLifecycleCountsAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var accountValue = account.Value;

        // Grouped by the stored stage rather than by the lifecycle, because the lifecycle is derived by a domain method
        // the provider cannot translate. Collapsing the three converging stages happens once the rows are here, over an
        // answer that is at most one row per mutation per stage.
        var groupedStages = await readContext.MailboxMutations
            .AsNoTracking()
            .Where(mutation => mutation.MailboxAccountId == accountValue &&
                mutation.Stage != MailboxMutationStage.Completed &&
                mutation.Stage != MailboxMutationStage.Cancelled)
            .GroupBy(mutation => new { mutation.Mutation, mutation.Stage })
            .Select(group => new
            {
                group.Key.Mutation,
                group.Key.Stage,
                Count = group.Count(),
                OldestRecordedAt = group.Min(mutation => mutation.RecordedAt),
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. groupedStages
                .Select(group => new
                {
                    Mutation = ParseMutationOrThrow(group.Mutation),
                    Lifecycle = MailboxMutationLifecycle.Of(group.Stage),
                    group.Count,
                    group.OldestRecordedAt,
                })
                .GroupBy(group => new { group.Mutation, group.Lifecycle })
                .Select(group => new MailboxMutationLifecycleCount(
                    group.Key.Mutation,
                    group.Key.Lifecycle,
                    group.Sum(stage => stage.Count),
                    group.Min(stage => stage.OldestRecordedAt))),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OutstandingMailboxMutation>> ReadRefusedAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accountValue = account.Value;

        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolderBinding)
            .Where(mutation => mutation.MailboxAccountId == accountValue &&
                mutation.FirstRefusedAt != null &&
                mutation.Stage != MailboxMutationStage.PlacementIssued &&
                mutation.Stage != MailboxMutationStage.Completed &&
                mutation.Stage != MailboxMutationStage.Abandoned &&
                mutation.Stage != MailboxMutationStage.Cancelled)
            .OrderBy(mutation => mutation.FirstRefusedAt)
            .ThenBy(mutation => mutation.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. entities.Select(entity => new OutstandingMailboxMutation(
                MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolderBinding),
                MailFolderBindingResolver.ToResolution(entity.MailFolderBinding))),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<MailAccountId, MailboxWaitingChanges>> ReadWaitingChangesAsync(
        IReadOnlyCollection<MailAccountId> accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        if (accounts.Count == 0)
        {
            return new Dictionary<MailAccountId, MailboxWaitingChanges>();
        }

        var accountValues = AccountValuesOf([.. accounts]);

        var waiting = await readContext.MailboxMutations
            .AsNoTracking()
            .Where(mutation => accountValues.Contains(mutation.MailboxAccountId) &&
                mutation.Stage != MailboxMutationStage.Completed &&
                mutation.Stage != MailboxMutationStage.Abandoned &&
                mutation.Stage != MailboxMutationStage.Cancelled &&
                mutation.LocalChange != MailboxMutationLocalChange.Erasure)
            .GroupBy(mutation => mutation.MailboxAccountId)
            .Select(group => new
            {
                Account = group.Key,
                Count = group.Count(),
                OldestCommittedAt = group.Min(mutation => mutation.RecordedAt),
            })
            .ToArrayAsync(cancellationToken);

        return waiting.ToDictionary(
            group => MailAccountId.Create(group.Account),
            group => new MailboxWaitingChanges(group.Count, group.OldestCommittedAt));
    }

    /// <summary>Reads a stored mutation name back, refusing one this build does not permit.</summary>
    /// <remarks>
    /// A name that no longer parses is the same defect here as it is when a whole record is rebuilt, and it is refused
    /// the same way: a count broken down by a mutation nothing performs would report work that cannot exist.
    /// </remarks>
    private static MailboxMutation ParseMutationOrThrow(string mutationName) =>
        MailboxMutation.TryParseName(mutationName, out var mutation)
            ? mutation
            : throw new InvalidOperationException(
                $"Mailbox mutation records name '{mutationName}', which is not a permitted mutation.");

    /// <summary>Refuses a stage that would pull the record backwards or move it out of a terminal one.</summary>
    /// <remarks>
    /// The record is what a resumed attempt reads, so a late write from an attempt that lost its race must not undo
    /// progress a later one made. A terminal stage is refused separately from the ordering, because the two terminal
    /// members are ordered against each other and neither may follow the other.
    /// </remarks>
    private static void RequireForwardMovement(MailboxMutationEntity entity, MailboxMutationStage stage)
    {
        var isTerminal = entity.Stage
            is MailboxMutationStage.Completed
            or MailboxMutationStage.Abandoned
            or MailboxMutationStage.Cancelled;

        if (isTerminal || stage <= entity.Stage)
        {
            throw new InvalidOperationException(
                $"Mailbox mutation record {entity.Id} is at stage {entity.Stage} and cannot be moved to {stage}.");
        }
    }

    private static async Task<StoredEmailEntity> RequireStoredEmailAsync(
        MailFathomDbContext writeContext,
        MailboxMutationRequest request,
        CancellationToken cancellationToken) =>
        await writeContext.StoredEmails.FindAsync([request.StoredEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No stored email carries the identifier {request.StoredEmailId}, so no mutation can be recorded against it.");

    /// <summary>Reads every overlaying change still outstanding on one message, persisted or staged in this transaction, oldest first.</summary>
    private static async Task<IReadOnlyList<MailboxMutationEntity>> ReadOutstandingOverlaysAsync(
        MailFathomDbContext writeContext,
        Guid storedEmailId,
        CancellationToken cancellationToken)
    {
        var persisted = await writeContext.MailboxMutations
            .Include(mutation => mutation.MailFolderBinding)
            .Where(mutation => mutation.StoredEmailId == storedEmailId
                && mutation.OverlaysStoredState
                && mutation.Stage != MailboxMutationStage.Completed
                && mutation.Stage != MailboxMutationStage.Abandoned
                && mutation.Stage != MailboxMutationStage.Cancelled)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. writeContext.MailboxMutations.Local
                .Where(mutation => mutation.StoredEmailId == storedEmailId
                    && mutation.OverlaysStoredState
                    && !IsTerminal(mutation.Stage))
                .Union(persisted)
                .OrderBy(mutation => mutation.RecordedAt)
                .ThenBy(mutation => mutation.Id),
        ];
    }

    private static bool IsTerminal(MailboxMutationStage stage) =>
        stage is MailboxMutationStage.Completed or MailboxMutationStage.Abandoned or MailboxMutationStage.Cancelled;

    /// <summary>Takes each field's last observation from the oldest change still outstanding on it, and from the stored value where none is.</summary>
    private static MailboxMutationOverlay ObservationCarriedOver(
        MailboxMutation mutation,
        IReadOnlyList<MailboxMutationEntity> earlier,
        MailboxMutationOverlay replaced)
    {
        var seen = earlier.FirstOrDefault(entity => entity.Mutation == MailboxMutation.SetSeen.Name);
        var flagged = earlier.FirstOrDefault(entity => entity.Mutation == MailboxMutation.SetFlagged.Name);
        var keywords = earlier.FirstOrDefault(entity => WritesKeywords(entity.Mutation));
        var folder = earlier.FirstOrDefault(entity => entity.Mutation == MailboxMutation.Relocate.Name);

        return new MailboxMutationOverlay(
            mutation == MailboxMutation.SetSeen ? seen?.ObservedSeenState ?? replaced.ObservedSeenState : null,
            mutation == MailboxMutation.SetFlagged ? flagged?.ObservedFlaggedState ?? replaced.ObservedFlaggedState : null,
            MailboxMutationRequest.IsKeywordChange(mutation)
                ? keywords?.ObservedKeywords is { } carried ? EmailKeywords.Create(carried) : replaced.ObservedKeywords
                : null,
            mutation == MailboxMutation.Relocate
                ? folder?.ObservedFolderId is { } carriedFolder ? MailFolderId.Create(carriedFolder) : replaced.ObservedFolder
                : null,
            mutation == MailboxMutation.Copy ? replaced.LocalCopy : null);
    }

    private static bool WritesKeywords(string mutation) =>
        mutation == MailboxMutation.AddKeywords.Name
        || mutation == MailboxMutation.RemoveKeywords.Name
        || mutation == MailboxMutation.SetKeywords.Name;

    private static void WriteObservations(MailboxMutationEntity entity, MailboxMutationOverlay overlay)
    {
        entity.OverlaysStoredState = true;
        entity.ObservedSeenState = overlay.ObservedSeenState;
        entity.ObservedFlaggedState = overlay.ObservedFlaggedState;
        entity.ObservedKeywords = overlay.ObservedKeywords is { } keywords ? [.. keywords.Values] : null;
        entity.ObservedFolderId = overlay.ObservedFolder?.Value;
        entity.LocalCopyStoredEmailId = overlay.LocalCopy?.Value;
    }

    /// <summary>Withdraws every earlier change, still unissued, that the new request supersedes, handing its fields to the new one.</summary>
    /// <remarks>
    /// Nothing is restored: the new change already carries the field's last observation forward and is now its writer.
    /// </remarks>
    private void WithdrawSuperseded(MailboxMutationRequest request, IReadOnlyList<MailboxMutationEntity> earlier)
    {
        var withdrawnAt = timeProvider.GetUtcNow();

        foreach (var entity in earlier.Where(entity => entity.Stage == MailboxMutationStage.Recorded
            && request.Supersedes(MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolderBinding).Request)))
        {
            entity.Stage = MailboxMutationStage.Cancelled;
            entity.StageChangedAt = withdrawnAt;
        }
    }

    /// <summary>Returns each field an overlaying change wrote to the value last observed on the server, where no other change still holds it.</summary>
    /// <remarks>
    /// Called in the transaction that withdraws or gives up the change, so the stored message never shows a change the
    /// server will not be told about. A delete restores nothing here: its message is hidden only while the delete is
    /// outstanding, so it shows again once the record ends.
    /// </remarks>
    private static async Task RestoreObservedStateAsync(
        MailFathomDbContext writeContext,
        MailboxMutationEntity ended,
        CancellationToken cancellationToken)
    {
        if (!ended.OverlaysStoredState
            || await writeContext.StoredEmails.FindAsync([ended.StoredEmailId], cancellationToken) is not { } email)
        {
            return;
        }

        var others = (await ReadOutstandingOverlaysAsync(writeContext, ended.StoredEmailId, cancellationToken))
            .Where(entity => entity.Id != ended.Id)
            .ToArray();

        if (ended.Mutation == MailboxMutation.SetSeen.Name
            && ended.ObservedSeenState is { } seen
            && !others.Any(entity => entity.Mutation == ended.Mutation))
        {
            email.IsMarkedSeen = seen;
        }
        else if (ended.Mutation == MailboxMutation.SetFlagged.Name
            && ended.ObservedFlaggedState is { } flagged
            && !others.Any(entity => entity.Mutation == ended.Mutation))
        {
            email.IsFlagged = flagged;
        }
        else if (ended.Mutation == MailboxMutation.Relocate.Name
            && ended.ObservedFolderId is { } folder
            && !others.Any(entity => entity.Mutation == ended.Mutation))
        {
            email.FolderId = folder;
        }
        else if (WritesKeywords(ended.Mutation) && ended.ObservedKeywords is { } observed)
        {
            email.Keywords = KeywordsRestored(ended, email.Keywords, observed, others);
        }
        else if (ended.Mutation == MailboxMutation.Delete.Name
            && !others.Any(entity => entity.Mutation == ended.Mutation
                && entity.LocalDisposition is not AuthoredDeleteEmailDisposition.RetainLocalCopy))
        {
            email.LocalDeleteCommittedAt = null;
        }
    }

    /// <summary>Erases the second stored message a copy committed locally first wrote, once the copy is given up.</summary>
    /// <remarks>The copy is a message the server was about to be told to make, so a copy it never made leaves a row showing mail the mailbox does not hold.</remarks>
    private static async Task EraseLocalCopyAsync(
        IPersistenceSession session,
        MailFathomDbContext writeContext,
        MailboxMutationEntity ended,
        CancellationToken cancellationToken)
    {
        if (ended.Mutation == MailboxMutation.Copy.Name && ended.LocalCopyStoredEmailId is { } copyId)
        {
            await LocalCopyErasure.EraseAsync(session, writeContext, copyId, cancellationToken);
        }
    }

    /// <summary>Returns each keyword the ended change touched, and no other outstanding change still holds, to its observed membership.</summary>
    private static string[] KeywordsRestored(
        MailboxMutationEntity ended,
        string[] stored,
        string[] observed,
        IReadOnlyList<MailboxMutationEntity> others)
    {
        var otherKeywordChanges = others.Where(entity => WritesKeywords(entity.Mutation)).ToArray();

        if (otherKeywordChanges.Any(entity => entity.Mutation == MailboxMutation.SetKeywords.Name))
        {
            return stored;
        }

        var heldByOthers = otherKeywordChanges
            .SelectMany(entity => entity.Keywords ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Restored(string keyword) =>
            !heldByOthers.Contains(keyword)
            && (ended.Mutation == MailboxMutation.SetKeywords.Name || (ended.Keywords ?? []).Contains(keyword, StringComparer.OrdinalIgnoreCase));

        return
        [
            .. EmailKeywords.Create(
                stored.Where(keyword => !Restored(keyword)).Concat(observed.Where(Restored))).Values,
        ];
    }

    private static async Task<MailboxMutationEntity?> FindByIdentityAsync(
        MailFathomDbContext writeContext,
        MailboxMutationRequest request,
        long folderId,
        CancellationToken cancellationToken)
    {
        var uidValidity = request.Occurrence.UidValidity.Value;
        var uid = request.Occurrence.Uid.Value;
        var origin = request.Requester.Origin;
        var identity = request.Requester.Identity;
        var mutationName = request.Mutation.Name;

        // Looked up by the idempotency identity rather than by the key, so the change-tracker pass is explicit: a
        // request opened earlier in this same uncommitted session would be invisible to a query.
        return await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.MailboxMutations,
            writeContext.MailboxMutations,
            mutation => mutation.MailFolderBindingId == folderId &&
                mutation.UidValidity == uidValidity &&
                mutation.Uid == uid &&
                mutation.RequesterOrigin == origin &&
                mutation.RequesterIdentity == identity &&
                mutation.Mutation == mutationName,
            cancellationToken);
    }

    private static string[] AccountValuesOf(IReadOnlyList<MailAccountId> accounts) =>
        [.. accounts.Select(account => account.Value).Distinct(StringComparer.Ordinal)];

    private static async Task<MailboxMutationEntity> RequireEntityAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // A primary-key lookup, so FindAsync already resolves an insert this session may still be holding.
        return await writeContext.MailboxMutations.FindAsync([recordId.Value], cancellationToken)
            ?? throw new InvalidOperationException($"No mailbox mutation record carries the identifier {recordId}.");
    }
}
