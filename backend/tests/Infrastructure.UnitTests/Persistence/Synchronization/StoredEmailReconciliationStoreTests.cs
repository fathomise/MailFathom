// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Synchronization;

/// <summary>Covers what one observation writes onto a stored message, which depends on who the truth about the mailbox is.</summary>
public sealed class StoredEmailReconciliationStoreTests
{
    private static readonly DateTimeOffset EarlierObservation = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LaterObservation = EarlierObservation.AddHours(1);

    /// <summary>The source holds the opposite of everything the row says, so a field the observation reached cannot go unnoticed.</summary>
    [Fact]
    public void ApplyFlagSnapshot_MailFathomIsTheTruthAndTheRowWasObservedBefore_WritesNothingButTheObservation()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);
        row.IsMarkedSeen = true;
        row.Keywords = ["held"];

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(row, SourceReportingEverything(), sourceIsTheTruth: false);

        // Assert
        Assert.Equal(LaterObservation, row.FlagsObservedAt);
        Assert.True(row.IsMarkedSeen);
        Assert.False(row.IsFlagged);
        Assert.False(row.IsRemotelyAnswered);
        Assert.False(row.IsDraft);
        Assert.False(row.IsRemotelyDeleted);
        Assert.Equal(["held"], row.Keywords);
    }

    /// <summary>A new arrival's flags have no other writer, so skipping this write would leave every new message unread for good.</summary>
    /// <remarks>
    /// The <c>\Deleted</c> flag is the exception: on such an account the drain sets it itself between its store and its
    /// expunge, so reading it back would publish a held message as deleted.
    /// </remarks>
    [Fact]
    public void ApplyFlagSnapshot_MailFathomIsTheTruthAndTheRowWasNeverObserved_WritesTheInitialStateButNotTheDeletion()
    {
        // Arrange
        var row = CreateRow(observedAt: null);

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(row, SourceReportingEverything(), sourceIsTheTruth: false);

        // Assert
        Assert.Equal(LaterObservation, row.FlagsObservedAt);
        Assert.True(row.IsMarkedSeen);
        Assert.True(row.IsFlagged);
        Assert.True(row.IsRemotelyAnswered);
        Assert.True(row.IsDraft);
        Assert.Equal(["$SOURCE"], row.Keywords);
        Assert.False(row.IsRemotelyDeleted);
    }

    /// <summary>A message read and then marked unread looks untouched, so the field an act wrote is what the first observation has to ask about.</summary>
    [Fact]
    public void ApplyFlagSnapshot_FirstObservationAfterAnAct_KeepsWhatTheActWroteAndTakesTheRest()
    {
        // Arrange
        var row = CreateRow(observedAt: null);
        row.FieldsSetBeforeFirstObservation =
            EmailFieldsSetBeforeFirstObservation.Seen | EmailFieldsSetBeforeFirstObservation.Keywords;
        row.Keywords = ["person"];

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(row, SourceReportingEverything(), sourceIsTheTruth: false);

        // Assert
        Assert.False(row.IsMarkedSeen);
        Assert.Equal(["person"], row.Keywords);
        Assert.True(row.IsFlagged);
    }

    /// <summary>The control: the same observation on a mirrored account writes every field, so the two above cannot pass by writing nothing.</summary>
    [Fact]
    public void ApplyFlagSnapshot_SourceIsTheTruth_WritesEveryField()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(row, SourceReportingEverything(), sourceIsTheTruth: true);

        // Assert
        Assert.Equal(LaterObservation, row.FlagsObservedAt);
        Assert.True(row.IsMarkedSeen);
        Assert.True(row.IsFlagged);
        Assert.True(row.IsRemotelyAnswered);
        Assert.True(row.IsDraft);
        Assert.True(row.IsRemotelyDeleted);
        Assert.Equal(["$SOURCE"], row.Keywords);
    }

    private static RemoteEmailFlagSnapshot SourceReportingEverything() => new(
        LaterObservation,
        IsSeen: true,
        IsAnswered: true,
        IsFlagged: true,
        IsDraft: true,
        IsDeleted: true,
        EmailKeywords.Create(["$SOURCE"]));

    private static StoredEmailEntity CreateRow(DateTimeOffset? observedAt)
    {
        var account = new MailboxAccountEntity { Id = "held" };

        return new StoredEmailEntity
        {
            Id = Guid.CreateVersion7(),
            MailboxAccountId = account.Id,
            MailFolderBinding = new MailFolderBindingEntity
            {
                MailboxAccountId = account.Id,
                MailboxAccount = account,
                Alias = "inbox",
                RemotePath = "INBOX",
            },
            UidValidity = 7,
            Uid = 42,
            SizeOctets = 128,
            ContentAvailability = StoredEmailContentAvailability.Available,
            FlagsObservedAt = observedAt,
        };
    }
}
