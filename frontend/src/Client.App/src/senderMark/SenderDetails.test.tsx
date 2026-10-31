// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import {
    readMailMessage,
    type ClientRequest,
    type ClientResponse,
    type ClientSession,
    type MailFathomTransport,
    type MailMessage,
} from '@mailfathom/client-backend';
import { newsletterId, newsletterMessage } from '../../../../tests/fixtures/messages';
import { messageSenderEvidence, type SenderKind } from '../../../../tests/fixtures/senders';
import { LocalizationProvider } from '../localization/Localization';
import { storeLocale } from '../localization/locale';
import { pl } from '../localization/pl';
import { MessageSenderDetails, SenderDetails, type AskedSender } from './SenderDetails';

// What the harness's own control says, held as a value rather than written into the markup.
const opening = 'Explain';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

function wire(kind: SenderKind): unknown {
    return { ...newsletterMessage, ...messageSenderEvidence(kind) };
}

function answering(kind: SenderKind): ClientResponse {
    return { status: 200, body: JSON.stringify(wire(kind)), headers: {} };
}

async function read(kind: SenderKind): Promise<MailMessage> {
    const result = await readMailMessage(session, () => Promise.resolve(answering(kind)), newsletterId);

    if (result.outcome === 'failed') {
        throw new Error('The corpus message is not one the client reads.');
    }

    return result.value;
}

const answersNothing: MailFathomTransport = () => new Promise<ClientResponse>(() => undefined);

/** The reader's contact holding the author's address, as the book states it. */
const heldContact = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    displayName: 'Example',
    addresses: ['news@example.invalid'],
    preferredAddress: 'news@example.invalid',
    note: null,
    phoneNumbers: [{ number: '+1 202 555 0178', label: 'Work' }],
    origin: 'Asserted',
    book: 'Own',
    recordedAt: '2026-03-01T08:00:00+00:00',
    amendedAt: '2026-03-01T08:00:00+00:00',
    photo: null,
    organization: null,
};

/** What the book answers to a write it performed, naming the contact it wrote. */
const contactWritten: ClientResponse = {
    status: 200,
    body: JSON.stringify({ outcome: 'Written', contact: heldContact, addressHolder: null }),
    headers: {},
};

/** The unrecognized sender, offered as a restatement of the address the reader's own contact holds. */
const offeredAsRestated: ClientResponse = {
    status: 200,
    body: JSON.stringify({
        ...newsletterMessage,
        ...messageSenderEvidence('unrecognized'),
        sender: {
            ...messageSenderEvidence('unrecognized').sender,
            addToContacts: { restatedContactId: heldContact.id },
        },
    }),
    headers: {},
};

const fromRow: AskedSender = { storedEmailId: newsletterId, message: null, anchor: null };

function Explaining({ transport }: { readonly transport: MailFathomTransport }) {
    const dialog = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    dialog.current?.showModal();
                }}
            >
                {opening}
            </button>

            <SenderDetails asked={fromRow} session={session} transport={transport} dialog={dialog} />
        </>
    );
}

function explain(transport: MailFathomTransport): void {
    render(
        <LocalizationProvider>
            <Explaining transport={transport} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: opening }));
}

// Asks the way a list does: every press builds the question afresh, though it is about the same message.
function ExplainingEachPress({ transport }: { readonly transport: MailFathomTransport }) {
    const dialog = useRef<HTMLDialogElement>(null);
    const [asked, setAsked] = useState<AskedSender | null>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    flushSync(() => {
                        setAsked({ ...fromRow });
                    });
                    dialog.current?.showModal();
                }}
            >
                {opening}
            </button>

            <SenderDetails asked={asked} session={session} transport={transport} dialog={dialog} />
        </>
    );
}

function ExplainingInHand({
    message,
    transport = answersNothing,
}: {
    readonly message: MailMessage;
    readonly transport?: MailFathomTransport;
}) {
    const dialog = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    dialog.current?.showModal();
                }}
            >
                {opening}
            </button>

            <MessageSenderDetails
                session={session}
                transport={transport}
                message={message}
                anchor={null}
                dialog={dialog}
            />
        </>
    );
}

describe('SenderDetails', () => {
    it('reads the message it explains, and says so while it does', async () => {
        const asked: ClientRequest[] = [];

        explain((request) => {
            asked.push(request);

            return Promise.resolve(answering('trusted'));
        });

        expect(screen.getByRole('status').textContent).toBe('Reading what was checked…');
        expect(await screen.findByRole('heading', { name: 'Trusted sender' })).toBeDefined();
        expect(asked.map((request) => request.path)).toEqual([
            `https://mail.example.invalid/api/client/messages/${newsletterId}`,
        ]);
    });

    it('lists every step under the three groups, each with its result', async () => {
        explain(() => Promise.resolve(answering('verified')));

        const signature = await screen.findByRole('region', { name: 'Signature' });

        expect(within(signature).getByText('Certificate authority')).toBeDefined();
        expect(within(signature).getAllByText('Passed')).toHaveLength(6);
        expect(screen.getByRole('region', { name: 'Sender authentication' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'Trust' })).toBeDefined();
    });

    it('says why the mark is the mark, under who the message is from', async () => {
        explain(() => Promise.resolve(answering('forgedSignature')));

        expect(
            await screen.findByText(
                'Its signature does not match its content: it was changed after it was signed, or the signature is forged.',
            ),
        ).toBeDefined();
        expect(screen.getByText('news@example.invalid')).toBeDefined();
    });

    it('names the reader’s organization a member is trusted through, in the reason and in the trust step', async () => {
        explain(() => Promise.resolve(answering('organizationMember')));

        expect(
            await screen.findByText(
                "The sender is a member of your organization, Nordwind, and the organization's signature proves this message comes from this address.",
            ),
        ).toBeDefined();
        expect(
            screen.getByText(
                "A member of your organization, Nordwind. The organization's signature proves the message comes from news@example.invalid.",
            ),
        ).toBeDefined();
    });

    it('says a member’s organization has since been deleted rather than naming it', async () => {
        explain(() => Promise.resolve(answering('deletedOrganizationMember')));

        expect(await screen.findByRole('heading', { name: 'Trusted sender' })).toBeDefined();
        expect(
            screen.getByText(
                "A member of your organization. The organization's signature proves the message comes from news@example.invalid; the organization has since been deleted, so its name is not shown.",
            ),
        ).toBeDefined();
    });

    it('names the organization in Polish', async () => {
        storeLocale('pl');
        explain(() => Promise.resolve(answering('organizationMember')));

        expect(
            await screen.findByText(
                pl['senderMark.trust.organizationMember']
                    .replace('{organization}', 'Nordwind')
                    .replace('{address}', 'news@example.invalid'),
            ),
        ).toBeDefined();
        expect(
            screen.getByText(pl['senderMark.why.trustedOrganizationMember'].replace('{organization}', 'Nordwind')),
        ).toBeDefined();
    });

    it('says in Polish that a member’s organization was deleted', async () => {
        storeLocale('pl');
        explain(() => Promise.resolve(answering('deletedOrganizationMember')));

        expect(
            await screen.findByText(
                pl['senderMark.trust.organizationMemberDeleted'].replace('{address}', 'news@example.invalid'),
            ),
        ).toBeDefined();
        expect(screen.getByText(pl['senderMark.why.trustedOrganizationMemberDeleted'])).toBeDefined();
    });

    it('unfolds the certificate’s details from the step that names its authority', async () => {
        explain(() => Promise.resolve(answering('verified')));

        const unfold = await screen.findByRole('button', { name: 'Certificate details' });

        fireEvent.click(unfold);

        expect(unfold.getAttribute('aria-expanded')).toBe('true');
        expect(screen.getByText('Harbourline Public Email CA R4', { selector: 'dd' })).toBeDefined();
    });

    it('says what failed and offers to read again where the server could not be reached', async () => {
        const transport = vi
            .fn<MailFathomTransport>()
            .mockResolvedValueOnce({ status: 503, body: '', headers: {} })
            .mockResolvedValueOnce(answering('trusted'));

        explain(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

        expect(await screen.findByRole('heading', { name: 'Trusted sender' })).toBeDefined();
        expect(transport).toHaveBeenCalledTimes(2);
    });

    // A trust change made since, or a failure no retry is offered for, is only seen by reading again.
    it('reads the message again each time it is opened, rather than drawing the last answer', async () => {
        const transport = vi
            .fn<MailFathomTransport>()
            .mockResolvedValueOnce(answering('unrecognized'))
            .mockResolvedValueOnce(answering('trusted'));

        render(
            <LocalizationProvider>
                <ExplainingEachPress transport={transport} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: opening }));
        await screen.findByRole('heading', { name: 'Sender not recognized' });
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        fireEvent.click(screen.getByRole('button', { name: opening }));

        expect(await screen.findByRole('heading', { name: 'Trusted sender' })).toBeDefined();
        expect(transport).toHaveBeenCalledTimes(2);
    });

    it('closes from its own control', async () => {
        explain(() => Promise.resolve(answering('trusted')));

        await screen.findByRole('heading', { name: 'Trusted sender' });
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('offers nothing to act on a sender already in the reader’s contacts', async () => {
        explain(() => Promise.resolve(answering('trusted')));

        const details = await screen.findByRole('dialog');

        await within(details).findByRole('heading', { name: 'Trusted sender' });

        expect(within(details).getAllByRole('button')).toEqual([
            within(details).getByRole('button', { name: 'Close' }),
        ]);
    });

    it('says what adding a sender does before it writes anything', async () => {
        const asked: ClientRequest[] = [];

        explain((request) => {
            asked.push(request);

            return Promise.resolve(answering('unrecognized'));
        });

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));

        const asking = screen.getByRole('group', { name: 'Add Example to your contacts?' });

        expect(
            within(asking).getByText(
                'Mail from news@example.invalid is then marked trusted whenever its sender is authenticated, and known whenever it shows this address, unless this account is strict.',
            ),
        ).toBeDefined();
        expect(document.activeElement).toBe(within(asking).getByRole('button', { name: 'Add to contacts' }));
        expect(asked).toHaveLength(1);
    });

    it('adds a warning’s sender only after saying the warning stays where the domain’s policy says so', async () => {
        explain(() => Promise.resolve(answering('failedAuthentication')));

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));

        expect(
            screen.getByText(/This message stays a warning on a strict account, or where example\.invalid publishes/),
        ).toBeDefined();
    });

    it('writes the author down as a new contact and says the mark changes once the mail is checked again', async () => {
        const asked: ClientRequest[] = [];

        explain((request) => {
            asked.push(request);

            return Promise.resolve(request.method === 'POST' ? contactWritten : answering('unrecognized'));
        });

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));
        fireEvent.click(
            within(screen.getByRole('group', { name: 'Add Example to your contacts?' })).getByRole('button', {
                name: 'Add to contacts',
            }),
        );

        const done = await screen.findByText(
            'Example is in your contacts now. The mark changes once your mail is checked again.',
        );

        expect(asked[1]).toMatchObject({
            method: 'POST',
            path: 'https://mail.example.invalid/api/client/contacts',
            body: JSON.stringify({
                displayName: 'Example',
                addresses: ['news@example.invalid'],
                preferredAddress: 'news@example.invalid',
                note: null,
                phoneNumbers: [],
            }),
        });
        expect(document.activeElement).toBe(done.closest('[role="status"]'));
        expect(screen.getByRole('heading', { name: 'Sender not recognized' })).toBeDefined();
    });

    it('says why an addition failed and offers to try again', async () => {
        explain((request) =>
            Promise.resolve(
                request.method === 'POST' ? { status: 503, body: '', headers: {} } : answering('unrecognized'),
            ),
        );

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));
        fireEvent.click(
            within(screen.getByRole('group', { name: 'Add Example to your contacts?' })).getByRole('button', {
                name: 'Add to contacts',
            }),
        );

        expect((await screen.findByRole('alert')).textContent).toBe(
            'Example could not be added to your contacts: unavailable',
        );
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('says the reader’s contacts changed where the book did not write the author down', async () => {
        explain((request) =>
            Promise.resolve(
                request.method === 'POST'
                    ? {
                          status: 200,
                          body: JSON.stringify({
                              outcome: 'AddressHeldByAnotherContact',
                              contact: null,
                              addressHolder: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91',
                          }),
                          headers: {},
                      }
                    : answering('unrecognized'),
            ),
        );

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));
        fireEvent.click(
            within(screen.getByRole('group', { name: 'Add Example to your contacts?' })).getByRole('button', {
                name: 'Add to contacts',
            }),
        );

        expect((await screen.findByRole('alert')).textContent).toBe(
            'Example could not be added to your contacts: your contacts changed since this message was read',
        );
    });

    it('holds Cancel while the write is in flight, so the card is not left before the book answers', async () => {
        explain((request) =>
            request.method === 'POST'
                ? new Promise<ClientResponse>(() => undefined)
                : Promise.resolve(answering('unrecognized')),
        );

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));
        fireEvent.click(
            within(screen.getByRole('group', { name: 'Add Example to your contacts?' })).getByRole('button', {
                name: 'Add to contacts',
            }),
        );

        const cancel = await screen.findByRole('button', { name: 'Cancel' });

        expect(cancel.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(cancel);

        expect(screen.getByRole('group', { name: 'Add Example to your contacts?' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Adding…' })).toBeDefined();
    });

    it('restates the address the reader’s own contact holds, saying so before it writes', async () => {
        const asked: ClientRequest[] = [];

        explain((request) => {
            asked.push(request);

            if (request.method === 'PUT') {
                return Promise.resolve(contactWritten);
            }

            return Promise.resolve(
                request.path.includes('/contacts/')
                    ? { status: 200, body: JSON.stringify(heldContact), headers: {} }
                    : offeredAsRestated,
            );
        });

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));

        const asking = screen.getByRole('group', { name: 'Add Example to your contacts?' });

        expect(
            within(asking).getByText(
                /^news@example\.invalid is already in your contacts, but you did not write it down yourself, so it counts only once you add it\. Mail from/,
            ),
        ).toBeDefined();

        fireEvent.click(within(asking).getByRole('button', { name: 'Add to contacts' }));
        await screen.findByText('Example is in your contacts now. The mark changes once your mail is checked again.');

        expect(asked.slice(1).map((request) => [request.method, request.path, request.body ?? null])).toEqual([
            ['GET', `https://mail.example.invalid/api/client/contacts/${heldContact.id}`, null],
            [
                'PUT',
                `https://mail.example.invalid/api/client/contacts/${heldContact.id}`,
                JSON.stringify({
                    displayName: 'Example',
                    addresses: ['news@example.invalid'],
                    preferredAddress: 'news@example.invalid',
                    note: null,
                    phoneNumbers: heldContact.phoneNumbers,
                    restatedAddresses: ['news@example.invalid'],
                }),
            ],
        ]);
    });

    it('shows a write still in flight when the details are opened again, rather than offering a second one', async () => {
        const asked: ClientRequest[] = [];
        let answer: (response: ClientResponse) => void = () => undefined;

        render(
            <LocalizationProvider>
                <ExplainingEachPress
                    transport={(request) => {
                        asked.push(request);

                        return request.method === 'POST'
                            ? new Promise<ClientResponse>((resolve) => {
                                  answer = resolve;
                              })
                            : Promise.resolve(answering('unrecognized'));
                    }}
                />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: opening }));
        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));
        fireEvent.click(
            within(screen.getByRole('group', { name: 'Add Example to your contacts?' })).getByRole('button', {
                name: 'Add to contacts',
            }),
        );
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        fireEvent.click(screen.getByRole('button', { name: opening }));

        expect(await screen.findByRole('button', { name: 'Adding…' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Add to contacts' })).toBeNull();

        answer(contactWritten);

        await screen.findByText('Example is in your contacts now. The mark changes once your mail is checked again.');

        expect(asked.filter((request) => request.method === 'POST')).toHaveLength(1);
    });

    it('goes back to the offer when the addition is cancelled', async () => {
        explain(() => Promise.resolve(answering('unrecognized')));

        fireEvent.click(await screen.findByRole('button', { name: 'Add to contacts' }));
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(screen.queryByRole('group', { name: 'Add Example to your contacts?' })).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Add to contacts' }));
    });
});

describe('MessageSenderDetails', () => {
    it('draws from the message already read, asking for nothing', async () => {
        const message = await read('failedAuthentication');

        render(
            <LocalizationProvider>
                <ExplainingInHand message={message} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: opening }));

        expect(screen.getByRole('heading', { name: 'Suspicious - sender authentication failed' })).toBeDefined();
        expect(
            screen.getByText(
                'example.invalid is not established as the author. lists.example.invalid authenticated the message instead.',
            ),
        ).toBeDefined();
    });

    it('offers nothing again in a later opening once the author was added', async () => {
        const message = await read('unrecognized');

        render(
            <LocalizationProvider>
                <ExplainingInHand message={message} transport={() => Promise.resolve(contactWritten)} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: opening }));
        fireEvent.click(screen.getByRole('button', { name: 'Add to contacts' }));
        fireEvent.click(
            within(screen.getByRole('group', { name: 'Add Example to your contacts?' })).getByRole('button', {
                name: 'Add to contacts',
            }),
        );
        await screen.findByText('Example is in your contacts now. The mark changes once your mail is checked again.');
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        fireEvent.click(screen.getByRole('button', { name: opening }));

        expect(screen.queryByRole('button', { name: 'Add to contacts' })).toBeNull();
    });
});
