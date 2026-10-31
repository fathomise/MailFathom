// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState } from 'react';
import {
    longestContactSearch,
    mostContactsPerPage,
    readColleagues,
    type ClientSession,
    type Colleague,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { wordAbsenceEnd } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';

// The colleagues among a message's *To* who are out of office, each said under the recipients before anybody waits on
// an answer: their name and the last day their automatic reply runs to, never what it says.
//
// **Each address is asked of the directory, whoever put it there.** A header holds addresses rather than people, and
// an address reaches it from a suggestion, from somebody typing it, from the message being answered, and from a draft
// opened again — so what decides whether somebody is a colleague is the directory's answer for the address, not the
// route it came by. The answer is kept for as long as the composer is open, so an address is asked about once, and a
// failed read is kept as nobody: this is a courtesy beside the message, and a sentence about a directory that did not
// answer would be a refusal nobody asked about.
//
// The directory is searched by the address, which also matches every colleague whose own address contains it, so one
// page is read at the largest size the service serves and the colleague holding the address exactly is picked out of
// it. A directory with more colleagues than that containing one address would miss them, which no organization's
// naming has been seen to produce; reading further pages is the way past it.

/** Two spellings of one address are one recipient, which is how the book itself compares them. */
function addressKey(address: string): string {
    return address.toLowerCase();
}

async function colleagueAt(
    session: ClientSession,
    transport: MailFathomTransport,
    address: string,
): Promise<Colleague | null> {
    const answer = await readColleagues(session, transport, { search: address, pageSize: mostContactsPerPage });

    return answer.outcome === 'read'
        ? (answer.value.colleagues.find((colleague) =>
              colleague.addresses.some((held) => addressKey(held) === address),
          ) ?? null)
        : null;
}

export function AbsentRecipients({
    addresses,
    session,
    transport,
}: {
    readonly addresses: readonly string[];
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
}) {
    const { locale, translate } = useLocalization();
    const readingZone = useReadingZone();
    const [found, setFound] = useState<ReadonlyMap<string, Colleague | null>>(new Map());

    const keys = [...new Set(addresses.map(addressKey))];
    const unasked = keys.filter((key) => !found.has(key) && key.length <= longestContactSearch).join('\n');

    // Asking the directory is a request going out, which is what an effect is for. The answers are discarded where the
    // header moved on before they arrived, and the addresses still unasked then are asked again.
    useEffect(() => {
        if (unasked === '') {
            return;
        }

        let listening = true;
        const asked = unasked.split('\n');

        void Promise.all(asked.map((key) => colleagueAt(session, transport, key))).then((answers) => {
            if (listening) {
                setFound(
                    (before) =>
                        new Map([...before, ...asked.map((key, index) => [key, answers[index] ?? null] as const)]),
                );
            }
        });

        return () => {
            listening = false;
        };
    }, [unasked, session, transport]);

    const absent = keys.flatMap((key) => {
        const colleague = found.get(key) ?? null;
        const until = colleague?.outOfOfficeUntil ?? null;
        const day = until === null ? null : wordAbsenceEnd(until, locale, readingZone);

        return colleague === null || day === null ? [] : [{ key, name: colleague.displayName, day }];
    });

    return absent.map(({ key, name, day }) => (
        <p
            key={key}
            role="status"
            className="flex items-start gap-2.25 border-b border-warning bg-warning-soft px-3.75 py-2.25 text-sm/relaxed text-warning-text pointer-coarse:py-2.75 pointer-coarse:text-base"
        >
            <Icon name="event_busy" className="size-icon-md shrink-0" />
            <span className="min-w-0 flex-1 text-pretty">
                {translate('compose.recipientOutOfOffice', { name, day })}
            </span>
        </p>
    ));
}
