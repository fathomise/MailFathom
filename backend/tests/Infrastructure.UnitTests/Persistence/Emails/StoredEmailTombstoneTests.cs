// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers which rows a tombstone hides, which every mailbox read composes rather than restates.</summary>
/// <remarks>
/// The rule is no null check, because a message its server lost can be kept readable under
/// <c>RemotelyDeletedEmailDisposition.RetainLocalCopy</c>, so every column it reads is asserted here rather than in each query that uses them. Evaluating the expression needs no
/// database: it is the predicate itself that is under test, and the queries that compose it are proved against
/// PostgreSQL in the integration suite.
/// </remarks>
public sealed class StoredEmailTombstoneTests
{
    private static readonly DateTimeOffset ExpungeObservedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>An email the server still holds is in every mailbox, which is the case the rule must not narrow.</summary>
    [Fact]
    public void IsNotTombstoned_EmailTheServerStillHolds_IsAdmitted()
    {
        // Arrange
        var email = CreateEmail();

        // Act
        var admitted = StoredEmailTombstone.IsNotTombstoned.Compile()(email);

        // Assert
        Assert.True(admitted);
    }

    /// <summary>A tombstone is what takes an email out of every query, and an expunge alone is one.</summary>
    [Fact]
    public void IsNotTombstoned_EmailWhoseOccurrenceHasGone_IsExcluded()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;

        // Act
        var admitted = StoredEmailTombstone.IsNotTombstoned.Compile()(email);

        // Assert
        Assert.False(admitted);
    }

    /// <summary>A message its server lost stays readable under <c>RetainLocalCopy</c>, which is the whole of that value.</summary>
    /// <remarks>
    /// The row carries the expunge as well, because the server genuinely no longer holds the message and the
    /// reconciliation queue is ordered by that column. Reading only that column here would hide exactly the mail this
    /// value exists to keep.
    /// </remarks>
    [Fact]
    public void IsNotTombstoned_EmailKeptAfterItsServerLostIt_IsAdmittedDespiteTheExpunge()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;
        email.IsRetainedAfterRemoteDelete = true;

        // Act
        var admitted = StoredEmailTombstone.IsNotTombstoned.Compile()(email);

        // Assert
        Assert.True(admitted);
    }

    /// <summary>A delete MailFathom performed never leaves its message readable, whatever an earlier release recorded.</summary>
    [Fact]
    public void IsNotTombstoned_EmailKeptAfterAnAuthoredDeleteByAnEarlierRelease_IsExcluded()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;
        email.IsRetainedAfterAuthoredDelete = true;

        // Act
        var admitted = StoredEmailTombstone.IsNotTombstoned.Compile()(email);

        // Assert
        Assert.False(admitted);
    }

    /// <summary>A local-only message filed into another account goes at once, as any other message a delete hides.</summary>
    [Fact]
    public void IsNotTombstoned_EmailKeptAfterItsServerLostItAndThenDeletedLocally_IsExcluded()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;
        email.IsRetainedAfterRemoteDelete = true;
        email.LocalDeleteCommittedAt = ExpungeObservedAt;

        // Act
        var admitted = StoredEmailTombstone.IsNotTombstoned.Compile()(email);

        // Assert
        Assert.False(admitted);
    }

    /// <summary>A row is measured from the moment the server was seen to have removed it, whatever came before.</summary>
    [Fact]
    public void SettledBefore_AnExpungeOlderThanTheMoment_IsAdmitted()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;

        // Act
        var admitted = StoredEmailTombstone.SettledBefore(ExpungeObservedAt.AddTicks(1)).Compile()(email);

        // Assert
        Assert.True(admitted);
    }

    /// <summary>"Longer than" is strict, so a row exactly at the age has not yet outlived it.</summary>
    [Fact]
    public void SettledBefore_AnExpungeExactlyAtTheMoment_IsLeftOut()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;

        // Act
        var admitted = StoredEmailTombstone.SettledBefore(ExpungeObservedAt).Compile()(email);

        // Assert
        Assert.False(admitted);
    }

    /// <summary>A flagged row became a tombstone when it was flagged, and a later expunge does not restart its clock.</summary>
    [Fact]
    public void SettledBefore_AFlagFollowedByAnExpunge_IsMeasuredFromTheFlag()
    {
        // Arrange
        var email = CreateEmail();
        email.AuthoredDeleteFlaggedAt = ExpungeObservedAt.AddDays(-10);
        email.RemoteExpungeObservedAt = ExpungeObservedAt;

        // Act
        var admitted = StoredEmailTombstone.SettledBefore(ExpungeObservedAt.AddDays(-1)).Compile()(email);

        // Assert
        Assert.True(admitted);
    }

    /// <summary>A delete committed locally and not yet observed to settle can still be given up, so it is not a tombstone yet.</summary>
    [Fact]
    public void SettledBefore_ADeleteOnlyCommittedLocally_IsLeftOutHoweverOld()
    {
        // Arrange
        var email = CreateEmail();
        email.LocalDeleteCommittedAt = ExpungeObservedAt.AddYears(-5);

        // Act
        var admitted = StoredEmailTombstone.SettledBefore(ExpungeObservedAt).Compile()(email);

        // Assert
        Assert.False(admitted);
    }

    /// <summary>Mail kept readable is a message rather than a tombstone, and this pass must never reach it.</summary>
    [Fact]
    public void SettledBefore_EmailKeptAfterItsServerLostIt_IsLeftOut()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;
        email.IsRetainedAfterRemoteDelete = true;

        // Act
        var admitted = StoredEmailTombstone.SettledBefore(ExpungeObservedAt.AddDays(1)).Compile()(email);

        // Assert
        Assert.False(admitted);
    }

    /// <summary>An email the server still holds has no age as a tombstone at all.</summary>
    [Fact]
    public void SettledBefore_EmailTheServerStillHolds_IsLeftOut()
    {
        // Arrange
        var email = CreateEmail();

        // Act
        var admitted = StoredEmailTombstone.SettledBefore(ExpungeObservedAt).Compile()(email);

        // Assert
        Assert.False(admitted);
    }

    private static StoredEmailEntity CreateEmail()
    {
        var account = new MailboxAccountEntity { Id = "personal" };

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
            Subject = "tombstone rule",
            SizeOctets = 128,
            ContentAvailability = StoredEmailContentAvailability.Available,
        };
    }
}
