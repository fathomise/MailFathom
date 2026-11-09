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
/// The rule stopped being a null check when an authored delete gained a value that keeps the local copy readable, so
/// both columns are asserted here rather than in each query that uses them. Evaluating the expression needs no
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

    /// <summary>A delete the user authored to free the server keeps the mail readable, which is the whole of that setting.</summary>
    /// <remarks>
    /// The row carries the expunge as well, because the server genuinely no longer holds the message and the
    /// reconciliation queue is ordered by that column. Reading only that column here would hide exactly the mail this
    /// disposition exists to keep.
    /// </remarks>
    [Fact]
    public void IsNotTombstoned_EmailRetainedAfterAnAuthoredDelete_IsAdmittedDespiteTheExpunge()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;
        email.IsRetainedAfterAuthoredDelete = true;

        // Act
        var admitted = StoredEmailTombstone.IsNotTombstoned.Compile()(email);

        // Assert
        Assert.True(admitted);
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
    public void SettledBefore_EmailRetainedAfterAnAuthoredDelete_IsLeftOut()
    {
        // Arrange
        var email = CreateEmail();
        email.RemoteExpungeObservedAt = ExpungeObservedAt;
        email.IsRetainedAfterAuthoredDelete = true;

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
