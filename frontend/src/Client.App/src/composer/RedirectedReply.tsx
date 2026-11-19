// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailParticipant } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

// Said under the recipients, in the warning tone and in words, because the person pressing Reply believes they are
// answering the author. It names both sides by address as well as by name, since the address is what tells a mailing
// list or an assistant from a stranger. An alert rather than a status, so a screen reader says it as the composer
// opens rather than only when somebody walks past it.

function named({ displayName, address }: MailParticipant): string {
    return displayName === null || displayName.trim() === '' ? address : `${displayName} <${address}>`;
}

export function RedirectedReply({
    replyTo,
    authors,
}: {
    readonly replyTo: readonly MailParticipant[];
    readonly authors: readonly MailParticipant[];
}) {
    const { locale, translate } = useLocalization();
    const list = new Intl.ListFormat(locale, { style: 'long', type: 'conjunction' });

    return (
        <p
            role="alert"
            className="flex items-start gap-2.25 border-b border-warning bg-warning-soft px-3.75 py-2.25 text-sm/relaxed text-warning-text pointer-coarse:py-2.75 pointer-coarse:text-base"
        >
            <Icon name="warning" className="size-icon-md shrink-0" />
            <span className="min-w-0 flex-1 text-pretty">
                {translate('compose.replyRedirected', {
                    replyTo: list.format(replyTo.map(named)),
                    authors: list.format(authors.map(named)),
                })}
            </span>
        </p>
    );
}
