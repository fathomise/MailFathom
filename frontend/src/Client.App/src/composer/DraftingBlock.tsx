// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, useState, type KeyboardEvent } from 'react';
import { longestDraftInstruction } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';

// Asking the deployment to write the message, as the design draws it over the body: a field with the product's
// mark on it, the four acts beside it, the three tones after them, and a line saying what the draft is written against
// and where it stays. It is the composer's own block rather than a screen somebody is sent to, which is the whole
// point of it — a draft is a proposal inside the message being written, so nothing here navigates and nothing here
// sends.
//
// **What is typed and what is pressed are two halves of one request.** The field says what the message should be
// about and an act says what shape it should take, so pressing one sends both: the act's own sentence, then whatever
// was typed. Nothing is sent on its own, which is why the field has no submit of its own — a prompt with no act named
// is a request the deployment would have to guess the shape of.
//
// **The acts and the tone travel in English however the screen is read**, and that is deliberate rather than an
// oversight of the two languages: what goes out is an instruction to a writer rather than a sentence anybody reads, and
// the deployment infers the language of the message from the correspondence it answers — so an instruction written in
// the reader's language would be a second, silent answer to a question the correspondence has already settled. What is
// translated is the label on the control, which is what somebody actually reads.

/** One act the block offers, as the design draws it and as the deployment is asked for it. */
interface DraftingAct {
    readonly key: string;
    readonly named: MessageKey;

    /** What the deployment is told the message should be, before whatever its author typed. */
    readonly asks: string;
}

// The design's own four, in the design's own order. Each is a re-drafting rather than an edit of what is on the
// screen — which is what the design's prototype does with all four as well, and what the drafting route offers: it
// writes a message out of the correspondence and the instruction, and reads nothing back out of the composer.
const writeADraft: DraftingAct = { key: 'write', named: 'compose.aiWrite', asks: 'Write this message.' };

const draftingActs: readonly DraftingAct[] = [
    writeADraft,
    { key: 'shorten', named: 'compose.aiShorten', asks: 'Write this message short: the fewest sentences that say it.' },
    { key: 'expand', named: 'compose.aiExpand', asks: 'Write this message in full, spelling out what it rests on.' },
    { key: 'language', named: 'compose.aiFixLanguage', asks: 'Write this message again in careful, correct wording.' },
];

/** The three the design offers, in its own order, with the middle one standing as what nobody chose. */
const tones = [
    { key: 'formal', named: 'compose.aiToneFormal', asks: 'Keep the tone formal.' },
    { key: 'neutral', named: 'compose.aiToneNeutral', asks: null },
    { key: 'direct', named: 'compose.aiToneDirect', asks: 'Keep the tone direct and brief.' },
] as const satisfies readonly { key: string; named: MessageKey; asks: string | null }[];

// Which way each arrow moves the tone in force. A radio group is one tab stop and the arrows are what choose inside
// it, so the role below is a promise about the keyboard rather than a label for the shading — and a promise a reader
// is told about and then finds unkept is worse than never making it.
const toneSteps: Readonly<Record<string, number>> = {
    ArrowLeft: -1,
    ArrowUp: -1,
    ArrowRight: 1,
    ArrowDown: 1,
};

type Tone = (typeof tones)[number]['key'];

// Where the keyboard lands once the control holding it has gone: a removed chip hands it to its neighbour, or to the
// field once none is left, and *Restore* — which goes with the line it stands on — to the first chip it put back.
type FocusLanding =
    { readonly on: 'chip'; readonly storedEmailId: string } | { readonly on: 'firstChip' } | { readonly on: 'field' };

/** One message the draft is written from, as its chip names it. */
export interface DraftingContextMessage {
    readonly storedEmailId: string;

    /** What the chip reads, which is the message's subject once it has been read. */
    readonly subject: string;
}

/** Where the context started, named for the line that says it has moved since: its first message, and how many more. */
export interface DraftingContextStart {
    readonly subject: string;
    readonly more: number;
}

export function DraftingBlock({
    context,
    noContextSaid,
    changedFrom,
    asked = '',
    busy,
    onDraft,
    onRemove,
    onRestore,
}: {
    /** The messages the draft is written from, one chip apiece, in the order they were taken. */
    readonly context: readonly DraftingContextMessage[];

    /** What the row says while the context holds no message, which depends on whether a list stands beside it. */
    readonly noContextSaid: MessageKey;

    /** Where the context started, while it differs from that; `null` while it is its starting point or has none. */
    readonly changedFrom: DraftingContextStart | null;

    /** What the field opens holding: what the reply was asked to say before the composer opened, where it was. */
    readonly asked?: string;

    /** Whether a draft is being written now, which is what keeps one press from asking twice. */
    readonly busy: boolean;

    /** Asks the deployment for a draft, with the act's own sentence and whatever was typed beside it. */
    readonly onDraft: (instruction: string) => void;

    /** Takes one message out of the context. */
    readonly onRemove: (storedEmailId: string) => void;

    /** Puts the context back where it started. */
    readonly onRestore: () => void;
}) {
    const { locale, translate } = useLocalization();
    const wideWorkspace = useWideWorkspace();
    const [written, setWritten] = useState(asked);
    const [tone, setTone] = useState<Tone>('neutral');
    const focusLanding = useRef<FocusLanding | null>(null);

    // A ref runs on every render, so the landing is what stops a later render taking the keyboard back from wherever the
    // reader has moved it since.
    function landHere(element: HTMLElement | null, here: (landing: FocusLanding) => boolean): void {
        if (element !== null && focusLanding.current !== null && here(focusLanding.current)) {
            focusLanding.current = null;
            element.focus();
        }
    }

    function askFor(act: DraftingAct): void {
        const chosen = tones.find((one) => one.key === tone)?.asks ?? null;

        // Cut here as well as at the wire, because what the deployment refuses is the whole instruction rather than
        // the part somebody typed — and a request refused for length would be refused with a sentence nobody asked to
        // read. The act and the tone come first because they are what the request is; the rest is what was typed.
        const said = [act.asks, ...(chosen === null ? [] : [chosen]), written.trim()]
            .filter((part) => part !== '')
            .join(' ');

        onDraft(said.slice(0, longestDraftInstruction));
    }

    // Moving the tone with the arrows, and taking the focus with it, which is the other half of what the group's role
    // promises. The focused control is found among the group's own children rather than through a ref apiece: the
    // three are rendered from one list, so their order on the screen is the list's order and nothing else decides it.
    function moveTone(event: KeyboardEvent<HTMLButtonElement>): void {
        const step = toneSteps[event.key];

        if (step === undefined) {
            return;
        }

        event.preventDefault();

        const landing = (tones.findIndex((one) => one.key === tone) + step + tones.length) % tones.length;
        const moved = tones[landing];

        if (moved === undefined) {
            return;
        }

        setTone(moved.key);

        const control = event.currentTarget.parentElement?.children[landing];

        if (control instanceof HTMLElement) {
            control.focus();
        }
    }

    return (
        <div className="flex shrink-0 flex-col gap-2.25 border-b border-accent-line bg-accent-soft px-3.75 py-2.75">
            <div className="flex items-center gap-2.75 rounded-md border-2 border-accent bg-panel px-2.75 py-2 transition focus-within:ring-3 focus-within:ring-accent-soft">
                <span
                    aria-hidden="true"
                    className="shrink-0 rounded-sm bg-accent px-1.75 py-0.75 text-xs font-semibold text-on-accent"
                >
                    {translate('ai.badge')}
                </span>

                <input
                    ref={(element) => {
                        landHere(element, (landing) => landing.on === 'field');
                    }}
                    aria-label={translate('compose.aiPrompt')}
                    placeholder={translate(
                        wideWorkspace ? 'compose.aiPromptPlaceholder' : 'compose.aiPromptPlaceholderShort',
                    )}
                    value={written}
                    maxLength={longestDraftInstruction}
                    className="min-w-0 flex-1 bg-transparent text-base text-text outline-none placeholder:text-faint"
                    onChange={(event) => {
                        setWritten(event.target.value);
                    }}
                />

                {/* The mark is never the only thing saying it: the line under the context row says the same in words. */}
                {changedFrom === null ? null : (
                    <span
                        role="img"
                        aria-label={translate('compose.aiContextChanged')}
                        className="flex size-5.5 shrink-0 animate-pop-in items-center justify-center rounded-full bg-warning-soft text-warning-text ring-1 ring-warning ring-inset"
                    >
                        <Icon name="priority_high" className="size-icon-sm" />
                    </span>
                )}
            </div>

            <div className="flex flex-wrap items-center gap-1.75">
                {draftingActs.map((act) => (
                    <button
                        key={act.key}
                        type="button"
                        disabled={busy}
                        className="touch-extended rounded-full border border-accent-line bg-panel px-3 py-1.5 text-sm whitespace-nowrap text-accent-deep transition hover:bg-accent hover:text-on-accent disabled:opacity-60"
                        onClick={() => {
                            askFor(act);
                        }}
                    >
                        {translate(act.named)}
                    </button>
                ))}

                <span aria-hidden="true" className="mx-0.75 h-5 w-px bg-accent-line" />

                {/* A radio group rather than four more buttons, because one of the three is always in force and a
                    reader who cannot see which is shaded has no other way to be told. */}
                <div role="radiogroup" aria-label={translate('compose.aiTone')} className="flex items-center gap-1.75">
                    {tones.map((one) => (
                        <button
                            key={one.key}
                            type="button"
                            role="radio"
                            aria-checked={tone === one.key}
                            // The group is one tab stop: the tone in force is what a Tab lands on, and the arrows
                            // move within it. Three separate stops would make the keyboard disagree with the role.
                            tabIndex={tone === one.key ? 0 : -1}
                            className={`touch-extended rounded-full px-2.75 py-1.25 text-sm whitespace-nowrap transition ${
                                tone === one.key
                                    ? 'bg-accent text-on-accent'
                                    : 'border border-line text-text-soft hover:bg-hover'
                            }`}
                            onClick={() => {
                                setTone(one.key);
                            }}
                            onKeyDown={moveTone}
                        >
                            {translate(one.named)}
                        </button>
                    ))}
                </div>
            </div>

            <div className="flex flex-wrap items-center gap-2 text-sm text-accent-deep">
                <span className="font-semibold">{translate('compose.aiContext')}</span>

                {context.map((message, at) => (
                    <span
                        key={message.storedEmailId}
                        className="flex max-w-65 min-w-0 items-center gap-1.5 rounded-sm border border-accent-line bg-panel py-0.5 ps-1.5 pe-0.5 max-workspace:max-w-55 pointer-coarse:min-h-9"
                    >
                        <Icon name="mail" className="size-icon-sm shrink-0" />
                        <span className="min-w-0 truncate">{message.subject}</span>

                        <SymbolControl
                            ref={(element) => {
                                landHere(
                                    element,
                                    (landing) =>
                                        (landing.on === 'chip' && landing.storedEmailId === message.storedEmailId) ||
                                        (landing.on === 'firstChip' && at === 0),
                                );
                            }}
                            label={translate('compose.aiContextRemove', { subject: message.subject })}
                            className="flex shrink-0 items-center justify-center rounded-xs text-muted transition hover:bg-hover hover:text-text pointer-coarse:size-8"
                            onPress={() => {
                                const neighbour = context[at + 1] ?? context[at - 1];

                                focusLanding.current =
                                    neighbour === undefined
                                        ? { on: 'field' }
                                        : { on: 'chip', storedEmailId: neighbour.storedEmailId };
                                onRemove(message.storedEmailId);
                            }}
                        >
                            <Icon name="close" className="size-icon-xs" />
                        </SymbolControl>
                    </span>
                ))}

                {context.length === 0 ? <span className="text-muted">{translate(noContextSaid)}</span> : null}

                {changedFrom === null ? <span className="text-muted">{translate('compose.aiStaysLocal')}</span> : null}
            </div>

            {changedFrom === null ? null : (
                <div
                    role="status"
                    className="flex animate-unfold-in flex-wrap items-center gap-2 text-sm text-warning-text"
                >
                    <span className="min-w-0 flex-1 text-pretty">
                        {changedFrom.more === 0
                            ? translate('compose.aiContextStartedFrom', { subject: changedFrom.subject })
                            : translate('compose.aiContextStartedFromSeveral', {
                                  subject: changedFrom.subject,
                                  more: new Intl.NumberFormat(locale).format(changedFrom.more),
                              })}
                    </span>

                    <button
                        type="button"
                        className="flex items-center gap-1.5 rounded-md px-2 py-1 font-semibold transition hover:bg-warning-soft"
                        onClick={() => {
                            focusLanding.current = { on: 'firstChip' };
                            onRestore();
                        }}
                    >
                        <Icon name="undo" className="size-icon-md" />
                        {translate('compose.aiContextRestore')}
                    </button>
                </div>
            )}
        </div>
    );
}

/**
 * What became of the last drafting, drawn between the formatting bar and the words the way the design draws it.
 *
 * Two states and neither is an error: one says a draft is being written, and one says the words on the screen are a
 * draft nobody has read yet. The second is what the send confirmation cautions about, which is why accepting it is a
 * control here rather than something that happens when somebody types.
 */
export function DraftingStanding({
    busy,
    answering,
    drafted,
    onRestore,
    onAccept,
}: {
    readonly busy: boolean;

    /** Whether the message answers one, which is what the band says it is generating: a reply, or a draft. */
    readonly answering: boolean;

    /** Whether the words on the screen are a draft its author has neither accepted nor restored away from. */
    readonly drafted: boolean;

    /** Puts back what was written before the draft replaced it. */
    readonly onRestore: () => void;

    /** Takes the draft as the author's own words, which is what stops the send confirmation cautioning about it. */
    readonly onAccept: () => void;
}) {
    const { translate } = useLocalization();

    if (busy) {
        return (
            <p
                role="status"
                className="flex shrink-0 items-center gap-2.5 border-b border-accent-line bg-accent-soft px-3.75 py-2.5 text-base text-accent-deep"
            >
                <Icon name="auto_awesome" className="size-icon-md shrink-0 animate-working" />
                <span className="font-semibold">
                    {translate(answering ? 'compose.aiGeneratingReply' : 'compose.aiDrafting')}
                </span>
                <span aria-hidden="true" className="-ms-1.5 flex items-center gap-0.75 pt-1">
                    <span className="size-1 animate-rising-dot rounded-full bg-current" />
                    <span className="size-1 animate-rising-dot-second rounded-full bg-current" />
                    <span className="size-1 animate-rising-dot-third rounded-full bg-current" />
                </span>
            </p>
        );
    }

    if (!drafted) {
        return null;
    }

    return (
        <div className="flex shrink-0 flex-wrap items-center gap-3 border-b border-line-soft bg-sunken px-3.75 py-2">
            <p className="text-sm text-text-soft">{translate('compose.aiDraftShown')}</p>

            <button
                type="button"
                className="rounded-md px-1.5 py-1 text-sm text-accent-deep underline transition hover:bg-hover"
                onClick={onRestore}
            >
                {translate('compose.aiRestore')}
            </button>

            <button
                type="button"
                className="rounded-md px-1.5 py-1 text-sm text-muted transition hover:bg-hover hover:text-text"
                onClick={onAccept}
            >
                {translate('compose.aiAccept')}
            </button>
        </div>
    );
}
