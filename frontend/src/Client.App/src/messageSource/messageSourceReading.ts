// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { ClientResult, MailSource } from '@mailfathom/client-backend';

// How a control several components below the frame reads a message's raw source without being handed the session and
// the transport through every component between them, none of which reads either. Provided at the frame beside the
// other operations a message's head and the toolbar reach.

export interface MessageSourceReading {
    /** Reads one message's source from the deployment's local copy, which is asked only when somebody presses for it. */
    readonly read: (storedEmailId: string) => Promise<ClientResult<MailSource>>;

    /** Whether this machine has a network, which is a different thing from the deployment refusing to answer. */
    readonly online: boolean;
}

export const MessageSourceReadingContext = createContext<MessageSourceReading | null>(null);

/** The way to read a source, or `null` where no session stands behind the screen to read one with. */
export function useMessageSourceReading(): MessageSourceReading | null {
    return useContext(MessageSourceReadingContext);
}
