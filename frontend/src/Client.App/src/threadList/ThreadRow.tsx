// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ListedMailThread } from '@mailfathom/client-backend';
import { RowCategories } from '../categories/CategoryLabels';
import { drawnCategories } from '../categories/categoryWords';
import { useContactPhoto } from '../contactBook/contactPhotos';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { LocalOnlyMark } from '../controls/MessageMarkers';
import { ReceivedAt } from '../controls/ReceivedAt';
import { SenderAvatar } from '../controls/SenderAvatar';
import { useLocalization } from '../localization/useLocalization';
import { ExternalPill } from '../senderMark/ExternalChip';
import { externalMarkOf, useExternalMarking } from '../senderMark/externalMark';
import { senderMarkInk, senderMarkOf, senderMarksDrawn } from '../senderMark/senderMarks';
import { newestAuthor, participantsNamed, rowVariantOf, titleStateOf, type ThreadTitleState } from './threadRows';

// One conversation as the list draws it when it is read by conversation. The first line names who took part, beside the
// marks and the time; under it the conversation's title, which a model wrote for the whole of it, in a light frame of
// its own so it is never read as the sender's words; under that the opening of its newest message, quoted with who
// wrote it; and last whatever categories its messages hold. The row carries no AI summary control: the opened thread carries it on its newest received message.
//
// The row is one press, so everything on it is read out as the press's name rather than offered as a control of its own
// — the sender mark is the mark alone, and its details are a press away on the opened message's head.
//
// What is done to the conversation is asked of the row's menu — the pointer's own menu gesture, the menu key, or a
// finger held on it — and a row is picked out as a message row is: a modified press, the menu's first item, or any
// press while a selection is held.

export function ThreadRow({
    thread,
    open,
    selected,
    selecting,
    reading,
    onOpen,
    onToggle,
    onMenu,
    onElement,
}: {
    readonly thread: ListedMailThread;

    /** Whether this is the conversation the reading column stands on. */
    readonly open: boolean;

    /** Whether the conversation is picked out for an act. */
    readonly selected: boolean;

    /** Whether a selection is being held, which turns a plain press into picking the row out. */
    readonly selecting: boolean;

    /** Whether the conversation's messages are being read for an act asked of it, which nothing has changed yet. */
    readonly reading: boolean;

    readonly onOpen: () => void;

    /** Puts the row into the selection, or takes it out. */
    readonly onToggle: () => void;

    /** Opens the row's menu at the point it was asked for. */
    readonly onMenu: (at: MenuPoint) => void;

    /** Hands the drawn row over to be measured, since a row is as tall as what its title says. */
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { locale, translate } = useLocalization();
    const externalMarking = useExternalMarking();
    const unread = thread.unreadCount > 0;
    const titled = titleStateOf(thread);
    const people = new Intl.ListFormat(locale, { style: 'short', type: 'unit' }).format(
        participantsNamed(thread, translate('threadList.me')),
    );
    const author = newestAuthor(thread);
    const picture = useContactPhoto(thread.newest.own ? null : thread.newest.senderPhoto);
    const categories = drawnCategories(thread.categories, translate);
    const categorized = categories.length > 0;
    const press = useRowPress(onMenu);

    // The avatar is the correspondent's, so a conversation ending in the reader's own reply shows who it is with.
    const correspondent = thread.newest.own
        ? (thread.participants.find((participant) => !participant.reader) ?? null)
        : { displayName: thread.newest.senderDisplayName, address: thread.newest.senderAddress };
    const mark =
        thread.newestReceivedSender === null ? null : senderMarksDrawn[senderMarkOf(thread.newestReceivedSender)];
    // The conversation's newest received message earns the chip, on the first line after the mark, because the second
    // line is the title and holds nothing but the topic.
    const external =
        thread.newestReceivedSender === null
            ? null
            : externalMarkOf(externalMarking, thread.account, thread.newestReceivedSender);

    return (
        <li
            ref={onElement}
            data-thread-row={rowVariantOf(titled, categorized)}
            aria-busy={reading}
            // An act under way is said where its answer will appear: the row waits, dimmed as a control that cannot
            // act yet is, until the act has read what it is about.
            className={`border-b border-b-sunken transition-opacity ${reading ? 'opacity-60' : ''}`}
        >
            {reading ? (
                <span role="status" className="sr-only">
                    {translate('threadAct.reading')}
                </span>
            ) : null}
            <button
                type="button"
                aria-current={open ? 'true' : undefined}
                aria-pressed={selecting ? selected : undefined}
                onContextMenu={press.onContextMenu}
                onPointerDown={press.onPointerDown}
                onPointerMove={press.onPointerMove}
                onPointerUp={press.onPointerUp}
                onPointerCancel={press.onPointerCancel}
                onClick={(event) => {
                    // The tap that follows a hold which opened the menu is the hold's, not a press of its own.
                    if (press.tapSuppressed()) {
                        return;
                    }

                    if (event.ctrlKey || event.metaKey || selecting) {
                        onToggle();
                    } else {
                        onOpen();
                    }
                }}
                // Marked at the edge as a message row is: the rows picked out for an act, and the row that is open.
                className={`flex w-full cursor-pointer flex-col gap-1.5 border-s-4 py-3.5 ps-3 pe-4 text-start transition workspace:gap-1.25 workspace:py-3 workspace:ps-2.5 workspace:pe-3.5 ${
                    selected
                        ? 'border-s-accent bg-accent-soft'
                        : open
                          ? 'border-s-accent-strong bg-accent-soft'
                          : 'border-s-transparent bg-panel hover:bg-hover'
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

                    {external === null ? null : <ExternalPill mark={external} />}

                    <span className="ms-auto flex shrink-0 items-center gap-1.75">
                        {thread.newest.localOnly === true ? <LocalOnlyMark tapReveals={false} /> : null}

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

                <span
                    className={`border-s-2 border-s-line-strong ps-2 text-base/quoted wrap-anywhere text-muted ${
                        categorized ? 'line-clamp-1' : 'line-clamp-2 h-thread-snippet'
                    }`}
                >
                    <span className="me-1 font-semibold text-text-soft">
                        {translate('threadList.who', { who: author ?? translate('threadList.you') })}
                    </span>
                    {thread.newest.preview}
                </span>

                <RowCategories categories={categories} />
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
