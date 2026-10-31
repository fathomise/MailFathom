// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Settlement;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Settlement;

/// <summary>Covers when an arriving message settles, what releases it without a stage having finished, and whose arrival is announced.</summary>
public sealed class SettlingEmailTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan ClassificationWait = TimeSpan.FromMinutes(15);

    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly DateTimeOffset ReleasedWhenStoredBefore =
        Now - ClassificationWait - MailSettlementPass.MaximumPlacementWait;

    private static DerivedWorkAdmissionTerms Classifying { get; } = new(
        [Account],
        [new MailFolderIdentity(Account, Junk)],
        [new MailFolderIdentity(Account, Inbox)],
        Now - ClassificationWait);

    private static DerivedWorkAdmissionTerms NotClassifying { get; } = new([], [], [], Now - ClassificationWait);

    /// <summary>
    /// A sender's authentication, trust, and signature are established when the content is read, so a message whose
    /// content waits for storage headroom waits with it, and is released on the same bound as every other wait.
    /// </summary>
    [Fact]
    public void DecideUnder_ContentAwaitingStorageHeadroom_KeepsSettlingUntilTheLongestWait()
    {
        // Arrange
        var awaitingContent = Email() with { ContentAvailability = StoredEmailContentAvailability.AwaitingStorageHeadroom };

        // Act
        var whileWaiting = awaitingContent.DecideUnder(NotClassifying, ReleasedWhenStoredBefore);
        var pastTheWait = (awaitingContent with { StoredAt = ReleasedWhenStoredBefore })
            .DecideUnder(NotClassifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.Settling, whileWaiting);
        Assert.Equal(MailSettlement.ReleasedAfterWaiting, pastTheWait);
    }

    [Fact]
    public void DecideUnder_RulesFinishedAndNothingClassifies_Settles()
    {
        // Act
        var settlement = Email().DecideUnder(NotClassifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.Settled, settlement);
    }

    /// <summary>A message with no verdict yet has not been offered to the rules, and a verdict may still file it into junk.</summary>
    [Fact]
    public void DecideUnder_AClassifiedFolderWithNoVerdictInsideTheWait_KeepsSettling()
    {
        // Act
        var settlement = Email(verdict: null, rulesFinished: false).DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.Settling, settlement);
    }

    [Fact]
    public void DecideUnder_NoVerdictPastItsWaitAndTheRulesFinished_IsReleasedWithoutAVerdict()
    {
        // Arrange
        var email = Email(verdict: null, storedAt: Now - ClassificationWait - TimeSpan.FromMinutes(1));

        // Act
        var settlement = email.DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.ReleasedWithoutVerdict, settlement);
    }

    [Fact]
    public void DecideUnder_ContentTooLargeToClassifyAndTheRulesFinished_IsReleasedAsUnclassifiable()
    {
        // Arrange
        var email = Email(verdict: null) with { ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit };

        // Act
        var settlement = email.DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.ReleasedAsUnclassifiable, settlement);
    }

    /// <summary>A rule may still move a message judged ordinary mail, so it waits for the rule pass.</summary>
    [Fact]
    public void DecideUnder_AVerdictOfOrdinaryMailTheRulesHaveNotEvaluated_KeepsSettling()
    {
        // Act
        var settlement = Email(SpamVerdict.NotSpam, rulesFinished: false).DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.Settling, settlement);
    }

    /// <summary>No rule is ever offered junk, so a junk verdict does not wait for a rule pass that will never come.</summary>
    [Fact]
    public void DecideUnder_AJunkVerdictTheRulesWillNeverEvaluate_Settles()
    {
        // Act
        var settlement = Email(SpamVerdict.Spam, rulesFinished: false).DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.Settled, settlement);
    }

    /// <summary>A filing into junk that has not reached the stored row would show the message in the inbox first.</summary>
    [Fact]
    public void DecideUnder_AJunkVerdictWhoseFilingIsStillOnItsWay_KeepsSettling()
    {
        // Arrange
        var email = Email(SpamVerdict.Spam, rulesFinished: false) with { AwaitsPlacement = true };

        // Act
        var settlement = email.DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.Settling, settlement);
    }

    /// <summary>A move that never reaches the row holds the message only for as long as a message is ever held.</summary>
    [Fact]
    public void DecideUnder_AMoveStillOnItsWayPastTheLongestWait_IsReleasedAfterWaiting()
    {
        // Arrange
        var email = Email(SpamVerdict.NotSpam, storedAt: ReleasedWhenStoredBefore) with { AwaitsPlacement = true };

        // Act
        var settlement = email.DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.ReleasedAfterWaiting, settlement);
    }

    /// <summary>A rule pass never reaches a message the server no longer holds, so nothing is left to wait for.</summary>
    [Fact]
    public void DecideUnder_ATombstonedMessageTheRulesNeverEvaluated_Settles()
    {
        // Arrange
        var email = Email(SpamVerdict.NotSpam, rulesFinished: false) with { IsTombstoned = true };

        // Act
        var settlement = email.DecideUnder(Classifying, ReleasedWhenStoredBefore);

        // Assert
        Assert.Equal(MailSettlement.Settled, settlement);
    }

    [Theory]
    [InlineData(true, MailFolderSpecialUse.Inbox, true)]
    [InlineData(false, MailFolderSpecialUse.Inbox, true)]
    [InlineData(true, null, true)]
    [InlineData(true, MailFolderSpecialUse.Archive, true)]
    [InlineData(true, MailFolderSpecialUse.Junk, false)]
    [InlineData(true, MailFolderSpecialUse.Trash, false)]
    [InlineData(false, null, false)]
    [InlineData(false, MailFolderSpecialUse.Sent, false)]
    public void IsAnnounced_WhereTheMessageArrivedAndWhereItSettled_AnnouncesTheInboxAndWhatARuleFiledOutOfIt(
        bool arrivedInInbox,
        MailFolderSpecialUse? folderRole,
        bool expected)
    {
        // Arrange
        var email = Email() with { ArrivedInInbox = arrivedInInbox, FolderRole = folderRole };

        // Act
        var announced = email.IsAnnounced();

        // Assert
        Assert.Equal(expected, announced);
    }

    /// <summary>Junk is never news, even where nothing filed it out of the inbox.</summary>
    [Fact]
    public void IsAnnounced_AJunkVerdictLeftInTheInbox_IsNotAnnounced()
    {
        // Act
        var announced = Email(SpamVerdict.Spam).IsAnnounced();

        // Assert
        Assert.False(announced);
    }

    /// <summary>A message read before MailFathom saw it is nothing new, wherever it settled.</summary>
    [Fact]
    public void IsAnnounced_AMessageTheSourceAlreadyMarkedRead_IsNotAnnounced()
    {
        // Act
        var announced = (Email() with { IsSeen = true }).IsAnnounced();

        // Assert
        Assert.False(announced);
    }

    [Fact]
    public void IsAnnounced_AMessageTheServerNoLongerHolds_IsNotAnnounced()
    {
        // Act
        var announced = (Email() with { IsTombstoned = true }).IsAnnounced();

        // Assert
        Assert.False(announced);
    }

    /// <summary>A message already read elsewhere is still answered automatically, while junk and a deleted one are not.</summary>
    [Theory]
    [InlineData(true, SpamVerdict.NotSpam, false, true)]
    [InlineData(false, SpamVerdict.Spam, false, false)]
    [InlineData(false, SpamVerdict.NotSpam, true, false)]
    public void IsAnswerable_WhatTheMessageIs_AnswersInboxMailReadOrNot(
        bool isSeen,
        SpamVerdict verdict,
        bool isTombstoned,
        bool expected)
    {
        // Arrange
        var email = Email(verdict) with { IsSeen = isSeen, IsTombstoned = isTombstoned };

        // Act
        var answerable = email.IsAnswerable();

        // Assert
        Assert.Equal(expected, answerable);
    }

    private static SettlingEmail Email(
        SpamVerdict? verdict = SpamVerdict.NotSpam,
        bool rulesFinished = true,
        DateTimeOffset? storedAt = null) =>
        new(
            StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")),
            Account,
            Inbox,
            storedAt ?? Now,
            StoredEmailContentAvailability.Available,
            verdict,
            rulesFinished,
            IsTombstoned: false,
            AwaitsPlacement: false,
            IsSeen: false,
            ArrivedInInbox: true,
            MailFolderSpecialUse.Inbox);
}
