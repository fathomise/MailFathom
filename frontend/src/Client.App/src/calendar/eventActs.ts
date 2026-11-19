// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { CalendarEvent, PersonalTask } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';

// What every view does with an event, which is the same four things in all four of them: draw whether it is picked
// out, open it, put it into the selection, and open its own menu — and with a task beside it, which is opened, held for
// its menu, and drawn apart where it clashes with a meeting. They travel together because a view hands them to every
// entry it draws and reads none of them itself — the selection is the screen's rather than any one view's.

export interface EventActs {
    /** The events picked out, by identity, which the selection bar names and the acts on it apply to. */
    readonly selected: readonly string[];

    readonly onOpen: (event: CalendarEvent) => void;

    /** Puts one event into the selection or takes it out. */
    readonly onToggle: (event: CalendarEvent) => void;

    /** Opens one event's own menu at the point the gesture happened. */
    readonly onPress: (event: CalendarEvent, at: MenuPoint) => void;

    /** Opens one task in the task dialog rather than the event dialog. */
    readonly onOpenTask: (task: PersonalTask) => void;

    /** Opens one task's own menu at the point the gesture happened. */
    readonly onPressTask: (task: PersonalTask, at: MenuPoint) => void;

    /** Whether a task is planned over a meeting, which draws it apart. */
    readonly clashes: (task: PersonalTask) => boolean;
}
