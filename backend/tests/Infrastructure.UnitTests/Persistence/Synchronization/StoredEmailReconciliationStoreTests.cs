// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
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

    /// <summary>A junk mark another client moved is what asks for the verdict to be decided again, dated at the observation.</summary>
    [Fact]
    public void ApplyFlagSnapshot_AnotherClientMovedAJunkMark_RecordsTheChange()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(
            row,
            SourceReporting("$NotJunk"),
            sourceIsTheTruth: true,
            keywordsChangedElsewhere: true);

        // Assert
        Assert.Equal(LaterObservation, row.JunkMarksChangedAt);
    }

    /// <summary>The last mark being taken off is a change like any other, so it is compared in both directions.</summary>
    [Fact]
    public void ApplyFlagSnapshot_AnotherClientTookTheLastJunkMarkOff_RecordsTheChange()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);
        row.Keywords = ["$JUNK"];

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(
            row,
            SourceReporting(),
            sourceIsTheTruth: true,
            keywordsChangedElsewhere: true);

        // Assert
        Assert.Equal(LaterObservation, row.JunkMarksChangedAt);
    }

    /// <summary>Another client labelling mail says nothing about whether it is junk.</summary>
    [Fact]
    public void ApplyFlagSnapshot_AnotherClientMovedAnOrdinaryLabel_RecordsNoChange()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);
        row.Keywords = ["$JUNK"];

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(
            row,
            SourceReporting("$Junk", "$Invoice"),
            sourceIsTheTruth: true,
            keywordsChangedElsewhere: true);

        // Assert
        Assert.Null(row.JunkMarksChangedAt);
    }

    /// <summary>A change a mutation record accounts for is MailFathom's own, and never another client's.</summary>
    [Fact]
    public void ApplyFlagSnapshot_AChangeARecordAccountsFor_RecordsNoChange()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(
            row,
            SourceReporting("$Junk"),
            sourceIsTheTruth: true,
            keywordsChangedElsewhere: false);

        // Assert
        Assert.Null(row.JunkMarksChangedAt);
    }

    /// <summary>While MailFathom's own change is on its way the field is the act's, so a difference from the source is no statement.</summary>
    [Fact]
    public void ApplyFlagSnapshot_TheMarkIsHeldByAnOutstandingChange_RecordsNoChange()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);
        row.Keywords = ["$NOTJUNK"];
        row.Mutations.Add(new MailboxMutationEntity
        {
            StoredEmail = row,
            MailboxAccountId = row.MailboxAccountId,
            MailFolderBinding = row.MailFolderBinding,
            Mutation = MailboxMutation.AddKeywords.Name,
            RequesterIdentity = "person",
            Keywords = ["$NotJunk"],
            OverlaysStoredState = true,
        });

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(
            row,
            SourceReporting(),
            sourceIsTheTruth: true,
            keywordsChangedElsewhere: true);

        // Assert
        Assert.Equal(["$NOTJUNK"], row.Keywords);
        Assert.Null(row.JunkMarksChangedAt);
    }

    /// <summary>A classification can run before anybody has read the marks, so the first reading asks again, dated before any person's verdict.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyFlagSnapshot_TheFirstReadingBringsAJunkMark_RecordsItAsOfTheStoring(bool sourceIsTheTruth)
    {
        // Arrange
        var row = CreateRow(observedAt: null);
        row.StoredAt = EarlierObservation;

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(row, SourceReporting("$NotJunk"), sourceIsTheTruth);

        // Assert
        Assert.Equal(EarlierObservation, row.JunkMarksChangedAt);
    }

    /// <summary>Most mail arrives with no mark at all, and must cost nothing.</summary>
    [Fact]
    public void ApplyFlagSnapshot_TheFirstReadingBringsNoJunkMark_RecordsNoChange()
    {
        // Arrange
        var row = CreateRow(observedAt: null);

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(row, SourceReporting("$Invoice"), sourceIsTheTruth: true);

        // Assert
        Assert.Null(row.JunkMarksChangedAt);
    }

    /// <summary>Where MailFathom is the truth a later change on the source reaches no verdict, even were it reported as another client's.</summary>
    [Fact]
    public void ApplyFlagSnapshot_MailFathomIsTheTruthAndALaterMarkMoved_RecordsNoChange()
    {
        // Arrange
        var row = CreateRow(observedAt: EarlierObservation);

        // Act
        StoredEmailReconciliationStore.ApplyFlagSnapshot(
            row,
            SourceReporting("$NotJunk"),
            sourceIsTheTruth: false,
            keywordsChangedElsewhere: true);

        // Assert
        Assert.Null(row.JunkMarksChangedAt);
    }

    private static RemoteEmailFlagSnapshot SourceReporting(params string[] keywords) => new(
        LaterObservation,
        IsSeen: false,
        IsAnswered: false,
        IsFlagged: false,
        IsDraft: false,
        IsDeleted: false,
        EmailKeywords.Create(keywords));

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
