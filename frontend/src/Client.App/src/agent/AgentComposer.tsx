// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type RefObject } from 'react';
import type { AgentAnswerMode } from '@mailfathom/client-backend';
import { ChoiceSegment } from '../controls/ChoiceSegment';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import type { IconName } from '../controls/icons';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { acceptedFiles, type QuestionFiles } from './questionFiles';
import { WaitingFiles } from './WaitingFiles';

// What a new conversation offers to start with. The design's prototype opens a seeded conversation from each;
// a client has none to open, so each asks its question exactly as typing it would.
const starters: readonly MessageKey[] = ['agent.starter.day', 'agent.starter.slipped'];

// The three modes a question may be asked in, in the order the control draws them, each with what it says it does.
const modes: readonly {
    readonly mode: AgentAnswerMode;
    readonly icon: IconName;
    readonly label: MessageKey;
    readonly says: MessageKey;
}[] = [
    { mode: 'auto', icon: 'auto_mode', label: 'agent.mode.auto', says: 'agent.modeSays.auto' },
    { mode: 'fast', icon: 'bolt', label: 'agent.mode.fast', says: 'agent.modeSays.fast' },
    { mode: 'deep', icon: 'manage_search', label: 'agent.mode.deep', says: 'agent.modeSays.deep' },
];

const heldUploading: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.sendHeld.uploading.other',
    one: 'agent.sendHeld.uploading.one',
    two: 'agent.sendHeld.uploading.other',
    few: 'agent.sendHeld.uploading.few',
    many: 'agent.sendHeld.uploading.many',
    other: 'agent.sendHeld.uploading.other',
};

const heldFailed: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.sendHeld.failed.other',
    one: 'agent.sendHeld.failed.one',
    two: 'agent.sendHeld.failed.other',
    few: 'agent.sendHeld.failed.few',
    many: 'agent.sendHeld.failed.many',
    other: 'agent.sendHeld.failed.other',
};

// What a picture pasted with no name of its own is called: the hour and minute it was pasted in the reader's own zone,
// joined by a point rather than the locale's own separator, since a colon is no character a file name may carry
// everywhere.
function pastedAt(locale: string, timeZone: string | null): string {
    const parts = new Intl.DateTimeFormat(locale, {
        hour: '2-digit',
        minute: '2-digit',
        hourCycle: 'h23',
        ...(timeZone === null ? {} : { timeZone }),
    }).formatToParts(new Date());

    return ['hour', 'minute'].map((unit) => parts.find((part) => part.type === unit)?.value ?? '').join('.');
}

/**
 * Where the reader tells the agent what to do: the field, Send, and Cancel beside it, which is drawn at all times and
 * can be pressed only while an answer is being composed.
 *
 * Sending while an answer is composed steers it rather than starting another — the instruction joins the run in flight
 * and it takes it from its next turn — so there is one field and one Send for both.
 *
 * @param onSend Sends what was typed, answering the conversation it went into and `null` where it did not go.
 * @param notSent Why the last send did not go, already in the reader's language, and `null` where it went.
 * @param conversation The conversation the field writes into, and `null` for a new one. What was typed belongs to it,
 * so moving to another puts the draft down — except where a new conversation is given its identity by the question
 * being sent from this field, which is the same conversation acquiring a name.
 * @param focusAsked Changed by the screen when what held focus went away, which puts focus in the field.
 * @param context What the next question is asked about, already worded for the chip, and `null` where it is about
 * nothing narrower — which draws no chip rather than an empty one.
 * @param onClearContext Drops that context, so the next question is asked about everything again.
 * @param mode The mode the next question is asked in, which the screen holds because asking again in `deep` sets it.
 * @param files The files the next question will carry, which the screen holds because a drop onto the conversation
 * hands them over too.
 */
export function AgentComposer({
    running,
    offersStarters,
    notSent,
    conversation,
    focusAsked,
    context,
    onSend,
    onCancel,
    onClearContext,
    mode,
    onMode,
    files,
    dock,
}: {
    readonly running: boolean;
    readonly offersStarters: boolean;
    readonly notSent: string | null;
    readonly conversation: string | null;
    readonly focusAsked: number;
    readonly context: string | null;
    readonly onSend: (text: string) => Promise<string | null>;
    readonly onCancel: () => void;
    readonly onClearContext: () => void;
    readonly mode: AgentAnswerMode;
    readonly onMode: (mode: AgentAnswerMode) => void;
    readonly files: QuestionFiles;

    /** Takes the field's column, which a draft's rewrite bar docks across under a finger. */
    readonly dock?: RefObject<HTMLDivElement | null>;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const wide = useWideWorkspace();
    const field = useId();
    const modeGroup = useId();
    const says = modes.find((offered) => offered.mode === mode)?.says ?? 'agent.modeSays.auto';
    const [text, setText] = useState('');
    const [sending, setSending] = useState(false);
    const [writtenFor, setWrittenFor] = useState(conversation);
    const [sentInto, setSentInto] = useState<string | null>(null);
    const input = useRef<HTMLInputElement>(null);
    const chooser = useRef<HTMLInputElement>(null);
    const writeRefused = useWriteRefusal();

    // A question leaves with every file it was handed or with none, so one still on its way, or one that did not
    // arrive, holds Send and says which.
    const uploading = files.waiting.filter((file) => file.state === 'uploading').length;
    const notUploaded = files.waiting.filter((file) => file.state === 'failed').length;
    const plural = new Intl.PluralRules(locale);
    const heldByFiles =
        uploading > 0
            ? translate(heldUploading[plural.select(uploading)], {
                  count: new Intl.NumberFormat(locale).format(uploading),
              })
            : notUploaded > 0
              ? translate(heldFailed[plural.select(notUploaded)], {
                    count: new Intl.NumberFormat(locale).format(notUploaded),
                })
              : null;

    // Sending, steering, and cancelling each ask the server to do something, so while it is out of reach each says so
    // before it is pressed rather than failing after. The field stays open: what is typed waits for the connection.
    const unreachable = writeRefused(translate('agent.send'));
    const sendRefused = unreachable ?? heldByFiles;
    const cancelRefused = running ? writeRefused(translate('agent.cancel')) : null;
    const clearName = useTip({
        says: translate('agent.clearContext'),
        onPress: () => {
            onClearContext();
            input.current?.focus();
        },
    });
    // The send button's press is the form's own; the hold that reads its name hands nothing to do. Refused, it does
    // nothing at all, so a tap is what says why.
    const sendName = useTip({
        says: sendRefused ?? translate('agent.send'),
        describes: sendRefused !== null,
        onPress: sendRefused === null ? () => undefined : undefined,
    });
    // Idle, the cancel control does nothing, so a tap is what says why — as it does for every control that cannot act.
    const cancels = running && cancelRefused === null;
    const cancelName = useTip({
        says: cancelRefused ?? translate(running ? 'agent.cancelRunning' : 'agent.cancelIdle'),
        onPress: cancels ? onCancel : undefined,
    });

    // Adjusted while rendering rather than in an effect, so the old draft is never drawn under the new conversation.
    if (writtenFor !== conversation) {
        setWrittenFor(conversation);

        if (!(writtenFor === null && conversation === sentInto)) {
            setText('');
        }
    }

    useEffect(() => {
        if (focusAsked > 0) {
            input.current?.focus();
        }
    }, [focusAsked]);

    async function send(written: string): Promise<void> {
        const question = written.trim();

        if (question.length === 0 || sending || sendRefused !== null) {
            return;
        }

        setSending(true);
        const sent = await onSend(question);
        setSending(false);

        // Against the field as it stands when the send answers: anything typed while it was going is kept.
        if (sent !== null) {
            setSentInto(sent);
            setText((now) => (now === written ? '' : now));
        }
    }

    return (
        // The band runs the whole width of the space, and what stands in it is the conversation's own column: the same
        // measure, centring and inset as `ConversationThread.tsx`, so the field lines up under the turns it answers.
        <div className="shrink-0 border-t border-line bg-panel py-3 workspace:pt-3.5 workspace:pb-4">
            <div
                ref={dock}
                className="mx-auto flex w-full max-w-agent-thread flex-col gap-2.25 px-3.5 workspace:gap-2.5 workspace:px-6.5"
            >
                {context === null ? null : (
                    <p className="flex max-w-full items-center gap-2.25 self-start rounded-lg border border-accent-line bg-accent-soft py-2 ps-3 pe-2.5 text-accent-deep">
                        <Icon name="attach_file" className="size-icon-md shrink-0" />
                        <span className="min-w-0 flex-1 truncate text-base">{context}</span>
                        <button
                            {...clearName.anchor}
                            type="button"
                            aria-label={translate('agent.clearContext')}
                            className="flex shrink-0 items-center justify-center rounded-xs transition hover:text-text"
                        >
                            <Icon name="close" className="size-icon-md" />
                        </button>
                        {clearName.tip}
                    </p>
                )}

                {offersStarters ? (
                    <ul aria-label={translate('agent.starters')} className="flex flex-wrap gap-2">
                        {starters.map((starter) => (
                            <li key={starter}>
                                <Starter
                                    question={translate(starter)}
                                    refused={sendRefused}
                                    onAsk={() => {
                                        void send(translate(starter));
                                    }}
                                />
                            </li>
                        ))}
                    </ul>
                ) : null}

                <WaitingFiles
                    files={files}
                    onLeft={() => {
                        input.current?.focus();
                    }}
                />

                <form
                    className="flex w-full items-center gap-1.5 rounded-lg border-2 border-accent bg-panel p-1.5 transition focus-within:ring-3 focus-within:ring-accent-soft workspace:gap-2.5 desktop:py-2.5 desktop:ps-2 desktop:pe-3.25"
                    onSubmit={(event) => {
                        event.preventDefault();
                        void send(text);
                    }}
                >
                    <input
                        ref={chooser}
                        type="file"
                        multiple
                        accept={acceptedFiles}
                        tabIndex={-1}
                        aria-hidden="true"
                        className="hidden"
                        onChange={(event) => {
                            files.add([...(event.target.files ?? [])], null);
                            event.target.value = '';
                        }}
                    />
                    <SymbolControl
                        label={translate('agent.attachFiles')}
                        className="flex size-8.5 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                        onPress={() => {
                            chooser.current?.click();
                        }}
                    >
                        <Icon name="attach_file" className="size-icon-lg" />
                    </SymbolControl>

                    <label htmlFor={field} className="sr-only">
                        {translate('agent.composerLabel')}
                    </label>

                    <input
                        ref={input}
                        id={field}
                        value={text}
                        autoComplete="off"
                        placeholder={translate(wide ? 'agent.composerPlaceholder' : 'agent.composerPlaceholderShort')}
                        className="min-w-0 flex-1 bg-transparent text-lg text-text outline-none placeholder:text-faint"
                        onChange={(event) => {
                            setText(event.target.value);
                        }}
                        onPaste={(event) => {
                            const pasted = [...event.clipboardData.files];

                            // A file pasted into the field is attached rather than typed, and words pasted are typed —
                            // including words that came with a picture of them, as cells copied out of a spreadsheet do.
                            if (pasted.length > 0 && event.clipboardData.getData('text/plain') === '') {
                                event.preventDefault();
                                files.add(
                                    pasted,
                                    translate('agent.files.pastedName', { time: pastedAt(locale, timeZone) }),
                                );
                            }
                        }}
                    />

                    {/* The phone draws both buttons as their symbols, so the field keeps the width its prompt needs; and no
                    button names a key where a finger drives the client, since there is no keyboard to press it on. */}
                    <button
                        {...(wide && sendRefused === null ? {} : sendName.anchor)}
                        type={sendRefused === null ? 'submit' : 'button'}
                        disabled={sending}
                        aria-disabled={sendRefused === null ? undefined : true}
                        aria-label={wide ? undefined : translate('agent.send')}
                        className={`flex size-11 shrink-0 items-center justify-center rounded-md bg-accent text-base whitespace-nowrap text-on-accent transition hover:opacity-90 disabled:opacity-60 aria-disabled:cursor-not-allowed workspace:size-auto workspace:px-4.5 workspace:py-2 desktop:px-4 ${
                            // The design draws Send held by files fainter than Send with no server behind it.
                            unreachable === null ? 'aria-disabled:opacity-45' : 'aria-disabled:opacity-60'
                        }`}
                    >
                        {wide ? (
                            <>
                                {translate('agent.send')}
                                <span aria-hidden="true" className="ms-1 pointer-coarse:hidden">
                                    {translate('agent.sendKey')}
                                </span>
                            </>
                        ) : (
                            <Icon name="send" className="size-icon-xl" />
                        )}
                    </button>
                    {wide && sendRefused === null ? null : sendName.tip}

                    <button
                        type="button"
                        aria-disabled={!cancels}
                        {...(wide && cancels ? {} : cancelName.anchor)}
                        aria-label={translate(running ? 'agent.cancelRunning' : 'agent.cancelIdle')}
                        tabIndex={running ? 0 : -1}
                        className={`flex size-11 shrink-0 items-center justify-center rounded-md border text-base whitespace-nowrap transition workspace:size-auto workspace:px-4 workspace:py-2 desktop:px-3.5 ${
                            running
                                ? 'border-error bg-error font-semibold text-on-accent hover:opacity-90 aria-disabled:cursor-not-allowed aria-disabled:opacity-60'
                                : 'cursor-default border-line text-faint'
                        }`}
                        onClick={(event) => {
                            if (wide && cancels) {
                                onCancel();
                            } else {
                                cancelName.anchor.onClick(event);
                            }
                        }}
                    >
                        {wide ? translate('agent.cancel') : <Icon name="cancel" className="size-icon-xl" />}
                    </button>
                    {wide && cancels ? null : cancelName.tip}
                </form>

                <div className="flex flex-wrap items-center gap-x-3.5 gap-y-2 max-workspace:gap-x-3">
                    <fieldset className="flex items-center gap-0.5 rounded-field bg-rail p-0.75 pointer-coarse:p-1 max-workspace:basis-full">
                        <legend className="sr-only">{translate('agent.modes')}</legend>
                        {modes.map((offered) => (
                            <ChoiceSegment
                                key={offered.mode}
                                shape="effort"
                                name={modeGroup}
                                value={offered.mode}
                                chosen={offered.mode === mode}
                                onChoose={() => {
                                    onMode(offered.mode);
                                }}
                            >
                                <Icon
                                    name={offered.icon}
                                    className={`size-icon-sm ${offered.mode === mode ? 'text-accent-deep' : ''}`}
                                />
                                <span aria-hidden="true">{translate(offered.label)}</span>
                                <span className="sr-only">
                                    {translate('agent.modeName', {
                                        mode: translate(offered.label),
                                        says: translate(offered.says),
                                    })}
                                </span>
                            </ChoiceSegment>
                        ))}
                    </fieldset>
                    {/* Each segment's name already says what its mode does, so the sentence is said once, to the eye. */}
                    <p aria-hidden="true" className="min-w-0 text-sm text-muted max-workspace:basis-full">
                        {translate(says)}
                    </p>
                </div>

                {notSent === null ? null : (
                    <p role="alert" className="flex items-start gap-1.75 text-sm text-warning-text text-pretty">
                        <Icon name="error" className="mt-0.25 size-icon-sm shrink-0" />
                        {notSent}
                    </p>
                )}

                <p className="text-xs text-faint">{translate('agent.footer')}</p>
            </div>
        </div>
    );
}

/**
 * One starter question, which sends itself when pressed — and while the server is out of reach says why it cannot,
 * which is a sentence a tap brings up rather than a press that silently does nothing.
 */
function Starter({
    question,
    refused,
    onAsk,
}: {
    readonly question: string;
    readonly refused: string | null;
    readonly onAsk: () => void;
}) {
    const { translate } = useLocalization();
    const said = useTip({ says: refused ?? question, describes: true });

    return (
        <>
            <button
                {...(refused === null ? { onClick: onAsk } : said.anchor)}
                type="button"
                aria-disabled={refused === null ? undefined : true}
                aria-label={translate('agent.askStarter', { question })}
                className="rounded-xl border border-line-strong bg-panel px-3.25 py-1.75 text-base text-text-soft transition hover:border-accent hover:text-accent-deep aria-disabled:cursor-not-allowed aria-disabled:opacity-60"
            >
                {question}
            </button>
            {refused === null ? null : said.tip}
        </>
    );
}
