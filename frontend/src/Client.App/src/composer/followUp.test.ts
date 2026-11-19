// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { longestAnchorLabel, longestTaskTitle } from '@mailfathom/client-backend';
import { describe, expect, it } from 'vitest';
import { followUpDays, followUpRecord } from './followUp';

// Thursday 27 August 2026 at 10:47 in Warsaw, which is the moment the design's own sample stands at.
const thursdayMorning = Date.parse('2026-08-27T08:47:00Z');

const replyFrom = (who: string) => `Reply from ${who}`;

const noSubject = 'No subject';

describe('followUpDays', () => {
    it('offers tomorrow, the next Monday, and a week on, as the reader’s zone counts days', () => {
        expect(followUpDays(thursdayMorning, 'Europe/Warsaw')).toStrictEqual({
            tomorrow: '2026-08-28',
            monday: '2026-08-31',
            week: '2026-09-03',
        });
    });
});

describe('followUpRecord', () => {
    it('the follow-up record names the first recipient and the replied thread', () => {
        expect(
            followUpRecord({
                to: ['Anna@Contoso.example', 'marta@contoso.example'],
                named: [{ address: 'anna@contoso.example', name: 'Anna Kowalska' }],
                thread: { threadId: 'thread-1', subject: 'Contract addendum - signatures' },
                dueOn: '2026-08-28',
                timeZone: 'Europe/Warsaw',
                replyFrom,
                noSubject,
            }),
        ).toStrictEqual({
            title: 'Reply from Anna Kowalska',
            dueOn: '2026-08-28',
            reminders: [0],
            dueDayOffsetMinutes: 120,
            sourceMessageId: null,
            direction: 'Awaited',
            estimatedMinutes: null,
            plan: null,
            anchor: { kind: 'Thread', id: 'thread-1', label: 'Contract addendum - signatures' },
            counterpart: { address: 'Anna@Contoso.example', displayName: 'Anna Kowalska' },
        });
    });

    it('names a recipient nobody named by the address, and belongs to no thread for a message of its own', () => {
        const record = followUpRecord({
            to: ['ewa@adventure-works.example'],
            named: [],
            thread: null,
            dueOn: '2026-08-28',
            timeZone: null,
            replyFrom,
            noSubject,
        });

        expect(record).toMatchObject({
            title: 'Reply from ewa@adventure-works.example',
            anchor: null,
            counterpart: { address: 'ewa@adventure-works.example', displayName: null },
        });
    });

    it('waits in a thread whose message had no subject under the words for no subject', () => {
        const record = followUpRecord({
            to: ['ewa@adventure-works.example'],
            named: [],
            thread: { threadId: 'thread-2', subject: ' ' },
            dueOn: '2026-08-28',
            timeZone: null,
            replyFrom,
            noSubject,
        });

        expect(record?.anchor).toStrictEqual({ kind: 'Thread', id: 'thread-2', label: 'No subject' });
    });

    it('cuts a subject and a line longer than the deployment keeps to what it keeps', () => {
        const record = followUpRecord({
            to: [`${'x'.repeat(300)}@adventure-works.example`],
            named: [],
            thread: { threadId: 'thread-3', subject: 's'.repeat(260) },
            dueOn: '2026-08-28',
            timeZone: null,
            replyFrom,
            noSubject,
        });

        expect(record?.anchor?.label).toHaveLength(longestAnchorLabel);
        expect(record?.title).toHaveLength(longestTaskTitle);
    });

    it('waits for nobody where the message has no recipient', () => {
        expect(
            followUpRecord({
                to: [],
                named: [],
                thread: null,
                dueOn: '2026-08-28',
                timeZone: null,
                replyFrom,
                noSubject,
            }),
        ).toBeNull();
    });
});
