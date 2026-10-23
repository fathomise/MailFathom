// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useContext, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useAgentHandOver, type AgentHandOver } from '../routing/agentHandOver';
import { Departing } from '../shell/Departing';
import { DepartureContext } from '../shell/departure';
import { useScreenLayer } from '../shell/screenLayers';
import { useDesktopComposition, useTwoPanes } from '../shell/useWideWorkspace';
import { fragmentBeingRead } from '../workspace/selectedFragment';
import { useWorkspace } from '../workspace/useWorkspace';
import { useReplyDrafting } from './replyDrafting';

// The one AI entry point at the foot of a correspondence, as the design draws it there and nowhere else: a floating
// `auto_awesome` button at the reading column's bottom corner that unfolds two choices above it — *Draft a reply*,
// which opens the reply drafter, and *Ask*, which hands the conversation to the agent. The drafter, once open, is a card
// hanging over the bottom of the thread with a field, a press that drafts a reply, and — where the composition has
// room — a row saying what the draft is about and where it stays. It is not Discover's field and shares nothing with it.
//
// **It stands at the foot of the thread, stuck to the bottom of the column's scroller**, so the room it takes is the
// thread's bottom clearance rather than a padding measured beside it: the last line of the thread scrolls clear of the
// button, and clear of the card while it is open, by exactly what each of them is, and the button stays at the bottom
// however far the conversation is scrolled.
//
// **What is typed here is about this correspondence and nothing else.** It is held by this component rather than by the
// workspace, and the frame mounts a fresh one for each message that is opened: a sentence written under one message is
// not a sentence about the next, and opening another message closes the drafter with it.
//
// **The press drafts in place and closes the card.** A reply is asked for here and drawn inside the thread as a card the
// reader checks before anything else happens to it, so nothing here sends; *Ask* is the one choice that navigates.

/**
 * The AI button a message or a conversation draws as the last thing in it, given the conversation an *Ask* hands over —
 * which is why the reading surface draws it rather than the column around it: the surface is what knows that thread.
 */
export type ReadingFoot = (thread: AgentHandOver | null) => ReactNode;

const choiceShape =
    'flex h-10 items-center gap-2.5 rounded-lg border border-line bg-panel ps-3 pe-4 text-md font-semibold whitespace-nowrap text-text shadow-overlay transition hover:bg-hover pointer-coarse:h-12 pointer-coarse:ps-3.5 pointer-coarse:pe-4.5 pointer-coarse:text-lg';

export function ThreadAgentField({
    thread,
    drafts,
}: {
    /** The conversation as it is handed to the agent, or `null` where what is open belongs to none. */
    readonly thread: AgentHandOver | null;

    /** Whether this deployment drafts replies, which is what decides whether *Draft a reply* is offered at all. */
    readonly drafts: boolean;
}) {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    const drafting = useReplyDrafting();
    const handToAgent = useAgentHandOver();
    const twoPanes = useTwoPanes();
    const desktop = useDesktopComposition();
    const choices = useId();
    const chooser = useRef<HTMLDivElement | null>(null);
    const [choosing, setChoosing] = useState(false);
    const [open, setOpen] = useState(false);
    const [typed, setTyped] = useState('');

    const draftingOffered = drafts && drafting !== null;
    const asking =
        handToAgent === null || thread === null
            ? null
            : () => {
                  handToAgent(thread);
              };

    // Focus goes back to the button whenever the card folds into it, which is the button mounting again — so it is the
    // button's own ref that honours the debt rather than an effect watching the state that dropped it.
    const buttonFocusOwed = useRef(false);
    const choiceFocusOwed = useRef(false);

    // The field is held while the drafter is open, so a draft discarded with the drafter open puts the keyboard on the
    // field rather than on the button that is not drawn; and it takes the keyboard as the card arrives, and only then.
    const holdField = drafting?.holdField;
    const holdTheField = useCallback(
        (field: HTMLInputElement | null) => {
            if (field !== null) {
                holdField?.(field);
                field.focus();
            }
        },
        [holdField],
    );

    const passage = fragmentBeingRead(workspace);
    const answering = workspace.selection;
    const busy = drafting?.draft?.body === null && drafting.draft.answering === answering;

    function close(clearingTheScreen = false): void {
        buttonFocusOwed.current = !clearingTheScreen;
        setOpen(false);
    }

    // A surface over the screen like any other, so the back gesture folds it before it leaves the thread — and the two
    // choices are one too, folded by the same gesture before the drafter or the thread is.
    useScreenLayer(open, close);
    useScreenLayer(choosing, (clearingTheScreen) => {
        fold(!clearingTheScreen);
    });

    // A press anywhere outside the button and its choices folds them, which is the one way of leaving them the design
    // draws without a control of its own. A subscription to the document for as long as they stand, and no longer.
    useEffect(() => {
        if (!choosing) {
            return;
        }

        function pressed(event: PointerEvent): void {
            if (!(event.target instanceof Node) || chooser.current?.contains(event.target) !== true) {
                setChoosing(false);
            }
        }

        document.addEventListener('pointerdown', pressed);

        return () => {
            document.removeEventListener('pointerdown', pressed);
        };
    }, [choosing]);

    // The first choice takes the keyboard as the choices unfold, and only then: a ref runs on every render, so the debt
    // is what stops a later render taking focus back from wherever the reader has moved it since.
    function takeChoiceFocus(element: HTMLButtonElement | null): void {
        if (element !== null && choiceFocusOwed.current) {
            choiceFocusOwed.current = false;
            element.focus();
        }
    }

    // Folds the choices, putting the keyboard back on the button where the reader asked for that rather than walked off.
    function fold(returningFocus: boolean): void {
        buttonFocusOwed.current = returningFocus;
        setChoosing(false);
    }

    function draftAReply(): void {
        if (drafting === null || answering === null || busy) {
            return;
        }

        drafting.draftReply(answering, typed, passage?.text ?? null);
        close();
    }

    if (!draftingOffered && asking === null) {
        return null;
    }

    return (
        <div className="pointer-events-none sticky bottom-0 z-10 mt-auto flex shrink-0 flex-col items-end">
            {/* Said as well as drawn: a passage somebody selected is part of what the reply is written about, and a
                reader who cannot see the chip — or a composition that draws none — would otherwise not be told. Held
                at the start edge, because out of sight is still laid out: at the end edge it would hang a pixel past
                the column and give the reading column a horizontal scrollbar it has no content for. */}
            <p className="sr-only self-start" role="status">
                {passage === null ? '' : translate('scope.fragment', { fragment: passage.text })}
            </p>

            {open ? null : (
                <div
                    ref={chooser}
                    className="pointer-events-auto mt-3.5 me-4 mb-4 flex flex-col items-end gap-2 panes:mt-5 panes:me-5.5 panes:mb-5"
                    onKeyDown={(event) => {
                        if (event.key === 'Escape' && choosing) {
                            event.preventDefault();
                            fold(true);
                        }
                    }}
                >
                    {choosing ? (
                        <div
                            id={choices}
                            role="group"
                            aria-label={translate('threadAgent.choose')}
                            className="flex animate-pop-in flex-col items-end gap-2"
                        >
                            {draftingOffered ? (
                                <button
                                    ref={takeChoiceFocus}
                                    type="button"
                                    className={choiceShape}
                                    onClick={() => {
                                        setChoosing(false);
                                        setOpen(true);
                                    }}
                                >
                                    <Icon name="edit_note" className="size-icon-lg shrink-0 text-accent-deep" />
                                    {translate('threadAgent.draftReply')}
                                </button>
                            ) : null}

                            {asking === null ? null : (
                                <button
                                    ref={draftingOffered ? undefined : takeChoiceFocus}
                                    type="button"
                                    title={translate('message.askTitle')}
                                    className={choiceShape}
                                    onClick={() => {
                                        setChoosing(false);
                                        asking();
                                    }}
                                >
                                    <Icon name="forum" className="size-icon-lg shrink-0 text-accent-deep" />
                                    {translate('message.ask')}
                                </button>
                            )}
                        </div>
                    ) : null}

                    {/* The glyph alone at every width, its name the button's; pressed, it turns to a close while the
                        choices stand, and the expanded state says the same to somebody who cannot see it turn. */}
                    <button
                        ref={(element) => {
                            drafting?.holdField(element);

                            if (element !== null && buttonFocusOwed.current) {
                                buttonFocusOwed.current = false;
                                element.focus();
                            }
                        }}
                        type="button"
                        aria-label={translate('threadAgent.choose')}
                        title={translate('threadAgent.choose')}
                        aria-expanded={choosing}
                        aria-controls={choosing ? choices : undefined}
                        className="flex size-14 animate-pop-in items-center justify-center rounded-xl bg-accent text-on-accent shadow-overlay transition hover:bg-accent-strong workspace:size-12"
                        onClick={() => {
                            if (choosing) {
                                fold(true);
                            } else {
                                choiceFocusOwed.current = true;
                                setChoosing(true);
                            }
                        }}
                    >
                        <Icon name={choosing ? 'close' : 'auto_awesome'} className="size-icon-xl shrink-0" />
                    </button>
                </div>
            )}

            <Departing value={open ? true : null}>
                {() => (
                    <DrafterCard
                        typed={typed}
                        busy={busy}
                        passageChosen={passage !== null}
                        passageText={passage?.text}
                        twoPanes={twoPanes}
                        desktop={desktop}
                        onType={setTyped}
                        onDraft={draftAReply}
                        onClose={() => {
                            close();
                        }}
                        holdTheField={holdTheField}
                    />
                )}
            </Departing>
        </div>
    );
}

function DrafterCard({
    typed,
    busy,
    passageChosen,
    passageText,
    twoPanes,
    desktop,
    onType,
    onDraft,
    onClose,
    holdTheField,
}: {
    readonly typed: string;
    readonly busy: boolean;
    readonly passageChosen: boolean;
    readonly passageText: string | undefined;
    readonly twoPanes: boolean;
    readonly desktop: boolean;
    readonly onType: (typed: string) => void;
    readonly onDraft: () => void;
    readonly onClose: () => void;
    readonly holdTheField: (field: HTMLInputElement | null) => void;
}) {
    const { translate } = useLocalization();
    const leaving = useContext(DepartureContext);

    return (
        <form
            aria-label={translate('threadAgent.label')}
            className={`pointer-events-auto relative mx-2.5 mb-2.5 flex flex-col gap-2.5 self-stretch rounded-xl border border-line bg-panel p-2 shadow-dialog max-panes:pointer-coarse:pt-4 panes:mx-4.5 panes:mb-4 panes:p-3 ${
                leaving ? 'animate-rise-out' : 'animate-rise-in'
            }`}
            onSubmit={(event) => {
                event.preventDefault();
                onDraft();
            }}
        >
            <button
                type="button"
                aria-label={translate('threadAgent.close')}
                title={translate('threadAgent.close')}
                className="absolute -end-2.75 -top-2.75 z-1 flex size-6.5 items-center justify-center rounded-full border border-line bg-panel text-text-soft shadow-overlay transition hover:bg-hover hover:text-text pointer-coarse:-end-1 pointer-coarse:-top-4.75 pointer-coarse:size-8"
                onClick={onClose}
            >
                <Icon name="close" className="size-icon-sm pointer-coarse:size-icon-md" />
            </button>

            {/* The focus treatment stands on the box rather than on the field inside it: a ring drawn round a
                transparent field inside a bordered box reads as two nested borders rather than one field that lit up. */}
            <div className="flex min-w-0 items-center gap-2.5 rounded-full border border-accent-line bg-sunken py-1 ps-3.5 pe-1 transition focus-within:ring-3 focus-within:ring-accent-soft workspace:rounded-lg workspace:ps-3 panes:border-2 panes:border-accent panes:py-1.5 panes:ps-3.5 panes:pe-1.5">
                <Icon name="auto_awesome" className="size-icon-md shrink-0 text-accent-deep" />

                <input
                    ref={holdTheField}
                    type="text"
                    aria-label={translate('threadAgent.label')}
                    placeholder={translate(twoPanes ? 'threadAgent.placeholder' : 'threadAgent.placeholderShort')}
                    value={typed}
                    onChange={(event) => {
                        onType(event.target.value);
                    }}
                    onKeyDown={(event) => {
                        if (event.key === 'Escape') {
                            event.preventDefault();
                            onClose();
                        }
                    }}
                    className="min-w-0 flex-1 bg-transparent text-md text-text outline-none placeholder:text-faint panes:text-lg"
                />

                {/* `aria-disabled` rather than `disabled` while a reply is being written, so the control somebody just
                    pressed keeps their focus; the handler is what refuses the second press. */}
                <button
                    type="submit"
                    aria-disabled={busy}
                    className="flex min-h-11 shrink-0 items-center justify-center rounded-full bg-accent px-4.5 text-md font-semibold whitespace-nowrap text-on-accent transition hover:bg-accent-strong aria-disabled:opacity-60 workspace:h-7.5 workspace:min-h-0 workspace:rounded-sm workspace:px-3.25 workspace:text-base panes:h-9 panes:px-4"
                >
                    {translate(twoPanes ? 'threadAgent.draftReply' : 'threadAgent.draftReplyShort')}
                </button>
            </div>

            {desktop ? (
                <div className="flex flex-wrap items-center gap-2 px-0.5">
                    <span className="rounded-full bg-accent-soft px-2.75 py-1.25 text-sm text-accent-deep">
                        {translate('threadAgent.scope')}
                    </span>

                    {/* The passage is not chosen here — it is whatever the reader selected in the message above — so the
                        chip is a statement rather than a control, and it reads as taken in once there is one. */}
                    <span
                        title={passageText}
                        className={`rounded-full px-2.75 py-1.25 text-sm ${
                            passageChosen ? 'bg-accent-soft text-accent-deep' : 'border border-line bg-rail'
                        }`}
                    >
                        {translate('threadAgent.passage')}
                    </span>

                    <span className="ms-auto text-sm text-muted">{translate('threadAgent.staysLocal')}</span>
                </div>
            ) : null}
        </form>
    );
}
