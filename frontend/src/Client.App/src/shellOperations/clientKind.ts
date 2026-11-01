// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientKind } from '@mailfathom/client-backend';

// Which head this is, as a sign-in tells the deployment. It is what lets a colleague see that somebody uses the phone
// app, and nothing in the application branches on it: the sign-in passes it on, and the deployment, which records it,
// decides what a phone session means.
//
// A page no shell is hosting is the web head. A shell names itself; one that answers something this client does not
// know is the desktop, because the phone is the one head whose sessions mean anything to anybody else, and a marker
// a shell did not clearly ask for is one nobody should be shown.

/**
 * Asks the shell which head it is, once, at the composition root.
 *
 * @returns `web` where no shell is hosting the page, and the shell's own answer otherwise.
 */
export async function clientKind(): Promise<ClientKind> {
    const shell = window.__TAURI__;

    if (shell === undefined) {
        return 'web';
    }

    const answered = await shell.core.invoke('client_kind').catch(() => null);

    return answered === 'android' ? 'android' : 'desktop';
}
