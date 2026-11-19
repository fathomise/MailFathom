// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useState, type RefObject } from 'react';
import {
    longestTaskMinutes,
    longestTaskTitle,
    shortestTaskMinutes,
    type TaskAnchor,
    type TaskCounterpart,
    type TaskDirection,
} from '@mailfathom/client-backend';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { ChoiceSegment } from '../controls/ChoiceSegment';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SurfaceControl } from '../controls/SurfaceControl';
import { SymbolControl } from '../controls/SymbolControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { readEstimate } from './taskWording';

// Writing a task down, which is the one way this client puts something into the list. What it records is asserted by
// whoever typed it, which is not a field on this form and never could be: the surface reads the origin off who is
// writing rather than off what they send.
//
// **What it asks is which way the task runs, its line, its day and how long it takes**, and — where whatever opened it
// named one — the context it belongs to, drawn as a card above the rest with the way to make it belong to nothing. Two
// blocks the design draws are absent: the reminders, which are written from the row once the task exists, a lead being
// measured back from a day the record has to hold first; and the block that would read a task out of a typed
// sentence, which has no route on this surface — the calendar has one for an event and the task list has none, which
// is a correction owed to the design rather than a field to invent here.
//
// **The save is refused before the request where the client can tell**, which is an empty line, one past the bound
// the deployment states, and a duration outside the minutes it takes. Everything else the list judges is an outcome the screen reports rather than a rule restated
// here.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.
//
// **Nothing here asks for focus**, which is `confirmation/Confirmation.tsx`'s arrangement and is deliberate: a dialog
// that is mounted whether or not it is open, carrying an element that asks for focus on mount, takes focus from
// whatever the reader was on the moment the screen it stands in is drawn. `showModal` places focus for itself.

/** What a task is being written down about, handed in by whatever opened the form: a thread, an event or a person. */
export interface TaskContext {
    readonly anchor?: TaskAnchor;
    readonly counterpart?: TaskCounterpart;

    /** The line the form starts with, which the person may change. */
    readonly title?: string;
}

/** What a person has typed into the form, which is the whole of what a task this client writes carries. */
export interface TaskDraft {
    readonly title: string;

    /** The day it is due on as `yyyy-mm-dd`, or the empty string where nobody has said when. */
    readonly dueOn: string;

    readonly direction: TaskDirection;

    /** How long it takes in minutes, or `null` where nobody said. */
    readonly estimatedMinutes: number | null;

    readonly anchor: TaskAnchor | null;
    readonly counterpart: TaskCounterpart | null;
}

// What has been typed so far. The line is `null` until somebody types, so the context's own line shows until then,
// and the context is let go of rather than copied, so what the opener handed in stays the opener's.
interface Typed {
    readonly title: string | null;
    readonly dueOn: string;
    readonly direction: TaskDirection;
    readonly minutes: string;
    readonly anchorLetGo: boolean;
    readonly counterpartLetGo: boolean;
}

const nothingTyped: Typed = {
    title: null,
    dueOn: '',
    direction: 'Owed',
    minutes: '',
    anchorLetGo: false,
    counterpartLetGo: false,
};

const contextKinds: Readonly<Record<TaskAnchor['kind'], { readonly glyph: IconName; readonly kind: MessageKey }>> = {
    Thread: { glyph: 'mail', kind: 'tasks.belongsToThread' },
    Event: { glyph: 'event', kind: 'tasks.belongsToEvent' },
};

// The minutes typed, as the whole number the record takes, `null` where nothing was typed, or `undefined` where what
// was typed is not a duration the deployment would take.
function minutesIn(typed: string): number | null | undefined {
    const minutes = readEstimate(typed);

    return minutes === null || minutes === undefined
        ? minutes
        : minutes >= shortestTaskMinutes && minutes <= longestTaskMinutes
          ? minutes
          : undefined;
}

export function NewTask({
    asked,
    context,
    onSave,
}: {
    /** The dialog itself, held by the caller so that every way of opening it reaches the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** What the task is being written down about, which whatever opened the form names. */
    readonly context?: TaskContext | undefined;

    /** Records what was typed, run once the dialog has closed and focus has been restored. */
    readonly onSave: (draft: TaskDraft) => void;
}) {
    const { translate } = useLocalization();
    const closesOnBack = useTopLayerSurface();
    const asks = useId();
    const titles = useId();
    const days = useId();
    const lengths = useId();
    const bounds = useId();

    const [draft, setDraft] = useState<Typed>(nothingTyped);

    const title = draft.title ?? context?.title ?? '';
    const line = title.trim();
    const minutes = minutesIn(draft.minutes);
    const anchor = draft.anchorLetGo ? null : (context?.anchor ?? null);
    const counterpart = draft.counterpartLetGo ? null : (context?.counterpart ?? null);
    const savable = line !== '' && line.length <= longestTaskTitle && minutes !== undefined;

    // Saving writes the task, so while the server is out of reach it says why it cannot act — beside the button, where
    // a finger reads it as well as a mouse — and nothing typed is lost: the dialog stays open on what was written.
    const refused = useWriteRefusal()(translate('tasks.saveTask'));

    function contextCard(glyph: IconName, kind: MessageKey, label: string, letGo: () => void) {
        return (
            <div className="flex items-center gap-2.25 rounded-lg border border-line bg-sunken px-2.75 py-2.25">
                <Icon name={glyph} className="size-icon-lg shrink-0 text-accent-deep" />
                <span className="flex min-w-0 flex-1 flex-col gap-px">
                    <span className="text-xs text-muted">{translate(kind)}</span>
                    <span className="truncate text-base">{label}</span>
                </span>
                <SymbolControl
                    label={translate('tasks.removeLinkTo', { label })}
                    className="flex size-7 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                    onPress={letGo}
                >
                    <Icon name="link_off" className="size-icon-sm" />
                </SymbolControl>
            </div>
        );
    }

    return (
        <dialog
            onToggle={closesOnBack}
            ref={asked}
            aria-labelledby={asks}
            className="surface-pop m-auto w-dialog max-w-dialog-narrow rounded-lg border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would write the task again.
                dialog.returnValue = '';

                if (answer === 'save' && refused === null && savable) {
                    onSave({
                        title: line,
                        dueOn: draft.dueOn,
                        direction: draft.direction,
                        estimatedMinutes: minutes ?? null,
                        anchor,
                        counterpart,
                    });
                }

                // The form is emptied on the way out rather than on the way in, so a dialog opened again is a blank
                // one and nothing of what was typed outlives the question it was typed into.
                setDraft(nothingTyped);
            }}
        >
            <form
                // The form judges the duration itself and says so in its own words, so no browser bubble stands over it.
                noValidate
                className="flex flex-col"
                onSubmit={(submitting) => {
                    // Closed by hand rather than by `method="dialog"`, because Enter in a field submits with no button
                    // behind it — and a form closed that way answers with nothing, which would drop a save somebody
                    // made from the keyboard.
                    submitting.preventDefault();

                    if (savable && refused === null) {
                        asked.current?.close('save');
                    }
                }}
            >
                <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                    <Icon name="task_alt" className="size-icon-lg shrink-0 text-muted" />

                    <h2 id={asks} className="min-w-0 flex-1 truncate text-lg font-semibold">
                        {translate(draft.direction === 'Awaited' ? 'tasks.newWaitingTask' : 'tasks.newTask')}
                    </h2>

                    <SurfaceControl
                        label={translate('tasks.closeNewTask')}
                        icon="close"
                        onActivate={() => {
                            asked.current?.close();
                        }}
                    />
                </div>

                <div className="flex flex-col gap-3.5 px-4.5 py-4.5">
                    {anchor === null
                        ? null
                        : contextCard(
                              contextKinds[anchor.kind].glyph,
                              contextKinds[anchor.kind].kind,
                              anchor.label,
                              () => {
                                  setDraft({ ...draft, anchorLetGo: true });
                              },
                          )}

                    {counterpart === null
                        ? null
                        : contextCard(
                              'person',
                              'tasks.belongsToPerson',
                              counterpart.displayName ?? counterpart.address,
                              () => {
                                  setDraft({ ...draft, counterpartLetGo: true });
                              },
                          )}

                    <fieldset className="flex gap-1.5">
                        <legend className="sr-only">{translate('tasks.directions')}</legend>
                        {(['Owed', 'Awaited'] as const).map((direction) => (
                            <ChoiceSegment
                                key={direction}
                                shape="chip"
                                name={`${asks}-direction`}
                                value={direction}
                                chosen={draft.direction === direction}
                                onChoose={() => {
                                    setDraft({ ...draft, direction });
                                }}
                            >
                                {translate(direction === 'Owed' ? 'tasks.iOwe' : 'tasks.iAwait')}
                            </ChoiceSegment>
                        ))}
                    </fieldset>

                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={titles} className={dialogFieldLabel}>
                            {translate('tasks.taskTitle')}
                        </label>

                        <input
                            id={titles}
                            type="text"
                            maxLength={longestTaskTitle}
                            value={title}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, title: typing.target.value });
                            }}
                        />
                    </div>

                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={days} className={dialogFieldLabel}>
                            {translate('tasks.taskDueOn')}
                        </label>

                        {/* The platform's own day field rather than a picker package, which is the rule
                            `frontend/AGENTS.md` states about a component library: what it answers with is already the
                            `yyyy-mm-dd` the surface takes, in whatever spelling the reader's own locale draws. */}
                        <input
                            id={days}
                            type="date"
                            value={draft.dueOn}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, dueOn: typing.target.value });
                            }}
                        />
                    </div>

                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={lengths} className={dialogFieldLabel}>
                            {translate('tasks.taskDuration')}
                        </label>

                        <input
                            id={lengths}
                            type="text"
                            autoComplete="off"
                            value={draft.minutes}
                            aria-invalid={minutes === undefined ? true : undefined}
                            aria-describedby={minutes === undefined ? bounds : undefined}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, minutes: typing.target.value });
                            }}
                        />

                        {minutes === undefined ? (
                            <p id={bounds} className="text-sm text-warning text-pretty">
                                {translate('tasks.durationBounds', {
                                    shortest: String(shortestTaskMinutes),
                                    longest: String(longestTaskMinutes),
                                })}
                            </p>
                        ) : null}
                    </div>
                </div>

                {refused === null ? null : (
                    <p role="status" className="px-4.5 pb-2.25 text-sm text-muted text-pretty">
                        {refused}
                    </p>
                )}

                <div className="flex flex-wrap justify-end gap-2.25 px-4.5 pb-4.5">
                    <button
                        type="button"
                        className={mannerDrawn.back}
                        onClick={() => {
                            asked.current?.close();
                        }}
                    >
                        {translate('act.cancel')}
                    </button>

                    <button
                        type="submit"
                        disabled={!savable}
                        aria-disabled={refused === null ? undefined : true}
                        className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100 aria-disabled:cursor-not-allowed aria-disabled:opacity-60`}
                    >
                        {translate('tasks.saveTask')}
                    </button>
                </div>
            </form>
        </dialog>
    );
}
