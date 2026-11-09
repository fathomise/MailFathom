// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useRef, useState, type ReactNode } from 'react';
import {
    changeMailFlags,
    deleteMail,
    mostMessagesPerMutation,
    moveMail,
    readMailFolders,
    releaseMailDeletes,
    wentUnanswered,
    type ClientFailure,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailFlagChange,
    type MailFolder,
    type MailFolderDirectory,
    type MailFolderRole,
    type MailMutationResult,
} from '@mailfathom/client-backend';
import { newIdentifier } from '../agent/newIdentifier';
import { useFolderMaintenance } from '../folders/useFolderMaintenance';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useListedMail, type DrawnMail } from '../messageList/useListedMail';
import type { ChangeAct, ChangeSubmission } from '../pendingChanges/changeSubmission';
import { usePendingChanges } from '../pendingChanges/usePendingChanges';
import { useReadMarking } from '../readMarking/useReadMarking';
import { useSpamMarks } from '../spamMarking/useSpamMarks';
import { useTelemetry } from '../telemetry/clientTelemetry';
import { disconnected, saidByTheStatus, useConnectionStatus } from '../shell/connectionStatus';
import { useToasts } from '../toasts/useToasts';
import {
    deletesPermanently,
    destinationName,
    destinationsFor,
    filesIntoTrash,
    filingFor,
    mayBeTakenBack,
    refusalFor,
    type ActRefusal,
    type MoveDestination,
} from './mailboxDestinations';
import {
    changesAFlag,
    flagWritten,
    MailboxActsContext,
    nothingActed,
    type ActedMessage,
    type AskedAct,
    type FilingAct,
    type MailboxAct,
    type MailboxActs,
} from './useMailboxActs';

// Performing the acts, which is the one place in this client that changes somebody's mailbox from the Mail space.
// Nothing here reaches a mail server: each act goes through `/api/client`, which makes the change in what MailFathom
// holds and answers, and the account's own convergence pass is what issues the IMAP command. So an account nobody can
// connect to still has the change made, and what is held below is what was asked for until a listing shows it.
//
// **The answer is the whole of what an act is owed.** Every batch goes out under a key of its own, and the answer is
// handed to `pendingChanges/`, which says what the deployment refused in the words it says them in for every act — so
// which sentence a person meets never turns on which surface asked — and holds a batch nobody answered, to be sent
// again under that same key once the server answers. A change answered as made is drawn from the first listing read
// after it, which is how a change the mail server later refused, and MailFathom undid, is drawn undone. What stays here
// is the half the queue has no notion of: the report of what an act was written down for, in the toast surface rather
// than on the control that was pressed, and the way back the three that file a message elsewhere offer as that toast's
// single action. Taking one back is the reverse move rather than a withdrawal of the first: a change
// already on its way to a mail server cannot be unsaid, and pretending otherwise would leave the mailbox and the screen
// disagreeing. The one act that offers no way back is the
// delete that destroys the mail, which is what *delete* means for a message already in the trash — there is no message
// left to move back, which is why the question in front of it says so before it is performed, and why the deployment is
// told at once that nobody will be offered one.
//
// **Filing into the trash is deleting**, whichever surface named the folder: a move naming the account's trash is
// performed as the delete it is, so it is reported, drawn and taken back exactly as the delete control's is.
//
// **What each act may do at all is answered before it is offered**, which is `mailboxDestinations.ts`. An account with
// no archive folder is a control that says so rather than one that fails once it has been pressed.

// The three acts that file a message elsewhere are reported and the four that write a flag are not, which is the
// design's own and is a statement about what a report is for rather than about how much each act matters. A
// message filed somewhere else has left the screen it was on, so the toast is where somebody learns where it went and
// the only place the way back is offered; a flag is a mark the row draws the moment it is asked for, and a card in the
// corner saying the mark that just appeared has appeared is the client narrating itself.

/** What the toast reporting a finished act is titled, exhaustive by the acts that raise one. */
const actReported: Readonly<Record<FilingAct, MessageKey>> = {
    archive: 'act.archived',
    delete: 'act.deleted',
    move: 'act.filed',
};

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

// How many messages are counted in each of the forms a language has for the noun. Selected rather than spelled, for
// the reason `mailSpace/TabStrip.tsx` gives: Polish needs three forms and English hides that it needs two.
const messagesCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'act.messages.other',
    one: 'act.messages.one',
    two: 'act.messages.other',
    few: 'act.messages.few',
    many: 'act.messages.many',
    other: 'act.messages.other',
};

/**
 * What is held, and whose it is, so one person's pending acts are never drawn over the next person's mail.
 *
 * Kept by the person rather than by the session object, because a renewed credential is a new object for the same
 * person: what they asked for before the renewal is still theirs, and is still answered as theirs.
 */
interface Held {
    readonly signedInAs: string | null;
    readonly directory: MailFolderDirectory | null;
    readonly asked: ReadonlyMap<string, AskedAct>;
}

const heldForNobody: Held = { signedInAs: null, directory: null, asked: new Map() };

/**
 * The messages a submission was actually written down for, which a batch that answered does not say by itself.
 *
 * A message the deployment did not record is mail that has moved on since the list drew it, an account it no longer
 * serves, or a message already where it was asked to go. Each is that message's own answer rather than the request's,
 * which is what lets the rest of a batch stand — and it is read the same way whichever direction the act was going in.
 * A message answered `applied` counts beside a recorded one: the deployment already made the change, on an account it
 * holds alone, so the act stands even though there is no record to follow.
 */
function writtenDown(batches: readonly Submitted[]): ReadonlySet<string> {
    return new Set(
        batches.flatMap(({ answer }) =>
            answer.outcome === 'read'
                ? answer.value
                      .filter((result) => result.outcome === 'recorded' || result.outcome === 'applied')
                      .map((result) => result.storedEmailId)
                : [],
        ),
    );
}

/**
 * The messages the deployment answered `applied` for, which it changed at once and wrote no record of.
 *
 * A permanent delete is only what the records stand behind: a message the deployment applied a delete to was filed into
 * the trash of an account it holds alone, so it is reported the way a delete into the trash is, never as destroyed.
 */
function appliedTo(batches: readonly Submitted[]): ReadonlySet<string> {
    return new Set(
        batches.flatMap(({ answer }) =>
            answer.outcome === 'read'
                ? answer.value.filter((result) => result.outcome === 'applied').map((result) => result.storedEmailId)
                : [],
        ),
    );
}

/**
 * The records a submission wrote down, which is what ending a permanent delete's wait names.
 *
 * Records rather than messages, because a record is the unit the deployment holds, cancels, and takes in hand — and a
 * message it answered `recorded` for is exactly a message whose changes carry one.
 */
function recordsWritten(batches: readonly Submitted[]): readonly string[] {
    return batches.flatMap(({ answer }) =>
        answer.outcome === 'read'
            ? answer.value
                  .filter((result) => result.outcome === 'recorded')
                  .flatMap((result) => result.changes.map((change) => change.recordId))
            : [],
    );
}

/** An asked act on its own, without what it held beside it. */
function alone({ act, from, leaves, destroys, awaiting, appliedAt }: AskedAct): AskedAct {
    return { act, from, leaves, destroys, awaiting, ...(appliedAt === undefined ? {} : { appliedAt }) };
}

/**
 * The claim on one message once the deployment has answered the act: no longer awaiting, and stamped with when it was
 * made where the deployment made it. Only the claim of that act is touched, since the other flag's act answered apart.
 */
function answeredClaim(asked: AskedAct, act: MailboxAct, applied: number | null): AskedAct {
    function answeredOne(one: AskedAct): AskedAct {
        return one.act !== act ? one : { ...one, awaiting: false, ...(applied === null ? {} : { appliedAt: applied }) };
    }

    const beside = asked.beside === undefined ? undefined : answeredOne(asked.beside);

    return { ...answeredOne(asked), ...(beside === undefined ? {} : { beside }) };
}

/**
 * What a message is still asked for once a page read after the deployment made the change has drawn it: every claim
 * made before that read is let go, because the page is what the server holds — the change, or the change undone.
 */
function afterDrawn(asked: AskedAct, askedAt: number): AskedAct | undefined {
    const shown = (one: AskedAct): boolean => one.appliedAt !== undefined && askedAt >= one.appliedAt;
    const beside = asked.beside === undefined || shown(asked.beside) ? undefined : asked.beside;

    if (shown(asked)) {
        return beside;
    }

    return beside === asked.beside ? asked : alone(asked);
}

/**
 * The act a message is still asked of the other flag, which an act writing one flag keeps beside it rather than
 * replacing: marking a message unread and then flagging it are two changes to two flags, and each still stands.
 *
 * Nothing is kept for an act that files the message, which takes it out of the folder every flag act was asked in, nor
 * across folders, where the earlier act is finished rather than waiting.
 */
function otherFlagAsked(before: AskedAct | undefined, act: MailboxAct, folder: string): AskedAct | undefined {
    const flag = flagWritten(act);

    if (before === undefined || flag === null || before.from !== folder) {
        return undefined;
    }

    const other = [before, before.beside].find((one) => {
        const written = one === undefined ? null : flagWritten(one.act);

        return written !== null && written !== flag;
    });

    return other === undefined ? undefined : alone(other);
}

/**
 * What a message is still asked for once one act's claim is taken back.
 *
 * An act writing one flag answers for that flag alone, so taking it back leaves whatever was asked of the other flag
 * standing — beside it or on its own. A flag act a later act of the same flag already superseded holds no claim of its
 * own to take back, so nothing goes with it. Anything else goes with it, as it always has.
 */
function withoutClaim(asked: AskedAct | undefined, act: MailboxAct): AskedAct | undefined {
    if (asked === undefined) {
        return undefined;
    }

    if (asked.beside?.act === act) {
        return alone(asked);
    }

    if (asked.act === act) {
        return asked.beside;
    }

    return flagWritten(act) !== null && flagWritten(asked.act) !== null ? asked : undefined;
}

/**
 * One batch ready to go out: the messages it carries and how to send it, under the one key it was given.
 *
 * The key is drawn once, when the batch is made, so sending it again is the same request rather than a second one: the
 * deployment answers a repeat under a key it already holds with the first attempt's outcome. The credential is not
 * part of it, because a batch sent again after a renewal goes under the credential the person holds by then.
 */
interface Batch {
    readonly messages: readonly ActedMessage[];
    readonly send: (session: ClientSession) => Promise<ClientResult<readonly MailMutationResult[]>>;
}

/**
 * One batch as it went out: the messages it carried, beside what the deployment answered about them.
 *
 * Paired rather than answered alone, because a submission over the bound is several batches and they do not answer
 * together — one may be written down while the next never reaches the deployment at all. Only the pairing says which
 * messages that failure was about, and everything the screen reports afterwards turns on the difference.
 */
interface Submitted extends Batch {
    readonly answer: ClientResult<readonly MailMutationResult[]>;
}

/** Sends every batch under the credential named, each answered beside what it carried. */
function sent(batches: readonly Batch[], session: ClientSession): Promise<readonly Submitted[]> {
    return Promise.all(batches.map((batch) => batch.send(session).then((answer) => ({ ...batch, answer }))));
}

/** Makes the batches the submission bound admits, each sent with a key of its own. */
function batched<T>(
    items: readonly T[],
    messages: readonly ActedMessage[],
    send: (
        session: ClientSession,
        items: readonly T[],
        requestId: string,
    ) => Promise<ClientResult<readonly MailMutationResult[]>>,
): readonly Batch[] {
    const batches: Batch[] = [];

    for (let from = 0; from < items.length; from += mostMessagesPerMutation) {
        const carried = items.slice(from, from + mostMessagesPerMutation);
        const requestId = newIdentifier();

        batches.push({
            messages: messages.slice(from, from + mostMessagesPerMutation),
            send: (session) => send(session, carried, requestId),
        });
    }

    return batches;
}

/** The messages of every batch nobody answered, whose outcome is therefore not known. */
function unansweredIn(answered: readonly Submitted[]): ReadonlySet<string> {
    return new Set(
        answered
            .filter(({ answer }) => answer.outcome === 'failed' && wentUnanswered(answer.failure))
            .flatMap(({ messages }) => messages.map((message) => message.storedEmailId)),
    );
}

export function MailboxActsProvider({
    session,
    signedInAs,
    transport,
    online,
    flags,
    moves,
    deletes,
    composes,
    children,
}: {
    /** The credential an act goes out under now, or `null` where there is nobody to act for. */
    readonly session: ClientSession | null;

    /** Who is signed in and where, which is whose acts these are, or `null` where there is nobody. */
    readonly signedInAs: string | null;

    readonly transport: MailFathomTransport;
    readonly online: boolean;

    /** Whether this credential may write the two flags a mail server keeps. */
    readonly flags: boolean;

    /** Whether this credential may file mail in another folder, which is a grant of its own. */
    readonly moves: boolean;

    /** Whether this credential may delete mail, which is the grant with no way back. */
    readonly deletes: boolean;

    /**
     * Whether this credential may write drafts, which makes a message in a drafts folder one to carry on writing.
     *
     * It is not an act on the mailbox, so it offers none of the acts above. What it needs from here is which role a
     * message's folder plays, and that is the same folders read the acts name their destinations from.
     */
    readonly composes: boolean;

    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const status = useConnectionStatus();
    const { disconnectedNow } = status;
    const lostServer = disconnected(status);
    const telemetry = useTelemetry();
    const pending = usePendingChanges();
    const marking = useReadMarking();
    const foldersChanged = useFolderMaintenance().changed;
    const [kept, setKept] = useState<Held>(heldForNobody);

    // How many times the folders have been asked for, which is what the second attempt is: the read is an effect, so
    // asking again is a value it depends on rather than a call from the toast that offered it.
    const [attempts, setAttempts] = useState(0);
    const [carried, carry] = useState<readonly ActedMessage[]>([]);

    // A drag ends wherever it ends, and the row it started on is not always there to say so: the list draws a window of
    // rows, so a scroll during the drag can take the row out of the document, and a platform dispatches the drag's end
    // to the row it started on or nowhere. So the end is heard on the window — where it arrives while that row is still
    // drawn — and from the first pointer movement after it, which a drag in flight never reports.
    const carrying = carried.length > 0;

    useEffect(() => {
        if (!carrying) {
            return undefined;
        }

        function ended(): void {
            carry([]);
        }

        window.addEventListener('dragend', ended);
        window.addEventListener('pointermove', ended);

        return () => {
            window.removeEventListener('dragend', ended);
            window.removeEventListener('pointermove', ended);
        };
    }, [carrying]);

    // Derived rather than cleared, for the reason `readMarking/ReadMarking.tsx` gives: signing out and back in on one
    // tab keeps this component mounted, and the previous person's pending acts would otherwise be drawn over the next
    // person's mail — and their folders read as this one's.
    const held = kept.signedInAs === signedInAs ? kept : heldForNobody;

    // Who is signed in now, and the credential they hold, for the reason `readMarking/ReadMarking.tsx` gives about its
    // own: an act answers, and a question the queue holds about it is answered, after the render that asked has gone —
    // and neither may be acted on for a person who has since left. What goes out goes under the credential read here at
    // that moment rather than the one the render held, so a renewal in between sends under the renewed one. Nothing
    // reads either while rendering, so neither drives anything on the screen.
    const signedIn = useRef(session);
    const person = useRef(signedInAs);

    useEffect(() => {
        signedIn.current = session;
        person.current = signedInAs;
    }, [session, signedInAs]);

    const spam = useSpamMarks({ session, transport, signedIn, directory: held.directory, folderRoleOf, counted });

    // The folders, because three of the acts are folder moves and none of them can name a destination without
    // them. Read where the credential may file mail or delete it: without either grant those three acts are refused
    // before a destination is looked for, so asking would be a request every session pays for and no screen reads.
    // Deleting needs them for a second reason — which folder an account calls its trash is what says whether *delete*
    // files a message or destroys it. Writing drafts needs them for a third: which folder an account calls its drafts
    // is what says whether opening a message carries it on in the composer or reads it in the pane. Marking spam needs
    // them for a fourth: whether a message sits in the Spam folder, and whether its account moves spam, decide whether
    // *Not spam* is asked first.
    //
    // It is a read of its own rather than the tree's, which the mailbox column performs for what it draws: the two
    // answer the same route and neither is derived from the other, so a shared read would be one more thing to own than
    // either surface needs today.
    //
    // A second reader of `/folders`. One read the whole client shares is the upgrade, and the moment to take
    // it is when a third surface needs the tree.
    //
    // A read that does not answer is said rather than dropped, and it is said once with the way out on it: without the
    // folders the three acts that file a message are refused as `foldersUnknown`, which is a sentence nobody can act on
    // unless the client offers them the second attempt.
    //
    // A folder made, renamed, moved, or deleted here reads them again, for the reason the tree does: the destinations
    // this offers are those folders, and a folder somebody has just made is one they mean to file into at once.
    useEffect(() => {
        if (
            session === null ||
            signedInAs === null ||
            !online ||
            lostServer ||
            !(flags || moves || deletes || composes)
        ) {
            return;
        }

        let listening = true;

        void readMailFolders(session, transport).then((answer) => {
            if (!listening) {
                return;
            }

            if (answer.outcome === 'read') {
                setKept((current) => ({
                    signedInAs,
                    directory: answer.value,
                    asked: current.signedInAs === signedInAs ? current.asked : new Map(),
                }));

                return;
            }

            // A server out of reach is the status's to say, and the folders are read again the moment it answers.
            if (saidByTheStatus(disconnectedNow, answer.failure.reason)) {
                return;
            }

            toasts.raise({
                kind: 'warning',
                title: translate('act.foldersNotRead', {
                    reason: translate(failureLabels[answer.failure.reason]),
                }),
                action: {
                    label: translate('act.readFoldersAgain'),
                    take: () => {
                        setAttempts((made) => made + 1);
                    },
                },
            });
        });

        return () => {
            listening = false;
        };
    }, [
        session,
        signedInAs,
        transport,
        online,
        lostServer,
        flags,
        moves,
        deletes,
        composes,
        attempts,
        foldersChanged,
        toasts,
        translate,
        disconnectedNow,
    ]);

    // An act with no route to the server is refused before it is pressed, as ADR 0028 asks of a client that cannot
    // reach its deployment, rather than accepted and then reported as a write whose outcome is unknown. That is left to
    // the few seconds before a lost connection is noticed.
    function refusalOf(act: MailboxAct, messages: readonly ActedMessage[]): ActRefusal | null {
        // A refusal the grant or the folders make stands whatever the connection: an act that is not there is not
        // drawn back in by the server going quiet, and its reason is not replaced by a passing one. Folders nobody
        // could read are the exception, since what stands in the way of reading them is the connection itself.
        const refusal = refusalFor(act, messages, held.directory, { flags, moves, deletes });
        const passing = refusal === null || refusal === 'foldersUnknown';

        return passing && lostServer && messages.length > 0 ? 'noConnection' : refusal;
    }

    /** Whether asking to delete these messages destroys them, which is true where every one is already in the trash. */
    function destroys(messages: readonly ActedMessage[]): boolean {
        return deletesPermanently(held.directory, messages);
    }

    /**
     * Writes down what was asked for, so the rows say so from the press rather than from the next read of the folder.
     *
     * The folder each message was in is written down with it, because an act is about a message *in a place*: it is
     * what the sentence a row wears is drawn against, and what says the row is to leave this list and no other.
     */
    function remember(
        act: MailboxAct,
        messages: readonly ActedMessage[],
        leaves: boolean,
        destroys: boolean,
        awaiting = true,
    ): void {
        setKept((current) => {
            const asked = new Map(current.signedInAs === signedInAs ? current.asked : []);

            for (const message of messages) {
                const beside = otherFlagAsked(asked.get(message.storedEmailId), act, message.folder);

                asked.set(message.storedEmailId, {
                    act,
                    from: message.folder,
                    leaves,
                    destroys,
                    awaiting,
                    ...(beside === undefined ? {} : { beside }),
                });
            }

            return { signedInAs, directory: current.signedInAs === signedInAs ? current.directory : null, asked };
        });
    }

    /**
     * Takes back what a message was asked for, which is what an act the deployment did not write down leaves behind.
     *
     * Only that act's claim: where the message is also waiting on the other flag, that act was written down on its own
     * and still stands.
     */
    function forget(storedEmailIds: readonly string[], act: MailboxAct): void {
        setKept((current) => {
            const asked = new Map(current.asked);

            for (const storedEmailId of storedEmailIds) {
                const left = withoutClaim(asked.get(storedEmailId), act);

                if (left === undefined) {
                    asked.delete(storedEmailId);
                } else {
                    asked.set(storedEmailId, left);
                }
            }

            return { ...current, asked };
        });
    }

    /**
     * Marks what the deployment answered for, so the act is no longer under way, and stamps when it was made where the
     * deployment made it, which is what lets the next listing replace the claim.
     */
    function markAnswered(act: MailboxAct, batches: readonly Submitted[]): void {
        const now = Date.now();
        const results = batches.flatMap(({ answer }) => (answer.outcome === 'read' ? answer.value : []));

        if (results.length === 0) {
            return;
        }

        setKept((current) => {
            const asked = new Map(current.asked);

            for (const result of results) {
                const claim = asked.get(result.storedEmailId);

                if (claim !== undefined) {
                    asked.set(
                        result.storedEmailId,
                        answeredClaim(claim, act, result.outcome === 'applied' ? now : null),
                    );
                }
            }

            return { ...current, asked };
        });
    }

    // A page drawn after the deployment made a change is what the server holds, so what this client claimed about a
    // message on it is let go. Read when the page lands rather than captured, so the listener is one function for the
    // list's life.
    const settleDrawn = useEffectEvent((emails: readonly DrawnMail[], askedAt: number) => {
        setKept((current) => {
            let changed = false;
            const asked = new Map(current.asked);

            for (const email of emails) {
                const claim = asked.get(email.id);
                const left = claim === undefined ? undefined : afterDrawn(claim, askedAt);

                if (claim !== undefined && left !== claim) {
                    changed = true;

                    if (left === undefined) {
                        asked.delete(email.id);
                    } else {
                        asked.set(email.id, left);
                    }
                }
            }

            return changed ? { ...current, asked } : current;
        });
    });

    const listed = useListedMail();

    useEffect(
        () =>
            listed.observe((emails, askedAt) => {
                settleDrawn(emails, askedAt);
            }),
        [listed],
    );

    function counted(messages: number): string {
        return translate(messagesCounted[new Intl.PluralRules(locale).select(messages)], {
            count: new Intl.NumberFormat(locale).format(messages),
        });
    }

    /** Makes one act into the batches the submission bound admits, each to be sent under a key of its own. */
    function batchesFor(
        act: MailboxAct,
        messages: readonly ActedMessage[],
        destination: MoveDestination | undefined,
        destroying: boolean,
    ): readonly Batch[] {
        // A delete in the trash files nothing, so it names no destination and takes the route of its own rather than
        // the move route with the folder the message is already in.
        if (destroying) {
            return batched(
                messages.map((message) => message.storedEmailId),
                messages,
                (asking, storedEmailIds, requestId) => deleteMail(asking, transport, storedEmailIds, requestId),
            );
        }

        if (changesAFlag(act)) {
            return batched(
                messages.map((message): MailFlagChange =>
                    act === 'markUnread' || act === 'markRead'
                        ? { storedEmailId: message.storedEmailId, seen: act === 'markRead' }
                        : { storedEmailId: message.storedEmailId, flagged: act === 'flag' },
                ),
                messages,
                (asking, changes, requestId) => changeMailFlags(asking, transport, changes, requestId),
            );
        }

        const filing = filingFor(act, messages, held.directory, destination?.folderId ?? null);
        const filed = new Set(filing.map((one) => one.storedEmailId));

        // `filingFor` keeps the order it was given and drops only a message the act files nowhere, so the messages
        // below and the filings above are one list read twice rather than two lists that have to agree. Split rather
        // than truncated: the route refuses a longer batch whole, so a message silently dropped here would be a row
        // drawn as filed against a mailbox nobody told.
        return batched(
            filing,
            messages.filter((message) => filed.has(message.storedEmailId)),
            (asking, filings, requestId) => moveMail(asking, transport, filings, requestId),
        );
    }

    /**
     * Reports what an act was written down for, and offers the way back where the act has one.
     *
     * Only the half the queue has no notion of. A batch that was written down is reported as written down however the
     * batch beside it ended, because what the deployment holds does not turn on what it was asked next — and what it
     * refused, or never answered at all, is the queue's to say, which `handOver` gives it.
     *
     * **An act that writes a flag reports nothing at all**, for the reason stated beside `actReported`: what it did is
     * the mark the row is already drawing. The queue still follows it, so a refusal is still said — what is silent is
     * the success.
     */
    function report(
        act: MailboxAct,
        recorded: readonly ActedMessage[],
        destination: MoveDestination | undefined,
        destroying: boolean,
    ): void {
        if (recorded.length === 0 || changesAFlag(act)) {
            return;
        }

        // Where the act filed each message, read now rather than when the way back is taken: that is the folder the
        // message stands in until then, and the folders may be read again in between.
        const filedIn = new Map(
            filingFor(act, recorded, held.directory, destination?.folderId ?? null).map((filing) => [
                filing.storedEmailId,
                filing.destinationFolder,
            ]),
        );

        toasts.raise(
            // A delete that destroys the mail offers nothing to take back, which is the design's own: the
            // question in front of it has already said it cannot be undone.
            destroying
                ? { kind: 'neutral', title: translate('act.purging'), body: counted(recorded.length) }
                : {
                      kind: 'neutral',
                      title: translate(actReported[act], {
                          folder: destination === undefined ? '' : destinationName(destination, translate),
                      }),
                      body: counted(recorded.length),

                      // The way back is the toast's single action, which is the design's own — and it is left off
                      // where it would be a move the deployment refuses, which is a draft or a sent copy deleted out
                      // of its tree.
                      ...(mayBeTakenBack(held.directory, recorded, filedIn) && {
                          action: {
                              label: translate('act.undo'),
                              take: () => {
                                  takeBack(act, recorded, filedIn);
                              },
                          },
                      }),
                  },
        );
    }

    /**
     * Hands the queue what an act's batches came to, which is where a refusal is said and a batch nobody answered is
     * held to be sent again.
     *
     * Two submissions at most rather than one per batch: the batches that answered are one answer, so a reason is said
     * once however many batches it happened in, and the batches that never reached the deployment are one silence. The
     * ways out are the producer's, because only it knows what performing its act afresh, sending the same request again,
     * or no longer claiming it, means — and none is taken unless the person the act was performed for is still the one
     * signed in, under whichever credential they hold by then.
     *
     * @param resend Sends the batches named again, exactly as they went, under the credential named, and settles their
     * answer as this one was.
     * @param resent Whether these batches were already sent again once, which says nothing the first silence said.
     */
    function handOver(
        askingFor: string,
        act: ChangeAct,
        answered: readonly Submitted[],
        askAgain: (messages: readonly ActedMessage[]) => void,
        letGo: (storedEmailIds: readonly string[]) => void,
        resend: (batches: readonly Batch[], session: ClientSession) => void,
        resent: boolean,
    ): void {
        function submission(
            batches: readonly Submitted[],
            results: readonly MailMutationResult[] | null,
            failure: ClientFailure | null,
        ): ChangeSubmission {
            const carried = batches.flatMap(({ messages }) => messages);

            return {
                act,
                asked: carried.map((message) => message.storedEmailId),
                results,
                failure,
                resent,
                askAgain: (storedEmailIds) => {
                    const named = new Set(storedEmailIds);

                    if (person.current === askingFor) {
                        askAgain(carried.filter((message) => named.has(message.storedEmailId)));
                    }
                },
                sendAgain: () => {
                    const now = signedIn.current;

                    if (now !== null && person.current === askingFor) {
                        resend(batches, now);
                    }
                },
                letGo: (storedEmailIds) => {
                    if (person.current === askingFor) {
                        letGo(storedEmailIds);
                    }
                },
            };
        }

        const reached = answered.filter(({ answer }) => answer.outcome === 'read');

        if (reached.length > 0) {
            pending.follow(
                submission(
                    reached,
                    reached.flatMap(({ answer }) => (answer.outcome === 'read' ? answer.value : [])),
                    null,
                ),
            );
        }

        // The batches nobody answered are one silence, and those the server refused are one refusal per reason it
        // gave: a refusal says nothing was written down, while silence leaves the outcome unknown.
        const lost = new Map<ClientFailureReason | null, { failure: ClientFailure | null; batches: Submitted[] }>();

        for (const batch of answered) {
            if (batch.answer.outcome === 'failed') {
                const failure = wentUnanswered(batch.answer.failure) ? null : batch.answer.failure;
                const reason = failure?.reason ?? null;
                const gathered = lost.get(reason) ?? { failure, batches: [] };

                lost.set(reason, { failure: gathered.failure, batches: [...gathered.batches, batch] });
            }
        }

        for (const { failure, batches } of lost.values()) {
            pending.follow(submission(batches, null, failure));
        }
    }

    /** Splits records into the batches the release route admits, which are bounded exactly as a submission is. */
    function batchesOf(records: readonly string[]): readonly (readonly string[])[] {
        const batches: (readonly string[])[] = [];

        for (let from = 0; from < records.length; from += mostMessagesPerMutation) {
            batches.push(records.slice(from, from + mostMessagesPerMutation));
        }

        return batches;
    }

    /**
     * Tells the deployment it need not hold a permanent delete back, since nobody is being offered a way to take it back.
     *
     * The deployment holds every delete it writes down for a window of its own, which is there for a client that offers
     * a way back. This one offers none, so waiting it out would only keep a message somebody destroyed on the mail
     * server for longer than they were told.
     */
    function release(records: readonly string[]): void {
        const asking = signedIn.current;

        if (asking === null) {
            return;
        }

        for (const batch of batchesOf(records)) {
            // Nothing is reported and nothing is waited for: what this asks for is what would have happened anyway
            // once the window ran out, so a request that never arrives costs the delete a wait rather than an outcome.
            void releaseMailDeletes(asking, transport, batch);
        }
    }

    /**
     * Files the named messages back where each of them was, which is what taking a move back is.
     *
     * The reverse mutation rather than a withdrawal of the first: what was asked for may already be on its way to a
     * mail server, and a screen that merely stopped saying so would leave the mailbox somewhere the reader was told it
     * was not.
     *
     * It is claimed from the press exactly as the act was, against the folder the act filed each message in: that is
     * the list a reader who opened the destination is looking at, and a row the deployment last listed there would
     * otherwise stay drawn in it while the message is drawn back where it came from — one message in two places.
     *
     * @param act The act being taken back, whose claim stands again for every message the way back was not written for.
     * @param filedIn The folder the act filed each message in, by the message.
     */
    function takeBack(act: MailboxAct, messages: readonly ActedMessage[], filedIn: ReadonlyMap<string, string>): void {
        const asking = signedIn.current;
        const askingFor = person.current;

        // Read at the press rather than off the render that offered the way back: a toast outlives renewals, and one
        // offered to somebody who has since signed out is not the next person's to take.
        if (asking === null || askingFor === null || askingFor !== signedInAs || !moves) {
            return;
        }

        const answeredFor: string = askingFor;

        /** The act's own claim, standing again over messages that are still where it put them. */
        function stillFiled(storedEmailIds: readonly string[]): void {
            const named = new Set(storedEmailIds);

            // Standing again rather than asked again: the act was answered, so it is not under way.
            remember(
                act,
                messages.filter((message) => named.has(message.storedEmailId)),
                true,
                false,
                false,
            );
        }

        function settleBack(back: readonly Submitted[], resent: boolean): void {
            if (person.current !== answeredFor) {
                return;
            }

            // Each message's own answer, exactly as the act itself is read: a mailbox that moved on between the act
            // and the press has messages the reverse move cannot write down either, and a row whose way back was not
            // recorded is still where the act put it — so it goes on saying so rather than being drawn back on the
            // strength of a batch that answered for something else.
            const carried = back.flatMap((batch) => batch.messages);
            const written = writtenDown(back);
            const returned = carried.filter((message) => written.has(message.storedEmailId));

            // A way back nobody answered is not known to have failed, so the act it reverses is not drawn again over it.
            const unknown = unansweredIn(back);

            stillFiled(
                carried
                    .filter((message) => !written.has(message.storedEmailId) && !unknown.has(message.storedEmailId))
                    .map((one) => one.storedEmailId),
            );
            markAnswered('move', back);

            if (returned.length > 0) {
                toasts.raise({ kind: 'neutral', title: translate('act.undone'), body: counted(returned.length) });
            }

            // Handed over like any act, because it is one. Letting one go hands the message back to the act it
            // reversed, which still stands for it.
            handOver(
                answeredFor,
                'putBack',
                back,
                (again) => {
                    takeBack(act, again, filedIn);
                },
                stillFiled,
                (lost, now) => {
                    void sent(lost, now).then((again) => {
                        settleBack(again, true);
                    });
                },
                resent,
            );
        }

        remember(
            'move',
            messages.map((message) => ({ ...message, folder: filedIn.get(message.storedEmailId) ?? message.folder })),
            true,
            false,
        );

        const back = batched(
            messages.map((message) => ({ storedEmailId: message.storedEmailId, destinationFolder: message.folder })),
            messages,
            (sending, filings, requestId) => moveMail(sending, transport, filings, requestId),
        );

        void sent(back, asking).then((answer) => {
            settleBack(answer, false);
        });
    }

    // Which role a message's own folder plays, read out of the same tree the destinations are read from.
    function folderOf(message: Pick<ActedMessage, 'account' | 'folder'>): MailFolder | null {
        return (
            held.directory?.accounts
                .find((entry) => entry.account.id === message.account)
                ?.folders.find((folder) => folder.id === message.folder) ?? null
        );
    }

    function folderRoleOf(message: ActedMessage): MailFolderRole | null {
        return folderOf(message)?.role ?? null;
    }

    function perform(act: MailboxAct, messages: readonly ActedMessage[], destination?: MoveDestination): void {
        if (act === 'move' && destination !== undefined && filesIntoTrash(destination)) {
            perform('delete', messages);

            return;
        }

        const asking = signedIn.current;
        const askingFor = person.current;

        // Read at the press rather than off the render that offered the control, for the reason `takeBack` gives: an act
        // asked again from a toast after a renewal goes under the renewed credential, and is answered as its person's.
        if (asking === null || askingFor === null || askingFor !== signedInAs || refusalOf(act, messages) !== null) {
            return;
        }

        // Read before anything is submitted, so the act reported afterwards is the act that went out: the folders could
        // be re-read while the batch is in flight, and a message that left the trash in between must not turn a delete
        // somebody was told was permanent into one reported as a move.
        const destroying = act === 'delete' && destroys(messages);

        // Whether the message is leaving the list it was acted in, which every act that files it does — a delete that
        // destroys the mail included, since nothing is offered that would bring it back.
        const leaves = !changesAFlag(act);

        // The act and how many messages it was asked over. Which messages is a list of stored identities and is not
        // written down; how many is what separates somebody pressing archive on one row from a select-all across two
        // hundred, which is the difference an operator reading a mail server complaining about write volume needs.
        telemetry.happened('act_asked', {
            'mailfathom.client.act': act,
            'mailfathom.client.messages': messages.length,
        });

        remember(act, messages, leaves, destroying);

        void sent(batchesFor(act, messages, destination, destroying), asking).then((answer) => {
            settle(askingFor, act, messages, answer, destination, destroying, false);
        });
    }

    /**
     * Reads what an act's batches came to: what stays drawn, what is reported, and what the queue is handed.
     *
     * @param messages Every message the act was asked over, including any it filed nowhere and so sent in no batch.
     * @param resent Whether these batches were sent again after nobody answered them, which `handOver` passes on.
     */
    function settle(
        askingFor: string,
        act: MailboxAct,
        messages: readonly ActedMessage[],
        answered: readonly Submitted[],
        destination: MoveDestination | undefined,
        destroying: boolean,
        resent: boolean,
    ): void {
        // An answer arriving after somebody else has signed in is neither theirs to be told about nor their queue's to
        // follow.
        if (person.current !== askingFor) {
            return;
        }

        // A batch that was written down stands whatever the batch beside it came to: two hundred messages the
        // deployment holds are two hundred messages it holds, and forgetting them because the next batch never
        // reached it would leave every one of those rows saying nothing while the mailbox says otherwise.
        const written = writtenDown(answered);
        const recorded = messages.filter((message) => written.has(message.storedEmailId));

        // A message the deployment has written down as unread is no longer one this client marked read by opening it,
        // so that marking stops standing over what the deployment reports. Left in place, it would draw the row read
        // and count it read however many times the deployment answered otherwise.
        if (act === 'markUnread') {
            marking.forget(recorded.map((message) => message.storedEmailId));
        }

        // Everything that was not written down stops being claimed here, rather than only where the queue lets go of
        // it: a message already where it was asked to go is a refusal nobody is told about, and an act filing it into
        // the folder it is already in has not taken it out of the list it is drawn in either. A batch nobody answered
        // is the exception: its outcome is not known, and it most likely landed, so it stays drawn as asked, and under
        // way, until it is sent again and answered.
        const unknown = unansweredIn(answered);

        forget(
            messages
                .filter((message) => !written.has(message.storedEmailId) && !unknown.has(message.storedEmailId))
                .map((one) => one.storedEmailId),
            act,
        );
        markAnswered(act, answered);

        // What this adds to what the deployment already wrote down is the screen: it had drawn the act as done and has
        // just put itself back, which somebody using it experiences as the client undoing their work. That is a defect
        // report against this client rather than something whoever reads a collector acts on, and the refusal itself
        // is the deployment's own answer read back to it — so it is the client's account of what it did and sits at
        // the level the rest of that account does.
        //
        // Counted over the batches the deployment actually answered, and never over one that failed to reach it. A
        // dropped connection or a credential that expired leaves `writtenDown` with nothing from that batch, which is
        // indistinguishable here from a deployment declining every message in it — so counting both would put every
        // transport failure into the one record that says a deployment declined a write it had answered.
        // `request_failed` already reports that half, under the name a reader groups it by.
        const declined = answered
            .filter(({ answer }) => answer.outcome === 'read')
            .flatMap(({ messages: batch }) => batch)
            .filter((message) => !written.has(message.storedEmailId));

        if (declined.length > 0) {
            telemetry.happened('act_refused', {
                'mailfathom.client.act': act,
                'mailfathom.client.messages': declined.length,
            });
        }

        if (destroying) {
            const applied = appliedTo(answered);
            const filed = recorded.filter((message) => applied.has(message.storedEmailId));

            // A message the deployment filed into the trash rather than holding a record for is not going anywhere, so
            // it stops being claimed as destroyed and is reported as the delete into the trash it was.
            forget(
                filed.map((message) => message.storedEmailId),
                act,
            );
            report(act, filed, destination, false);
            report(
                act,
                recorded.filter((message) => !applied.has(message.storedEmailId)),
                destination,
                true,
            );
            release(recordsWritten(answered));
        } else {
            report(act, recorded, destination, destroying);
        }

        // A move into the Spam folder is the same act as pressing *Spam*: the deployment recorded the mark with the
        // move, so what is left is to draw it and ask for the kind.
        if (act === 'move' && destination?.role === 'Junk') {
            spam.filedIntoJunk(recorded);
        }

        // Asking again is the same act performed afresh over the same messages, naming the folder a move named, so it
        // travels the path the first attempt took and is answered again on its own. The claim the first attempt left
        // is taken back before it goes: an act the folders no longer allow is refused before anything is submitted,
        // and a row must not go on saying the first attempt is still on its way. Sending again is the other way out:
        // the very batches nobody answered, under the keys they first went with, settled here once they answer.
        handOver(
            askingFor,
            act,
            answered,
            (again) => {
                forget(
                    again.map((message) => message.storedEmailId),
                    act,
                );
                perform(act, again, destination);
            },
            (storedEmailIds) => {
                forget(storedEmailIds, act);
            },
            (lost, now) => {
                void sent(lost, now).then((again) => {
                    settle(
                        askingFor,
                        act,
                        lost.flatMap((batch) => batch.messages),
                        again,
                        destination,
                        destroying,
                        true,
                    );
                });
            },
            resent,
        );
    }

    // A credential that may act on no mail is still told which folder a message stands in, because a draft is carried on
    // in the composer by whoever may write one, whether or not they may also move it.
    const acts: MailboxActs =
        session === null
            ? nothingActed
            : !(flags || moves || deletes)
              ? { ...nothingActed, folderRoleOf, folderOf }
              : {
                    asked: held.asked,
                    refusalOf,
                    folderRoleOf,
                    folderOf,
                    destinationsOf: (messages) => destinationsFor(held.directory, messages),
                    deletesPermanently: destroys,
                    perform,
                    carried,
                    carry,
                    spamOffered: flags,
                    spamMarked: spam.marked,
                    toggleSpam: spam.toggle,
                    notSpamMoves: spam.notSpamMoves,
                };

    return (
        <MailboxActsContext value={acts}>
            {children}
            {session === null ? null : spam.questions}
        </MailboxActsContext>
    );
}
