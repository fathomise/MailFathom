// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import { freshnessGrace, nextFreshnessChangeIn, staleAccounts, staleCauseOf } from './accountFreshness';

const began = '2026-08-15T09:40:00+00:00';
const beganAt = Date.parse(began);

function account(differs: Partial<MailAccount> = {}): MailAccount {
    return {
        id: 'work',
        displayName: 'Work',
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: began,
        behind: false,
        filesJunk: false,
        marksExternalMail: false,
        shared: false,
        synchronizationStateSince: began,
        behindSince: null,
        waitingChangeCount: 0,
        oldestWaitingChangeCommittedAt: null,
        ...differs,
    };
}

describe('staleCauseOf', () => {
    it('says nothing of an account that is current', () => {
        expect(staleCauseOf(account(), beganAt + 10 * freshnessGrace)).toBeNull();
    });

    it('leaves changes that have waited less than a minute unsaid, so an ordinary round trip never shows', () => {
        const waiting = account({ waitingChangeCount: 2, oldestWaitingChangeCommittedAt: began });

        expect(staleCauseOf(waiting, beganAt + freshnessGrace - 1)).toBeNull();
    });

    it('says changes have waited once the oldest of them has waited a minute since the deployment committed it', () => {
        const waiting = account({ waitingChangeCount: 2, oldestWaitingChangeCommittedAt: began });

        expect(staleCauseOf(waiting, beganAt + freshnessGrace)).toEqual({ kind: 'waiting', waiting: 2 });
    });

    it('measures an unreachable mail server from when the deployment says it became unreachable', () => {
        const unreachable = account({
            synchronizationState: 'Unreachable',
            waitingChangeCount: 3,
            oldestWaitingChangeCommittedAt: began,
        });

        expect(staleCauseOf(unreachable, beganAt + freshnessGrace - 1)).toBeNull();
        expect(staleCauseOf(unreachable, beganAt + freshnessGrace)).toEqual({
            kind: 'unreachable',
            since: began,
            waiting: 3,
        });
    });

    it('says the changes waiting on a server that answers when the outage has not held a minute yet', () => {
        const unreachable = account({
            synchronizationState: 'Unreachable',
            synchronizationStateSince: '2026-08-15T09:45:00+00:00',
            waitingChangeCount: 1,
            oldestWaitingChangeCommittedAt: began,
        });

        expect(staleCauseOf(unreachable, beganAt + 5 * freshnessGrace + 1)).toEqual({ kind: 'waiting', waiting: 1 });
    });

    it.each([
        ['a run failing for a minute', { synchronizationState: 'Failing' as const }],
        ['mail left behind for a minute', { behind: true, behindSince: began }],
        [
            'an account never run',
            { synchronizationState: 'NeverSynchronized' as const, synchronizationStateSince: null },
        ],
    ])('says the state of %s', (_, differs) => {
        expect(staleCauseOf(account(differs), beganAt + freshnessGrace)).toEqual({ kind: 'state' });
    });

    it('waits out a run failing and mail behind for a minute before saying either', () => {
        const stopped = account({ synchronizationState: 'Failing', behind: true, behindSince: began });

        expect(staleCauseOf(stopped, beganAt + freshnessGrace - 1)).toBeNull();
    });

    it('says at once a cause whose start the deployment did not report, since nobody watched it begin', () => {
        const waiting = account({ waitingChangeCount: 1, oldestWaitingChangeCommittedAt: null });

        expect(staleCauseOf(waiting, beganAt)).toEqual({ kind: 'waiting', waiting: 1 });
    });
});

describe('staleAccounts', () => {
    it('lists only the accounts not current, in the order the account list gave them', () => {
        const accounts = [
            account({ id: 'a', waitingChangeCount: 1, oldestWaitingChangeCommittedAt: began }),
            account({ id: 'b' }),
            account({ id: 'c', synchronizationState: 'Failing' }),
        ];

        expect(staleAccounts(accounts, beganAt + freshnessGrace).map(({ account: { id } }) => id)).toEqual(['a', 'c']);
    });
});

describe('nextFreshnessChangeIn', () => {
    it('waits until the youngest cause still inside its minute ends it', () => {
        const accounts = [
            account({ waitingChangeCount: 1, oldestWaitingChangeCommittedAt: '2026-08-15T09:40:30+00:00' }),
            account({ synchronizationState: 'Unreachable', synchronizationStateSince: '2026-08-15T09:40:10+00:00' }),
        ];

        expect(nextFreshnessChangeIn(accounts, beganAt + 20_000)).toBe(50_000);
    });

    it('waits for nothing once every cause has outlived its minute, or where none holds', () => {
        expect(nextFreshnessChangeIn([account()], beganAt + 10 * freshnessGrace)).toBeNull();
        expect(
            nextFreshnessChangeIn(
                [account({ waitingChangeCount: 1, oldestWaitingChangeCommittedAt: began })],
                beganAt + freshnessGrace,
            ),
        ).toBeNull();
    });
});
