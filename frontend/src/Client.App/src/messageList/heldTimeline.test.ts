// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailTimelineEntry } from '@mailfathom/client-backend';
import {
    answered,
    arrivalNoticed,
    changeNoticed,
    cursorAfter,
    cursorBefore,
    flagsNoticed,
    heldRows,
    nothingHeld,
    pagesKeptEitherSide,
    positionOfRow,
    refillsHeldRows,
    refreshAsked,
    refreshUnanswered,
    rowAt,
    rowCountOf,
    rowOf,
    rowOfSlot,
    trimmedAround,
    trimmedWithoutRepeats,
    wantedFor,
    withoutLeaving,
    withoutRepeats,
    type HeldTimeline,
    type TimelinePage,
    type TimelineRead,
} from './heldTimeline';

const rowsPerPage = 4;

const idOf = (email: MailTimelineEntry): string => email.id;

function message(at: number): MailTimelineEntry {
    return {
        id: `message-${String(at)}`,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        subject: `Message ${String(at)}`,
        receivedAt: '2026-08-31T09:41:00+00:00',
        sentAt: null,
        senderAddress: 'auditor@example.invalid',
        senderDisplayName: null,
        senderPhoto: null,
        report: null,
        receipts: null,
        toAddresses: [],
        unread: false,
        flagged: false,
        answered: false,
        hasAttachments: false,
        attachmentCount: 0,
        sizeOctets: 1_024,
        preview: null,
        enrichment: null,
        threadMessageCount: null,
        categories: [],
        spam: null,
        sender: {
            authorAuthentication: 'Authenticated',
            deploymentTrust: 'Unknown',
            signatureOutcome: 'NotSigned',
            signerMatchesAuthor: false,
            trustSource: null,
            trustMember: null,
        },
    };
}

// One page of four, numbered from where it starts, so a row identity says which page it came from.
function page(from: number, cursors: Partial<TimelinePage<MailTimelineEntry>> = {}): TimelinePage<MailTimelineEntry> {
    return {
        rows: Array.from({ length: rowsPerPage }, (_, at) => message(from + at)),
        nextCursor: `after-${String(from + rowsPerPage)}`,
        previousCursor: from === 0 ? null : `before-${String(from)}`,
        ...cursors,
    };
}

const forward: TimelineRead = { cursor: null, direction: 'forward', refilling: null };

function extending(cursor: string): TimelineRead {
    return { cursor, direction: 'forward', refilling: null };
}

/** A list read forward from its leading end, one page at a time, which is what scrolling down produces. */
function readForward(pages: number): HeldTimeline<MailTimelineEntry> {
    let held = answered(nothingHeld, page(0), forward);

    for (let at = 1; at < pages; at += 1) {
        held = answered(held, page(at * rowsPerPage), extending(`after-${String(at * rowsPerPage)}`));
    }

    return held;
}

// An act a person performs takes the message out of the list at the press, and a refusal puts it back — which here is
// one function answered twice rather than a state edit and its undo, because what the list draws is derived from what
// is still being asked for. The deployment goes on answering the pre-change state for seconds afterwards, so a row
// removed by editing the held pages would come back on the next read of the folder.
describe('withoutLeaving', () => {
    it('takes the rows an act is leaving with out of the list it was asked in', () => {
        const held = readForward(2);

        const shown = withoutLeaving(held, (email) => email.id === 'message-2' || email.id === 'message-5');

        expect(rowCountOf(shown)).toBe(6);
        expect(heldRows(shown).map((email) => email.id)).not.toContain('message-2');
        expect(rowAt(shown, 2)?.id).toBe('message-3');
    });

    it('puts a row back the moment the act stops being asked for, which is what a refusal leaves behind', () => {
        const held = readForward(1);
        const leaving = withoutLeaving(held, (email) => email.id === 'message-1');

        const restored = withoutLeaving(held, () => false);

        expect(rowCountOf(leaving)).toBe(rowsPerPage - 1);
        expect(rowCountOf(restored)).toBe(rowsPerPage);
        expect(rowAt(restored, 1)?.id).toBe('message-1');
    });

    it('answers with the list itself where nothing is leaving, so a folder nobody acted in redraws no row', () => {
        const held = readForward(2);

        expect(withoutLeaving(held, () => false)).toBe(held);
    });

    it('leaves the page cursors alone, so the list goes on reading from where it had got to', () => {
        const held = readForward(2);

        const shown = withoutLeaving(held, (email) => email.id === 'message-7');

        expect(cursorAfter(shown)).toBe(cursorAfter(held));
    });
});

// A conversation stands where its newest message does, so one that moved to the top since a page further down was read
// is on both pages until that page is read again. Rows here stand for a conversation by their subject.
describe('withoutRepeats', () => {
    const conversationOf = (email: MailTimelineEntry): string => email.subject ?? '';

    function movedToTheTop(): HeldTimeline<MailTimelineEntry> {
        const leading = page(0);
        const further = page(4);

        return answered(
            answered(
                nothingHeld,
                { ...leading, rows: [{ ...message(9), subject: 'Message 5' }, ...leading.rows] },
                forward,
            ),
            further,
            extending('after-4'),
        );
    }

    it('draws a conversation held on two pages once, where the earlier page stands it', () => {
        const shown = withoutRepeats(movedToTheTop(), conversationOf);

        expect(
            heldRows(shown)
                .filter((email) => email.subject === 'Message 5')
                .map((email) => email.id),
        ).toEqual(['message-9']);
        expect(rowCountOf(shown)).toBe(rowCountOf(movedToTheTop()) - 1);
        expect(shown.slots[1]?.rowCount).toBe(rowsPerPage - 1);
    });

    it('answers with the list itself where nothing repeats', () => {
        const held = readForward(2);

        expect(withoutRepeats(held, conversationOf)).toBe(held);
    });
});

describe('answered', () => {
    it('stands a list on the first page it read', () => {
        const held = answered(nothingHeld, page(0), forward);

        expect(rowCountOf(held)).toBe(rowsPerPage);
        expect(rowAt(held, 0)?.id).toBe('message-0');
    });

    it('records a folder holding no mail rather than reading it again', () => {
        const empty = { rows: [], nextCursor: null, previousCursor: null };
        const held = answered(nothingHeld, empty, forward);

        expect(held.slots).toHaveLength(1);
        expect(rowCountOf(held)).toBe(0);
        expect(wantedFor(held, 0, 0)).toBeNull();
    });

    it('joins a page read forward to the end of the list', () => {
        const held = readForward(2);

        expect(rowCountOf(held)).toBe(8);
        expect(rowAt(held, 7)?.id).toBe('message-7');
    });

    it('joins a page read backward to the beginning of the list', () => {
        const opened = answered(nothingHeld, page(4), forward);
        const held = answered(opened, page(0), { cursor: 'before-4', direction: 'backward', refilling: null });

        expect(rowAt(held, 0)?.id).toBe('message-0');
        expect(rowAt(held, 4)?.id).toBe('message-4');
    });

    it('retires the cursor at an end that answered with nothing, so the list stops asking there', () => {
        const opened = answered(nothingHeld, page(0), forward);
        const empty = { rows: [], nextCursor: null, previousCursor: null };
        const held = answered(opened, empty, extending('after-4'));

        expect(rowCountOf(held)).toBe(rowsPerPage);
        expect(cursorAfter(held)).toBeNull();
    });

    it('puts a page read again back where its rows stood, rather than at an end', () => {
        const held = readForward(3);
        const dropped = trimmedAround(held, 8, 11);
        const refilled = answered(dropped, page(0), { cursor: null, direction: 'forward', refilling: 0 });

        expect(rowCountOf(refilled)).toBe(rowCountOf(held));
        expect(rowAt(refilled, 0)?.id).toBe('message-0');
    });
});

describe('trimmedAround', () => {
    it('drops the rows of a page far from the reader and keeps the space they stood in', () => {
        const held = readForward(2 + pagesKeptEitherSide * 2);
        const trimmed = trimmedAround(held, 0, 3);

        expect(rowCountOf(trimmed)).toBe(rowCountOf(held));
        expect(rowAt(trimmed, rowCountOf(held) - 1)).toBeNull();
    });

    it('keeps the pages either side of the one being read', () => {
        const held = readForward(6);
        const trimmed = trimmedAround(held, 8, 11);

        expect(rowAt(trimmed, 0)?.id).toBe('message-0');
        expect(rowAt(trimmed, 19)?.id).toBe('message-19');
    });

    it('answers with the list it was given where nothing was far enough to drop', () => {
        const held = readForward(2);

        expect(trimmedAround(held, 0, 7)).toBe(held);
    });

    it('leaves a page it already dropped alone rather than reporting a change', () => {
        const held = trimmedAround(readForward(6), 0, 3);

        expect(trimmedAround(held, 0, 3)).toBe(held);
    });

    // The window was worked out in rows as they are drawn, so a page leaving above it moves every row number down; the
    // pages kept are the ones either side of the page on the screen, not of the page that would be there had none left.
    it('keeps the pages either side of the one on the screen while rows above it are leaving', () => {
        const held = readForward(9);
        const leaving = new Set(['message-0', 'message-1', 'message-2', 'message-3']);
        const trimmed = trimmedAround(held, 20, 23, (all) => withoutLeaving(all, (email) => leaving.has(email.id)));

        expect(rowAt(trimmed, 12)?.id).toBe('message-16');
        expect(rowAt(trimmed, 8)).toBeNull();
    });
});

// A conversation that moved to the top stands on two held pages, and which of its rows is drawn is decided by the two
// together — so letting either go writes the decision down rather than leaving the other page to make it alone.
describe('trimmedWithoutRepeats', () => {
    const conversationOf = (email: MailTimelineEntry): string => email.subject ?? '';
    const drawn = (all: HeldTimeline<MailTimelineEntry>) => withoutRepeats(all, conversationOf);

    function movedToTheTopOfSix(): HeldTimeline<MailTimelineEntry> {
        const leading = page(0);
        let held = answered(
            nothingHeld,
            { ...leading, rows: [{ ...message(99), subject: 'Message 5' }, ...leading.rows] },
            forward,
        );

        for (let at = 1; at < 6; at += 1) {
            held = answered(held, page(at * rowsPerPage), extending(`after-${String(at * rowsPerPage)}`));
        }

        return held;
    }

    it('keeps the list its height when it drops the later page of a repeat', () => {
        const held = movedToTheTopOfSix();
        const tall = rowCountOf(drawn(held));
        const trimmed = trimmedWithoutRepeats(held, tall - rowsPerPage, tall - 1, conversationOf);

        expect(trimmed.slots[1]?.rows).toBeNull();
        expect(rowCountOf(drawn(trimmed))).toBe(tall);
    });

    it('keeps the list its height when it drops the earlier page of a repeat and keeps the later', () => {
        const held = movedToTheTopOfSix();
        const tall = rowCountOf(drawn(held));
        const trimmed = trimmedWithoutRepeats(held, 12, 15, conversationOf);

        expect(trimmed.slots[0]?.rows).toBeNull();
        expect(trimmed.slots[1]?.rows).not.toBeNull();
        expect(rowCountOf(drawn(trimmed))).toBe(tall);
    });

    it('answers with the list it was given, repeats and all, where nothing was far enough to drop', () => {
        const held = movedToTheTopOfSix();

        expect(trimmedWithoutRepeats(held, 10, 13, conversationOf)).toBe(held);
    });
});

describe('wantedFor', () => {
    it('wants nothing while the window is inside rows the list is holding', () => {
        expect(wantedFor(readForward(3), 4, 7)).toBeNull();
    });

    it('wants the page after the end once the window reaches it', () => {
        expect(wantedFor(readForward(2), 4, 7)).toStrictEqual({
            cursor: 'after-8',
            direction: 'forward',
            refilling: null,
        });
    });

    it('wants the page before the beginning once the window reaches it', () => {
        const held = answered(nothingHeld, page(4, { nextCursor: null }), forward);

        expect(wantedFor(held, 0, 3)).toStrictEqual({
            cursor: 'before-4',
            direction: 'backward',
            refilling: null,
        });
    });

    it('reads onward before it reads back, where a short list has both of its ends on the screen', () => {
        const held = answered(nothingHeld, page(4), forward);

        expect(wantedFor(held, 0, 3)?.direction).toBe('forward');
    });

    it('wants a dropped page back through the cursor it was read under, not the leading end of the list', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(wantedFor(held, 0, 3)).toStrictEqual({ cursor: null, direction: 'forward', refilling: 0 });
    });

    it('wants a dropped page before either end, so a hole on the screen is filled before the list grows', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(wantedFor(held, 0, 23)?.refilling).toBe(0);
    });

    it('wants nothing at an end the list has reached', () => {
        const held = answered(nothingHeld, page(0, { nextCursor: null, previousCursor: null }), forward);

        expect(wantedFor(held, 0, 3)).toBeNull();
    });
});

describe('positionOfRow', () => {
    it('answers the page a row is in and where in it, so a returning visit reads that page', () => {
        expect(positionOfRow(readForward(3), 9)).toStrictEqual({
            cursor: 'after-8',
            readAs: 'forward',
            rowInPage: 1,
        });
    });

    it('answers the leading page for a row in it', () => {
        expect(positionOfRow(readForward(2), 1)).toStrictEqual({ cursor: null, readAs: 'forward', rowInPage: 1 });
    });

    it('answers nothing for a row past the end of the list', () => {
        expect(positionOfRow(readForward(1), 99)).toBeNull();
    });
});

describe('rowOfSlot', () => {
    it('answers where a page starts', () => {
        expect(rowOfSlot(readForward(3), 2)).toBe(8);
    });

    it('answers nothing for a page the list does not know', () => {
        expect(rowOfSlot(readForward(1), 4)).toBeNull();
    });
});

describe('rowOf', () => {
    it('answers where a message stands, counting the rows of every page before it the list dropped', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(rowOf(held, 'message-21')).toBe(21);
    });

    it('answers nothing for a message no held page carries', () => {
        expect(rowOf(trimmedAround(readForward(6), 20, 23), 'message-0')).toBeNull();
    });
});

describe('heldRows', () => {
    it('answers only the rows the list is holding, in reading order', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(heldRows(held).map((email) => email.id)).not.toContain('message-0');
        expect(heldRows(held)[0]?.id).toBe(`message-${String(rowsPerPage * (6 - 1 - pagesKeptEitherSide))}`);
    });
});

describe('cursorBefore', () => {
    it('answers nothing at the beginning of a list read from its leading end', () => {
        expect(cursorBefore(readForward(2))).toBeNull();
    });
});

describe('arrivalNoticed', () => {
    it('marks the leading page to be read again, and no other, keeping every row of it drawn', () => {
        const held = arrivalNoticed(readForward(3));

        expect(held.slots[0]?.stale).toBe(true);
        expect(held.slots[1]?.stale).toBe(false);
        expect(held.slots[2]?.stale).toBe(false);
        expect(heldRows(held)).toStrictEqual(heldRows(readForward(3)));
    });

    it('keeps the list the length it was, so nothing under the reader moves', () => {
        expect(rowCountOf(arrivalNoticed(readForward(3)))).toBe(rowCountOf(readForward(3)));
    });

    it('leaves a leading page it has already marked as it was, so a second arrival asks once', () => {
        const once = arrivalNoticed(readForward(3));

        expect(arrivalNoticed(once)).toBe(once);
    });

    it('asks for the leading page again only while it is on the screen', () => {
        const held = arrivalNoticed(readForward(3));

        expect(wantedFor(held, 0, 1)?.refilling).toBe(0);
        expect(wantedFor(held, 8, 9)).toBeNull();
    });

    it('leaves a list that has read nothing alone', () => {
        expect(arrivalNoticed(nothingHeld)).toBe(nothingHeld);
    });
});

describe('refreshAsked', () => {
    it('marks every page it holds to be read again, keeping each of its rows drawn', () => {
        const held = refreshAsked(readForward(3));

        expect(held.slots.every((slot) => slot.stale)).toBe(true);
        expect(heldRows(held)).toStrictEqual(heldRows(readForward(3)));
    });

    it('asks for the page on the screen again, through the cursor it was read under', () => {
        expect(wantedFor(refreshAsked(readForward(3)), 4, 7)).toStrictEqual({
            cursor: 'after-4',
            direction: 'forward',
            refilling: 1,
        });
    });

    it('stops asking once the page on the screen has answered, leaving the others for when they are reached', () => {
        const refilling: TimelineRead = { cursor: 'after-4', direction: 'forward', refilling: 1 };
        const held = answered(refreshAsked(readForward(3)), page(4), refilling);

        expect(held.slots[1]?.stale).toBe(false);
        expect(wantedFor(held, 4, 7)).toBeNull();
        expect(wantedFor(held, 8, 9)?.refilling).toBe(2);
    });

    it('leaves a page it had already dropped as it was, which the list reads again once it is reached anyway', () => {
        const held = refreshAsked(trimmedAround(readForward(6), 20, 23));

        expect(held.slots[0]?.rows).toBeNull();
        expect(held.slots[0]?.stale).toBe(false);
    });

    it('leaves a list that has read nothing alone', () => {
        expect(refreshAsked(nothingHeld)).toBe(nothingHeld);
    });
});

describe('refreshUnanswered', () => {
    it('stops asking for a page the deployment did not answer, and keeps its rows', () => {
        const held = refreshUnanswered(refreshAsked(readForward(1)), 0);

        expect(held.slots[0]?.stale).toBe(false);
        expect(heldRows(held)).toStrictEqual(heldRows(readForward(1)));
        expect(wantedFor(held, 0, 3)?.refilling).toBeNull();
    });
});

describe('refillsHeldRows', () => {
    it('answers that a read of a page still drawn is one the reader is looking through', () => {
        const held = refreshAsked(readForward(2));

        expect(refillsHeldRows(held, wantedFor(held, 0, 3))).toBe(true);
    });

    it('answers that a read of a dropped page is not, since nothing of it is drawn', () => {
        const held = trimmedAround(readForward(6), 20, 23);

        expect(refillsHeldRows(held, wantedFor(held, 0, 3))).toBe(false);
    });

    it('answers that a read extending the list is not, and neither is no read at all', () => {
        expect(refillsHeldRows(readForward(2), extending('after-8'))).toBe(false);
        expect(refillsHeldRows(readForward(2), null)).toBe(false);
    });
});

describe('changeNoticed', () => {
    it('marks the pages holding the mail the deployment named and no others, keeping their rows drawn', () => {
        const held = changeNoticed(readForward(3), ['message-5'], idOf);

        expect(held.slots[0]?.stale).toBe(false);
        expect(held.slots[1]?.stale).toBe(true);
        expect(held.slots[2]?.stale).toBe(false);
        expect(heldRows(held)).toStrictEqual(heldRows(readForward(3)));
    });

    it('marks every page holding one of them', () => {
        const held = changeNoticed(readForward(3), ['message-1', 'message-9'], idOf);

        expect(held.slots[0]?.stale).toBe(true);
        expect(held.slots[1]?.stale).toBe(false);
        expect(held.slots[2]?.stale).toBe(true);
    });

    it('leaves a page it has already marked as it was, so a second statement about it asks once', () => {
        const once = changeNoticed(readForward(3), ['message-5'], idOf);

        expect(changeNoticed(once, ['message-5'], idOf)).toBe(once);
    });

    it('reads mail it holds on no page as mail that came into the folder, marking the leading page alone', () => {
        const held = changeNoticed(readForward(3), ['message-999'], idOf);

        expect(held.slots.map((slot) => slot.stale)).toStrictEqual([true, false, false]);
        expect(heldRows(held)).toStrictEqual(heldRows(readForward(3)));
    });

    it('reads the leading page for mail it holds on no page while only a page far below it was dropped', () => {
        const trimmed = trimmedAround(readForward(2 + pagesKeptEitherSide * 2), 0, 3);
        const held = changeNoticed(trimmed, ['message-999'], idOf);

        expect(held.slots[0]?.stale).toBe(true);
        expect(held.slots.slice(1).map((slot) => slot.stale)).toStrictEqual(trimmed.slots.slice(1).map(() => false));
    });

    it('reads nothing for mail it holds on no page while the leading page is itself dropped', () => {
        const trimmed = trimmedAround(readForward(6), 20, 23);

        expect(changeNoticed(trimmed, ['message-999'], idOf)).toBe(trimmed);
    });

    it('leaves the list the object it was where nothing is named', () => {
        const held = readForward(3);

        expect(changeNoticed(held, [], idOf)).toBe(held);
    });

    it('keeps the list the length it was', () => {
        expect(rowCountOf(changeNoticed(readForward(3), ['message-5'], idOf))).toBe(rowCountOf(readForward(3)));
    });
});

describe('flagsNoticed', () => {
    it('redraws the named rows where they are held, dropping no page', () => {
        const held = flagsNoticed(readForward(3), [{ email: 'message-5', isSeen: false, isFlagged: true }]);

        expect(held.slots.every((slot) => slot.rows !== null)).toBe(true);
        expect(rowAt(held, 5)).toMatchObject({ id: 'message-5', unread: true, flagged: true });
    });

    it('leaves a flag the statement is silent about where it was', () => {
        const starred = flagsNoticed(readForward(1), [{ email: 'message-1', isSeen: null, isFlagged: true }]);
        const read = flagsNoticed(starred, [{ email: 'message-1', isSeen: true, isFlagged: null }]);

        expect(rowAt(read, 1)).toMatchObject({ unread: false, flagged: true });
    });

    it('leaves the same rows behind when the same statement arrives twice', () => {
        const flags = [{ email: 'message-2', isSeen: false, isFlagged: true }];
        const once = flagsNoticed(readForward(2), flags);

        expect(heldRows(flagsNoticed(once, flags))).toStrictEqual(heldRows(once));
    });

    it('leaves the list the object it was where it holds none of the rows named', () => {
        const held = readForward(3);

        expect(flagsNoticed(held, [{ email: 'message-999', isSeen: true, isFlagged: null }])).toBe(held);
        expect(flagsNoticed(held, [])).toBe(held);
    });

    it('leaves every other row alone, and every page holding none of them the object it was', () => {
        const before = readForward(2);
        const held = flagsNoticed(before, [{ email: 'message-3', isSeen: false, isFlagged: null }]);

        expect(rowAt(held, 4)).toStrictEqual(rowAt(before, 4));
        expect(held.slots[1]).toBe(before.slots[1]);
    });
});
