// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailFathomTransport,
    OwnPresence,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { nothingSignalled } from '../signals/signalledChanges';
import { ToastContext, type ToastsRaised } from '../toasts/useToasts';
import { StatusMenu } from './StatusMenu';
import { useOwnPresence } from './useOwnPresence';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Bearer presence' };

// The deployment as the menu reaches it: the presence it holds, and every choice written to it, which answers what the
// route answers — `Active` until changed is the member's clients deciding again, and anything else is held.
function deployment(
    holding: OwnPresence,
    choiceStatus = 200,
): { readonly transport: MailFathomTransport; readonly choices: unknown[] } {
    const choices: unknown[] = [];
    let held = holding;

    return {
        choices,
        transport: (request: ClientRequest) => {
            if (request.method === 'PUT') {
                const chosen = JSON.parse(request.body ?? '{}') as {
                    state: OwnPresence['state'];
                    duration: OwnPresence['choiceDuration'];
                };

                choices.push(chosen);

                if (choiceStatus !== 200) {
                    return Promise.resolve<ClientResponse>({ status: choiceStatus, body: '', headers: {} });
                }

                const automatic = chosen.state === 'Active' && chosen.duration === null;

                held = {
                    state: chosen.state,
                    chosenState: automatic ? null : chosen.state,
                    choiceDuration: chosen.duration,
                    choiceEndsAt: chosen.duration === null ? null : '2026-10-31T12:00:00Z',
                };
            }

            return Promise.resolve<ClientResponse>({ status: 200, body: JSON.stringify(held), headers: {} });
        },
    };
}

function Harness({ transport }: { readonly transport: MailFathomTransport }) {
    const own = useOwnPresence(session, transport, nothingSignalled);

    return own === null ? null : <StatusMenu own={own} displayName="Ada Lovelace" picture={null} />;
}

function renderMenu(holding: OwnPresence, choiceStatus = 200) {
    const reached = deployment(holding, choiceStatus);
    const toasts: ToastsRaised = { raise: vi.fn(), raiseOperation: () => () => undefined };

    render(
        <LocalizationProvider>
            <ToastContext value={toasts}>
                <Harness transport={reached.transport} />
            </ToastContext>
        </LocalizationProvider>,
    );

    return { ...reached, toasts };
}

// jsdom shows no popover at all, so the menu is drawn the one way a document can carry the platform showing it — as
// `AccountMenu.test.tsx` draws its own — once the presence it is drawn from has been read.
async function theMenuShown(): Promise<HTMLElement> {
    const menu = await waitFor(() => {
        const drawn = document.getElementById('status-menu');

        if (drawn === null) {
            throw new Error('The menu was not drawn.');
        }

        return drawn;
    });

    menu.style.display = 'block';

    return menu;
}

function state(name: string): HTMLElement {
    return screen.getByRole('menuitemradio', { name: new RegExp(`^${name}`, 'u') });
}

const automatic: OwnPresence = { state: 'Active', chosenState: null, choiceDuration: null, choiceEndsAt: null };

describe('StatusMenu', () => {
    it('offers the six states as a menu’s radios, the one in force checked', async () => {
        renderMenu({ state: 'Busy', chosenState: 'Busy', choiceDuration: null, choiceEndsAt: null });
        await theMenuShown();

        expect(screen.getAllByRole('menuitemradio').map((row) => row.textContent)).toEqual([
            'Active',
            'Away',
            'Busy',
            'Do not disturb',
            'Offline',
            'HiddenOthers see you as Offline',
        ]);
        expect(state('Busy').getAttribute('aria-checked')).toBe('true');
        expect(state('Away').getAttribute('aria-checked')).toBe('false');
    });

    it('takes a picked state until changed, sending no while, and closes as any menu’s item does', async () => {
        const { choices } = renderMenu(automatic);
        const menu = await theMenuShown();
        const closed = vi.fn();

        Object.defineProperty(menu, 'hidePopover', { configurable: true, value: closed });
        fireEvent.click(state('Away'));

        await waitFor(() => {
            expect(state('Away').getAttribute('aria-checked')).toBe('true');
        });
        expect(choices).toEqual([{ state: 'Away', duration: null }]);
        expect(closed).toHaveBeenCalledOnce();
    });

    it('returns the member to their clients’ presence when Active is picked until changed', async () => {
        const { choices } = renderMenu({
            state: 'Busy',
            chosenState: 'Busy',
            choiceDuration: '1h',
            choiceEndsAt: '2026-10-31T12:00:00Z',
        });
        await theMenuShown();

        fireEvent.click(state('Active'));

        await waitFor(() => {
            expect(screen.getByRole('menuitem', { name: /Until changed/u })).toBeDefined();
        });
        expect(choices).toEqual([{ state: 'Active', duration: null }]);
    });

    it('names the while the deployment holds, whichever client chose it', async () => {
        renderMenu({ state: 'Busy', chosenState: 'Busy', choiceDuration: '1h', choiceEndsAt: '2026-10-31T12:00:00Z' });
        await theMenuShown();

        expect(screen.getByRole('menuitem', { name: /Busy · 1 hour/u })).toBeDefined();
    });

    it('opens Duration on the state in force and on Today, and takes both together on Done', async () => {
        const { choices } = renderMenu(automatic);
        await theMenuShown();

        fireEvent.click(screen.getByRole('menuitem', { name: /Duration/u }));

        const field = screen.getByRole('combobox', { name: 'Status' });
        const after = screen.getByRole<HTMLSelectElement>('combobox', { name: 'Reset status after' });

        expect(field.textContent).toBe('Active');
        expect(after.value).toBe('today');

        fireEvent.click(field);
        fireEvent.click(screen.getByRole('option', { name: 'Do not disturb' }));
        fireEvent.change(after, { target: { value: '2h' } });
        fireEvent.click(screen.getByRole('button', { name: 'Done' }));

        expect(await screen.findByRole('menuitem', { name: /Do not disturb · 2 hours/u })).toBeDefined();
        expect(choices).toEqual([{ state: 'DoNotDisturb', duration: '2h' }]);
    });

    it('goes back from Duration without taking anything', async () => {
        const { choices } = renderMenu(automatic);
        await theMenuShown();

        fireEvent.click(screen.getByRole('menuitem', { name: /Duration/u }));
        fireEvent.click(screen.getByRole('button', { name: 'Back' }));

        expect(await screen.findByRole('menu', { name: 'Your status' })).toBeDefined();
        expect(choices).toEqual([]);
    });

    it('says the server did not answer where it did not, and offers to choose again', async () => {
        const { toasts, choices } = renderMenu(automatic, 503);
        await theMenuShown();

        fireEvent.click(state('Busy'));

        await waitFor(() => {
            expect(toasts.raise).toHaveBeenCalledOnce();
        });

        const [raised] = vi.mocked(toasts.raise).mock.calls[0] ?? [];

        expect([raised?.kind, raised?.title, raised?.body, raised?.action?.label]).toEqual([
            'error',
            'Your status could not be changed.',
            'The server did not answer.',
            'Try again',
        ]);

        raised?.action?.take();

        await waitFor(() => {
            expect(choices).toEqual([
                { state: 'Busy', duration: null },
                { state: 'Busy', duration: null },
            ]);
        });
    });

    it('says the credential may not change the status where the grant is missing, with nothing to try again', async () => {
        const { toasts } = renderMenu(automatic, 403);
        await theMenuShown();

        fireEvent.click(state('Busy'));

        await waitFor(() => {
            expect(toasts.raise).toHaveBeenCalledWith(
                {
                    kind: 'error',
                    title: 'Your status could not be changed.',
                    body: 'This credential may not change your status.',
                },
                expect.any(Number),
            );
        });
    });
});
