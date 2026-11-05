// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailAccount } from '@mailfathom/client-backend';

// Which accounts the freshness line says are not current, and why. It is the one place a person reads that a change
// MailFathom holds has not reached its mail server, so the rule is stated here, as functions over the account list,
// rather than inside the line that draws it: a cause that clears within a minute never shows, and a reader has to be
// able to check that the minute is measured from what the deployment reported rather than from when this client
// happened to notice — a client opened late, or one that reconnected, does not start the minute again.

/** How long a cause must hold without a break before the account is drawn not current. */
export const freshnessGrace = 60_000;

/**
 * Why one account is not current, the most telling first.
 *
 * A mail server that cannot be reached is said with the changes waiting on it, because those are what the outage is
 * holding up; changes waiting on a server that answers are said on their own; and a run failing, an account behind,
 * or one never run are said by the account's state alone, as the line has always said them.
 */
export type StaleCause =
    | { readonly kind: 'unreachable'; readonly since: string | null; readonly waiting: number }
    | { readonly kind: 'waiting'; readonly waiting: number }
    | { readonly kind: 'state' };

export interface StaleAccount {
    readonly account: MailAccount;
    readonly cause: StaleCause;
}

/**
 * Whether a cause the deployment says began at `since` has held for the whole grace by `now`.
 *
 * An instant the deployment did not report is a cause whose start nobody watched, so it is not waited out: the account
 * is said not current at once, which is what the line said of every such account before the deployment reported any.
 */
function heldSince(since: string | null, now: number): boolean {
    return since === null || now - Date.parse(since) >= freshnessGrace;
}

/** Why this account is not current at `now`, or `null` where every cause it has is younger than the grace. */
export function staleCauseOf(account: MailAccount, now: number): StaleCause | null {
    const waiting = account.waitingChangeCount;

    if (account.synchronizationState === 'Unreachable' && heldSince(account.synchronizationStateSince, now)) {
        return { kind: 'unreachable', since: account.synchronizationStateSince, waiting };
    }

    if (waiting > 0 && heldSince(account.oldestWaitingChangeCommittedAt, now)) {
        return { kind: 'waiting', waiting };
    }

    const failing = account.synchronizationState === 'Failing' && heldSince(account.synchronizationStateSince, now);
    const behind = account.behind && heldSince(account.behindSince, now);

    return failing || behind || account.synchronizationState === 'NeverSynchronized' ? { kind: 'state' } : null;
}

/** Every account not current at `now`, in the order the account list gives them. */
export function staleAccounts(accounts: readonly MailAccount[], now: number): readonly StaleAccount[] {
    return accounts.flatMap((account) => {
        const cause = staleCauseOf(account, now);

        return cause === null ? [] : [{ account, cause }];
    });
}

/**
 * How long until some account's cause ends its grace with no new answer from the deployment, or `null` where none is
 * waiting to. It is what a timer waits out, so the line turns at the instant the minute ends rather than at whatever
 * moment something else happened to render it.
 */
export function nextFreshnessChangeIn(accounts: readonly MailAccount[], now: number): number | null {
    const ends = accounts.flatMap((account) => {
        const starts = [
            account.synchronizationState === 'Unreachable' || account.synchronizationState === 'Failing'
                ? account.synchronizationStateSince
                : null,
            account.behind ? account.behindSince : null,
            account.waitingChangeCount > 0 ? account.oldestWaitingChangeCommittedAt : null,
        ];

        return starts.flatMap((since) => (since === null ? [] : [Date.parse(since) + freshnessGrace]));
    });
    const ahead = ends.filter((end) => end > now);

    return ahead.length === 0 ? null : Math.min(...ahead) - now;
}
