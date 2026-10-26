// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, render, screen } from '@testing-library/react';
import { useRef } from 'react';
import { describe, expect, it } from 'vitest';
import { SignalledChangesContext, type SignalListener, type SignalledChanges } from '../signals/signalledChanges';
import { ListPull } from './ListPull';
import { pullDisc } from './pullTravel';

/** Changes a test speaks for: a refresh is told to every listener, as the hook tells it, and each one asked is counted. */
function signalling(): { changes: SignalledChanges; refreshedElsewhere: () => void; asked: () => number } {
    const listeners = new Set<SignalListener>();
    let asked = 0;

    function tell(): void {
        for (const listener of [...listeners]) {
            listener({ kind: 'refresh' });
        }
    }

    return {
        changes: {
            listen: (listener) => {
                listeners.add(listener);

                return () => {
                    listeners.delete(listener);
                };
            },
            refresh: () => {
                asked += 1;
                tell();
            },
        },
        refreshedElsewhere: () => {
            act(tell);
        },
        asked: () => asked,
    };
}

function Pulled({ enabled }: { readonly enabled: boolean }) {
    const scroller = useRef<HTMLDivElement>(null);

    return (
        <ListPull scroller={scroller} enabled={enabled}>
            <div ref={scroller}>
                <ul role="listbox" />
            </div>
        </ListPull>
    );
}

/** The list, under a finger unless told otherwise, and scrolled this far from its first row. */
function drawing(changes: SignalledChanges, options: { enabled?: boolean; scrolled?: number } = {}): HTMLElement {
    render(
        <SignalledChangesContext value={changes}>
            <Pulled enabled={options.enabled ?? true} />
        </SignalledChangesContext>,
    );

    const list = screen.getByRole('listbox');

    Object.defineProperty(list.parentElement, 'scrollTop', { value: options.scrolled ?? 0 });

    return list;
}

/** Where the disc that follows the pull stands, as the offset it is drawn at: above the list while no pull holds it. */
function discStandsAt(list: HTMLElement): string {
    return (list.parentElement?.previousElementSibling as HTMLElement | null)?.style.translate ?? '';
}

/** One touch event at `x`, `y`, with `fingers` on the glass while it lands or moves. */
function touch(type: string, on: Element, x: number, y: number, fingers = 1): Event {
    const event = new Event(type, { bubbles: true, cancelable: true });
    const down = type === 'touchstart' || type === 'touchmove';

    Object.defineProperty(event, 'touches', {
        value: down ? Array.from({ length: fingers }, (_, finger) => ({ clientX: x + finger * 50, clientY: y })) : [],
    });
    act(() => {
        on.dispatchEvent(event);
    });

    return event;
}

/** A finger that lands, travels through each point, and lifts — or is taken by the platform, when `ending` says so. */
function drag(on: Element, points: readonly (readonly [number, number])[], ending = 'touchend'): readonly Event[] {
    const [first, ...rest] = points;

    if (first === undefined) {
        return [];
    }

    touch('touchstart', on, ...first);

    const moves = rest.map((point) => touch('touchmove', on, ...point));

    touch(ending, on, 0, 0);

    return moves;
}

describe('ListPull', () => {
    it('asks for one refresh when the list is pulled down from its first row past the distance', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes);

        drag(list, [
            [100, 100],
            [100, 120],
            [100, 240],
        ]);

        expect(deployment.asked()).toBe(1);
    });

    it('asks nothing when the finger is let go short of the distance', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes);

        drag(list, [
            [100, 100],
            [100, 120],
            [100, 220],
        ]);

        expect(deployment.asked()).toBe(0);
    });

    it('keeps the list from scrolling while the finger pulls it', () => {
        const list = drawing(signalling().changes);

        const moves = drag(list, [
            [100, 100],
            [100, 120],
            [100, 160],
        ]);

        expect(moves.map((move) => move.defaultPrevented)).toEqual([true, true]);
    });

    it('takes no pull from a list scrolled away from its first row', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes, { scrolled: 40 });

        const moves = drag(list, [
            [100, 100],
            [100, 120],
            [100, 300],
        ]);

        expect(deployment.asked()).toBe(0);
        expect(moves.some((move) => move.defaultPrevented)).toBe(false);
    });

    it('leaves a finger travelling up to the scroll for the rest of its touch', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes);

        drag(list, [
            [100, 300],
            [100, 280],
            [100, 500],
        ]);

        expect(deployment.asked()).toBe(0);
    });

    it("leaves a finger travelling more across than down to the row's swipe for the rest of its touch", () => {
        const deployment = signalling();
        const list = drawing(deployment.changes);

        drag(list, [
            [100, 100],
            [130, 110],
            [130, 300],
        ]);

        expect(deployment.asked()).toBe(0);
    });

    it('asks nothing for a pull while a refresh is already under way', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes);

        deployment.refreshedElsewhere();
        drag(list, [
            [100, 100],
            [100, 120],
            [100, 300],
        ]);

        expect(deployment.asked()).toBe(0);
    });

    it('asks nothing when the platform takes the touch away mid-pull', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes);

        drag(
            list,
            [
                [100, 100],
                [100, 120],
                [100, 300],
            ],
            'touchcancel',
        );

        expect(deployment.asked()).toBe(0);
    });

    it('lets a pull go, asking nothing, when a second finger lands on the list', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes);

        touch('touchstart', list, 100, 100);
        touch('touchmove', list, 100, 120);
        touch('touchmove', list, 100, 300);

        expect(discStandsAt(list)).not.toBe(`0 ${String(-pullDisc)}px`);

        touch('touchstart', list, 100, 300, 2);
        touch('touchend', list, 0, 0);

        expect(discStandsAt(list)).toBe(`0 ${String(-pullDisc)}px`);
        expect(deployment.asked()).toBe(0);
    });

    it('changes nothing about the list under a fine pointer', () => {
        const deployment = signalling();
        const list = drawing(deployment.changes, { enabled: false });

        const moves = drag(list, [
            [100, 100],
            [100, 120],
            [100, 300],
        ]);

        expect(deployment.asked()).toBe(0);
        expect(moves.some((move) => move.defaultPrevented)).toBe(false);
    });
});
