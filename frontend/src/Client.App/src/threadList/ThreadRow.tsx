// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ListedMailThread } from '@mailfathom/client-backend';
import { useContactPhoto } from '../contactBook/contactPhotos';
import { Icon } from '../controls/Icon';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SenderAvatar } from '../controls/SenderAvatar';
import { useLocalization } from '../localization/useLocalization';
import { senderMarkInk, senderMarkOf, senderMarksDrawn } from '../senderMark/senderMarks';
import { newestAuthor, participantsNamed, rowVariantOf, titleStateOf, type ThreadTitleState } from './threadRows';

// One conversation as the list draws it when it is read by conversation. The first line names who took part, beside the
// marks and the time; under it the conversation's title, which a model wrote for the whole of it, in a light frame of
// its own so it is never read as the sender's words; and under that the opening of its newest message, quoted with who
// wrote it. The row carries no AI summary control: the opened thread carries it on its newest received message.
//
// The row is one press, so everything on it is read out as the press's name rather than offered as a control of its own
// — the sender mark is the mark alone, and its details are a press away on the opened message's head.

export function ThreadRow({
    thread,
    open,
    onOpen,
    onElement,
}: {
    readonly thread: ListedMailThread;

    /** Whether this is the conversation the reading column stands on. */
    readonly open: boolean;

    readonly onOpen: () => void;

    /** Hands the drawn row over to be measured, since a row is as tall as what its title says. */
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { locale, translate } = useLocalization();
    const unread = thread.unreadCount > 0;
    const titled = titleStateOf(thread);
    const people = new Intl.ListFormat(locale, { style: 'short', type: 'unit' }).format(
        participantsNamed(thread, translate('threadList.me')),
    );
    const author = newestAuthor(thread);
    const picture = useContactPhoto(thread.newest.own ? null : thread.newest.senderPhoto);

    // The avatar is the correspondent's, so a conversation ending in the reader's own reply shows who it is with.
    const correspondent = thread.newest.own
        ? (thread.participants.find((participant) => !participant.reader) ?? null)
        : { displayName: thread.newest.senderDisplayName, address: thread.newest.senderAddress };
    const mark =
        thread.newestReceivedSender === null ? null : senderMarksDrawn[senderMarkOf(thread.newestReceivedSender)];

    return (
        <li ref={onElement} data-thread-row={rowVariantOf(titled)} className="border-b border-b-sunken">
            <button
                type="button"
                aria-current={open ? 'true' : undefined}
                onClick={onOpen}
                className={`flex w-full cursor-pointer flex-col gap-1.5 border-s-4 py-3.5 ps-3 pe-4 text-start transition workspace:gap-1.25 workspace:py-3 workspace:ps-2.5 workspace:pe-3.5 ${
                    open ? 'border-s-accent-strong bg-accent-soft' : 'border-s-transparent bg-panel hover:bg-hover'
                }`}
            >
                <span className="flex w-full items-center gap-2">
                    <SenderAvatar
                        displayName={correspondent?.displayName ?? null}
                        address={correspondent?.address ?? null}
                        picture={picture}
                        place="row"
                    />

                    <span className={`min-w-0 shrink truncate text-lg workspace:text-md ${unreadInk(unread)}`}>
                        {people}
                    </span>

                    {mark === null ? null : (
                        <span className={`flex shrink-0 items-center ${senderMarkInk[mark.tone]}`}>
                            <Icon name={mark.icon} className="size-icon-sm" />
                            <span className="sr-only">{translate(mark.name)}</span>
                        </span>
                    )}

                    <span className="ms-auto flex shrink-0 items-center gap-1.75">
                        {thread.hasAttachments ? (
                            <span className="text-muted">
                                <Icon name="attach_file" className="size-icon-sm" />
                                <span className="sr-only">{translate('threadList.attachments')}</span>
                            </span>
                        ) : null}

                        {thread.messageCount > 1 ? (
                            <span className="rounded-md border border-line-soft bg-rail px-1.5 py-px text-xs text-text-soft">
                                <span aria-hidden="true">{thread.messageCount}</span>
                                <span className="sr-only">
                                    {translate('list.threadMessages', { count: String(thread.messageCount) })}
                                </span>
                            </span>
                        ) : null}

                        {unread ? <span className="sr-only">{translate('list.unread')}</span> : null}

                        <ReceivedAt at={thread.newest.receivedAt} />
                    </span>
                </span>

                <ThreadTitle thread={thread} titled={titled} unread={unread} />

                <span className="line-clamp-2 h-thread-snippet border-s-2 border-s-line-strong ps-2 text-base/quoted wrap-anywhere text-muted">
                    <span className="me-1 font-semibold text-text-soft">
                        {translate('threadList.who', { who: author ?? translate('threadList.you') })}
                    </span>
                    {thread.newest.preview}
                </span>
            </button>
        </li>
    );
}

function ThreadTitle({
    thread,
    titled,
    unread,
}: {
    readonly thread: ListedMailThread;
    readonly titled: ThreadTitleState;
    readonly unread: boolean;
}) {
    const { translate } = useLocalization();
    const words = `min-w-0 flex-1 truncate text-lg text-text workspace:text-md ${unread ? 'font-bold' : 'font-semibold'}`;

    if (thread.title === null || titled === 'untitled') {
        return (
            <span className="flex min-w-0">
                <span className={words}>{thread.newest.subject ?? translate('list.noSubject')}</span>
            </span>
        );
    }

    return (
        <span className="mt-0.5 mb-0.75 flex min-w-0 flex-col gap-px rounded-md border border-accent-line bg-accent-soft/40 pt-1.5 pe-2.5 pb-1.75 ps-2">
            <span className="flex min-w-0 items-center gap-1.5">
                <Icon name="auto_awesome" className="size-icon-sm shrink-0 text-muted" />
                <span className="sr-only">{translate('threadList.aiTitle')}</span>
                <span className={words}>{thread.title.topic}</span>
            </span>

            {titled === 'standing' ? (
                <span className="truncate ps-5.25 text-base/snug text-text-soft">{thread.title.standing}</span>
            ) : null}

            {titled === 'stale' ? (
                <span className="flex min-w-0 items-center gap-1.25 ps-5.25 text-base/snug text-faint">
                    <Icon name="hourglass_top" className="size-icon-sm shrink-0" />
                    <span className="truncate">{translate('threadList.updating')}</span>
                </span>
            ) : null}
        </span>
    );
}

function unreadInk(unread: boolean): string {
    return unread ? 'font-bold text-text' : 'font-normal text-text-soft';
}
