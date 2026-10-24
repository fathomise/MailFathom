// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { OutgoingMail } from '@mailfathom/client-backend';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { catalogues, type Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import {
    addresseeOf,
    attemptsSaid,
    standingLine,
    standingOf,
    standingShapes,
    type OutboxWording,
} from './outboxStanding';

/** Reads the catalogue the way the provider does, which is a lookup and a hole filled. */
function translating(locale: Locale): Translate {
    return (key, values) =>
        catalogues[locale][key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);
}

/** The instant every wording here is made at: the day the sample send was handed over and is tried again on. */
const today = '2026-08-31T12:00:00+00:00';

function wording(locale: Locale = 'en'): OutboxWording {
    return { locale, timeZone: null, translate: translating(locale), now: Date.parse(today) };
}

const send: OutgoingMail = {
    outgoingEmailId: 'send',
    account: 'work',
    stage: 'Recorded',
    heldByAttempt: false,
    subject: 'Re: rollout schedule',
    recordedAt: '2026-08-31T10:12:00+00:00',
    dueAt: null,
    availableAt: '2026-08-31T10:55:00+00:00',
    attemptCount: 0,
    failure: null,
    recipients: [{ address: 'p.zielinski@company.example', role: 'To' }],
    otherRecipientCount: 0,
};

const zone = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zone;
});

describe('standingOf', () => {
    it.each([
        ['waiting', { stage: 'Recorded', heldByAttempt: false, attemptCount: 0, dueAt: null }],
        ['scheduled', { stage: 'Recorded', heldByAttempt: false, attemptCount: 0, dueAt: '2026-09-01T06:00:00+00:00' }],
        ['retrying', { stage: 'Recorded', heldByAttempt: false, attemptCount: 2, dueAt: null }],
        ['sending', { stage: 'Recorded', heldByAttempt: true, attemptCount: 1, dueAt: null }],
        ['sending', { stage: 'TransmissionBegun', heldByAttempt: true, attemptCount: 1, dueAt: null }],
        ['unknown', { stage: 'TransmissionBegun', heldByAttempt: false, attemptCount: 1, dueAt: null }],
        ['refused', { stage: 'Refused', heldByAttempt: false, attemptCount: 1, dueAt: null }],
    ] as const)('reads a send as %s from what the deployment says about it', (standing, facts) => {
        expect(standingOf(facts)).toBe(standing);
    });

    it.each(['Sent', 'Cancelled'] as const)('reads a %s send as one that has left the Outbox', (stage) => {
        expect(standingOf({ stage, heldByAttempt: false, attemptCount: 1, dueAt: null })).toBeNull();
    });
});

describe('standingShapes', () => {
    it('offers withdrawing a send only while no attempt has taken it, and deciding about one only once one has', () => {
        expect(Object.entries(standingShapes).map(([standing, { decisions }]) => [standing, decisions])).toStrictEqual([
            ['waiting', 'cancel'],
            ['scheduled', 'cancel'],
            ['retrying', 'cancel'],
            ['sending', null],
            ['unknown', 'decide'],
            ['refused', 'decide'],
        ]);
    });
});

describe('standingLine', () => {
    it('says when a send failing to leave is tried next, on the reader’s clock, with the attempts behind it', () => {
        expect(standingLine('retrying', { ...send, attemptCount: 2 }, wording())).toBe(
            'Trying again at 12:55 PM · 2 attempts',
        );
        expect(standingLine('retrying', { ...send, attemptCount: 2 }, wording('pl'))).toBe(
            'Kolejna próba o 12:55 · 2 próby',
        );
    });

    it('says the retry time on the clock of the zone the runtime reports', () => {
        process.env['TZ'] = 'America/New_York';

        expect(standingLine('retrying', { ...send, attemptCount: 2 }, wording())).toBe(
            'Trying again at 6:55 AM · 2 attempts',
        );
    });

    // A backoff may run past midnight, and a clock time alone would then name a moment today that already went by.
    it('says the day of a retry that does not fall today', () => {
        const tomorrow = { ...send, attemptCount: 2, availableAt: '2026-09-01T07:30:00+00:00' };

        expect(standingLine('retrying', tomorrow, wording())).toBe('Trying again at 9/1/26, 9:30 AM · 2 attempts');
        expect(standingLine('retrying', tomorrow, wording('pl'))).toBe('Kolejna próba o 1.09.2026, 09:30 · 2 próby');
    });

    it('says no attempts for a send being sent right now', () => {
        expect(standingLine('sending', { ...send, heldByAttempt: true, attemptCount: 1 }, wording())).toBe(
            'Sending now',
        );
    });
});

describe('attemptsSaid', () => {
    it('counts attempts in the plural form Polish takes for each count', () => {
        expect([1, 2, 5].map((count) => attemptsSaid(count, wording('pl')))).toStrictEqual([
            '1 próba',
            '2 próby',
            '5 prób',
        ]);
    });
});

describe('addresseeOf', () => {
    it('names the first recipient and counts everybody else the send is addressed to', () => {
        expect(
            addresseeOf(
                [
                    { address: 'p.zielinski@company.example', role: 'To' },
                    { address: 'j.wrona@company.example', role: 'Cc' },
                ],
                3,
                wording(),
            ),
        ).toBe('p.zielinski@company.example +4');
    });

    it('names a send addressed to one recipient by that recipient alone', () => {
        expect(addresseeOf(send.recipients, 0, wording())).toBe('p.zielinski@company.example');
    });
});
