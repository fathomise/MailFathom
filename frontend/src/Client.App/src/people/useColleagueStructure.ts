// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    readColleagueStructure,
    type ClientFailureReason,
    type ClientSession,
    type ColleagueStructure,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { useSignalledChanges } from '../signals/signalledChanges';

// Where the colleague somebody has opened sits in their organization: the managers above them and who reports to them.
// It is read apart from the colleague because the directory composes it out of every member while the colleague is one
// row, so the card it feeds draws its own wait rather than holding the page up — and it is keyed on the colleague and
// the credential for the reason `useContactCorrespondence.ts` is: an answer for the person just left is never drawn
// under the person just opened.

/** What one colleague's structure says, and the way out of a read that did not answer. */
export interface ColleagueStructureInForce {
    /** The structure, or `null` while nothing has answered. */
    readonly structure: ColleagueStructure | null;

    readonly reading: boolean;

    /** Why the structure did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    readonly readAgain: () => void;
}

interface HeldStructure {
    readonly colleagueId: string | null;
    readonly session: ClientSession | null;
    readonly structure: ColleagueStructure | null;
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
}

function nothingRead(colleagueId: string | null, session: ClientSession | null): HeldStructure {
    return {
        colleagueId,
        session,
        structure: null,
        reading: colleagueId !== null && session !== null,
        failure: null,
    };
}

/**
 * Holds where one colleague sits in the reader's organization.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with.
 * @param transport How a request reaches the deployment.
 * @param colleagueId The colleague opened, or `null` where none is.
 * @returns What the structure said, and the way out of a read that did not answer.
 */
export function useColleagueStructure(
    session: ClientSession | null,
    transport: MailFathomTransport,
    colleagueId: string | null,
): ColleagueStructureInForce {
    const [held, setHeld] = useState<HeldStructure>(() => nothingRead(colleagueId, session));
    const latest = useRef(0);
    const signalled = useSignalledChanges();

    // Who the structure last read names, so a presence change about one of them reads it again and one about anybody
    // else does not.
    const named = useRef<readonly string[]>([]);

    const read = useCallback(async (): Promise<void> => {
        if (session === null || colleagueId === null) {
            return;
        }

        const asked = ++latest.current;
        const answer = await readColleagueStructure(session, transport, colleagueId);

        if (asked !== latest.current) {
            return;
        }

        named.current =
            answer.outcome === 'read'
                ? [...answer.value.managers, ...answer.value.directReports].map(({ id }) => id)
                : [];
        setHeld({
            colleagueId,
            session,
            structure: answer.outcome === 'read' ? answer.value : null,
            reading: false,
            failure: answer.outcome === 'failed' ? answer.failure.reason : null,
        });
    }, [colleagueId, session, transport]);

    useEffect(() => {
        void read();
    }, [read]);

    useEffect(
        () =>
            signalled.listen((change) => {
                if (
                    change.kind === 'refresh' ||
                    (change.kind === 'presence.changed' &&
                        (change.members.length === 0 || change.members.some((id) => named.current.includes(id))))
                ) {
                    void read();
                }
            }),
        [read, signalled],
    );

    const standing =
        held.colleagueId === colleagueId && held.session === session ? held : nothingRead(colleagueId, session);

    return {
        structure: standing.structure,
        reading: standing.reading,
        failure: standing.failure,
        readAgain: () => {
            setHeld({ ...standing, reading: true, failure: null });
            void read();
        },
    };
}
