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

// The deployment as the menu reaches it: the presence it holds, and every write made to it, which answers what each
// route answers — `Active` until changed is the member's clients deciding again, a blank message is none, and anything
// else is held.
function deployment(
    holding: OwnPresence,
    choiceStatus = 200,
): {
    readonly transport: MailFathomTransport;
    readonly choices: unknown[];
    readonly messages: unknown[];
    readonly workModes: unknown[];
} {
    const choices: unknown[] = [];
    const messages: unknown[] = [];
    const workModes: unknown[] = [];
    let held = holding;

    return {
        choices,
        messages,
        workModes,
        transport: (request: ClientRequest) => {
            if (request.method === 'PUT' && choiceStatus !== 200) {
                choices.push(JSON.parse(request.body ?? '{}'));

                return Promise.resolve<ClientResponse>({ status: choiceStatus, body: '', headers: {} });
            }

            if (request.method === 'PUT' && request.path.endsWith('/status-message')) {
                const written = JSON.parse(request.body ?? '{}') as {
                    text: string;
                    clears: OwnPresence['choiceDuration'];
                };

                messages.push(written);
                held = {
                    ...held,
                    statusMessage:
                        written.text === ''
                            ? null
                            : {
                                  text: written.text,
                                  clears: written.clears,
                                  clearsAt: written.clears === null ? null : '2026-10-31T23:00:00Z',
                              },
                };
            } else if (request.method === 'PUT' && request.path.endsWith('/work-mode')) {
                const chosen = JSON.parse(request.body ?? '{}') as { mode: 'Office' | 'Remote'; today: boolean };

                workModes.push(chosen);
                held = {
                    ...held,
                    workMode: { mode: chosen.mode, endsAt: chosen.today ? '2026-10-31T23:00:00Z' : null },
                };
            } else if (request.method === 'PUT') {
                const chosen = JSON.parse(request.body ?? '{}') as {
                    state: OwnPresence['state'];
                    duration: OwnPresence['choiceDuration'];
                };

                choices.push(chosen);

                const automatic = chosen.state === 'Active' && chosen.duration === null;

                held = {
                    ...held,
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

// Nothing said and no place of work stated, which is how a member's presence starts.
const nothingSaid = { statusMessage: null, workMode: null } as const;

const automatic: OwnPresence = {
    state: 'Active',
    chosenState: null,
    choiceDuration: null,
    choiceEndsAt: null,
    ...nothingSaid,
};

describe('StatusMenu', () => {
    it('offers the six states as a menu’s radios, the one in force checked', async () => {
        renderMenu({ state: 'Busy', chosenState: 'Busy', choiceDuration: null, choiceEndsAt: null, ...nothingSaid });
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
            ...nothingSaid,
        });
        await theMenuShown();

        fireEvent.click(state('Active'));

        await waitFor(() => {
            expect(screen.getByRole('menuitem', { name: /Until changed/u })).toBeDefined();
        });
        expect(choices).toEqual([{ state: 'Active', duration: null }]);
    });

    it('names the while the deployment holds, whichever client chose it', async () => {
        renderMenu({
            state: 'Busy',
            chosenState: 'Busy',
            choiceDuration: '1h',
            choiceEndsAt: '2026-10-31T12:00:00Z',
            ...nothingSaid,
        });
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

    it('shows the message and the place of work on the first page, faint where neither is stated', async () => {
        renderMenu({
            ...automatic,
            statusMessage: { text: 'In workshops until 15:00', clears: 'today', clearsAt: '2026-10-31T23:00:00Z' },
        });
        await theMenuShown();

        expect(screen.getByRole('menuitem', { name: /Message.*In workshops until 15:00/u })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: /Working from.*Not stated/u })).toBeDefined();
    });

    it('writes what was typed as one line of text and when it clears once the message page is left, and nothing where nothing changed', async () => {
        const { messages } = renderMenu(automatic);
        await theMenuShown();

        fireEvent.click(screen.getByRole('menuitem', { name: /Message/u }));
        fireEvent.click(screen.getByRole('button', { name: 'Back' }));
        await screen.findByRole('menu', { name: 'Your status' });

        expect(messages).toEqual([]);

        fireEvent.click(screen.getByRole('menuitem', { name: /Message/u }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Status message' }), {
            target: { value: 'Back\n at‮ 14:00' },
        });
        fireEvent.click(screen.getByRole('button', { name: '1 hour' }));
        fireEvent.keyDown(screen.getByRole('textbox', { name: 'Status message' }), { key: 'Enter' });

        expect(await screen.findByRole('menuitem', { name: /Message.*Back at 14:00/u })).toBeDefined();
        expect(messages).toEqual([{ text: 'Back at 14:00', clears: '1h' }]);
    });

    it('counts the message against its bound and takes no more than the bound', async () => {
        renderMenu(automatic);
        await theMenuShown();

        fireEvent.click(screen.getByRole('menuitem', { name: /Message/u }));

        const field = screen.getByRole<HTMLTextAreaElement>('textbox', { name: 'Status message' });

        fireEvent.change(field, { target: { value: 'On call' } });

        expect(field.maxLength).toBe(280);
        expect(screen.getByText('7 / 280')).toBeDefined();
    });

    it('clears a held message, sending a blank one', async () => {
        const { messages } = renderMenu({
            ...automatic,
            statusMessage: { text: 'On call', clears: null, clearsAt: null },
        });
        await theMenuShown();

        fireEvent.click(screen.getByRole('menuitem', { name: /Message/u }));
        fireEvent.click(screen.getByRole('button', { name: 'Clear status message' }));

        expect(await screen.findByRole('menuitem', { name: /Message.*Set a status message/u })).toBeDefined();
        expect(messages).toEqual([{ text: '', clears: null }]);
    });

    it('offers four places of work, the one in force checked, and takes a picked one for today', async () => {
        const { workModes } = renderMenu({ ...automatic, workMode: { mode: 'Remote', endsAt: null } });
        await theMenuShown();

        fireEvent.click(screen.getByRole('menuitem', { name: /Working from/u }));

        expect(screen.getByRole('dialog', { name: 'Working from' })).toBeDefined();
        const offered = screen.getByRole('radiogroup', { name: 'Working from' });

        expect(Array.from(offered.querySelectorAll('[role="radio"]'), (row) => row.textContent)).toEqual([
            'Office today',
            'Office until changed',
            'Remote today',
            'Remote until changed',
        ]);
        expect(screen.getByRole('radio', { name: 'Remote until changed' }).getAttribute('aria-checked')).toBe('true');

        fireEvent.click(screen.getByRole('radio', { name: 'Office today' }));

        expect(await screen.findByRole('menuitem', { name: /Working from.*Office today/u })).toBeDefined();
        expect(workModes).toEqual([{ mode: 'Office', today: true }]);
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
