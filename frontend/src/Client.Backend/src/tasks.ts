// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { isReminderInstantList, isReminderSet } from './reminders';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// The signed-in person's own task list, which this surface serves as two halves rather than as one listing with a
// filter: what somebody has committed to and what mail suggested and nobody has agreed to yet are different things to
// whoever is reading them, and the service publishes a route for each. A cursor is refused in the half it was not
// issued for, so the two walks cannot be spliced.
//
// Nothing here names a user. Which list is reached is resolved from the session, so a task somebody else holds answers
// exactly as one nobody holds.
//
// **Dismissing a proposal is the erasure rather than a route of its own.** Declining what mail suggested and deleting
// something a person owes leave the same list behind, so the surface publishes one act and a screen puts its own word
// on the control.
//
// **Arranging a day is the one thing here that leaves the deployment.** It is reached under the asking grant rather
// than the reading one, it is a capability a screen asks about before offering it at all, and a deployment that has
// spent what it allows a provider answers it with a value rather than a failure — the shape `mailReplyDrafting.ts`
// already stands in, and for its reason: somebody pressed a control and has to be told what became of it.

/** The route the acting person's committed tasks are listed and written to, relative to the client prefix. */
export const tasksRoute = '/tasks';

/** The route the tasks mail proposed and nobody has accepted are listed at, relative to the client prefix. */
export const proposedTasksRoute = '/tasks/proposed';

/** The route the acting person's day is arranged at, relative to the client prefix. */
export const todayLayoutRoute = '/tasks/today/layout';

/** The route one task is revised and erased at, relative to the client prefix. */
export function taskRoute(taskId: string): string {
    return `${tasksRoute}/${encodeURIComponent(taskId)}`;
}

/** The route one task's completion state is stated on, relative to the client prefix. */
export function taskCompletionRoute(taskId: string): string {
    return `${taskRoute(taskId)}/completion`;
}

/** The route a proposed task is taken on at, relative to the client prefix. */
export function taskAcceptanceRoute(taskId: string): string {
    return `${taskRoute(taskId)}/acceptance`;
}

/** Whether the person committed to a task themselves or mail proposed it to them. */
export type PersonalTaskOrigin = 'Asserted' | 'Proposed';

/** Whether the person owes it to somebody or waits on somebody for it. */
export type TaskDirection = 'Owed' | 'Awaited';

/** The stretch of a day a task is planned into, which is a commitment rather than a suggestion. */
export interface TaskPlan {
    /** When it begins, as the instant the deployment stated. */
    readonly startAt: string;
    readonly minutes: number;
}

/** What a task is about, named by the label a person knows it by. */
export type TaskAnchor =
    | { readonly kind: 'Thread'; readonly id: string; readonly label: string }
    | { readonly kind: 'Event'; readonly id: string; readonly label: string };

/** The person on the other side of a task. */
export interface TaskCounterpart {
    readonly address: string;
    readonly displayName: string | null;
}

/** The message that settled an awaited task. */
export interface TaskAnswer {
    readonly messageId: string;
    readonly arrivedAt: string;
}

/** What narrows a half of the list; a window is stated by both of its ends or by neither. */
export interface TaskFilter {
    readonly threadId?: string;
    readonly eventId?: string;
    readonly counterpart?: string;
    readonly from?: string;
    readonly until?: string;
}

/** One thing a person owes. */
export interface PersonalTask {
    readonly id: string;
    readonly title: string;

    /** The day it is due on as `yyyy-mm-dd`, or `null` where nobody has said when. */
    readonly dueOn: string | null;

    /** What announces it, in minutes before nine in the morning on the due day, longest lead first. */
    readonly reminders: readonly number[];

    /** The instants those leads fall at, in the same order, which the deployment states and this client draws. */
    readonly remindsAt: readonly string[];

    readonly origin: PersonalTaskOrigin;
    readonly completed: boolean;

    /** The message it was read out of or written beside, or `null` where it cites none. */
    readonly sourceMessageId: string | null;

    readonly direction: TaskDirection;

    /** How long the person thinks it takes, in whole minutes, or `null` where nobody said. */
    readonly estimatedMinutes: number | null;

    /** When it is planned into the day, or `null` where it is not. */
    readonly plan: TaskPlan | null;

    /** The thread or event it is about, or `null` where it is about neither. */
    readonly anchor: TaskAnchor | null;

    /** The person on the other side of it, or `null` where there is none. */
    readonly counterpart: TaskCounterpart | null;

    /** The message that answered an awaited task, or `null` while nothing has. */
    readonly answer: TaskAnswer | null;
}

/** One bounded page of one half of the list, and where the walk continues. */
export interface PersonalTaskPage {
    readonly tasks: readonly PersonalTask[];

    /**
     * The cursor the following page is asked with, or `null` at the end of the half.
     *
     * The absent cursor is the end of the walk rather than a page that happened to be short, so a caller stops when
     * this stops instead of comparing the count against the size it asked for.
     */
    readonly nextCursor: string | null;
}

/**
 * The line, the day, and what announces it that a person states for one task of their own.
 *
 * A revision states the same document as a creation, which is what makes turning the last reminder off a task stated
 * with none rather than a field left out. `reviseTask` therefore takes this record whole and never the part that
 * changed.
 */
export interface PersonalTaskRecord {
    readonly title: string;

    /** The day it is due on as `yyyy-mm-dd`, or `null` to say nothing about when. */
    readonly dueOn: string | null;

    /** What is to announce it, in minutes before nine in the morning on the due day, which `isReminderSet` is the bar for. */
    readonly reminders: readonly number[];

    /**
     * The whole-minute UTC offset the due day runs in, which `dueDayOffsetMinutes` reads and a record stating no lead
     * may leave `null`.
     *
     * It travels with the leads because the deployment keeps no timezone for anybody: a day names no instant until a
     * client says which offset it is read in.
     */
    readonly dueDayOffsetMinutes: number | null;

    /** The message the task cites, or `null` where it cites none. */
    readonly sourceMessageId: string | null;

    /** Whether the person owes it or waits on it, which a revision restates rather than leaves to be inferred. */
    readonly direction: TaskDirection;

    /** How long it takes in whole minutes, `shortestTaskMinutes` to `longestTaskMinutes`, or `null` to say nothing. */
    readonly estimatedMinutes: number | null;

    /** Where it is planned into the day, or `null` for nowhere. */
    readonly plan: TaskPlan | null;

    /** What it is about, or `null` for neither a thread nor an event. */
    readonly anchor: TaskAnchor | null;

    /** Who is on the other side of it, or `null` for nobody. */
    readonly counterpart: TaskCounterpart | null;
}

/** Where one task is suggested to sit in the day. */
export interface DayLayoutPlacement {
    readonly taskId: string;

    /** When it is suggested to begin, as the instant the deployment stated. */
    readonly startAt: string;

    /** How long it is suggested to take. */
    readonly minutes: number;
}

/** How an arrangement of a day ended. */
export type DayLayoutOutcome = 'Arranged' | 'NotArranged' | 'AllowanceSpent';

/**
 * An arrangement of one day, offered and applied to nothing.
 *
 * `AllowanceSpent` is a value rather than a failure for the reason drafting a reply states one: it is the deployment
 * having spent what its operator allows a provider, which is neither a defect in this client nor something a reader
 * repairs by signing in again or retrying at once — and the person pressed a control, so a screen has to say what
 * became of it rather than draw nothing.
 */
export interface DayLayout {
    readonly outcome: DayLayoutOutcome;

    /** What to do and when, earliest first, which is empty unless the day was arranged. */
    readonly placements: readonly DayLayoutPlacement[];

    /** The tasks that do not realistically fit the day, by identity. */
    readonly notToday: readonly string[];
}

/** What erasing a task removed, which carries no title and no day by design. */
export interface PersonalTaskErasure {
    readonly id: string;
    readonly erased: boolean;
}

/** The most tasks one page may name, which is the deployment's own bound rather than a preference. */
export const mostTasksPerPage = 200;

/** How many a screen asks for where it states no window of its own, which is the size the service serves by default. */
export const tasksPerPage = 50;

/** The longest line a task may carry, which the deployment refuses rather than truncates. */
export const longestTaskTitle = 200;

// A task is a line, a day, two flags and two identifiers. Generous against that arithmetic over a full page and well
// under the transport's own backstop.
const longestTaskPage = 256 * 1024;

// One task and one erasure answer are each a single short record.
const longestTaskAnswer = 8 * 1024;

// An arrangement names identities, instants and durations and repeats no line of anybody's list, so it is smaller than
// a page of the list it arranges.
const longestLayoutAnswer = 64 * 1024;

/** The shortest a task, an estimate or a plan may be, which the deployment refuses rather than rounds. */
export const shortestTaskMinutes = 5;

/** The longest a task, an estimate or a plan may be. */
export const longestTaskMinutes = 720;

/** The longest label an anchor may carry. */
export const longestAnchorLabel = 200;

const longestCounterpartAddress = 320;
const longestCounterpartName = 200;

const taskOrigins: readonly PersonalTaskOrigin[] = ['Asserted', 'Proposed'];
const taskDirections: readonly TaskDirection[] = ['Owed', 'Awaited'];

/** Reads one page of what the person has committed to, soonest due first. */
export function readOwnTasks(
    session: ClientSession,
    transport: MailFathomTransport,
    page: { readonly pageSize?: number; readonly cursor?: string | null; readonly filter?: TaskFilter } = {},
): Promise<ClientResult<PersonalTaskPage>> {
    return readHalf(session, transport, tasksRoute, page);
}

/** Reads one page of what mail proposed and the person has not accepted, soonest due first. */
export function readProposedTasks(
    session: ClientSession,
    transport: MailFathomTransport,
    page: { readonly pageSize?: number; readonly cursor?: string | null; readonly filter?: TaskFilter } = {},
): Promise<ClientResult<PersonalTaskPage>> {
    return readHalf(session, transport, proposedTasksRoute, page);
}

/**
 * Reads one task by its identity, whichever half holds it, which is how a reminder naming a task not on the page in
 * front of the reader is opened. A `404` is `missing`: the route names one task, so it is one nobody holds any longer.
 */
export function readTask(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`GET ${tasksRoute}/{taskId}`, async () =>
        namedTaskOf(
            await send(transport, {
                method: 'GET',
                path: routeFor(session, taskRoute(taskId)),
                headers: headersFor(session),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/** Records a task the person has just committed to, which is written as one they asserted. */
export function recordTask(
    session: ClientSession,
    transport: MailFathomTransport,
    record: PersonalTaskRecord,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`POST ${tasksRoute}`, async () =>
        taskOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, tasksRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(record),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/**
 * Revises one task to the record the caller states, which is the whole record rather than the part that changed.
 *
 * A `404` here is `missing` for the reason completion's is: the route names one task, so a deployment answering that
 * way is one somebody erased while this screen had it open. It can also mean the anchor the record states, when it
 * moves the task, is one the person cannot open; the two read alike here because both leave nothing written.
 */
export function reviseTask(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
    record: PersonalTaskRecord,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`PUT ${tasksRoute}/{taskId}`, async () =>
        namedTaskOf(
            await send(transport, {
                method: 'PUT',
                path: routeFor(session, taskRoute(taskId)),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(record),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/**
 * States whether one task stands completed.
 *
 * A `404` here is `missing` rather than `unavailable`, which is the reading `failureReasonForStatus` leaves to a route
 * that names one thing.
 */
export function setTaskCompletion(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
    completed: boolean,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`POST ${tasksRoute}/{taskId}/completion`, async () =>
        namedTaskOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, taskCompletionRoute(taskId)),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify({ completed }),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/** Takes on a task mail proposed, so it becomes one the person owes rather than one they were offered. */
export function acceptTask(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
): Promise<ClientResult<PersonalTask>> {
    return spanned(`POST ${tasksRoute}/{taskId}/acceptance`, async () =>
        namedTaskOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, taskAcceptanceRoute(taskId)),
                headers: headersFor(session),
                longestAnswer: longestTaskAnswer,
            }),
        ),
    );
}

/** Erases one task, which is what both deleting something owed and declining something proposed reach. */
export function eraseTask(
    session: ClientSession,
    transport: MailFathomTransport,
    taskId: string,
): Promise<ClientResult<PersonalTaskErasure>> {
    return spanned(`DELETE ${tasksRoute}/{taskId}`, async () => {
        const response = await send(transport, {
            method: 'DELETE',
            path: routeFor(session, taskRoute(taskId)),
            headers: headersFor(session),
            longestAnswer: longestTaskAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const erasure = parseErasure(bodyOf(response));

        return erasure === null ? failed('unreadable', response.status) : read(erasure);
    });
}

/**
 * Says whether this deployment arranges a day at all.
 *
 * It resolves a registration and calls no provider, so a screen asking it once costs nothing and reads nobody's list.
 * A screen asks it because a control promising an arrangement over a deployment that composes none fails a person at
 * the one moment they trusted it.
 */
export function arrangesDays(session: ClientSession, transport: MailFathomTransport): Promise<ClientResult<boolean>> {
    return spanned(`GET ${todayLayoutRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, todayLayoutRoute),
            headers: headersFor(session),
            longestAnswer: longestTaskAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const arranges = asRecord(bodyOf(response))?.['arrangesDays'];

        return typeof arranges === 'boolean' ? read(arranges) : failed('unreadable', response.status);
    });
}

/**
 * Asks for an arrangement of the day the caller's own client drew.
 *
 * The window is stated rather than derived, exactly as it is for reading the calendar and for the same reason: this
 * deployment keeps no timezone for a person, so which hours are their day is something only their client knows.
 *
 * Nothing is applied by this. What comes back is an offer, and writing any of it is the calendar's own act.
 */
export function layOutToday(
    session: ClientSession,
    transport: MailFathomTransport,
    day: { readonly from: string; readonly until: string },
): Promise<ClientResult<DayLayout>> {
    return spanned(`POST ${todayLayoutRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            changesNothing: true,
            path: routeFor(session, todayLayoutRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({ from: day.from, until: day.until }),
            longestAnswer: longestLayoutAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 429) {
            return read<DayLayout>({ outcome: 'AllowanceSpent', placements: [], notToday: [] });
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const layout = parseLayout(bodyOf(response));

        return layout === null ? failed('unreadable', response.status) : read(layout);
    });
}

function readHalf(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    page: { readonly pageSize?: number; readonly cursor?: string | null; readonly filter?: TaskFilter },
): Promise<ClientResult<PersonalTaskPage>> {
    // The window the screen can render rather than whatever the service would serve, and never more than the bound the
    // route enforces — a request past it is served the maximum, which would leave the client refusing an answer larger
    // than the number it thought it had asked for.
    const pageSize = Math.min(page.pageSize ?? tasksPerPage, mostTasksPerPage);

    return spanned(`GET ${route}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, route)}?${pageArguments(pageSize, page.cursor ?? null, page.filter ?? {})}`,
            headers: headersFor(session),
            longestAnswer: longestTaskPage,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const half = parsePage(bodyOf(response), pageSize);

        return half === null ? failed('unreadable', response.status) : read(half);
    });
}

// Written out rather than composed with `URLSearchParams`, which is a browser API and therefore one this package
// declares nothing of: the wire half of the client knows a route and a status code and nothing about a document.
function pageArguments(pageSize: number, cursor: string | null, filter: TaskFilter): string {
    const asked = [`pageSize=${pageSize.toFixed(0)}`];

    if (cursor !== null && cursor !== '') {
        asked.push(`cursor=${encodeURIComponent(cursor)}`);
    }

    for (const name of ['threadId', 'eventId', 'counterpart'] as const) {
        const value = filter[name];
        if (value !== undefined && value !== '') {
            asked.push(`${name}=${encodeURIComponent(value)}`);
        }
    }

    // The deployment refuses a window with one end, so one end alone is left unsent rather than asked and refused.
    if (filter.from !== undefined && filter.from !== '' && filter.until !== undefined && filter.until !== '') {
        asked.push(`from=${encodeURIComponent(filter.from)}`, `until=${encodeURIComponent(filter.until)}`);
    }

    return asked.join('&');
}

function bodyOf(response: ClientResponse): unknown {
    try {
        return JSON.parse(response.body);
    } catch {
        return null;
    }
}

// The three writes that answer with one task read the same way, and take the general reading of a `404` — the route
// that creates a task names nothing that could be gone, so a deployment answering it that way is one this client
// cannot reach rather than a task somebody erased.
function taskOf(response: ClientResponse | null): ClientResult<PersonalTask> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const task = parseTask(bodyOf(response));

    return task === null ? failed('unreadable', response.status) : read(task);
}

// The ones whose route does name one task, which is what lets a `404` be read as that task being gone: one erased
// while somebody had the list open is let go of, where a deployment that is down is retried.
function namedTaskOf(response: ClientResponse | null): ClientResult<PersonalTask> {
    return response !== null && response.status === 404 ? failed('missing', response.status) : taskOf(response);
}

function parsePage(value: unknown, asked: number): PersonalTaskPage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const entries = record['tasks'];
    const nextCursor = record['nextCursor'] ?? null;

    if (!Array.isArray(entries) || entries.length > asked) {
        return null;
    }

    if (nextCursor !== null && typeof nextCursor !== 'string') {
        return null;
    }

    const tasks: PersonalTask[] = [];
    for (const entry of entries) {
        const task = parseTask(entry);
        if (task === null) {
            return null;
        }

        tasks.push(task);
    }

    return { tasks, nextCursor };
}

function parseLayout(value: unknown): DayLayout | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const arranged = record['arranged'];
    const entries = record['placements'];
    const notToday = record['notToday'];

    if (typeof arranged !== 'boolean' || !Array.isArray(entries) || !Array.isArray(notToday)) {
        return null;
    }

    // Bounded during the walk rather than after it, and against the page bound the list itself carries: an arrangement
    // places tasks, so an answer naming more of them than a page of the list may hold was never an arrangement.
    if (entries.length > mostTasksPerPage || notToday.length > mostTasksPerPage) {
        return null;
    }

    const placements: DayLayoutPlacement[] = [];
    for (const entry of entries) {
        const placement = parsePlacement(entry);
        if (placement === null) {
            return null;
        }

        placements.push(placement);
    }

    for (const task of notToday) {
        if (typeof task !== 'string') {
            return null;
        }
    }

    return {
        outcome: arranged ? 'Arranged' : 'NotArranged',
        placements,
        notToday: notToday as readonly string[],
    };
}

function parsePlacement(value: unknown): DayLayoutPlacement | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const taskId = record['taskId'];
    const startAt = record['startAt'];
    const minutes = record['minutes'];

    if (typeof taskId !== 'string' || typeof startAt !== 'string') {
        return null;
    }

    return Number.isSafeInteger(minutes) && typeof minutes === 'number' && minutes > 0
        ? { taskId, startAt, minutes }
        : null;
}

function parseErasure(value: unknown): PersonalTaskErasure | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const erased = record['erased'];

    return typeof id === 'string' && typeof erased === 'boolean' ? { id, erased } : null;
}

/** Reads one task off a response body, or answers `null` where any field of it is missing or of the wrong shape. */
export function parseTask(value: unknown): PersonalTask | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const title = record['title'];
    const dueOn = record['dueOn'] ?? null;
    const reminders = record['reminders'];
    const remindsAt = record['remindsAt'];
    const origin = record['origin'];
    const completed = record['completed'];
    const sourceMessageId = record['sourceMessageId'] ?? null;
    const direction = record['direction'];
    const estimatedMinutes = record['estimatedMinutes'] ?? null;
    const plan = parsePlan(record['plan']);
    const anchor = parseAnchor(record['anchor']);
    const counterpart = parseCounterpart(record['counterpart']);
    const answer = parseAnswer(record['answer']);

    if (typeof id !== 'string' || typeof title !== 'string' || typeof completed !== 'boolean') {
        return null;
    }

    if (!isTaskOrigin(origin) || !isTaskDirection(direction)) {
        return null;
    }

    // A nested part is `null` where the deployment stated nothing and `undefined` where it stated something unreadable,
    // which refuses the whole task rather than drawing one that quietly lost its plan or its anchor.
    if (
        (estimatedMinutes !== null && !isTaskMinutes(estimatedMinutes)) ||
        plan === undefined ||
        anchor === undefined ||
        counterpart === undefined ||
        answer === undefined
    ) {
        return null;
    }

    // The day is held to its own spelling rather than to being a string, because what reads it compares it against
    // another `yyyy-mm-dd` day as text: an instant where a day was expected sorts after the day it falls on, so a task
    // due today would be grouped under a later week rather than refused.
    if (dueOn !== null && !isCalendarDay(dueOn)) {
        return null;
    }

    if (sourceMessageId !== null && typeof sourceMessageId !== 'string') {
        return null;
    }

    // The line is held against the bound the deployment states for it, because a title past it was never a task this
    // deployment wrote — and a row drawn from one would be this client rendering an answer it should have refused.
    if (title.length > longestTaskTitle) {
        return null;
    }

    // An announcement the contract says cannot exist — more than a task may carry, a lead further ahead than one may
    // state, or the same lead twice — is a body this client refuses rather than a set it draws, exactly as it is for an
    // event: the panel enforces the same three rules before it writes, so an answer breaking them describes a task
    // nothing here could have made.
    if (!isReminderSet(reminders) || !isReminderInstantList(remindsAt)) {
        return null;
    }

    // A lead with no instant beside it, or an instant with no lead, would leave a row drawing one count and announcing
    // another — and a task announcing anything at all was dated, which is what the deployment refuses to write.
    if (reminders.length !== remindsAt.length || (dueOn === null && reminders.length > 0)) {
        return null;
    }

    return {
        id,
        title,
        dueOn,
        reminders,
        remindsAt,
        origin,
        completed,
        sourceMessageId,
        direction,
        estimatedMinutes,
        plan,
        anchor,
        counterpart,
        answer,
    };
}

function parsePlan(value: unknown): TaskPlan | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    const startAt = record?.['startAt'];
    const minutes = record?.['minutes'];

    return isInstant(startAt) && isTaskMinutes(minutes) ? { startAt, minutes } : undefined;
}

function parseAnchor(value: unknown): TaskAnchor | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    const kind = record?.['kind'];
    const id = record?.['id'];
    const label = record?.['label'];

    if ((kind !== 'Thread' && kind !== 'Event') || !isGuid(id) || typeof label !== 'string') {
        return undefined;
    }

    return label.length <= longestAnchorLabel ? { kind, id, label } : undefined;
}

function parseCounterpart(value: unknown): TaskCounterpart | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    const address = record?.['address'];
    const displayName = record?.['displayName'] ?? null;

    if (typeof address !== 'string' || address === '' || address.length > longestCounterpartAddress) {
        return undefined;
    }

    if (displayName !== null && (typeof displayName !== 'string' || displayName.length > longestCounterpartName)) {
        return undefined;
    }

    return { address, displayName };
}

function parseAnswer(value: unknown): TaskAnswer | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    const messageId = record?.['messageId'];
    const arrivedAt = record?.['arrivedAt'];

    return typeof messageId === 'string' && messageId !== '' && isInstant(arrivedAt)
        ? { messageId, arrivedAt }
        : undefined;
}

/** Whether the value is one of the two directions this surface publishes. */
export function isTaskDirection(value: unknown): value is TaskDirection {
    return typeof value === 'string' && taskDirections.includes(value as TaskDirection);
}

function isTaskMinutes(value: unknown): value is number {
    return (
        typeof value === 'number' &&
        Number.isSafeInteger(value) &&
        value >= shortestTaskMinutes &&
        value <= longestTaskMinutes
    );
}

function isInstant(value: unknown): value is string {
    return typeof value === 'string' && !Number.isNaN(Date.parse(value));
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
    return typeof value === 'string' && guidPattern.test(value);
}

/** Whether the value is one of the two origins this surface publishes. */
export function isTaskOrigin(value: unknown): value is PersonalTaskOrigin {
    return typeof value === 'string' && taskOrigins.includes(value as PersonalTaskOrigin);
}

const calendarDayPattern = /^\d{4}-\d{2}-\d{2}$/;

function isCalendarDay(value: unknown): value is string {
    return typeof value === 'string' && calendarDayPattern.test(value);
}
