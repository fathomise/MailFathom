// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.Notifications;

/// <summary>What one account's arrival does to a person's unread arrival notifications: which go, and which are raised.</summary>
/// <param name="Replaced">The standing notifications the arrival merged away.</param>
/// <param name="Raised">The notifications the arrival raises.</param>
/// <remarks>
/// <para>
/// One rule decides it, per person and per account. While the account's unread arrivals number
/// <see cref="MaximumSeparateArrivals" /> or fewer, every message is a notification of its own, naming its sender and its
/// subject and leading to it. The arrival that would take them past that replaces them all with one merged notification
/// counting every message, and while that one stands unread each later arrival replaces it again with a count that
/// includes its own. Reading the merged notification ends it, so the next arrival starts again a message at a time.
/// </para>
/// <para>
/// A merged notification is replaced rather than updated, so it is raised anew — newest in the centre, and announced to
/// whatever the person has open — exactly as the arrival it now includes would have been. It names the messages it
/// stands for so a question can be asked about them, up to <see cref="MaximumNamedEmails" />, keeping the newest; the
/// count is never bounded, because it is what the person is told.
/// </para>
/// </remarks>
public sealed record MailArrivalFold(
    IReadOnlyList<NotificationId> Replaced,
    IReadOnlyList<RaisedMailArrival> Raised)
{
    /// <summary>The most unread arrivals one account says a message at a time before they are merged.</summary>
    public const int MaximumSeparateArrivals = 5;

    /// <summary>The most messages a merged notification names, which bounds what a question about it reads.</summary>
    public const int MaximumNamedEmails = 100;

    private const string MergedCondition = "mail-arrived";

    private const string SeparateCondition = "message-arrived";

    private const string NoSubject = "No subject";

    private const string UnknownSender = "Unknown sender";

    /// <summary>Gets a fold that changes nothing, which is what an arrival every message of which already stands produces.</summary>
    public static MailArrivalFold Nothing { get; } = new([], []);

    /// <summary>Gets whether the fold changes nothing.</summary>
    public bool IsEmpty => this.Replaced.Count == 0 && this.Raised.Count == 0;

    /// <summary>Folds one account's arrival into the unread arrival notifications a person already holds for it.</summary>
    /// <param name="user">The person told.</param>
    /// <param name="account">The account the mail arrived in.</param>
    /// <param name="standing">The person's unread arrival notifications for that account.</param>
    /// <param name="arrived">The messages that arrived, in the order they were stored.</param>
    /// <param name="occurredAt">When the arrival was observed.</param>
    /// <returns>What the arrival replaces and raises.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="standing" /> or <paramref name="arrived" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A message already standing — named by a notification of its own or by a merged one — is not said again, so an
    /// arrival reported twice raises nothing the second time.
    /// </remarks>
    public static MailArrivalFold Decide(
        UserId user,
        MailAccountId account,
        IReadOnlyList<StandingMailArrival> standing,
        IReadOnlyList<ArrivedEmail> arrived,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(arrived);

        var alreadyStanding = standing.SelectMany(static notice => notice.NamedEmails).ToHashSet();
        var news = arrived
            .Where(email => alreadyStanding.Add(email.Email))
            .ToArray();

        if (news.Length == 0)
        {
            return Nothing;
        }

        var source = account.Value.Length <= Notification.MaximumSourceLength ? account.Value : null;
        var standingCount = standing.Sum(static notice => notice.Counted);
        var mergedStanding = standing.Any(static notice => notice.Email is null);

        if (!mergedStanding && standingCount + news.Length <= MaximumSeparateArrivals)
        {
            return new MailArrivalFold(
                [],
                [.. news.Select(email => new RaisedMailArrival(Separate(user, email, source, occurredAt), []))]);
        }

        var named = standing
            .SelectMany(static notice => notice.NamedEmails)
            .Concat(news.Select(static email => email.Email))
            .TakeLast(MaximumNamedEmails)
            .ToArray();

        return new MailArrivalFold(
            [.. standing.Select(static notice => notice.Notification)],
            [new RaisedMailArrival(Merged(user, account, standingCount + news.Length, source, occurredAt), named)]);
    }

    /// <summary>Gets the condition a merged notification about an account stands under.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The deduplication key.</returns>
    public static NotificationDeduplicationKey MergedConditionFor(MailAccountId account) =>
        NotificationDeduplicationKey.For(MergedCondition, account.Value);

    private static Notification Separate(UserId user, ArrivedEmail email, string? source, DateTimeOffset occurredAt) =>
        Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(occurredAt)),
            user,
            NotificationKind.Mail,
            Shortened(email.Sender ?? UnknownSender, Notification.MaximumTitleLength),
            email.Subject is { } subject ? Shortened(subject, Notification.MaximumBodyLength) : NoSubject,
            NotificationStatement.MessageArrived(carriesSubject: email.Subject is not null),
            source,
            NotificationTarget.ToMessage(email.Email),
            NotificationDeduplicationKey.For(SeparateCondition, email.Email.ToString()),
            occurredAt);

    private static Notification Merged(
        UserId user,
        MailAccountId account,
        int count,
        string? source,
        DateTimeOffset occurredAt) =>
        Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(occurredAt)),
            user,
            NotificationKind.Mail,
            "New mail",
            string.Create(CultureInfo.InvariantCulture, $"{count} new messages arrived."),
            NotificationStatement.MailArrived(count),
            source,
            NotificationTarget.ToScreen(NotificationScreen.Mail),
            MergedConditionFor(account),
            occurredAt);

    // A sender's name and a subject are somebody else's text and nothing bounds their length, while the record's
    // columns are bounded. The row is a headline rather than the message, so the end is cut and marked as cut.
    private static string Shortened(string text, int maximumLength) =>
        text.Length <= maximumLength ? text : string.Concat(text.AsSpan(0, maximumLength - 1), "…");
}
