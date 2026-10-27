// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';

/** What a press on something a message marked spam refuses is answered with, one sentence per refusal. */
export type SpamRefused =
    | 'spam.refusedOriginal'
    | 'spam.refusedAnswer'
    | 'spam.refusedForward'
    | 'spam.refusedAttachmentOpen'
    | 'spam.refusedAttachmentDownload';

/**
 * Answers a press a message marked spam refuses: a warning saying what was refused and the one way back, rather than a
 * control that does nothing. The controls stay drawn, because a reader who meets one missing has no way to learn why.
 */
export function useSpamRefusal(): (refused: SpamRefused) => void {
    const { translate } = useLocalization();
    const toasts = useToasts();

    return (refused) => {
        toasts.raise({ kind: 'warning', title: translate('spam.refusedTitle'), body: translate(refused) });
    };
}
