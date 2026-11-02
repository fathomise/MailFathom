// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ReactNode } from 'react';
import type { ClientFailure, ClientFailureReason } from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';

// What the mail list says in place of rows or above them, whichever way it is read: a failed read and its way out, and
// a sentence where there is nothing to draw. The two readings say the same things in the same shapes, so they are
// stated once here.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

/**
 * A read of the list that failed, with the way out where there is one.
 *
 * @param said The sentence the failure is said in, with a `{reason}` hole.
 * @param partial Whether rows are already drawn under it, which is the partial state: what is on the screen stays, and
 * what is missing is said above it rather than replacing it.
 */
export function ListFailed({
    failure,
    said,
    partial,
    onRetry,
}: {
    readonly failure: ClientFailure;
    readonly said: MessageKey;
    readonly partial: boolean;
    readonly onRetry: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div
            className={
                partial
                    ? 'flex flex-wrap items-center gap-1.5 border-b border-line px-3 py-1.5'
                    : 'flex flex-col items-start gap-2 px-3 py-3'
            }
        >
            {/* Announced rather than merely drawn, for the reason the reading pane's failure is: a reader who heard the
                list say it was reading hears nothing at all when it stops, and the way out sits under a sentence they
                were never told about. */}
            <p className="text-sm text-warning" role="alert">
                {translate(said, { reason: translate(failureLabels[failure.reason]) })}
            </p>

            {/* Reading again is the way out of exactly one of the five failures, for the reason
                `shell/ConnectionSummary.tsx` gives: the other four repeat identically on a second attempt. */}
            {failure.reason === 'unavailable' ? (
                <SecondaryButton label={translate('connection.retry')} onActivate={onRetry} />
            ) : null}
        </div>
    );
}

/** A sentence standing where the list has nothing to draw, announced where it ends a wait somebody heard begin. */
export function ListNote({
    announced = false,
    children,
}: {
    readonly announced?: boolean;
    readonly children: ReactNode;
}) {
    return (
        <p className="px-3 py-3 text-sm text-muted" role={announced ? 'status' : undefined}>
            {children}
        </p>
    );
}
