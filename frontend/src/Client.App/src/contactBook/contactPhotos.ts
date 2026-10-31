// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext, useEffect, useState } from 'react';
import type { ClientSession, Contact } from '@mailfathom/client-backend';
import type { ContactPhotoExchange } from '../deployment/portraitExchange';

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

/** Reads the photo of one person in the book, answering an address it may be drawn at or `null` where there is none. */
export interface ContactPhotoReader {
    read(contactId: string, photo: string, abandoned: AbortSignal): Promise<string | null>;
}

/** The reader of the space that draws the book, or `null` outside one — where every person is drawn by initials. */
export const ContactPhotoContext = createContext<ContactPhotoReader | null>(null);

/** A reader over one session, keeping each photo it drew by the name of the stored file it is. */
export function contactPhotoReader(session: ClientSession, exchange: ContactPhotoExchange): ContactPhotoReader {
    const drawn = new Map<string, string>();

    return {
        read: async (contactId, photo, abandoned) => {
            const held = drawn.get(photo);
            if (held !== undefined) {
                return held;
            }

            const answer = await exchange.read(session, contactId, abandoned);
            if (answer.outcome !== 'drawn') {
                return null;
            }

            drawn.set(photo, answer.picture);

            return answer.picture;
        },
    };
}

/** The address a person's photo may be drawn at, or `null` while there is none to draw — the initials stand in. */
export function useContactPhoto(contact: Contact): string | null {
    const reader = useContext(ContactPhotoContext);
    const [drawn, setDrawn] = useState<{ readonly photo: string; readonly picture: string } | null>(null);
    const photo = contact.photo;

    useEffect(() => {
        if (reader === null || photo === null) {
            return;
        }

        const abandoned = new AbortController();

        void reader.read(contact.id, photo, abandoned.signal).then((picture) => {
            if (!abandoned.signal.aborted && picture !== null) {
                setDrawn({ photo, picture });
            }
        });

        return () => {
            abandoned.abort();
        };
    }, [reader, contact.id, photo]);

    // Read against the photo the contact names now, so a photo replaced or removed is never drawn from the answer an
    // earlier one was given.
    return drawn !== null && drawn.photo === photo ? drawn.picture : null;
}
