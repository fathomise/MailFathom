// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientSession } from './session';
import {
    acceptTask,
    arrangesDays,
    eraseTask,
    layOutToday,
    type PersonalTaskRecord,
    longestAnchorLabel,
    longestTaskMinutes,
    longestTaskTitle,
    parseTask,
    readOwnTasks,
    readProposedTasks,
    recordTask,
    reviseTask,
    setTaskCompletion,
    shortestTaskMinutes,
} from './tasks';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const asserted = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    title: 'Answer the counter-proposal',
    dueOn: '2026-09-21',
    reminders: [1440, 0],
    remindsAt: ['2026-09-20T09:00:00+02:00', '2026-09-21T09:00:00+02:00'],
    origin: 'Asserted',
    completed: false,
    sourceMessageId: null,
    direction: 'Owed',
    estimatedMinutes: null,
    plan: null,
    anchor: null,
    counterpart: null,
    answer: null,
};

const proposed = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91',
    title: 'Send the signed annex',
    dueOn: null,
    reminders: [],
    remindsAt: [],
    origin: 'Proposed',
    completed: false,
    sourceMessageId: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92',
    direction: 'Owed',
    estimatedMinutes: null,
    plan: null,
    anchor: null,
    counterpart: null,
    answer: null,
};

const waiting = {
    ...asserted,
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a93',
    title: 'Hear back about the annex',
    direction: 'Awaited',
    estimatedMinutes: 15,
    plan: { startAt: '2026-09-21T08:30:00+00:00', minutes: 30 },
    anchor: { kind: 'Thread', id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a94', label: 'Annex 3' },
    counterpart: { address: 'dana@example.invalid', displayName: 'Dana' },
    answer: { messageId: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a95', arrivedAt: '2026-09-22T10:00:00+00:00' },
};

const announced: PersonalTaskRecord = {
    title: 'Answer the counter-proposal',
    dueOn: '2026-09-21',
    reminders: [1440, 0],
    dueDayOffsetMinutes: 120,
    sourceMessageId: null,
    direction: 'Owed',
    estimatedMinutes: null,
    plan: null,
    anchor: null,
    counterpart: null,
};

const pageBody = JSON.stringify({ tasks: [asserted], nextCursor: 'page-two' });

// The transport is the network boundary and the whole of what a test here fakes. No operation in this module reads a
// header off an answer, so every helper supplies the empty set.
type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

describe('readOwnTasks', () => {
    it('asks for the committed half with the window it will render', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnTasks(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks?pageSize=50');
    });

    it('carries a cursor into the request that continues the walk', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnTasks(session, transport, { cursor: 'page two/first' });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/tasks?pageSize=50&cursor=page%20two%2Ffirst',
        );
    });

    it('never asks for more than the bound the route enforces', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnTasks(session, transport, { pageSize: 5000 });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks?pageSize=200');
    });

    it('reads the page and where the walk continues', async () => {
        const answer = await readOwnTasks(session, answering({ status: 200, body: pageBody }));

        expect(answer).toEqual({
            outcome: 'read',
            value: {
                tasks: [
                    {
                        id: asserted.id,
                        title: 'Answer the counter-proposal',
                        dueOn: '2026-09-21',
                        reminders: [1440, 0],
                        remindsAt: ['2026-09-20T09:00:00+02:00', '2026-09-21T09:00:00+02:00'],
                        origin: 'Asserted',
                        completed: false,
                        sourceMessageId: null,
                        direction: 'Owed',
                        estimatedMinutes: null,
                        plan: null,
                        anchor: null,
                        counterpart: null,
                        answer: null,
                    },
                ],
                nextCursor: 'page-two',
            },
        });
    });

    it('reads the end of the half as a walk with nowhere left to go', async () => {
        const answer = await readOwnTasks(
            session,
            answering({ status: 200, body: JSON.stringify({ tasks: [], nextCursor: null }) }),
        );

        expect(answer.outcome === 'read' && answer.value.nextCursor).toBeNull();
    });

    it('refuses a page holding more tasks than it asked for', async () => {
        const overlong = JSON.stringify({
            tasks: Array.from({ length: 3 }, () => asserted),
            nextCursor: null,
        });

        const answer = await readOwnTasks(session, answering({ status: 200, body: overlong }), { pageSize: 2 });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
    ])('reads a %i as %s', async (status, reason) => {
        const answer = await readOwnTasks(session, answering({ status, body: '' }));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reads a deployment that did not answer at all as unavailable', async () => {
        const answer = await readOwnTasks(session, () => Promise.reject(new Error('no route to host')));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

describe('the filter a half is read with', () => {
    it('sends the filter as query parameters', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnTasks(session, transport, {
            filter: {
                threadId: 'thread/1',
                eventId: 'event 1',
                counterpart: 'dana@example.invalid',
                from: '2026-09-21T00:00:00+02:00',
                until: '2026-09-22T00:00:00+02:00',
            },
        });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/tasks?pageSize=50&threadId=thread%2F1&eventId=event%201' +
                '&counterpart=dana%40example.invalid&from=2026-09-21T00%3A00%3A00%2B02%3A00' +
                '&until=2026-09-22T00%3A00%3A00%2B02%3A00',
        );
    });

    it('sends a window only when both of its ends are stated', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readProposedTasks(session, transport, { filter: { from: '2026-09-21T00:00:00Z' } });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks/proposed?pageSize=50');
    });
});

describe('readProposedTasks', () => {
    it('asks the half of its own rather than the committed one with a filter', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ tasks: [proposed], nextCursor: null }),
        });

        await readProposedTasks(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks/proposed?pageSize=50');
    });

    it('reads the message a proposal was taken out of', async () => {
        const answer = await readProposedTasks(
            session,
            answering({ status: 200, body: JSON.stringify({ tasks: [proposed], nextCursor: null }) }),
        );

        expect(answer.outcome === 'read' && answer.value.tasks[0]?.sourceMessageId).toBe(proposed.sourceMessageId);
    });
});

describe('recordTask', () => {
    it('states the line, the day, what announces it and the message the task cites', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(asserted) });

        await recordTask(session, transport, announced);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks');
        expect(requests[0]?.body).toBe(JSON.stringify(announced));
    });

    it('states the offset the due day runs in beside the leads, the deployment keeping no timezone', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(asserted) });

        await recordTask(session, transport, announced);

        expect(JSON.parse(requests[0]?.body ?? 'null')).toMatchObject({
            reminders: [1440, 0],
            dueDayOffsetMinutes: 120,
        });
    });

    it('reads the task as it was written, with the instants its leads fall at', async () => {
        const answer = await recordTask(session, answering({ status: 200, body: JSON.stringify(asserted) }), announced);

        expect(answer.outcome === 'read' && answer.value.remindsAt).toEqual([
            '2026-09-20T09:00:00+02:00',
            '2026-09-21T09:00:00+02:00',
        ]);
    });

    it('reads a refusal of what it stated as a body this client could not use', async () => {
        const answer = await recordTask(session, answering({ status: 400, body: '' }), {
            title: '',
            dueOn: null,
            reminders: [],
            dueDayOffsetMinutes: null,
            sourceMessageId: null,
            direction: 'Owed',
            estimatedMinutes: null,
            plan: null,
            anchor: null,
            counterpart: null,
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 400 } });
    });

    it('reads a 404 as the deployment being out of reach, this route naming no task to be gone', async () => {
        const answer = await recordTask(session, answering({ status: 404, body: '' }), {
            title: 'Answer the counter-proposal',
            dueOn: null,
            reminders: [],
            dueDayOffsetMinutes: null,
            sourceMessageId: null,
            direction: 'Owed',
            estimatedMinutes: null,
            plan: null,
            anchor: null,
            counterpart: null,
        });

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 404 } });
    });
});

describe('the commitment a task is written with', () => {
    const committed = {
        ...announced,
        direction: 'Awaited',
        estimatedMinutes: 15,
        plan: { startAt: '2026-09-21T08:30:00+00:00', minutes: 30 },
        anchor: { kind: 'Thread', id: waiting.anchor.id, label: 'Annex 3' },
        counterpart: { address: 'dana@example.invalid', displayName: null },
    } as const;

    it('sends the commitment when recording', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(waiting) });

        await recordTask(session, transport, committed);

        expect(JSON.parse(requests[0]?.body ?? 'null')).toMatchObject({
            direction: 'Awaited',
            estimatedMinutes: 15,
            plan: { startAt: '2026-09-21T08:30:00+00:00', minutes: 30 },
            anchor: { kind: 'Thread', id: waiting.anchor.id, label: 'Annex 3' },
            counterpart: { address: 'dana@example.invalid', displayName: null },
        });
    });

    it('restates the whole commitment when revising', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(waiting) });

        await reviseTask(session, transport, waiting.id, committed);

        expect(requests[0]?.body).toBe(JSON.stringify(committed));
    });
});

describe('reviseTask', () => {
    it('states the whole record at the route that names the one task', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(asserted) });

        await reviseTask(session, transport, asserted.id, announced);

        expect(requests[0]?.method).toBe('PUT');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/tasks/${asserted.id}`);
        expect(requests[0]?.body).toBe(JSON.stringify(announced));
    });

    it('states a task announcing nothing as an empty set rather than as a field left out', async () => {
        const quiet = { ...asserted, reminders: [], remindsAt: [] };
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(quiet) });

        const answer = await reviseTask(session, transport, asserted.id, {
            ...announced,
            reminders: [],
            dueDayOffsetMinutes: null,
        });

        expect(JSON.parse(requests[0]?.body ?? 'null')).toMatchObject({ reminders: [], dueDayOffsetMinutes: null });
        expect(answer.outcome === 'read' && answer.value.reminders).toEqual([]);
    });

    it('reads a task somebody erased while the screen held it as missing rather than as a deployment that is down', async () => {
        const answer = await reviseTask(session, answering({ status: 404, body: '' }), asserted.id, announced);

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });
});

describe('setTaskCompletion', () => {
    it('states the completion on the one route that carries both directions', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ ...asserted, completed: true }),
        });

        await setTaskCompletion(session, transport, asserted.id, true);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/tasks/${asserted.id}/completion`);
        expect(requests[0]?.body).toBe(JSON.stringify({ completed: true }));
    });

    it('reads a task the list no longer holds as missing rather than as the deployment being down', async () => {
        const answer = await setTaskCompletion(session, answering({ status: 404, body: '' }), asserted.id, true);

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });
});

describe('acceptTask', () => {
    it('takes a proposal on at the acceptance route and states no body', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ ...proposed, origin: 'Asserted' }),
        });

        await acceptTask(session, transport, proposed.id);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/tasks/${proposed.id}/acceptance`);
        expect(requests[0]?.body).toBeUndefined();
    });

    it('reads the task as one the person now owes', async () => {
        const answer = await acceptTask(
            session,
            answering({ status: 200, body: JSON.stringify({ ...proposed, origin: 'Asserted' }) }),
            proposed.id,
        );

        expect(answer.outcome === 'read' && answer.value.origin).toBe('Asserted');
    });
});

describe('eraseTask', () => {
    it('reads what the erasure removed', async () => {
        const answer = await eraseTask(
            session,
            answering({ status: 200, body: JSON.stringify({ id: asserted.id, erased: true }) }),
            asserted.id,
        );

        expect(answer).toEqual({ outcome: 'read', value: { id: asserted.id, erased: true } });
    });

    it('reads an erasure that removed nothing as an answer rather than as a failure', async () => {
        const answer = await eraseTask(
            session,
            answering({ status: 200, body: JSON.stringify({ id: asserted.id, erased: false }) }),
            asserted.id,
        );

        expect(answer.outcome === 'read' && answer.value.erased).toBe(false);
    });

    it('refuses an answer that is not an erasure', async () => {
        const answer = await eraseTask(session, answering({ status: 200, body: '{"id":1}' }), asserted.id);

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('arrangesDays', () => {
    it('reads whether this deployment arranges a day at all', async () => {
        const answer = await arrangesDays(
            session,
            answering({ status: 200, body: JSON.stringify({ arrangesDays: true }) }),
        );

        expect(answer).toEqual({ outcome: 'read', value: true });
    });

    it('reads a deployment that arranges none', async () => {
        const answer = await arrangesDays(
            session,
            answering({ status: 200, body: JSON.stringify({ arrangesDays: false }) }),
        );

        expect(answer).toEqual({ outcome: 'read', value: false });
    });

    it('refuses an answer saying nothing about whether days are arranged', async () => {
        const answer = await arrangesDays(session, answering({ status: 200, body: '{}' }));

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('layOutToday', () => {
    const day = { from: '2026-09-21T07:00:00+02:00', until: '2026-09-21T18:00:00+02:00' };

    const arranged = JSON.stringify({
        arranged: true,
        placements: [{ taskId: asserted.id, startAt: '2026-09-21T09:00:00+02:00', minutes: 45 }],
        notToday: [proposed.id],
    });

    it('states the window the reader’s own client drew', async () => {
        const { transport, requests } = recording({ status: 200, body: arranged });

        await layOutToday(session, transport, day);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/tasks/today/layout');
        expect(requests[0]?.body).toBe(JSON.stringify({ from: day.from, until: day.until }));
    });

    it('reads the arrangement and what does not fit the day', async () => {
        const answer = await layOutToday(session, answering({ status: 200, body: arranged }), day);

        expect(answer).toEqual({
            outcome: 'read',
            value: {
                outcome: 'Arranged',
                placements: [{ taskId: asserted.id, startAt: '2026-09-21T09:00:00+02:00', minutes: 45 }],
                notToday: [proposed.id],
            },
        });
    });

    it('reads a deployment that composed no arrangement', async () => {
        const answer = await layOutToday(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ arranged: false, placements: [], notToday: [] }),
            }),
            day,
        );

        expect(answer.outcome === 'read' && answer.value.outcome).toBe('NotArranged');
    });

    it('reads a spent provider allowance as an answer the screen says something about', async () => {
        const answer = await layOutToday(session, answering({ status: 429, body: '' }), day);

        expect(answer).toEqual({
            outcome: 'read',
            value: { outcome: 'AllowanceSpent', placements: [], notToday: [] },
        });
    });

    it('refuses a placement stating no duration', async () => {
        const answer = await layOutToday(
            session,
            answering({
                status: 200,
                body: JSON.stringify({
                    arranged: true,
                    placements: [{ taskId: asserted.id, startAt: '2026-09-21T09:00:00+02:00', minutes: 0 }],
                    notToday: [],
                }),
            }),
            day,
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an arrangement naming something other than a task', async () => {
        const answer = await layOutToday(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ arranged: true, placements: [], notToday: [7] }),
            }),
            day,
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

describe('parseTask', () => {
    it('reads a waiting task with its anchor, counterpart and answer', () => {
        expect(parseTask(waiting)).toEqual(waiting);
    });

    it('reads an event anchor', () => {
        const anchor = { kind: 'Event', id: waiting.anchor.id, label: 'Review call' };

        expect(parseTask({ ...waiting, anchor })?.anchor).toEqual(anchor);
    });

    it('reads a task whose commitment fields the deployment left out as an owed one with none stated', () => {
        const nested = ['estimatedMinutes', 'plan', 'anchor', 'counterpart', 'answer'];
        const bare = Object.fromEntries(Object.entries(asserted).filter(([name]) => !nested.includes(name)));

        expect(parseTask(bare)).toEqual(asserted);
    });

    it('refuses a task whose plan is shorter than five minutes as unreadable', async () => {
        const task = { ...waiting, plan: { startAt: waiting.plan.startAt, minutes: shortestTaskMinutes - 1 } };
        const answer = await readOwnTasks(
            session,
            answering({ status: 200, body: JSON.stringify({ tasks: [task], nextCursor: null }) }),
        );

        expect(answer).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an unknown direction', () => {
        expect(parseTask({ ...waiting, direction: 'Sideways' })).toBeNull();
    });

    it('refuses an unknown anchor kind', () => {
        expect(parseTask({ ...waiting, anchor: { ...waiting.anchor, kind: 'Message' } })).toBeNull();
    });

    it.each([
        ['an estimate below the shortest task', { ...waiting, estimatedMinutes: shortestTaskMinutes - 1 }],
        ['an estimate above the longest task', { ...waiting, estimatedMinutes: longestTaskMinutes + 1 }],
        ['an estimate that is not whole minutes', { ...waiting, estimatedMinutes: 7.5 }],
        [
            'a plan longer than the longest task',
            { ...waiting, plan: { ...waiting.plan, minutes: longestTaskMinutes + 1 } },
        ],
        ['a plan beginning at nothing readable', { ...waiting, plan: { ...waiting.plan, startAt: 'soon' } }],
        ['a plan that is not a record', { ...waiting, plan: 30 }],
        ['an anchor naming no identity', { ...waiting, anchor: { ...waiting.anchor, id: 'not-a-guid' } }],
        [
            'an anchor label past the bound',
            { ...waiting, anchor: { ...waiting.anchor, label: 'x'.repeat(longestAnchorLabel + 1) } },
        ],
        ['a counterpart with no address', { ...waiting, counterpart: { address: '', displayName: null } }],
        [
            'a counterpart address past the bound',
            { ...waiting, counterpart: { address: `${'a'.repeat(320)}@x`, displayName: null } },
        ],
        [
            'a counterpart name past the bound',
            { ...waiting, counterpart: { address: 'dana@example.invalid', displayName: 'n'.repeat(201) } },
        ],
        ['an answer naming no message', { ...waiting, answer: { messageId: 4, arrivedAt: waiting.answer.arrivedAt } }],
        ['an answer arriving at nothing readable', { ...waiting, answer: { ...waiting.answer, arrivedAt: 'later' } }],
    ])('refuses %s', (_, value) => {
        expect(parseTask(value)).toBeNull();
    });

    it('reads a task nobody has said a day for, which announces nothing either', () => {
        expect(parseTask({ ...asserted, dueOn: null, reminders: [], remindsAt: [] })?.dueOn).toBeNull();
    });

    it('reads the leads longest first beside the instants they fall at', () => {
        expect(parseTask(asserted)?.reminders).toEqual([1440, 0]);
    });

    it.each([
        ['an origin the surface does not publish', { ...asserted, origin: 'Invented' }],
        ['more leads than a task may carry', { ...asserted, reminders: Array.from({ length: 17 }, (_, at) => at) }],
        ['a lead further ahead than one may state', { ...asserted, reminders: [28 * 24 * 60 + 1] }],
        ['the same lead twice', { ...asserted, reminders: [60, 60] }],
        ['a lead that is not a number', { ...asserted, reminders: ['60'] }],
        ['an instant that is not written down', { ...asserted, remindsAt: [20260920] }],
        [
            'a lead with no instant beside it',
            { ...asserted, reminders: [1440, 0], remindsAt: ['2026-09-20T09:00:00+02:00'] },
        ],
        ['leads against a day nobody stated', { ...asserted, dueOn: null }],
        ['a completion that is not a flag', { ...asserted, completed: 'no' }],
        ['a day that is not written down', { ...asserted, dueOn: 20260921 }],
        ['an instant where the day it falls on belongs', { ...asserted, dueOn: '2026-09-21T10:00:00Z' }],
        ['a day written in another order', { ...asserted, dueOn: '21-09-2026' }],
        ['a citation that is not an identity', { ...asserted, sourceMessageId: 12 }],
        ['a line past the bound the deployment states', { ...asserted, title: 'x'.repeat(longestTaskTitle + 1) }],
        ['an array where a record belongs', []],
        ['nothing at all', null],
    ])('refuses %s', (_, value) => {
        expect(parseTask(value)).toBeNull();
    });
});
