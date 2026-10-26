// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Notifications;

namespace MailFathom.Application.Notifications;

/// <summary>Tells a person about mail work that failed after the request asking for it had already answered.</summary>
/// <remarks>
/// <para>
/// Three things are accepted by a request and done by the mail server later: a send is handed to the outbox, a change to
/// a mirrored mailbox is written down for the converger, and a draft's copy is filed in the drafts folder. When the
/// later half fails, the screen that asked has long since said it went well, and the tab that asked may be closed. A
/// durable notification is the one channel that outlives a reload and needs nobody to be polling, so each of the three
/// failures is raised here once, from the place that decided it.
/// </para>
/// <para>
/// Each is raised once per thing that failed — the send, the change record, the draft — and the deduplication key names
/// that thing rather than its account. Two refusals are two messages somebody has to decide about, so folding them into
/// one row would hide the second.
/// </para>
/// <para>
/// A send and a change belong to the account rather than to whoever asked, as ADR 0014 makes every send the account's,
/// so they are told to every user assigned it. A draft is its writer's alone and is told to them alone. A change a rule
/// or the spam verdict asked for is not reported: nothing local changed for it on a mirrored account, so no screen ever
/// claimed it happened, and a person who did not ask for it has nothing to decide.
/// </para>
/// <para>
/// The subject of the send or the draft is the row's body, as an arrival's subject is, because which message failed is
/// what a person needs to act. A change's row carries no mail at all: it leads to the message, which says what it is.
/// </para>
/// </remarks>
public sealed class MailFailureNotifications
{
    private readonly NotificationRaiser raiser;
    private readonly IMailAccountAssignments assignments;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the producer from the record it raises into.</summary>
    /// <param name="raiser">Keeps what is raised and announces it to whatever the person has open.</param>
    /// <param name="assignments">Answers who a failure about one account's mail is about.</param>
    /// <param name="timeProvider">Stamps a notification with when the failure was decided.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public MailFailureNotifications(
        NotificationRaiser raiser,
        IMailAccountAssignments assignments,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(raiser);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.raiser = raiser;
        this.assignments = assignments;
        this.timeProvider = timeProvider;
    }

    /// <summary>Reports a send the mail server refused, to everybody assigned the account it was sent from.</summary>
    /// <param name="send">The send, as the refusal left it.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept for anybody.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="send" /> is <see langword="null" />.</exception>
    /// <remarks>The row leads to the send's entry in its outbox, which is where sending it again, editing it, or dismissing it is offered.</remarks>
    public async Task<bool> ReportRefusedSendAsync(OutgoingEmailRecord send, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);

        var subject = SubjectOrNull(send.Subject);
        var users = await this.assignments.ReadUsersAssignedToAsync(send.AccountId, cancellationToken);

        return await this.RecordAsync(
            users,
            send.AccountId,
            title: "Not sent - it waits in the Outbox",
            body: subject ?? "A message with no subject was not sent. It waits in the Outbox.",
            NotificationStatement.SendRefused(carriesSubject: subject is not null),
            NotificationTarget.ToOutgoingEmail(send.Id),
            NotificationDeduplicationKey.For("send-refused", send.Id.ToString()),
            cancellationToken);
    }

    /// <summary>Reports a change somebody asked of a mailbox that MailFathom gave up on, to everybody assigned the account.</summary>
    /// <param name="change">The change record, as giving it up left it.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept for anybody, and <see langword="false" /> for a change nobody asked for in person.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="change" /> is <see langword="null" />.</exception>
    public async Task<bool> ReportAbandonedChangeAsync(MailboxMutationRecord change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        var request = change.Request;

        if (request.Requester.Origin is not MailboxMutationOrigin.Command)
        {
            return false;
        }

        var act = ActOf(request);
        var users = await this.assignments.ReadUsersAssignedToAsync(request.Account, cancellationToken);

        return await this.RecordAsync(
            users,
            request.Account,
            title: "A change did not reach your mailbox",
            body: $"{ActSaid(act)} - the mail server would not take it, so the message is still where the server holds it.",
            NotificationStatement.MailboxChangeAbandoned(act),
            NotificationTarget.ToMailboxChange(change.Id, request.StoredEmailId),
            NotificationDeduplicationKey.For("mailbox-change-abandoned", change.Id.ToString()),
            cancellationToken);
    }

    /// <summary>Reports a draft whose copy on the mail server stopped following it, to the person who wrote it.</summary>
    /// <param name="draft">The draft, as the filing that diverged left it.</param>
    /// <param name="cancellationToken">Cancels the raise.</param>
    /// <returns><see langword="true" /> when a notification was kept.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Two divergences reach this, and one sentence says both: an append the server never answered, which may or may
    /// not have left the draft in the folder, and a copy of an earlier revision that could not be taken back out, which
    /// leaves the folder showing what the draft used to say. Either way another mail client does not show what
    /// MailFathom holds. One row per draft while it stands unread, however many of its copies diverged, because a later
    /// divergence of the same draft says nothing new.
    /// </remarks>
    public Task<bool> ReportDraftCopyNotKeptAsync(MailDraftRecord draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var subject = SubjectOrNull(draft.Subject);

        return this.RecordAsync(
            [draft.User],
            draft.AccountId,
            title: "Draft out of step with your server",
            body: subject ?? "A draft with no subject is kept in MailFathom, but your mail server's copy may be missing or out of date.",
            NotificationStatement.DraftCopyNotKept(carriesSubject: subject is not null),
            NotificationTarget.ToMailDraft(draft.Id),
            NotificationDeduplicationKey.For("draft-copy-not-kept", draft.Id.ToString()),
            cancellationToken);
    }

    /// <summary>Names the act a change record asked for, folding the command and the value it was asked with into one.</summary>
    /// <param name="request">The request the record holds.</param>
    /// <returns>The act.</returns>
    internal static MailboxChangeAct ActOf(MailboxMutationRequest request)
    {
        var mutation = request.Mutation;

        if (mutation == MailboxMutation.SetSeen)
        {
            return request.DesiredSeenState is false ? MailboxChangeAct.MarkUnread : MailboxChangeAct.MarkRead;
        }

        if (mutation == MailboxMutation.SetFlagged)
        {
            return request.DesiredFlaggedState is false ? MailboxChangeAct.Unflag : MailboxChangeAct.Flag;
        }

        if (mutation == MailboxMutation.Relocate)
        {
            return MailboxChangeAct.Move;
        }

        if (mutation == MailboxMutation.Delete)
        {
            return MailboxChangeAct.Delete;
        }

        return mutation == MailboxMutation.Copy ? MailboxChangeAct.Copy : MailboxChangeAct.ChangeTags;
    }

    private static string ActSaid(MailboxChangeAct act) => act switch
    {
        MailboxChangeAct.MarkRead => "Mark as read",
        MailboxChangeAct.MarkUnread => "Mark as unread",
        MailboxChangeAct.Flag => "Flag",
        MailboxChangeAct.Unflag => "Remove the flag",
        MailboxChangeAct.Move => "Move",
        MailboxChangeAct.Delete => "Delete",
        MailboxChangeAct.Copy => "Copy",
        _ => "Change the tags",
    };

    private static string? SubjectOrNull(string? subject) =>
        string.IsNullOrWhiteSpace(subject) ? null : Shortened(subject.Trim(), Notification.MaximumBodyLength);

    private static string Shortened(string text, int maximumLength) =>
        text.Length <= maximumLength
            ? text
            : MailTextBounds.TruncateAtTextElementBoundary(text, maximumLength - 1) + "…";

    private async Task<bool> RecordAsync(
        IReadOnlyCollection<UserId> users,
        MailAccountId account,
        string title,
        string body,
        NotificationStatement statement,
        NotificationTarget target,
        NotificationDeduplicationKey deduplicationKey,
        CancellationToken cancellationToken)
    {
        var occurredAt = this.timeProvider.GetUtcNow();

        // An account identifier is the operator's own text and nothing bounds its length, so one that does not fit the
        // source column leaves the kind as the whole of the source line, as a synchronization notification does.
        var source = account.Value.Length <= Notification.MaximumSourceLength ? account.Value : null;

        var anyKept = false;

        foreach (var user in users.Distinct())
        {
            var notification = Notification.Compose(
                NotificationId.Create(Guid.CreateVersion7(occurredAt)),
                user,
                NotificationKind.Mail,
                title,
                body,
                statement,
                source,
                target,
                deduplicationKey,
                occurredAt);

            anyKept |= await this.raiser.RecordAndAnnounceAsync(notification, cancellationToken);
        }

        return anyKept;
    }
}
