// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';

// Copying somebody's address out of the People space, which the design offers wherever a person cannot be changed —
// a colleague's page, a colleague's or the organization's row, and the selection bar under *Colleagues* — and answers
// with a toast either way, since a clipboard written in silence is a press nobody can tell landed.

/** Puts addresses on the clipboard, one or several, and says whether they arrived there. */
export function useAddressCopy(): (addresses: readonly string[]) => void {
    const { translate } = useLocalization();
    const toasts = useToasts();

    return (addresses) => {
        const joined = addresses.join(', ');
        const copied = translate(addresses.length === 1 ? 'people.addressCopied' : 'people.addressesCopied');

        // A clipboard the runtime does not offer — a page served over an insecure origin has none, whatever the
        // declaration says — is a copy that did not happen, said as such rather than skipped.
        const written =
            'clipboard' in window.navigator
                ? window.navigator.clipboard.writeText(joined)
                : Promise.reject(new Error('No clipboard.'));

        void written.then(
            () => {
                toasts.raise({ kind: 'success', title: copied, body: joined });
            },
            () => {
                toasts.raise({ kind: 'error', title: translate('people.notCopied') });
            },
        );
    };
}
