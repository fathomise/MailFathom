// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useRef, useState, type ReactNode } from 'react';
import {
    readMailThreadList,
    type ClientFailure,
    type ClientFailureReason,
    type ClientSession,
    type ListedMailThread,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import { Skeleton } from '../controls/Skeleton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { windowOfHeights } from '../messageRows/rowWindow';
import { useSignalledChanges } from '../signals/signalledChanges';
import { useWorkspace } from '../workspace/useWorkspace';
import { rowVariantOf, titleStateOf, type ThreadRowVariant } from './threadRows';
import { ThreadRow } from './ThreadRow';

// The mail list read by conversation: one row per conversation of the account picked, or of every account, newest first
// by the arrival of each one's newest message. It is read a page at a time as the reader scrolls towards its end, and it
// is windowed because a mailbox's conversations run to far more rows than a document should hold.
//
// A row is as tall as what its title says — a subject alone, a framed topic, or a framed topic with a line under it — so
// the window is laid out from the height each of the three measured at rather than from one row height. Every row of a
// variant draws the same lines, so measuring one drawn row of it measures them all, and nothing moves once the first of
// each has been drawn.
//
// It reads nothing about a folder, because a conversation belongs to none: what narrows it is the account, and the list
// is keyed by that account where it is drawn, so picking another one starts a list rather than adjusting this one.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

// What a row of each variant is laid out at before one of it has been drawn to measure. It only has to be close: the
// first row of the variant drawn replaces it.
const estimatedHeights: Readonly<Record<ThreadRowVariant, number>> = { subject: 96, topic: 116, standing: 134 };

// How many rows before the end of what is held the next page is asked for, so the reader scrolling down meets rows
// rather than the sentence saying more are being read.
const readAheadRows = 8;

interface HeldThreads {
    readonly threads: readonly ListedMailThread[];

    /** The cursor the page after the last one held is asked with, or `null` where the list has been read to its end. */
    readonly nextCursor: string | null;

    /** How many pages are held, which is how far a list read again is read. */
    readonly pages: number;
}

export function ThreadList({
    session,
    transport,
    account,
    online,
    onOpen,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The account whose conversations are listed, or `null` for every account. */
    readonly account: string | null;

    readonly online: boolean;

    /** Opens the conversation at its newest message, as a conversation rather than as one message of it. */
    readonly onOpen: (thread: ListedMailThread) => void;
}) {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    const signalledChanges = useSignalledChanges();

    const [held, setHeld] = useState<HeldThreads | null>(null);
    const [failure, setFailure] = useState<ClientFailure | null>(null);
    const [asked, setAsked] = useState(false);

    // The list read again because something said it changed, and how many of its pages were held when it did. A
    // counter rather than a flag, so that two signals in a row are two reads rather than one.
    const [reread, setReread] = useState<{ readonly attempt: number; readonly pages: number }>({
        attempt: 0,
        pages: 0,
    });

    const [measured, setMeasured] = useState<ReadonlyMap<ThreadRowVariant, number>>(() => new Map());
    const [scrollTop, setScrollTop] = useState(0);
    const [viewport, setViewport] = useState(0);
    const scroller = useRef<HTMLDivElement>(null);
    const rows = useRef(new Map<string, HTMLLIElement>());

    const threads = held?.threads ?? [];
    const heights = threads.map((thread) => {
        const variant = rowVariantOf(titleStateOf(thread));

        return measured.get(variant) ?? estimatedHeights[variant];
    });
    const drawn = windowOfHeights(heights, scrollTop, viewport);

    // The page wanted next, or `undefined` where none is: the first page until one has answered, and the one after the
    // last page held once the window has come within reach of its end. A failure and a network gap both stop it.
    const nearTheEnd = drawn.first + drawn.count >= threads.length - readAheadRows;
    const wanted: string | null | undefined =
        !online || failure !== null
            ? undefined
            : held === null
              ? null
              : held.nextCursor !== null && (asked || nearTheEnd)
                ? held.nextCursor
                : undefined;
    const reading = wanted !== undefined;

    // The one effect that puts a page on the wire. An answer to a read this list has moved on from is discarded.
    useEffect(() => {
        if (wanted === undefined) {
            return;
        }

        let listening = true;

        void readMailThreadList(session, transport, account, wanted).then((result) => {
            if (!listening) {
                return;
            }

            if (result.outcome === 'failed') {
                setFailure(result.failure);

                return;
            }

            setAsked(false);
            setHeld((current) => ({
                threads:
                    wanted === null ? result.value.threads : [...(current?.threads ?? []), ...result.value.threads],
                nextCursor: result.value.nextCursor,
                pages: wanted === null ? 1 : (current?.pages ?? 0) + 1,
            }));
        });

        return () => {
            listening = false;
        };
    }, [session, transport, account, wanted]);

    // A refresh, or a change the deployment names about any mail, reads the list again as far as it had been read, and
    // replaces it only once every page has answered: a conversation's row moves when a message arrives in it, and which
    // conversations a page holds moves with it. One that did not answer leaves the list as it is.
    // How far it had been read is taken when the signal arrives rather than when the listener was made, so a signal
    // landing between a page arriving and a re-subscription cannot reread as far as an earlier list.
    const heldPages = held?.pages ?? 0;
    const readAgainAsFarAsHeld = useEffectEvent(() => {
        setReread((current) => ({ attempt: current.attempt + 1, pages: heldPages }));
    });

    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (signal.kind === 'refresh' || signal.kind === 'mail.changed' || signal.kind === 'contacts.changed') {
                    readAgainAsFarAsHeld();
                }
            }),
        [signalledChanges],
    );

    useEffect(() => {
        if (!online || reread.pages === 0) {
            return;
        }

        let listening = true;

        void readPagesAgain(session, transport, account, reread.pages).then((answered) => {
            if (listening && answered !== null) {
                setHeld(answered);
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, account, online, reread]);

    // The scroller's height is something outside React, which is what an effect is for, and it changes with the window.
    // The scroller is drawn only once there are rows to draw, so the observer is placed when there first are.
    const listed = threads.length > 0;

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
    }, [listed]);

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

        const before = rows.current.get(threadId);

        if (before !== undefined) {
            rowSizes.current?.unobserve(before);
            rows.current.delete(threadId);
        }

        if (element !== null) {
            rows.current.set(threadId, element);
            rowSizes.current?.observe(element);
        }
    }

    if (!online && held === null) {
        return <Note>{translate('connection.offline')}</Note>;
    }

    if (held === null && failure !== null) {
        return (
            <Failed
                failure={failure}
                said="threadList.failed"
                onRetry={() => {
                    setFailure(null);
                }}
            />
        );
    }

    if (held === null) {
        return <ListWaiting />;
    }

    if (threads.length === 0) {
        return <Note announced>{translate('threadList.empty')}</Note>;
    }

    return (
        <div className="flex min-h-0 flex-1 animate-content-arrival flex-col">
            {failure === null ? null : (
                <Failed
                    failure={failure}
                    said="threadList.partiallyFailed"
                    onRetry={() => {
                        setFailure(null);
                        setAsked(true);
                    }}
                />
            )}

            <div
                ref={scroller}
                className="rows-windowed min-h-0 flex-1 overflow-y-auto overscroll-contain"
                onScroll={(event) => {
                    setScrollTop(event.currentTarget.scrollTop);
                }}
            >
                <div aria-hidden="true" style={{ height: `${String(drawn.above)}px` }} />

                <ul aria-label={translate('threadList.label')} aria-busy={reading} className="flex flex-col">
                    {threads.slice(drawn.first, drawn.first + drawn.count).map((thread) => (
                        <ThreadRow
                            key={thread.threadId}
                            thread={thread}
                            open={workspace.conversation?.threadId === thread.threadId}
                            onOpen={() => {
                                onOpen(thread);
                            }}
                            onElement={(element) => {
                                measure(thread.threadId, element);
                            }}
                        />
                    ))}
                </ul>

                <div aria-hidden="true" style={{ height: `${String(drawn.below)}px` }} />

                {reading ? (
                    <Note announced>{translate('list.readingMore')}</Note>
                ) : held.nextCursor === null ? (
                    <Note>{translate('threadList.wholeListRead')}</Note>
                ) : null}
            </div>
        </div>
    );
}

const rowVariants: readonly ThreadRowVariant[] = ['subject', 'topic', 'standing'];

function variantNamed(value: string | undefined): ThreadRowVariant | null {
    return rowVariants.find((variant) => variant === value) ?? null;
}

/**
 * Reads the list again from its first page, as far as it had been read, or answers `null` where any page did not
 * answer — a list read again is replaced whole or not at all, so what is drawn is never half old and half new.
 */
async function readPagesAgain(
    session: ClientSession,
    transport: MailFathomTransport,
    account: string | null,
    pageCount: number,
): Promise<HeldThreads | null> {
    const threads: ListedMailThread[] = [];
    let cursor: string | null = null;
    let pages = 0;

    do {
        const result = await readMailThreadList(session, transport, account, cursor);

        if (result.outcome === 'failed') {
            return null;
        }

        threads.push(...result.value.threads);
        cursor = result.value.nextCursor;
        pages += 1;
    } while (cursor !== null && pages < pageCount);

    return { threads, nextCursor: cursor, pages };
}

function Failed({
    failure,
    said,
    onRetry,
}: {
    readonly failure: ClientFailure;
    readonly said: MessageKey;
    readonly onRetry: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex flex-wrap items-center gap-1.5 border-b border-line px-3 py-1.5">
            <p className="text-sm text-warning" role="alert">
                {translate(said, { reason: translate(failureLabels[failure.reason]) })}
            </p>

            {/* Reading again is the way out of exactly one of the five failures, for the reason
                `shell/ConnectionSummary.tsx` gives: the other four repeat identically on a second attempt. */}
            {failure.reason === 'unavailable' ? (
                <SecondaryButton label={translate('connection.retry')} onActivate={onRetry} />
            ) : null}
        </div>
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

function Note({ announced = false, children }: { readonly announced?: boolean; readonly children: ReactNode }) {
    return (
        <p className="px-3 py-3 text-sm text-muted" role={announced ? 'status' : undefined}>
            {children}
        </p>
    );
}
