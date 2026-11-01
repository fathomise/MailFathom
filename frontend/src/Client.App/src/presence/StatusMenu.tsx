// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ToggleEvent } from 'react';
import {
    longestStatusMessage,
    presenceDurations,
    presenceStates,
    type ClientFailureReason,
    type PresenceDuration,
    type PresenceState,
    type WorkMode,
} from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { PersonAvatar } from '../controls/PersonAvatar';
import { SymbolControl } from '../controls/SymbolControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { useToasts } from '../toasts/useToasts';
import { PresenceDot } from './PresenceDot';
import { presenceSaid, statedWorkModes, wordDuration, workModeSaid } from './presenceWords';
import { returnFocus, statusMenu } from './statusMenuOpening';
import type { OwnPresenceInForce } from './useOwnPresence';

// The member's own status, set from the row under their name in the user menu — the head of the *More* sheet on a
// phone. It is a menu rather than the Settings screen, and it has no *Done* but *Duration*'s: every other choice takes
// the moment it is made, and picking a state takes it and closes the menu the way any menu's item does. A state picked
// from the list lasts until changed, which ends any while chosen before; *Duration* is a state and when to reset it,
// taken together by its own *Done*. *Message* and *Working from* are pages of the same menu: a place picked on the
// second takes and comes back, and what was typed on the first is kept however the page is left — Enter, the way back,
// or the menu closing. A 272 px menu beside the rail where the user menu stood, and a sheet on a phone;
// `statusMenuOpening.ts` is how a row in another surface reaches it.

/** The page of the menu in front: its first one, or one of the three its rows lead to. */
type StatusPage = 'main' | 'duration' | 'message' | 'work';

/** What is being written on *Message*, kept here so that closing the menu keeps it as leaving the page does. */
interface MessageDraft {
    readonly text: string;
    readonly clears: PresenceDuration | null;
}

// What a refused write says beneath *Your status could not be changed*: each reason has its own way out, and only a
// server that did not answer is one trying again can fix.
const refusals: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'presence.notChosenUnauthenticated',
    unauthorized: 'presence.notChosenUnauthorized',
    unavailable: 'presence.notChosenUnavailable',
    unreadable: 'presence.notChosenUnreadable',
    missing: 'presence.notChosenMissing',
};

// When a message clears, in the four the design offers, and what each segment is called.
const clearings: readonly (PresenceDuration | null)[] = [null, '1h', 'today', 'week'];

// The count turns to a warning this close to the bound.
const nearTheBound = 260;

// The items of the page in front, which the arrow keys move between as they do in any menu.
function moveAmong(event: KeyboardEvent<HTMLElement>, items: string): void {
    const offset = { ArrowDown: 1, ArrowUp: -1 }[event.key];
    const among = [...event.currentTarget.querySelectorAll<HTMLElement>(items)];
    const at = among.findIndex((item) => item === document.activeElement);

    if (offset === undefined || among.length === 0) {
        return;
    }

    event.preventDefault();
    among[(at + offset + among.length) % among.length]?.focus();
}

// A message is one line of text wherever it is drawn, so a line break typed or pasted into the field is a space by the
// time it is written, and a formatting character pasted with it is dropped. The two joiners stay, because the scripts
// that need them write names with them; the deployment refuses the same set rather than stripping it.
function oneLine(text: string): string {
    return text
        .replace(/[\p{Cc}\p{Zl}\p{Zp}]+/gu, ' ')
        .replace(/\p{Cf}/gu, (formatting) => (formatting === '\u200C' || formatting === '\u200D' ? formatting : ''))
        .trim();
}

// The state, and the while it holds for: *Busy · 1 hour* where a while was chosen, and the state alone where it lasts
// until changed.
function useWhileSaid(own: OwnPresenceInForce): { readonly state: string; readonly within: string | null } {
    const { locale, translate } = useLocalization();
    const state = translate(presenceSaid[own.presence.state]);
    const chosenFor = own.presence.choiceDuration;

    if (chosenFor === null) {
        return { state, within: null };
    }

    const duration = wordDuration(chosenFor, locale, translate).toLocaleLowerCase(locale);

    return { state, within: translate('presence.stateFor', { state, duration }) };
}

export function StatusMenu({
    own,
    displayName,
    picture,
}: {
    /** The member's presence and the ways of changing it. */
    readonly own: OwnPresenceInForce;

    /** What this deployment records the member as called, or `null` while nothing has answered. */
    readonly displayName: string | null;

    /** Where their picture may be drawn from, or `null` where they have none. */
    readonly picture: string | null;
}) {
    const { translate } = useLocalization();
    const toasts = useToasts();
    const closesOnBack = useTopLayerSurface();
    const said = useWhileSaid(own);
    const menu = useRef<HTMLDivElement>(null);
    const [page, setPage] = useState<StatusPage>('main');
    const [returnedFrom, setReturnedFrom] = useState<StatusPage | null>(null);
    const [draft, setDraft] = useState<MessageDraft | null>(null);
    const message = own.presence.statusMessage;
    const workMode = own.presence.workMode;

    // Whether the platform is showing the menu, as its own toggle last said: a page changed while it is closed — which
    // closing it does — moves no focus.
    const standing = useRef(false);

    // A page replaced the one in front, so focus goes to where the new one starts: the way back on a page of its own,
    // and the row that led there where somebody came back from one.
    useEffect(() => {
        if (!standing.current || menu.current === null) {
            return;
        }

        const start =
            page !== 'main'
                ? '[data-page-start] button'
                : returnedFrom !== null
                  ? `[data-row="${returnedFrom}"]`
                  : null;

        if (start !== null) {
            menu.current.querySelector<HTMLElement>(start)?.focus();
        }
    }, [page, returnedFrom]);

    function refused(why: ClientFailureReason, again: () => void): void {
        toasts.raise({
            kind: 'error',
            title: translate('presence.notChosen'),
            body: translate(refusals[why]),
            ...(why === 'unavailable' ? { action: { label: translate('presence.chooseAgain'), take: again } } : {}),
        });
    }

    function writeMessage(text: string, clears: PresenceDuration | null): void {
        void own.writeMessage(text, clears).then((why) => {
            if (why !== null) {
                refused(why, () => {
                    writeMessage(text, clears);
                });
            }
        });
    }

    // What was typed is written once the page is left, however it is left, and only where it says something new.
    function keepDraft(): void {
        if (draft === null) {
            return;
        }

        const text = oneLine(draft.text);
        const unchanged = text === (message?.text ?? '') && (text === '' || draft.clears === (message?.clears ?? null));

        setDraft(null);

        if (!unchanged) {
            writeMessage(text, draft.clears);
        }
    }

    function open(next: Exclude<StatusPage, 'main'>): void {
        if (next === 'message') {
            setDraft({ text: message?.text ?? '', clears: message === null ? 'today' : message.clears });
        }

        setReturnedFrom(null);
        setPage(next);
    }

    function back(): void {
        keepDraft();
        setReturnedFrom(page);
        setPage('main');
    }

    function toggled(event: ToggleEvent<HTMLDivElement>): void {
        closesOnBack(event);
        standing.current = event.newState === 'open';

        if (event.newState === 'open') {
            event.currentTarget.querySelector<HTMLElement>('[role="menuitemradio"][aria-checked="true"]')?.focus();

            return;
        }

        keepDraft();
        setPage('main');
        setReturnedFrom(null);
        returnFocus(event.currentTarget);
    }

    // Whatever was chosen closes the menu, on its first page for the next time it opens.
    function choose(state: PresenceState, duration: PresenceDuration | null): void {
        menu.current?.hidePopover();
        setPage('main');
        setReturnedFrom(null);

        void own.choose(state, duration).then((why) => {
            if (why !== null) {
                refused(why, () => {
                    choose(state, duration);
                });
            }
        });
    }

    function chooseWorkMode(mode: WorkMode, today: boolean): void {
        setReturnedFrom('work');
        setPage('main');

        void own.chooseWorkMode(mode, today).then((why) => {
            if (why !== null) {
                refused(why, () => {
                    chooseWorkMode(mode, today);
                });
            }
        });
    }

    function clearMessage(): void {
        setDraft(null);
        setReturnedFrom('message');
        setPage('main');

        if (message !== null) {
            writeMessage('', null);
        }
    }

    return (
        // No display utility on the popover itself, for the reason the account menu's own carries none: the platform
        // hides a closed popover from its own stylesheet, and a utility here would outrank that.
        <div
            ref={menu}
            id={statusMenu}
            popover="auto"
            role={page === 'main' ? 'menu' : 'dialog'}
            aria-label={translate(pageTitles[page])}
            data-pull="down"
            onToggle={toggled}
            className="bar:surface-sheet rail:surface-pop inset-x-0 top-auto bottom-0 mx-auto my-0 max-h-sheet-height w-full max-w-140 overflow-y-auto rounded-t-xl border-t border-line bg-panel px-2 pb-safe-bottom text-base text-text shadow-overlay backdrop:bg-scrim open:flex open:flex-col rail:inset-x-auto rail:bottom-4.5 rail:left-26.5 rail:w-71 rail:rounded-lg rail:border rail:px-1.25 rail:pb-1.25 rail:backdrop:bg-transparent"
        >
            {/* The bar a sheet draws across its top, which is decoration: the pull is the frame's rule for every sheet. */}
            <span
                aria-hidden="true"
                className="mx-auto mt-2.5 mb-1.5 h-1 w-9.5 shrink-0 rounded-full bg-line-strong rail:hidden"
            />

            {page === 'main' ? (
                <>
                    <div className="-mx-1.25 mb-1 flex items-center gap-2.5 border-b border-line-soft py-2 ps-3.5 pe-2.25 rail:pt-2.5">
                        <span className="relative shrink-0">
                            <PersonAvatar displayName={displayName} picture={picture} place="status" />
                            <PresenceDot presence={own.presence.state} place="head" />
                        </span>
                        <span className="flex min-w-0 flex-1 flex-col gap-px leading-natural">
                            {displayName === null ? null : (
                                <span className="truncate font-semibold">{displayName}</span>
                            )}
                            <span className="text-sm text-muted">{said.within ?? said.state}</span>
                        </span>
                    </div>

                    <div
                        className="flex min-h-0 flex-1 flex-col gap-px overflow-auto"
                        onKeyDown={(event) => {
                            moveAmong(event, '[role="menuitemradio"], [role="menuitem"]');
                        }}
                    >
                        {presenceStates.map((state) => (
                            <button
                                key={state}
                                type="button"
                                role="menuitemradio"
                                aria-checked={state === own.presence.state}
                                className={`flex min-h-8 w-full items-center gap-2.5 rounded-md px-2.25 text-start transition hover:bg-hover pointer-coarse:min-h-11 ${
                                    state === own.presence.state ? 'bg-accent-soft' : ''
                                }`}
                                onClick={() => {
                                    choose(state, null);
                                }}
                            >
                                <PresenceDot presence={state} place="line" />
                                <span className="flex min-w-0 flex-1 flex-col gap-px">
                                    <span>{translate(presenceSaid[state])}</span>
                                    {state === 'Hidden' ? (
                                        <span className="text-xs text-muted">{translate('presence.hiddenNote')}</span>
                                    ) : null}
                                </span>
                                {state === own.presence.state ? (
                                    <Icon name="check" className="size-icon-md text-accent" />
                                ) : null}
                            </button>
                        ))}

                        <span role="separator" className="mx-1 my-1 h-px shrink-0 bg-line-soft" />

                        <PageRow
                            row="duration"
                            icon="timer"
                            label={translate('presence.duration')}
                            value={said.within ?? translate('presence.untilChanged')}
                            stated
                            onOpen={() => {
                                open('duration');
                            }}
                        />
                        <PageRow
                            row="message"
                            icon="chat_bubble"
                            label={translate('presence.message')}
                            value={message?.text ?? translate('presence.setMessage')}
                            stated={message !== null}
                            onOpen={() => {
                                open('message');
                            }}
                        />
                        <PageRow
                            row="work"
                            icon="apartment"
                            label={translate('presence.workingFrom')}
                            value={translate(workMode === null ? 'presence.notStated' : workModeSaid(workMode))}
                            stated={workMode !== null}
                            onOpen={() => {
                                open('work');
                            }}
                        />
                    </div>
                </>
            ) : (
                <>
                    <PageHead title={translate(pageTitles[page])} onBack={back} />

                    {page === 'duration' ? <DurationPage own={own} onDone={choose} /> : null}

                    {page === 'work' ? (
                        <div
                            role="radiogroup"
                            aria-label={translate('presence.workingFrom')}
                            className="flex flex-col gap-px overflow-auto"
                            onKeyDown={(event) => {
                                moveAmong(event, '[role="radio"]');
                            }}
                        >
                            {statedWorkModes.map((offered) => {
                                const current =
                                    workMode !== null &&
                                    workMode.mode === offered.mode &&
                                    (workMode.endsAt !== null) === offered.today;

                                return (
                                    <button
                                        key={offered.key}
                                        type="button"
                                        role="radio"
                                        aria-checked={current}
                                        className={`flex min-h-8 w-full items-center gap-2.5 rounded-md px-2.25 text-start transition hover:bg-hover pointer-coarse:min-h-11 ${
                                            current ? 'bg-accent-soft' : ''
                                        }`}
                                        onClick={() => {
                                            chooseWorkMode(offered.mode, offered.today);
                                        }}
                                    >
                                        <Icon
                                            name={offered.mode === 'Remote' ? 'home_work' : 'apartment'}
                                            className="size-icon-md text-muted"
                                        />
                                        <span className="flex-1">{translate(offered.key)}</span>
                                        {current ? <Icon name="check" className="size-icon-md text-accent" /> : null}
                                    </button>
                                );
                            })}
                        </div>
                    ) : null}

                    {page === 'message' && draft !== null ? (
                        <MessagePage
                            draft={draft}
                            held={message !== null}
                            onDraft={setDraft}
                            onKeep={back}
                            onClear={clearMessage}
                        />
                    ) : null}
                </>
            )}
        </div>
    );
}

// What each page is called, which is the menu's name while it stands in front.
const pageTitles: Readonly<Record<StatusPage, MessageKey>> = {
    main: 'presence.yours',
    duration: 'presence.duration',
    message: 'presence.statusMessage',
    work: 'presence.workingFrom',
};

// A row under the states that leads to a page of the menu, showing what that page holds — in `faint` where nothing
// is stated yet.
function PageRow({
    row,
    icon,
    label,
    value,
    stated,
    onOpen,
}: {
    readonly row: Exclude<StatusPage, 'main'>;
    readonly icon: IconName;
    readonly label: string;
    readonly value: string;
    readonly stated: boolean;
    readonly onOpen: () => void;
}) {
    return (
        <button
            type="button"
            role="menuitem"
            aria-haspopup="true"
            data-row={row}
            className="flex min-h-8 w-full items-center gap-2.5 rounded-md px-2.25 text-start transition hover:bg-hover pointer-coarse:min-h-11"
            onClick={onOpen}
        >
            <Icon name={icon} className="size-icon-md text-muted" />
            <span className="shrink-0">{label}</span>
            <span className={`min-w-0 flex-1 truncate text-end text-sm ${stated ? 'text-text-soft' : 'text-faint'}`}>
                {value}
            </span>
            <Icon name="chevron_right" className="size-icon-md text-faint" />
        </button>
    );
}

// The head every page but the first stands under: the way back, and the page's name.
function PageHead({ title, onBack }: { readonly title: string; readonly onBack: () => void }) {
    const { translate } = useLocalization();

    return (
        <div className="-mx-1.25 mb-1 flex items-center gap-1.5 border-b border-line-soft px-1 py-1.5">
            <span data-page-start="" className="contents">
                <SymbolControl
                    label={translate('presence.back')}
                    className="flex size-7 shrink-0 items-center justify-center rounded-md text-muted hover:bg-hover hover:text-text"
                    onPress={onBack}
                >
                    <Icon name="arrow_back" className="size-icon-md pointer-coarse:size-icon-2xl" />
                </SymbolControl>
            </span>
            <span className="font-semibold">{title}</span>
        </div>
    );
}

// *Message*, in place of the first page: a field of at most 280 characters, when it clears, and a way of clearing it.
// Nothing here writes: what was typed is kept by leaving the page, which Enter does as well.
function MessagePage({
    draft,
    held,
    onDraft,
    onKeep,
    onClear,
}: {
    readonly draft: MessageDraft;

    /** Whether a message is held, which is what *Clear status message* would clear. */
    readonly held: boolean;

    readonly onDraft: (draft: MessageDraft) => void;
    readonly onKeep: () => void;
    readonly onClear: () => void;
}) {
    const { locale, translate } = useLocalization();
    const clearsLabel = useId();

    return (
        <div className="flex flex-col gap-1.75 overflow-auto px-1 pt-0.5 pb-1">
            <textarea
                rows={2}
                maxLength={longestStatusMessage}
                value={draft.text}
                aria-label={translate('presence.statusMessage')}
                placeholder={translate('presence.messagePlaceholder')}
                className="w-full resize-none rounded-md border border-line-strong bg-sunken px-2.5 py-2 text-base text-text outline-none focus:border-accent pointer-coarse:text-lg"
                onChange={(typed) => {
                    onDraft({ ...draft, text: typed.target.value });
                }}
                onKeyDown={(event) => {
                    if (event.key === 'Enter' && !event.shiftKey) {
                        event.preventDefault();
                        onKeep();
                    }
                }}
            />

            <div className="flex items-center gap-2">
                <span className="text-xs text-muted">{translate('presence.messageSaved')}</span>
                <span
                    className={`ms-auto text-xs whitespace-nowrap tabular-nums ${
                        draft.text.length >= nearTheBound ? 'text-warning-text' : 'text-faint'
                    }`}
                >
                    {translate('presence.messageCount', {
                        count: draft.text.length.toLocaleString(locale),
                        most: longestStatusMessage.toLocaleString(locale),
                    })}
                </span>
            </div>

            <span id={clearsLabel} className="text-xs font-semibold text-muted">
                {translate('presence.clears')}
            </span>

            <div role="group" aria-labelledby={clearsLabel} className="flex gap-1.25">
                {clearings.map((clears) => (
                    <button
                        key={clears ?? 'never'}
                        type="button"
                        aria-pressed={clears === draft.clears}
                        className={`touch-extended flex flex-1 items-center justify-center rounded-md border px-1 py-1.25 text-sm whitespace-nowrap ${
                            clears === draft.clears
                                ? 'border-accent bg-accent-soft font-semibold text-accent-deep'
                                : 'border-line bg-panel text-text-soft hover:border-accent'
                        }`}
                        onClick={() => {
                            onDraft({ ...draft, clears });
                        }}
                    >
                        {clears === null ? translate('presence.clearsNever') : wordDuration(clears, locale, translate)}
                    </button>
                ))}
            </div>

            {held || draft.text !== '' ? (
                <button
                    type="button"
                    className="-mx-1 mt-0.5 flex min-h-8 items-center gap-2.5 rounded-md px-2.25 text-start text-warning-text transition hover:bg-hover pointer-coarse:min-h-11"
                    onClick={onClear}
                >
                    <Icon name="backspace" className="size-icon-md" />
                    {translate('presence.clearMessage')}
                </button>
            ) : null}
        </div>
    );
}

// *Duration*, in place of the first page: a state and when to reset it, both taken together by *Done*. The state is a
// list of its own under the field rather than a native select, which cannot draw the dots; when to reset it is one.
function DurationPage({
    own,
    onDone,
}: {
    readonly own: OwnPresenceInForce;
    readonly onDone: (state: PresenceState, duration: PresenceDuration) => void;
}) {
    const { locale, translate } = useLocalization();
    const [state, setState] = useState(own.presence.state);
    const [after, setAfter] = useState<PresenceDuration>(own.presence.choiceDuration ?? 'today');
    const [listing, setListing] = useState(false);
    const field = useRef<HTMLButtonElement>(null);
    const list = useRef<HTMLDivElement>(null);
    const listId = useId();

    useEffect(() => {
        if (listing) {
            list.current?.querySelector<HTMLElement>('[aria-selected="true"]')?.focus();
        }
    }, [listing]);

    function pick(chosen: PresenceState): void {
        setState(chosen);
        setListing(false);
        field.current?.focus();
    }

    return (
        <div className="flex flex-col gap-1.75 overflow-auto px-1 pt-1 pb-1.5">
            <span className="text-xs font-semibold text-muted">{translate('presence.status')}</span>

            <button
                ref={field}
                type="button"
                role="combobox"
                aria-label={translate('presence.status')}
                aria-haspopup="listbox"
                aria-expanded={listing}
                aria-controls={listId}
                className={`flex h-9 w-full items-center gap-2.5 rounded-md border bg-sunken ps-3 pe-2.25 text-start text-text hover:border-accent pointer-coarse:text-lg ${
                    listing ? 'border-accent' : 'border-line-strong'
                }`}
                onClick={() => {
                    setListing(!listing);
                }}
            >
                <PresenceDot presence={state} place="line" />
                <span className="min-w-0 flex-1">{translate(presenceSaid[state])}</span>
                <Icon
                    name="expand_more"
                    className={`size-icon-md text-muted transition-transform ${listing ? 'rotate-180' : ''}`}
                />
            </button>

            {listing ? (
                <div
                    ref={list}
                    id={listId}
                    role="listbox"
                    aria-label={translate('presence.status')}
                    className="flex flex-col gap-px rounded-md border border-line bg-sunken p-1"
                    onKeyDown={(event) => {
                        if (event.key === 'Escape') {
                            // The list closes rather than the menu around it.
                            event.preventDefault();
                            event.stopPropagation();
                            setListing(false);
                            field.current?.focus();

                            return;
                        }

                        moveAmong(event, '[role="option"]');
                    }}
                >
                    {presenceStates.map((offered) => (
                        <div
                            key={offered}
                            role="option"
                            tabIndex={-1}
                            aria-selected={offered === state}
                            className={`flex min-h-8 cursor-pointer items-center gap-2.5 rounded-md px-2.25 outline-offset-0 hover:bg-hover pointer-coarse:min-h-11 ${
                                offered === state ? 'bg-accent-soft' : ''
                            }`}
                            onClick={() => {
                                pick(offered);
                            }}
                            onKeyDown={(event) => {
                                if (event.key === 'Enter' || event.key === ' ') {
                                    event.preventDefault();
                                    pick(offered);
                                }
                            }}
                        >
                            <PresenceDot presence={offered} place="line" />
                            <span className="flex-1">{translate(presenceSaid[offered])}</span>
                            {offered === state ? <Icon name="check" className="size-icon-md text-accent" /> : null}
                        </div>
                    ))}
                </div>
            ) : null}

            <span className="pt-1.5 text-xs font-semibold text-muted">{translate('presence.resetAfter')}</span>

            <span className="relative flex items-center">
                <select
                    aria-label={translate('presence.resetAfter')}
                    value={after}
                    className="h-9 w-full appearance-none rounded-md border border-line-strong bg-sunken ps-3 pe-8 text-text outline-none focus:border-accent pointer-coarse:text-lg"
                    onChange={(choosing) => {
                        const chosen = presenceDurations.find((duration) => duration === choosing.target.value);

                        if (chosen !== undefined) {
                            setAfter(chosen);
                        }
                    }}
                >
                    {presenceDurations.map((duration) => (
                        <option key={duration} value={duration}>
                            {wordDuration(duration, locale, translate)}
                        </option>
                    ))}
                </select>
                <Icon name="expand_more" className="pointer-events-none absolute end-2.25 size-icon-md text-muted" />
            </span>

            <Control
                label={translate('presence.done')}
                shape="primary"
                className="mt-2.5 self-end px-4.5"
                onPress={() => {
                    onDone(state, after);
                }}
            />
        </div>
    );
}
