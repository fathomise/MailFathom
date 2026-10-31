// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useContext, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { ComposingContext } from '../composer/useComposing';
import { GrowingField } from '../controls/GrowingField';
import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import { useAgentHandOver, type AgentHandOver } from '../routing/agentHandOver';
import { Departing } from '../shell/Departing';
import { DepartureContext } from '../shell/departure';
import { useScreenLayer } from '../shell/screenLayers';
import { useTwoPanes } from '../shell/useWideWorkspace';
import { fragmentBeingRead } from '../workspace/selectedFragment';
import { useWorkspace } from '../workspace/useWorkspace';

// The one AI entry point at the foot of a correspondence, as the design draws it there and nowhere else: a floating
// `auto_awesome` button at the reading column's bottom corner that unfolds two choices above it — *Draft a reply*,
// which opens the reply drafter, and *Ask*, which hands the conversation to the agent. The drafter, once open, is a card
// hanging over the bottom of the thread with a field asking what the reply should say, its press, and — where the
// composition has room — a row saying what the draft is about and where it stays. It is not Discover's field and shares
// nothing with it.
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
// **The press opens the composer and closes the card.** The reply composer opens on the message being read and starts
// the draft there with what was typed and any passage selected above, so the draft is written where it is edited and
// sent rather than in a card in front of it; nothing here sends.

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
    takesTheKeyboard = false,
    onKeyboardTaken,
}: {
    /** The conversation as it is handed to the agent, or `null` where what is open belongs to none. */
    readonly thread: AgentHandOver | null;

    /** Whether this deployment drafts replies, which is what decides whether *Draft a reply* is offered at all. */
    readonly drafts: boolean;

    /**
     * Whether the AI button takes the keyboard as it is drawn, which is where a composer opened from the drafter hands it
     * back: the drafter went with the reading column the composer stood in, and this button is what stands in its place.
     */
    readonly takesTheKeyboard?: boolean;

    /** Called once the button has taken the keyboard, so it is taken once rather than on every later render. */
    readonly onKeyboardTaken?: () => void;
}) {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    // Read where it is offered rather than required: a reading surface drawn with no composer behind it offers *Ask*
    // alone, exactly as a deployment that drafts nothing does.
    const composing = useContext(ComposingContext);
    const handToAgent = useAgentHandOver();
    const twoPanes = useTwoPanes();
    const choices = useId();
    const chooser = useRef<HTMLDivElement | null>(null);
    const [choosing, setChoosing] = useState(false);
    const [open, setOpen] = useState(false);
    const [typed, setTyped] = useState('');

    const draftingOffered = drafts && composing !== null;
    const asking =
        handToAgent === null || thread === null
            ? null
            : () => {
                  handToAgent(thread);
              };

    // Focus goes back to the button whenever the card folds into it, which is the button mounting again — so it is the
    // button's own ref that honours the debt rather than an effect watching the state that dropped it.
    const buttonFocusOwed = useRef(false);
    const keyboardTakenBack = useRef(false);
    const choiceFocusOwed = useRef(false);

    const passage = fragmentBeingRead(workspace);
    const answering = workspace.selection;

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

    // The composer takes the keyboard as it opens, so the card folding owes the button nothing; the composer closing owes
    // it the keyboard back, which `takesTheKeyboard` carries because this field is drawn anew by then.
    function draftAReply(): void {
        if (composing === null || answering === null) {
            return;
        }

        composing.compose({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: answering,
            drafting: { asked: typed, passage: passage?.text ?? null },
        });
        setTyped('');
        close(true);
    }

    const chooserName = useTip({
        says: translate('threadAgent.choose'),
        onPress: () => {
            if (choosing) {
                fold(true);
            } else {
                choiceFocusOwed.current = true;
                setChoosing(true);
            }
        },
    });
    const askHint = useTip({
        says: translate('message.askTitle'),
        describes: true,
        onPress: () => {
            setChoosing(false);
            asking?.();
        },
    });

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
                                <>
                                    <button
                                        {...askHint.anchor}
                                        ref={(element) => {
                                            askHint.anchor.ref(element);
                                            if (!draftingOffered) {
                                                takeChoiceFocus(element);
                                            }
                                        }}
                                        type="button"
                                        className={choiceShape}
                                    >
                                        <Icon name="forum" className="size-icon-lg shrink-0 text-accent-deep" />
                                        {translate('message.ask')}
                                    </button>
                                    {askHint.tip}
                                </>
                            )}
                        </div>
                    ) : null}

                    {/* The glyph alone at every width, its name the button's; pressed, it turns to a close while the
                        choices stand, and the expanded state says the same to somebody who cannot see it turn. */}
                    <button
                        {...chooserName.anchor}
                        ref={(element) => {
                            chooserName.anchor.ref(element);

                            const handedBack = takesTheKeyboard && !keyboardTakenBack.current;

                            if (element !== null && (buttonFocusOwed.current || handedBack)) {
                                buttonFocusOwed.current = false;
                                element.focus();

                                if (handedBack) {
                                    keyboardTakenBack.current = true;
                                    onKeyboardTaken?.();
                                }
                            }
                        }}
                        type="button"
                        aria-label={translate('threadAgent.choose')}
                        aria-expanded={choosing}
                        aria-controls={choosing ? choices : undefined}
                        className="flex size-14 animate-pop-in items-center justify-center rounded-xl bg-accent text-on-accent shadow-overlay transition hover:bg-accent-strong workspace:size-12"
                    >
                        <Icon name={choosing ? 'close' : 'auto_awesome'} className="size-icon-xl shrink-0" />
                    </button>
                    {chooserName.tip}
                </div>
            )}

            <Departing value={open ? true : null}>
                {() => (
                    <DrafterCard
                        typed={typed}
                        passageChosen={passage !== null}
                        passageText={passage?.text}
                        twoPanes={twoPanes}
                        onType={setTyped}
                        onDraft={draftAReply}
                        onClose={() => {
                            close();
                        }}
                    />
                )}
            </Departing>
        </div>
    );
}

// The field takes the keyboard as the card arrives, and only then: a ref declared once is called when the field is
// attached and never again while it stands.
function takeTheKeyboard(field: HTMLTextAreaElement | null): void {
    field?.focus();
}

function DrafterCard({
    typed,
    passageChosen,
    passageText,
    twoPanes,
    onType,
    onDraft,
    onClose,
}: {
    readonly typed: string;
    readonly passageChosen: boolean;
    readonly passageText: string | undefined;
    readonly twoPanes: boolean;
    readonly onType: (typed: string) => void;
    readonly onDraft: () => void;
    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const leaving = useContext(DepartureContext);
    const closerName = useTip({ says: translate('threadAgent.close'), onPress: onClose });
    // Said by a tap as well as a mouse resting on it, since the chip is a statement and does nothing else.
    const passageSaid = useTip({ says: passageText ?? '', describes: passageText !== undefined });

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
                {...closerName.anchor}
                type="button"
                aria-label={translate('threadAgent.close')}
                className="absolute -end-2.75 -top-2.75 z-1 flex size-6.5 items-center justify-center rounded-full border border-line bg-panel text-text-soft shadow-overlay transition hover:bg-hover hover:text-text pointer-coarse:-end-1 pointer-coarse:-top-4.75 pointer-coarse:size-8"
            >
                <Icon name="close" className="size-icon-sm pointer-coarse:size-icon-md" />
            </button>
            {closerName.tip}

            {/* The focus treatment stands on the box rather than on the field inside it: a ring drawn round a
                transparent field inside a bordered box reads as two nested borders rather than one field that lit up. */}
            {/* A 2xl radius rather than a pill below the workspace width, as the design draws it: one line still reads as
                the pill, and a field grown to several lines stays a rounded box rather than a capsule cutting into it. */}
            <div className="flex min-w-0 items-center gap-2.5 rounded-2xl border border-accent-line bg-sunken py-1 ps-3.5 pe-1 transition focus-within:ring-3 focus-within:ring-accent-soft workspace:rounded-lg workspace:ps-3 panes:border-2 panes:border-accent panes:py-1.5 panes:ps-3.5 panes:pe-1.5">
                <Icon name="auto_awesome" className="size-icon-md shrink-0 text-accent-deep" />

                <GrowingField
                    ref={takeTheKeyboard}
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
                        } else if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
                            // A text area takes Enter as a new line, so the press that asked for the draft from the one-line
                            // field is asked for here; Shift+Enter keeps the new line.
                            event.preventDefault();
                            event.currentTarget.form?.requestSubmit();
                        }
                    }}
                    className="min-w-0 flex-1 bg-transparent text-md text-text outline-none placeholder:text-faint panes:text-lg"
                />

                <button
                    type="submit"
                    className="flex min-h-11 shrink-0 items-center justify-center rounded-full bg-accent px-4.5 text-md font-semibold whitespace-nowrap text-on-accent transition hover:bg-accent-strong workspace:h-7.5 workspace:min-h-0 workspace:rounded-sm workspace:px-3.25 workspace:text-base panes:h-9 panes:px-4"
                >
                    {translate(twoPanes ? 'threadAgent.draftReply' : 'threadAgent.draftReplyShort')}
                </button>
            </div>

            {/* The scope a draft is written from, which a pane narrower than 760 pixels has no room to say. */}
            <div className="flex flex-wrap items-center gap-2 px-0.5 @max-pane-compact:hidden">
                <span className="rounded-full bg-accent-soft px-2.75 py-1.25 text-sm text-accent-deep">
                    {translate('threadAgent.scope')}
                </span>

                {/* The passage is not chosen here — it is whatever the reader selected in the message above — so the
                    chip is a statement rather than a control, and it reads as taken in once there is one. */}
                <span
                    {...(passageText === undefined ? {} : passageSaid.anchor)}
                    className={`rounded-full px-2.75 py-1.25 text-sm ${
                        passageChosen ? 'bg-accent-soft text-accent-deep' : 'border border-line bg-rail'
                    }`}
                >
                    {translate('threadAgent.passage')}
                </span>
                {passageText === undefined ? null : passageSaid.tip}

                <span className="ms-auto text-sm text-muted">{translate('threadAgent.staysLocal')}</span>
            </div>
        </form>
    );
}
