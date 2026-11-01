// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import type { ReportWords } from './reportWords';

// A report that came back as mail, folded to the one line naming what it reports and when, in its mark's symbol and
// tone. A press — a click, Enter, or Space — unfolds it into the message it is, and another folds it again; the line
// stays where it is either way, so the focus a press left on it stays too. It is the one message that folds: every
// other message is drawn in full.

export function FoldedReport({
    words,
    unfolded,
    onToggle,
}: {
    readonly words: ReportWords;
    readonly unfolded: boolean;
    readonly onToggle: () => void;
}) {
    return (
        <button
            type="button"
            aria-expanded={unfolded}
            onClick={onToggle}
            className="flex min-h-9.5 w-full items-center gap-2.5 rounded-field border border-line bg-sunken ps-3 pe-1.5 text-start text-base text-text-soft transition hover:bg-hover pointer-coarse:min-h-12 pointer-coarse:ps-3.5 pointer-coarse:text-md"
        >
            <Icon name={words.icon} className={`size-icon-md shrink-0 ${words.ink}`} />

            <span className="min-w-0 flex-1 truncate">{words.line}</span>

            <span
                aria-hidden="true"
                className="flex size-7 shrink-0 items-center justify-center text-muted pointer-coarse:size-9"
            >
                <Icon name={unfolded ? 'expand_less' : 'expand_more'} className="size-icon-lg" />
            </span>
        </button>
    );
}
