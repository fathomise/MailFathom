// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { requestsInFlightOver } from '../deployment/requestsInFlight';
import { missesBeforeLost, probeAfter, probeLiveness, type ProbeOutcome } from './livenessProbe';

// The schedule is proven against a clock the test moves, so every threshold is named rather than waited out: when a
// probe goes, what one answer or one silence changes, and what two silences in a row do.

/** A probe whose answers are handed out in order, recording when each was asked. */
function probing(...outcomes: readonly ProbeOutcome[]): { ask: () => Promise<ProbeOutcome>; asked: number[] } {
    const asked: number[] = [];

    return {
        asked,
        ask: () => {
            asked.push(Date.now());

            return Promise.resolve(outcomes[Math.min(asked.length - 1, outcomes.length - 1)] ?? 'answered');
        },
    };
}

describe('probeLiveness', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(0);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('asks nothing while no request is waiting', async () => {
        const inFlight = requestsInFlightOver(() => Date.now());
        const probe = probing('answered');

        probeLiveness(inFlight, probe.ask, () => undefined);
        await vi.advanceTimersByTimeAsync(10 * probeAfter);

        expect(probe.asked).toEqual([]);
    });

    it('asks once a request has waited the interval, and again every interval while it still waits', async () => {
        const inFlight = requestsInFlightOver(() => Date.now());
        const probe = probing('answered');

        probeLiveness(inFlight, probe.ask, () => undefined);
        inFlight.enter(false, false);

        await vi.advanceTimersByTimeAsync(probeAfter - 1);
        expect(probe.asked).toEqual([]);

        await vi.advanceTimersByTimeAsync(1);
        await vi.advanceTimersByTimeAsync(probeAfter);
        expect(probe.asked).toEqual([probeAfter, 2 * probeAfter]);
    });

    it('stops asking once the request that was waiting is answered', async () => {
        const inFlight = requestsInFlightOver(() => Date.now());
        const probe = probing('answered');

        probeLiveness(inFlight, probe.ask, () => undefined);
        const request = inFlight.enter(false, false);

        await vi.advanceTimersByTimeAsync(probeAfter / 2);
        request.left();
        await vi.advanceTimersByTimeAsync(5 * probeAfter);

        expect(probe.asked).toEqual([]);
    });

    it('asks at once when the signal channel drops', async () => {
        const inFlight = requestsInFlightOver(() => Date.now());
        const probe = probing('answered');
        const liveness = probeLiveness(inFlight, probe.ask, () => undefined);

        await vi.advanceTimersByTimeAsync(1_234);
        liveness.channelDropped();
        await vi.advanceTimersByTimeAsync(0);

        expect(probe.asked).toEqual([1_234]);
    });

    it('does not give up on one probe nobody answered, and asks again an interval later', async () => {
        const inFlight = requestsInFlightOver(() => Date.now());
        const probe = probing('missed', 'answered');
        const gaveUp = vi.fn();
        const liveness = probeLiveness(inFlight, probe.ask, gaveUp);

        liveness.channelDropped();
        await vi.advanceTimersByTimeAsync(probeAfter);
        await vi.advanceTimersByTimeAsync(5 * probeAfter);

        expect(probe.asked).toEqual([0, probeAfter]);
        expect(gaveUp).not.toHaveBeenCalled();
    });

    it('gives up after two probes in a row went unanswered, dated from the first', async () => {
        const inFlight = requestsInFlightOver(() => Date.now());
        const probe = probing('missed', 'missed');
        const gaveUp = vi.fn();

        probeLiveness(inFlight, probe.ask, gaveUp);
        inFlight.enter(true, false);

        await vi.advanceTimersByTimeAsync(probeAfter * missesBeforeLost);

        expect(probe.asked).toEqual([probeAfter, 2 * probeAfter]);
        expect(gaveUp).toHaveBeenCalledExactlyOnceWith(probeAfter);
    });

    it('asks nothing while the server is lost, and counts afresh once it is back', async () => {
        const inFlight = requestsInFlightOver(() => Date.now());
        const probe = probing('missed', 'missed', 'missed');
        const gaveUp = vi.fn();
        const liveness = probeLiveness(inFlight, probe.ask, gaveUp);

        inFlight.enter(false, false);
        await vi.advanceTimersByTimeAsync(2 * probeAfter);
        expect(gaveUp).toHaveBeenCalledTimes(1);

        liveness.lost(true);
        liveness.channelDropped();
        await vi.advanceTimersByTimeAsync(5 * probeAfter);
        expect(probe.asked).toHaveLength(2);

        liveness.lost(false);
        await vi.advanceTimersByTimeAsync(0);
        expect(probe.asked).toHaveLength(3);
        expect(gaveUp).toHaveBeenCalledTimes(1);
    });
});
