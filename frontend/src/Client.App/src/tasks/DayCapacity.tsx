// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ReactNode } from 'react';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// How spoken-for the day is — the design's second panel beside the list — and room inside it for the offer to lay the
// rest of the day out, which grows the panel to hold it.
//
// **What the panel says about the day is counted rather than composed.** The design writes a sentence a model would
// have written, ending in a judgement about which task to start with; this deployment publishes no route that answers
// that, so what is drawn is the two numbers the screen already holds — how much is due today and how much of the day
// is already promised. The *AI* mark stays, because the act the panel carries is the laid-out day and that is composed
// by a provider.
//
// **Each number is its own sentence rather than a hole in a shared one.** The two counts inflect the noun after them
// differently in Polish and would otherwise need a form per pair, which is a table of sixteen entries saying one thing.

// How much is due today, in the forms a language has for the noun, and the same for how much the calendar already
// holds. `Intl.PluralRules` selects between them, which Polish needs and English hides.
const tasksCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.capacityTasks.other',
    one: 'tasks.capacityTasks.one',
    two: 'tasks.capacityTasks.other',
    few: 'tasks.capacityTasks.few',
    many: 'tasks.capacityTasks.many',
    other: 'tasks.capacityTasks.other',
};

const eventsCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.capacityEvents.other',
    one: 'tasks.capacityEvents.one',
    two: 'tasks.capacityEvents.other',
    few: 'tasks.capacityEvents.few',
    many: 'tasks.capacityEvents.many',
    other: 'tasks.capacityEvents.other',
};

export function DayCapacity({
    dueToday,
    eventsToday,
    children,
}: {
    /** The tasks due today and not done, which is what the day is counted against. */
    readonly dueToday: number;

    /** How much of the day the calendar already holds. */
    readonly eventsToday: number;

    /** The offer to lay the rest of the day out, which stands under the count. */
    readonly children?: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const counted = new Intl.NumberFormat(locale);
    const forms = new Intl.PluralRules(locale);

    return (
        <section
            aria-labelledby="tasks-day-capacity"
            className="flex flex-col gap-2.25 rounded-lg bg-accent-soft p-3.25"
        >
            <div className="flex items-center gap-2.25">
                <span className="rounded-sm bg-accent px-1.5 py-0.5 text-xs font-semibold text-on-accent">
                    {translate('tasks.authored')}
                </span>
                <h2 id="tasks-day-capacity" className="text-xs font-semibold text-accent-deep">
                    {translate('tasks.dayCapacity')}
                </h2>
            </div>

            <p className="text-sm text-text-soft text-pretty">
                {translate(tasksCounted[forms.select(dueToday)], { count: counted.format(dueToday) })}{' '}
                {translate(eventsCounted[forms.select(eventsToday)], { count: counted.format(eventsToday) })}
            </p>

            {children}
        </section>
    );
}
