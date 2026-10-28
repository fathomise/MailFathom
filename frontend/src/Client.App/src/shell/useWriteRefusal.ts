// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLocalization } from '../localization/useLocalization';
import { disconnected, useConnectionStatus } from './connectionStatus';

// Whether a control that writes may be pressed, and what it says where it may not.
//
// ADR 0028 asks a client with no route to its deployment to say so rather than take an act it cannot carry, so while
// the status is red every control that writes is refused before it is used: drawn as a control that cannot act, and
// saying — on a hover, a focus, or a tap — that there is no connection to the MailFathom server. That leaves *outcome
// unknown* to the few seconds before a lost connection is noticed, rather than to every act made while it is lost.
//
// One hook rather than the same two calls at every control, because every space has controls that write and the
// sentence is one decision: a second wording of it is how two spaces come to say the same thing differently.

/**
 * Why a control that writes cannot be pressed now, by its own name, or `null` while it can.
 *
 * @returns A function taking the control's name and answering the sentence a refused control says in its place.
 */
export function useWriteRefusal(): (control: string) => string | null {
    const status = useConnectionStatus();
    const { translate } = useLocalization();
    const lost = disconnected(status);

    return (control) => (lost ? translate('connection.writeRefused', { control }) : null);
}
