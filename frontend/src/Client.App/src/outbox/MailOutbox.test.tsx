// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mailFolders, workAccount } from '../../../../tests/fixtures/deployment';
import * as outbox from '../../../../tests/fixtures/outbox';
import { LocalizationProvider } from '../localization/Localization';
import { SignalledChangesContext, type SignalListener } from '../signals/signalledChanges';
import { ToastsProvider } from '../toasts/Toasts';
import { useWorkspace } from '../workspace/useWorkspace';
import { WorkspaceProvider } from '../workspace/Workspace';
import { MailOutboxProvider } from './MailOutbox';
import { OutboxList } from './OutboxList';
import { OutboxPane } from './OutboxPane';

// The Outbox as the frame draws it: one reader of the page, the list in the list's place and the preview in the pane's,
// both acting through the provider's decisions. They are proven together because that is what they are — a send
// cancelled in the preview is one the list no longer holds, and neither half could show it alone.

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const sent = { kind: 'folder', accountId: workAccount.id, folderId: 'SENT' } as const;
const inbox = { kind: 'folder', accountId: workAccount.id, folderId: 'INBOX' } as const;
const personalAccountId = 'personal';
const anotherOutboxName = 'Open another account’s Outbox';
const workOutboxName = 'Open the work account’s Outbox';

// What stands in for the Sent folder's list and the mark on its row, which the test names rather than the client.
const sentFolderName = 'The Sent folder';
const anotherFolderName = 'Another folder';
const openingName = 'Open the Outbox';

type Send = (typeof outbox.outboxPage.sends)[number];

/** A deployment holding the corpus Outbox, applying what each decision does to it, and recording every request. */
function deployment(sends: readonly Send[] = outbox.outboxPage.sends, refusing = false) {
    const requests: ClientRequest[] = [];
    let held = sends;
    let pagesHeldBack = false;
    let decided: { readonly outcome: string } | { readonly status: number } = { outcome: 'Accepted' };
    let previewsRefused = false;
    let decisionsHeldBack: (() => void)[] | null = null;

    const transport: MailFathomTransport = (request) => {
        requests.push(request);

        if (refusing) {
            return Promise.resolve({ status: 503, body: '', headers: {} });
        }

        const path = new URL(request.path).pathname.replace('/api/client', '');
        const named = (JSON.parse(request.body ?? '{}') as { outgoingEmail?: string }).outgoingEmail;

        if (request.method === 'POST') {
            const decide = () => {
                if ('status' in decided) {
                    return Promise.resolve({ status: decided.status, body: '', headers: {} });
                }

                if (decided.outcome === 'Accepted' && path !== '/outbox/requeue') {
                    held = held.filter((send) => send.outgoingEmail !== named);
                }

                return answering({ outgoingEmail: named, outcome: decided.outcome });
            };
            const waiting = decisionsHeldBack;

            return waiting === null
                ? decide()
                : new Promise<ClientResponse>((resolve) => {
                      waiting.push(() => {
                          resolve(decide());
                      });
                  });
        }

        if (path === '/folders') {
            return answering(mailFolders);
        }

        if (path === '/outbox') {
            const ownSends = new URL(request.path).searchParams.get('account') === workAccount.id ? held : [];

            return pagesHeldBack
                ? new Promise<never>(() => undefined)
                : answering({ sends: ownSends, nextCursor: null });
        }

        const send = held.find(({ outgoingEmail }) => path === `/outbox/${outgoingEmail}`);

        if (send !== undefined && previewsRefused) {
            return Promise.resolve({ status: 503, body: '', headers: {} });
        }

        return send === undefined
            ? Promise.resolve({ status: 404, body: '', headers: {} })
            : answering(outbox.outgoingMailPreview(send));
    };

    return {
        requests,
        transport,
        posted: (route: string) =>
            requests
                .filter((request) => request.method === 'POST' && request.path.endsWith(route))
                .map((request) => JSON.parse(request.body ?? '{}') as unknown),
        pageReads: () => requests.filter((request) => new URL(request.path).pathname.endsWith('/outbox')).length,
        leaves: (outgoingEmail: string) => {
            held = held.filter((send) => send.outgoingEmail !== outgoingEmail);
        },
        holdPagesBack: () => {
            pagesHeldBack = true;
        },
        refusePreviews: (refused: boolean) => {
            previewsRefused = refused;
        },
        answerDecisionsWith: (answer: { readonly outcome: string } | { readonly status: number }) => {
            decided = answer;
        },
        holdDecisionsBack: () => {
            decisionsHeldBack = [];
        },
        releaseDecisions: () => {
            const waiting = decisionsHeldBack ?? [];

            decisionsHeldBack = null;
            waiting.forEach((answer) => {
                answer();
            });
        },
    };
}

function answering(body: unknown) {
    return Promise.resolve({ status: 200, body: JSON.stringify(body), headers: {} });
}

/**
 * The two surfaces while an Outbox is open, and the Sent folder it is opened on — by the press its mark answers — while
 * it is not.
 */
function Surfaces({
    transport,
    entry,
    openedOn,
}: {
    readonly transport: MailFathomTransport;
    readonly entry: string | null;
    readonly openedOn: typeof sent | typeof inbox;
}) {
    const { workspace, revise } = useWorkspace();

    if (workspace.outbox === null) {
        const onSent = workspace.scope.kind === 'folder' && workspace.scope.folderId === sent.folderId;

        return (
            <section aria-label={onSent ? sentFolderName : anotherFolderName}>
                <button
                    type="button"
                    aria-label={openingName}
                    onClick={() => {
                        revise({ scope: openedOn, outbox: { account: workAccount.id, entry } });
                    }}
                />
            </section>
        );
    }

    return (
        <>
            <button
                type="button"
                aria-label={anotherOutboxName}
                onClick={() => {
                    revise({ outbox: { account: personalAccountId, entry: null } });
                }}
            />
            <button
                type="button"
                aria-label={workOutboxName}
                onClick={() => {
                    revise({ outbox: { account: workAccount.id, entry: null } });
                }}
            />
            <OutboxList outbox={workspace.outbox} />
            <OutboxPane session={session} transport={transport} outbox={workspace.outbox} />
        </>
    );
}

function drawing(
    transport: MailFathomTransport,
    {
        entry = null,
        online = true,
        openedOn = sent,
    }: { entry?: string | null; online?: boolean; openedOn?: typeof sent | typeof inbox } = {},
) {
    const listeners: SignalListener[] = [];

    render(
        <LocalizationProvider>
            <ToastsProvider>
                <SignalledChangesContext
                    value={{
                        listen: (listener) => {
                            listeners.push(listener);

                            return () => {
                                listeners.splice(listeners.indexOf(listener), 1);
                            };
                        },
                        refresh: () => undefined,
                    }}
                >
                    <WorkspaceProvider>
                        <MailOutboxProvider session={session} transport={transport} online={online}>
                            <Surfaces transport={transport} entry={entry} openedOn={openedOn} />
                        </MailOutboxProvider>
                    </WorkspaceProvider>
                </SignalledChangesContext>
            </ToastsProvider>
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: openingName }));

    return {
        signal: (account: string) => {
            for (const listener of [...listeners]) {
                listener({ kind: 'outbox.changed', account });
            }
        },
    };
}

async function listed(): Promise<HTMLElement> {
    return screen.findByRole('list', { name: 'Outbox' });
}

describe('the Outbox list', () => {
    it('lists every send not yet gone, newest first, each with whom it is for and where it stands', async () => {
        drawing(deployment().transport);

        const rows = within(await listed()).getAllByRole('button');

        expect(rows.map((row) => row.textContent)).toStrictEqual([
            expect.stringMatching(/^Tot\.bak@fabrikam\.example.*Re: UAT testing dateWaiting to send$/u),
            expect.stringMatching(/^Tom\.nowak@company\.example.*Budget line for Q4 renewalsSending now$/u),
            expect.stringMatching(
                /^Top\.zielinski@company\.example \+1.*Re: rollout scheduleTrying again at .* · 2 attempts$/u,
            ),
            expect.stringMatching(/Re: proposed terms for 2027Outcome unknown · 1 attempt$/u),
            expect.stringMatching(/Re: Contract addendum - signaturesRefused by the server · 1 attempt$/u),
        ]);
        expect(screen.getByText(/^Read-only\. What has not left yet waits here/u)).toBeDefined();
    });

    it('offers no mail act: nothing to select, no menu, and no act on a row', async () => {
        drawing(deployment().transport);

        const list = await listed();

        fireEvent.contextMenu(within(list).getAllByRole('button')[0] ?? list);

        expect(screen.queryByRole('checkbox')).toBeNull();
        expect(screen.queryByRole('menu')).toBeNull();
        expect(screen.queryByRole('button', { name: /^(Delete|Archive|Move|Reply|Forward|Flag)/u })).toBeNull();
    });

    it('says the Outbox is empty and leads back to the Sent folder it was opened on', async () => {
        drawing(deployment([]).transport);

        expect(await screen.findByText('Outbox is empty')).toBeDefined();
        expect(
            screen.getByText('Outbox is empty, so there is nothing to read here. What went out is in Sent.'),
        ).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Go to Sent' }));

        expect(await screen.findByRole('region', { name: sentFolderName })).toBeDefined();
    });

    // An Outbox opened from a send toast stands over whatever was listed until its Sent folder is known, so what is
    // behind it is no answer to where *Go to Sent* goes.
    it('leads to the account’s Sent folder although it was opened over another one', async () => {
        drawing(deployment([]).transport, { openedOn: inbox });

        fireEvent.click(await screen.findByRole('button', { name: 'Go to Sent' }));

        expect(await screen.findByRole('region', { name: sentFolderName })).toBeDefined();
    });

    it('says why it could not be read and reads it again on a retry', async () => {
        const failing = deployment(outbox.outboxPage.sends, true);

        drawing(failing.transport);

        expect(await screen.findByText('The Outbox could not be read: unavailable.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        await waitFor(() => {
            expect(failing.pageReads()).toBe(2);
        });
    });

    it('says the machine is offline rather than reading an Outbox it cannot reach', () => {
        const offline = deployment();

        drawing(offline.transport, { online: false });

        expect(screen.getByText(/^This machine is offline\./u)).toBeDefined();
        expect(offline.requests).toStrictEqual([]);
    });

    it('says once that a send being sent has left, when the Outbox no longer holds it', async () => {
        const held = deployment();
        const { signal } = drawing(held.transport);

        await listed();
        held.leaves(outbox.sendingSend.outgoingEmail);
        held.leaves(outbox.waitingSend.outgoingEmail);
        signal(workAccount.id);

        expect(await screen.findByText('“Budget line for Q4 renewals” is in Sent now.')).toBeDefined();
        expect(screen.queryByText('“Re: UAT testing date” is in Sent now.')).toBeNull();
    });

    it('reads the page again when the deployment says this account’s Outbox moved, and only then', async () => {
        const held = deployment();
        const { signal } = drawing(held.transport);

        await listed();
        await act(async () => {
            signal('another-account');
            await Promise.resolve();
        });
        expect(held.pageReads()).toBe(1);

        signal(workAccount.id);

        await waitFor(() => {
            expect(held.pageReads()).toBe(2);
        });
    });
});

describe('the Outbox preview', () => {
    it('opens the send a row names, with whom it is from and for and the message itself', async () => {
        drawing(deployment().transport);

        fireEvent.click(within(await listed()).getByRole('button', { name: /Re: UAT testing date/u }));

        expect(await screen.findByRole('heading', { name: 'Re: UAT testing date' })).toBeDefined();
        expect(screen.getByText('k.kowalska@nordwind.example')).toBeDefined();
        expect(screen.getByText('The dates you suggested work for us.')).toBeDefined();
        expect(screen.getByText(/^In Outbox, read-only/u)).toBeDefined();
        expect(screen.getByRole('button', { name: /Re: UAT testing date/u }).getAttribute('aria-current')).toBe('true');
    });

    it('withdraws a waiting send, which leaves the list, and says to whom nothing was sent', async () => {
        const held = deployment();

        drawing(held.transport, { entry: outbox.waitingSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Cancel sending' }));

        expect(await screen.findByText('Nothing was sent to t.bak@fabrikam.example.')).toBeDefined();
        expect(held.posted('/outbox/cancellation')).toStrictEqual([
            { outgoingEmail: outbox.waitingSend.outgoingEmail },
        ]);
        await waitFor(() => {
            expect(within(screen.getByRole('list', { name: 'Outbox' })).queryByText('Re: UAT testing date')).toBeNull();
        });
    });

    // A second press before the first is answered would ask again, and the deployment would answer the repeat as a
    // decision it could not take beside the first one's success.
    it('holds a send’s decisions while one is unanswered, says what is under way, and asks once', async () => {
        const held = deployment();

        held.holdDecisionsBack();
        drawing(held.transport, { entry: outbox.waitingSend.outgoingEmail });

        const withdrawing = await screen.findByRole('button', { name: 'Cancel sending' });

        fireEvent.click(withdrawing);

        expect(await screen.findByText('Cancelling sending…')).toBeDefined();
        expect(withdrawing.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(withdrawing);
        held.releaseDecisions();

        expect(await screen.findByText('Nothing was sent to t.bak@fabrikam.example.')).toBeDefined();
        expect(held.posted('/outbox/cancellation')).toHaveLength(1);
        expect(screen.queryByText('Nothing changed')).toBeNull();
    });

    // A decision the deployment did not take changes nothing, so the send stays where it was and the toast says why in
    // the words that answer names.
    it.each([
        ['AttemptUnderWay', 'It is already being sent, so it could not be taken back.'],
        [
            'StageDoesNotAllowIt',
            'It moved on since this was shown, so nothing was changed. The Outbox now says where it stands.',
        ],
        ['RecordUnknown', 'The server no longer holds that send.'],
    ])('says what a withdrawal the deployment answered with %s came to, and keeps the send', async (outcome, said) => {
        const held = deployment();

        held.answerDecisionsWith({ outcome });
        drawing(held.transport, { entry: outbox.waitingSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Cancel sending' }));

        expect(await screen.findByText(said)).toBeDefined();
        expect(screen.getByText('Nothing changed')).toBeDefined();
        expect(within(screen.getByRole('list', { name: 'Outbox' })).getByText('Re: UAT testing date')).toBeDefined();
    });

    it('says a send offered again was not, once its refusal no longer stands as it was shown', async () => {
        const held = deployment();

        held.answerDecisionsWith({ outcome: 'RefusalNotRestated' });
        drawing(held.transport, { entry: outbox.refusedOutgoingSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Send again' }));

        expect(
            await screen.findByText(
                'It moved on since this was shown, so nothing was changed. The Outbox now says where it stands.',
            ),
        ).toBeDefined();
    });

    it('says why a decision could not be done where the deployment did not answer it', async () => {
        const held = deployment();

        held.answerDecisionsWith({ status: 503 });
        drawing(held.transport, { entry: outbox.waitingSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Cancel sending' }));

        expect(await screen.findByText('That could not be done: unavailable.')).toBeDefined();
    });

    it('puts a dismissed send back on the list where the deployment did not take the dismissal', async () => {
        const held = deployment();

        held.answerDecisionsWith({ outcome: 'StageDoesNotAllowIt' });
        drawing(held.transport, { entry: outbox.unknownSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Close' }));

        expect(
            await within(screen.getByRole('list', { name: 'Outbox' })).findByText('Re: proposed terms for 2027'),
        ).toBeDefined();
        expect(screen.getByText('Nothing changed')).toBeDefined();
    });

    it('offers nothing to decide about a send an attempt holds right now', async () => {
        drawing(deployment().transport, { entry: outbox.sendingSend.outgoingEmail });

        expect(await screen.findByText(/^An attempt is sending it right now/u)).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Cancel sending' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Send again' })).toBeNull();
    });

    it('states a refusal in the server’s words and offers it again as one whose refusal was read', async () => {
        const held = deployment();

        drawing(held.transport, { entry: outbox.refusedOutgoingSend.outgoingEmail });

        expect(await screen.findByText('Not sent - the server refused it')).toBeDefined();
        expect(screen.getByText(outbox.refusedOutgoingSend.failure)).toBeDefined();
        expect(screen.getAllByText('Not taken')).toHaveLength(2);
        expect(screen.getByRole('list', { name: 'Files this message carries' }).textContent).toContain(
            'Addendum-3-signed.pdf',
        );

        fireEvent.click(screen.getByRole('button', { name: 'Send again' }));

        expect(await screen.findByText('It goes out again now.')).toBeDefined();
        expect(held.posted('/outbox/requeue')).toStrictEqual([
            { outgoingEmail: outbox.refusedOutgoingSend.outgoingEmail, refusalRestated: true },
        ]);
    });

    it('takes a dismissed send off the list at once and puts it back on undo, having told the deployment nothing', async () => {
        const held = deployment();

        drawing(held.transport, { entry: outbox.unknownSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));

        expect(await screen.findByText('Taken out of Outbox')).toBeDefined();
        expect(
            within(screen.getByRole('list', { name: 'Outbox' })).queryByText('Re: proposed terms for 2027'),
        ).toBeNull();
        expect(screen.getByText('Nothing open')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Undo' }));

        expect(
            await within(screen.getByRole('list', { name: 'Outbox' })).findByText('Re: proposed terms for 2027'),
        ).toBeDefined();
        expect(held.posted('/outbox/dismissal')).toStrictEqual([]);
    });

    it('tells the deployment of a dismissal once its toast has gone', async () => {
        const held = deployment();

        drawing(held.transport, { entry: outbox.unknownSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(held.posted('/outbox/dismissal')).toStrictEqual([
                { outgoingEmail: outbox.unknownSend.outgoingEmail },
            ]);
        });
    });

    it('keeps a dismissed send off the list once the deployment took it, before the page is read again', async () => {
        const held = deployment();

        drawing(held.transport, { entry: outbox.unknownSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
        held.holdPagesBack();

        const readsBefore = held.pageReads();

        fireEvent.click(await screen.findByRole('button', { name: 'Close' }));

        await waitFor(() => {
            expect(held.pageReads()).toBe(readsBefore + 1);
        });
        expect(
            within(screen.getByRole('list', { name: 'Outbox' })).queryByText('Re: proposed terms for 2027'),
        ).toBeNull();
    });

    // Another account's page says nothing about this one's sends, so reading it while a dismissal's toast still stands
    // does not draw the dismissed send again once this account's Outbox is back.
    it('keeps a dismissed send off its own list while another account’s Outbox is read', async () => {
        const held = deployment();

        drawing(held.transport, { entry: outbox.unknownSend.outgoingEmail });

        fireEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
        fireEvent.click(screen.getByRole('button', { name: anotherOutboxName }));

        expect(await screen.findByText('Outbox is empty')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: workOutboxName }));

        const list = await listed();

        expect(await within(list).findByText('Re: UAT testing date')).toBeDefined();
        expect(within(list).queryByText('Re: proposed terms for 2027')).toBeNull();
    });

    it('puts focus on what the pane draws once the send it stood on has left', async () => {
        drawing(deployment().transport, { entry: outbox.waitingSend.outgoingEmail });

        const withdrawing = await screen.findByRole('button', { name: 'Cancel sending' });

        withdrawing.focus();
        fireEvent.click(withdrawing);

        await waitFor(() => {
            expect(document.activeElement?.getAttribute('role')).toBe('status');
        });
        expect(document.activeElement?.textContent).toContain('Nothing open');
    });

    it('says a preview the deployment did not answer could not be read, and reads it again on a retry', async () => {
        const held = deployment();

        held.refusePreviews(true);
        drawing(held.transport, { entry: outbox.waitingSend.outgoingEmail });

        expect(await screen.findByText('This message could not be read: unavailable.')).toBeDefined();
        expect(screen.getByText('Not read')).toBeDefined();

        held.refusePreviews(false);
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(await screen.findByRole('button', { name: 'Cancel sending' })).toBeDefined();
    });

    it('says the machine is offline rather than reading a send it cannot reach', () => {
        const offline = deployment();

        drawing(offline.transport, { entry: outbox.waitingSend.outgoingEmail, online: false });

        expect(screen.getByText('Offline')).toBeDefined();
        expect(offline.requests).toStrictEqual([]);
    });

    it('lets go of a send the deployment no longer holds, leaving the Outbox open with nothing in the pane', async () => {
        drawing(deployment().transport, { entry: '00000000-0000-4000-8000-00000000ffff' });

        expect(await screen.findByText('Nothing open')).toBeDefined();
        expect(await listed()).toBeDefined();
    });
});
