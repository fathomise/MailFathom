// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    discardMailDraft,
    readMailAttachment,
    reviseMailDraft,
    sendMailDraft,
    stageMailDraftAttachment,
    unstageMailDraftAttachment,
    withdrawOutgoingMail,
    wentUnanswered,
    writeMailDraft,
    type ClientFailure,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailAttachment,
    type MailDraft,
    type MailFathomTransport,
    type MailSendRefusal,
    type MailSendWithdrawal,
} from '@mailfathom/client-backend';
import { takingFailureOf, useAttachmentExchange } from '../deployment/attachmentExchange';
import { useAttachmentUpload } from '../deployment/attachmentUpload';
import { useTelemetry } from '../telemetry/clientTelemetry';
import { wireComposition, type Composition } from './composition';

// What the deployment holds for the message being written, and the five acts that change it. It is a hook rather than
// state inside the composer because none of it is what is on the screen: the draft in the user's own drafts folder,
// the files staged against it, and what became of a send are all the deployment's, and each act is a sequence with an
// outcome rather than a value to render.
//
// **Two acts file the message and no others do**: saving it, and sending it. Each writes the draft the deployment
// holds and then puts up whatever files the author chose that have not reached it yet. Choosing a file is not one of
// them — a file somebody picked is part of what they are writing, exactly as the words are, so it is held here until
// one of those two acts says to file the message. That is what keeps attaching offered on a deployment nobody has
// needed yet, and it is what leaves nothing behind for closing the composer to have to delete.

// Which act asked for the write, which is what the refusal it meets is worded as. Only two words exist because only
// two are true of the person: they pressed save, or they pressed send.
type DraftAct = 'save' | 'send';

/** What the composer is doing about the deployment, which is one piece of state rather than a set of flags. */
export type DraftStanding =
    | { readonly kind: 'held' }
    | { readonly kind: 'saving' }
    | { readonly kind: 'saved' }
    | { readonly kind: 'attaching'; readonly fileName: string }
    | { readonly kind: 'carrying'; readonly fileName: string }
    | { readonly kind: 'notCarried'; readonly fileNames: readonly string[] }
    | { readonly kind: 'sending' }
    | { readonly kind: 'queued'; readonly outgoingEmailId: string }
    | { readonly kind: 'withdrawn'; readonly withdrawal: MailSendWithdrawal }
    | { readonly kind: 'refused'; readonly refusal: MailSendRefusal }
    | { readonly kind: 'refusedSave'; readonly refusal: MailSendRefusal }

    /**
     * An act that failed, and whether it failed because nothing answered — silence, or a gateway's `502`, `503`, or
     * `504` — which leaves what it did unknown rather than undone: a save may have been written, a file staged.
     */
    | { readonly kind: 'failed'; readonly reason: ClientFailureReason; readonly unanswered: boolean }

    /**
     * The send itself went out and nothing answered it, so whether the message is on its way is not known. It is the one
     * failure of a send that may already have queued the message, which is why it is not `failed`: asking again would
     * send it twice where the first one landed, so what is said instead points at the Outbox, which knows.
     */
    | { readonly kind: 'sendUnknown' };

/**
 * One file the message carries, as the composer draws it.
 *
 * It says nothing about whether the deployment has it yet, because nothing on the screen says that either: a file is
 * part of the message from the moment it was chosen, and where it currently sits is this hook's business.
 */
export interface AttachedFile {
    /** This composer's own name for it, which never changes once the file is chosen. */
    readonly attachmentId: string;

    readonly fileName: string;
    readonly sizeOctets: number;
}

/**
 * One file as this hook holds it: the file itself where it was chosen here, and what the deployment calls it once it
 * has it. A file a kept draft already carried has no bytes on this machine at all — the deployment has it, which is
 * all a save and a send need.
 */
interface HeldFile {
    readonly attachmentId: string;
    readonly fileName: string;
    readonly sizeOctets: number;
    readonly file: File | null;

    /** What the deployment named it when it was staged, or `null` while the deployment does not have it. */
    readonly stagedAs: string | null;
}

/** A file chosen here that the deployment does not have yet, which is the one kind a save puts up. */
type ChosenFile = HeldFile & { readonly file: File };

/** The draft the deployment holds for what is being written, and what a person does to it. */
export interface DraftAtDeployment {
    readonly standing: DraftStanding;

    /** The files the message carries, oldest first, which is nothing until one is chosen. */
    readonly attached: readonly AttachedFile[];

    /** The record the deployment last answered for this draft — the one it was opened on, then each save's — or none. */
    readonly record: MailDraft | null;

    /** Files the draft in the user's own drafts folder, putting up whatever was attached and not yet staged. */
    readonly save: (composition: Composition) => Promise<boolean>;

    /** Takes a chosen file into the message. Nothing leaves the client: a save or a send is what puts it up. */
    readonly attach: (file: File) => void;

    /** Takes one file back off, and off the deployment as well where it had already reached it. */
    readonly detach: (attachmentId: string) => Promise<void>;

    /**
     * Takes the files a draft in a drafts folder was filed with into the message carrying it on.
     *
     * A save writes a draft of its own rather than taking the stored one over, so a file the draft carried would be
     * left behind unless it is chosen again — here, from the deployment, exactly as if its author had chosen it from
     * their machine. A file that could not be brought over is said by name rather than dropped quietly.
     */
    readonly carry: (storedEmailId: string, attachments: readonly MailAttachment[]) => Promise<void>;

    /**
     * Carries on with a draft MailFathom keeps, as that same draft: every later save revises it, and the files staged
     * against it stay where they are rather than being chosen again.
     */
    readonly adopt: (draft: MailDraft) => void;

    /**
     * Queues the message, saving what has been written since first, and answers what it came to.
     *
     * The standing is answered as well as held because the surface says what became of a send where the design
     * says it — in a toast, which stands over whatever the person moved on to rather than at the foot of the composer.
     * A toast is raised at the moment of the outcome rather than watched for, so what is owed to the caller is the
     * outcome itself; reading it back out of state afterwards would raise a second toast on every later render.
     */
    readonly send: (composition: Composition) => Promise<DraftStanding>;

    /**
     * Takes a queued send back while it has not begun transmitting, and answers what that came to.
     *
     * Asked before the deployment has answered the send, it withdraws the message the moment there is one to withdraw
     * rather than doing nothing: somebody who said stop while it was still going out said it about this message, and
     * asking them to say it again once a round trip has landed is a stop the client quietly dropped.
     */
    readonly withdraw: () => Promise<DraftStanding>;

    /**
     * Gives the draft up, taking its copies back out of the user's drafts folder.
     *
     * It reaches the deployment only where the deployment is actually holding something, which after the rule at the
     * head of this file is a send that was refused or failed. Closing a composer nothing was filed for asks nothing of
     * anybody.
     */
    readonly discard: () => Promise<boolean>;
}

export function useDraftAtDeployment(session: ClientSession, transport: MailFathomTransport): DraftAtDeployment {
    const upload = useAttachmentUpload();
    const exchange = useAttachmentExchange();
    const telemetry = useTelemetry();
    const [standing, setStanding] = useState<DraftStanding>({ kind: 'held' });
    const [attached, setAttached] = useState<readonly AttachedFile[]>([]);
    const [record, setRecord] = useState<MailDraft | null>(null);

    // The draft the deployment holds, as a ref rather than as state: two acts in the same turn have to see the
    // identifier the first of them wrote, and nothing on the screen is drawn from it.
    const draftId = useRef<string | null>(null);

    // The files themselves, beside the state that draws them, for the reason the standing is kept twice: a save runs
    // through them one at a time and each round has to see what the round before it recorded, and what somebody took
    // off while a file was going up.
    const held = useRef<readonly HeldFile[]>([]);

    // What this composer calls the next file chosen. A counter rather than a random name because nothing outside this
    // composer ever sees it and a test should not have to fix a generator to read the list.
    const named = useRef(0);

    // The save in flight, if any. `saved` reads `draftId.current` and only writes it back once its own request has
    // answered, so two acts starting inside that window would each write a draft and strand whatever the loser staged
    // against it. A second caller joins the first rather than starting a second write.
    const saving = useRef<Promise<string | null> | null>(null);
    const queued = useRef<string | null>(null);
    const uploading = useRef<AbortController | null>(null);
    const taking = useRef<AbortController | null>(null);

    // The draft whose files were already carried over, so a second read of the same draft brings none over twice.
    const carried = useRef<string | null>(null);

    // The carrying still under way, which every write waits for: a message saved or sent before its draft's files have
    // arrived would leave behind the ones not yet taken, and nothing would say so.
    const carrying = useRef<Promise<void> | null>(null);

    // Whether somebody has asked to stop the send while the deployment had not yet answered it. A ref rather than
    // state because nothing draws it: what reads it is the send itself, one turn later, and a value read out of a
    // render would be the one from before they asked.
    const stopping = useRef(false);

    // What the standing already is, beside the state that draws it. An act answers what it came to, and two acts in
    // one turn — a stop landing on a send, a refusal met while writing the draft a send needed — have to read what the
    // one before them settled on rather than the value this render was given.
    const settledAs = useRef<DraftStanding>({ kind: 'held' });

    function hold(next: DraftStanding): DraftStanding {
        settledAs.current = next;
        setStanding(next);

        return next;
    }

    function holdFiles(next: readonly HeldFile[]): void {
        held.current = next;
        setAttached(next.map(({ attachmentId, fileName, sizeOctets }) => ({ attachmentId, fileName, sizeOctets })));
    }

    // An upload whose composer has gone is an upload nobody is waiting for, and letting it finish would stage a file
    // against a draft the author has closed. A file still being carried over from a draft is the same.
    useEffect(
        () => () => {
            uploading.current?.abort();
            taking.current?.abort();
        },
        [],
    );

    function attach(file: File): void {
        named.current += 1;

        holdFiles([
            ...held.current,
            { attachmentId: String(named.current), fileName: file.name, sizeOctets: file.size, file, stagedAs: null },
        ]);
    }

    function saved(composition: Composition, asked: DraftAct): Promise<string | null> {
        const already = saving.current;

        if (already !== null) {
            return already;
        }

        const writing = write(composition, asked);

        saving.current = writing;

        return writing.finally(() => {
            saving.current = null;
        });
    }

    async function write(composition: Composition, asked: DraftAct): Promise<string | null> {
        if (carrying.current !== null) {
            await carrying.current;
        }

        const draft = draftId.current;
        const wire = wireComposition(composition);

        const answer =
            draft === null
                ? await writeMailDraft(session, transport, wire)
                : await reviseMailDraft(session, transport, draft, wire);

        if (answer.outcome === 'failed') {
            hold(failedOn(answer.failure));

            return null;
        }

        // A refusal is said in the words of the act that met it. The deployment screens the draft book by the same
        // rules as the outbox and answers the same codes, so the same refusal reaches a save — and telling its author
        // the message was not sent, when they pressed save, names an act nobody performed. A send writes the draft
        // before it posts it, so the refusal it meets here is the one it would have met at the send itself, and is
        // worded as the send's.
        if (!answer.value.written) {
            hold(
                asked === 'send'
                    ? { kind: 'refused', refusal: answer.value.refusal }
                    : { kind: 'refusedSave', refusal: answer.value.refusal },
            );

            return null;
        }

        draftId.current = answer.value.draft.draftId;
        setRecord(answer.value.draft);

        return answer.value.draft.draftId;
    }

    /**
     * Puts up every file the deployment does not have yet, oldest first, and answers whether all of them arrived.
     *
     * One at a time, because each is a request of its own against one draft and a failure part way through has to stop
     * the act rather than leave the rest going up behind a message that will not be sent. The list is re-read on every
     * round rather than walked as a snapshot: a file somebody took off while it was going up is taken off the
     * deployment too, so nothing they removed travels with the message.
     */
    async function stageHeld(draft: string): Promise<boolean> {
        for (;;) {
            const next = held.current.find((file): file is ChosenFile => file.stagedAs === null && file.file !== null);

            if (next === undefined) {
                return true;
            }

            hold({ kind: 'attaching', fileName: next.file.name });

            const abandoning = new AbortController();
            uploading.current = abandoning;

            const answer = await stageMailDraftAttachment(
                session,
                draft,
                next.file.name,
                // What the file declares itself to be, which is what the author's own system said. A file the system
                // could not name is the general binary type, which is what the deployment reads a request declaring
                // none as anyway.
                next.file.type === '' ? 'application/octet-stream' : next.file.type,
                (request) => upload(request, next.file, abandoning.signal),
            );

            uploading.current = null;

            if (answer.outcome === 'failed') {
                hold(failedOn(answer.failure));

                return false;
            }

            if (held.current.some((file) => file.attachmentId === next.attachmentId)) {
                holdFiles(
                    held.current.map((file) =>
                        file.attachmentId === next.attachmentId
                            ? { ...file, stagedAs: answer.value.attachmentId }
                            : file,
                    ),
                );
            } else {
                // Taken off while it was going up. The author's act stands, so what arrived is taken back off the
                // deployment rather than left staged against a message that would then carry it — and a removal the
                // deployment refused stops the act, because carrying on would send a file somebody took off.
                const removed = await unstageMailDraftAttachment(session, transport, draft, answer.value.attachmentId);

                if (removed.outcome === 'failed') {
                    hold(failedOn(removed.failure));

                    return false;
                }
            }
        }
    }

    // Every act ends by saying what happened, and a failure says which of the five it was rather than that something
    // went wrong. Stated once here because five acts would otherwise each carry their own copy of the same three lines.
    function settled<TValue>(answer: ClientResult<TValue>, whenRead: (value: TValue) => DraftStanding): DraftStanding {
        return hold(answer.outcome === 'failed' ? failedOn(answer.failure) : whenRead(answer.value));
    }

    async function send(composition: Composition): Promise<DraftStanding> {
        hold({ kind: 'sending' });

        const draft = await saved(composition, 'send');

        if (draft === null) {
            // The write said what stopped it and in the words of the send, so what this answers is that same
            // statement rather than a second, vaguer one made here. A stop asked while it was in flight goes with it:
            // it was asked about a message that never left.
            stopping.current = false;

            return settledAs.current;
        }

        if (!(await stageHeld(draft))) {
            // A message goes out whole or not at all: a file that did not arrive is one the reader would never know
            // was meant to be there, so the send stops on it and the draft stays where the author can try again.
            stopping.current = false;

            return settledAs.current;
        }

        hold({ kind: 'sending' });

        const posted = await sendMailDraft(session, transport, draft);

        // Nothing answered the send itself, so the message may be queued already: it is not said to have failed, and
        // nothing here offers to send it again.
        const outcome =
            posted.outcome === 'failed' && wentUnanswered(posted.failure)
                ? hold({ kind: 'sendUnknown' })
                : settled(posted, (sent) => {
                      if (!sent.queued) {
                          return { kind: 'refused', refusal: sent.refusal };
                      }

                      queued.current = sent.outgoingEmailId;

                      return { kind: 'queued', outgoingEmailId: sent.outgoingEmailId };
                  });

        // A stop belongs to the send it was asked about, so it is read once here and cleared whatever became of that
        // send. Left standing over a send that was refused or failed, it would take back the *next* message somebody
        // deliberately sent — which is a message discarded on state nobody was acting on any more.
        const stopWasAsked = stopping.current;
        stopping.current = false;

        // What became of a send this client asked for, which the deployment's own outbox cannot say: it records what
        // it was handed rather than what somebody wrote and then could not send. The refusal is a name out of the
        // closed set the wire package publishes; no recipient, no subject, and no part of the message reaches this.
        telemetry.happened('message_sent', {
            'mailfathom.client.send': outcome.kind,
            ...(outcome.kind === 'refused' ? { 'mailfathom.client.refusal': outcome.refusal } : {}),
        });

        // Somebody asked to stop while the deployment had not yet answered, so this is where their stop lands.
        if (outcome.kind === 'queued' && stopWasAsked) {
            return withdraw();
        }

        return outcome;
    }

    async function withdraw(): Promise<DraftStanding> {
        const sent = queued.current;

        if (sent === null) {
            stopping.current = true;

            return settledAs.current;
        }

        const taken = settled(await withdrawOutgoingMail(session, transport, sent), (withdrawal) => ({
            kind: 'withdrawn' as const,
            withdrawal,
        }));

        // Somebody changing their mind inside the window the deployment gives them, which is a decision rather than a
        // failure and is why it is written at the client's own account level rather than above it. The deployment's own
        // answer is what is reported rather than the standing this hook composed from it: `withdrawn` is the standing
        // whatever the route said, so a take-back the deployment refused because the message was already going out
        // would otherwise read as one it accepted.
        telemetry.happened('send_withdrawn', {
            'mailfathom.client.withdrawal': taken.kind === 'withdrawn' ? taken.withdrawal : taken.kind,
        });

        return taken;
    }

    return {
        standing,
        attached,
        record,

        save: async (composition) => {
            hold({ kind: 'saving' });

            const draft = await saved(composition, 'save');

            if (draft === null || !(await stageHeld(draft))) {
                return false;
            }

            hold({ kind: 'saved' });

            return true;
        },

        attach,

        adopt: (draft) => {
            draftId.current = draft.draftId;
            setRecord(draft);
            holdFiles(
                draft.attachments.map((attachment) => {
                    named.current += 1;

                    return {
                        attachmentId: String(named.current),
                        fileName: attachment.fileName,
                        sizeOctets: attachment.sizeOctets,
                        file: null,
                        stagedAs: attachment.attachmentId,
                    };
                }),
            );
        },

        carry: async (storedEmailId, attachments) => {
            if (carried.current === storedEmailId) {
                return;
            }

            carried.current = storedEmailId;

            // Said only over a composer that is otherwise at rest: a save the author started while the files were
            // still arriving is saying something of its own, and waits for them before it writes anything.
            function holdWhileCarrying(next: DraftStanding): void {
                if (['held', 'carrying', 'notCarried'].includes(settledAs.current.kind)) {
                    hold(next);
                }
            }

            async function bringOver(): Promise<void> {
                // Every file left behind, named together, so a second refusal does not hide the first.
                const leftBehind: string[] = [];

                for (const attachment of attachments) {
                    // A file this client staged always has a name; one filed by another mail client may not, and it
                    // is then called what a download would save it as.
                    const fileName = attachment.fileName ?? `attachment-${attachment.position.toFixed(0)}`;
                    const abandoning = new AbortController();

                    taking.current = abandoning;
                    holdWhileCarrying({ kind: 'carrying', fileName });

                    const taken = await readMailAttachment(
                        session,
                        storedEmailId,
                        attachment.position,
                        attachment.sizeOctets,
                        (request) => exchange.take(request, abandoning.signal),
                        takingFailureOf,
                    );

                    taking.current = null;

                    if (taken.outcome === 'taken') {
                        attach(new File([taken.octets], fileName, { type: attachment.mediaType }));
                    } else if (taken.refusal === 'abandoned') {
                        return;
                    } else {
                        leftBehind.push(fileName);
                    }
                }

                holdWhileCarrying(
                    leftBehind.length > 0 ? { kind: 'notCarried', fileNames: leftBehind } : { kind: 'held' },
                );
            }

            const bringing = bringOver();

            carrying.current = bringing;

            try {
                await bringing;
            } finally {
                carrying.current = null;
            }
        },

        detach: async (attachmentId) => {
            const going = held.current.find((file) => file.attachmentId === attachmentId);

            if (going === undefined) {
                return;
            }

            // Off the screen first: the author asked for it, and what the deployment still holds is this hook's
            // business rather than something they should watch happen.
            holdFiles(held.current.filter((file) => file.attachmentId !== attachmentId));

            const draft = draftId.current;

            if (going.stagedAs === null || draft === null) {
                return;
            }

            settled(await unstageMailDraftAttachment(session, transport, draft, going.stagedAs), () => ({
                kind: 'held',
            }));
        },

        send,
        withdraw,

        discard: async () => {
            // Whatever save is in flight first, so a draft written a moment ago is one this knows about rather than
            // one it leaves behind because the identifier had not landed yet.
            await saving.current;

            const draft = draftId.current;

            if (draft === null) {
                return true;
            }

            const answer = await discardMailDraft(session, transport, draft);

            if (answer.outcome === 'failed') {
                // Said rather than swallowed: closing on a refused delete would tell somebody their words are gone
                // while the deployment is still holding them, with nothing on this screen to go back to.
                hold(failedOn(answer.failure));

                return false;
            }

            draftId.current = null;

            return true;
        },
    };
}

/** A failure as a standing, saying whether nothing answered, which leaves the act's outcome unknown. */
function failedOn(failure: ClientFailure): DraftStanding {
    return { kind: 'failed', reason: failure.reason, unanswered: wentUnanswered(failure) };
}
