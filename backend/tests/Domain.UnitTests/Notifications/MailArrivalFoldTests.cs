// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;
using Xunit;

namespace MailFathom.Domain.UnitTests.Notifications;

/// <summary>Covers the bounds of the rule an account's arrival is folded by, which the producer's own tests do not reach.</summary>
public sealed class MailArrivalFoldTests
{
    private static readonly UserId User = UserId.Create(Guid.NewGuid());

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset ArrivedAt = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A merged notification names a bounded number of messages and keeps the newest, while its count stays whole.</summary>
    [Fact]
    public void Decide_MoreArrivalsThanAMergedNotificationNames_KeepsTheNewestAndCountsThemAll()
    {
        // Arrange
        var arrived = Arrivals(MailArrivalFold.MaximumNamedEmails + 20);

        // Act
        var fold = MailArrivalFold.Decide(User, Account, [], arrived, ArrivedAt);

        // Assert
        var raised = Assert.Single(fold.Raised);
        Assert.Equal(arrived.Length, raised.Notification.Statement!.Counted);
        Assert.Equal(
            arrived.Skip(20).Select(static email => email.Email),
            raised.NamedEmails);
    }

    /// <summary>A sender's name is somebody else's text, so one past the headline's bound is cut and marked as cut.</summary>
    [Fact]
    public void Decide_SenderNamePastTheHeadlineBound_ShortensItToTheBound()
    {
        // Arrange
        var email = new ArrivedEmail(StoredEmailId.Create(Guid.NewGuid()), new string('a', 400), "Subject");

        // Act
        var fold = MailArrivalFold.Decide(User, Account, [], [email], ArrivedAt);

        // Assert
        var title = Assert.Single(fold.Raised).Notification.Title;
        Assert.Equal(Notification.MaximumTitleLength, title.Length);
        Assert.EndsWith("…", title, StringComparison.Ordinal);
    }

    /// <summary>A message a merged notification already names is not news again, so its second report changes nothing.</summary>
    [Fact]
    public void Decide_ArrivalAMergedNotificationAlreadyNames_ChangesNothing()
    {
        // Arrange
        var arrived = Arrivals(2);
        var standing = StandingMailArrival.Merged(
            NotificationId.Create(Guid.NewGuid()),
            counted: 6,
            [.. arrived.Select(static email => email.Email)]);

        // Act
        var fold = MailArrivalFold.Decide(User, Account, [standing], arrived, ArrivedAt);

        // Assert
        Assert.True(fold.IsEmpty);
    }

    private static ArrivedEmail[] Arrivals(int count) =>
    [
        .. Enumerable.Range(0, count)
            .Select(number => new ArrivedEmail(StoredEmailId.Create(Guid.NewGuid()), $"Sender {number}", $"Subject {number}")),
    ];
}
