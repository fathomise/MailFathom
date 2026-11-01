// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { reportOpening, type ClientSession, type MailFathomTransport } from '@mailfathom/client-backend';

// Telling the deployment this client was opened, which is what keeps a colleague's phone-app marker true: the marker
// shows while a phone session was opened within the last seven days, and goes away by itself when it was not.
//
// **Every head says it and the deployment decides what it means.** A session signed in from anything but the phone app
// records nothing, so no screen asks which head this is. What is kept is the day alone, so this says it once per day
// the client is in front of somebody — when it starts, and when it comes back into view on a day it had not said so
// yet — rather than on every return, which would tell the deployment nothing more.

/** The UTC day of an instant, which is the precision the deployment keeps an opening at. */
function dayOf(instant: Date): string {
    return instant.toISOString().slice(0, 10);
}

/**
 * Reports an opening while there is a session to report it on.
 *
 * @param session Who is signed in and where, or `null` where nobody is.
 * @param transport How the report goes out.
 */
export function useOpeningReport(session: ClientSession | null, transport: MailFathomTransport): void {
    useEffect(() => {
        if (session === null) {
            return;
        }

        let reportedOn: string | null = null;

        const reportIfNewDay = (): void => {
            const today = dayOf(new Date());

            if (document.visibilityState !== 'visible' || reportedOn === today) {
                return;
            }

            reportedOn = today;

            // One that never arrived is said again on the next return rather than written off for the day.
            void reportOpening(session, transport).then((answer) => {
                if (answer.outcome === 'failed' && reportedOn === today) {
                    reportedOn = null;
                }
            });
        };

        reportIfNewDay();
        document.addEventListener('visibilitychange', reportIfNewDay);

        return () => {
            document.removeEventListener('visibilitychange', reportIfNewDay);
        };
    }, [session, transport]);
}
