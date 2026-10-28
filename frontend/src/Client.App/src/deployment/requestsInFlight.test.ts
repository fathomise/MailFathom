// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { movingWithin, requestsInFlightOver } from './requestsInFlight';

// The clock is a variable the test moves, so what is proven is which request counts as moving at which instant.

function heldAt(start: number): { readonly at: { now: number }; readonly inFlight: ReturnType<typeof requestsInFlightOver> } {
    const at = { now: start };

    return { at, inFlight: requestsInFlightOver(() => at.now) };
}

describe('requestsInFlightOver', () => {
    it('abandons a request that has not been answered when the probe gives up', () => {
        const { inFlight } = heldAt(0);
        const request = inFlight.enter(false, false);

        inFlight.abandonStill();

        expect(request.signal.aborted).toBe(true);
    });

    it('leaves a download alone while its bytes keep arriving', () => {
        const { at, inFlight } = heldAt(0);
        const request = inFlight.enter(false, false);

        request.answered();
        at.now = 40_000;
        request.moved();
        at.now += movingWithin - 1;
        inFlight.abandonStill();

        expect(request.signal.aborted).toBe(false);
    });

    it('abandons a download whose bytes stopped arriving', () => {
        const { at, inFlight } = heldAt(0);
        const request = inFlight.enter(false, false);

        request.answered();
        request.moved();
        at.now = movingWithin;
        inFlight.abandonStill();

        expect(request.signal.aborted).toBe(true);
    });

    it('leaves an upload alone until its answer begins, past two probes that went unanswered', () => {
        const { at, inFlight } = heldAt(0);
        const upload = inFlight.enter(true, true);

        at.now = 60_000;
        inFlight.abandonStill();
        inFlight.abandonStill();

        expect(upload.signal.aborted).toBe(false);
    });

    it('says when the longest-waiting request and the longest-waiting write began', () => {
        const { at, inFlight } = heldAt(5);
        const read = inFlight.enter(false, false);

        at.now = 9;
        const write = inFlight.enter(true, false);

        expect(inFlight.oldest()).toBe(5);
        expect(inFlight.oldestWrite()).toBe(9);

        read.left();
        write.left();

        expect(inFlight.oldest()).toBeNull();
        expect(inFlight.oldestWrite()).toBeNull();
    });

    it('tells a listener each time a request enters or leaves', () => {
        const { inFlight } = heldAt(0);
        let told = 0;
        const stop = inFlight.listen(() => {
            told += 1;
        });
        const request = inFlight.enter(false, false);

        request.left();
        request.left();
        stop();
        inFlight.enter(false, false);

        expect(told).toBe(2);
    });
});
