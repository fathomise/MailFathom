// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { requestsInFlightOver, type EnteredRequest, type RequestsInFlight } from '../deployment/requestsInFlight';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from './Toasts';
import { stillSendingAfter } from './useStillSending';

// *Still sending…* as the surface says it: a write with no answer after ten seconds, and nothing for a read however long
// it takes. The clock is fake, so the threshold is named rather than waited out.

function surfaceOver(inFlight: RequestsInFlight): void {
    render(
        <LocalizationProvider>
            <ToastsProvider inFlight={inFlight}>{null}</ToastsProvider>
        </LocalizationProvider>,
    );
}

/** A write entered as the transport enters one, inside the act its listeners render in. */
function enterWrite(inFlight: RequestsInFlight): EnteredRequest {
    const entered: EnteredRequest[] = [];

    act(() => {
        entered.push(inFlight.enter(true, false));
    });

    const [write] = entered;

    if (write === undefined) {
        throw new Error('The write was not entered.');
    }

    return write;
}

function pass(milliseconds: number): void {
    act(() => {
        vi.advanceTimersByTime(milliseconds);
    });
}

describe('Still sending', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('says a write is still waiting once it has had no answer for ten seconds, until it is answered', () => {
        const inFlight = requestsInFlightOver(() => Date.now());

        surfaceOver(inFlight);
        act(() => {
            inFlight.enter(true, false);
        });

        pass(stillSendingAfter - 1);
        expect(screen.queryByText('Still sending…')).toBeNull();

        pass(1);
        expect(screen.getByText('Still sending…')).toBeTruthy();
    });

    it('goes once every write that was waiting has been answered or ended', () => {
        const inFlight = requestsInFlightOver(() => Date.now());

        surfaceOver(inFlight);

        const write = enterWrite(inFlight);

        pass(stillSendingAfter);
        expect(screen.getByText('Still sending…')).toBeTruthy();

        act(() => {
            write.left();
        });
        expect(screen.queryByText('Still sending…')).toBeNull();
    });

    it('says nothing about a read, however long it takes', () => {
        const inFlight = requestsInFlightOver(() => Date.now());

        surfaceOver(inFlight);
        act(() => {
            inFlight.enter(false, false);
        });
        pass(10 * stillSendingAfter);

        expect(screen.queryByText('Still sending…')).toBeNull();
    });

    it('stays closed once closed, until the next wait', () => {
        const inFlight = requestsInFlightOver(() => Date.now());

        surfaceOver(inFlight);

        const first = enterWrite(inFlight);

        pass(stillSendingAfter);
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        expect(screen.queryByText('Still sending…')).toBeNull();

        act(() => {
            first.left();
        });
        enterWrite(inFlight);
        pass(stillSendingAfter);
        expect(screen.getByText('Still sending…')).toBeTruthy();
    });
});
