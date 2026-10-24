// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Writes one notification and tells whatever that person already has open about it.</summary>
/// <remarks>
/// <para>
/// The two halves are one act rather than two a producer remembers to perform in order, which is why they are here
/// rather than in each producer: a row nobody announced is a bell that waits for the next interval, and an
/// announcement of a row the deduplication rule folded away is a bell drawn for a statement the centre does not hold.
/// Every producer therefore raises through this, exactly as every producer records through
/// <see cref="INotificationStore" />.
/// </para>
/// <para>
/// The unread count is read only where a row was actually written and something is listening, so a deployment serving
/// no client and a producer that changed nothing both pay nothing for this.
/// </para>
/// </remarks>
public sealed class NotificationRaiser
{
    private readonly INotificationStore store;
    private readonly ClientSignals signals;

    /// <summary>Initializes the raiser.</summary>
    /// <param name="store">Keeps what is raised, and decides whether the condition was already standing.</param>
    /// <param name="signals">Tells an open client that a row was written.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public NotificationRaiser(INotificationStore store, ClientSignals signals)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(signals);

        this.store = store;
        this.signals = signals;
    }

    /// <summary>Records one notification and announces it where it was kept.</summary>
    /// <param name="notification">The notification to raise.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when the notification was kept, and <see langword="false" /> when an unread one already names the same condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="notification" /> is <see langword="null" />.</exception>
    public async Task<bool> RecordAndAnnounceAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!await this.store.RecordAsync(notification, cancellationToken))
        {
            return false;
        }

        if (this.signals.Reaches)
        {
            var unreadCount = await this.store.CountUnreadAsync(notification.User, cancellationToken);

            this.signals.Publish(ClientSignal.NotificationRaised(notification, unreadCount));
        }

        return true;
    }

    /// <summary>Folds one account's arrival into a user's notifications, and announces each one it raised.</summary>
    /// <param name="user">The user told.</param>
    /// <param name="account">The account the mail arrived in.</param>
    /// <param name="arrived">The messages that arrived, in the order they were stored.</param>
    /// <param name="occurredAt">When the arrival was observed.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when at least one notification was raised.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arrived" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The unread count is read once, after the fold, and every announcement carries it: a merged notification replacing
    /// five is one new row and four fewer, which is a count no announcement made before the fold landed could state.
    /// </remarks>
    public async Task<bool> RecordAndAnnounceMailArrivalAsync(
        UserId user,
        MailAccountId account,
        IReadOnlyList<ArrivedEmail> arrived,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arrived);

        var raised = await this.store.RecordMailArrivalAsync(user, account, arrived, occurredAt, cancellationToken);

        if (raised.Count == 0)
        {
            return false;
        }

        if (this.signals.Reaches)
        {
            var unreadCount = await this.store.CountUnreadAsync(user, cancellationToken);

            foreach (var notification in raised)
            {
                this.signals.Publish(ClientSignal.NotificationRaised(notification, unreadCount));
            }
        }

        return true;
    }
}
