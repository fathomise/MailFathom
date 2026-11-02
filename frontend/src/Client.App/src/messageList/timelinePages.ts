// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent } from 'react';
import {
    conversationPageSize,
    readMailConversations,
    readMailTimeline,
    type ClientFailure,
    type ClientResult,
    type ClientSession,
    type ListedMailThread,
    type MailFathomTransport,
    type MailTimelineEntry,
    type MailTimelineQuery,
} from '@mailfathom/client-backend';
import { useSignalledChanges } from '../signals/signalledChanges';
import { scopeReaches, type MailScope } from '../workspace/mailScope';
import {
    answered,
    arrivalNoticed,
    changeNoticed,
    refreshAsked,
    refreshUnanswered,
    type HeldTimeline,
    type TimelinePage,
    type TimelineRead,
} from './heldTimeline';
import { queryFor, type MailListing } from './listing';

// How the mail list reads its pages and what it does when the deployment says something changed, which is one rule
// whichever way the list is read: by message, or grouped by conversation. The two readings draw different rows, and
// that is the whole of the difference — both keep a window of pages rather than every page read, both read again only
// the stale pages on the screen, each from its own cursor, and both react to the same signals the same way.
//
// So a list grouped by conversation never reads its pages again in sequence from the first, however far down it was
// read: a refresh costs what it costs at the top.

/** How one reading of the list asks for a page, and what a row of it is. */
export type PageReader<Row> = (
    session: ClientSession,
    transport: MailFathomTransport,
    query: MailTimelineQuery,
) => Promise<ClientResult<TimelinePage<Row>>>;

/** The list read by message: one row per message. */
export const messagePages: PageReader<MailTimelineEntry> = async (session, transport, query) => {
    const result = await readMailTimeline(session, transport, query);

    return result.outcome === 'failed'
        ? result
        : {
              outcome: 'read',
              value: {
                  rows: result.value.emails,
                  nextCursor: result.value.nextCursor,
                  previousCursor: result.value.previousCursor,
              },
          };
};

/** The list grouped by conversation: one row per conversation, at the deployment's own page size for it. */
export const conversationPages: PageReader<ListedMailThread> = async (session, transport, query) => {
    const result = await readMailConversations(session, transport, { ...query, pageSize: conversationPageSize });

    return result.outcome === 'failed'
        ? result
        : {
              outcome: 'read',
              value: {
                  rows: result.value.conversations,
                  nextCursor: result.value.nextCursor,
                  previousCursor: result.value.previousCursor,
              },
          };
};

/**
 * Puts the page the list wants on the wire, and writes its answer where it belongs. The one effect a list reads with.
 *
 * What was asked for travels with the answer, so a page knows where it belongs and an answer to a read the list has
 * moved on from is discarded. A stale page read again while its rows are still drawn — `quietly` — says nothing when it
 * is not answered: the rows stay, and the next refresh asks again.
 *
 * @param onPage What the caller makes of a page as it lands, before it is held — called with the read's own rows, when
 * it was asked for, and what it asked for, which names the page.
 */
export function usePageReads<Row>({
    session,
    transport,
    scope,
    listing,
    reader,
    wanted,
    quietly,
    setHeld,
    setFailure,
    onPage,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly scope: MailScope;
    readonly listing: MailListing;
    readonly reader: PageReader<Row>;
    readonly wanted: TimelineRead | null;
    readonly quietly: boolean;
    readonly setHeld: (held: (current: HeldTimeline<Row>) => HeldTimeline<Row>) => void;
    readonly setFailure: (failure: ClientFailure) => void;
    readonly onPage: (rows: readonly Row[], askedAt: number, asked: TimelineRead) => void;
}): void {
    const landed = useEffectEvent(onPage);

    // Named apart so the effect depends on what the request *is* rather than on the object naming it. A fresh object
    // every render would put a request on the wire every render; these three change only when the page wanted changes,
    // which is what starts one read per page and abandons one the reader has scrolled away from.
    const wantedCursor = wanted?.cursor ?? null;
    const wantedDirection = wanted?.direction ?? null;
    const wantedRefilling = wanted?.refilling ?? null;

    useEffect(() => {
        if (wantedDirection === null) {
            return;
        }

        const asked: TimelineRead = { cursor: wantedCursor, direction: wantedDirection, refilling: wantedRefilling };
        let listening = true;
        const askedAt = Date.now();

        void reader(session, transport, queryFor(scope, listing, asked.cursor, asked.direction)).then((result) => {
            if (!listening) {
                return;
            }

            const refilled = asked.refilling;

            if (result.outcome === 'failed' && quietly && refilled !== null) {
                setHeld((current) => refreshUnanswered(current, refilled));
            } else if (result.outcome === 'failed') {
                setFailure(result.failure);
            } else {
                landed(result.value.rows, askedAt, asked);
                setHeld((current) => answered(current, result.value, asked));
            }
        });

        return () => {
            listening = false;
        };
    }, [
        session,
        transport,
        scope,
        listing,
        reader,
        wantedCursor,
        wantedDirection,
        wantedRefilling,
        quietly,
        setHeld,
        setFailure,
    ]);
}

/**
 * What the deployment said changed, acted on by marking the pages it concerns rather than by reading the list again
 * from its leading end: the read that follows is the one the window itself asks for, so the reader keeps their place
 * and the row under their pointer, and pages they have scrolled away from are read again when they reach them.
 *
 * A burst of signals is one read rather than one per signal: a page already marked is marked once, and what goes out is
 * whichever page the window wants next.
 *
 * @param messageOf The message a row was drawn from, which is what a signal naming mail is held against.
 */
export function useSignalledPages<Row>({
    scope,
    setHeld,
    setFailure,
    messageOf,
}: {
    readonly scope: MailScope;
    readonly setHeld: (held: (current: HeldTimeline<Row>) => HeldTimeline<Row>) => void;
    readonly setFailure: (failure: ClientFailure | null) => void;
    readonly messageOf: (row: Row) => string;
}): void {
    const signalledChanges = useSignalledChanges();

    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                // A refresh marks every page the list holds rather than dropping one, so the rows stay drawn while the
                // pages on the screen are read again — and a list that had stopped on a failure is let try again.
                if (signal.kind === 'refresh') {
                    setFailure(null);
                    setHeld(refreshAsked);
                }

                // A sender is recognized by the contact books as they stand when a row is read, so a contact written
                // or erased anywhere can move any row's mark: the pages on the screen are read again the same way.
                if (signal.kind === 'contacts.changed') {
                    setHeld(refreshAsked);
                }

                if (signal.kind === 'mail.arrived' && scopeReaches(scope, signal.account, signal.folder)) {
                    setHeld(arrivalNoticed);
                }

                // The pages holding the named rows are read again and nothing is washed here. What a signal names is
                // mail the deployment wrote down, which is not the same question as whether a reader would see the row
                // differently: the answer is what says that, and the read is where it is asked.
                if (signal.kind === 'mail.changed' && scopeReaches(scope, signal.account, signal.folder)) {
                    setHeld((current) => changeNoticed(current, signal.emails, messageOf));
                }
            }),
        [signalledChanges, scope, setHeld, setFailure, messageOf],
    );
}
