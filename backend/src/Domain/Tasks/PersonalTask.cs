// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;

namespace MailFathom.Domain.Tasks;

/// <summary>States one thing a person owes, entered by them or read out of their mail.</summary>
/// <remarks>
/// <para>
/// It is native to this deployment and stored beside everything else: no external task-management protocol is spoken,
/// chosen, or synchronized against, so what a person sees here is what this database holds. It is per user, on the
/// <c>(user, identifier)</c> axis every account reference already uses, and it is a personal list rather than a shared
/// board — nothing here assigns work to anybody but the person whose list it is.
/// </para>
/// <para>
/// The type is named for the list it belongs to rather than simply <c>Task</c>, because a domain type by that name
/// would collide at every point of use with the one every asynchronous method in this solution returns, and the
/// conventions refuse a name a reader has to recover from a namespace.
/// </para>
/// <para>
/// A task derived from mail is derived personal data with the same classification as the message behind it: the title
/// may be a sentence read out of a body. What it does not hold is a second copy of the mailbox — no body, no
/// attachment, no address — so the message is cited rather than repeated, and a task outlives that citation because
/// what a person owes does not stop being owed when the mail naming it is erased.
/// </para>
/// </remarks>
public sealed record PersonalTask
{
    /// <summary>The longest title stored, which is a line on a list rather than a note.</summary>
    public const int MaximumTitleLength = 200;

    /// <summary>The hour on the due day a task's reminders are measured back from, in the offset the task states.</summary>
    /// <remarks>
    /// The design settles the hour, and it is the same nine o'clock an all-day calendar event is announced
    /// from: a person who wrote down a day never chose midnight, and a reminder an hour before one would arrive in
    /// the night before anybody is awake to be told about it.
    /// </remarks>
    public const int DueDayReminderHour = 9;

    /// <summary>The furthest behind UTC a due day may run, which no zone is further behind than.</summary>
    private static readonly TimeSpan EarliestDueDayOffset = TimeSpan.FromHours(-12);

    /// <summary>The furthest ahead of UTC a due day may run, which no zone is further ahead of.</summary>
    /// <remarks>
    /// The two are not a symmetric band, because the offsets the world actually runs are not one: a zone reaches
    /// fourteen hours ahead and only twelve behind. Stating them as one magnitude would admit thirteen and fourteen
    /// hours behind UTC, which no zone runs and no client resolving a reader's own record could have produced.
    /// </remarks>
    private static readonly TimeSpan LatestDueDayOffset = TimeSpan.FromHours(14);

    private PersonalTask(
        PersonalTaskId id,
        UserId user,
        string title,
        DateOnly? dueOn,
        TimeSpan? dueDayOffset,
        IReadOnlyList<Reminder> reminders,
        PersonalTaskOrigin origin,
        StoredEmailId? sourceMessage,
        bool isCompleted,
        TaskCommitment commitment,
        TaskAnswer? answer)
    {
        this.Id = id;
        this.User = user;
        this.Title = title;
        this.DueOn = dueOn;
        this.DueDayOffset = dueDayOffset;
        this.Reminders = reminders;
        this.Origin = origin;
        this.SourceMessage = sourceMessage;
        this.IsCompleted = isCompleted;
        this.Direction = commitment.Direction;
        this.EstimatedMinutes = commitment.EstimatedMinutes;
        this.Plan = commitment.Plan;
        this.Anchor = commitment.Anchor;
        this.Counterpart = commitment.Counterpart;
        this.Answer = answer;
    }

    /// <summary>Gets what addresses this task.</summary>
    public PersonalTaskId Id { get; }

    /// <summary>Gets the person whose list it is on.</summary>
    public UserId User { get; }

    /// <summary>Gets the line the list is drawn with.</summary>
    public string Title { get; }

    /// <summary>Gets the day it is due on, and <see langword="null" /> where nobody has said when.</summary>
    /// <remarks>
    /// A day rather than an instant, because what a person owes is owed on a date and never at a minute; a reminder
    /// against that date is what carries a time, and it is a separate record.
    /// </remarks>
    public DateOnly? DueOn { get; }

    /// <summary>Gets the UTC offset the person's due day runs in, and <see langword="null" /> where the task announces nothing.</summary>
    /// <remarks>
    /// A day names no instant on its own, so nine in the morning on it is nine o'clock somewhere — and this
    /// deployment keeps no timezone for a person, which is why arranging a day has the client state the window it
    /// runs in rather than deriving one. A task that announces something states the same fact once, and everything
    /// measured back from its due date is measured in it.
    /// </remarks>
    public TimeSpan? DueDayOffset { get; }

    /// <summary>Gets the leads this task is announced at, longest first, which is empty where nothing announces it.</summary>
    /// <remarks>
    /// An empty set is a statement rather than an omission: nothing will be raised about this task. A task nobody
    /// dated carries none at all, because what a lead is measured back from is the due day and there is none.
    /// </remarks>
    public IReadOnlyList<Reminder> Reminders { get; }

    /// <summary>Gets the instant every reminder on this task is measured back from, and <see langword="null" /> where nothing announces it.</summary>
    /// <remarks>
    /// <see cref="DueDayReminderHour" /> o'clock on the due day, read in the offset the task states. Derived rather
    /// than stored, which is what makes a due date somebody moved announced at its new day without anything
    /// rewriting the leads they chose.
    /// </remarks>
    public DateTimeOffset? AnchorsRemindersAt => this.DueOn is { } day && this.DueDayOffset is { } offset
        ? new DateTimeOffset(day.ToDateTime(new TimeOnly(DueDayReminderHour, 0)), offset)
        : null;

    /// <summary>Gets where the task came from, and so whether the person has committed to it.</summary>
    public PersonalTaskOrigin Origin { get; }

    /// <summary>Gets the message the task was read out of, and <see langword="null" /> where it names none.</summary>
    /// <remarks>
    /// Optional whichever origin the task carries: mail is the usual source of a proposal and never the only one, and
    /// a person entering a task by hand may still cite the thread they promised it in.
    /// </remarks>
    public StoredEmailId? SourceMessage { get; }

    /// <summary>Gets whether the person has done it.</summary>
    public bool IsCompleted { get; }

    /// <summary>Gets whether the person owes the task or waits for it.</summary>
    public PersonalTaskDirection Direction { get; }

    /// <summary>Gets how long the task is expected to take, in minutes, and <see langword="null" /> where nobody has said.</summary>
    public int? EstimatedMinutes { get; }

    /// <summary>Gets the time set aside for the task, and <see langword="null" /> where none is.</summary>
    /// <remarks>Independent of <see cref="DueOn" />: the day it is owed and the moment it is intended to be done are two statements.</remarks>
    public TaskPlan? Plan { get; }

    /// <summary>Gets the conversation or event the task is about, and <see langword="null" /> where it is about neither.</summary>
    public TaskAnchor? Anchor { get; }

    /// <summary>Gets who the task is owed to or who owes it, and <see langword="null" /> where that is nobody in particular.</summary>
    public TaskCounterpart? Counterpart { get; }

    /// <summary>Gets the reply that answered a task its person was waiting on, and <see langword="null" /> until one did.</summary>
    public TaskAnswer? Answer { get; }

    /// <summary>Composes a task nobody has completed yet.</summary>
    /// <param name="id">What addresses the task.</param>
    /// <param name="user">The person whose list it is on.</param>
    /// <param name="title">The line the list is drawn with.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where nobody has said when.</param>
    /// <param name="announcement">What announces it and the offset its due day runs in, or <see cref="TaskAnnouncement.Silent" /> to announce nothing.</param>
    /// <param name="origin">Where the task came from.</param>
    /// <param name="sourceMessage">The message it was read out of, or <see langword="null" /> where it names none.</param>
    /// <param name="commitment">What the task is as a commitment, or <see cref="TaskCommitment.Owed" /> where nothing more is said.</param>
    /// <returns>A task that stands outstanding.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announcement" /> or <paramref name="commitment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when <paramref name="user" /> names nobody, when <paramref name="id" /> is the struct default, when one lead is stated twice, or when leads are stated against no due day.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin, when <paramref name="commitment" /> states an undeclared direction or an estimate outside five minutes to twelve hours, when <paramref name="title" /> exceeds <see cref="MaximumTitleLength" />, when more than <see cref="Reminder.MaximumCount" /> leads are stated, or when the stated offset is not one a UTC offset can be.</exception>
    /// <remarks>
    /// The user has to be a named one, because a row written under the unspecified identity would belong to nobody:
    /// unreachable by any read and uncollected by any erasure.
    /// </remarks>
    public static PersonalTask Compose(
        PersonalTaskId id,
        UserId user,
        string title,
        DateOnly? dueOn,
        TaskAnnouncement announcement,
        PersonalTaskOrigin origin,
        StoredEmailId? sourceMessage,
        TaskCommitment commitment)
    {
        Validate(id, user, origin, commitment);

        var announced = Announced(dueOn, announcement);

        return new PersonalTask(
            id,
            user,
            Bounded(title, nameof(title)),
            dueOn,
            announced.DueDayOffset,
            announced.Reminders,
            origin,
            sourceMessage,
            isCompleted: false,
            commitment,
            answer: null);
    }

    /// <summary>Restores a task this deployment already kept, with the origin and completion it was stored under.</summary>
    /// <param name="id">What addresses the task.</param>
    /// <param name="user">The person whose list it is on.</param>
    /// <param name="title">The line the list is drawn with.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where nobody has said when.</param>
    /// <param name="announcement">What announces it and the offset its due day runs in, as the row states them.</param>
    /// <param name="origin">Where the task came from.</param>
    /// <param name="sourceMessage">The message it was read out of, or <see langword="null" /> where it names none.</param>
    /// <param name="isCompleted">Whether the person has done it.</param>
    /// <param name="commitment">What the task is as a commitment, as the row states it.</param>
    /// <param name="answer">The reply that answered it, or <see langword="null" /> where none did.</param>
    /// <returns>The task as it stands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announcement" /> or <paramref name="commitment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when <paramref name="user" /> names nobody, when <paramref name="id" /> is the struct default, when one lead is stated twice, or when leads are stated against no due day.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="origin" /> is not a declared origin, when <paramref name="commitment" /> states an undeclared direction or an estimate outside five minutes to twelve hours, when <paramref name="title" /> exceeds <see cref="MaximumTitleLength" />, when more than <see cref="Reminder.MaximumCount" /> leads are stated, or when the stated offset is not one a UTC offset can be.</exception>
    /// <remarks>
    /// It validates exactly what <see cref="Compose" /> validates rather than trusting the store, because a row read
    /// back is input from outside this process however it got there. The two things it takes that composing does not
    /// are the store's to say and never a producer's: whether the task has been completed, and whether a proposal has
    /// since been accepted.
    /// </remarks>
    public static PersonalTask Restore(
        PersonalTaskId id,
        UserId user,
        string title,
        DateOnly? dueOn,
        TaskAnnouncement announcement,
        PersonalTaskOrigin origin,
        StoredEmailId? sourceMessage,
        bool isCompleted,
        TaskCommitment commitment,
        TaskAnswer? answer)
    {
        Validate(id, user, origin, commitment);

        var announced = Announced(dueOn, announcement);

        return new PersonalTask(
            id,
            user,
            Bounded(title, nameof(title)),
            dueOn,
            announced.DueDayOffset,
            announced.Reminders,
            origin,
            sourceMessage,
            isCompleted,
            commitment,
            answer);
    }

    /// <summary>States the task as the person has just edited it.</summary>
    /// <param name="title">The line the list is to be drawn with from now on.</param>
    /// <param name="dueOn">The day it is due on, or <see langword="null" /> where the person took the date off it.</param>
    /// <param name="announcement">What announces it afterwards and the offset its due day runs in, or <see cref="TaskAnnouncement.Silent" /> to announce nothing.</param>
    /// <param name="commitment">What the task is as a commitment from now on.</param>
    /// <returns>The task as it now stands.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="announcement" /> or <paramref name="commitment" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="title" /> is blank, when one lead is stated twice, or when leads are stated against no due day.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="commitment" /> states an undeclared direction or an estimate outside five minutes to twelve hours, when <paramref name="title" /> exceeds <see cref="MaximumTitleLength" />, when more than <see cref="Reminder.MaximumCount" /> leads are stated, or when the stated offset is not one a UTC offset can be.</exception>
    /// <remarks>
    /// Only the values an edit is about, because the rest of the record is not the editor's to state: the identity
    /// and the person are what address the task, the origin moves by accepting a proposal rather than by typing, the
    /// completion moves by doing the thing, the answer is the reply that arrived rather than anything typed, and the
    /// citation records where the task came from rather than what it is now about. An edit that could rewrite any of those would let a person turn a proposal into a commitment, or
    /// point one at a message it was never read out of, through the route that renames it.
    /// </remarks>
    public PersonalTask Revise(string title, DateOnly? dueOn, TaskAnnouncement announcement, TaskCommitment commitment)
    {
        var announced = Announced(dueOn, announcement);

        ValidateCommitment(commitment);

        return new PersonalTask(
            this.Id,
            this.User,
            Bounded(title, nameof(title)),
            dueOn,
            announced.DueDayOffset,
            announced.Reminders,
            this.Origin,
            this.SourceMessage,
            this.IsCompleted,
            commitment,
            this.Answer);
    }

    /// <summary>Gets what the task is as a commitment, in the form <see cref="Revise" /> takes.</summary>
    public TaskCommitment Commitment => new(this.Direction, this.EstimatedMinutes, this.Plan, this.Anchor, this.Counterpart);

    /// <summary>Completes a task its person was waiting on, citing the reply that answered it.</summary>
    /// <param name="answer">The reply that arrived.</param>
    /// <returns>The task as it now stands: completed, and citing <paramref name="answer" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answer" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the task is not one the person waits for, or is already completed.</exception>
    /// <remarks>
    /// Only an awaited task is answered: what a person owes is done by them, and no reply to it settles it.
    /// </remarks>
    public PersonalTask Answered(TaskAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (this.Direction != PersonalTaskDirection.Awaited)
        {
            throw new InvalidOperationException("Only a task the person waits for is answered by a reply.");
        }

        if (this.IsCompleted)
        {
            throw new InvalidOperationException("A completed task is not answered again.");
        }

        return new PersonalTask(
            this.Id,
            this.User,
            this.Title,
            this.DueOn,
            this.DueDayOffset,
            this.Reminders,
            this.Origin,
            this.SourceMessage,
            isCompleted: true,
            this.Commitment,
            answer);
    }

    /// <summary>Reports the instant one of this task's reminders falls at.</summary>
    /// <param name="reminder">The lead to measure back from this task's anchor.</param>
    /// <returns>The instant the reminder comes due.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this task announces nothing, and so anchors nothing.</exception>
    /// <remarks>
    /// Derived rather than stored on the reminder, which is what makes a due date somebody moved reminded on its new
    /// day without anything rewriting what they chose.
    /// </remarks>
    public DateTimeOffset RemindsAt(Reminder reminder) => this.AnchorsRemindersAt is { } anchor
        ? anchor - reminder.Lead
        : throw new InvalidOperationException("A task that announces nothing anchors no reminder.");

    /// <summary>Reads what a writer stated about announcing the task, refusing what no task may hold.</summary>
    /// <remarks>
    /// A lead is measured back from the due day, so leads against no due day are refused rather than kept as
    /// reminders nothing could ever raise — which is the same answer the client gives, where a task nobody has dated
    /// offers no presets at all. The offset is kept only where leads are, for the same reason: it exists to say what
    /// nine in the morning means, and there is nothing to say it about otherwise. The offset is held to what a UTC
    /// offset can be here as well as at the transport boundary that refuses one, so that the anchor a lead is
    /// measured back from is a value rather than a property that throws.
    /// </remarks>
    private static (TimeSpan? DueDayOffset, IReadOnlyList<Reminder> Reminders) Announced(
        DateOnly? dueOn,
        TaskAnnouncement announcement)
    {
        ArgumentNullException.ThrowIfNull(announcement);

        var ordered = Reminder.Ordered(announcement.Reminders);

        if (ordered.Count == 0)
        {
            return (null, ordered);
        }

        if (dueOn is null)
        {
            throw new ArgumentException(
                "A task is announced against its due day, so one nobody has dated carries no reminder.",
                nameof(announcement));
        }

        if (announcement.DueDayOffset.Ticks % TimeSpan.TicksPerMinute != 0
            || announcement.DueDayOffset < EarliestDueDayOffset
            || announcement.DueDayOffset > LatestDueDayOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(announcement),
                announcement.DueDayOffset,
                "A due day runs in a whole number of minutes from UTC, between twelve hours behind and fourteen ahead.");
        }

        return (announcement.DueDayOffset, ordered);
    }

    /// <summary>Refuses the identities and the origin that no task can be built from, whether composed or restored.</summary>
    private static void Validate(PersonalTaskId id, UserId user, PersonalTaskOrigin origin, TaskCommitment commitment)
    {
        if (!id.IsSpecified)
        {
            throw new ArgumentException("A task is addressed by a specified identifier.", nameof(id));
        }

        if (!user.IsSpecified)
        {
            throw new ArgumentException(
                "A task is owed by a named user, so it is never held under the unspecified one.",
                nameof(user));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "A task carries a declared origin.");
        }

        ValidateCommitment(commitment);
    }

    /// <summary>Refuses a direction nobody declared and an estimate outside what a plan may set aside.</summary>
    private static void ValidateCommitment(TaskCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);

        if (!Enum.IsDefined(commitment.Direction))
        {
            throw new ArgumentOutOfRangeException(nameof(commitment), commitment.Direction, "A task carries a declared direction.");
        }

        if (commitment.EstimatedMinutes is { } minutes
            && (minutes < TaskPlan.ShortestMinutes || minutes > TaskPlan.LongestMinutes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(commitment),
                minutes,
                "An estimate is between five minutes and twelve hours.");
        }
    }

    private static string Bounded(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var trimmed = value.Trim();

        ArgumentOutOfRangeException.ThrowIfGreaterThan(trimmed.Length, MaximumTitleLength, parameterName);

        return trimmed;
    }
}
