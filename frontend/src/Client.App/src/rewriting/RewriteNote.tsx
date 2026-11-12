// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef } from 'react';
import { Icon } from '../controls/Icon';

/**
 * The line standing where a draft's controls are while part of it is rewritten: what is being rewritten and how, and
 * once it has come back, that the struck text stays until the change is kept. It is a live region, so a screen reader
 * hears the rewrite come back. It takes the focus each time a rewrite is asked, because the control it was asked from —
 * a preset in the bar, or *Try again* — has just gone and the focus with it: from here the controls that answer the
 * rewrite are the next stop.
 */
export function RewriteNote({ said, busy }: { readonly said: string; readonly busy: boolean }) {
    const note = useRef<HTMLParagraphElement>(null);

    useEffect(() => {
        if (busy) {
            note.current?.focus();
        }
    }, [busy]);

    return (
        <p
            ref={note}
            tabIndex={-1}
            role="status"
            aria-live="polite"
            className="flex min-w-0 grow basis-55 items-center gap-2 text-base text-accent-deep text-pretty outline-none pointer-coarse:grow-0 pointer-coarse:basis-auto"
        >
            <Icon
                name={busy ? 'progress_activity' : 'auto_awesome'}
                className={`size-icon-sm shrink-0 ${busy ? 'animate-spin motion-reduce:animate-none' : ''}`}
            />
            <span className="min-w-0">{said}</span>
        </p>
    );
}
