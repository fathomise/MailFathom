// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { SignalledChangesContext, type SignalListener } from '../signals/signalledChanges';
import { useContactBook, type ContactBookName } from './useContactBook';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// The probe's own controls, named here rather than written into its markup: the localization rule holds over
// every `.tsx` under the packages, tests included, and a label a test invented is not a catalogue entry.
const asksForMore = 'Read more';
const asksAgain = 'Read again';

function contact(id: string, displayName: string): unknown {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        phoneNumbers: [],
        origin: 'Asserted',
        book: 'Own',
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
        photo: null,
        organization: null,
    };
}

function pageOf(contacts: readonly unknown[], nextCursor: string | null): string {
    return JSON.stringify({ contacts, nextCursor });
}

function colleague(id: string, displayName: string, presence = 'Active'): unknown {
    return {
        id,
        book: 'Member',
        displayName,
        addresses: [`${id}@nordwind.example`],
        preferredAddress: `${id}@nordwind.example`,
        phoneNumbers: [],
        jobTitle: 'Controller',
        organizationName: 'Nordwind',
        timeZone: null,
        photo: null,
        presence,
        lastSeen: null,
    };
}

// Every book over one transport, so a test can say what each answered and how often it was asked. The collected route
// and the directory are matched first because the asserted one is a prefix of both. A book a test states nothing for
// answers an empty page.
function answering(answers: Partial<Readonly<Record<ContactBookName, readonly string[]>>>): {
    readonly transport: MailFathomTransport;
    readonly asked: () => readonly string[];
} {
    const asked: string[] = [];
    const served: Record<ContactBookName, number> = { own: 0, colleagues: 0, collected: 0 };

    return {
        asked: () => asked,
        transport: ({ path }) => {
            asked.push(path);

            const book: ContactBookName = path.includes('/contacts/collected')
                ? 'collected'
                : path.includes('/contacts/colleagues')
                  ? 'colleagues'
                  : 'own';
            const pages = answers[book] ?? [];
            const body = pages[Math.min(served[book], pages.length - 1)] ?? pageOf([], null);

            served[book] += 1;

            return Promise.resolve({ status: 200, body, headers: {} });
        },
    };
}

function Book({
    transport,
    book = 'own',
    asking = session,
}: {
    readonly transport: MailFathomTransport;
    readonly book?: ContactBookName;
    readonly asking?: ClientSession | null;
}) {
    const held = useContactBook(asking, transport, book);

    return (
        <div>
            <ul>
                {held.contacts.map((person) => (
                    <li key={person.id}>
                        {person.displayName}
                        {person.book === 'Member' ? <span>{person.presence}</span> : null}
                    </li>
                ))}
            </ul>
            <p>{held.reading ? 'reading' : 'settled'}</p>
            <p>{held.paging ? 'paging' : 'not paging'}</p>
            <p>{held.failure ?? 'answered'}</p>
            <p>{held.complete ? 'complete' : 'more to read'}</p>
            <button type="button" onClick={held.readMore}>
                {asksForMore}
            </button>
            <button type="button" onClick={held.readAgain}>
                {asksAgain}
            </button>
        </div>
    );
}

describe('useContactBook', () => {
    it('reads the book it was asked for and holds the people it named', async () => {
        const { transport, asked } = answering({
            own: [pageOf([contact('anna', 'Anna Kowalska')], null)],
            collected: [],
        });

        render(<Book transport={transport} />);

        expect(await screen.findByText('Anna Kowalska')).toBeDefined();
        await waitFor(() => {
            expect(asked()[0]).toContain('/contacts?');
        });
        expect(screen.getByText('complete')).toBeDefined();
    });

    it('appends the page after the ones held rather than replacing what has been read', async () => {
        const { transport } = answering({
            own: [
                pageOf([contact('anna', 'Anna Kowalska')], 'after-anna'),
                pageOf([contact('bartek', 'Bartek Nowak')], null),
            ],
            collected: [],
        });

        render(<Book transport={transport} />);
        expect(await screen.findByText('Anna Kowalska')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: asksForMore }));

        expect(await screen.findByText('Bartek Nowak')).toBeDefined();
        expect(screen.getByText('Anna Kowalska')).toBeDefined();
    });

    it('reads the other book from the leading end, so the tab pressed never draws the book left behind', async () => {
        const { transport } = answering({
            own: [pageOf([contact('anna', 'Anna Kowalska')], null)],
            collected: [pageOf([contact('celina', 'Celina Wrona')], null)],
        });

        const drawn = render(<Book transport={transport} />);
        expect(await screen.findByText('Anna Kowalska')).toBeDefined();

        drawn.rerender(<Book transport={transport} book="collected" />);

        expect(screen.queryByText('Anna Kowalska')).toBeNull();
        expect(await screen.findByText('Celina Wrona')).toBeDefined();
    });

    it("reads the organization's members out of the directory, as the book of colleagues", async () => {
        const { transport, asked } = answering({
            own: [pageOf([contact('anna', 'Anna Kowalska')], null)],
            colleagues: [pageOf([colleague('piotr', 'Piotr Zieliński')], null)],
        });

        render(<Book transport={transport} book="colleagues" />);

        expect(await screen.findByText('Piotr Zieliński')).toBeDefined();
        expect(screen.queryByText('Anna Kowalska')).toBeNull();
        expect(asked()[0]).toContain('/contacts/colleagues?');
    });

    it('reports why the deployment did not answer, and reads again when asked to', async () => {
        let attempts = 0;
        const transport: MailFathomTransport = () => {
            attempts += 1;

            return Promise.resolve(
                attempts === 1
                    ? { status: 503, body: '', headers: {} }
                    : { status: 200, body: pageOf([contact('anna', 'Anna Kowalska')], null), headers: {} },
            );
        };

        render(<Book transport={transport} />);
        expect(await screen.findByText('unavailable')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: asksAgain }));

        expect(await screen.findByText('Anna Kowalska')).toBeDefined();
        expect(screen.getByText('answered')).toBeDefined();
    });

    it('reads nothing at all where there is no credential to ask with', async () => {
        let attempts = 0;
        const transport: MailFathomTransport = () => {
            attempts += 1;

            return Promise.resolve({ status: 200, body: pageOf([], null), headers: {} });
        };

        render(<Book transport={transport} asking={null} />);

        expect(await screen.findByText('settled')).toBeDefined();
        expect(attempts).toBe(0);
    });

    describe('a colleague’s presence', () => {
        // The directory, and each member read alone, answering whatever the test last said about them.
        function directory(presence: Record<string, string>): {
            readonly transport: MailFathomTransport;
            readonly asked: () => readonly string[];
        } {
            const asked: string[] = [];

            return {
                asked: () => asked,
                transport: ({ path }) => {
                    asked.push(path);

                    const member = /\/contacts\/colleagues\/([a-z]+)$/u.exec(path)?.[1];
                    const body =
                        member === undefined
                            ? pageOf(
                                  Object.entries(presence).map(([id, state]) => colleague(id, id, state)),
                                  null,
                              )
                            : JSON.stringify(colleague(member, member, presence[member]));

                    return Promise.resolve({ status: 200, body, headers: {} });
                },
            };
        }

        function told(): { readonly say: SignalListener; readonly listen: (listener: SignalListener) => () => void } {
            const listeners = new Set<SignalListener>();

            return {
                say: (change) => {
                    for (const listener of listeners) {
                        listener(change);
                    }
                },
                listen: (listener) => {
                    listeners.add(listener);

                    return () => {
                        listeners.delete(listener);
                    };
                },
            };
        }

        function drawDirectory(
            transport: MailFathomTransport,
            channel: ReturnType<typeof told>,
            book: ContactBookName,
        ) {
            render(
                <SignalledChangesContext value={{ listen: channel.listen, refresh: () => undefined }}>
                    <Book transport={transport} book={book} />
                </SignalledChangesContext>,
            );
        }

        it('reads again each member a presence change names, and only them', async () => {
            const presence: Record<string, string> = { marta: 'Busy', piotr: 'Active' };
            const { transport, asked } = directory(presence);
            const channel = told();

            drawDirectory(transport, channel, 'colleagues');
            expect(await screen.findByText('Busy')).toBeDefined();

            presence['piotr'] = 'Away';
            act(() => {
                channel.say({ kind: 'presence.changed', members: ['piotr'] });
            });

            expect(await screen.findByText('Away')).toBeDefined();
            expect(asked().slice(1)).toEqual(['https://mail.example.invalid/api/client/contacts/colleagues/piotr']);
        });

        it('reads the members held again where a presence change names nobody', async () => {
            const presence: Record<string, string> = { marta: 'Busy' };
            const { transport } = directory(presence);
            const channel = told();

            drawDirectory(transport, channel, 'colleagues');
            expect(await screen.findByText('Busy')).toBeDefined();

            presence['marta'] = 'Offline';
            act(() => {
                channel.say({ kind: 'presence.changed', members: [] });
            });

            expect(await screen.findByText('Offline')).toBeDefined();
        });

        it('reads nothing again for a book that holds no colleagues', async () => {
            const { transport, asked } = answering({ own: [pageOf([contact('anna', 'Anna Kowalska')], null)] });
            const channel = told();

            drawDirectory(transport, channel, 'own');
            expect(await screen.findByText('Anna Kowalska')).toBeDefined();

            act(() => {
                channel.say({ kind: 'presence.changed', members: [] });
            });

            expect(asked()).toHaveLength(1);
        });
    });
});
