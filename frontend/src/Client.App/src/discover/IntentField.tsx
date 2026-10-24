// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef } from 'react';
import { chip } from '../controls/chrome';
import { useTip } from '../controls/useTip';
import { SecondaryButton } from '../controls/SecondaryButton';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { withAsked, type AskedQuestion } from '../workspace/askedQuestions';
import { useWorkspace } from '../workspace/useWorkspace';

// Discover's question, drawn as the design draws it under that screen's head: a field with the product's mark
// on it. Submitting records the question in the workspace, which is what the screen starts a run from.
//
// One field takes every kind of ask — a question, an instruction to search, a request to compare — because a mode
// selector is an admission that the field does not understand what was typed. It offers no scope either: Discover asks
// about all of the reader's mail, so nothing the Mail space has open, picked out, or highlighted is read here, named
// here, or quoted here.
//
// **It is Discover's alone.** The field under a correspondence is a different field with state of its own —
// `threadAgent/ThreadAgentField.tsx` — so what somebody typed here never turns up under a message, and what they typed
// there never follows them back.

export function IntentField({
    recall,
}: {
    /**
     * Whether what was asked before is offered back under the field.
     *
     * Only while the field is idle, which is how the design draws it: once a run is answering, the row of earlier
     * questions leaves, because it stands where the answer's own context would be read.
     */
    readonly recall: boolean;
}) {
    const { translate } = useLocalization();
    const wideWorkspace = useWideWorkspace();
    const { workspace, revise } = useWorkspace();
    const question = useRef<HTMLInputElement>(null);

    // The front door is reachable without hunting for it: a reader somewhere in the answer would otherwise tab back
    // out of it to reach the field. An imperative browser API being subscribed to is what an effect is for, and the
    // shortcut is announced on the field itself rather than written anywhere on the screen.
    useEffect(() => {
        function pressed(event: KeyboardEvent): void {
            if (event.key.toLowerCase() === askShortcutKey && (event.ctrlKey || event.metaKey) && !event.altKey) {
                event.preventDefault();
                question.current?.focus();
                question.current?.select();
            }
        }

        document.addEventListener('keydown', pressed);

        return () => {
            document.removeEventListener('keydown', pressed);
        };
    }, []);

    function ask(asked: string): void {
        const words = asked.trim();

        if (words.length > 0) {
            revise({ question: asked, askedBefore: withAsked(workspace.askedBefore, words) });
        }
    }

    return (
        <form
            // The one landmark the platform has no element for, and the one this frame genuinely is: a region a reader
            // moves to in order to ask something, rather than a form that submits a record.
            role="search"
            aria-label={translate('intent.label')}
            className="flex shrink-0 flex-col gap-2.25 border-t border-line bg-panel px-4 py-3.5 workspace:px-5.5"
            onSubmit={(event) => {
                event.preventDefault();
                ask(workspace.question);
            }}
        >
            {/* The focus treatment stands on the box rather than on the field inside it, which is the sign-in screen's
                own shape and for a reason that is visible here rather than argued: the stylesheet's ring is drawn
                around whatever took focus, so a transparent field inside a bordered box draws a second rectangle
                *inside* the first and the bar reads as two nested borders instead of one field that lit up. The field
                therefore gives its own outline up and the box widens instead — which is also what somebody putting a
                cursor in it is looking for. */}
            <div className="flex items-center gap-3 rounded-lg border-2 border-accent px-3.25 py-2 transition focus-within:ring-3 focus-within:ring-accent-soft">
                <span
                    aria-hidden="true"
                    className="shrink-0 rounded-sm bg-accent px-1.75 py-0.75 text-xs font-semibold text-on-accent"
                >
                    {translate('ai.badge')}
                </span>

                <input
                    ref={question}
                    type="search"
                    aria-label={translate('intent.label')}
                    aria-keyshortcuts={askShortcut}
                    placeholder={translate(wideWorkspace ? 'intent.placeholder' : 'intent.placeholderShort')}
                    value={workspace.question}
                    onChange={(event) => {
                        revise({ question: event.target.value });
                    }}
                    className="min-w-0 flex-1 bg-transparent text-lg text-text outline-none placeholder:text-faint"
                />

                <button
                    type="submit"
                    className="shrink-0 rounded-md bg-accent px-3 py-1.75 text-base font-semibold text-on-accent shadow-raised transition hover:bg-accent-strong"
                >
                    {translate('intent.ask')}
                </button>
            </div>

            {!recall || workspace.askedBefore.length === 0 ? null : (
                <AskedBefore
                    asked={workspace.askedBefore}
                    onAskAgain={ask}
                    // Forgetting the list takes the list off the screen, and the control that was pressed with it, so
                    // focus is placed rather than left to fall to the document — on the question, which is what the
                    // field is for.
                    onForget={() => {
                        revise({ askedBefore: [] });
                        question.current?.focus();
                    }}
                />
            )}
        </form>
    );
}

// The key the shortcut is pressed with, beside the modifier the platform reads it under. Stated once because the
// handler tests it and `aria-keyshortcuts` announces it, and a screen reader promising a shortcut nothing listens for
// is worse than promising none — which is why the announcement below is built from the key rather than spelled beside
// it, where the two could drift apart without either half looking wrong.
const askShortcutKey = 'k';
const askShortcut = `Control+${askShortcutKey.toUpperCase()} Meta+${askShortcutKey.toUpperCase()}`;

// What was asked before, offered back so that asking again is one press rather than retyping the sentence. Forgetting
// them is offered beside them: what somebody asked their own mail is theirs, and a list of it they cannot clear is one
// they did not choose to keep.
function AskedBefore({
    asked,
    onAskAgain,
    onForget,
}: {
    readonly asked: readonly AskedQuestion[];
    readonly onAskAgain: (question: string) => void;
    readonly onForget: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex flex-col gap-1">
            <p className="text-sm text-muted">{translate('intent.askedBefore')}</p>

            <ul aria-label={translate('intent.askedBefore')} className="flex flex-wrap items-center gap-2">
                {asked.map((before) => (
                    <li key={before.question} className="min-w-0 max-w-full">
                        <AskedAgain
                            question={before.question}
                            onAsk={() => {
                                onAskAgain(before.question);
                            }}
                        />
                    </li>
                ))}

                <li>
                    <SecondaryButton label={translate('intent.forgetAsked')} onActivate={onForget} />
                </li>
            </ul>
        </div>
    );
}

/** A question asked before, cut to one line; the whole of it is said to a mouse resting on it and a finger holding it. */
function AskedAgain({ question, onAsk }: { readonly question: string; readonly onAsk: () => void }) {
    const { anchor, tip } = useTip({ says: question, onPress: onAsk });

    return (
        <>
            <button
                {...anchor}
                type="button"
                className={`flex min-w-0 max-w-full items-center gap-1.5 px-2.75 py-1.25 text-sm ${chip}`}
            >
                <span className="truncate">{question}</span>
            </button>
            {tip}
        </>
    );
}
