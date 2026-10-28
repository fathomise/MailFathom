// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    channelGrace,
    mailServerGrace,
    nextChangeIn,
    readConnection,
    type ConnectionEvidence,
} from './connectionStatus';

const began = 1_000_000;

/** A client whose session answered and whose channel has stood since `began`, with nothing else wrong. */
const standing: ConnectionEvidence = {
    offlineSince: null,
    unansweredSince: null,
    sessionAnswered: true,
    channel: { standing: 'opened', since: began, hasStood: true },
    unreachableAccounts: [],
};

describe('readConnection', () => {
    it('is connecting from sign-in until the session read has answered', () => {
        expect(readConnection({ ...standing, sessionAnswered: false, channel: null }, began).level).toBe('connecting');
    });

    it('stays connecting through the first channel opening rather than turning amber at start-up', () => {
        const opening: ConnectionEvidence = {
            ...standing,
            channel: { standing: 'refused', since: began, hasStood: false },
        };

        expect(readConnection(opening, began + channelGrace - 1).level).toBe('connecting');
        expect(readConnection(opening, began + channelGrace)).toEqual({
            level: 'delayed',
            delays: [{ kind: 'channel', since: began }],
        });
    });

    it('is online once the session answered and the channel stands', () => {
        expect(readConnection(standing, began + 5 * channelGrace).level).toBe('online');
    });

    it('is online on a server that carries no channel at all', () => {
        expect(readConnection({ ...standing, channel: null }, began).level).toBe('online');
    });

    it('never turns amber for a channel that reopens within ten seconds', () => {
        const dropped: ConnectionEvidence = {
            ...standing,
            channel: { standing: 'dropped', since: began, hasStood: true },
        };

        expect(readConnection(dropped, began + channelGrace - 1).level).toBe('online');
        expect(readConnection(dropped, began + channelGrace).level).toBe('delayed');
    });

    it('turns amber for a mail server unreachable for a minute without a break, and not a second sooner', () => {
        const unreachable: ConnectionEvidence = {
            ...standing,
            unreachableAccounts: [{ account: 'Work', since: began }],
        };

        expect(readConnection(unreachable, began + 59_000).level).toBe('online');
        expect(readConnection(unreachable, began + 61_000)).toEqual({
            level: 'delayed',
            delays: [{ kind: 'mailServer', account: 'Work', since: began }],
        });
    });

    it('lists every cause of a delay that holds', () => {
        const both: ConnectionEvidence = {
            ...standing,
            channel: { standing: 'refused', since: began, hasStood: true },
            unreachableAccounts: [{ account: 'Work', since: began }],
        };

        expect(readConnection(both, began + mailServerGrace)).toEqual({
            level: 'delayed',
            delays: [
                { kind: 'channel', since: began },
                { kind: 'mailServer', account: 'Work', since: began },
            ],
        });
    });

    it('is not connected while the server does not answer, outranking every delay', () => {
        const unanswered: ConnectionEvidence = {
            ...standing,
            unansweredSince: began,
            channel: { standing: 'unanswered', since: began, hasStood: true },
            unreachableAccounts: [{ account: 'Work', since: began - mailServerGrace }],
        };

        expect(readConnection(unanswered, began + mailServerGrace)).toEqual({
            level: 'disconnected',
            cause: 'unanswered',
            since: began,
        });
    });

    it('says the machine is offline rather than that the server did not answer', () => {
        expect(readConnection({ ...standing, offlineSince: began, unansweredSince: began }, began)).toEqual({
            level: 'disconnected',
            cause: 'offline',
            since: began,
        });
    });
});

describe('nextChangeIn', () => {
    it('waits for the end of the nearest grace period', () => {
        const evidence: ConnectionEvidence = {
            ...standing,
            channel: { standing: 'dropped', since: began, hasStood: true },
            unreachableAccounts: [{ account: 'Work', since: began }],
        };

        expect(nextChangeIn(evidence, began + 4_000)).toBe(channelGrace - 4_000);
        expect(nextChangeIn(evidence, began + channelGrace)).toBe(mailServerGrace - channelGrace);
    });

    it('waits for nothing while nothing is crossing a threshold', () => {
        expect(nextChangeIn(standing, began)).toBeNull();
    });
});
