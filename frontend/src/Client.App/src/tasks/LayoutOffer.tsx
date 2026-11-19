// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    layOutToday,
    type CalendarEvent,
    type ClientResult,
    type ClientSession,
    type DayLayout,
    type MailFathomTransport,
    type PersonalTask,
    type TaskPlan,
} from '@mailfathom/client-backend';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { wordInstant, wordInstantRange } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { dayAround, endAfter } from './dayInstants';
import { clashOf } from './taskViews';

// *Lay out today*, inside the capacity panel, which grows to hold the offer: the deployment proposes a time for each
// task left in the day, and the panel draws each as a dashed row — with where it moved from and why, where it held a
// time already — and names what did not fit. **Nothing is planned until *Plan these***, which hands every placement
// to the screen to plan; *Not now* puts the offer down and plans nothing.
//
// The panel also draws the wait while the day is laid out, that nothing is left to lay out, and that laying out days
// is used up — after which the act is offered again. A deployment that arranges no day draws no act at all.

/** One task and the plan proposed for it, as *Plan these* hands it over. */
export type ProposedPlan = readonly [PersonalTask, TaskPlan];

type Offer =
    | { readonly kind: 'idle' }
    | { readonly kind: 'arranging' }
    | { readonly kind: 'failed' }
    | { readonly kind: 'answered'; readonly layout: DayLayout };

const didNotFit: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.didNotFit.other',
    one: 'tasks.didNotFit.one',
    two: 'tasks.didNotFit.other',
    few: 'tasks.didNotFit.few',
    many: 'tasks.didNotFit.many',
    other: 'tasks.didNotFit.other',
};

export function LayoutOffer({
    session,
    transport,
    offered,
    tasks,
    events,
    startsArranging = false,
    onPlan,
}: {
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;

    /** Whether this deployment arranges a day at all and this credential may ask it to. */
    readonly offered: boolean;

    /** The tasks the screen holds, which a placement is drawn with the line of. */
    readonly tasks: readonly PersonalTask[];

    /** Today's calendar events, which say why a planned task was moved. */
    readonly events: readonly CalendarEvent[];

    /** Whether the day is laid out as the panel is drawn, which is how another screen opens Tasks on the offer. */
    readonly startsArranging?: boolean;

    /** Plans each proposed placement, which only *Plan these* calls. */
    readonly onPlan: (plans: readonly ProposedPlan[]) => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const writeRefused = useWriteRefusal();
    const [offer, setOffer] = useState<Offer>({
        kind: startsArranging && session !== null ? 'arranging' : 'idle',
    });

    // Which arrangement is the latest asked for, and whether the panel still stands to draw one.
    const asked = useRef(0);
    const here = useRef(true);

    // The control that asked is gone while the day is laid out, so focus moves to the sentence being waited on rather
    // than falling to the document body.
    const waiting = useRef<HTMLParagraphElement | null>(null);

    function settle(asking: number, answer: ClientResult<DayLayout>): void {
        if (here.current && asking === asked.current) {
            setOffer(answer.outcome === 'failed' ? { kind: 'failed' } : { kind: 'answered', layout: answer.value });
        }
    }

    function arrange(): void {
        if (session === null) {
            return;
        }

        const asking = ++asked.current;

        setOffer({ kind: 'arranging' });
        void restOfToday(session, transport).then((answer) => {
            settle(asking, answer);
        });
    }

    // Opened on the offer, the panel asks once as it is first drawn — a request going out, which is what an effect is
    // for. It is asked under the first credential the panel has, and never again for the same panel.
    useEffect(() => {
        here.current = true;

        if (startsArranging && session !== null && asked.current === 0) {
            const asking = ++asked.current;

            void restOfToday(session, transport).then((answer) => {
                if (here.current && asking === asked.current) {
                    setOffer(
                        answer.outcome === 'failed' ? { kind: 'failed' } : { kind: 'answered', layout: answer.value },
                    );
                }
            });
        }

        return () => {
            here.current = false;
        };
    }, [startsArranging, session, transport]);

    useEffect(() => {
        if (offer.kind === 'arranging') {
            waiting.current?.focus();
        }
    }, [offer.kind]);

    const titles = new Map(tasks.map((task) => [task.id, task]));
    const refused = writeRefused(translate('tasks.layOutToday'));
    const layOut = !offered ? null : (
        <Control
            label={translate('tasks.layOutToday')}
            shape="primary"
            className="self-start"
            {...(refused === null ? {} : { held: true, hint: refused })}
            onPress={arrange}
        />
    );

    if (offer.kind === 'arranging') {
        return (
            <p
                ref={waiting}
                tabIndex={-1}
                role="status"
                className="flex items-center gap-1.75 text-sm text-accent-deep"
            >
                <Icon name="progress_activity" className="size-icon-sm shrink-0 animate-spin" />
                {translate('tasks.arranging')}
            </p>
        );
    }

    if (offer.kind === 'idle' || offer.kind === 'failed') {
        return (
            <>
                {offer.kind === 'failed' ? (
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate('tasks.arrangeFailed')}
                    </p>
                ) : null}
                {layOut}
            </>
        );
    }

    const { layout } = offer;
    const placed = layout.placements.flatMap((placement) => {
        const task = titles.get(placement.taskId);

        return task === undefined ? [] : [{ task, plan: { startAt: placement.startAt, minutes: placement.minutes } }];
    });

    if (layout.outcome === 'AllowanceSpent') {
        return (
            <>
                <p role="status" className="flex items-start gap-1.75 text-sm text-warning-text text-pretty">
                    <Icon name="hourglass_disabled" className="size-icon-md shrink-0" />
                    {translate('tasks.allowanceSpent')}
                </p>
                {layOut}
            </>
        );
    }

    if (layout.outcome === 'NotArranged' || placed.length === 0) {
        return (
            <>
                <p role="status" className="flex items-start gap-1.75 text-sm text-healthy-text text-pretty">
                    <Icon name="event_available" className="size-icon-md shrink-0" />
                    {translate('tasks.nothingLeftToLayOut')}
                </p>
                <PutDown
                    onPress={() => {
                        setOffer({ kind: 'idle' });
                    }}
                />
            </>
        );
    }

    const notFitting = layout.notToday.flatMap((id) => {
        const task = titles.get(id);

        return task === undefined ? [] : [task.title];
    });

    return (
        <div className="flex flex-col gap-2.25">
            <p className="text-sm font-semibold text-text">{translate('tasks.layoutOffered')}</p>

            <ul className="flex flex-col gap-1.5">
                {placed.map(({ task, plan }) => (
                    <li
                        key={task.id}
                        className="flex flex-col gap-0.5 rounded-md border border-dashed border-accent bg-panel px-2.25 py-1.75"
                    >
                        <span className="flex items-baseline gap-2">
                            <time dateTime={plan.startAt} className="shrink-0 text-xs text-accent-deep">
                                {wordInstantRange(
                                    plan.startAt,
                                    endAfter(plan.startAt, plan.minutes),
                                    locale,
                                    'time',
                                    timeZone,
                                )}
                            </time>
                            <span className="min-w-0 text-base">{task.title}</span>
                        </span>
                        {movedFrom(task, plan)}
                    </li>
                ))}
            </ul>

            {notFitting.length === 0 ? null : (
                <p className="text-sm text-text-soft text-pretty">
                    {translate(didNotFit[new Intl.PluralRules(locale).select(notFitting.length)], {
                        count: new Intl.NumberFormat(locale).format(notFitting.length),
                        titles: new Intl.ListFormat(locale, { type: 'unit' }).format(notFitting),
                    })}
                </p>
            )}

            <div className="flex flex-wrap gap-2">
                <button
                    type="button"
                    className={mannerDrawn.act}
                    onClick={() => {
                        setOffer({ kind: 'idle' });
                        onPlan(placed.map(({ task, plan }) => [task, plan] as const));
                    }}
                >
                    {translate('tasks.planThese')}
                </button>
                <PutDown
                    onPress={() => {
                        setOffer({ kind: 'idle' });
                    }}
                />
            </div>
        </div>
    );

    // Where a task already held a time and the offer moves it, the row says from when — and what it clashed with there.
    function movedFrom(task: PersonalTask, plan: TaskPlan) {
        if (task.plan === null || task.plan.startAt === plan.startAt) {
            return null;
        }

        const time = wordInstant(task.plan.startAt, locale, 'time', timeZone) ?? '';
        const clash = clashOf(task, events);

        return (
            <span className="text-sm text-muted">
                {clash === null
                    ? translate('tasks.movedFrom', { time })
                    : translate('tasks.movedFromClash', { time, title: clash.title })}
            </span>
        );
    }
}

// The rest of today laid out: from now rather than from this morning, since hours already past are nobody's to give.
function restOfToday(session: ClientSession, transport: MailFathomTransport): Promise<ClientResult<DayLayout>> {
    return layOutToday(session, transport, { from: new Date().toISOString(), until: dayAround(Date.now()).until });
}

function PutDown({ onPress }: { readonly onPress: () => void }) {
    const { translate } = useLocalization();

    return (
        <button type="button" className={mannerDrawn.back} onClick={onPress}>
            {translate('tasks.notNow')}
        </button>
    );
}
