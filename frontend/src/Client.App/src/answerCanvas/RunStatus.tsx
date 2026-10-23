// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import type { Locale } from '../localization/locale';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import type { FollowedAnswer, RunEnding } from './followedRun';
import type { RunStopping } from './useRunStopping';

// Everything around the blocks: that the run started, how far its search of the mail has got, the control that stops
// it, and — when it is over — which of the eleven ways it ended and how many messages it read.
//
// **A run has one sentence at a time, and it stands in one place.** The working line and the ending notice are the same
// region rather than two that replace each other, because it is a live region: a reader who cannot see the screen is
// told that retrieval moved, that the run was stopped, and that a ceiling refused it, by the text of one node changing
// rather than by nodes appearing somewhere they were not.
//
// **A ceiling is not an error.** A deployment that has spent what it allows answering to cost is behaving exactly as
// its operator configured it, so both spend refusals read as states and neither offers a retry — the period one names
// the instant its allowance returns, and the run one names the one thing the person can change.
//
// **Nothing here is machinery.** A person asking about their mail is told that it is being searched and how much of it
// the answer was read from — never calls, tokens, characters, ceilings, or which endpoint and model answered, all of
// which stay with the operator's meter and logs.

/** How loudly the run's own state reads, which follows whether anything went wrong rather than whether it ended. */
type RunTone = 'working' | 'noted' | 'warned';

const toneShells: Readonly<Record<RunTone, string>> = {
    working: 'border border-line bg-panel text-text-soft',
    noted: 'bg-sunken text-text-soft',
    warned: 'bg-warning-soft text-warning-text',
};

/** What each ending says and how it reads. Exhaustive by its own type, so a new ending fails to compile until it has one. */
const endings: Readonly<
    Record<RunEnding, { readonly note: MessageKey; readonly icon: IconName; readonly tone: RunTone }>
> = {
    completed: { note: 'run.ended.completed', icon: 'check_circle', tone: 'noted' },
    cancelled: { note: 'run.ended.cancelled', icon: 'cancel', tone: 'noted' },
    gone: { note: 'run.ended.gone', icon: 'info', tone: 'noted' },
    periodSpent: { note: 'run.ended.periodSpent', icon: 'schedule', tone: 'noted' },
    runSpent: { note: 'run.ended.runSpent', icon: 'insights', tone: 'noted' },
    timedOut: { note: 'run.ended.timedOut', icon: 'hourglass_top', tone: 'warned' },
    stopped: { note: 'run.ended.stopped', icon: 'warning', tone: 'warned' },
    unavailable: { note: 'run.ended.unavailable', icon: 'cloud_off', tone: 'warned' },
    temporarilyUnavailable: { note: 'run.ended.temporarilyUnavailable', icon: 'cloud_off', tone: 'warned' },
    retrievalRefused: { note: 'run.ended.retrievalRefused', icon: 'warning', tone: 'warned' },
    failed: { note: 'run.ended.failed', icon: 'error', tone: 'warned' },
};

/**
 * The run's own state, and the chrome that says it.
 *
 * @param answer The run as far as it has been read.
 * @param stopping How far asking the deployment to stop it has got.
 * @param onStop What the control that stops the run does, and nothing where the surface offers none.
 */
export function RunStatus({
    answer,
    stopping = 'idle',
    onStop,
}: {
    readonly answer: FollowedAnswer;
    readonly stopping?: RunStopping;
    readonly onStop?: (() => void) | undefined;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();

    const region = useRef<HTMLDivElement>(null);
    const offeredStopping = useRef(false);

    // The control goes when the run ends, which is the moment a keyboard would otherwise be left on nothing at all. It
    // is the move a contained block makes when it recovers, and for its reason.
    const offersStopping = answer.running && onStop !== undefined && stopping !== 'asking';

    useEffect(() => {
        if (offeredStopping.current && !offersStopping) {
            region.current?.focus();
        }

        offeredStopping.current = offersStopping;
    }, [offersStopping]);

    const ended = answer.running ? null : endings[answer.ending ?? 'completed'];
    const tone = ended?.tone ?? 'working';

    const allowanceReturns =
        ended !== null && answer.ending === 'periodSpent'
            ? wordInstant(answer.retryAt, locale, 'full', timeZone)
            : null;

    return (
        <div className="flex flex-col gap-2" ref={region} tabIndex={-1}>
            <div className={`flex items-start gap-3 rounded-lg px-4.5 py-3.5 ${toneShells[tone]}`}>
                {ended === null ? (
                    /* A pulse rather than a spinner: what a reader is owed is the sentence beside it, and the mark is
                       there to say the sentence is still being made true. It carries no name, the sentence being the
                       whole of what it means, and `prefers-reduced-motion` stops it moving at all. */
                    <span aria-hidden className="mt-1.5 size-2 shrink-0 animate-working rounded-full bg-accent" />
                ) : (
                    <Icon className="mt-0.5 size-icon-md shrink-0" name={ended.icon} />
                )}

                {/* The live region is the whole of what the run says rather than its first sentence, so the instant a
                    refused allowance returns is announced with the refusal that named it instead of arriving silently
                    beneath it. */}
                <div className="flex min-w-0 flex-1 flex-col gap-1" role="status">
                    <p className="text-md text-pretty">
                        {ended === null ? translate(...working(answer, stopping, locale)) : translate(ended.note)}
                    </p>

                    {allowanceReturns === null ? null : (
                        <p className="text-sm">{translate('run.allowanceReturns', { at: allowanceReturns })}</p>
                    )}
                </div>

                {offersStopping ? <SecondaryButton label={translate('run.stop')} onActivate={onStop} /> : null}
            </div>

            <RunReach answer={answer} />
        </div>
    );
}

/** A message and what fills its holes, in the shape `translate` is spread over. */
type Sentence = readonly [MessageKey, Readonly<Record<string, string>>?];

/** What the run is doing, which asking it to stop replaces: a stop somebody pressed is the newer fact about the run. */
function working(answer: FollowedAnswer, stopping: RunStopping, locale: Locale): Sentence {
    if (stopping === 'asking') {
        return ['run.stopping'];
    }

    if (stopping === 'refused') {
        return ['run.stopNotReached'];
    }

    const retrieval = answer.retrieval;

    return retrieval === null
        ? ['run.working']
        : ['run.retrieval', { passages: counted(locale, retrieval.passagesFound) }];
}

/** The plural forms of what a finished run read, which Polish needs four of and English two. */
const readCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'run.read.other',
    one: 'run.read.one',
    two: 'run.read.other',
    few: 'run.read.few',
    many: 'run.read.many',
    other: 'run.read.other',
};

/**
 * How many messages a run read, once it is over and only where it read any.
 *
 * It sits with the run rather than in the frame: a figure in permanent chrome teaches people to watch a meter while
 * they work, and one beside the answer is legible exactly when somebody asks what the answer rests on. While the run
 * works the line above already says the search is moving, so a second moving count would only repeat it.
 */
function RunReach({ answer }: { readonly answer: FollowedAnswer }) {
    const { locale, translate } = useLocalization();

    const messages = answer.spend?.messagesRetrieved ?? 0;

    // A run that read nothing — refused before it searched, or stopped at once — has no reach worth a line.
    if (answer.running || messages === 0) {
        return null;
    }

    return (
        <p className="px-1 text-sm text-faint">
            {translate(readCounts[new Intl.PluralRules(locale).select(messages)], {
                count: counted(locale, messages),
            })}
        </p>
    );
}

function counted(locale: Locale, count: number): string {
    return new Intl.NumberFormat(locale).format(count);
}
