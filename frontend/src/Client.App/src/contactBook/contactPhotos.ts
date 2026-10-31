// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext, useEffect, useState } from 'react';
import {
    mostPersonPhotos,
    readPersonPhotos,
    type ClientSession,
    type MailFathomTransport,
    type PersonPhotoReference,
} from '@mailfathom/client-backend';
import type { ContactPhotoExchange } from '../deployment/portraitExchange';
import type { Person } from './useContactBook';

// The photos of the people the client draws, which the frame hands every avatar below it through context rather than
// down the tree: a person is drawn on a row of the list, at the head of a message, on the People screen, and in an AI
// answer, each several components below the frame, so the reading is said once here and asked for wherever a person
// is drawn.
//
// **A photo is read where it is drawn, not for a whole list at once.** The list is windowed, so a row nobody scrolls
// to asks for nothing; and a person with no photo asks for nothing either, the initials being what is drawn instead.
// What says which photo a person has is the response that drew them — a contact, a colleague, a mail row, a message —
// so no list asks for photos one row at a time. An AI answer is the one place that says nothing about photos, so its
// people are resolved by address once per block.
//
// **What was read is kept by the photo's own name for as long as the session stands.** A person names the stored file
// their photo is, and that name changes whenever the photo does — so a list read again after a write asks again for
// exactly the photos that changed, and nothing else. Only a photo drawn is kept: a read that was abandoned or refused
// is asked again by the next avatar that wants it rather than remembered as a person with no photo.
//
// A member of the organization is drawn by the portrait their own record holds, which is read at the directory's route
// and named by a revision of the directory's own — so it is kept apart from the books' photos rather than in one map.

/** What a photo is read by: the person it belongs to, which route serves it, and which stored file it is. */
export type PhotoOfPerson = Pick<Person, 'id' | 'book' | 'photo'>;

/** Reads the photos of the people the client draws. */
export interface ContactPhotoReader {
    /** Reads one person's photo, answering an address it may be drawn at or `null` where there is none. */
    read(person: Pick<Person, 'id' | 'book'>, photo: string, abandoned: AbortSignal): Promise<string | null>;

    /** Resolves which photo each address is drawn by, answering nothing for an address drawn by initials. */
    resolve(addresses: readonly string[]): Promise<ReadonlyMap<string, PersonPhotoReference>>;
}

/** The reader of the session that draws people, or `null` outside one — where every person is drawn by initials. */
export const ContactPhotoContext = createContext<ContactPhotoReader | null>(null);

const resolvedNobody: ReadonlyMap<string, PersonPhotoReference> = new Map();

/**
 * A reader of the pictures over one session, keeping each photo it drew by the name of the stored file it is.
 *
 * Which photo an address is drawn by is the frame's to add, through {@link photosOfAddresses}, because it goes out on
 * the frame's own transport.
 */
export function contactPhotoReader(
    session: ClientSession,
    exchange: ContactPhotoExchange,
): Pick<ContactPhotoReader, 'read'> {
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

/** Which photo each of a set of addresses is drawn by, answering nobody where the deployment would not say. */
export async function photosOfAddresses(
    session: ClientSession,
    transport: MailFathomTransport,
    addresses: readonly string[],
): Promise<ReadonlyMap<string, PersonPhotoReference>> {
    // A refusal leaves everybody on their initials, which is what an answer drawn without photos looks like.
    const answer = await readPersonPhotos(session, transport, addresses.slice(0, mostPersonPhotos));

    return answer.outcome === 'read' ? answer.value : resolvedNobody;
}

/** The address a person's photo may be drawn at, or `null` while there is none to draw — the initials stand in. */
export function useContactPhoto(person: PhotoOfPerson | null): string | null {
    const reader = useContext(ContactPhotoContext);
    const [drawn, setDrawn] = useState<{ readonly photo: string; readonly picture: string } | null>(null);
    const photo = person?.photo ?? null;

    // The person is read through the two fields the photo depends on, so a list read again — which hands every row a
    // new record of the same person — asks for nothing it already drew.
    const id = person?.id ?? null;
    const book = person?.book ?? null;

    useEffect(() => {
        if (reader === null || photo === null || id === null || book === null) {
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

    // Read against the photo the person names now, so a photo replaced or removed is never drawn from the answer an
    // earlier one was given.
    return drawn !== null && drawn.photo === photo ? drawn.picture : null;
}

/**
 * Which photo each of the people one block of an AI answer names is drawn by, asked once for the whole block.
 *
 * The answer was composed once and kept, so it names people by address alone; asking when it is drawn is what draws
 * somebody by the photo they have now rather than by none.
 */
export function usePhotosOfAddresses(addresses: readonly (string | null)[]): ReadonlyMap<string, PersonPhotoReference> {
    const reader = useContext(ContactPhotoContext);
    const [resolved, setResolved] = useState<{
        readonly asked: string;
        readonly photos: ReadonlyMap<string, PersonPhotoReference>;
    } | null>(null);

    // The addresses are compared as one string rather than as an array, because the block hands a new array on every
    // render and the set it names is what decides whether to ask again.
    const asked = [...new Set(addresses.filter((address) => address !== null))].join('\n');

    useEffect(() => {
        if (reader === null || asked === '') {
            return;
        }

        let listening = true;

        void reader.resolve(asked.split('\n')).then((photos) => {
            if (listening) {
                setResolved({ asked, photos });
            }
        });

        return () => {
            listening = false;
        };
    }, [reader, asked]);

    return resolved !== null && resolved.asked === asked ? resolved.photos : resolvedNobody;
}
