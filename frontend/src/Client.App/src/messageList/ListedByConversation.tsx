// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState } from 'react';
import type {
    ClientFailure,
    ClientSession,
    ListedMailThread,
    MailAccount,
    MailFathomTransport,
    MailTimelinePageDirection,
} from '@mailfathom/client-backend';
import { Skeleton } from '../controls/Skeleton';
import { useLocalization } from '../localization/useLocalization';
import { windowOfHeights } from '../messageRows/rowWindow';
import { useSignalledChanges } from '../signals/signalledChanges';
import { drawsCategories } from '../categories/categoryWords';
import { rowVariantOf, titleStateOf, type ThreadRowVariant } from '../threadList/threadRows';
import { ThreadRow } from '../threadList/ThreadRow';
import { scopeReaches, type MailScope } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import {
    cursorAfter,
    nothingHeld,
    refillsHeldRows,
    refreshAsked,
    rowAt,
    trimmedWithoutRepeats,
    wantedFor,
    withoutRepeats,
    type HeldTimeline,
    type TimelineRead,
} from './heldTimeline';
import { ListFailed, ListNote } from './ListNotices';
import { ListSettings } from './ListSettings';
import { emptyReason, narrowedToView, type MailListing } from './listing';
import { rememberedListing, rememberListing } from './rememberedListings';
import { conversationPages, usePageReads, useSignalledPages } from './timelinePages';
import { useListedMail } from './useListedMail';

// The mail list grouped by conversation: one row per conversation of the account picked, or of every account, standing
// where its newest message that passed the filters does. It is the message list read another way rather than a list
// of its own — the same route, the same filters and order, the same window of pages either side of the screen, and the
// same rule for reading a page again — so what is here is only what a conversation's row needs that a message's does
// not, and what a list of messages has that this one does not have at all: no selection, no acts, no row menu.
//
// A row is as tall as what its title says — a subject alone, a framed topic, or a framed topic with a line under it —
// and whether its categories stand a line of their own, so the window is laid out from the height each of those
// variants measured at rather than from one row height. Every row of a variant draws the same lines, so measuring one
// drawn row of it measures them all.
//
// It names no folder, because a conversation belongs to none: what narrows it is the account and the filters, and the
// list is keyed by that account where it is drawn, so picking another one starts a list rather than adjusting this one.

// What a row of each variant is laid out at before one of it has been drawn to measure. It only has to be close: the
// first row of the variant drawn replaces it.
const estimatedHeights: Readonly<Record<ThreadRowVariant, number>> = {
    subject: 96,
    topic: 116,
    standing: 134,
    subjectCategorized: 101,
    topicCategorized: 121,
    standingCategorized: 139,
};

const rowVariants: readonly ThreadRowVariant[] = [
    'subject',
    'topic',
    'standing',
    'subjectCategorized',
    'topicCategorized',
    'standingCategorized',
];

// The list opens at its leading end. Where a reader was in it is not kept across a visit, because a conversation moves
// to the top whenever a message arrives in it: a returning reader would be put back beside rows that are no longer
// where they were.
const leadingEnd: TimelineRead = { cursor: null, direction: 'forward', refilling: null };

function conversationOf(thread: ListedMailThread): string {
    return thread.threadId;
}

// A conversation's row is drawn from its newest message, so that is the message whose change changes the row.
function newestOf(thread: ListedMailThread): string {
    return thread.newest.id;
}

function variantOf(thread: ListedMailThread): ThreadRowVariant {
    return rowVariantOf(titleStateOf(thread), drawsCategories(thread.categories));
}

// A page is named by its cursor and the way it was read from it, which is how it is read again.
function pageKey(cursor: string | null, readAs: MailTimelinePageDirection): string {
    return `${readAs}\n${cursor ?? ''}`;
}

// One row of a page as it answered: the conversation it stood for, and the variant it was drawn as.
interface DrawnRow {
    readonly threadId: string;
    readonly variant: ThreadRowVariant;
}

function drawnRowOf(thread: ListedMailThread): DrawnRow {
    return { threadId: conversationOf(thread), variant: variantOf(thread) };
}

/**
 * How tall each row of the list stands, held or not: a held row by its variant, and a row of a page the list has let go
 * of by the variant it was drawn as when that page last answered, so letting a page go moves nothing on the screen.
 *
 * A page let go of keeps the count it was drawn at, which leaves out a conversation standing higher up, so its rows are
 * laid out from what it answered less the conversations already laid out above it — the same rule it was drawn by.
 */
function heightsOf(
    shown: HeldTimeline<ListedMailThread>,
    drawnAs: ReadonlyMap<string, readonly DrawnRow[]>,
    measured: ReadonlyMap<ThreadRowVariant, number>,
): readonly number[] {
    const heightOf = (variant: ThreadRowVariant): number => measured.get(variant) ?? estimatedHeights[variant];
    const above = new Set<string>();

    return shown.slots.flatMap((slot) => {
        const rows =
            slot.rows?.map(drawnRowOf) ??
            (drawnAs.get(pageKey(slot.askedWith, slot.readAs)) ?? []).filter((row) => !above.has(row.threadId));

        for (const row of rows) {
            above.add(row.threadId);
        }

        return Array.from({ length: slot.rowCount }, (_, at) => heightOf(rows[at]?.variant ?? 'subject'));
    });
}

export function ListedByConversation({
    session,
    transport,
    scope,
    accounts,
    online,
    onOpen,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The account whose conversations are listed, or every account — never a folder. */
    readonly scope: MailScope;

    readonly accounts: readonly MailAccount[];
    readonly online: boolean;

    /** Opens the conversation at its newest message, as a conversation rather than as one message of it. */
    readonly onOpen: (
        storedEmailId: string,
        subject: string | null,
        threadId: string | null,
        byThread: boolean,
    ) => void;
}) {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    const listed = useListedMail();
    const signalledChanges = useSignalledChanges();

    // How the list is read, which is kept across a visit as a folder's is: the order and the filters, and with them the
    // criteria an AI filter put in force.
    const [listing, setListing] = useState<MailListing>(() => {
        const { order, filters } = rememberedListing(session.baseAddress, scope, 'threads');

        return { order, filters };
    });

    const [held, setHeld] = useState<HeldTimeline<ListedMailThread>>(nothingHeld);
    const [failure, setFailure] = useState<ClientFailure | null>(null);

    // The conversations each page answered with when it last answered, and the variants they were drawn as, by the
    // page, which is what a page the list has let go of is laid out from. One entry per page read, replaced when the
    // page is read again.
    const [drawnAs, setDrawnAs] = useState<ReadonlyMap<string, readonly DrawnRow[]>>(() => new Map());
    const [measured, setMeasured] = useState<ReadonlyMap<ThreadRowVariant, number>>(() => new Map());
    const [scrollTop, setScrollTop] = useState(0);
    const [viewport, setViewport] = useState(0);
    const scroller = useRef<HTMLDivElement>(null);
    const rowElements = useRef(new Map<string, HTMLLIElement>());

    // A conversation that moved to the top since a page further down was read stands on both until that page is read
    // again, and it is drawn once — where the page that moved it put it.
    const shown = withoutRepeats(held, conversationOf);
    const heights = heightsOf(shown, drawnAs, measured);
    const rowCount = heights.length;
    const drawn = windowOfHeights(heights, scrollTop, viewport);
    const lastDrawn = drawn.first + drawn.count - 1;

    // The page wanted next, worked out during render for the reason the message list gives: it is a function of what is
    // held and where the window is. A failure and a network gap both stop it.
    const wanted =
        !online || failure !== null
            ? null
            : shown.slots.length === 0
              ? leadingEnd
              : wantedFor(shown, drawn.first, lastDrawn);

    usePageReads({
        session,
        transport,
        scope,
        listing,
        reader: conversationPages,
        wanted,
        quietly: refillsHeldRows(shown, wanted),
        setHeld,
        setFailure,
        onPage: (threads, _askedAt, asked) => {
            setDrawnAs((current) =>
                new Map(current).set(pageKey(asked.cursor, asked.direction), threads.map(drawnRowOf)),
            );
        },
    });

    useSignalledPages({ scope, setHeld, setFailure, messageOf: newestOf });

    // A message read or unread changes how many of a conversation's messages are unread, which its row draws — and the
    // message can be any of them, while a row names only its newest. So a statement about flags marks every page held,
    // as a refresh does, and what is read again is only the pages on the screen.
    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (signal.kind === 'mail.flags.changed' && scopeReaches(scope, signal.account, signal.folder)) {
                    setHeld(refreshAsked);
                }
            }),
        [signalledChanges, scope],
    );

    // The scroller's height is something outside React, which is what an effect is for, and it changes with the window.
    // The scroller is drawn only once there are rows to draw, so the observer is placed when there first are.
    const listedAny = rowCount > 0;

    useEffect(() => {
        const element = scroller.current;

        if (element === null || typeof ResizeObserver !== 'function') {
            return;
        }

        // An observer reports what it observes as soon as it starts, so the first height arrives without asking for it.
        const watched = new ResizeObserver(() => {
            setViewport(element.clientHeight);
        });

        watched.observe(element);

        return () => {
            watched.disconnect();
        };
    }, [listedAny]);

    // Each variant's height is read off the drawn rows of it as they are laid out and whenever the width moves them, so
    // the window is laid out from what the rows measure at this width rather than from the estimate. One observer for
    // every row, made with the first row drawn and let go with the list.
    const rowSizes = useRef<ResizeObserver | null>(null);

    useEffect(
        () => () => {
            rowSizes.current?.disconnect();
        },
        [],
    );

    function measure(threadId: string, element: HTMLLIElement | null): void {
        if (rowSizes.current === null && typeof ResizeObserver === 'function') {
            rowSizes.current = new ResizeObserver((entries) => {
                for (const { target } of entries) {
                    const variant = target instanceof HTMLElement ? variantNamed(target.dataset['threadRow']) : null;
                    const height = target instanceof HTMLElement ? target.offsetHeight : 0;

                    if (variant !== null && height > 0) {
                        setMeasured((current) =>
                            current.get(variant) === height ? current : new Map(current).set(variant, height),
                        );
                    }
                }
            });
        }

        const before = rowElements.current.get(threadId);

        if (before !== undefined) {
            rowSizes.current?.unobserve(before);
            rowElements.current.delete(threadId);
        }

        if (element !== null) {
            rowElements.current.set(threadId, element);
            rowSizes.current?.observe(element);
        }
    }

    // Scrolling is where the list stops holding what the reader has moved away from, on the message list's rule: the
    // pages either side of the screen keep their rows, and every page further off keeps only its place.
    function scrolledTo(top: number): void {
        setScrollTop(top);

        const moved = windowOfHeights(heights, top, viewport);

        setHeld((current) =>
            trimmedWithoutRepeats(current, moved.first, moved.first + moved.count - 1, conversationOf),
        );
    }

    function readWith(chosen: MailListing): void {
        // Written down here, because how the list is read is a choice somebody made: leaving the moment after making it
        // keeps it. The cursor belongs to the order and the filters it was issued under, so the list starts again at
        // its leading end.
        rememberListing(
            session.baseAddress,
            scope,
            { ...chosen, readingsShown: true, cursor: null, readAs: 'forward', rowInPage: 0 },
            'threads',
        );
        setListing(chosen);
        setHeld(nothingHeld);
        setFailure(null);
        setScrollTop(0);
    }

    // A press on an AI filter is a shortcut to filters on the list in front of the reader, and lands here as any other
    // filter change does. Selecting everything and taking focus back belong to a list of messages, which this is not.
    useEffect(() => {
        listed.listing({
            selectAll: () => undefined,
            takeFocus: () => undefined,
            stand: (view) => {
                readWith({ ...listing, filters: narrowedToView(listing.filters, view, new Date()) });
            },
        });

        return () => {
            listed.listing(null);
        };
    });

    function tryAgain(): void {
        setFailure(null);
    }

    if (!online && rowCount === 0) {
        return <ListNote>{translate('connection.offline')}</ListNote>;
    }

    if (rowCount === 0 && failure !== null) {
        return <ListFailed failure={failure} said="threadList.failed" partial={false} onRetry={tryAgain} />;
    }

    if (rowCount === 0 && wanted !== null) {
        return <ListWaiting />;
    }

    return (
        <div className="flex min-h-0 flex-1 animate-content-arrival flex-col">
            <ListSettings
                listing={listing}
                junkAskable={false}
                readingsShown={false}
                onRead={readWith}
                onDrawReadings={null}
            />

            {failure === null ? null : (
                <ListFailed failure={failure} said="threadList.partiallyFailed" partial onRetry={tryAgain} />
            )}

            {rowCount === 0 ? (
                <ListNote announced>{translate(emptyReason(accounts, scope, listing, 'threadList.empty'))}</ListNote>
            ) : (
                <div
                    ref={scroller}
                    className="rows-windowed min-h-0 flex-1 overflow-y-auto overscroll-contain"
                    onScroll={(event) => {
                        scrolledTo(event.currentTarget.scrollTop);
                    }}
                >
                    <div aria-hidden="true" style={{ height: `${String(drawn.above)}px` }} />

                    <ul
                        aria-label={translate('threadList.label')}
                        aria-busy={wanted !== null}
                        className="flex flex-col"
                    >
                        {Array.from({ length: drawn.count }, (_, at) => drawn.first + at).map((row) => {
                            const thread = rowAt(shown, row);

                            return thread === null ? (
                                <ArrivingRow key={`arriving-${String(row)}`} height={heights[row] ?? 0} />
                            ) : (
                                <ThreadRow
                                    key={thread.threadId}
                                    thread={thread}
                                    open={workspace.conversation?.threadId === thread.threadId}
                                    onOpen={() => {
                                        onOpen(thread.newest.id, thread.newest.subject, thread.threadId, true);
                                    }}
                                    onElement={(element) => {
                                        measure(thread.threadId, element);
                                    }}
                                />
                            );
                        })}
                    </ul>

                    <div aria-hidden="true" style={{ height: `${String(drawn.below)}px` }} />

                    {wanted !== null ? (
                        <ListNote announced>{translate('list.readingMore')}</ListNote>
                    ) : cursorAfter(shown) === null ? (
                        <ListNote>{translate('threadList.wholeListRead')}</ListNote>
                    ) : null}
                </div>
            )}
        </div>
    );
}

function variantNamed(value: string | undefined): ThreadRowVariant | null {
    return rowVariants.find((variant) => variant === value) ?? null;
}

// A row standing where a page the list let go of used to be, at the height the conversation stood at, so the space is
// visibly a conversation on its way rather than a hole.
function ArrivingRow({ height }: { readonly height: number }) {
    const { translate } = useLocalization();

    return (
        <li
            className="flex items-center border-b border-b-sunken px-3 text-sm text-faint"
            style={{ height: `${String(height)}px` }}
        >
            {translate('threadList.rowArriving')}
        </li>
    );
}

// How long each waiting row's lines are, ragged as the message list's are, so a column of them reads as a list.
const waitingRowLengths: readonly number[] = [74, 58, 68, 50, 71, 61];

// The list before it has answered, in the shape its rows take: the avatar and the names, the title, and the two lines
// of the quotation, so nothing moves when the answer lands.
function ListWaiting() {
    const { translate } = useLocalization();

    return (
        <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
            <p className="sr-only" role="status">
                {translate('threadList.reading')}
            </p>

            {waitingRowLengths.map((length) => (
                <div
                    key={length}
                    aria-hidden="true"
                    className="flex shrink-0 flex-col gap-1.5 border-s-4 border-b border-s-transparent border-b-sunken py-3.5 ps-3 pe-4 workspace:gap-1.25 workspace:py-3 workspace:ps-2.5 workspace:pe-3.5"
                >
                    <div className="flex items-center gap-2">
                        <Skeleton className="size-8.5 shrink-0 rounded-full workspace:size-5.5" />
                        <Skeleton className="h-2.25" fills={Math.max(28, length - 26)} />
                    </div>

                    <Skeleton className="h-2.25" fills={length} />
                    <div className="h-thread-snippet" />
                </div>
            ))}
        </div>
    );
}
