// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { flushSync } from 'react-dom';
import {
    isSpam,
    type ClientSession,
    type MailCarried,
    type MailFathomTransport,
    type MailMessage,
} from '@mailfathom/client-backend';
import { useContactPhoto } from '../contactBook/contactPhotos';
import { Icon } from '../controls/Icon';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SenderAvatar } from '../controls/SenderAvatar';
import { ShowFullHtml } from '../fullHtml/ShowFullHtml';
import type { MessageKey } from '../localization/en';
import { wordLastSeen } from '../localization/instants';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { Message } from '../messageBody/Message';
import type { MessageBodyRead } from '../messageBody/useMessageBody';
import { drawnSpam, useMailboxActs } from '../mailboxActs/useMailboxActs';
import { useMessageView } from '../preferences/messageView';
import { usePendingChanges } from '../pendingChanges/usePendingChanges';
import { useReadMarking } from '../readMarking/useReadMarking';
import { FoldedReport } from '../receipts/FoldedReport';
import { ReadReceiptAnswer } from '../receipts/ReadReceiptAnswer';
import { ReceiptBadge } from '../receipts/ReceiptBadge';
import { ReceiptDetails, type AskedReceipts } from '../receipts/ReceiptDetails';
import { readerOf, reportWordsOf } from '../receipts/reportWords';
import { SenderBadge } from '../senderMark/SenderBadge';
import { MessageSenderDetails, type AskedSender } from '../senderMark/SenderDetails';
import { listedSenderOf, marksSender, senderMarkOf } from '../senderMark/senderMarks';
import { SpamWarning } from '../spamMarking/SpamWarning';
import { useSpamRefusal } from '../spamMarking/useSpamRefusal';
import { useWorkspace } from '../workspace/useWorkspace';
import { Attachments } from './Attachments';
import { ChangeNotice } from './ChangeNotice';
import { MessagePlace } from './MessagePlace';

// One message drawn out in full, which is the same drawing wherever a message is drawn: who wrote it, the mark on who
// sent it, and what the copy holds on one line, the words themselves, and the files the message carries.
//
// It is one component rather than two because a message read on its own and a message read inside the correspondence it
// belongs to are the same message. The design draws them identically, and a second implementation of this is
// how the two would come to differ by a line height nobody meant to change — which is exactly what the conversation
// screen had before it drew the message this way.
//
// What is *not* here is the screen's own head. A subject, the acts over what is being read, and the way back to the
// list belong to the surface rather than to a message: a conversation says its subject once above every message in it,
// and a message read on its own says it above that one. Each caller therefore draws its own head and this draws what
// stands under it.
//
// Reading the body is the caller's, for the reason `messageBody/useMessageBody.ts` gives — the read starts where the
// surface mounts rather than where the body is drawn — and a conversation hands the answer it already holds instead of
// starting one at all.

// The most of a selected passage the workspace carries. A question is asked about a fragment somebody pointed at, so a
// select-all is a gesture rather than a scope, and a whole message in the workspace would travel into every later screen
// that reads it.
const longestFragment = 2000;

export function OpenedMessage({
    session,
    transport,
    message,
    body,
    onShowFullHtml,
    marks = null,
    summary = null,
    receiptsShown = true,
    answersReadReceipt = true,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The message as the deployment described it, which is what everything but the words is drawn from. */
    readonly message: MailMessage;

    /** The words, as the surface drawing this message is reading them. */
    readonly body: MessageBodyRead;

    /** Opens the surface drawing this message's own markup, which the line above the words offers. */
    readonly onShowFullHtml: () => void;

    /** What a conversation says about this message beside who wrote it — that it is new — or `null` where it says nothing. */
    readonly marks?: ReactNode;

    /** The control opening what MailFathom read from the conversation, which only its newest received message carries. */
    readonly summary?: ReactNode;

    /**
     * Whether the message carries the mark on what came back for it. What came back belongs to the newest message the
     * account sent, so in a conversation only that one does; a message read on its own is that message.
     */
    readonly receiptsShown?: boolean;

    /**
     * Whether a read receipt the message asks for is offered here. Only the newest received message of a conversation
     * offers one; a message read on its own is that message.
     */
    readonly answersReadReceipt?: boolean;
}) {
    const { locale, translate } = useLocalization();
    const embeddedHtml = useMessageView() === 'embeddedHtml';
    const { revise } = useWorkspace();
    const { markRead } = useReadMarking();
    const words = useRef<HTMLDivElement>(null);
    const refuse = useSpamRefusal();
    const acts = useMailboxActs();

    // A message marked spam is read-only: a warning stands over its words, and its original, its remote content, its
    // links, and its files open nothing. The person's own mark from this session counts before the next read says so.
    const spam = drawnSpam(acts, { id: message.storedEmailId, spam: message.spam });
    const readOnly = isSpam(spam);
    const details = useRef<HTMLDialogElement>(null);
    const [anchor, setAnchor] = useState<AskedSender['anchor']>(null);
    const receiptDialog = useRef<HTMLDialogElement>(null);
    const [askedReceipts, setAskedReceipts] = useState<AskedReceipts | null>(null);
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());

    // A message in Sent or Drafts was written by the reader, so there is no sender to mark — the rule a row follows.
    const marked = marksSender(
        acts.folderRoleOf({
            storedEmailId: message.storedEmailId,
            account: message.account,
            folder: message.folder,
            unread: message.unread,
            flagged: message.flagged,
            spam: message.spam,
        }),
    );

    const storedEmailId = message.storedEmailId;
    const author = message.headers.participants.find((participant) => participant.role === 'From') ?? null;
    const authorPicture = useContactPhoto(message.senderPhoto);
    const numbers = new Intl.NumberFormat(locale);

    // A report that came back as mail is folded to one line naming what it reports, until a press unfolds it. Held by
    // the message it was unfolded on, so the next message drawn here opens folded as every report does.
    const report =
        message.report === null
            ? null
            : reportWordsOf(
                  message.report,
                  readerOf(message.report, author?.displayName ?? null, author?.address ?? null, translate),
                  message.report.reportedAt === null
                      ? null
                      : wordLastSeen(message.report.reportedAt, locale, now, timeZone),
                  translate,
                  locale,
              );
    const [unfoldedFor, setUnfoldedFor] = useState<string | null>(null);
    const folded = report !== null && unfoldedFor !== storedEmailId;

    // Its line is the whole of what opening a folded report draws, so drawing the line is what marks it read — the rule
    // the words of every other message keep, held to once per message for the reason `messageBody/Message.tsx` gives.
    const markedFolded = useRef<string | null>(null);

    useEffect(() => {
        if (folded && markedFolded.current !== storedEmailId) {
            markedFolded.current = storedEmailId;
            markRead({ storedEmailId, account: message.account, folder: message.folder, unread: message.unread });
        }
    }, [folded, storedEmailId, markRead, message.account, message.folder, message.unread]);

    // Whether a change the mailbox did not take stands over this message, which is the only case the notice is mounted in.
    const pending = usePendingChanges();
    const asked = [...pending.undecided, ...pending.abandoned].some((change) => change.storedEmailId === storedEmailId);

    // What a person selected becomes the scope the intent field asks its next question under. It is read from the
    // gesture that produced it rather than from an effect watching the document, and it is bounded, trimmed, and
    // confined to this message's own words: a selection that started outside the body is not part of the message.
    function capture(): void {
        const selected = window.getSelection();
        const region = words.current;

        if (selected === null || region === null) {
            return;
        }

        if (!region.contains(selected.anchorNode) || !region.contains(selected.focusNode)) {
            return;
        }

        const text = selected.toString().trim().slice(0, longestFragment);

        revise({ fragment: text === '' ? null : { messageId: storedEmailId, text } });
    }

    return (
        <>
            {/* The message as the design draws one: flat on the column, at the measure a conversation's
                messages take, with who wrote it and what the copy holds on its first line — how many attachments, the
                sender's own markup where there is one, and when this deployment recorded it — and what it says under
                that. */}
            <div className="mx-auto flex w-full max-w-conversation flex-col gap-3">
                {report === null ? null : (
                    <FoldedReport
                        words={report}
                        unfolded={!folded}
                        onToggle={() => {
                            setUnfoldedFor(folded ? storedEmailId : null);
                        }}
                    />
                )}

                {folded ? null : (
                    <>
                        <div className="flex items-center gap-2.75">
                            <SenderAvatar
                                displayName={author?.displayName ?? null}
                                address={author?.address ?? null}
                                picture={authorPicture}
                                place="card"
                            />

                            <span className="min-w-0 truncate text-md font-semibold text-text">
                                {author === null
                                    ? translate('message.noAuthor')
                                    : (author.displayName ?? author.address)}
                            </span>

                            {/* Who sent it, as far as anything established it, beside the name it claims — the same mark its
                        row carries, and the way to every step that reached it. */}
                            {marked ? (
                                <SenderBadge
                                    mark={senderMarkOf(listedSenderOf(message))}
                                    onOpen={(at) => {
                                        // Placed before it opens, so the details never draw for a frame where the last ones stood.
                                        flushSync(() => {
                                            setAnchor({
                                                left: at.left,
                                                right: at.right,
                                                top: at.top,
                                                bottom: at.bottom,
                                            });
                                        });
                                        details.current?.showModal();
                                    }}
                                />
                            ) : null}

                            {marks}

                            <span className="flex-1" />

                            {/* What came back for a message the account sent, first in the end of the line as the design draws
                        it, and the way to every recipient's own reports. */}
                            {receiptsShown ? (
                                <ReceiptBadge
                                    receipts={message.receipts}
                                    participants={message.headers.participants}
                                    onOpen={(at) => {
                                        flushSync(() => {
                                            setAskedReceipts({
                                                storedEmailId,
                                                message,
                                                anchor: {
                                                    left: at.left,
                                                    right: at.right,
                                                    top: at.top,
                                                    bottom: at.bottom,
                                                },
                                            });
                                        });
                                        receiptDialog.current?.showModal();
                                    }}
                                />
                            ) : null}

                            {summary}

                            {message.attachments.length === 0 ? null : (
                                <span className="flex shrink-0 items-center gap-0.75 text-xs text-faint">
                                    <Icon name="attach_file" className="size-icon-xs" />
                                    {numbers.format(message.attachments.length)}
                                </span>
                            )}

                            {/* Drawn in the reduced view and in no other. With the embedded HTML view chosen the markup is
                        already on the screen under this line, so a control offering to open it would open a second
                        copy of what is being read — which is why it goes rather than being disabled. */}
                            {/* Spam keeps the control whatever view is chosen, and the press says why its original stays shut. */}
                            {embeddedHtml && !readOnly ? null : (
                                <ShowFullHtml
                                    onShow={onShowFullHtml}
                                    {...(readOnly && {
                                        onRefused: () => {
                                            refuse('spam.refusedOriginal');
                                        },
                                    })}
                                />
                            )}

                            <ReceivedAt at={message.headers.receivedAt} />
                        </div>

                        {spam !== null && readOnly ? <SpamWarning spam={spam} /> : null}

                        {/* A change the mailbox did not take stands over the words it is about, until it is answered. */}
                        {asked ? <ChangeNotice message={message} /> : null}

                        {/* The gestures a selection ends on rather than a document-wide subscription: a selection made with the
                    pointer settles on the release and one made with the keyboard on the key coming back up, and both of
                    them are events this region already receives. */}
                        {/* The ceiling a message's words are read under, which binds the content alone: the mark on who
                    sent it, the files it carries, and the line above have no measure to keep. It is ranged left, so a
                    window wider than the ceiling leaves its margin on the empty side rather than pushing the words away
                    from the list they were opened from. */}
                        <div ref={words} onKeyUp={capture} onMouseUp={capture} className="max-w-reading">
                            {/* The body being drawn is what opening this message means, so it is what marks it read — one rule
                        wherever a message is drawn, because a message read inside its conversation was read. */}
                            <Message
                                body={body}
                                storedEmailId={storedEmailId}
                                readOnly={readOnly}
                                answer={
                                    answersReadReceipt && !readOnly && message.readReceiptRequest !== null ? (
                                        <ReadReceiptAnswer
                                            key={storedEmailId}
                                            session={session}
                                            transport={transport}
                                            storedEmailId={storedEmailId}
                                            request={message.readReceiptRequest}
                                            sender={
                                                author === null
                                                    ? translate('message.noAuthor')
                                                    : (author.displayName ?? author.address)
                                            }
                                        />
                                    ) : null
                                }
                                onBodyDrawn={() => {
                                    markRead({
                                        storedEmailId,
                                        account: message.account,
                                        folder: message.folder,
                                        unread: message.unread,
                                    });
                                }}
                            />
                        </div>

                        {message.attachments.length === 0 ? null : (
                            <Attachments
                                session={session}
                                storedEmailId={storedEmailId}
                                attachments={message.attachments}
                                readOnly={readOnly}
                            />
                        )}

                        <MessagePlace message={message} />
                    </>
                )}
            </div>

            {folded || message.carried === null ? null : <Carried carried={message.carried} />}

            {marked ? (
                <MessageSenderDetails
                    session={session}
                    transport={transport}
                    message={message}
                    anchor={anchor}
                    dialog={details}
                />
            ) : null}

            {receiptsShown && message.receipts !== null ? (
                <ReceiptDetails asked={askedReceipts} session={session} transport={transport} dialog={receiptDialog} />
            ) : null}
        </>
    );
}

// What a message carries besides its files, where any of it is true. Each of the three is a fact about the message
// rather than about a part, which is why they are said here and not on a row: a signature and an unopened `winmail.dat`
// are not files a reader can open, and drawing them as ones would offer a download that answers with nothing.
function Carried({ carried }: { readonly carried: MailCarried }) {
    const { locale, translate } = useLocalization();

    const notes: readonly MessageKey[] = [
        ...(carried.encrypted ? (['carried.encrypted'] as const) : []),
        ...(carried.unverifiedSignature ? (['carried.unverifiedSignature'] as const) : []),
        ...(carried.unexpandedTnefPart ? (['carried.unexpandedTnefPart'] as const) : []),
    ];

    if (notes.length === 0 && carried.attachmentCount === 0) {
        return null;
    }

    return (
        // At the measure the message itself takes rather than across the pane: what it says is about the copy a reader
        // is looking at, so it lines up under the words instead of standing out in the margin a wide window leaves.
        <aside className="mx-auto flex w-full max-w-conversation flex-col gap-1 text-sm text-muted">
            {carried.attachmentCount === 0 ? null : (
                <p>{translate('carried.total', { size: sizeOf(carried.totalSizeOctets, locale) })}</p>
            )}

            {notes.map((note) => (
                <p key={note}>{translate(note)}</p>
            ))}
        </aside>
    );
}
