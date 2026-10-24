// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Turns what a synchronization run observed into what the account's assigned users are told about it.</summary>
/// <remarks>
/// <para>
/// The run is the only producer wired today, and it produces two things: that mail arrived, and that something about
/// the account needs a person. Both are facts the service holds and the client cannot derive, which is the whole
/// reason the record exists.
/// </para>
/// <para>
/// Mail is reported a message at a time while an account's unread arrivals are few, and as one merged notification
/// once they are not: <see cref="MailArrivalFold" /> is that rule. A notification about one message names its sender
/// and its subject and leads to it, because what a person away from the screen wants to know is whether anything that
/// arrived needs them; forty such rows would bury every other kind of notification under one mailbox's traffic, which
/// is what the merged one is for.
/// </para>
/// <para>
/// The sender and the subject are the only things read from mail here, and only for an arrival small enough to be said
/// a message at a time. No body fragment, recipient, filename, or credential reaches the record through this path, and
/// neither the sender nor the subject reaches a log or a telemetry event.
/// </para>
/// <para>
/// Every raise states its condition twice, in the two forms a notification is read in. The statement is the condition
/// itself and the numbers it is made with, which is what a client turns into a sentence in whatever language the
/// person reading it has; the title and the body beside it are that same condition written out in English, for a
/// reader with no client to say it in their own. Both are composed here from the same values, so the two cannot come
/// to describe different runs.
/// </para>
/// <para>
/// A row that was kept is also announced to whatever the user has open, which is <see cref="NotificationRaiser" />'s
/// half of the raise rather than this producer's: writing and announcing are one act, and a row the deduplication rule
/// folded into a standing one is not announced for the same reason it is not written.
/// </para>
/// <para>
/// A notification is one person's, so a mailbox assigned to several people is one condition raised once for each of
/// them: the run observed one thing about one account, and each of the people served by it has their own unread list
/// and their own deduplication. An account assigned to nobody raises nothing, which is the same answer the signal
/// channel gives — there is nobody the condition is about.
/// </para>
/// </remarks>
public sealed class SynchronizationNotifications
{
    private readonly NotificationRaiser raiser;
    private readonly IMailAccountAssignments assignments;
    private readonly IStoredEmailSummaryReader summaries;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the producer from the record it raises into.</summary>
    /// <param name="raiser">Keeps what is raised and announces it to whatever the person has open.</param>
    /// <param name="assignments">Answers who a condition about one mailbox is about.</param>
    /// <param name="summaries">Reads who sent an arrived message and what it is about.</param>
    /// <param name="timeProvider">Stamps a notification with when the run observed what it describes.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public SynchronizationNotifications(
        NotificationRaiser raiser,
        IMailAccountAssignments assignments,
        IStoredEmailSummaryReader summaries,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(raiser);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.raiser = raiser;
        this.assignments = assignments;
        this.summaries = summaries;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reports the mail one run committed, folded into what each assigned user already holds for the account.</summary>
    /// <param name="account">The account the run was over.</param>
    /// <param name="arrivedEmails">The messages the run committed that were mail arriving for the person, in the order it stored them.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was raised for anybody.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="arrivedEmails" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A run that committed nothing says nothing: an empty run is the ordinary case and is not an event anybody was
    /// away from the screen for. A message the run committed and somebody erased before this report reads it is still
    /// counted, but named by no sender, because it was news whether or not it is still there.
    /// </remarks>
    public async Task<bool> ReportArrivedMailAsync(
        MailAccountId account,
        IReadOnlyList<StoredEmailId> arrivedEmails,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arrivedEmails);

        if (arrivedEmails.Count == 0)
        {
            return false;
        }

        var arrived = await this.ReadArrivedAsync(arrivedEmails, cancellationToken);
        var occurredAt = this.timeProvider.GetUtcNow();
        var anyRaised = false;

        foreach (var user in await this.assignments.ReadUsersAssignedToAsync(account, cancellationToken))
        {
            anyRaised |= await this.raiser.RecordAndAnnounceMailArrivalAsync(
                user,
                account,
                arrived,
                occurredAt,
                cancellationToken);
        }

        return anyRaised;
    }

    /// <summary>Reports a run that ended with folders it did not finish.</summary>
    /// <param name="account">The account the run was over.</param>
    /// <param name="failedFolderCount">How many of the run's folders did not finish.</param>
    /// <param name="scheduledFolderCount">How many folders the run scheduled.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when either count is negative.</exception>
    /// <remarks>
    /// A run that finished everything it scheduled says nothing, because a working mailbox is not news. The row leads
    /// nowhere: there is nothing for the person to open, and the account will try again on its own.
    /// </remarks>
    public Task<bool> ReportIncompleteRunAsync(
        MailAccountId account,
        int failedFolderCount,
        int scheduledFolderCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(failedFolderCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scheduledFolderCount);

        if (failedFolderCount == 0)
        {
            return Task.FromResult(false);
        }

        return this.RecordAsync(
            account,
            NotificationKind.System,
            title: "Some mail could not be fetched",
            body: string.Create(
                CultureInfo.InvariantCulture,
                $"{failedFolderCount} of {scheduledFolderCount} folders did not finish. MailFathom will try again."),
            NotificationStatement.SynchronizationIncomplete(failedFolderCount, scheduledFolderCount),
            NotificationTarget.Nothing,
            condition: "synchronization-incomplete",
            cancellationToken);
    }

    /// <summary>Reports an account whose credential the mail server refused.</summary>
    /// <param name="account">The account the server refused.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept.</returns>
    /// <remarks>
    /// This is the condition the deduplication rule exists for. A refused credential is refused again on every run
    /// until somebody replaces it, so the statement is made once and stays made until it has been read.
    /// </remarks>
    public Task<bool> ReportRefusedCredentialAsync(
        MailAccountId account,
        CancellationToken cancellationToken) =>
        this.RecordAsync(
            account,
            NotificationKind.System,
            title: "This account needs signing in again",
            body: "The mail server refused the credential MailFathom holds, so this account is no longer being fetched.",
            NotificationStatement.CredentialRefused(),
            NotificationTarget.ToScreen(NotificationScreen.Settings),
            condition: "credential-refused",
            cancellationToken);

    // Only an arrival small enough to be said a message at a time is read for its senders and subjects: a larger one is
    // merged whatever the person already holds, so reading it would fetch names nobody is shown.
    private async Task<IReadOnlyList<ArrivedEmail>> ReadArrivedAsync(
        IReadOnlyList<StoredEmailId> arrivedEmails,
        CancellationToken cancellationToken)
    {
        if (arrivedEmails.Count > MailArrivalFold.MaximumSeparateArrivals)
        {
            return [.. arrivedEmails.Select(static email => new ArrivedEmail(email, sender: null, subject: null))];
        }

        var read = await this.summaries.ReadSummariesAsync(arrivedEmails, cancellationToken);

        return
        [
            .. arrivedEmails.Select(email => read.TryGetValue(email, out var summary)
                ? new ArrivedEmail(email, summary.SenderDisplayName ?? summary.SenderAddress, summary.Subject)
                : new ArrivedEmail(email, sender: null, subject: null)),
        ];
    }

    private async Task<bool> RecordAsync(
        MailAccountId account,
        NotificationKind kind,
        string title,
        string body,
        NotificationStatement statement,
        NotificationTarget target,
        string condition,
        CancellationToken cancellationToken)
    {
        var occurredAt = this.timeProvider.GetUtcNow();
        var accountId = account.Value;

        // An account identifier is the operator's own text and nothing bounds its length, while both places one
        // reaches here are bounded columns. The key reduces an outsized one to a digest of itself; the source line is
        // a label rather than an identity, so an identifier that cannot be shown leaves the kind as the whole of it
        // rather than being shown truncated as an account nobody configured.
        var source = accountId.Length <= Notification.MaximumSourceLength ? accountId : null;

        var anyKept = false;

        // One row per assigned user rather than one for the mailbox, because the unread list, the read mark, and the
        // deduplication window are each one person's. The identity is generated per row for the same reason: two
        // people told the same thing are two rows either of them may read or dismiss without touching the other's.
        foreach (var user in await this.assignments.ReadUsersAssignedToAsync(account, cancellationToken))
        {
            var notification = Notification.Compose(
                NotificationId.Create(Guid.CreateVersion7(occurredAt)),
                user,
                kind,
                title,
                body,
                statement,
                source,
                target,
                NotificationDeduplicationKey.For(condition, account.Value),
                occurredAt);

            anyKept |= await this.raiser.RecordAndAnnounceAsync(notification, cancellationToken);
        }

        return anyKept;
    }
}
