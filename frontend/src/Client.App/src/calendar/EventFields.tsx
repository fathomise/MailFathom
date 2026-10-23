// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId } from 'react';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { wordLasting } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { endsBeforeItBegins, lengthOf, wantsAnEndTime, type EventDraft } from './eventDraft';

// The fields an event is, drawn once for the two dialogs that hold them: writing one down and amending one are the
// same values, and two arrangements of them is how one form comes to accept what the other refuses. The design's
// *Editing an event* state is the arrangement — *Starts* and *Ends* each a day and a time, *All day* folding the times
// away and keeping the days, and a line under them saying how long it lasts or why it cannot be saved.
//
// **The days and the times are the platform's own fields.** A date input and a time input already know the reader's
// own calendar and clock, offer the platform's picker on a phone, and are reachable from a keyboard without this
// client writing a line of it.

/** The longest description the deployment keeps, which the field stops at rather than letting a save be refused. */
const longestNotes = 4000;

export function EventFields({
    draft,
    onDraft,
}: {
    readonly draft: EventDraft;
    readonly onDraft: (draft: EventDraft) => void;
}) {
    const { locale, translate } = useLocalization();
    const titles = useId();
    const notes = useId();
    const startDays = useId();
    const endDays = useId();
    const allDay = useId();

    const conflicting = endsBeforeItBegins(draft);
    const unended = wantsAnEndTime(draft);
    const length = lengthOf(draft);

    function startOn(day: string): void {
        // The last day travels with the first where it stood on it or would now stand before it, so moving an event
        // to another day does not leave it ending on the day it was moved away from.
        onDraft({
            ...draft,
            startDay: day,
            endDay: draft.endDay === draft.startDay || draft.endDay < day ? day : draft.endDay,
        });
    }

    return (
        <>
            <div className="flex flex-col gap-1.5">
                <label htmlFor={titles} className={dialogFieldLabel}>
                    {translate('calendar.eventTitle')}
                </label>

                <input
                    id={titles}
                    type="text"
                    value={draft.title}
                    className={dialogField}
                    onChange={(typing) => {
                        onDraft({ ...draft, title: typing.target.value });
                    }}
                />
            </div>

            <div className="flex flex-col gap-1.5">
                <label htmlFor={notes} className={dialogFieldLabel}>
                    {translate('calendar.eventNotes')}
                </label>

                <textarea
                    id={notes}
                    rows={3}
                    maxLength={longestNotes}
                    value={draft.notes}
                    className={`${dialogField} resize-y`}
                    onChange={(typing) => {
                        onDraft({ ...draft, notes: typing.target.value });
                    }}
                />
            </div>

            <div className="flex items-center gap-2.25">
                <input
                    id={allDay}
                    type="checkbox"
                    checked={draft.allDay}
                    className="size-4.5 accent-accent"
                    onChange={(ticking) => {
                        // A day carries no clock reading, so turning it on empties the two times rather than leaving
                        // values the record would not state and the form would then have to explain.
                        onDraft(
                            ticking.target.checked
                                ? { ...draft, allDay: true, start: '', end: '' }
                                : { ...draft, allDay: false },
                        );
                    }}
                />

                <label htmlFor={allDay} className="text-sm text-text-soft">
                    {translate('calendar.eventAllDay')}
                </label>
            </div>

            {draft.allDay ? (
                <div className="flex gap-2.5">
                    <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                        <label htmlFor={startDays} className={dialogFieldLabel}>
                            {translate('calendar.eventStart')}
                        </label>

                        <input
                            id={startDays}
                            type="date"
                            value={draft.startDay}
                            className={dialogField}
                            onChange={(typing) => {
                                startOn(typing.target.value);
                            }}
                        />
                    </div>

                    <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                        <label htmlFor={endDays} className={dialogFieldLabel}>
                            {translate('calendar.eventEnd')}
                        </label>

                        <input
                            id={endDays}
                            type="date"
                            value={draft.endDay}
                            aria-invalid={conflicting}
                            className={`${dialogField} aria-invalid:border-error`}
                            onChange={(typing) => {
                                onDraft({ ...draft, endDay: typing.target.value });
                            }}
                        />
                    </div>
                </div>
            ) : (
                <>
                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={startDays} className={dialogFieldLabel}>
                            {translate('calendar.eventStart')}
                        </label>

                        <div className="flex gap-2.5">
                            <div className="min-w-0 flex-1">
                                <input
                                    id={startDays}
                                    type="date"
                                    value={draft.startDay}
                                    className={dialogField}
                                    onChange={(typing) => {
                                        startOn(typing.target.value);
                                    }}
                                />
                            </div>

                            <div className="w-28 shrink-0">
                                <input
                                    type="time"
                                    aria-label={translate('calendar.eventStartTime')}
                                    value={draft.start}
                                    className={dialogField}
                                    onChange={(typing) => {
                                        onDraft({ ...draft, start: typing.target.value });
                                    }}
                                />
                            </div>
                        </div>
                    </div>

                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={endDays} className={dialogFieldLabel}>
                            {translate('calendar.eventEnd')}
                        </label>

                        <div className="flex gap-2.5">
                            <div className="min-w-0 flex-1">
                                <input
                                    id={endDays}
                                    type="date"
                                    value={draft.endDay}
                                    className={dialogField}
                                    onChange={(typing) => {
                                        onDraft({ ...draft, endDay: typing.target.value });
                                    }}
                                />
                            </div>

                            <div className="w-28 shrink-0">
                                <input
                                    type="time"
                                    aria-label={translate('calendar.eventEndTime')}
                                    aria-invalid={conflicting || unended}
                                    value={draft.end}
                                    className={`${dialogField} aria-invalid:border-error`}
                                    onChange={(typing) => {
                                        onDraft({ ...draft, end: typing.target.value });
                                    }}
                                />
                            </div>
                        </div>
                    </div>
                </>
            )}

            {conflicting || unended ? (
                <p role="alert" className="text-sm text-error-text text-pretty">
                    {translate(conflicting ? 'calendar.endsBeforeStart' : 'calendar.endTimeNeeded')}
                </p>
            ) : length === null ? null : (
                <p className="text-sm text-muted">{wordLasting(length, locale, 'short')}</p>
            )}
        </>
    );
}
