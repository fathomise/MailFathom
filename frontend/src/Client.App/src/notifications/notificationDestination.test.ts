// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it, vi } from 'vitest';
import { followTarget, type NotificationDestinations } from './notificationDestination';

function following(): NotificationDestinations {
    return { openMail: vi.fn(), goTo: vi.fn(), showOutboxEntry: vi.fn(), openDraft: vi.fn() };
}

describe('followTarget', () => {
    it('opens the message a change the mailbox did not take was asked of', () => {
        const client = following();

        followTarget({ kind: 'MailboxChange', storedEmailId: 'e-4', changeRecordId: 'r-1' }, client);

        expect(client.openMail).toHaveBeenCalledExactlyOnceWith('e-4');
    });

    it('opens a refused send in its Outbox', () => {
        const client = following();

        followTarget({ kind: 'OutgoingEmail', outgoingEmailId: 'o-2' }, client);

        expect(client.showOutboxEntry).toHaveBeenCalledExactlyOnceWith('o-2');
        expect(client.openMail).not.toHaveBeenCalled();
    });

    it('opens the composer on a draft out of step with its server', () => {
        const client = following();

        followTarget({ kind: 'MailDraft', draftId: 'd-3' }, client);

        expect(client.openDraft).toHaveBeenCalledExactlyOnceWith('d-3');
    });

    it('opens the message a notification about mail names', () => {
        const client = following();

        followTarget({ kind: 'Message', storedEmailId: 'e-9' }, client);

        expect(client.openMail).toHaveBeenCalledExactlyOnceWith('e-9');
        expect(client.goTo).not.toHaveBeenCalled();
    });

    it('goes to the space a notification about a screen names', () => {
        const client = following();

        followTarget({ kind: 'Screen', screen: 'Mail' }, client);

        expect(client.goTo).toHaveBeenCalledExactlyOnceWith('mail');
        expect(client.openMail).not.toHaveBeenCalled();
    });

    // As close to the event as this client can take somebody until the Calendar screen draws one.
    it('goes to the calendar for a reminder, which names an event no screen opens yet', () => {
        const client = following();

        followTarget({ kind: 'CalendarEvent', calendarEventId: 'c-4' }, client);

        expect(client.goTo).toHaveBeenCalledExactlyOnceWith('calendar');
        expect(client.openMail).not.toHaveBeenCalled();
    });

    // The same answer for the same reason: the Tasks screen that would open one has not shipped either.
    it('goes to the tasks for a due reminder, which names a task no screen opens yet', () => {
        const client = following();

        followTarget({ kind: 'PersonalTask', taskId: 't-7' }, client);

        expect(client.goTo).toHaveBeenCalledExactlyOnceWith('tasks');
        expect(client.openMail).not.toHaveBeenCalled();
    });

    it('leaves the reader where they were where the screen has no address in this client yet', () => {
        const client = following();

        followTarget({ kind: 'Screen', screen: 'Settings' }, client);

        expect(client.goTo).not.toHaveBeenCalled();
        expect(client.openMail).not.toHaveBeenCalled();
    });

    it('leaves the reader where they were where the notification was never a place to go', () => {
        const client = following();

        followTarget({ kind: 'Nothing' }, client);

        expect(client.goTo).not.toHaveBeenCalled();
        expect(client.openMail).not.toHaveBeenCalled();
    });
});
