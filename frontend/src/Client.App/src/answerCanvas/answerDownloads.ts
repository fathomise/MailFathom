// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { ClientSession } from '@mailfathom/client-backend';

// Who a file an answer hands over is downloaded as. It is the session rather than an operation, because what a
// download is asked for under is exactly what the mail screen asks under — the reader's own grant at the moment they
// press — and the renderer reaches the mail screen's own download with it. `null` is a surface that offers none, so a
// card drawn there names the file and offers nothing to press.
export const AnswerDownloadsContext = createContext<ClientSession | null>(null);

/** The session a file an answer hands over is downloaded under, or `null` where the surface drawing it offers no download. */
export function useAnswerDownloads(): ClientSession | null {
    return useContext(AnswerDownloadsContext);
}
