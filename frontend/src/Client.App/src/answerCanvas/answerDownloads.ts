// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { ClientSession } from '@mailfathom/client-backend';

// Who a file an answer hands over is downloaded as, and from which conversation. It is the session rather than an
// operation, because what a download is asked for under is exactly what the mail screen asks under — the reader's own
// grant at the moment they press — and the renderer reaches the mail screen's own download with it. `null` is a surface
// that offers none, so a card drawn there names the file and offers nothing to press.

/** What a file an answer hands over is downloaded under. */
export interface AnswerDownloads {
    /** Who asks, and where. */
    readonly session: ClientSession;

    /**
     * The conversation the answer belongs to, which a report the Agent wrote is downloaded from, or `null` where the
     * surface draws no conversation — a report there is named and offered nowhere.
     */
    readonly conversation: string | null;
}

export const AnswerDownloadsContext = createContext<AnswerDownloads | null>(null);

/** What a file an answer hands over is downloaded under, or `null` where the surface drawing it offers no download. */
export function useAnswerDownloads(): AnswerDownloads | null {
    return useContext(AnswerDownloadsContext);
}
