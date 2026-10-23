// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useState, type RefObject } from 'react';
import {
    mostCitedPassages,
    readCitedPassages,
    type CitedPassage,
    type ClientFailure,
    type ClientFailureReason,
    type ClientSession,
    type MailEnrichmentMark,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { useReadingZone } from '../localization/useReadingZone';
import { readingNames, readingSources } from './messageReadings';

// Where a reading is checked. Every mark a message carries, each with the sentence, why the producer says it, what
// produced it, and the passages of the message it was drawn from — which is the whole of what makes a mark something
// somebody can disagree with rather than something they have to take.
//
// **A modal rather than something that opens on the row**, which is what the design draws as well. Two things decide
// that here and neither is taste: a row is an `option` of a listbox, which holds no focusable descendant, and its
// height is what the window above it does arithmetic over — so a reading that expanded where it stands would take the
// keyboard path off the list and put every row below it somewhere other than where the list drew the space for it.
//
// **The row's mark opens it under a pointer, and the row's menu is the path everything else takes.** The design puts a
// symbol on the tile between the thread count and the time, and this client draws it there — but hidden from the
// accessibility tree and reachable by pointer alone, for the listbox reason above. What a keyboard, a screen reader and
// a finger held on the row each reach is the menu entry, which was this surface's only affordance before the mark and
// remains the one that carries its name.
//
// **The evidence is fetched rather than carried.** A row publishes passage identifiers because a list page carrying
// the passages themselves would be publishing a body it had no reason to, so the words arrive when somebody asks to
// see them — one request for the whole message's evidence, made when this is opened.
//
// **Nothing here is written down anywhere.** The sentences, the reasons and the passages are mail, and they live for
// as long as this dialog is open.

/** What is being checked: one message's readings, and the message its evidence is followed against. */
export interface AskedReadings {
    readonly storedEmailId: string;
    readonly subject: string | null;
    readonly marks: readonly MailEnrichmentMark[];
}

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

export function ReadingsAsked({
    asked,
    session,
    transport,
    dialog,
}: {
    /** The readings being checked, or `null` while nothing is. */
    readonly asked: AskedReadings | null;

    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The dialog itself, opened by whichever surface asked, so that whether it is open is the element's own state. */
    readonly dialog: RefObject<HTMLDialogElement | null>;
}) {
    const { translate } = useLocalization();
    const closesOnBack = useTopLayerSurface();
    const names = useId();

    return (
        <dialog
            onToggle={closesOnBack}
            ref={dialog}
            aria-labelledby={names}
            data-pull="down"
            // The whole screen below the wide breakpoint and a dialog from it up, which is the design's own `dlgAi`:
            // a reading set beside its passage needs the width, and one stacked over it on a phone or a fold reads
            // better across the whole screen than in a card with scrim down either side. The full-screen shape has no
            // border, radius, or shadow, and its own safe-area insets, since a dialog stands in the platform's top
            // layer and the frame's padding is not around it.
            className="max-wide:surface-sheet wide:surface-pop m-0 h-full max-h-full w-full max-w-full rounded-none border-0 bg-panel p-0 pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left text-text open:flex open:flex-col backdrop:bg-scrim wide:m-auto wide:h-auto wide:max-h-readings-tall wide:w-readings-narrow wide:max-w-readings-narrow wide:overflow-hidden wide:rounded-xl wide:border wide:border-line wide:shadow-dialog roomy:w-readings roomy:max-w-readings"
        >
            <div className="flex shrink-0 items-center gap-3 border-b border-line py-2.5 ps-4 pe-2.5 wide:pt-4.5 wide:pb-4 wide:ps-6.5 wide:pe-4.5">
                <span className="flex size-8 shrink-0 items-center justify-center rounded-field bg-accent-soft text-accent-deep wide:size-9.5">
                    <Icon name="auto_awesome" className="size-icon-md wide:size-icon-xl" />
                </span>

                <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                    <h2 id={names} className="text-xl font-semibold tracking-tight wide:text-2xl">
                        {translate('reading.title')}
                    </h2>

                    {asked === null ? null : (
                        <p className="truncate text-base text-muted">{asked.subject ?? translate('list.noSubject')}</p>
                    )}
                </div>

                <SurfaceControl
                    label={translate('reading.close')}
                    icon="close"
                    onActivate={() => {
                        dialog.current?.close();
                    }}
                />
            </div>

            {/* Keyed by the message, so asking about a second one starts its evidence read rather than reconciling an
                answer with a question the reader has already moved on from. */}
            {asked === null ? null : (
                <AskedMessage key={asked.storedEmailId} asked={asked} session={session} transport={transport} />
            )}
        </dialog>
    );
}

function AskedMessage({
    asked,
    session,
    transport,
}: {
    readonly asked: AskedReadings;
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const evidence = useCitedEvidence(session, transport, asked);

    // What produced the readings is named once, in the footer, rather than on every reading — and every distinct
    // producer is named there, because a deterministic rule re-run over the same message says the same thing and a
    // model promises no such thing, so a dialog holding both says so rather than naming one of them for all.
    const producers = [...new Set(asked.marks.map((mark) => translate(readingSources[mark.source])))];

    return (
        <>
            {/* The readings scroll inside whatever height the surface has, which is the whole screen below the wide
                breakpoint and the dialog's ceiling above it. Neither is a height this states: the dialog is the flex
                column and this is the one part of it that gives. */}
            <div className="flex min-h-0 flex-1 flex-col overflow-y-auto px-4 pt-1 pb-4 wide:px-6.5 wide:pt-1.5 wide:pb-4.5">
                <div className="flex flex-col gap-px bg-line-soft">
                    {asked.marks.map((mark) => {
                        const due = mark.dueAt === null ? null : wordInstant(mark.dueAt, locale, 'full', timeZone);

                        return (
                            <section
                                key={mark.aspect}
                                className="flex flex-col gap-2.5 bg-panel py-3.5 wide:flex-row wide:gap-7 wide:py-5.5"
                            >
                                <div className="flex min-w-0 flex-col gap-1.5 wide:flex-5">
                                    <h3 className="text-xs font-semibold text-faint">
                                        {translate(readingNames[mark.aspect])}
                                    </h3>

                                    <p className="text-xl/snug font-semibold text-balance wide:text-claim wide:tracking-tight">
                                        {mark.text}
                                    </p>

                                    {due === null ? null : (
                                        <p className="flex items-center gap-1.5 text-sm text-muted">
                                            <Icon name="schedule" className="size-icon-sm shrink-0" />

                                            {translate('reading.dueAt', { when: due })}
                                        </p>
                                    )}

                                    <p className="text-base/relaxed text-muted text-pretty">{mark.reason}</p>
                                </div>

                                <div className="flex min-w-0 flex-col gap-1.5 wide:flex-6">
                                    <MarkEvidence mark={mark} evidence={evidence} />
                                </div>
                            </section>
                        );
                    })}
                </div>
            </div>

            <div className="flex shrink-0 items-center gap-2 border-t border-line bg-sunken px-4 py-2.25 text-sm text-faint wide:px-6.5 wide:py-2.75">
                <Icon name="format_quote" className="size-icon-sm shrink-0" />

                {/* The contract stated in full where there is room for it and shortened where there is not, so the
                    footer stays one line at every width. */}
                <span className="min-w-0 flex-1 truncate">
                    <span className="wide:hidden">{translate('reading.aboutShort')}</span>
                    <span className="max-wide:hidden">{translate('reading.about')}</span>
                </span>

                {producers.map((producer) => (
                    <span key={producer} className="whitespace-nowrap">
                        {producer}
                    </span>
                ))}
            </div>
        </>
    );
}

// What one mark rests on, which is the half of this surface somebody who doubts the sentence actually came for. The
// passages are drawn in the order the producer named them, best first, and each says for itself whether the words
// behind it could still be reached.
function MarkEvidence({ mark, evidence }: { readonly mark: MailEnrichmentMark; readonly evidence: CitedEvidence }) {
    const { translate } = useLocalization();

    // A reading with no evidence is one the deployment refuses to store, so this states an absence rather than drawing
    // an empty list under a sentence that is supposed to be checkable.
    if (mark.evidence.length === 0) {
        return <p className="text-sm text-faint">{translate('reading.noEvidence')}</p>;
    }

    if (evidence.reading) {
        return (
            <p className="text-sm text-muted" role="status">
                {translate('reading.readingEvidence')}
            </p>
        );
    }

    if (evidence.failure !== null) {
        return (
            <div className="flex flex-wrap items-center gap-1.5">
                <p className="text-sm text-warning" role="alert">
                    {translate('reading.evidenceFailed', {
                        reason: translate(failureLabels[evidence.failure.reason]),
                    })}
                </p>

                {evidence.failure.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={evidence.readAgain} />
                ) : null}
            </div>
        );
    }

    return (
        <ul className="flex flex-col gap-2">
            {mark.evidence.map((passage) => {
                const cited = evidence.passages.get(passage);
                const words = cited?.text ?? null;

                // Bounded rather than as long as the passage is, which is what the design draws: four lines where the
                // reading is stacked over it, and a height that scrolls where it stands beside it, so one long
                // quotation never pushes every reading after it off the screen.
                return (
                    <li
                        key={passage}
                        className="line-clamp-4 rounded-e-field border-s-2 border-s-accent-line bg-sunken px-2.75 py-2 text-base/relaxed text-text-soft text-pretty wide:line-clamp-none wide:max-h-45 wide:overflow-y-auto wide:px-4 wide:py-3 wide:text-md/relaxed"
                    >
                        {words ?? <span className="text-faint">{translate(insteadOf(cited))}</span>}
                    </li>
                );
            })}
        </ul>
    );
}

/**
 * What is said where a passage produced no words, which is three different things and not one.
 *
 * A passage nothing answered for was never asked about: one request follows ten citations and a message may name
 * twelve, so the last of them are past what this surface follows. Saying the message had moved under it would be
 * telling somebody a fact about their mail that this client has no evidence for.
 */
function insteadOf(cited: CitedPassage | undefined): MessageKey {
    if (cited === undefined) {
        return 'reading.passageNotFollowed';
    }

    return cited.outcome === 'PrivateSource' ? 'reading.passagePrivate' : 'reading.passageGone';
}

interface CitedEvidence {
    readonly reading: boolean;
    readonly failure: ClientFailure | null;
    readonly passages: ReadonlyMap<string, CitedPassage>;
    readonly readAgain: () => void;
}

/** What one answer was to, so an answer that arrives for a question nobody is asking any more is not read as this one. */
interface EvidenceAnswer {
    readonly asking: string;
    readonly attempt: number;
    readonly failure: ClientFailure | null;
    readonly passages: ReadonlyMap<string, CitedPassage>;
}

// One read for the whole message's evidence rather than one per mark: three readings resting on four passages each is
// within the ten one request follows, so a message's readings are backed by a single request however many there are.
//
// Whether it is still reading is derived from whether the answer standing is the answer to the question being asked,
// rather than kept as a second piece of state beside it: the two would have to be held in step, and the pair that
// disagrees is a spinner over an answer that has already arrived.
function useCitedEvidence(session: ClientSession, transport: MailFathomTransport, asked: AskedReadings): CitedEvidence {
    const wanted = [...new Set(asked.marks.flatMap((mark) => mark.evidence))].slice(0, mostCitedPassages);

    // The identities joined rather than the array itself, because a fresh array of the same passages on every render
    // would start the read again on every render. They are identifiers rather than words, so this is a key rather
    // than a copy of anything anybody wrote.
    const asking = wanted.join('\n');

    const [attempt, setAttempt] = useState(0);
    const [answer, setAnswer] = useState<EvidenceAnswer | null>(null);

    const standing = answer !== null && answer.asking === asking && answer.attempt === attempt ? answer : null;

    useEffect(() => {
        if (asking === '') {
            return;
        }

        let listening = true;

        void readCitedPassages(session, transport, asked.storedEmailId, asking.split('\n')).then((result) => {
            if (!listening) {
                return;
            }

            setAnswer({
                asking,
                attempt,
                failure: result.outcome === 'failed' ? result.failure : null,
                passages:
                    result.outcome === 'read'
                        ? new Map(result.value.map((cited) => [cited.passage, cited]))
                        : new Map(),
            });
        });

        return () => {
            listening = false;
        };
    }, [session, transport, asked.storedEmailId, asking, attempt]);

    return {
        reading: asking !== '' && standing === null,
        failure: standing?.failure ?? null,
        passages: standing?.passages ?? new Map(),
        readAgain: () => {
            setAttempt(attempt + 1);
        },
    };
}
