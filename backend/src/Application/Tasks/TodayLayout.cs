// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Calendar;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Arranges the signed-in person's day when they ask for it, and changes nothing by doing so.</summary>
/// <remarks>
/// <para>
/// <b>Once per request and never otherwise.</b> Nothing schedules this, no pass reaches it, and opening a screen does
/// not run it: it is the one act behind a control somebody presses, so what a deployment spends on arranging days is
/// what its people asked for.
/// </para>
/// <para>
/// <b>It applies nothing.</b> What comes back is read by the screen that asked and is forgotten unless the person acts
/// on it. No task is rescheduled, no proposal is accepted, and no event is written — every one of those is an act of
/// their own through the route that owns it, and this route holds no write at all.
/// </para>
/// <para>
/// <b>Both halves are read through the readings that own them</b> rather than through queries of this use case's own:
/// the tasks through <see cref="OwnTasks" /> and the day's commitments through <see cref="OwnCalendar" />. Each of
/// those resolves the person from the principal, narrows to what they hold, and decides what a window may ask for, so
/// an arrangement is decided from exactly what the person's own screens would have drawn — and a day cannot be laid
/// out from somebody else's calendar, because there is no way here to name one.
/// </para>
/// <para>
/// <b>Whose day it is says when the day begins.</b> This deployment keeps no timezone for a person, so the window is
/// stated by the client exactly as it states the window it draws the calendar with, and the day a task is compared
/// against is the one the opening instant falls on in the offset it carries.
/// </para>
/// </remarks>
public sealed class TodayLayout
{
    /// <summary>The greatest number of tasks one arrangement is decided from.</summary>
    /// <remarks>
    /// More than any day holds, which is what makes it a bound rather than a rule: a person with a long backlog is
    /// laid out over what is due soonest, and the rest is a list to work down rather than a day to arrange. It also
    /// bounds what one press sends to a provider, which is the other reason a number is needed at all.
    /// </remarks>
    public const int MaximumTasks = 20;

    /// <summary>The greatest number of commitments one arrangement is decided against.</summary>
    /// <remarks>Well past a full day of meetings, and low enough that a window somebody widened cannot compose an unbounded turn.</remarks>
    public const int MaximumCommitments = 50;

    /// <summary>The longest window one request may call a day.</summary>
    /// <remarks>
    /// Two days rather than one, because a day is stated as two instants by a client in a timezone this deployment
    /// does not know, and every offset, every transition, and every rounding a client applies stands well inside it.
    /// It is a bound on what may be asked rather than a definition of a day: what the arrangement treats as the day is
    /// the window as stated.
    /// </remarks>
    public static readonly TimeSpan MaximumDaySpan = TimeSpan.FromDays(2);

    private readonly AccessAuthorization authorization;
    private readonly OwnTasks tasks;
    private readonly OwnCalendar calendar;
    private readonly IDayLayoutPlanner planner;
    private readonly SensitiveContentEgressGuard egressGuard;

    /// <summary>Initializes the use case.</summary>
    /// <param name="authorization">Reports the grant the caller holds and the person it acts for.</param>
    /// <param name="tasks">Reads what the person owes, under the reading that owns their list.</param>
    /// <param name="calendar">Reads what the person is already committed to, under the reading that owns their calendar.</param>
    /// <param name="planner">Arranges the day, in whichever state the deployment left it.</param>
    /// <param name="egressGuard">Holds the posture the lines are scanned under while the arrangement is asked for.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public TodayLayout(
        AccessAuthorization authorization,
        OwnTasks tasks,
        OwnCalendar calendar,
        IDayLayoutPlanner planner,
        SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.authorization = authorization;
        this.tasks = tasks;
        this.calendar = calendar;
        this.planner = planner;
        this.egressGuard = egressGuard;
    }

    /// <summary>Suggests how the signed-in person's day could be arranged.</summary>
    /// <param name="dayStart">The instant their day opens.</param>
    /// <param name="dayEnd">The instant it closes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The arrangement or the reason there is none, or <see langword="null" /> where what was asked for is not a day this deployment arranges.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no person, or its grant omits <see cref="MailFathomPermission.AssistantUse" />, <see cref="MailFathomPermission.TasksRead" />, or <see cref="MailFathomPermission.CalendarRead" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The deployment's state is asked first, so an instance that lays out no day reads nobody's tasks and nobody's
    /// calendar to say so. What is laid out is what the person owes and has not done by that day and has set no time
    /// aside for in it — a task they have already completed is not work the day has to hold, a task they wait for is
    /// somebody else's move, and one mail proposed and nobody accepted is not work they owe at all, which is why only
    /// the committed half is read. A block already set aside is fixed in the day like an event, so it is passed as a
    /// commitment.
    /// </remarks>
    public async Task<DayLayoutDerivation?> SuggestAsync(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        CancellationToken cancellationToken)
    {
        // All three before anything is read: the titles of what the person owes and of what they are committed to are
        // what leaves for the chat provider, so the act needs the grant to send and the grant over each record it sends.
        this.authorization.RequirePermission(MailFathomPermission.AssistantUse);
        this.authorization.RequirePermission(MailFathomPermission.TasksRead);
        this.authorization.RequirePermission(MailFathomPermission.CalendarRead);

        var user = this.authorization.RequireUser();

        if (dayEnd <= dayStart || dayEnd - dayStart > MaximumDaySpan)
        {
            return null;
        }

        if (!this.planner.IsActive)
        {
            return DayLayoutDerivation.Withholding(DayLayoutWithholding.NotActivated);
        }

        var open = await this.ReadOpenAsync(new PersonalTaskQuery(PersonalTaskOrigin.Asserted), cancellationToken);
        var inWindow = await this.ReadOpenAsync(
            new PersonalTaskQuery(PersonalTaskOrigin.Asserted, From: dayStart, Until: dayEnd),
            cancellationToken);
        var committed = await this.calendar.ReadWindowAsync(
            dayStart,
            dayEnd,
            CalendarEventOrigin.Asserted,
            MaximumCommitments,
            cancellationToken);

        if (committed is null)
        {
            return null;
        }

        // Established around the one call that leaves this deployment, and against the person rather than an account,
        // because a task and an event are held per person and a line on either may have been read out of any mailbox
        // they are assigned.
        using var actingFor = this.egressGuard.ActingFor(
            await this.egressGuard.ReadPostureAcrossAccountsOfAsync(user, cancellationToken));

        // A block the person already set aside for a task is as fixed as an event, so it joins the commitments. A plan
        // starting before dayStart is not one: it is a block of another day, whatever hour it runs into.
        var blocks = inWindow
            .Where(task => IsPlannedWithin(task, dayStart, dayEnd))
            .Select(static task => new DayLayoutCommitment(task.Title, task.Plan!.StartAt, task.Plan.StartAt.AddMinutes(task.Plan.Minutes)));

        return await this.planner.SuggestAsync(
            new DayLayoutQuestion(
                dayStart,
                dayEnd,
                Unplanned(open, dayStart, dayEnd),
                [
                    .. committed
                        .Select(static held => new DayLayoutCommitment(held.Title.Value, held.Start, held.End))
                        .Concat(blocks)
                        .OrderBy(static held => held.Start)
                        .Take(MaximumCommitments),
                ]),
            cancellationToken);
    }

    /// <summary>Reads the open tasks a query narrows to, soonest due first.</summary>
    /// <remarks>
    /// One page each, and two reads of it: the unwindowed list stands what is due by the day at the front of it, and a
    /// page of the size this reads is far past what a day could hold; the window read is the store's own answer to
    /// which tasks are due in the day or planned into it, so a block is found however late or undated the task it
    /// belongs to sorts in the first list.
    /// </remarks>
    private async Task<IReadOnlyList<PersonalTask>> ReadOpenAsync(PersonalTaskQuery query, CancellationToken cancellationToken)
    {
        var page = await this.tasks.ReadPageAsync(query, OwnTasks.MaximumPageSize, cursor: null, cancellationToken);

        return page is null ? [] : [.. page.Tasks.Where(static task => !task.IsCompleted)];
    }

    /// <summary>Picks what the day is still to place: what the person owes by that day and has set no time aside for in it.</summary>
    /// <remarks>
    /// A task with no day on it is not part of any particular day and is left where it is, so an arrangement never
    /// quietly decides that something nobody dated is due now. A task the person waits for is somebody else's move and
    /// is never offered, and one already planned inside the window is a commitment rather than work to place twice.
    /// </remarks>
    private static IReadOnlyList<DayLayoutTask> Unplanned(IReadOnlyList<PersonalTask> open, DateTimeOffset dayStart, DateTimeOffset dayEnd)
    {
        var day = DateOnly.FromDateTime(dayStart.DateTime);

        return
        [
            .. open
                .Where(task => task.Direction == PersonalTaskDirection.Owed
                    && task.DueOn is { } due
                    && due <= day
                    && !IsPlannedWithin(task, dayStart, dayEnd))
                .Take(MaximumTasks)
                .Select(static task => new DayLayoutTask(task.Id, task.Title, task.DueOn, task.EstimatedMinutes)),
        ];
    }

    private static bool IsPlannedWithin(PersonalTask task, DateTimeOffset dayStart, DateTimeOffset dayEnd) =>
        task.Plan is { } plan && plan.StartAt >= dayStart && plan.StartAt < dayEnd;
}
