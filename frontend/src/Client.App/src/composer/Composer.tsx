// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import {
    draftMailReply,
    longestDraftInstruction,
    longestDraftSelection,
    readMailBody,
    readMailDraft,
    readMailMessage,
    type ClientFailureReason,
    type ClientSession,
    type MailAccount,
    type MailAttachment,
    type MailDraft,
    type MailDraftAnswer,
    type MailSendDueTime,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { disconnected, saidByTheStatus, useConnectionStatus } from '../shell/connectionStatus';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { useToasts, type Toast } from '../toasts/useToasts';
import { showOutboxEntry } from '../outbox/showOutboxEntry';
import { useWorkspace } from '../workspace/useWorkspace';
import {
    anythingWritten,
    answerTo,
    draftContinued,
    draftHeld,
    nothingWrittenYet,
    type ComposerOpening,
    type Composition,
    type ReplyDraftAsked,
} from './composition';
import { DiscardConfirmation } from './DiscardConfirmation';
import { DraftingBlock, DraftingStanding, type DraftingContextStart } from './DraftingBlock';
import { contextPointedAt, sameMessages, type ListPointer } from './draftingContext';
import { draftingRead, failureSaid } from './draftingRead';
import { writtenParagraphs } from './draftWords';
import { forgetComposition, rememberComposition, rememberedComposition } from './keptComposition';
import type { RecipientSuggestion } from './recipientSuggestions';
import { AbsentRecipients } from './AbsentRecipients';
import { RecipientField } from './RecipientField';
import { SendConfirmation } from './SendConfirmation';
import { SendLaterPicker } from './SendLaterPicker';
import {
    instantStillAhead,
    namedZone,
    readyTimeOf,
    readyTimes,
    type ChosenSendTime,
    type SendTime,
    type SendTimeWording,
} from './sendLater';
import { useDraftAtDeployment, type AttachedFile, type DraftStanding } from './useDraftAtDeployment';
import { useSendingOptions } from './useSendingOptions';
import { useSendTimeWording } from './useSendTimeWording';
import { WrittenMessage } from './WrittenMessage';
import type { WrittenNode } from './writtenText';

// Writing a message, as the design composes it: one model in two shapes, decided by the width the client has
// rather than by which head it runs on. Wide, it is the reading column — what is being written stands where what is
// being read stands, with the mailboxes and the list still beside it, so a reply is written against a conversation one
// press away rather than behind a window. Narrow, it is the screen, because a column that has to hold a header, four
// fields, and a footer has nothing left over to show a message underneath.
//
// **The body is rich text, and `WrittenMessage` is the whole of it**: the editable region, the formatting bar the
// design draws over it, and the toggle that stands in for the bar where a finger drives the screen. What reaches the
// deployment is both parts of one message — the markup and the plain-text alternative read out of the same tree. The
// block of AI actions the design draws above the bar is `DraftingBlock`, drawn wherever the deployment writes drafts.
//
// **The design's copy headers carry a Reply-to field this composer does not draw.** A message MailFathom writes names
// no reply address — the draft, the outbox, and the composed message carry To, Cc, and Bcc and nothing else — so a
// field here would be an address somebody typed that no message ever carried.
//
// **An answer's subject is read-only, and that is the platform rather than a choice.** A save either names an account
// and a subject, or names the message it answers and lets the deployment derive both — so an edited subject on a reply
// is a value the surface has nowhere to put, and offering the field would be offering an edit that is discarded.

/**
 * What the composer is doing before there is anything to write in, which is a state of its own rather than a blank.
 *
 * A read that failed is said and the composer stays open: a surface somebody opened is left by the control they opened
 * it with, and closing itself under them would report a message that is gone by taking away the one place that said so.
 */
type Reading = { readonly kind: 'reading' } | { readonly kind: 'unread'; readonly reason: ClientFailureReason };

// What each rule that refuses a send is called, and what would change it. Exhaustive by its own type, so a refusal the
// surface adds fails to compile until somebody has written what a person does about it.
const refusalSaid = {
    sendingNotEnabled: 'compose.refusedSendingNotEnabled',
    recipientRefused: 'compose.refusedRecipient',
    ceilingReached: 'compose.refusedCeiling',
    contentRefused: 'compose.refusedContent',
    notFullyScanned: 'compose.refusedNotScanned',
    attachmentNotRead: 'compose.refusedAttachmentNotRead',
    screeningUnavailable: 'compose.refusedScreeningUnavailable',
    dueTimePassed: 'compose.refusedDueTimePassed',
    refusedForAnotherReason: 'compose.refusedForAnotherReason',
} as const satisfies Readonly<Record<string, MessageKey>>;

// The same rules said in the words of a save. The draft book is screened exactly as the outbox is, so the same codes
// reach a save and a revision — and only the four a screen produces can, a deployment that refuses a recipient or has
// sending switched off refusing nothing that is merely written down. The other four are still named, because the type
// is what makes this exhaustive and a saved draft refused for a reason this client does not know is still a refusal
// rather than an outage.
const saveRefusalSaid = {
    sendingNotEnabled: 'compose.saveRefusedForAnotherReason',
    recipientRefused: 'compose.saveRefusedForAnotherReason',
    ceilingReached: 'compose.saveRefusedForAnotherReason',
    contentRefused: 'compose.saveRefusedContent',
    notFullyScanned: 'compose.saveRefusedNotScanned',
    attachmentNotRead: 'compose.saveRefusedAttachmentNotRead',
    screeningUnavailable: 'compose.saveRefusedScreeningUnavailable',
    dueTimePassed: 'compose.saveRefusedForAnotherReason',
    refusedForAnotherReason: 'compose.saveRefusedForAnotherReason',
} as const satisfies Readonly<Record<string, MessageKey>>;

// What became of a send somebody took back, which is four answers rather than a success and a failure: a message
// already going out cannot be recalled, and saying so is the answer.
const withdrawalSaid = {
    withdrawn: 'compose.withdrawn',
    alreadyBeingSent: 'compose.alreadyBeingSent',
    pastRecall: 'compose.pastRecall',
    noSuchSend: 'compose.noSuchSend',
} as const satisfies Readonly<Record<string, MessageKey>>;

// The same four as the headline a toast carries. A toast is read at a glance and its first line is what somebody
// takes from it, so what became of the message is said there and why is said under it.
const withdrawalTitle = {
    withdrawn: 'compose.withdrawnTitle',
    alreadyBeingSent: 'compose.notWithdrawnTitle',
    pastRecall: 'compose.notWithdrawnTitle',
    noSuchSend: 'compose.notWithdrawnTitle',
} as const satisfies Readonly<Record<string, MessageKey>>;

// Everything a keyboard may land on, as the platform decides it rather than as a list of the composer's own
// controls: a control added later is caught by this without anybody remembering to name it here.
const reachableControls =
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]';

// What the deployment is told a reply asked for from the drafter should be, before whatever was typed there. It travels
// in English however the screen is read, for the reason `DraftingBlock.tsx` gives.
const writeTheReply = 'Write a reply to this message.';

const titles: Readonly<Record<'new' | 'draft' | 'held' | MailDraftAnswer, MessageKey>> = {
    new: 'compose.titleNew',
    draft: 'compose.titleDraft',
    held: 'compose.titleDraft',
    senderOnly: 'compose.titleReply',
    everyone: 'compose.titleReplyAll',
    forward: 'compose.titleForward',
};

export function Composer({
    session,
    transport,
    accounts,
    opening,
    online,
    drafts,
    showsOutbox = false,
    inFront = true,
    organizationName = null,
    onClosed,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The mailboxes a message may go out from, which is what a message of its own is addressed from. */
    readonly accounts: readonly MailAccount[];

    readonly opening: ComposerOpening;
    readonly online: boolean;

    /**
     * Whether this deployment writes a draft at all, which is what decides whether the block is drawn.
     *
     * Read once by the frame above rather than here: a control promising a draft over a deployment that writes none
     * fails a person at the one moment they trusted it, and a block drawn and then withdrawn as an answer arrives
     * would move the fields under whoever was already writing in them.
     */
    readonly drafts: boolean;

    /**
     * Whether the session reads mail, which is what an Outbox is read under: a credential that only sends is offered no
     * way to a send it could not open.
     */
    readonly showsOutbox?: boolean;

    /** Whether the mail space is the one in front, which the composer is written in and stays in while it is aside. */
    readonly inFront?: boolean;

    /** The name the reader's organization is shown by, which the recipients its book keeps are offered under. */
    readonly organizationName?: string | null;

    /**
     * Takes the composer off the screen.
     *
     * @param handFocusBack Whether the keyboard goes back to what opened it, which is false where the close is a send
     *   answering after somebody had moved on.
     */
    readonly onClosed: (handFocusBack: boolean) => void;
}) {
    const { locale, translate } = useLocalization();
    const wide = useWideWorkspace();
    const toasts = useToasts();
    const writeRefused = useWriteRefusal();
    const { disconnectedNow } = useConnectionStatus();
    const draft = useDraftAtDeployment(session, transport);
    const zone = useReadingZone();

    // When the message leaves, which belongs to the send rather than to the draft: it is held here and nowhere else,
    // so a draft saved with a time keeps none and one opened again sends at once until a time is chosen.
    const [sendAt, setSendAt] = useState<ChosenSendTime | null>(null);
    const [picking, setPicking] = useState<{ readonly asking: SendTime; readonly openedAt: number } | null>(null);
    const picker = useRef<HTMLDialogElement>(null);
    const [shownAt] = useState(() => Date.now());
    const wordSendTime = useSendTimeWording(shownAt, zone);
    const leaves = sendAt === null ? null : wordSendTime(sendAt);

    // Who the message is for, joined the way the active language joins a list rather than with a comma written here.
    const addresses = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' });
    const [composition, setComposition] = useState(() => opened(opening, accounts));

    // Whether anybody has written in this message, which is not the same question as whether it has anything in it:
    // an answer opens addressed to the conversation it answers, and asking to confirm giving up a message nobody has
    // touched is the confirmation that teaches a reader to dismiss them. A message this tab was already writing is
    // authored by definition — somebody wrote it, in this tab, before the reload.
    const [authored, setAuthored] = useState(() => composition !== null && anythingWritten(composition));
    const [reading, setReading] = useState<Reading>({ kind: 'reading' });

    const [participants, setParticipants] = useState<readonly RecipientSuggestion[]>([]);

    // Whether the copy headers are drawn, which is a choice only once somebody made one: before that they are drawn
    // exactly where the message already copies somebody in, so an answer to everyone and a draft carried on show the
    // addresses they will go to rather than holding them behind a control nobody pressed. Hiding them keeps what they
    // hold, so pressing the control again shows them where they were — but nothing goes to an address nobody can see,
    // so a save, a send, and the question before a send are all written without them while they are hidden.
    const [copiesChosen, setCopiesChosen] = useState<boolean | null>(null);

    // What the deployment is doing about a draft, and what the words were before one replaced them. The second is
    // both the way back and the fact the send confirmation cautions about: a draft nobody has read is words somebody
    // else wrote, and accepting it is what makes them the author's own.
    const [drafting, setDrafting] = useState(false);
    const [beforeDrafting, setBeforeDrafting] = useState<readonly WrittenNode[] | null>(null);

    // What the AI drafts from, as messages: a reply starts with the one it answers and a new message with none. The
    // first context a composition takes is where it started, and a context that differs from that as a set is what the
    // block warns about. Both are held here and nowhere else, so every way the composer closes empties them.
    const { workspace, revise: reviseWorkspace } = useWorkspace();
    const answered = opening.kind === 'answer' ? [opening.storedEmailId] : [];
    const [context, setContext] = useState<readonly string[]>(answered);
    const [contextStart, setContextStart] = useState<readonly string[] | null>(answered.length === 0 ? null : answered);

    // Where the list pointed when the composer last looked, so a click on a row is read as the click it was. It is
    // adjusted during render rather than in an effect, which is React's own shape for state following a value it does
    // not own: the context changes with the click that changed what the list points at, and no render is drawn between.
    // Only while the list stands beside the composer — narrow, the composer is the screen and nothing is pointed at.
    const [pointer, setPointer] = useState<ListPointer>({
        selection: workspace.selection,
        selected: workspace.selected,
    });

    if (pointer.selection !== workspace.selection || pointer.selected !== workspace.selected) {
        const now = { selection: workspace.selection, selected: workspace.selected };
        const pointed = wide ? contextPointedAt(pointer, now) : null;

        setPointer(now);

        if (pointed !== null) {
            setContext(pointed);
            setContextStart((started) => started ?? pointed);
        }
    }

    // What each message in the context is called, which only a read of it says. Held in memory for as long as the
    // composer is open and never kept, like everything else read about mail; `null` for a message that answered no
    // subject or could not be read.
    const [subjects, setSubjects] = useState<ReadonlyMap<string, string | null>>(new Map());
    const subjectsAsked = useRef(new Set(answered));

    // How many times the words have been replaced wholesale, which is what the editable region is keyed by: it renders
    // the tree it opens with once and never again, so a draft arriving has to be a new region rather than a new prop.
    // Ordinary typing changes nothing here, which is what keeps the caret where it was.
    const [draftGeneration, setDraftGeneration] = useState(0);

    const files = useRef<HTMLInputElement>(null);
    // The symbol alone below the workspace width, where it names the act beyond doubt; the name stays the control's,
    // read out and shown to a mouse resting on it or a finger holding it.
    const attachName = useTip({
        says: translate('compose.attach'),
        onPress: () => {
            files.current?.click();
        },
    });
    const asked = useRef<HTMLDialogElement>(null);
    const frame = useRef<HTMLElement>(null);
    const subjectId = useId();

    // Whether this composer is still mounted, which is what a send settling after it was closed reads.
    const present = useRef(true);

    useEffect(() => {
        present.current = true;

        return () => {
            present.current = false;
        };
    }, []);

    // An answer opens addressed to the people in the conversation, which means reading the message it answers. A
    // request going out is what an effect is for; the answer is discarded where the composer stopped listening for it,
    // and a message already restored from what this tab was writing needs no read at all.
    useEffect(() => {
        if (opening.kind !== 'answer') {
            return;
        }

        let listening = true;

        void readMailMessage(session, transport, opening.storedEmailId).then((answer) => {
            if (!listening) {
                return;
            }

            if (answer.outcome === 'failed') {
                setReading({ kind: 'unread', reason: answer.failure.reason });
                setSubjects((held) => new Map(held).set(opening.storedEmailId, null));

                return;
            }

            setParticipants(offeredFrom(answer.value.headers.participants));
            setSubjects((held) => new Map(held).set(opening.storedEmailId, answer.value.headers.subject));
            setComposition((held) => held ?? answerTo(answer.value, opening.answers));
        });

        return () => {
            listening = false;
        };
    }, [session, transport, opening]);

    // Each message the list puts into the context is read once for what its chip is called. A request going out is what
    // an effect is for; an answer arriving after the composer closed changes nothing.
    useEffect(() => {
        for (const storedEmailId of context) {
            if (subjectsAsked.current.has(storedEmailId)) {
                continue;
            }

            subjectsAsked.current.add(storedEmailId);

            void readMailMessage(session, transport, storedEmailId).then((answer) => {
                if (present.current) {
                    setSubjects((held) =>
                        new Map(held).set(
                            storedEmailId,
                            answer.outcome === 'failed' ? null : answer.value.headers.subject,
                        ),
                    );
                }
            });
        }
    }, [session, transport, context]);

    // The files a draft was filed with, taken into the message carrying it on. An effect event because it is what the
    // read below does once it answers, rather than something the read depends on.
    const carryTheDraftsFiles = useEffectEvent((storedEmailId: string, attachments: readonly MailAttachment[]) => {
        void draft.carry(storedEmailId, attachments);
    });

    // A draft is read the same way an answer is, and in two reads rather than one: the message route describes who it
    // is for and what it is about, and the body route is what holds the words. The body is asked for without the
    // sender's own markup, because what goes back into the composer is the reduced tree rather than markup this client
    // never parses — `draftWords.ts` is where that reading is stated. A body that could not be read opens the draft on
    // what the message route did answer, so the addresses and the subject are not lost with the words, and the files it
    // carries are brought over as well, because a message sent from a draft is sent whole.
    useEffect(() => {
        if (opening.kind !== 'draft') {
            return;
        }

        let listening = true;

        void Promise.all([
            readMailMessage(session, transport, opening.storedEmailId),
            readMailBody(session, transport, opening.storedEmailId, { remoteImages: false, fullHtml: false }),
        ]).then(([answer, body]) => {
            if (!listening) {
                return;
            }

            if (answer.outcome === 'failed') {
                setReading({ kind: 'unread', reason: answer.failure.reason });

                return;
            }

            setParticipants(offeredFrom(answer.value.headers.participants));
            setComposition((held) => held ?? draftContinued(answer.value, body.outcome === 'read' ? body.value : null));
            carryTheDraftsFiles(answer.value.storedEmailId, answer.value.attachments);
        });

        return () => {
            listening = false;
        };
    }, [session, transport, opening]);

    // A draft MailFathom keeps is read through its own record and carried on with as itself: the composer adopts its
    // identity, so every save revises it, and the files staged against it stay staged rather than being fetched and
    // chosen again. What this tab was already writing on it wins over what the record says, as with any other opening.
    const adoptTheDraft = useEffectEvent((held: MailDraft) => {
        draft.adopt(held);
    });

    useEffect(() => {
        if (opening.kind !== 'held') {
            return;
        }

        let listening = true;

        void readMailDraft(session, transport, opening.draftId).then((answer) => {
            if (!listening) {
                return;
            }

            if (answer.outcome === 'failed') {
                setReading({ kind: 'unread', reason: answer.failure.reason });

                return;
            }

            // A kept draft is somebody's writing whatever this tab has typed into it, so closing it asks first.
            adoptTheDraft(answer.value.draft);
            setAuthored(true);
            setComposition((held) => held ?? draftHeld(answer.value));
        });

        return () => {
            listening = false;
        };
    }, [session, transport, opening]);

    // The local draft, kept continuously so that a reload returns to what was being written rather than to nothing.
    // A browser store is something outside React, which is what an effect synchronizes with.
    useEffect(() => {
        if (composition !== null) {
            rememberComposition(composition);
        }
    }, [composition]);

    // Opening the composer is a view change, so focus goes into it rather than staying on the control that opened it.
    // Once, as the fields appear, on the first one there is to write in — the words wherever the recipients are written
    // already, which an answer and a draft carried on both are, and the recipients for a message of its own.
    const written = composition !== null;

    useEffect(() => {
        if (written) {
            frame.current
                ?.querySelector<HTMLElement>(opening.kind === 'new' ? 'input' : '[contenteditable="true"]')
                ?.focus();
        }
    }, [written, opening.kind]);

    function revise(change: Partial<Composition>): void {
        setAuthored(true);
        setComposition((held) => (held === null ? null : { ...held, ...change }));
    }

    // Asking the deployment to write the message, which is a request going out rather than anything that navigates:
    // what comes back replaces the words in front of the person, with the way back beside it. The press is where the
    // guard lives — a second drafting while the first is in flight would spend the allowance twice and put whichever
    // answered last on the screen.
    function askForADraft(instruction: string, passage: string | null = null): void {
        if (composition === null || drafting) {
            return;
        }

        // What the person themselves wrote, which a second drafting must not overwrite with the first draft: asking
        // twice and then restoring is asking for their own words back, not for the draft before this one.
        const before = beforeDrafting ?? composition.words;

        setDrafting(true);

        void draftMailReply(session, transport, {
            contextEmailIds: context,
            selection: passage === null ? null : passage.slice(0, longestDraftSelection),
            instruction,
        }).then((answer) => {
            // A draft asked for as the composer opened is often still in flight when it closes, and an answer for a
            // composer nobody has open is neither written anywhere nor worth a notice.
            if (!present.current) {
                return;
            }

            setDrafting(false);

            const read = draftingRead(answer);

            // A drafting nobody answered while the server is out of reach is the status's to say, not a second voice.
            if (answer.outcome === 'failed' && saidByTheStatus(disconnectedNow, answer.failure.reason)) {
                return;
            }

            if (!read.drafted) {
                toasts.raise({
                    kind: read.kind,
                    title: translate('compose.notDraftedTitle'),
                    body: translate(read.said),
                });

                return;
            }

            setBeforeDrafting(before);
            setDraftGeneration((written) => written + 1);
            revise({ words: writtenParagraphs(read.body) });
        });
    }

    // A reply asked for from the drafter is written the moment there is a message to write it into, and once per ask:
    // the answer it is written against has to be read first, and a render after that is not a second request. The ask
    // is the opening itself, because asking again on a reply already open hands this composer a new opening rather than
    // a new composer.
    const draftAsked = useRef<ComposerOpening | null>(null);
    const writeTheAskedDraft = useEffectEvent((asked: ReplyDraftAsked) => {
        askForADraft(
            [writeTheReply, asked.asked.trim()]
                .filter((part) => part !== '')
                .join(' ')
                .slice(0, longestDraftInstruction),
            asked.passage,
        );
    });

    useEffect(() => {
        if (written && opening.kind === 'answer' && opening.drafting !== undefined && draftAsked.current !== opening) {
            draftAsked.current = opening;
            writeTheAskedDraft(opening.drafting);
        }
    }, [written, opening]);

    // Putting back what the draft replaced, which is the one act that has to work whatever the draft turned out to be:
    // somebody who asked for one and did not want it is owed their own words rather than an undo they have to find.
    function restoreWhatWasWritten(): void {
        if (beforeDrafting === null) {
            return;
        }

        setDraftGeneration((written) => written + 1);
        revise({ words: beforeDrafting });
        setBeforeDrafting(null);
    }

    // Tab kept inside the composer while it stands over the whole screen. The two confirmations are `<dialog>`
    // elements and hold the keyboard themselves once open, so what is inside a closed one is skipped rather than
    // counted — a closed dialog draws nothing and is not somewhere a keyboard may land.
    function holdTheKeyboard(event: KeyboardEvent<HTMLElement>): void {
        if (wide || event.key !== 'Tab' || frame.current === null) {
            return;
        }

        const reachable = [...frame.current.querySelectorAll<HTMLElement>(reachableControls)].filter(
            (control) => control.tabIndex !== -1 && control.closest('dialog:not([open])') === null,
        );

        const first = reachable.at(0);
        const last = reachable.at(-1);

        if (first === undefined || last === undefined) {
            return;
        }

        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    function close(handFocusBack = true): void {
        forgetComposition();
        onClosed(handFocusBack);
    }

    // Whether the keyboard is still where the composer is, which decides whether a close nobody pressed may take it: a
    // send answered after somebody moved on to the list or the search closes the composer and leaves them where they
    // are.
    function focusIsHere(): boolean {
        const holder = document.activeElement;

        return holder === null || holder === document.body || (frame.current?.contains(holder) ?? false);
    }

    // Sending, said where the design says it: a toast stands over whatever the person turned to next and
    // follows the send until the deployment has answered it. **The composer closes once the deployment has the
    // message** — queued, or taken back into the drafts folder by a stop — rather than on the press, which is one round
    // trip later on a deployment that answers and the whole difference on one that refuses. A send can fail before it
    // files anything: the write it performs first can be refused by screening, or never answered, and a composer that
    // had closed on the press would have taken the only copy of the words with it while the toast said they were kept.
    // Left open, it holds everything written, every file, and the draft it did file, with the line at its foot saying
    // why — so the corrected message is a revision of that draft rather than a second one beside it.
    //
    // Closing it while the send is still in flight is the design's own order, and asks nothing: the toast is already
    // following the send and says what became of it. A message in flight is not the tab's composition, so a composer
    // opened meanwhile opens empty rather than on a copy of what is already on its way; only a send refused or failed
    // after the window closed hands the words back to the tab, for the next composer to open on — unless the tab is
    // keeping something written since.
    // The picker, opened on a time: the one already chosen, the first ready time, or one the deployment refused.
    function pickOn(asking: SendTime | null): void {
        const openedAt = Date.now();

        setPicking({ asking: asking ?? readyTimes(openedAt, zone).tomorrowMorning, openedAt });
        picker.current?.showModal();
    }

    // The confirmation's own act. A time that passed while the message was being written is refused before anything is
    // handed over, by opening the picker on it with its refusal — which the deployment's refusal of a time already
    // gone is the reason for.
    function sendAsked(sending: Composition, written: Composition): void {
        const leavesAt = sendAt === null ? null : instantStillAhead(sendAt, zone);

        if (sendAt !== null && leavesAt === null) {
            pickOn(sendAt);

            return;
        }

        sendAndReport(
            sending,
            written,
            leavesAt === null ? null : { instant: new Date(leavesAt).toISOString(), timeZone: namedZone(zone) },
        );
    }

    function sendAndReport(sending: Composition, written: Composition, dueAt: MailSendDueTime | null): void {
        // Whether somebody stopped it, which takes the toast following it away. What the stop came to — taken back, or
        // already on its way — is then said by a toast of its own, because the one it would have settled has gone.
        let stopped = false;
        const scheduled = sendAt;

        // A message scheduled for later is not being sent, so no *Sending your message…* stands over it: what it
        // came to is said once the deployment has answered.
        const settled =
            dueAt === null
                ? toasts.raiseOperation({
                      title: translate('compose.sendingTitle'),
                      body: translate('compose.confirmTo', { addresses: addresses.format(sending.to) }),
                      stoppingLeavesBehind: translate('compose.stoppingSendLeavesBehind'),
                      stoppedLeftBehind: translate('compose.stoppedSendLeftBehind'),
                      stop: () => {
                          stopped = true;
                          withdrawAndReport();
                      },
                  })
                : (report: Toast) => {
                      toasts.raise(report);
                  };

        void draft.send(sending, dueAt).then((outcome) => {
            const report = sendReport(outcome, sending.account, dueAt === null ? null : leaves);

            if (stopped) {
                toasts.raise(report);
            } else {
                settled(report);
            }

            // The time went while the deployment read it, so the picker opens on it again to say so.
            if (present.current && outcome.kind === 'refused' && outcome.refusal === 'dueTimePassed') {
                pickOn(scheduled);
            }

            const deploymentHasIt = outcome.kind === 'queued' || outcome.kind === 'withdrawn';

            // A send nobody answered may be queued already, so its words are not handed back to the tab to be sent a
            // second time: the deployment holds them either way, as the draft that was saved first or as the send.
            const deploymentMayHaveIt = deploymentHasIt || outcome.kind === 'sendUnknown';

            // Only while this composer is still the one on the screen: closed in flight, it has already gone, and
            // closing again would close whatever the person opened in its place.
            if (present.current) {
                if (deploymentHasIt) {
                    close(focusIsHere());
                }
            } else if (!deploymentMayHaveIt && rememberedComposition() === null) {
                rememberComposition(written);
            }
        });
    }

    // Opened at once, so the list's place says it is reading, and then on that account's Sent folder, as the mark on
    // its Sent row opens it, so that whatever closes the Outbox leaves the reader where the send went. The folder lands
    // only while that account's Outbox is still open: a reader who picked a folder or a message meanwhile has closed
    // it, and a late directory answer must not take them back. One that does not answer leaves the Outbox open over
    // whatever was listed, and *Go to Sent* asks for the folder again rather than trusting what is behind it.
    function showInOutbox(account: string, outgoingEmailId: string): void {
        showOutboxEntry(session, transport, reviseWorkspace, account, outgoingEmailId);
    }

    function withdrawAndReport(): void {
        void draft.withdraw().then((outcome) => {
            if (outcome.kind === 'withdrawn') {
                toasts.raise({
                    kind: outcome.withdrawal === 'withdrawn' ? 'success' : 'warning',
                    title: translate(withdrawalTitle[outcome.withdrawal]),
                    body: translate(withdrawalSaid[outcome.withdrawal]),
                });
            }
        });
    }

    // What the standing toast becomes once the deployment has answered, which is the whole answer: the composer may
    // already have been closed while the send was in flight, so this is the one place the outcome is always said, and a
    // title with nothing under it would leave somebody knowing a message did not go and not why.
    //
    // **A queued message offers no way back here.** The design draws none, and this client has nothing to draw one
    // with: the one act that would have to outlive the composer is the one the composer is gone for. What it offers
    // instead is the send itself, in its account's Outbox, which is where withdrawing it is offered for as long as it
    // can be.
    function sendReport(outcome: DraftStanding, account: string, scheduled: SendTimeWording | null): Toast {
        switch (outcome.kind) {
            case 'queued':
                return {
                    kind: 'success',
                    title: translate(scheduled === null ? 'compose.sentTitle' : 'compose.scheduledTitle'),
                    body:
                        scheduled === null
                            ? translate('compose.queued')
                            : translate('compose.scheduled', { when: scheduled.long }),
                    ...(showsOutbox
                        ? {
                              action: {
                                  label: translate('compose.showInOutbox'),
                                  take: () => {
                                      showInOutbox(account, outcome.outgoingEmailId);
                                  },
                              },
                          }
                        : {}),
                };
            case 'withdrawn':
                return {
                    kind: outcome.withdrawal === 'withdrawn' ? 'success' : 'warning',
                    title: translate(withdrawalTitle[outcome.withdrawal]),
                    body: translate(withdrawalSaid[outcome.withdrawal]),
                };
            case 'refused':
                return {
                    kind: 'error',
                    title: translate('compose.notSentTitle'),
                    body: translate(refusalSaid[outcome.refusal]),
                };
            case 'refusedSave':
                return {
                    kind: 'error',
                    title: translate('compose.notSentTitle'),
                    body: translate(saveRefusalSaid[outcome.refusal]),
                };
            case 'failed':
                return {
                    kind: 'error',
                    title: translate('compose.notSentTitle'),
                    body: translate(outcome.unanswered ? 'compose.saveUnknown' : failureSaid[outcome.reason]),
                };
            case 'sendUnknown':
                // The send went out and nothing answered, so it is not said to have failed and nothing offers it again:
                // the Outbox is what knows whether it is on its way.
                return {
                    kind: 'warning',
                    title: translate('compose.sendUnknownTitle'),
                    body: translate('compose.sendUnknown'),
                };
            default:
                return { kind: 'error', title: translate('compose.notSentTitle') };
        }
    }

    // Whether the message is still being written rather than on its way, read by every control that changes it. Two
    // presses would queue the same message twice — while the first is still in flight, once it is queued, and equally
    // once a send went unanswered and may be queued already — and a save or an attach after that would say what is
    // happening over the queued state and take the way to withdraw off the screen with it.
    const stillBeingWritten =
        draft.standing.kind !== 'sending' && draft.standing.kind !== 'queued' && draft.standing.kind !== 'sendUnknown';

    // Whether it may be filed, which is the same question and a deployment that answers. Attaching does not ask it:
    // a chosen file is held here until the message is saved or sent, so it costs nothing and is offered offline.
    // Nor while a draft carried on is still bringing its files over: what went out then would leave the rest behind.
    const sendable = online && stillBeingWritten && draft.standing.kind !== 'carrying';

    // While the server is out of reach the two controls that write say so before they are pressed, rather than taking a
    // save or a send that has no route to the deployment. The words stay here, and the line above the controls says
    // they are not safe until the connection is back.
    const sendRefused = writeRefused(translate('compose.send'));
    const saveRefused = writeRefused(translate('compose.saveDraft'));
    const saveName = useTip({
        says: saveRefused ?? translate('compose.saveDraft'),
        describes: true,
        onPress: () => {
            if (sendable && saveRefused === null && composition !== null) {
                void draft.save(sentAs(composition, copiesShown));
            }
        },
    });

    const sendingOptions = useSendingOptions({
        requested: {
            requestsDeliveryNotification: composition?.requestsDeliveryNotification ?? false,
            requestsReadReceipt: composition?.requestsReadReceipt ?? false,
        },
        onTurned: revise,
        sendAt,
        zone,
        onSendAt: setSendAt,
        onPick: () => {
            pickOn(sendAt);
        },
    });

    const title = translate(titles[opening.kind === 'answer' ? opening.answers : opening.kind]);

    function subjectSaid(storedEmailId: string): string {
        const subject = subjects.get(storedEmailId);

        if (subject === undefined) {
            return translate('compose.aiContextReading');
        }

        return subject === null || subject.trim() === '' ? translate('message.noSubject') : subject;
    }

    const changedFrom: DraftingContextStart | null =
        contextStart === null || sameMessages(context, contextStart)
            ? null
            : { subject: subjectSaid(contextStart[0] ?? ''), more: contextStart.length - 1 };
    const copiesShown = copiesChosen ?? (composition !== null && composition.cc.length + composition.bcc.length > 0);

    return (
        <section
            ref={frame}
            aria-label={title}
            // Narrow, this covers the whole viewport, which makes it a dialog whatever it is composed out of: the
            // spaces beside it stay mounted underneath, so without this a keyboard would tab straight off the screen
            // into controls nobody can see. Wide it is a column beside the others and neither is true.
            role={wide ? undefined : 'dialog'}
            aria-modal={wide ? undefined : true}
            onKeyDown={holdTheKeyboard}
            // The scroll padding is the foot's height, and the head's where a low window pins it too: without it the
            // browser counts a field standing under either as on the screen, and focusing it scrolls nothing.
            className={`flex scroll-pb-18 flex-col overflow-y-auto bg-panel text-text low:scroll-pt-12 ${
                wide
                    ? 'h-full min-h-0 animate-rise-in'
                    : 'fixed inset-0 z-50 pt-safe-top pb-safe-bottom animate-sheet-in'
            }`}
        >
            {/* A low window shrinks the head to its close control and keeps it at the top, so the fields and the words
                scroll between it and the foot carrying *Send* however far a keyboard shortens the window. */}
            <div className="flex shrink-0 items-center gap-3 border-b border-line bg-sunken px-4.25 py-3 low:sticky low:top-0 low:z-10 low:py-0 low:pe-1.5">
                <h2 className="text-md font-semibold">{title}</h2>

                <div className="ms-auto flex items-center">
                    <DiscardConfirmation
                        inFront={inFront}
                        edged={!wide}
                        written={stillBeingWritten && (authored || draft.attached.length > 0)}
                        onDiscard={() => {
                            // A message on its way is not given up by closing the window it was written in: the toast
                            // following the send is where it goes from here, which is closing on the press. What was
                            // written comes back to the tab only if the send does not go.
                            if (!stillBeingWritten) {
                                close();

                                return;
                            }

                            // The same rule the keep path holds to: a deployment that refused is one the composer
                            // stays open on, because closing on it is how what somebody wrote is lost quietly.
                            void draft.discard().then((given) => {
                                if (given) {
                                    close();
                                }
                            });
                        }}
                        onKeep={() => {
                            if (composition !== null) {
                                // A draft the deployment refused to file is one the composer stays open on, because
                                // closing on it is how what somebody wrote is lost quietly.
                                void draft.save(sentAs(composition, copiesShown)).then((filed) => {
                                    if (filed) {
                                        close();
                                    }
                                });
                            }
                        }}
                    />
                </div>
            </div>

            {composition === null ? (
                <BeforeAnythingIsWritten reading={reading} opening={opening} />
            ) : (
                <>
                    {/* First, above everything it is about: what another mail app shows of this draft may not be what
                        is written here, and the next save tries again, so it is said and nothing is offered. */}
                    {(draft.record?.divergence ?? null) === null ? null : (
                        <p
                            role="status"
                            className="flex items-start gap-2.25 border-b border-warning bg-warning-soft px-3.75 py-2.25 text-sm/relaxed text-warning-text pointer-coarse:py-2.75 pointer-coarse:text-base"
                        >
                            <Icon name="cloud_off" className="size-icon-md shrink-0" />
                            <span className="min-w-0 flex-1 text-pretty">{translate('compose.noServerCopy')}</span>
                        </p>
                    )}

                    {accounts.length > 1 && composition.answering === null ? (
                        <div className="flex items-center gap-2.5 border-b border-line-soft px-3.75 py-2.25">
                            <label
                                htmlFor={`${subjectId}-from`}
                                className="w-14.5 shrink-0 text-sm text-muted workspace:w-22"
                            >
                                {translate('compose.from')}
                            </label>

                            <select
                                id={`${subjectId}-from`}
                                value={composition.account}
                                className="flex-1 rounded-md border border-line bg-panel px-2 py-1 text-base text-text"
                                onChange={(event) => {
                                    revise({ account: event.target.value });
                                }}
                            >
                                {accounts.map((account) => (
                                    <option key={account.id} value={account.id}>
                                        {account.displayName}
                                    </option>
                                ))}
                            </select>
                        </div>
                    ) : null}

                    <RecipientField
                        label={translate('compose.to')}
                        placeholder={translate('compose.addRecipient')}
                        addresses={composition.to}
                        participants={participants}
                        session={session}
                        organizationName={organizationName}
                        transport={transport}
                        onChanged={(to) => {
                            revise({ to });
                        }}
                        trailing={
                            <button
                                type="button"
                                aria-expanded={copiesShown}
                                aria-label={translate(copiesShown ? 'compose.hideCopies' : 'compose.showCopies')}
                                className={`shrink-0 rounded-md px-2 py-0.5 text-sm transition hover:text-text ${
                                    copiesShown ? 'bg-accent-soft text-accent-deep' : 'text-muted'
                                }`}
                                onClick={() => {
                                    setCopiesChosen(!copiesShown);
                                }}
                            >
                                {translate('compose.copyHeaders')}
                            </button>
                        }
                    />

                    <AbsentRecipients addresses={composition.to} session={session} transport={transport} />

                    {copiesShown ? (
                        <>
                            <RecipientField
                                label={translate('compose.cc')}
                                placeholder={translate('compose.ccPlaceholder')}
                                addresses={composition.cc}
                                participants={participants}
                                session={session}
                                organizationName={organizationName}
                                transport={transport}
                                onChanged={(cc) => {
                                    revise({ cc });
                                }}
                            />

                            <p className="flex items-start gap-2.25 border-b border-line-soft bg-sunken px-3.75 py-2.25">
                                <span className="w-14.5 shrink-0 workspace:w-22">
                                    <Icon name="visibility" className="size-icon-md text-warning-text" />
                                </span>
                                <span className="min-w-0 flex-1 text-sm text-text-soft text-pretty">
                                    {translate('compose.copiesVisible')}
                                </span>
                            </p>

                            <RecipientField
                                label={translate('compose.bcc')}
                                placeholder={translate('compose.bccPlaceholder')}
                                addresses={composition.bcc}
                                participants={participants}
                                session={session}
                                organizationName={organizationName}
                                transport={transport}
                                onChanged={(bcc) => {
                                    revise({ bcc });
                                }}
                            />
                        </>
                    ) : null}

                    <div className="flex items-center gap-2.5 border-b border-line px-3.75 py-2.25 focus-within:border-b-accent">
                        {/* Two elements rather than one, because only one of the two things the row holds is labelable:
                            an answer's subject is text the deployment writes, and `for` on a paragraph names nothing a
                            screen reader would follow. So the field takes a label and the paragraph is named by the
                            word beside it. */}
                        {composition.answering === null ? (
                            <>
                                <label
                                    htmlFor={subjectId}
                                    className="w-14.5 shrink-0 text-sm text-muted workspace:w-22"
                                >
                                    {translate('compose.subject')}
                                </label>

                                <input
                                    id={subjectId}
                                    value={composition.subject}
                                    placeholder={translate('compose.subjectPlaceholder')}
                                    className="min-w-0 flex-1 border-none bg-transparent text-md text-text outline-none placeholder:text-faint"
                                    onChange={(event) => {
                                        revise({ subject: event.target.value });
                                    }}
                                />
                            </>
                        ) : (
                            <>
                                <span id={subjectId} className="w-14.5 shrink-0 text-sm text-muted workspace:w-22">
                                    {translate('compose.subject')}
                                </span>

                                <p aria-labelledby={subjectId} className="min-w-0 flex-1 truncate text-md">
                                    {composition.subject}
                                    <span className="sr-only"> {translate('compose.subjectOfAnAnswer')}</span>
                                </p>
                            </>
                        )}
                    </div>

                    {drafts ? (
                        <DraftingBlock
                            context={context.map((storedEmailId) => ({
                                storedEmailId,
                                subject: subjectSaid(storedEmailId),
                            }))}
                            noContextSaid={wide ? 'compose.aiContextNone' : 'compose.aiContextNoneHere'}
                            changedFrom={changedFrom}
                            asked={opening.kind === 'answer' ? (opening.drafting?.asked ?? '') : ''}
                            busy={drafting}
                            onDraft={(instruction) => {
                                askForADraft(instruction);
                            }}
                            onRemove={(storedEmailId) => {
                                setContext((held) => held.filter((message) => message !== storedEmailId));
                            }}
                            onRestore={() => {
                                if (contextStart !== null) {
                                    setContext(contextStart);
                                }
                            }}
                        />
                    ) : null}

                    <WrittenMessage
                        key={draftGeneration}
                        opened={composition.words}
                        onWritten={(words) => {
                            revise({ words });
                        }}
                        onSendAsked={() => {
                            if (sendable && sendRefused === null) {
                                asked.current?.showModal();
                            }
                        }}
                        beforeTheWords={
                            drafts ? (
                                <DraftingStanding
                                    busy={drafting}
                                    answering={composition.answering !== null}
                                    drafted={beforeDrafting !== null}
                                    onRestore={restoreWhatWasWritten}
                                    onAccept={() => {
                                        setBeforeDrafting(null);
                                    }}
                                />
                            ) : null
                        }
                    />

                    <AttachedFiles
                        attached={draft.attached}
                        locale={locale}
                        onDetach={(attachmentId) => {
                            void draft.detach(attachmentId);
                        }}
                    />

                    <WhatIsHappening standing={draft.standing} online={online} timeChosen={sendAt !== null} />

                    {/* The line saying what the message asks for stands with the foot, so a long message scrolled to its
                        start still says it beside the control that sends it. */}
                    <div className={`sticky z-10 shrink-0 ${wide ? 'bottom-0' : 'bottom-safe-bottom'}`}>
                        {sendingOptions.line}
                        {sendingOptions.timeLine}

                        <div className="flex flex-wrap items-center gap-2.5 border-t border-line bg-sunken px-3.75 py-2.75">
                            <SendConfirmation
                                asked={asked}
                                composition={sentAs(composition, copiesShown)}
                                draftUnaccepted={beforeDrafting !== null}
                                disabled={!sendable}
                                refused={sendRefused}
                                leaves={leaves === null ? null : { ...leaves, zone: namedZone(zone) }}
                                onSend={() => {
                                    sendAsked(sentAs(composition, copiesShown), composition);
                                }}
                            />

                            <button
                                {...(wide ? { onClick: attachName.anchor.onClick } : attachName.anchor)}
                                type="button"
                                disabled={!stillBeingWritten}
                                className="flex items-center justify-center gap-1.5 rounded-md border border-line-strong px-3.25 py-2.25 text-base text-text-soft transition hover:bg-hover disabled:opacity-60 max-workspace:px-2 pointer-coarse:text-md"
                            >
                                <Icon name="attach_file" className="size-icon-md" />
                                <span className="max-workspace:sr-only">{translate('compose.attach')}</span>
                            </button>
                            {wide ? null : attachName.tip}

                            {/* The platform's own file picker, kept out of the accessible tree because the control above is
                            what carries the name and the keyboard path. */}
                            <input
                                ref={files}
                                type="file"
                                multiple
                                tabIndex={-1}
                                aria-hidden="true"
                                className="hidden"
                                onChange={(event) => {
                                    const chosen = [...(event.target.files ?? [])];

                                    event.target.value = '';

                                    for (const file of chosen) {
                                        draft.attach(file);
                                    }
                                }}
                            />

                            <button
                                {...(saveRefused === null
                                    ? {
                                          onClick: () => {
                                              void draft.save(sentAs(composition, copiesShown));
                                          },
                                      }
                                    : saveName.anchor)}
                                type="button"
                                disabled={!sendable && saveRefused === null}
                                aria-disabled={saveRefused === null ? undefined : true}
                                className="rounded-md py-2 text-sm text-muted pointer-coarse:px-1.5 transition hover:bg-hover hover:text-text disabled:opacity-60 aria-disabled:cursor-not-allowed aria-disabled:opacity-60 pointer-coarse:text-base"
                            >
                                {translate('compose.saveDraft')}
                            </button>
                            {saveRefused === null ? null : saveName.tip}

                            <p className="ms-auto text-xs text-faint pointer-coarse:hidden">
                                {translate(sendAt === null ? 'compose.shortcutSends' : 'compose.shortcutSchedules')}
                            </p>

                            {sendingOptions.control}
                        </div>
                    </div>

                    <SendLaterPicker
                        asked={picker}
                        asking={picking?.asking ?? null}
                        now={picking?.openedAt ?? shownAt}
                        zone={zone}
                        onAsking={(asking) => {
                            setPicking((standing) => (standing === null ? null : { ...standing, asking }));
                        }}
                        onSchedule={(chosen) => {
                            setSendAt({
                                ...chosen,
                                readyTime: readyTimeOf(chosen, picking?.openedAt ?? shownAt, zone),
                            });
                        }}
                    />
                </>
            )}
        </section>
    );
}

// The people a conversation names, as a recipient field offers them: each address once, however many headers it is in,
// under the name the message gave it where it gave one.
function offeredFrom(
    named: readonly { readonly address: string; readonly displayName: string | null }[],
): readonly RecipientSuggestion[] {
    return named
        .filter((person, index) => named.findIndex((earlier) => earlier.address === person.address) === index)
        .map((person) => ({ address: person.address, name: person.displayName }));
}

// What leaves the composer for what is written: all of it while the copy headers are shown, and nothing from them while
// they are hidden, whatever they still hold.
function sentAs(composition: Composition, copiesShown: boolean): Composition {
    return copiesShown ? composition : { ...composition, cc: [], bcc: [] };
}

// Where a composition starts: what this tab was already writing where it matches what is being opened, an empty
// message of its own, or nothing at all while the message an answer is written against is still being read.
function opened(opening: ComposerOpening, accounts: readonly MailAccount[]): Composition | null {
    const kept = rememberedComposition();

    const addressed = opening.kind === 'new' ? (opening.to ?? []) : [];

    if (kept !== null && sameOpening(kept, opening)) {
        // What was being written is kept and whoever the opening named is added to it: somebody who asks to write to a
        // person while half a message of their own is open is addressing that message rather than starting a second
        // one, and a repeat is not written down twice.
        return { ...kept, to: [...kept.to, ...addressed.filter((address) => !kept.to.includes(address))] };
    }

    return opening.kind === 'new' ? nothingWrittenYet(accounts[0]?.id ?? '', addressed) : null;
}

function sameOpening(kept: Composition, opening: ComposerOpening): boolean {
    switch (opening.kind) {
        case 'new':
            return kept.answering === null && kept.continuing === null;
        case 'draft':
            return kept.continuing === opening.storedEmailId;
        case 'held':
            return kept.continuing === opening.draftId;
        case 'answer':
            return (
                kept.answering?.storedEmailId === opening.storedEmailId && kept.answering.answers === opening.answers
            );
    }
}

// The composer before there is a message in it, which happens only for an answer: the conversation it is written
// against has to be read before it can be addressed. Both states say what is happening, and the way out of either is
// the control the header already carries — with nothing written, it closes rather than asking.
function BeforeAnythingIsWritten({
    reading,
    opening,
}: {
    readonly reading: Reading;
    readonly opening: ComposerOpening;
}) {
    const { translate } = useLocalization();

    // What is being read differs between the two openings that read anything at all, and so does what a reader is
    // owed while it happens: an answer waits on the message it answers, a draft on the words that reader wrote.
    const said =
        opening.kind === 'draft'
            ? 'compose.readingDraft'
            : opening.kind === 'held'
              ? 'compose.readingHeldDraft'
              : 'compose.reading';

    return (
        <div className="flex flex-1 flex-col items-start gap-3 px-4.25 py-6">
            <p aria-live="polite" className="text-base text-muted text-pretty">
                {translate(
                    reading.kind === 'reading'
                        ? said
                        : opening.kind === 'held' && reading.reason === 'missing'
                          ? 'compose.failedMissingDraft'
                          : failureSaid[reading.reason],
                )}
            </p>
        </div>
    );
}

// The files the message carries, drawn as the reading pane draws the files a message already has: what each one is
// called and how large it is, before anything is fetched or sent. Whether the deployment holds one yet is not drawn
// and is deliberately not drawable — a file is part of the message from the moment it was chosen, and where it
// currently sits is the hook's business rather than something its author has to follow.
function AttachedFiles({
    attached,
    locale,
    onDetach,
}: {
    readonly attached: readonly AttachedFile[];
    readonly locale: Locale;
    readonly onDetach: (attachmentId: string) => void;
}) {
    const { translate } = useLocalization();

    if (attached.length === 0) {
        return null;
    }

    return (
        <ul
            aria-label={translate('compose.attachedFiles')}
            className="flex shrink-0 flex-wrap gap-2 border-t border-line-soft px-4.25 py-2.5"
        >
            {attached.map((file) => (
                <li
                    key={file.attachmentId}
                    className="flex items-center gap-2 rounded-md border border-line bg-rail px-2.5 py-1.25 text-base"
                >
                    <Icon name="attach_file" className="size-icon-sm text-muted" />
                    <span className="max-w-60 truncate">{file.fileName}</span>
                    <span className="text-sm text-faint">{sizeOf(file.sizeOctets, locale)}</span>

                    <SymbolControl
                        label={translate('compose.removeFile', { name: file.fileName })}
                        className="flex items-center rounded-xs text-faint transition hover:text-text"
                        onPress={() => {
                            onDetach(file.attachmentId);
                        }}
                    >
                        <Icon name="close" className="size-icon-xs" />
                    </SymbolControl>
                </li>
            ))}
        </ul>
    );
}

// What the deployment is doing about the draft, said where it happens rather than as a banner over the whole client:
// saving, attaching, and what refused either. Nothing waits in silence, and every refusal names what would change it.
//
// **The send is not among them.** What became of a message somebody sent is said in a toast, which is where the design
// says it: it stands over whatever they turned to next rather than at the foot of a window they are finished
// with. A refusal is the one the toast and this line both carry, and deliberately — the toast is what reaches somebody
// who has looked away, and the line is what is still there when they come back to the words the deployment refused.
//
// **Without a connection the words are not safe, and the line says so.** What is being written is held in this tab's
// session store alone, so closing the tab or the window loses it; that is said whenever the server is out of reach —
// no network, or a server not answering — rather than only when this machine is offline.
function WhatIsHappening({
    standing,
    online,
    timeChosen,
}: {
    readonly standing: DraftStanding;
    readonly online: boolean;

    /** Whether a time to send at is chosen, which a saved draft does not keep and the line then says so. */
    readonly timeChosen: boolean;
}) {
    const { locale, translate } = useLocalization();
    const status = useConnectionStatus();

    if (!online || disconnected(status)) {
        return (
            <p
                role="status"
                className="flex shrink-0 items-start gap-2 border-t border-line bg-warning-soft px-3.75 py-2.25 text-base/normal text-pretty text-warning-text"
            >
                <Icon name="cloud_off" className="size-icon-md shrink-0" />
                <span>{translate('compose.disconnected')}</span>
            </p>
        );
    }

    switch (standing.kind) {
        case 'held':
        case 'sending':
        case 'queued':
        case 'withdrawn':
            return null;
        case 'saving':
            return <Said text={translate('compose.saving')} />;
        case 'saved':
            return <Said text={translate(timeChosen ? 'compose.savedWithoutTime' : 'compose.saved')} />;
        case 'attaching':
            return <Said text={translate('compose.attaching', { name: standing.fileName })} />;
        case 'carrying':
            return <Said text={translate('compose.carrying', { name: standing.fileName })} />;
        case 'notCarried':
            return (
                <Said
                    text={translate(
                        standing.fileNames.length === 1 ? 'compose.notCarried' : 'compose.notCarriedSeveral',
                        {
                            names: new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' }).format(
                                standing.fileNames,
                            ),
                        },
                    )}
                    warning
                />
            );
        case 'refused':
            return <Said text={translate(refusalSaid[standing.refusal])} warning />;
        case 'refusedSave':
            return <Said text={translate(saveRefusalSaid[standing.refusal])} warning />;
        case 'failed':
            return (
                <Said
                    text={translate(standing.unanswered ? 'compose.saveUnknown' : failureSaid[standing.reason])}
                    warning
                />
            );
        case 'sendUnknown':
            return <Said text={translate('compose.sendUnknown')} warning />;
    }
}

function Said({
    text,
    warning = false,
    children,
}: {
    readonly text: string;
    readonly warning?: boolean;
    readonly children?: ReactNode;
}) {
    return (
        <p
            aria-live="polite"
            className={`flex shrink-0 flex-wrap items-center gap-2 border-t px-4.25 py-2 text-base text-pretty ${
                warning ? 'border-warning bg-warning-soft text-warning-text' : 'border-line-soft bg-sunken text-muted'
            }`}
        >
            {text}
            {children}
        </p>
    );
}
