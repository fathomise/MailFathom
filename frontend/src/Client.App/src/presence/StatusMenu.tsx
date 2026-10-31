// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ToggleEvent } from 'react';
import {
    presenceDurations,
    presenceStates,
    type ClientFailureReason,
    type PresenceDuration,
    type PresenceState,
} from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { PersonAvatar } from '../controls/PersonAvatar';
import { SymbolControl } from '../controls/SymbolControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { useToasts } from '../toasts/useToasts';
import { PresenceDot } from './PresenceDot';
import { presenceSaid, wordDuration } from './presenceWords';
import { returnFocus, statusMenu } from './statusMenuOpening';
import type { OwnPresenceInForce } from './useOwnPresence';

// The member's own status, set from the row under their name in the user menu — the head of the *More* sheet on a
// phone. It is a menu rather than the Settings screen, and it has no *Done* but *Duration*'s: every other choice takes
// the moment it is made, and picking a state takes it and closes the menu the way any menu's item does. A state picked
// from the list lasts until changed, which ends any while chosen before; *Duration* is a state and when to reset it,
// taken together by its own *Done*. A 272 px menu beside the rail where the user menu stood, and a sheet on a phone;
// `statusMenuOpening.ts` is how a row in another surface reaches it.

// What a refused choice says beneath *Your status could not be changed*: each reason has its own way out, and only a
// server that did not answer is one trying again can fix.
const refusals: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'presence.notChosenUnauthenticated',
    unauthorized: 'presence.notChosenUnauthorized',
    unavailable: 'presence.notChosenUnavailable',
    unreadable: 'presence.notChosenUnreadable',
    missing: 'presence.notChosenMissing',
};

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
    /** The member's presence and the way of changing it. */
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
    const [onDuration, setOnDuration] = useState(false);
    const [returnedFromDuration, setReturnedFromDuration] = useState(false);

    // Whether the platform is showing the menu, as its own toggle last said: a page changed while it is closed — which
    // closing it does — moves no focus.
    const standing = useRef(false);

    // A page replaced the one in front, so focus goes to where the new one starts: the way back on *Duration*'s, and
    // the *Duration* row where somebody came back from it.
    useEffect(() => {
        if (!standing.current || menu.current === null) {
            return;
        }

        const start = onDuration ? '[data-page-start] button' : returnedFromDuration ? '[data-duration-row]' : null;

        if (start !== null) {
            menu.current.querySelector<HTMLElement>(start)?.focus();
        }
    }, [onDuration, returnedFromDuration]);

    function toggled(event: ToggleEvent<HTMLDivElement>): void {
        closesOnBack(event);
        standing.current = event.newState === 'open';

        if (event.newState === 'open') {
            event.currentTarget.querySelector<HTMLElement>('[role="menuitemradio"][aria-checked="true"]')?.focus();

            return;
        }

        setOnDuration(false);
        setReturnedFromDuration(false);
        returnFocus(event.currentTarget);
    }

    // Whatever was chosen closes the menu, on its first page for the next time it opens.
    function choose(state: PresenceState, duration: PresenceDuration | null): void {
        menu.current?.hidePopover();
        setOnDuration(false);
        setReturnedFromDuration(false);

        void own.choose(state, duration).then((refused) => {
            if (refused === null) {
                return;
            }

            toasts.raise({
                kind: 'error',
                title: translate('presence.notChosen'),
                body: translate(refusals[refused]),
                ...(refused === 'unavailable'
                    ? {
                          action: {
                              label: translate('presence.chooseAgain'),
                              take: () => {
                                  choose(state, duration);
                              },
                          },
                      }
                    : {}),
            });
        });
    }

    return (
        // No display utility on the popover itself, for the reason the account menu's own carries none: the platform
        // hides a closed popover from its own stylesheet, and a utility here would outrank that.
        <div
            ref={menu}
            id={statusMenu}
            popover="auto"
            role={onDuration ? 'dialog' : 'menu'}
            aria-label={translate(onDuration ? 'presence.duration' : 'presence.yours')}
            data-pull="down"
            onToggle={toggled}
            className="not-rail:surface-sheet rail:surface-pop inset-x-0 top-auto bottom-0 mx-auto my-0 max-h-sheet-height w-full max-w-140 overflow-y-auto rounded-t-xl border-t border-line bg-panel px-2 pb-safe-bottom text-base text-text shadow-overlay backdrop:bg-scrim open:flex open:flex-col rail:inset-x-auto rail:bottom-4.5 rail:left-26.5 rail:w-68 rail:rounded-lg rail:border rail:px-1.25 rail:pb-1.25 rail:backdrop:bg-transparent"
        >
            {/* The bar a sheet draws across its top, which is decoration: the pull is the frame's rule for every sheet. */}
            <span
                aria-hidden="true"
                className="mx-auto mt-2.5 mb-1.5 h-1 w-9.5 shrink-0 rounded-full bg-line-strong rail:hidden"
            />

            {onDuration ? (
                <DurationPage
                    own={own}
                    onBack={() => {
                        setOnDuration(false);
                        setReturnedFromDuration(true);
                    }}
                    onDone={choose}
                />
            ) : (
                <>
                    <div className="-mx-1.25 mb-1 flex items-center gap-2.5 border-b border-line-soft py-2 ps-3.5 pe-2.25 rail:pt-2.5">
                        <span className="relative shrink-0">
                            <PersonAvatar displayName={displayName} picture={picture} place="status" />
                            <PresenceDot presence={own.presence.state} place="head" />
                        </span>
                        <span className="flex min-w-0 flex-1 flex-col gap-px">
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

                        <button
                            type="button"
                            role="menuitem"
                            aria-haspopup="true"
                            data-duration-row=""
                            className="flex min-h-8 w-full items-center gap-2.5 rounded-md px-2.25 text-start transition hover:bg-hover pointer-coarse:min-h-11"
                            onClick={() => {
                                setOnDuration(true);
                            }}
                        >
                            <Icon name="timer" className="size-icon-md text-muted" />
                            <span className="shrink-0">{translate('presence.duration')}</span>
                            <span className="min-w-0 flex-1 truncate text-end text-sm text-text-soft">
                                {said.within ?? translate('presence.untilChanged')}
                            </span>
                            <Icon name="chevron_right" className="size-icon-md text-faint" />
                        </button>
                    </div>
                </>
            )}
        </div>
    );
}

// *Duration*, in place of the first page: a state and when to reset it, both taken together by *Done*. The state is a
// list of its own under the field rather than a native select, which cannot draw the dots; when to reset it is one.
function DurationPage({
    own,
    onBack,
    onDone,
}: {
    readonly own: OwnPresenceInForce;
    readonly onBack: () => void;
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
        <>
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
                <span className="font-semibold">{translate('presence.duration')}</span>
            </div>

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
                    <Icon
                        name="expand_more"
                        className="pointer-events-none absolute end-2.25 size-icon-md text-muted"
                    />
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
        </>
    );
}
