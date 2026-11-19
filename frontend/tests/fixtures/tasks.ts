// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { corpusEventId } from './calendar';
import { conversationId } from './mail';
import { newsletterId } from './messages';

// The signed-in person's task list, as the two halves the client surface publishes it as: what they committed to
// themselves and what MailFathom read out of their mail and has not been answered yet.
//
// `frontend/tests/AGENTS.md` § *The corpus* holds what the whole of it is and what may go in it. What this file adds
// beside the resting list is the states the Tasks screen has to be looked at in and cannot be reached by scrolling: a
// list with nothing on it, a task nobody has dated, a task already done, a wait a reply closed, a proposal citing the
// message it came out of and one citing none, and a day the deployment would not arrange. The resting list itself is
// the design's sample (`design/data/prototype/tasks.js`), with every address on a reserved `.example` host.
//
// **What is due is stated against a day the consumer names rather than against a fixed one**, which is the shape the
// first rule of that section allows: a list grouped under *Today*, *Tomorrow*, *This week* and *Later* is a list whose
// grouping is the question, so a corpus fixed to one calendar day would draw every row under the last heading on every day but
// one. Which day was asked about is the consumer's to decide, exactly as how far into a folder somebody has read is.
//
// Nobody here exists. Every name is invented and every host is a reserved one, exactly as it is for the mail.

/** How many tasks one page of a half holds, which is what a cursor moves by. */
export const tasksPerPage = 50;

/** The day a number of days after the one named, as a calendar day is spelled. */
function dayAfter(day: string, days: number): string {
    const at = new Date(`${day}T00:00:00`);

    at.setDate(at.getDate() + days);

    return `${String(at.getFullYear())}-${String(at.getMonth() + 1).padStart(2, '0')}-${String(at.getDate()).padStart(2, '0')}`;
}

/**
 * The instants a set of leads falls at, which the deployment resolves and a client draws rather than derives.
 *
 * Nine in the morning on the due day is what a task's reminders are measured back from, and the offset is stated as
 * Greenwich's for the reason the day's own arrangement states it: a corpus that read the machine's zone would answer
 * a different instant on every machine that served it.
 */
function announcedAt(dueOn: string, reminders: readonly number[]): readonly string[] {
    return reminders.map((lead) => {
        const at = new Date(`${dueOn}T09:00:00+00:00`);

        at.setUTCMinutes(at.getUTCMinutes() - lead);

        return at.toISOString().replace('Z', '+00:00');
    });
}

/** What a corpus task holds beyond the line, the day and the defaults every one of them starts from. */
type Held = Record<string, unknown>;

function task(id: string, title: string, dueOn: string | null, held: Held = {}) {
    return {
        id,
        title,
        dueOn,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        direction: 'Owed',
        estimatedMinutes: null,
        plan: null,
        anchor: null,
        counterpart: null,
        answer: null,
        ...held,
    };
}

/** A task that announces itself, which is the row drawn with the pill filled and the state a panel is opened onto. */
function announcing(id: string, title: string, dueOn: string, reminders: readonly number[], held: Held = {}) {
    return task(id, title, dueOn, { reminders, remindsAt: announcedAt(dueOn, reminders), ...held });
}

// The threads and the event the corpus's tasks belong to. One is the conversation the mail corpus holds, so a thread
// opened from a task lands on mail the corpus can draw, and the event is one the calendar corpus holds, so an event
// opened from a task lands on an event the calendar can draw; the rest are named by identity alone, which is what an
// anchor is.
const contractThread = conversationId;
const calculationThread = '00000000-0000-4000-8000-0000000000d1';
const slaThread = '00000000-0000-4000-8000-0000000000d2';
const deliveryThread = '00000000-0000-4000-8000-0000000000d3';
const registerThread = '00000000-0000-4000-8000-0000000000d4';
const ndaThread = '00000000-0000-4000-8000-0000000000d5';
const opinionThread = '00000000-0000-4000-8000-0000000000d6';
const rolloutThread = '00000000-0000-4000-8000-0000000000d7';
const carrierReview = corpusEventId(2);

function thread(id: string, label: string) {
    return { kind: 'Thread', id, label };
}

function person(address: string, displayName: string) {
    return { address, displayName };
}

/** A stretch of the day the corpus plans a task into, at a Greenwich clock time for the reason `announcedAt` gives. */
function planned(day: string, time: string, minutes: number) {
    return { startAt: `${day}T${time}:00+00:00`, minutes };
}

/**
 * What the person committed to themselves, against the day they are reading on: the design's own sample, so every
 * view, group and row shape the Tasks screen draws is reachable without pressing anything.
 *
 * What is owed runs from two tasks planned into today — one over the corpus's meeting, which is the clash — through a
 * task due today with nothing planned, one planned tomorrow, three this week and four later, to one nobody dated and
 * one already done. What is waited on runs from a follow-up due today to one a reply already closed. One owed and one
 * waited on are with the address book's first person, each under another of her addresses.
 *
 * @param day The reader's own day, as a calendar day.
 * @returns One page of the committed half, which is the whole of it.
 */
export function committedTasks(day: string) {
    return {
        tasks: [
            announcing('task-tender', 'Send the 5% CPI cap counter-proposal', day, [24 * 60, 0], {
                estimatedMinutes: 45,
                plan: planned(day, '11:00', 45),
                anchor: thread(contractThread, 'Contract addendum - signatures'),
                counterpart: person('anna@contoso.example', 'Anna Kowalska'),
            }),
            task('task-calculation', 'Reply to Marta about the calculation', day, {
                estimatedMinutes: 20,
                plan: planned(day, '15:00', 20),
                anchor: thread(calculationThread, 'CPI calculation 2027'),
                counterpart: person('marta@contoso.example', 'Marta Nowak'),
            }),
            task('task-room', 'Book the meeting room for the Fabrikam review', day, { estimatedMinutes: 10 }),
            task('task-addendum', 'Review the addendum before the call', dayAfter(day, 1), {
                estimatedMinutes: 30,
                plan: planned(dayAfter(day, 1), '09:00', 30),
                anchor: { kind: 'Event', id: carrierReview, label: 'Carrier review' },
            }),
            announcing('task-return', 'Answer Adventure Works on weekend SLA', dayAfter(day, 3), [0], {
                estimatedMinutes: 30,
                anchor: thread(slaThread, 'Question about weekend SLA'),
                counterpart: person('ewa@adventure-works.example', 'Ewa Sikora'),
            }),
            task('task-delivery', 'Confirm the Fabrikam delivery date', dayAfter(day, 6), {
                estimatedMinutes: 15,
                anchor: thread(deliveryThread, 'Q4 delivery dates'),
            }),
            task('task-audit', 'Prepare the IT contract list for audit', dayAfter(day, 7), {
                estimatedMinutes: 120,
                anchor: thread(registerThread, 'Request for the contract register'),
            }),
            task('task-nda', 'Sign the Northwind NDA', dayAfter(day, 16), {
                estimatedMinutes: 10,
                anchor: thread(ndaThread, 'Draft NDA - Northwind'),
            }),
            // Both ways with the address book's first person, each under another of her two addresses.
            task('task-stock', 'Send Anna the Gdańsk stock count', dayAfter(day, 9), {
                estimatedMinutes: 20,
                counterpart: person('anna@nordwind.example', 'Anna Marlow'),
            }),
            task('task-racking', 'Draft the onboarding checklist for Q4 hires', null, { estimatedMinutes: 60 }),
            task('task-invoice', 'Send the August invoice', dayAfter(day, -2), { completed: true }),
            task('wait-delivery', 'Delivery confirmation from Tomasz Bąk', day, {
                direction: 'Awaited',
                anchor: thread(deliveryThread, 'Q4 delivery dates'),
                counterpart: person('tomasz@fabrikam.example', 'Tomasz Bąk'),
            }),
            task('wait-addendum', 'Signed addendum from Anna Kowalska', dayAfter(day, 1), {
                direction: 'Awaited',
                anchor: thread(contractThread, 'Contract addendum - signatures'),
                counterpart: person('anna@contoso.example', 'Anna Kowalska'),
            }),
            task('wait-quote', 'Racking quote from Anna Marlow', dayAfter(day, 4), {
                direction: 'Awaited',
                counterpart: person('a.marlow@nordwind.example', 'Anna Marlow'),
            }),
            task('wait-opinion', 'Opinion on the indexation cap from Jacek Wrona', day, {
                direction: 'Awaited',
                completed: true,
                anchor: thread(opinionThread, 'Legal opinion - indexation cap'),
                counterpart: person('jacek@contoso.example', 'Jacek Wrona'),
                answer: { messageId: newsletterId, arrivedAt: `${day}T09:12:00+00:00` },
            }),
        ],
        nextCursor: null,
    };
}

/**
 * What MailFathom read out of the mail, and what the Agent suggested, that nobody has answered yet.
 *
 * One read out of mail cites the **message** it came out of rather than the thread that message stands in, which is
 * what the row draws the way back from; the Agent's cites none, which is how a row tells the two apart.
 *
 * @param day The reader's own day, as a calendar day.
 * @returns One page of the proposed half, which is the whole of it.
 */
export function proposedTasks(day: string) {
    return {
        tasks: [
            task('task-signatures', 'Send Piotr the rollout risks before the status call', day, {
                origin: 'Proposed',
                sourceMessageId: newsletterId,
                estimatedMinutes: 20,
                anchor: thread(rolloutThread, 'Re: rollout schedule'),
            }),
            task('task-baseline', 'Ask Marta for the 2026 baseline figures', dayAfter(day, 4), {
                origin: 'Proposed',
                estimatedMinutes: 10,
                counterpart: person('marta@contoso.example', 'Marta Nowak'),
            }),
            task('task-auditor', "Book the auditor's visit", null, {
                origin: 'Proposed',
                sourceMessageId: newsletterId,
                estimatedMinutes: 15,
            }),
        ],
        nextCursor: null,
    };
}

/** Either half with nothing on it, which is the empty state both the list and the capacity panel are drawn in. */
export const emptyTaskPage = { tasks: [], nextCursor: null };

/** What writing a task down answers with, which the screen follows by reading the list again. */
export const taskWritten = task('task-written', 'Call the warehouse back', null);

/** What marking a task done answers with. */
export const taskCompleted = task('task-tender', 'Send the 5% CPI cap counter-proposal', '2026-09-21', {
    completed: true,
});

/** What revising a task answers with, which is the record as the deployment now holds it. */
export const taskRevised = announcing('task-tender', 'Send the 5% CPI cap counter-proposal', '2026-09-21', [24 * 60]);

/** What taking a proposal on answers with: the same line, held now rather than offered. */
export const taskAccepted = task(
    'task-signatures',
    'Send Piotr the rollout risks before the status call',
    '2026-09-22',
    {
        sourceMessageId: newsletterId,
    },
);

/** What erasing a task answers with, for a task the deployment was still holding. */
export const taskErased = { id: 'task-racking', erased: true };

/** Whether this deployment arranges a day at all, which decides whether the panel offers the act. */
export const daysArranged = { arrangesDays: true };

/**
 * What the deployment suggests doing with what is left of the day.
 *
 * Something placed and something left over, because the panel has a sentence for each and a suggestion that fitted
 * everything would draw only half of it.
 *
 * @param day The reader's own day, as a calendar day.
 * @returns The arrangement, as an offer nothing has accepted.
 */
export function dayArrangement(day: string) {
    return {
        arranged: true,
        placements: [
            { taskId: 'task-room', startAt: `${day}T10:00:00+00:00`, minutes: 10 },
            { taskId: 'task-calculation', startAt: `${day}T16:00:00+00:00`, minutes: 20 },
        ],
        notToday: ['task-audit'],
    };
}

/** A day the deployment had nothing left to arrange, which is its own sentence rather than a failure. */
export const dayNotArranged = { arranged: false, placements: [], notToday: [] };
