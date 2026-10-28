// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { failed, failureReasonForStatus, read, wentUnanswered } from './failure';

describe('failureReasonForStatus', () => {
    // The reasons exist because a screen acts differently on each, so the mapping is asserted status by status rather
    // than as "not read": a refused credential that arrived as `unavailable` would be retried forever instead of
    // sending the person back to sign in.
    //
    // `404` is `unavailable` here deliberately, and `mailMessage.test.ts` is where the other reading of it is asserted:
    // most routes on this surface name no one thing, so a deployment that never served the client surface answers a
    // probe with the same status a deleted message answers a read with.
    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [404, 'unavailable'],
        [500, 'unavailable'],
        [503, 'unavailable'],
    ])('reads %i as %s', (status, reason) => {
        expect(failureReasonForStatus(status)).toBe(reason);
    });
});

describe('wentUnanswered', () => {
    it.each([
        ['nothing at all', null],
        ['a bad gateway', 502],
        ['a gateway with nobody behind it', 503],
        ['a gateway that timed out', 504],
    ])('reads %s as the server not answering', (_, status) => {
        expect(wentUnanswered({ reason: 'unavailable', status })).toBe(true);
    });

    it.each([
        ['a route the server does not serve', 'unavailable', 404],
        ['a failure the server itself reported', 'unavailable', 500],
        ['a refused credential', 'unauthenticated', 401],
        ['a missing grant', 'unauthorized', 403],
        ['an answer this client could not read', 'unreadable', 200],
    ] as const)('reads %s as an answer', (_, reason, status) => {
        expect(wentUnanswered({ reason, status })).toBe(false);
    });
});

describe('ClientResult', () => {
    it('carries the value it read', () => {
        expect(read('answer')).toEqual({ outcome: 'read', value: 'answer' });
    });

    it('carries the reason and the status a failure is explained by', () => {
        expect(failed('unauthenticated', 401)).toEqual({
            outcome: 'failed',
            failure: { reason: 'unauthenticated', status: 401 },
        });
    });
});
