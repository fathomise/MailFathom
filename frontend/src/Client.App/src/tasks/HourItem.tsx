// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';

const hourItemTones = {
    meeting: 'border border-line bg-rail text-text-soft',
    task: 'border border-dashed border-accent bg-panel text-text',
    clash: 'border border-dashed border-error bg-panel text-text',
    picked: 'border border-accent bg-accent-soft font-semibold text-text',
} as const;

// One thing an hour holds, in the day beside the list and in the plan dialog: a meeting, a planned task, or the task
// being planned where it would go.
export function HourItem({
    glyph,
    title,
    time,
    tone,
}: {
    readonly glyph: 'groups' | 'check_box_outline_blank';
    readonly title: string;
    readonly time: string;
    readonly tone: keyof typeof hourItemTones;
}) {
    return (
        <span
            className={`flex min-w-0 items-center gap-1.75 rounded-md px-2.25 py-1.25 text-sm ${hourItemTones[tone]}`}
        >
            <Icon name={glyph} className="size-icon-sm shrink-0 text-muted" />
            <span className="min-w-0 flex-1 truncate">{title}</span>
            <span className="shrink-0 text-xs text-muted">{time}</span>
        </span>
    );
}
