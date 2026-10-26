// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { SignalledChangesContext, type SignalListener } from '../signals/signalledChanges';
import * as drafts from '../../../../tests/fixtures/drafts';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { DraftsOutOfStep } from './DraftsOutOfStep';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function drawing(answer: unknown): Composing {
    const composing: Composing = { offered: true, drafts: false, opening: null, compose: vi.fn(), close: vi.fn() };
    const transport: MailFathomTransport = () =>
        Promise.resolve({ status: 200, body: JSON.stringify(answer), headers: {} });

    render(
        <LocalizationProvider>
            <ComposingContext value={composing}>
                <DraftsOutOfStep session={session} transport={transport} accounts={['work']} />
            </ComposingContext>
        </LocalizationProvider>,
    );

    return composing;
}

describe('DraftsOutOfStep', () => {
    it('lists only the drafts whose server copy is out of step, and opens one in the composer as itself', async () => {
        const composing = drawing(drafts.draftList);

        fireEvent.click(await screen.findByRole('button', { name: new RegExp(drafts.outOfStepDraft.subject, 'u') }));

        expect(screen.getByRole('region', { name: 'Out of step with your server' })).toBeDefined();
        expect(screen.getAllByRole('button')).toHaveLength(1);
        expect(composing.compose).toHaveBeenCalledExactlyOnceWith({
            kind: 'held',
            draftId: drafts.outOfStepDraft.draftId,
        });
    });

    it('draws nothing where every draft is in step with its server', async () => {
        drawing({ drafts: [drafts.draft], maximumCount: 200 });

        // The answer is read and drawn as nothing; letting the read settle is what makes the absence mean that.
        await act(async () => {
            await Promise.resolve();
        });

        expect(screen.queryByRole('region', { name: 'Out of step with your server' })).toBeNull();
    });
});

describe('DraftsOutOfStep, read again', () => {
    const composing: Composing = { offered: true, drafts: false, opening: null, compose: vi.fn(), close: vi.fn() };

    it('reads the drafts again when the deployment says something changed, and draws what it answers then', async () => {
        const listeners: SignalListener[] = [];
        const asked: string[] = [];
        let answer: unknown = { drafts: [drafts.draft], maximumCount: 200 };
        const transport: MailFathomTransport = (request) => {
            asked.push(request.path);

            return Promise.resolve({ status: 200, body: JSON.stringify(answer), headers: {} });
        };

        render(
            <LocalizationProvider>
                <SignalledChangesContext
                    value={{
                        listen: (listener) => {
                            listeners.push(listener);

                            return () => undefined;
                        },
                        refresh: () => undefined,
                    }}
                >
                    <ComposingContext value={composing}>
                        <DraftsOutOfStep session={session} transport={transport} accounts={['work']} />
                    </ComposingContext>
                </SignalledChangesContext>
            </LocalizationProvider>,
        );

        await waitFor(() => {
            expect(asked).toHaveLength(1);
        });

        answer = drafts.draftList;
        act(() => {
            for (const listener of listeners) {
                listener({ kind: 'refresh' });
            }
        });

        expect(await screen.findByRole('region', { name: 'Out of step with your server' })).toBeDefined();
    });

    it('keeps the drafts it drew when a read again fails, rather than taking the group away', async () => {
        const listeners: SignalListener[] = [];
        const asked: string[] = [];
        let failing = false;
        const transport: MailFathomTransport = (request) => {
            asked.push(request.path);

            return Promise.resolve(
                failing
                    ? { status: 503, body: '', headers: {} }
                    : { status: 200, body: JSON.stringify(drafts.draftList), headers: {} },
            );
        };

        render(
            <LocalizationProvider>
                <SignalledChangesContext
                    value={{
                        listen: (listener) => {
                            listeners.push(listener);

                            return () => undefined;
                        },
                        refresh: () => undefined,
                    }}
                >
                    <ComposingContext value={composing}>
                        <DraftsOutOfStep session={session} transport={transport} accounts={['work']} />
                    </ComposingContext>
                </SignalledChangesContext>
            </LocalizationProvider>,
        );

        await screen.findByRole('region', { name: 'Out of step with your server' });

        failing = true;
        act(() => {
            for (const listener of listeners) {
                listener({ kind: 'refresh' });
            }
        });

        await waitFor(() => {
            expect(asked).toHaveLength(2);
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(screen.getByRole('button', { name: new RegExp(drafts.outOfStepDraft.subject, 'u') })).toBeDefined();
    });

    it('draws none of one account’s drafts once another account’s Drafts folder is open and not yet read', async () => {
        const transport: MailFathomTransport = (request) =>
            request.path.endsWith('account=work')
                ? Promise.resolve({ status: 200, body: JSON.stringify(drafts.draftList), headers: {} })
                : new Promise(() => undefined);

        function Drawn({ accounts }: { readonly accounts: readonly string[] }) {
            return (
                <LocalizationProvider>
                    <ComposingContext value={composing}>
                        <DraftsOutOfStep session={session} transport={transport} accounts={accounts} />
                    </ComposingContext>
                </LocalizationProvider>
            );
        }

        const view = render(<Drawn accounts={['work']} />);

        await screen.findByRole('region', { name: 'Out of step with your server' });
        view.rerender(<Drawn accounts={['home']} />);

        expect(screen.queryByRole('region', { name: 'Out of step with your server' })).toBeNull();
    });
});
