// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Notifications;

/// <summary>An unread notification about mail that arrived in one account, as the next arrival there is folded against it.</summary>
/// <remarks>
/// It is one of two shapes: a notification about one message, which counts one, and a merged notification, which counts
/// every message it stands for and names as many of them as it keeps.
/// </remarks>
public sealed record StandingMailArrival
{
    private StandingMailArrival(
        NotificationId notification,
        StoredEmailId? email,
        int counted,
        IReadOnlyList<StoredEmailId> namedEmails)
    {
        this.Notification = notification;
        this.Email = email;
        this.Counted = counted;
        this.NamedEmails = namedEmails;
    }

    /// <summary>Gets the notification that stands unread.</summary>
    public NotificationId Notification { get; }

    /// <summary>Gets the one message the notification is about, and <see langword="null" /> for a merged notification.</summary>
    public StoredEmailId? Email { get; }

    /// <summary>Gets how many arrived messages the notification stands for.</summary>
    public int Counted { get; }

    /// <summary>Gets the messages the notification names, which is the message itself for a notification about one.</summary>
    public IReadOnlyList<StoredEmailId> NamedEmails { get; }

    /// <summary>Restores a standing notification about one message.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="email">The message it is about.</param>
    /// <returns>The standing arrival, counting one.</returns>
    public static StandingMailArrival OfEmail(NotificationId notification, StoredEmailId email) =>
        new(notification, email, counted: 1, [email]);

    /// <summary>Restores a standing merged notification.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="counted">How many messages it stands for.</param>
    /// <param name="namedEmails">The messages it still names, which erasure may have left fewer than it counts.</param>
    /// <returns>The standing arrival.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="namedEmails" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="counted" /> is negative.</exception>
    public static StandingMailArrival Merged(
        NotificationId notification,
        int counted,
        IReadOnlyList<StoredEmailId> namedEmails)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(counted);
        ArgumentNullException.ThrowIfNull(namedEmails);

        return new StandingMailArrival(notification, email: null, counted, [.. namedEmails]);
    }
}
