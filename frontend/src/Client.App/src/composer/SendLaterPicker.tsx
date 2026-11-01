// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, type RefObject } from 'react';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { calendarDayIn, wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { hasGone, instantOf, namedZone, wordZoneOffset, type SendTime } from './sendLater';
import { useSendTimeWording } from './useSendTimeWording';

// *Send later*'s own question: a date and a time, named in the zone the reader's profile sets rather than this device's,
// and said back as the moment the message would leave before it is taken. A time at or before now is said to have gone
// while it is being typed, and *Schedule* cannot act on it — the deployment would refuse it anyway, and a person
// finding that out after pressing would have to come back here to fix it.
//
// The fields are the platform's own date and time inputs, which every head draws in its own idiom, and which hand back
// the reader's clock reading as text rather than an instant — exactly the shape a time to send at is held in.

export function SendLaterPicker({
    asked,
    asking,
    now,
    zone,
    onAsking,
    onSchedule,
    onClosed,
}: {
    /** The dialog itself, held by the composer so that the menu and the line above the foot reach the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** The date and time in the fields, or `null` while nothing is being picked. */
    readonly asking: SendTime | null;

    /** The instant the picker was opened at, which a time has to be after. */
    readonly now: number;

    /** The zone the reader's record states, or `null` for the runtime's own. */
    readonly zone: string | null;

    /** What one change of a field left, which the composer holds. */
    readonly onAsking: (asking: SendTime) => void;

    /** Takes the time onto the composer, run once the dialog has closed. */
    readonly onSchedule: (chosen: SendTime) => void;

    /** Ends the picking however the dialog closed, so the fields are drawn afresh, focus first, on the next opening. */
    readonly onClosed: () => void;
}) {
    const { locale, translate } = useLocalization();
    const closesOnBack = useTopLayerSurface();
    const word = useSendTimeWording(now, zone);
    const names = useId();
    const dates = useId();
    const times = useId();

    const empty = asking === null || asking.date === '' || asking.time === '';
    const gone = !empty && hasGone(asking, now, zone);
    const schedulable = !empty && !gone;
    const said = schedulable ? word(asking) : null;
    const offset = wordZoneOffset(zone, asking === null ? now : (instantOf(asking, zone) ?? now));
    const nowSaid = wordInstant(new Date(now).toISOString(), locale, 'time', zone) ?? '';

    function scheduleName(): string {
        if (said !== null) {
            return translate('compose.scheduleFor', { when: said.long });
        }

        return translate(gone ? 'compose.scheduleRefusedGone' : 'compose.scheduleRefusedEmpty');
    }

    return (
        <dialog
            onToggle={closesOnBack}
            ref={asked}
            aria-labelledby={names}
            className="surface-pop m-auto w-dialog max-w-dialog-narrow rounded-lg border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                dialog.returnValue = '';

                if (answer === 'schedule' && asking !== null && schedulable) {
                    onSchedule(asking);
                }

                onClosed();
            }}
        >
            {asking === null ? null : (
                <form
                    className="flex flex-col"
                    onSubmit={(submitting) => {
                        submitting.preventDefault();

                        if (schedulable) {
                            asked.current?.close('schedule');
                        }
                    }}
                >
                    <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                        <Icon name="schedule_send" className="size-icon-lg shrink-0 text-accent-deep" />

                        <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                            <h2 id={names} className="text-lg font-semibold">
                                {translate('compose.sendLater')}
                            </h2>
                            <p className="text-xs text-muted text-pretty">{translate('compose.sendLaterExplains')}</p>
                        </div>

                        <SurfaceControl
                            label={translate('compose.sendLaterClose')}
                            icon="close"
                            onActivate={() => {
                                asked.current?.close();
                            }}
                        />
                    </div>

                    <div className="flex flex-col gap-3.5 px-4.5 py-4.5">
                        <div className="flex flex-wrap gap-3">
                            <div className="flex min-w-36 flex-1 flex-col gap-1.5">
                                <label htmlFor={dates} className={dialogFieldLabel}>
                                    {translate('compose.sendLaterDate')}
                                </label>
                                <input
                                    id={dates}
                                    type="date"
                                    autoFocus
                                    required
                                    min={calendarDayIn(now, zone)}
                                    value={asking.date}
                                    aria-invalid={gone}
                                    className={`${dialogField} aria-invalid:border-error pointer-coarse:min-h-11 pointer-coarse:text-xl`}
                                    onChange={(typing) => {
                                        onAsking({ ...asking, date: typing.target.value });
                                    }}
                                />
                            </div>

                            <div className="flex min-w-36 flex-1 flex-col gap-1.5">
                                <label htmlFor={times} className={dialogFieldLabel}>
                                    {translate('compose.sendLaterTime')}
                                </label>
                                <input
                                    id={times}
                                    type="time"
                                    required
                                    value={asking.time}
                                    aria-invalid={gone}
                                    className={`${dialogField} aria-invalid:border-error pointer-coarse:min-h-11 pointer-coarse:text-xl`}
                                    onChange={(typing) => {
                                        onAsking({ ...asking, time: typing.target.value });
                                    }}
                                />
                            </div>
                        </div>

                        <div aria-live="polite" className="flex flex-col gap-1">
                            {gone ? (
                                <p role="alert" className="flex items-center gap-1.5 text-md text-error-text">
                                    <Icon name="error" className="size-icon-md shrink-0" />
                                    {translate('compose.sendLaterGone', { now: nowSaid })}
                                </p>
                            ) : said === null ? (
                                <p className="text-md text-muted">{translate('compose.sendLaterEmpty')}</p>
                            ) : (
                                <p className="flex items-center gap-1.5 text-md font-semibold">
                                    <Icon name="schedule_send" className="size-icon-md shrink-0 text-accent-deep" />
                                    {translate('compose.sendLaterLeaves', { when: said.long })}
                                </p>
                            )}

                            <p className="flex items-center gap-1.5 text-sm text-muted">
                                <Icon name="public" className="size-icon-sm shrink-0" />
                                {translate('compose.sendLaterZone', { zone: namedZone(zone), offset })}
                            </p>
                        </div>
                    </div>

                    <div className="flex flex-wrap justify-end gap-2.25 px-4.5 pb-4.5">
                        <button type="button" className={mannerDrawn.back} onClick={() => asked.current?.close()}>
                            {translate('act.cancel')}
                        </button>

                        <button
                            type="submit"
                            aria-disabled={schedulable ? undefined : true}
                            aria-label={scheduleName()}
                            className={`${mannerDrawn.act} aria-disabled:cursor-not-allowed aria-disabled:opacity-45`}
                        >
                            {translate('compose.schedule')}
                        </button>
                    </div>
                </form>
            )}
        </dialog>
    );
}
