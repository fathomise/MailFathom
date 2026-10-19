// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useContext, useRef, useState } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { Departing } from '../shell/Departing';
import { DepartureContext } from '../shell/departure';
import { useScreenLayer } from '../shell/screenLayers';
import { useDesktopComposition, useTwoPanes } from '../shell/useWideWorkspace';
import { fragmentBeingRead } from '../workspace/askScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { useReplyDrafting } from './replyDrafting';

// The reply drafter at the foot of a correspondence, as the design draws it there and nowhere else: closed, a floating
// *Draft a reply* button at the thread's bottom corner, and open, a card hanging over the bottom of the thread with a
// field, a press that drafts a reply, and — where the composition has room — a row saying what the draft is about and
// where it stays. It is not Discover's field and shares nothing with it.
//
// **It stands in the thread's own scroller, stuck to its foot**, so the room it takes is the thread's bottom clearance
// rather than a padding measured beside it: the last line of the thread scrolls clear of the button, and clear of the
// card while it is open, by exactly what each of them is.
//
// **What is typed here is about this correspondence and nothing else.** It is held by this component rather than by the
// workspace, and the frame mounts a fresh one for each message that is opened: a sentence written under one message is
// not a sentence about the next, and opening another message closes the drafter with it.
//
// **The press drafts in place and closes the card.** A reply is asked for here and drawn inside the thread as a card the
// reader checks before anything else happens to it, so nothing here navigates and nothing here sends.

export function ThreadAgentField() {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    const drafting = useReplyDrafting();
    const twoPanes = useTwoPanes();
    const desktop = useDesktopComposition();
    const [open, setOpen] = useState(false);
    const [typed, setTyped] = useState('');

    // Focus goes back to the button whenever the card folds into it, which is the button mounting again — so it is the
    // button's own ref that honours the debt rather than an effect watching the state that dropped it.
    const buttonFocusOwed = useRef(false);

    const passage = fragmentBeingRead(workspace);
    const answering = workspace.selection;
    const busy = drafting?.draft?.body === null && drafting.draft.answering === answering;

    function close(clearingTheScreen = false): void {
        buttonFocusOwed.current = !clearingTheScreen;
        setOpen(false);
    }

    // A surface over the screen like any other, so the back gesture folds it before it leaves the thread.
    useScreenLayer(open, close);

    function draftAReply(): void {
        if (drafting === null || answering === null || busy) {
            return;
        }

        drafting.draftReply(answering, typed, passage?.text ?? null);
        close();
    }

    return (
        <div className="pointer-events-none sticky bottom-0 z-10 mt-auto flex shrink-0 flex-col items-end">
            {/* Said as well as drawn: a passage somebody selected is part of what the reply is written about, and a
                reader who cannot see the chip — or a composition that draws none — would otherwise not be told. */}
            <p className="sr-only" role="status">
                {passage === null ? '' : translate('scope.fragment', { fragment: passage.text })}
            </p>

            {open ? null : (
                <button
                    ref={(element) => {
                        drafting?.holdField(element);

                        if (element !== null && buttonFocusOwed.current) {
                            buttonFocusOwed.current = false;
                            element.focus();
                        }
                    }}
                    type="button"
                    title={translate('threadAgent.openTitle')}
                    className="pointer-events-auto mt-3.5 me-4 mb-4 flex size-14 animate-pop-in items-center justify-center gap-2 rounded-xl bg-accent font-semibold text-on-accent shadow-overlay transition hover:bg-accent-strong workspace:h-11.5 workspace:w-auto workspace:ps-3.75 workspace:pe-4.5 workspace:text-md panes:mt-5 panes:me-5.5 panes:mb-5"
                    onClick={() => {
                        setOpen(true);
                    }}
                >
                    <Icon name="auto_awesome" className="size-icon-lg shrink-0" />

                    {/* The glyph alone on the phone, where the button is a square; the name is still the button's. */}
                    <span className="max-workspace:sr-only">{translate('threadAgent.draftReply')}</span>
                </button>
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
                    />
                )}
            </Departing>
        </div>
    );
}

// Declared once rather than inline, so the field is focused as the card arrives and never again on a later render.
function focusOnArrival(field: HTMLInputElement | null): void {
    field?.focus();
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
                    ref={focusOnArrival}
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
