// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    contactsPerPage,
    mostContactsPerPage,
    readColleagues,
    readCollectedContacts,
    readOwnContacts,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type Colleague,
    type Contact,
    type ContactPageRequest,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { useSignalledChanges, type SignalledChange } from '../signals/signalledChanges';

// One book of the address book as a screen holds it: the page or pages read so far, where the walk continues, and
// whether it is still going. The books are read the same way and are calls of this rather than one reader with a
// filter, which is the shape the surface publishes — what somebody wrote down, who their organization lists as its
// members, and what their own mailboxes picked up are three books. The members are a directory rather than a book of
// contacts, so a row here is a `Person`: a contact, or a colleague, told apart by the book each names.
//
// **Each book is read from the leading end when it is asked for**, rather than both being held at once. A tab is a
// deliberate move between two lists rather than a filter over one, and holding the other book's pages against the
// chance of coming back would be a copy of somebody's correspondents kept for a screen nobody is looking at.
//
// **A write is followed by reading the book again rather than by editing what is held.** Recording, promoting and
// erasing each change where a person sorts in the book — and a promotion moves them between the two books entirely —
// so a held list corrected in place would be this client's own opinion of an ordering the deployment owns.
//
// **What is held names the book and the credential it was read for**, and a reading for either of those having changed
// is derived away during the render that observes it rather than cleared by an effect. That is what keeps the tab a
// reader has just pressed from drawing the other one's rows for a frame.
//
// **What is held moves while the book is open**, and is read again in place rather than from nothing. A book of
// contacts is read again when the deployment says one of the reader's books was written — by them on another device, by
// an agent, by an administrator, or by collection from arriving mail — and on every refresh. The colleagues are read
// again on the refresh alone: nobody is told when a colleague's presence moves, so the refresh is what keeps it current,
// and how a colleague whose client simply expired comes to read as gone. Every row held is read again, in as many pages
// as that takes, and the answer replaces what was held only once every page has answered — so the deployment stays the
// one deciding what a reader may see of somebody, and a reader deep in a long directory keeps every row they reached.

/** Which book a screen is reading: the reader's own, their organization's members, or what their mail collected. */
export type ContactBookName = 'own' | 'colleagues' | 'collected';

/** One person the People space lists: a contact in one of the reader's books, or a member of their organization. */
export type Person = Contact | Colleague;

/** What one book holds, and the two ways a screen asks it for more. */
export interface ContactBookInForce {
    /** The people read so far, in the order the deployment walks the book. */
    readonly contacts: readonly Person[];

    /** Whether nothing has answered yet, which is the wait the list says it is in. */
    readonly reading: boolean;

    /** Whether a further page is on its way, which is the wait drawn at the foot of the rows already there. */
    readonly paging: boolean;

    /** Why the book did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Whether the walk has reached the end of the book, so there is nothing further to ask for. */
    readonly complete: boolean;

    /** Asks for the page after the ones held, which a reader reaching the foot of the list is what calls. */
    readonly readMore: () => void;

    /** Reads the book again from the leading end: the way out of a failure, and what every write is followed by. */
    readonly readAgain: () => void;
}

// What is held, beside which book and which credential it belongs to.
interface HeldBook {
    readonly book: ContactBookName;
    readonly session: ClientSession | null;
    readonly contacts: readonly Person[];
    readonly cursor: string | null;
    readonly complete: boolean;
    readonly reading: boolean;
    readonly paging: boolean;
    readonly failure: ClientFailureReason | null;
}

// What one book's pages read again from the leading end answered: every row, and where the walk continues.
interface ReadAgain {
    readonly contacts: readonly Person[];
    readonly nextCursor: string | null;
}

// One page of whichever book is asked for, in the one shape this holds. The directory answers its members under a
// name of its own, which is the only difference between reading it and reading a book.
async function pageOf(
    session: ClientSession,
    transport: MailFathomTransport,
    book: ContactBookName,
    page: ContactPageRequest,
): Promise<ClientResult<{ readonly contacts: readonly Person[]; readonly nextCursor: string | null }>> {
    if (book !== 'colleagues') {
        return (book === 'own' ? readOwnContacts : readCollectedContacts)(session, transport, page);
    }

    const answer = await readColleagues(session, transport, page);

    return answer.outcome === 'failed'
        ? answer
        : { outcome: 'read', value: { contacts: answer.value.colleagues, nextCursor: answer.value.nextCursor } };
}

/**
 * Reads a book again from the leading end until at least as many rows as were held have answered, or the book ends, in
 * pages of at most what one page may hold; `null` where any page did not answer, so what is drawn is never half old and
 * half new.
 */
async function readHeldPagesAgain(
    session: ClientSession,
    transport: MailFathomTransport,
    book: ContactBookName,
    held: number,
): Promise<ReadAgain | null> {
    const wanted = Math.max(held, contactsPerPage);
    const contacts: Person[] = [];
    let cursor: string | null = null;

    do {
        const pageSize = Math.min(Math.max(wanted - contacts.length, contactsPerPage), mostContactsPerPage);
        const answer = await pageOf(session, transport, book, { cursor, pageSize });

        if (answer.outcome === 'failed') {
            return null;
        }

        contacts.push(...answer.value.contacts);
        cursor = answer.value.nextCursor;
    } while (cursor !== null && contacts.length < wanted);

    return { contacts, nextCursor: cursor };
}

function nothingRead(book: ContactBookName, session: ClientSession | null): HeldBook {
    return {
        book,
        session,
        contacts: [],
        cursor: null,
        complete: false,
        reading: session !== null,
        paging: false,
        failure: null,
    };
}

/**
 * Holds one half of the signed-in person's address book.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — in which case nothing is read
 * and the screen draws a book that never answered rather than an empty one.
 * @param transport How a request reaches the deployment.
 * @param book Which half of the book to read.
 * @returns What is held, and the two ways a screen asks for more of it.
 */
export function useContactBook(
    session: ClientSession | null,
    transport: MailFathomTransport,
    book: ContactBookName,
): ContactBookInForce {
    const [held, setHeld] = useState<HeldBook>(() => nothingRead(book, session));

    // Which read is the current one. A tab changed, a credential replaced, or a book read again while a page is still
    // in flight each leave an answer on its way that must not be drawn: the ordering is not guaranteed, and a stale
    // page appended to a book somebody has since left reads as a rendering defect rather than as a race.
    const latest = useRef(0);

    const readFrom = useCallback(
        async (cursor: string | null): Promise<void> => {
            if (session === null) {
                return;
            }

            const asked = ++latest.current;
            const answer = await pageOf(session, transport, book, { cursor });

            if (asked !== latest.current) {
                return;
            }

            setHeld((standing) => {
                const carried =
                    standing.book === book && standing.session === session ? standing : nothingRead(book, session);

                if (answer.outcome === 'failed') {
                    return { ...carried, reading: false, paging: false, failure: answer.failure.reason };
                }

                return {
                    book,
                    session,
                    // Appended where this was a further page and replaced where it was the first, which is the one
                    // place the cursor a read started from decides what its answer means.
                    contacts: cursor === null ? answer.value.contacts : [...carried.contacts, ...answer.value.contacts],
                    cursor: answer.value.nextCursor,
                    complete: answer.value.nextCursor === null,
                    reading: false,
                    paging: false,
                    failure: null,
                };
            });
        },
        [book, session, transport],
    );

    // The read that starts the book, which is a request going out and therefore the one thing an effect is for. It runs
    // again when the tab changes or the credential does; nothing is cleared here, because what is held names the book
    // it was read for and a reading for another one is derived away below.
    useEffect(() => {
        void readFrom(null);
    }, [readFrom]);

    const standing = held.book === book && held.session === session ? held : nothingRead(book, session);

    // What is held as the signal arrives, read in the answer rather than closed over by the subscription.
    const holding = useRef(standing);

    useEffect(() => {
        holding.current = standing;
    });

    const signalled = useSignalledChanges();
    const rereads = useRef(0);

    useEffect(() => {
        if (session === null) {
            return;
        }

        const readHeldAgain = async (): Promise<void> => {
            const asked = ++rereads.current;
            const answer = await readHeldPagesAgain(session, transport, book, holding.current.contacts.length);

            if (asked !== rereads.current || answer === null) {
                return;
            }

            setHeld((current) =>
                current.book === book && current.session === session
                    ? {
                          ...current,
                          contacts: answer.contacts,
                          cursor: answer.nextCursor,
                          complete: answer.nextCursor === null,
                      }
                    : current,
            );
        };

        // A contacts change says nothing of the directory, which is read from user records rather than from a book.
        const everyHeldChanged = (change: SignalledChange): boolean =>
            change.kind === 'refresh' || (book !== 'colleagues' && change.kind === 'contacts.changed');

        return signalled.listen((change) => {
            if (everyHeldChanged(change)) {
                void readHeldAgain();
            }
        });
    }, [book, session, transport, signalled]);

    return {
        contacts: standing.contacts,
        reading: standing.reading,
        paging: standing.paging,
        failure: standing.failure,
        complete: standing.complete,
        readMore: () => {
            if (standing.paging || standing.reading || standing.cursor === null) {
                return;
            }

            setHeld({ ...standing, paging: true, failure: null });
            void readFrom(standing.cursor);
        },
        readAgain: () => {
            setHeld({ ...standing, reading: true, failure: null });
            void readFrom(null);
        },
    };
}
