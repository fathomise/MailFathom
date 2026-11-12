// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

/** The route the user's accounts are served at, relative to the client prefix. */
export const mailAccountsRoute = '/accounts';

/** Whether the deployment's last finished attempt at an account succeeded, failed, found no mail server, or never ran. */
export type MailSynchronizationState = 'NeverSynchronized' | 'Synchronized' | 'Failing' | 'Unreachable';

/** One of the signed-in user's accounts, and how current the local copy of it is. */
export interface MailAccount {
    readonly id: string;
    readonly displayName: string;
    readonly synchronizationState: MailSynchronizationState;
    readonly lastSynchronizedAt: string | null;
    readonly behind: boolean;

    /** Whether marking a message spam moves it into this account's Spam folder, and not spam moves it out again. */
    readonly filesJunk: boolean;

    /** Whether the account is assigned to more than one person, so others read what arrives there. */
    readonly shared: boolean;

    /**
     * Since when the account has been in its synchronization state, as the deployment watched it, or `null` where the
     * answering process has not watched it reach that state.
     */
    readonly synchronizationStateSince: string | null;

    /** Since when the account has had mail its folders have not taken in yet, without a break, or `null` while it has none. */
    readonly behindSince: string | null;

    /** How many changes MailFathom holds for the account that its mail server does not have yet. Sends are not counted. */
    readonly waitingChangeCount: number;

    /** When the oldest of those changes was committed, or `null` where none waits. */
    readonly oldestWaitingChangeCommittedAt: string | null;

    /**
     * Whether the account belongs to an organization that turned the External mark on, so a received message its
     * signature does not prove is marked. Always `false` for an account in no organization.
     */
    readonly marksExternalMail: boolean;
}

/** The user's accounts, beside the deployment-wide switch that says whether any of them is being refreshed at all. */
export interface MailAccountDirectory {
    readonly synchronizationEnabled: boolean;
    readonly accounts: readonly MailAccount[];
}

// The most accounts a directory answer may carry before it is refused unread. One user holds a handful in practice,
// so the ceiling is far above anything real and exists for the case the answer is not: the array is walked and
// validated element by element, and a bound applied after that walk is not a bound.
//
// The folders route nests this same account shape, so the bound and the parser below are that route's as well: two
// copies of either would be two answers to what an account is, and the surface publishes one.
export const maximumAccountsInDirectory = 256;

const synchronizationStates: readonly MailSynchronizationState[] = [
    'NeverSynchronized',
    'Synchronized',
    'Failing',
    'Unreachable',
];

/** Reads the signed-in user's accounts, answering an expected failure as a value rather than by throwing. */
export function readMailAccounts(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<MailAccountDirectory>> {
    return spanned(`GET ${mailAccountsRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailAccountsRoute),
            headers: headersFor(session),
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const directory = parseDirectory(response.body);

        return directory === null ? failed('unreadable', response.status) : read(directory);
    });
}

function parseDirectory(body: string): MailAccountDirectory | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const record = asRecord(parsed);
    if (record === null) {
        return null;
    }

    const synchronizationEnabled = record['synchronizationEnabled'];
    const entries = record['accounts'];
    if (typeof synchronizationEnabled !== 'boolean' || !Array.isArray(entries)) {
        return null;
    }

    if (entries.length > maximumAccountsInDirectory) {
        return null;
    }

    const accounts: MailAccount[] = [];
    for (const entry of entries) {
        const account = parseMailAccount(entry);
        if (account === null) {
            return null;
        }

        accounts.push(account);
    }

    return { synchronizationEnabled, accounts };
}

/** Reads one account off a response body, or answers `null` where any field of it is missing or of the wrong shape. */
export function parseMailAccount(value: unknown): MailAccount | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const displayName = record['displayName'];
    const synchronizationState = record['synchronizationState'];
    const lastSynchronizedAt = record['lastSynchronizedAt'] ?? null;
    const behind = record['behind'];
    const filesJunk = record['filesJunk'];
    const shared = record['shared'];
    const synchronizationStateSince = record['synchronizationStateSince'];
    const behindSince = record['behindSince'];
    const waitingChangeCount = record['waitingChangeCount'];
    const oldestWaitingChangeCommittedAt = record['oldestWaitingChangeCommittedAt'];
    const marksExternalMail = record['marksExternalMail'];

    if (typeof id !== 'string' || typeof displayName !== 'string' || typeof behind !== 'boolean') {
        return null;
    }

    if (typeof filesJunk !== 'boolean' || typeof shared !== 'boolean' || typeof marksExternalMail !== 'boolean') {
        return null;
    }

    if (!isSynchronizationState(synchronizationState)) {
        return null;
    }

    if (lastSynchronizedAt !== null && typeof lastSynchronizedAt !== 'string') {
        return null;
    }

    if (!isInstantOrNothing(synchronizationStateSince) || !isInstantOrNothing(behindSince)) {
        return null;
    }

    if (!isInstantOrNothing(oldestWaitingChangeCommittedAt)) {
        return null;
    }

    if (typeof waitingChangeCount !== 'number' || !Number.isSafeInteger(waitingChangeCount) || waitingChangeCount < 0) {
        return null;
    }

    return {
        id,
        displayName,
        synchronizationState,
        lastSynchronizedAt,
        behind,
        filesJunk,
        shared,
        synchronizationStateSince,
        behindSince,
        waitingChangeCount,
        oldestWaitingChangeCommittedAt,
        marksExternalMail,
    };
}

/** Whether the value is an instant the deployment wrote, or absent; one that does not parse is refused with the rest. */
function isInstantOrNothing(value: unknown): value is string | null {
    return value === null || (typeof value === 'string' && !Number.isNaN(Date.parse(value)));
}

/** Whether the value is one of the four states this surface publishes, which the folders route answers with as well. */
export function isSynchronizationState(value: unknown): value is MailSynchronizationState {
    return typeof value === 'string' && synchronizationStates.includes(value as MailSynchronizationState);
}
