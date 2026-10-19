// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useContext } from 'react';
import { act, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Departing } from './Departing';
import { DepartureContext } from './departure';

// jsdom runs no animation and reports none, so the platform's report is stood in for here: before the module under test
// is loaded, because whether animations can be reported at all is read once, as it loads. Each test says what is
// running on the surface when it starts to leave, and settles it when the departure is meant to be over.
const platform = vi.hoisted(() => {
    const reported: { running: Animation[] } = { running: [] };

    Element.prototype.getAnimations = () => reported.running;

    return reported;
});

afterEach(() => {
    platform.running = [];
});

/** An animation running on the surface, finishing when the test says so, and lasting as long as `endTime`. */
function running(endTime = 200): { readonly animation: Animation; readonly finish: () => Promise<void> } {
    let finished: () => void = () => undefined;
    const done = new Promise<void>((resolve) => {
        finished = resolve;
    });

    const animation = {
        finished: done,
        effect: { getComputedTiming: () => ({ endTime }) },
    } as unknown as Animation;

    return {
        animation,
        finish: async () => {
            finished();
            await act(async () => {
                await done;
            });
        },
    };
}

// What a surface inside one draws: the value it was drawn from, carrying whether it was told it is leaving.
function Surface({ named }: { readonly named: string }) {
    const leaving = useContext(DepartureContext);

    return <p data-leaving={leaving}>{named}</p>;
}

function drawn(value: string | null) {
    return <Departing value={value}>{(named) => <Surface named={named} />}</Departing>;
}

describe('Departing', () => {
    it('keeps the surface on the screen, told it is leaving and answering nothing, while its departure runs', () => {
        const { rerender } = render(drawn('menu'));
        platform.running = [running().animation];

        rerender(drawn(null));

        const surface = screen.getByText('menu');
        expect(surface.dataset['leaving']).toBe('true');
        expect(surface.closest('[inert]')).not.toBeNull();
    });

    it('takes the surface away once its departure has finished', async () => {
        const { rerender } = render(drawn('menu'));
        const departure = running();
        platform.running = [departure.animation];

        rerender(drawn(null));
        await departure.finish();

        await waitFor(() => {
            expect(screen.queryByText('menu')).toBeNull();
        });
    });

    it('takes the surface away at once where nothing on it is moving', async () => {
        const { rerender } = render(drawn('menu'));

        rerender(drawn(null));

        await waitFor(() => {
            expect(screen.queryByText('menu')).toBeNull();
        });
    });

    it('does not wait on something inside the surface that never stops moving', async () => {
        const { rerender } = render(drawn('menu'));
        platform.running = [running(Infinity).animation];

        rerender(drawn(null));

        await waitFor(() => {
            expect(screen.queryByText('menu')).toBeNull();
        });
    });

    it('stands again rather than going where it is asked for once more while it leaves', async () => {
        const { rerender } = render(drawn('menu'));
        const departure = running();
        platform.running = [departure.animation];

        rerender(drawn(null));
        rerender(drawn('another menu'));
        await departure.finish();

        expect(screen.getByText('another menu').dataset['leaving']).toBe('false');
    });
});
