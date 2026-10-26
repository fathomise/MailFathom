// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailSpamVerdict } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { spamWarning } from './spamWords';

// The warning every opened spam message carries above its words: that it is spam, who judged it, what a dangerous kind
// asks for and whom it is written to look like, and that it is read-only. The strong form is the error tone with a
// shield glyph, the quiet form the sunken one, and both say it in words, so the tone is never the only thing saying it.

export function SpamWarning({ spam }: { readonly spam: MailSpamVerdict }) {
    const { translate } = useLocalization();
    const warning = spamWarning(spam, translate);

    return (
        <div
            role="status"
            className={`flex items-start gap-2.75 rounded-xl border px-4 py-3.25 pointer-coarse:px-3.75 pointer-coarse:py-3.5 ${
                warning.strong ? 'border-error bg-error-soft' : 'border-line bg-sunken'
            }`}
        >
            <Icon
                name={warning.strong ? 'gpp_bad' : 'report'}
                className={`mt-px size-icon-xl ${warning.strong ? 'text-error-text' : 'text-muted'}`}
            />

            <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                <p
                    className={`text-lg/snug font-semibold text-pretty ${warning.strong ? 'text-error-text' : 'text-text'}`}
                >
                    {warning.title}
                </p>

                {warning.lines.map((line, index) => (
                    <p
                        key={line}
                        className={`text-md/normal text-pretty ${index === warning.lines.length - 1 ? 'text-muted' : 'text-text'}`}
                    >
                        {line}
                    </p>
                ))}
            </div>
        </div>
    );
}
