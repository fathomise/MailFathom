// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { NotificationScreen, NotificationTarget } from '@mailfathom/client-backend';
import type { Space } from '../routing/spaces';

// Where a notification leads. The deployment says what a notification is *about*, and this says what this client can
// do about it: open the mail, the send, the draft, or the task it names, or go to a space. It is a function rather
// than a branch inside the frame because it is the one place the two vocabularies meet, and because a mapping nothing
// can call on its own is a mapping nothing can be asserted about either.
//
// **A reminder opens what it names where this client draws it.** A task reminder opens Tasks with that task's dialog
// over it. A calendar event's reminder still goes to the Calendar space, as close to the event as this mapping takes
// somebody.
//
// **A target this client cannot open leaves the reader where they were.** A screen no space here answers for is a
// stage that has not shipped, and a notification about nothing is one that was never a place to go: neither is a
// failure to report, so neither moves anybody. The panel closing is the reader's own act and happens either way.

/** What the client can do about a target, which is what the frame supplies and what a test stands in for. */
export interface NotificationDestinations {
    /** Opens one message, which is where a notification about mail leads. */
    readonly openMail: (storedEmailId: string) => void;

    /** Goes to a space, which is where a notification about a screen leads when this client has that space. */
    readonly goTo: (space: Space) => void;

    /** Opens one send in its account's Outbox, which is where a refusal is decided about. */
    readonly showOutboxEntry: (outgoingEmailId: string) => void;

    /**
     * Opens the composer on one draft the deployment holds, with its account's Drafts folder behind it, which is where
     * a draft out of step is carried on.
     */
    readonly openDraft: (draftId: string) => void;

    /** Opens Tasks with one task's dialog over it, which is where a task reminder leads. */
    readonly openTask: (taskId: string) => void;
}

// Exhaustive by its own type, so a screen the service adds fails to compile here until somebody has decided where it
// leads. `Settings` is reached from the account menu rather than from an address, so it has none — which is the
// stage-that-has-not-shipped case above rather than an omission.
const screenSpaces: Readonly<Record<NotificationScreen, Space | null>> = {
    Mail: 'mail',
    Settings: null,
};

export function followTarget(target: NotificationTarget, client: NotificationDestinations): void {
    // A change the mailbox did not take leads to the message it was asked of, which is where it is decided about.
    if (target.kind === 'Message' || target.kind === 'MailboxChange') {
        client.openMail(target.storedEmailId);

        return;
    }

    if (target.kind === 'OutgoingEmail') {
        client.showOutboxEntry(target.outgoingEmailId);

        return;
    }

    if (target.kind === 'MailDraft') {
        client.openDraft(target.draftId);

        return;
    }

    if (target.kind === 'CalendarEvent') {
        client.goTo('calendar');

        return;
    }

    if (target.kind === 'PersonalTask') {
        client.openTask(target.taskId);

        return;
    }

    if (target.kind !== 'Screen') {
        return;
    }

    const space = screenSpaces[target.screen];

    if (space !== null) {
        client.goTo(space);
    }
}
