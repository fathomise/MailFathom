// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLocalization } from '../localization/useLocalization';
import { RewriteNote } from '../rewriting/RewriteNote';

// What the composer's foot holds while a rewrite of part of the message is in hand, in place of *Send*, *Attach* and
// *Save draft*: what is being rewritten, and once it has come back, *Keep the change*, *Try again* and *Undo*. Nothing
// is sent while a rewrite waits, so the controls that send are not there to be pressed.

const secondary =
    'inline-flex items-center justify-center rounded-md border border-line-strong px-3.5 py-2 text-base whitespace-nowrap text-text-soft transition hover:bg-hover pointer-coarse:min-h-11 pointer-coarse:px-4';

export function RewriteFoot({
    note,
    busy,
    onKeep,
    onAgain,
    onUndo,
}: {
    readonly note: string;
    readonly busy: boolean;
    readonly onKeep: () => void;
    readonly onAgain: () => void;
    readonly onUndo: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <>
            <RewriteNote said={note} busy={busy} />

            {busy ? null : (
                <span className="flex flex-wrap items-center gap-2">
                    <button
                        type="button"
                        aria-label={translate('rewrite.keepMessageName')}
                        className="inline-flex items-center justify-center rounded-md bg-accent px-3.5 py-2 text-base font-semibold whitespace-nowrap text-on-accent transition hover:bg-accent-strong pointer-coarse:min-h-11 pointer-coarse:px-4"
                        onClick={onKeep}
                    >
                        {translate('rewrite.keep')}
                    </button>
                    <button
                        type="button"
                        aria-label={translate('rewrite.againName')}
                        className={secondary}
                        onClick={onAgain}
                    >
                        {translate('rewrite.again')}
                    </button>
                    <button
                        type="button"
                        aria-label={translate('rewrite.undoMessageName')}
                        className={secondary}
                        onClick={onUndo}
                    >
                        {translate('rewrite.undo')}
                    </button>
                </span>
            )}
        </>
    );
}
