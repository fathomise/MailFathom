// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { ChoiceSegment } from '../controls/ChoiceSegment';
import { Control } from '../controls/Control';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useDesktopComposition } from '../shell/useWideWorkspace';
import { calendarViews, isoWeek, type CalendarSpan, type CalendarView } from './calendarSpan';
import { wordMonth, wordSpan } from './calendarWording';

// Where the reader is in their calendar and how they move: the span they are looking at, the three ways to another
// one, and which of the four views is drawing it.
//
// The week number stands beside the week's own dates because the design draws it there, and it is arithmetic
// rather than a word `Intl` knows — `calendarSpan.ts` holds it, and the sentence around it is a catalogue entry with a
// hole, because where a number falls in that sentence is the language's answer.

/** What each view is called on the screen. Exhaustive by its own type, so a fifth view does not compile until it has a name. */
const viewNames: Readonly<Record<CalendarView, MessageKey>> = {
    day: 'calendar.day',
    week: 'calendar.week',
    month: 'calendar.month',
    agenda: 'calendar.agenda',
};

export function CalendarNavigation({
    view,
    anchor,
    span,
    onView,
    onMove,
    onToday,
}: {
    readonly view: CalendarView;

    /** The day the span is computed around, which is what the heading names. */
    readonly anchor: Date;

    readonly span: CalendarSpan;

    readonly onView: (view: CalendarView) => void;

    /** Moves one span forward, or backward for a negative count. */
    readonly onMove: (spans: number) => void;

    readonly onToday: () => void;
}) {
    const { locale, translate } = useLocalization();

    const desktop = useDesktopComposition();

    const covered = wordSpan(view, span, locale);

    return (
        <div className="flex shrink-0 flex-wrap items-center gap-x-3.5 gap-y-2.25 border-b border-line bg-sunken px-4 py-2.75 workspace:flex-nowrap workspace:px-6">
            <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                <h2 className="truncate text-xl font-semibold tracking-tighter">{wordMonth(anchor, locale)}</h2>

                {covered === null ? null : (
                    <p className="truncate text-sm text-muted">
                        {view === 'week'
                            ? translate('calendar.weekCovered', {
                                  days: covered,
                                  week: new Intl.NumberFormat(locale).format(isoWeek(span.from)),
                              })
                            : covered}
                    </p>
                )}
            </div>

            <div className="flex items-center gap-1.75">
                <Control
                    label={translate('calendar.previous')}
                    icon="chevron_left"
                    shape="symbol"
                    onPress={() => {
                        onMove(-1);
                    }}
                />

                <SecondaryButton label={translate('calendar.today')} shape="compact" onActivate={onToday} />

                <Control
                    label={translate('calendar.next')}
                    icon="chevron_right"
                    shape="symbol"
                    onPress={() => {
                        onMove(1);
                    }}
                />
            </div>

            {/* Chips at the desktop's width, and one segmented control under it, where four chips in a row read as
                four separate buttons rather than four ways of drawing the same span. */}
            <fieldset
                className={
                    desktop
                        ? 'flex items-center gap-1.75 overflow-x-auto'
                        : 'flex basis-full items-center gap-0.5 rounded-md border border-line bg-rail p-0.75 workspace:shrink-0 workspace:basis-auto'
                }
            >
                <legend className="sr-only">{translate('calendar.views')}</legend>

                {calendarViews.map((name) => (
                    <ChoiceSegment
                        key={name}
                        shape={desktop ? 'chip' : 'raised'}
                        name="calendar-view"
                        value={name}
                        chosen={view === name}
                        onChoose={() => {
                            onView(name);
                        }}
                    >
                        {translate(viewNames[name])}
                    </ChoiceSegment>
                ))}
            </fieldset>
        </div>
    );
}
