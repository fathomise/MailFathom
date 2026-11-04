// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Filing;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps filings into another account in memory, and records each effect the real store has on the rows beside them.</summary>
/// <remarks>
/// The real store hides a held source, binds or erases the destination's row, and withdraws a removal record, all on
/// rows this double does not hold. It names each of those effects instead, so a test asserts the effect the port
/// promises rather than a row a fake made up.
/// </remarks>
internal sealed class InMemoryCrossAccountFilingStore : ICrossAccountFilingStore
{
    private readonly Dictionary<CrossAccountFilingId, CrossAccountFilingRecord> filingsById = [];
    private readonly Dictionary<StoredEmailId, OutstandingCopy> copies = [];
    private readonly HashSet<StoredEmailId> filedFromOutgoing = [];
    private readonly List<StoredEmailId> hiddenSources = [];
    private readonly List<StoredEmailId> erasedCopies = [];
    private readonly List<StoredEmailId> shownSources = [];
    private readonly List<MailboxMutationRecordId> withdrawnRemovals = [];
    private readonly Dictionary<StoredEmailId, (ImapUidValidity UidValidity, ImapUid Uid)> boundCopies = [];
    private DateTimeOffset now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Gets every filing written, in no particular order.</summary>
    internal IReadOnlyCollection<CrossAccountFilingRecord> Filings => this.filingsById.Values;

    /// <summary>Gets the held sources a filing hid when it was written.</summary>
    internal IReadOnlyList<StoredEmailId> HiddenSources => this.hiddenSources;

    /// <summary>Gets the destination rows erased, by a revert or as a duplicate.</summary>
    internal IReadOnlyList<StoredEmailId> ErasedCopies => this.erasedCopies;

    /// <summary>Gets the sources a revert showed again.</summary>
    internal IReadOnlyList<StoredEmailId> ShownSources => this.shownSources;

    /// <summary>Gets the removal records a revert withdrew before they were issued.</summary>
    internal IReadOnlyList<MailboxMutationRecordId> WithdrawnRemovals => this.withdrawnRemovals;

    /// <summary>Gets where each destination row was bound.</summary>
    internal IReadOnlyDictionary<StoredEmailId, (ImapUidValidity UidValidity, ImapUid Uid)> BoundCopies => this.boundCopies;

    /// <summary>Answers whether a removal record is still held back by a filing that has not landed, as the real outstanding read filters it.</summary>
    internal bool HoldsBack(MailboxMutationRecordId removal) =>
        this.filingsById.Values.Any(filing => filing.RemovalRecordId == removal
            && filing.Stage != CrossAccountFilingStage.Appended);

    /// <summary>Gives a destination row what the append reads from it.</summary>
    internal void Store(StoredEmailId copy, MailFolderResolution folder, RestoredEmailState state, DateTimeOffset internalDate) =>
        this.copies[copy] = new OutstandingCopy(folder, state, internalDate);

    /// <summary>Marks a message as a sent copy one of its account's outgoing records filed.</summary>
    internal void FileFromOutgoing(StoredEmailId email) => this.filedFromOutgoing.Add(email);

    /// <summary>Reads back one filing.</summary>
    internal CrossAccountFilingRecord Single() => this.filingsById.Values.Single();

    /// <inheritdoc />
    public Task<CrossAccountFilingId> RecordAsync(
        IPersistenceSession session,
        NewCrossAccountFiling filing,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filing);

        var recordedAt = this.Advance();
        var id = CrossAccountFilingId.Create(Guid.NewGuid());

        this.filingsById[id] = new CrossAccountFilingRecord
        {
            Id = id,
            DestinationAccountId = filing.DestinationAccountId,
            DestinationEmailId = filing.DestinationEmailId,
            DestinationFolderPath = filing.DestinationFolderPath,
            SourceAccountId = filing.SourceAccountId,
            SourceEmailId = filing.SourceEmailId,
            RemovalRecordId = filing.RemovalRecordId,
            ErasesHeldSource = filing.ErasesHeldSource,
            Stage = CrossAccountFilingStage.Recorded,
            RecordedAt = recordedAt,
            StageChangedAt = recordedAt,
        };

        if (filing.ErasesHeldSource)
        {
            this.hiddenSources.Add(filing.SourceEmailId);
        }

        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutstandingCrossAccountFiling>> ReadOutstandingAsync(
        MailAccountId destination,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OutstandingCrossAccountFiling> outstanding =
        [
            .. this.filingsById.Values
                .Where(filing => filing.DestinationAccountId == destination && !filing.IsTerminal)
                .OrderBy(filing => filing.RecordedAt)
                .ThenBy(filing => filing.Id.Value)
                .Take(maximumCount)
                .Select(filing => this.copies.TryGetValue(filing.DestinationEmailId, out var copy)
                    ? new OutstandingCrossAccountFiling(filing, copy.Folder, copy.State, copy.InternalDate)
                    : new OutstandingCrossAccountFiling(filing, Folder: null, State: null, filing.RecordedAt)),
        ];

        return Task.FromResult(outstanding);
    }

    /// <inheritdoc />
    public Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken)
    {
        this.MoveTo(filing, CrossAccountFilingStage.AppendIssued);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        RemoteEmailPlacement placement,
        CancellationToken cancellationToken)
    {
        var completed = this.MoveTo(filing, CrossAccountFilingStage.Appended);

        if (placement is { UidValidity: { } uidValidity, Uid: { } uid })
        {
            this.boundCopies[completed.DestinationEmailId] = (uidValidity, uid);
        }
        else
        {
            this.erasedCopies.Add(completed.DestinationEmailId);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AbandonAsync(
        IPersistenceSession session,
        CrossAccountFilingId filing,
        CancellationToken cancellationToken)
    {
        var abandoned = this.MoveTo(filing, CrossAccountFilingStage.Abandoned);

        this.erasedCopies.Add(abandoned.DestinationEmailId);
        this.shownSources.Add(abandoned.SourceEmailId);

        if (abandoned.RemovalRecordId is { } removal)
        {
            this.withdrawnRemovals.Add(removal);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CrossAccountFilingRecord?> FindByRemovalAsync(
        MailboxMutationRecordId removal,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.filingsById.Values.FirstOrDefault(filing => filing.RemovalRecordId == removal));

    /// <inheritdoc />
    public Task<bool> IsFiledFromOutgoingRecordAsync(StoredEmailId email, CancellationToken cancellationToken) =>
        Task.FromResult(this.filedFromOutgoing.Contains(email));

    private CrossAccountFilingRecord MoveTo(CrossAccountFilingId id, CrossAccountFilingStage stage)
    {
        var filing = this.filingsById[id];

        if (filing.IsTerminal)
        {
            throw new InvalidOperationException($"The filing {id} has already ended as {filing.Stage}.");
        }

        var moved = filing with { Stage = stage, StageChangedAt = this.Advance() };
        this.filingsById[id] = moved;

        return moved;
    }

    private DateTimeOffset Advance()
    {
        this.now = this.now.AddSeconds(1);

        return this.now;
    }

    private sealed record OutstandingCopy(MailFolderResolution Folder, RestoredEmailState State, DateTimeOffset InternalDate);
}
