// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Calendar;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Tasks;

namespace MailFathom.Domain.Notifications;

/// <summary>Where opening a notification leads.</summary>
/// <remarks>
/// <para>
/// The shapes are closed and each is reached through its own factory, so a target can never half exist — a
/// message target with no message, or a screen target that also names one. Which of them a producer chose is what
/// a reader switches on, and <see cref="NotificationTargetKind" /> is that answer without unwrapping anything.
/// </para>
/// <para>
/// A message target is the one that ties a notification to mail, and it is what makes a notification erasable with the
/// message it describes: the record inherits the message's deletion rather than being swept for separately, so nothing
/// can leave a row pointing at mail that is gone.
/// </para>
/// </remarks>
public sealed record NotificationTarget
{
    private NotificationTarget(
        NotificationTargetKind kind,
        StoredEmailId? message,
        NotificationScreen? screen,
        CalendarEventId? calendarEvent,
        PersonalTaskId? personalTask,
        OutgoingEmailId? outgoingEmail = null,
        MailboxMutationRecordId? mailboxChange = null,
        MailDraftId? mailDraft = null)
    {
        this.Kind = kind;
        this.Message = message;
        this.Screen = screen;
        this.CalendarEvent = calendarEvent;
        this.PersonalTask = personalTask;
        this.OutgoingEmail = outgoingEmail;
        this.MailboxChange = mailboxChange;
        this.MailDraft = mailDraft;
    }

    /// <summary>Gets which of the shapes this target is.</summary>
    public NotificationTargetKind Kind { get; }

    /// <summary>Gets the stored message the notification leads to, which a message target and a change target carry, and <see langword="null" /> for every other shape.</summary>
    public StoredEmailId? Message { get; }

    /// <summary>Gets the screen the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public NotificationScreen? Screen { get; }

    /// <summary>Gets the calendar event the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public CalendarEventId? CalendarEvent { get; }

    /// <summary>Gets the task the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public PersonalTaskId? PersonalTask { get; }

    /// <summary>Gets the send the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public OutgoingEmailId? OutgoingEmail { get; }

    /// <summary>Gets the change the notification reports, and <see langword="null" /> for every other shape.</summary>
    public MailboxMutationRecordId? MailboxChange { get; }

    /// <summary>Gets the draft the notification leads to, and <see langword="null" /> for every other shape.</summary>
    public MailDraftId? MailDraft { get; }

    /// <summary>Gets the target of a notification that has nothing to open.</summary>
    public static NotificationTarget Nothing { get; } =
        new(NotificationTargetKind.Nothing, message: null, screen: null, calendarEvent: null, personalTask: null);

    /// <summary>Creates a target that leads to one stored message.</summary>
    /// <param name="message">The message the notification is about.</param>
    /// <returns>A message target.</returns>
    public static NotificationTarget ToMessage(StoredEmailId message) =>
        new(NotificationTargetKind.Message, message, screen: null, calendarEvent: null, personalTask: null);

    /// <summary>Creates a target that leads to a screen rather than to a record.</summary>
    /// <param name="screen">The screen the notification leads to.</param>
    /// <returns>A screen target.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="screen" /> is not a declared screen.</exception>
    public static NotificationTarget ToScreen(NotificationScreen screen)
    {
        if (!Enum.IsDefined(screen))
        {
            throw new ArgumentOutOfRangeException(
                nameof(screen),
                screen,
                "A notification leads to a declared screen or to nothing at all.");
        }

        return new NotificationTarget(
            NotificationTargetKind.Screen,
            message: null,
            screen,
            calendarEvent: null,
            personalTask: null);
    }

    /// <summary>Creates a target that leads to one event of the person's own calendar.</summary>
    /// <param name="calendarEvent">The event the notification is about.</param>
    /// <returns>A calendar event target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="calendarEvent" /> names no event.</exception>
    /// <remarks>
    /// One event rather than the calendar screen, because what a reminder is about is a single commitment rather than
    /// a day: a reader following it is taken to the thing they were reminded of, and a client with no screen for it
    /// yet leaves them where they were exactly as it does for a screen it does not answer for.
    /// </remarks>
    public static NotificationTarget ToCalendarEvent(CalendarEventId calendarEvent)
    {
        if (!calendarEvent.IsSpecified)
        {
            throw new ArgumentException(
                "A notification leading to a calendar event names the event it leads to.",
                nameof(calendarEvent));
        }

        return new NotificationTarget(
            NotificationTargetKind.CalendarEvent,
            message: null,
            screen: null,
            calendarEvent,
            personalTask: null);
    }

    /// <summary>Creates a target that leads to one task of the person's own list.</summary>
    /// <param name="personalTask">The task the notification is about.</param>
    /// <returns>A task target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="personalTask" /> names no task.</exception>
    /// <remarks>
    /// One task rather than the task screen, for the reason the calendar event is one event: what a reminder is
    /// about is a single thing somebody owes rather than a list, and a reader following it is taken to the thing
    /// they were reminded of.
    /// </remarks>
    public static NotificationTarget ToPersonalTask(PersonalTaskId personalTask)
    {
        if (!personalTask.IsSpecified)
        {
            throw new ArgumentException(
                "A notification leading to a task names the task it leads to.",
                nameof(personalTask));
        }

        return new NotificationTarget(
            NotificationTargetKind.PersonalTask,
            message: null,
            screen: null,
            calendarEvent: null,
            personalTask);
    }

    /// <summary>Creates a target that leads to one send in its account's outbox.</summary>
    /// <param name="outgoingEmail">The send the notification is about.</param>
    /// <returns>An outgoing email target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="outgoingEmail" /> names no send.</exception>
    /// <remarks>
    /// The send rather than the outbox, because a refusal is about one message somebody has to decide about, and its
    /// entry in the outbox is where that decision is offered.
    /// </remarks>
    public static NotificationTarget ToOutgoingEmail(OutgoingEmailId outgoingEmail)
    {
        if (outgoingEmail.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A notification leading to a send names the send it leads to.",
                nameof(outgoingEmail));
        }

        return new NotificationTarget(
            NotificationTargetKind.OutgoingEmail,
            message: null,
            screen: null,
            calendarEvent: null,
            personalTask: null,
            outgoingEmail: outgoingEmail);
    }

    /// <summary>Creates a target that leads to one stored message and names the change asked of it.</summary>
    /// <param name="mailboxChange">The change that never reached the mail server.</param>
    /// <param name="message">The message the change was asked of.</param>
    /// <returns>A mailbox change target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mailboxChange" /> names no change.</exception>
    /// <remarks>
    /// Both, because they answer two questions: the message is where opening the row leads, and the change is what
    /// asking for it again acts on. Carrying the message is also what erases the row with it, as every message target
    /// is erased.
    /// </remarks>
    public static NotificationTarget ToMailboxChange(MailboxMutationRecordId mailboxChange, StoredEmailId message)
    {
        if (mailboxChange.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A notification about a change names the change it is about.",
                nameof(mailboxChange));
        }

        return new NotificationTarget(
            NotificationTargetKind.MailboxChange,
            message,
            screen: null,
            calendarEvent: null,
            personalTask: null,
            mailboxChange: mailboxChange);
    }

    /// <summary>Creates a target that leads to one of the person's drafts.</summary>
    /// <param name="mailDraft">The draft the notification is about.</param>
    /// <returns>A draft target.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mailDraft" /> names no draft.</exception>
    public static NotificationTarget ToMailDraft(MailDraftId mailDraft)
    {
        if (mailDraft.Value == Guid.Empty)
        {
            throw new ArgumentException(
                "A notification leading to a draft names the draft it leads to.",
                nameof(mailDraft));
        }

        return new NotificationTarget(
            NotificationTargetKind.MailDraft,
            message: null,
            screen: null,
            calendarEvent: null,
            personalTask: null,
            mailDraft: mailDraft);
    }
}
