// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Notifications;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what a producer recorded, under the same deduplication rule the persisted store publishes.</summary>
/// <remarks>
/// The rule is restated here rather than substituted away because it is what the producers are written against: a
/// producer that composed a fresh key per call would suppress nothing, and a test whose double accepted everything
/// would pass anyway. What it deliberately does not model is the race the partial unique index settles, which no
/// in-memory double can establish and the integration suite proves against a real database.
/// </remarks>
internal sealed class InMemoryNotificationStore : INotificationStore
{
    private readonly List<Notification> recorded = [];

    private readonly Dictionary<NotificationId, RecordedArrival> arrivals = [];

    /// <summary>Gets the notifications this store kept, in the order they were recorded.</summary>
    public IReadOnlyList<Notification> Recorded => this.recorded;

    /// <summary>Gets or sets whether a record is refused with a fault rather than kept, as a store that cannot be reached refuses one.</summary>
    public bool RefusesRecords { get; set; }

    /// <inheritdoc />
    public Task<bool> RecordAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (this.RefusesRecords)
        {
            throw new InvalidOperationException("The notification store refused the record.");
        }

        var standing = this.recorded.Any(candidate => candidate.User == notification.User
            && candidate.DeduplicationKey == notification.DeduplicationKey
            && !candidate.IsRead);

        if (standing)
        {
            return Task.FromResult(false);
        }

        this.recorded.Add(notification);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Notification>> RecordMailArrivalAsync(
        UserId user,
        MailAccountId account,
        IReadOnlyList<ArrivedEmail> arrived,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arrived);

        StandingMailArrival[] standing =
        [
            .. this.recorded
                .Where(candidate => candidate.User == user
                    && !candidate.IsRead
                    && this.arrivals.TryGetValue(candidate.Id, out var arrival)
                    && arrival.Account == account)
                .Select(candidate => candidate.Target.Message is { } email
                    ? StandingMailArrival.OfEmail(candidate.Id, email)
                    : StandingMailArrival.Merged(
                        candidate.Id,
                        candidate.Statement?.Counted ?? 0,
                        this.arrivals[candidate.Id].NamedEmails)),
        ];

        var fold = MailArrivalFold.Decide(user, account, standing, arrived, occurredAt);
        var replaced = fold.Replaced.ToHashSet();

        this.recorded.RemoveAll(candidate => replaced.Contains(candidate.Id));

        foreach (var raised in fold.Raised)
        {
            this.recorded.Add(raised.Notification);
            this.arrivals[raised.Notification.Id] = new RecordedArrival(account, raised.NamedEmails);
        }

        return Task.FromResult<IReadOnlyList<Notification>>([.. fold.Raised.Select(static raised => raised.Notification)]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailId>?> ReadNamedEmailsAsync(
        UserId user,
        NotificationId notification,
        CancellationToken cancellationToken)
    {
        if (!this.recorded.Any(candidate => candidate.User == user && candidate.Id == notification))
        {
            return Task.FromResult<IReadOnlyList<StoredEmailId>?>(null);
        }

        return Task.FromResult<IReadOnlyList<StoredEmailId>?>(
            this.arrivals.TryGetValue(notification, out var arrival) ? arrival.NamedEmails : []);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Notification>> ReadPageAsync(
        UserId user,
        NotificationCursor? after,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var ordered = this.recorded
            .Where(candidate => candidate.User == user)
            .OrderByDescending(candidate => candidate.OccurredAt)
            .ThenByDescending(candidate => candidate.Id.Value);

        var beyond = after is { } boundary
            ? ordered.Where(candidate => candidate.OccurredAt < boundary.OccurredAt
                || (candidate.OccurredAt == boundary.OccurredAt
                    && candidate.Id.Value.CompareTo(boundary.NotificationId.Value) < 0))
            : ordered;

        return Task.FromResult<IReadOnlyList<Notification>>([.. beyond.Take(limit)]);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(UserId user, CancellationToken cancellationToken) =>
        Task.FromResult(this.recorded.Count(candidate => candidate.User == user && !candidate.IsRead));

    /// <inheritdoc />
    public Task<NotificationReadOutcome> SetReadAsync(
        UserId user,
        NotificationId notification,
        bool isRead,
        CancellationToken cancellationToken)
    {
        var position = this.recorded.FindIndex(candidate => candidate.User == user && candidate.Id == notification);

        if (position < 0)
        {
            return Task.FromResult(NotificationReadOutcome.NotFound);
        }

        var stored = this.recorded[position];

        if (stored.IsRead == isRead)
        {
            return Task.FromResult(NotificationReadOutcome.Applied);
        }

        if (!isRead
            && this.recorded.Any(candidate => candidate.User == user
                && candidate.DeduplicationKey == stored.DeduplicationKey
                && !candidate.IsRead))
        {
            return Task.FromResult(NotificationReadOutcome.ConditionAlreadyStanding);
        }

        this.recorded[position] = InReadState(stored, isRead);

        return Task.FromResult(NotificationReadOutcome.Applied);
    }

    /// <inheritdoc />
    public Task<int> MarkAllReadAsync(UserId user, CancellationToken cancellationToken)
    {
        var marked = 0;

        for (var position = 0; position < this.recorded.Count; position++)
        {
            if (this.recorded[position] is { IsRead: false } unread && unread.User == user)
            {
                this.recorded[position] = InReadState(unread, isRead: true);
                marked++;
            }
        }

        return Task.FromResult(marked);
    }

    /// <inheritdoc />
    public Task<int> EraseAsync(
        UserId user,
        IReadOnlyCollection<NotificationId> notifications,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var named = notifications.ToHashSet();

        return Task.FromResult(this.recorded.RemoveAll(candidate => candidate.User == user
            && named.Contains(candidate.Id)));
    }

    /// <summary>Rebuilds one notification in a stated read state, which is the only field a store may move.</summary>
    private static Notification InReadState(Notification notification, bool isRead) => Notification.Restore(
        notification.Id,
        notification.User,
        notification.Kind,
        notification.Title,
        notification.Body,
        notification.Statement,
        notification.Source,
        notification.Target,
        notification.DeduplicationKey,
        notification.OccurredAt,
        isRead);

    /// <inheritdoc />
    public Task<int> EraseOccurredBeforeAsync(
        UserId user,
        DateTimeOffset occurredBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        Notification[] expiring =
        [
            .. this.recorded
                .Where(candidate => candidate.User == user && candidate.OccurredAt < occurredBefore)
                .OrderBy(candidate => candidate.OccurredAt)
                .Take(limit),
        ];

        foreach (var notification in expiring)
        {
            this.recorded.Remove(notification);
        }

        return Task.FromResult(expiring.Length);
    }

    private sealed record RecordedArrival(MailAccountId Account, IReadOnlyList<StoredEmailId> NamedEmails);
}
