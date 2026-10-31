// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext, useEffect, useState } from 'react';
import type { ClientSession } from '@mailfathom/client-backend';
import type { ContactPhotoExchange } from '../deployment/portraitExchange';
import type { Person } from './useContactBook';

// The photos of the people in the book, which the People space hands every avatar below it through context rather
// than down the list: the row that draws one sits two components below the space that can read it, and the person's
// own page is a third place, so the reading is said once here and asked for wherever a person is drawn.
//
// **A photo is read where it is drawn, not for the whole book at once.** The list is windowed, so a row nobody scrolls
// to asks for nothing; and a contact with no photo asks for nothing either, the initials being what is drawn instead.
//
// **What was read is kept by the photo's own name for as long as the space stands.** A contact names the stored file
// its photo is, and that name changes whenever the photo does — so a book read again after a write asks again for
// exactly the photos that changed, and nothing else. Only a photo drawn is kept: a read that was abandoned or refused
// is asked again by the next row that wants it rather than remembered as a person with no photo.
//
// A member of the organization is drawn by the portrait their own record holds, which is read at the directory's route
// and named by a revision of the directory's own — so it is kept apart from the books' photos rather than in one map.

/** Reads the photo of one person in the book, answering an address it may be drawn at or `null` where there is none. */
export interface ContactPhotoReader {
    read(person: Pick<Person, 'id' | 'book'>, photo: string, abandoned: AbortSignal): Promise<string | null>;
}

/** The reader of the space that draws the book, or `null` outside one — where every person is drawn by initials. */
export const ContactPhotoContext = createContext<ContactPhotoReader | null>(null);

/** A reader over one session, keeping each photo it drew by the name of the stored file it is. */
export function contactPhotoReader(session: ClientSession, exchange: ContactPhotoExchange): ContactPhotoReader {
    const drawn = new Map<string, string>();

    return {
        read: async (person, photo, abandoned) => {
            const member = person.book === 'Member';
            const kept = `${member ? 'member' : 'contact'} ${photo}`;
            const held = drawn.get(kept);
            if (held !== undefined) {
                return held;
            }

            const answer = member
                ? await exchange.readColleague(session, person.id, abandoned)
                : await exchange.read(session, person.id, abandoned);
            if (answer.outcome !== 'drawn') {
                return null;
            }

            drawn.set(kept, answer.picture);

            return answer.picture;
        },
    };
}

/** The address a person's photo may be drawn at, or `null` while there is none to draw — the initials stand in. */
export function useContactPhoto(person: Person): string | null {
    const reader = useContext(ContactPhotoContext);
    const [drawn, setDrawn] = useState<{ readonly photo: string; readonly picture: string } | null>(null);
    const photo = person.photo;

    // The person is read through the two fields the photo depends on, so a book read again — which hands every row a
    // new record of the same person — asks for nothing it already drew.
    const { id, book } = person;

    useEffect(() => {
        if (reader === null || photo === null) {
            return;
        }

        const abandoned = new AbortController();

        void reader.read({ id, book }, photo, abandoned.signal).then((picture) => {
            if (!abandoned.signal.aborted && picture !== null) {
                setDrawn({ photo, picture });
            }
        });

        return () => {
            abandoned.abort();
        };
    }, [reader, id, book, photo]);

    // Read against the photo the contact names now, so a photo replaced or removed is never drawn from the answer an
    // earlier one was given.
    return drawn !== null && drawn.photo === photo ? drawn.picture : null;
}
