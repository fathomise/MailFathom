// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientResponse } from '@mailfathom/client-backend';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { startingListWidth, storeListWidth } from './mailSpace/listWidth';
import {
    accepted,
    anotherPersonsSession,
    asked,
    complete,
    deploymentAnswering,
    deploymentDrawingAConversation,
    deploymentDrawingAMessage,
    deploymentWorkingInTabs,
    directory,
    framed,
    goTo,
    heldSession,
    keptBesideTheMailbox,
    openingAt,
    preferencesAnswering,
    renderApp,
    resetsBetweenTests,
    routesAsked,
    servedFrom,
    sessionAnswering,
    typedSession,
    workAccount,
    type Answer,
} from './App.harness';

// The frame itself: which space it opens in, what a row of the list opens beside it, and what the workspace holds
// while somebody moves between spaces. Everything the frame composes rather than any one screen inside it.
//
// Six files stand beside this one — the session, sign-in, the deployment, language, telemetry, and the shell's
// layers — each named for the group it proves, and all seven mount the same frame out of `App.harness`.

resetsBetweenTests();

const aiButton = 'Draft a reply or ask about this thread';

/**
 * A deployment drawing one message of a conversation to a credential that may write mail and ask the agent, and
 * answering what it says about drafting.
 *
 * @param draftsReplies What the deployment answers when asked whether it writes a reply.
 * @param drafting Set once that question was asked, so an absence is asserted only after the answer that decides it.
 */
function deploymentAnsweringDrafting(draftsReplies: boolean, drafting: { read: boolean }): DeploymentTransport {
    const otherwise = deploymentDrawingAConversation(
        sessionAnswering([
            'mailfathom.mail.read',
            'mailfathom.assistant.use',
            'mailfathom.mail.drafts.write',
            ...keptBesideTheMailbox,
        ]),
    );

    return (signal) => (request) => {
        if (!request.path.endsWith('/replies/drafting')) {
            return otherwise(signal)(request);
        }

        drafting.read = true;

        return Promise.resolve(complete({ status: 200, body: JSON.stringify({ draftsReplies }) }));
    };
}

/** Moves to Mail and opens the corpus's invoice, answering once its body is drawn. */
async function openTheInvoice(): Promise<void> {
    await framed();
    await goTo('Mail');

    const list = await screen.findByRole('listbox', { name: 'Messages' });
    fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
    fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

    await screen.findByText('A drawn message.');
}

describe('App', () => {
    it('says it is reaching the deployment while nothing has answered', () => {
        renderApp(servedFrom, heldSession, () => () => new Promise<ClientResponse>(() => undefined));

        expect(screen.getByText('Reaching the server…')).toBeDefined();
    });

    it('says it is reading the accounts once the deployment has said what the credential may do', async () => {
        const send: DeploymentTransport = () => (request) =>
            request.path.endsWith('/session')
                ? Promise.resolve(complete(accepted))
                : new Promise<ClientResponse>(() => undefined);

        // Said at the foot of Mail's folder column, which is the one place the freshness line stands.
        openingAt('#/mail');
        renderApp(servedFrom, heldSession, send);

        expect(await screen.findByText('Reading accounts…')).toBeDefined();
    });

    it('opens in Discover when the address names no space', async () => {
        renderApp();

        expect(await screen.findByRole('heading', { name: 'Discover', level: 1 })).toBeDefined();
    });

    it('writes the space it is showing into an address that named none, so it can be reloaded', async () => {
        renderApp();

        await waitFor(() => {
            expect(window.location.hash).toBe('#/discover');
        });
    });

    it('corrects an address naming a space the client does not have, rather than showing one it does not name', async () => {
        openingAt('#/nowhere');

        renderApp();

        expect(await screen.findByRole('heading', { name: 'Discover', level: 1 })).toBeDefined();
        await waitFor(() => {
            expect(window.location.hash).toBe('#/discover');
        });
    });

    it.each(['Mail', 'Tasks'])('opens in %s when that is what the address names', async (space) => {
        openingAt(`#/${space.toLowerCase()}`);

        renderApp();

        expect(await screen.findByRole('main', { name: space })).toBeDefined();
    });

    it('draws the message a row of the list opened, which is what the frame wires the two together for', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    it('names what a person working in tabs has opened, in a strip above the mail', async () => {
        renderApp(servedFrom, heldSession, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByRole('tab', { name: 'Quarterly invoice' })).toBeDefined();
        expect(await screen.findByText('A drawn message.')).toBeDefined();
    });

    // The section is the reader's to remove, and what removes it is a preference the deployment holds — so this is the
    // one assertion that the answer reaches the tree rather than stopping at the hook that read it.
    it('draws the standing views under the tree for a person who has not taken them out of it', async () => {
        renderApp(servedFrom, heldSession, deploymentAnswering(undefined, accepted, preferencesAnswering(true)));
        await framed();

        await goTo('Mail');

        expect(await screen.findByRole('region', { name: 'AI filters' })).toBeDefined();
    });

    it('draws no standing views at all for a person who took the section out of the tree', async () => {
        renderApp(servedFrom, heldSession, deploymentAnswering(undefined, accepted, preferencesAnswering(true, false)));
        await framed();

        await goTo('Mail');

        await screen.findByRole('complementary', { name: 'Folders and filters' });

        expect(screen.queryByRole('region', { name: 'AI filters' })).toBeNull();
    });

    it('says nothing is open to a person working in tabs who has opened none', async () => {
        renderApp(servedFrom, heldSession, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        expect(await screen.findByText('Nothing is open')).toBeDefined();
        expect(screen.queryByRole('tablist')).toBeNull();
    });

    it('opens mail in the reading column, and draws no strip, for somebody who has not asked for tabs', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        expect(await screen.findByText('A drawn message.')).toBeDefined();
        expect(screen.queryByRole('tablist')).toBeNull();
    });

    it('divides the mail space where the person now signed in last left it, rather than where anybody did', async () => {
        // The width is stored against the person the held credential names, so this is the one assertion that the name
        // the frame takes the credential apart for is the name the store was written under. Everything below the frame
        // is handed that name and could not tell a wrong one from a right one.
        storeListWidth('test', 468);

        renderApp();
        await framed();

        await goTo('Mail');

        expect(
            (await screen.findByRole('separator', { name: 'Message list width' })).getAttribute('aria-valuenow'),
        ).toBe('468');
    });

    it('divides it where it starts for somebody else signing in on the same machine', async () => {
        storeListWidth('test', 468);

        renderApp(servedFrom, anotherPersonsSession);
        await framed();

        await goTo('Mail');

        expect(
            (await screen.findByRole('separator', { name: 'Message list width' })).getAttribute('aria-valuenow'),
        ).toBe(String(startingListWidth));
    });

    // Opening a message from the list *is* opening its conversation, so there is no message standing behind the
    // conversation and nothing offering a way back to one — a control that did appeared even here, where the reader
    // had arrived from the list rather than from a message.
    it('opens the conversation a message belongs to from the row itself, with nothing standing behind it', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAConversation());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        const conversation = await screen.findByRole('region', { name: 'Conversation' });

        expect(await within(conversation).findByText('A drawn message.')).toBeDefined();
        expect(within(conversation).queryByRole('button', { name: 'Back to the message' })).toBeNull();
    });

    // What #1759 is about: a correspondence costs one read of the deployment however many messages are drawn out of
    // it. The route carries every message's description and words, so nothing under the conversation asks for either.
    it('reads a whole conversation in one request, and asks for no message of it on its own', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAConversation());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        await screen.findByText('A drawn message.');

        // Where the conversation stands is read beside it and is a route of its own, so it is not one of the reads
        // this counts: what #1759 settled is that the messages arrive together, not that nothing else is asked for.
        const conversations = routesAsked().filter((path) => path.includes('/threads/') && !path.endsWith('/state'));

        expect(conversations).toHaveLength(1);
        expect(conversations[0]).toContain('content=true');
        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
    });

    // The message this one opens belongs to no conversation, which is deliberate: what is being proven is the shape the
    // sender's own view is drawn in, and a threaded message would draw the way into the conversation beside it — a
    // second surface for every query here to walk past, in the heaviest test this suite has.
    it('draws the sender own markup in a window over the message where the person does not work in tabs', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        fireEvent.click(await screen.findByRole('button', { name: 'Show the original message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the original' }));

        const standing = await screen.findByRole('dialog', { name: 'The original message, as its sender wrote it' });
        const surface = within(standing).getByRole('region', { name: 'The original message, as its sender wrote it' });

        // The message is where it was rather than replaced by the surface, which is the whole of what a window over it
        // means: the reading column goes on drawing what the reader was reading.
        expect(screen.getByText('A drawn message.')).toBeDefined();

        // Opening it is a view change, so the reader is placed in what was opened. Where that focus goes when the
        // window closes is the platform's own — the browser suite is where a real modal can be asked.
        await waitFor(() => {
            expect(document.activeElement).toBe(surface);
        });

        fireEvent.click(within(surface).getByRole('button', { name: 'Close the original message' }));

        expect(screen.queryByRole('dialog')).toBeNull();
        expect(screen.getByText('A drawn message.')).toBeDefined();
    });

    it('draws the sender own markup in the reading column where the person works in tabs', async () => {
        renderApp(servedFrom, heldSession, deploymentWorkingInTabs());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

        fireEvent.click(await screen.findByRole('button', { name: 'Show the original message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the original' }));

        // A tab of its own, so it takes the column the message had rather than standing in a window over it — which is
        // the mode deciding the shape, and the one difference between this test and the one above.
        expect(
            await screen.findByRole('region', { name: 'The original message, as its sender wrote it' }),
        ).toBeDefined();
        expect(screen.queryByRole('dialog')).toBeNull();
        expect(screen.queryByText('A drawn message.')).toBeNull();
    });

    // Three things decide whether opening a message marks it read, and all three are the frame's: the reader's own
    // setting, the grant the credential signed in under, and there being a session to submit over. Nothing below the
    // frame asks the question, so this is where each of them is proven.
    it('marks read a message a row opened, where the credential may write a flag', async () => {
        renderApp(
            servedFrom,
            heldSession,
            deploymentDrawingAMessage(
                sessionAnswering([
                    'mailfathom.mail.read',
                    'mailfathom.assistant.use',
                    'mailfathom.mail.flags.write',
                    ...keptBesideTheMailbox,
                ]),
            ),
        );
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        await screen.findByText('A drawn message.');

        await waitFor(() => {
            expect(
                asked.some(
                    (request) =>
                        request.path === 'https://mail.example.invalid/api/client/mutations/flags' &&
                        request.method === 'POST',
                ),
            ).toBe(true);
        });
    });

    it('marks nothing where the credential was never granted a flag write, and says so rather than failing', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        await screen.findByText('A drawn message.');

        expect(routesAsked().some((path) => path.includes('/mutations/'))).toBe(false);

        // An absence nobody explained is a client that looks broken, which is what the notice strip is for — so the
        // sentence is asserted on the screen rather than the withholding being asserted on its own.
        expect(
            screen.getByText(
                'This credential may not change a flag on your mail server, so opening a message leaves it unread there, flagging and marking unread are not offered, and this client shows what the server last reported. Your administrator can grant that.',
            ),
        ).toBeDefined();
    });

    it('draws no message until a row of the list opens one', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        await goTo('Mail');
        await screen.findByRole('listbox', { name: 'Messages' });

        expect(screen.getByText('Nothing is open')).toBeDefined();
        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
    });

    it('reads no message while the space on the screen is not Mail', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAMessage());
        await framed();

        expect(routesAsked().some((path) => path.includes('/messages/'))).toBe(false);
    });

    // Every entry the other spaces draw hands over the same way, and the frame is what carries the hand-over across:
    // it holds what was handed, moves to the Agent, and the Agent states it. The entries themselves are proven where
    // they are drawn; what only this file can prove is that pressing one arrives somewhere with the context on it.
    it('moves to the agent with what a space handed over, and says what it is about', async () => {
        renderApp(servedFrom, heldSession, deploymentDrawingAConversation());
        await framed();

        await goTo('Mail');

        const list = await screen.findByRole('listbox', { name: 'Messages' });
        fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));
        fireEvent.click(await screen.findByRole('button', { name: aiButton }));
        fireEvent.click(screen.getByRole('button', { name: 'Ask' }));

        expect(await screen.findByRole('main', { name: 'Agent' })).toBeDefined();
        expect(screen.getByText('Context: thread “Quarterly invoice”')).toBeDefined();
    });

    it('shows the space whose link was activated, and marks it as the current one', async () => {
        renderApp();
        await screen.findByRole('heading', { name: 'Discover', level: 1 });

        await goTo('Tasks');

        expect(screen.getByRole('link', { current: 'page' }).textContent).toBe('Tasks');
    });

    // Discover's question is the one field that asks one, so the space moved to draws no copy of it — and coming back
    // finds the sentence where it was left.
    it('keeps the question in Discover while the person moves between spaces', async () => {
        renderApp();
        await framed();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'the renewal Nordwind sent' },
        });

        await goTo('Mail');

        expect(screen.queryByRole('searchbox', { name: 'Ask your mail' })).toBeNull();

        await goTo('Discover');

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'value',
            'the renewal Nordwind sent',
        );
    });

    // The AI button under a correspondence offers *Draft a reply* only where the deployment drafts, so a deployment that
    // writes none is offered no choice whose press would have to go somewhere else. Both halves hold the grant to write,
    // so what decides between them is the deployment's own answer about drafting and nothing else.
    it.each([true, false])(
        'offers a drafted reply under an open message only where the deployment answers that it drafts: %s',
        async (draftsReplies) => {
            const drafting = { read: false };

            renderApp(servedFrom, heldSession, deploymentAnsweringDrafting(draftsReplies, drafting));
            await framed();

            await goTo('Mail');

            const list = await screen.findByRole('listbox', { name: 'Messages' });
            fireEvent.pointerDown(within(list).getByRole('option', { name: /Quarterly invoice/ }));
            fireEvent.pointerUp(within(list).getByRole('option', { name: /Quarterly invoice/ }));

            expect(await screen.findByText('A drawn message.')).toBeDefined();

            if (draftsReplies) {
                fireEvent.click(await screen.findByRole('button', { name: aiButton }));

                expect(await screen.findByRole('button', { name: 'Draft a reply' })).toBeDefined();
            } else {
                await waitFor(() => {
                    expect(drafting.read).toBe(true);
                });

                fireEvent.click(await screen.findByRole('button', { name: aiButton }));

                expect(screen.getByRole('button', { name: 'Ask' })).toBeDefined();
                expect(screen.queryByRole('button', { name: 'Draft a reply' })).toBeNull();
            }

            expect(screen.queryByRole('searchbox', { name: 'Ask your mail' })).toBeNull();
        },
    );

    // The drafter goes with the reading column the composer stands in, so closing that composer hands the keyboard to the
    // AI button drawn under the same message again — never to the drafter's field, which is no longer in the document.
    it('hands the keyboard to the AI button when a composer opened from the drafter closes', async () => {
        renderApp(servedFrom, heldSession, deploymentAnsweringDrafting(true, { read: false }));
        await openTheInvoice();

        fireEvent.click(await screen.findByRole('button', { name: aiButton }));
        fireEvent.click(await screen.findByRole('button', { name: 'Draft a reply' }));
        fireEvent.click(
            within(screen.getByRole('form', { name: 'Draft a reply to this correspondence' })).getByRole('button', {
                name: 'Draft a reply',
            }),
        );

        // Closed once the composer has read the message it answers and its drafting has answered, so the close is the
        // only thing still settling when the button is looked for — the deployment here drafts nothing, which the
        // composer says.
        await screen.findByText('Nothing was drafted');
        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        const handedBackTo = await screen.findByRole('button', { name: aiButton });

        await waitFor(() => {
            expect(document.activeElement).toBe(handedBackTo);
        });
    });

    // Each of these withholds the AI button from a message that would otherwise carry it, and each is asserted only
    // once the message it would have stood under is drawn — beside the case above that finds it there.
    it('takes the AI button away once a message is being written over the one that is open', async () => {
        renderApp(servedFrom, heldSession, deploymentAnsweringDrafting(true, { read: false }));
        await openTheInvoice();
        expect(await screen.findByRole('button', { name: aiButton })).toBeDefined();

        fireEvent.click(
            within(screen.getByRole('toolbar', { name: 'Mail actions' })).getByRole('button', { name: 'New message' }),
        );

        await screen.findByLabelText('Message');
        expect(screen.queryByRole('button', { name: aiButton })).toBeNull();
    });

    it('takes the AI button away with the rest of the panels', async () => {
        renderApp(servedFrom, heldSession, deploymentAnsweringDrafting(true, { read: false }));
        await openTheInvoice();
        expect(await screen.findByRole('button', { name: aiButton })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Hide the panels - the correspondence alone' }));

        await screen.findByRole('button', { name: "Show the thread's panels" });
        expect(screen.queryByRole('button', { name: aiButton })).toBeNull();
    });

    it('draws no AI button under a message for a credential that may not ask the agent', async () => {
        renderApp(
            servedFrom,
            heldSession,
            deploymentDrawingAConversation(
                sessionAnswering(['mailfathom.mail.read', 'mailfathom.mail.drafts.write', ...keptBesideTheMailbox]),
            ),
        );
        await openTheInvoice();

        expect(screen.queryByRole('button', { name: aiButton })).toBeNull();
    });

    it('says the deployment is not refreshing these accounts when it is not, as its setting rather than a grant', async () => {
        openingAt('#/mail');
        renderApp(servedFrom, heldSession, deploymentAnswering(directory(false, [workAccount])));

        expect(
            await screen.findByText(
                'This server is not refreshing the local copy of these accounts, so what you see is as current as its last run left it. That is a setting on the server rather than a permission you are missing.',
            ),
        ).toBeDefined();
    });

    it('reports why the accounts could not be read instead of saying nothing about them', async () => {
        openingAt('#/mail');
        renderApp(servedFrom, heldSession, deploymentAnswering({ status: 403, body: '' }));

        expect(await screen.findByText('The accounts could not be read: unauthorized.')).toBeDefined();
    });

    // An answer the client could not use, rather than none at all: a server that did not answer is the connection
    // status's to say, and the line offers nothing of its own then.
    it('reads the accounts again when the person asks it to, rather than only on a reload', async () => {
        let accounts: Answer = { status: 500, body: '' };
        const deployment: DeploymentTransport = () => (request) =>
            Promise.resolve(complete(request.path.endsWith('/session') ? accepted : accounts));

        openingAt('#/mail');
        renderApp(servedFrom, heldSession, deployment);
        const retry = await screen.findByRole('button', { name: 'Try again' });

        accounts = directory(true, [workAccount]);
        fireEvent.click(retry);

        expect(await screen.findByText('Every account is up to date.')).toBeDefined();
    });

    it('reads its mail with the credential it holds rather than with one written into the client', async () => {
        renderApp(servedFrom, typedSession);
        await framed();

        expect([...new Set(asked.map((request) => request.headers['Authorization']))]).toEqual([
            typedSession.authorization,
        ]);
    });
});
