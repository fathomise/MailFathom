// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { ChoiceSegment } from '../controls/ChoiceSegment';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useDesktopComposition } from '../shell/useWideWorkspace';
import { taskViews, type TaskView } from './taskViews';

// The head of the Tasks space: which view is shown, one line of what it holds, and the four views to move between,
// each with how much is open in it. Four loose chips at the desktop's width with the current one in the accent, and
// one segmented control under it — the calendar's, for the reason it gives: four chips in a row on a narrow screen
// read as four separate buttons rather than four ways of looking at one list. On a phone *Waiting for* reads
// *Waiting*, because four segments share the width of the screen.

const viewNames: Readonly<Record<TaskView, MessageKey>> = {
    today: 'tasks.viewToday',
    upcoming: 'tasks.viewUpcoming',
    waiting: 'tasks.viewWaiting',
    proposed: 'tasks.viewProposed',
};

type CountedForms = Readonly<Record<Intl.LDMLPluralRule, MessageKey>>;

function forms(
    stem: 'tasks.lineToday' | 'tasks.lineClashes' | 'tasks.lineUpcoming' | 'tasks.lineWaiting',
): CountedForms {
    return {
        zero: `${stem}.other`,
        one: `${stem}.one`,
        two: `${stem}.other`,
        few: `${stem}.few`,
        many: `${stem}.many`,
        other: `${stem}.other`,
    };
}

export function TasksHead({
    view,
    open,
    planned,
    clashes,
    now,
    onView,
}: {
    readonly view: TaskView;

    /** How much is open in each view. */
    readonly open: Readonly<Record<TaskView, number>>;

    /** How much of today's work is planned into it. */
    readonly planned: number;

    /** How many of today's planned tasks clash with the calendar. */
    readonly clashes: number;

    /** When the reader is reading, which is the day *Today* is headed with. */
    readonly now: number;

    readonly onView: (view: TaskView) => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const desktop = useDesktopComposition();
    const counted = new Intl.NumberFormat(locale);
    const plural = new Intl.PluralRules(locale);

    function counting(stem: Parameters<typeof forms>[0], count: number, names: Record<string, string> = {}): string {
        return translate(forms(stem)[plural.select(count)], { count: counted.format(count), ...names });
    }

    function line(): string {
        switch (view) {
            case 'today': {
                const today = counting('tasks.lineToday', open.today, { planned: counted.format(planned) });

                return clashes === 0 ? today : counting('tasks.lineClashes', clashes, { line: today });
            }
            case 'upcoming':
                return counting('tasks.lineUpcoming', open.upcoming);
            case 'waiting':
                return counting('tasks.lineWaiting', open.waiting);
            case 'proposed':
                return translate('tasks.lineProposed', { count: counted.format(open.proposed) });
        }
    }

    const titles: Readonly<Record<TaskView, string>> = {
        today: new Intl.DateTimeFormat(locale, {
            weekday: 'long',
            day: 'numeric',
            month: 'long',
            ...(timeZone === null ? {} : { timeZone }),
        }).format(now),
        upcoming: translate('tasks.headUpcoming'),
        waiting: translate('tasks.headWaiting'),
        proposed: translate('tasks.headProposed'),
    };

    return (
        <div className="flex shrink-0 flex-col gap-2 border-b border-line bg-sunken px-3.5 py-3 workspace:flex-row workspace:flex-wrap workspace:items-center workspace:gap-3.5 workspace:px-6">
            <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                <h2 className="text-2xl font-semibold tracking-tighter">{titles[view]}</h2>
                <p className="text-base text-muted">{line()}</p>
            </div>

            <fieldset
                className={
                    desktop
                        ? 'flex flex-wrap items-center gap-1.75'
                        : 'flex items-center gap-0.5 rounded-md border border-line bg-rail p-0.75 workspace:shrink-0'
                }
            >
                <legend className="sr-only">{translate('tasks.views')}</legend>

                {taskViews.map((name) => (
                    <ChoiceSegment
                        key={name}
                        shape={desktop ? 'chip' : 'raised'}
                        name="task-view"
                        value={name}
                        chosen={view === name}
                        onChoose={() => {
                            onView(name);
                        }}
                    >
                        <span className="flex items-center gap-1.5">
                            {name === 'waiting' ? (
                                <>
                                    <span className="max-workspace:sr-only">{translate(viewNames[name])}</span>
                                    <span aria-hidden="true" className="workspace:hidden">
                                        {translate('tasks.viewWaitingShort')}
                                    </span>
                                </>
                            ) : (
                                translate(viewNames[name])
                            )}
                            {open[name] === 0 ? null : (
                                <span
                                    className={`rounded-md px-1.5 text-xs ${
                                        view === name ? 'bg-accent-soft text-accent-deep' : 'bg-rail text-muted'
                                    }`}
                                >
                                    {counted.format(open[name])}
                                </span>
                            )}
                        </span>
                    </ChoiceSegment>
                ))}
            </fieldset>
        </div>
    );
}
