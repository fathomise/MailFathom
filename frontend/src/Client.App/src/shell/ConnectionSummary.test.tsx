// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientFailureReason,
    ClientResult,
    DeploymentSession,
    MailAccount,
    MailAccountDirectory,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from './connectionStatus';
import { ConnectionSummary } from './ConnectionSummary';
import { mostReconnectionAttempts, type Connection } from './useConnection';

const workAccount: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
    behind: false,
    filesJunk: false,
    marksExternalMail: false,
    shared: false,
    synchronizationStateSince: null,
    behindSince: null,
    waitingChangeCount: 0,
    oldestWaitingChangeCommittedAt: null,
};

const archiveAccount: MailAccount = { ...workAccount, id: 'archive', displayName: 'Archive', behind: true };

const failingAccount: MailAccount = {
    ...workAccount,
    id: 'failing',
    displayName: 'Newsletters',
    synchronizationState: 'Failing',
};

const reading: ClientResult<DeploymentSession> = {
    outcome: 'read',
    value: {
        version: '0.8.7',
        permissions: ['mailfathom.mail.read'],
        telemetryLevel: 'info',
        revision: null,
        organization: null,
        organizationName: null,
    },
};

function directory(
    synchronizationEnabled: boolean,
    accounts: readonly MailAccount[],
): ClientResult<MailAccountDirectory> {
    return { outcome: 'read', value: { synchronizationEnabled, accounts } };
}

function failing(reason: ClientFailureReason, status: number | null = 503): ClientResult<never> {
    return { outcome: 'failed', failure: { reason, status } };
}

const disconnectedStatus: ConnectionStatus = {
    level: 'disconnected',
    cause: 'unanswered',
    since: 0,
    attempts: 1,
    retry: () => undefined,
    detailsOpen: false,
    showDetails: () => undefined,
    disconnectedNow: () => false,
};

/** Renders the summary over the connection given, and answers how to hand the same summary another one. */
function renderSummary(
    connection: Partial<Connection>,
    status: ConnectionStatus | null = null,
): (next: Partial<Connection>) => void {
    function tree(over: Partial<Connection>) {
        const summary = (
            <ConnectionSummary
                connection={{
                    session: reading,
                    accounts: null,
                    online: true,
                    attempts: 0,
                    unansweredSince: null,
                    accountsReadAt: null,
                    reread: () => undefined,
                    channelDropped: () => undefined,
                    readUnderWay: () => false,
                    channelStands: () => undefined,
                    refresh: () => undefined,
                    ...over,
                }}
            />
        );

        return (
            <LocalizationProvider>
                {status === null ? (
                    summary
                ) : (
                    <ConnectionStatusContext value={status}>{summary}</ConnectionStatusContext>
                )}
            </LocalizationProvider>
        );
    }

    const view = render(tree(connection));

    return (next) => {
        view.rerender(tree(next));
    };
}

/** The accounts read and on the screen, which is the shape every freshness assertion below is made against. */
function showing(accounts: ClientResult<MailAccountDirectory>): Partial<Connection> {
    return { session: reading, accounts };
}

/**
 * The one gesture the design asks for. The account-by-account reading sits behind a disclosure that is closed when the
 * screen is drawn, so a test asserting on a row opens it exactly as a person would rather than reading through it.
 */
function reveal(freshness: string): void {
    fireEvent.click(screen.getByRole('button', { name: freshness }));
}

describe('ConnectionSummary', () => {
    it('says it is reaching the deployment before anything has answered', () => {
        renderSummary({ session: null });

        expect(screen.getByText('Reaching the server…')).toBeDefined();
    });

    it('says this machine is offline rather than blaming a deployment it never asked', () => {
        renderSummary({ session: null, online: false });

        expect(
            screen.getByText('This machine is offline. The client reconnects on its own when the network comes back.'),
        ).toBeDefined();
    });

    it('says only that the mailboxes are unknown once a session stands and the server is out of reach', () => {
        renderSummary(
            { ...showing(directory(true, [workAccount])), online: false },
            {
                ...disconnectedStatus,
                cause: 'offline',
            },
        );

        expect(screen.getByText('Mailbox status unknown: no connection to the MailFathom server.')).toBeDefined();
        expect(screen.queryByText(/offline/u)).toBeNull();
        expect(screen.queryByText('Every account is up to date.')).toBeNull();
    });

    it('says the mailboxes are unknown while a session that stands went unanswered on its quiet refresh', () => {
        renderSummary({ ...showing(directory(true, [workAccount])), unansweredSince: 0 }, disconnectedStatus);

        expect(screen.getByText('Mailbox status unknown: no connection to the MailFathom server.')).toBeDefined();
    });

    it('says which attempt it is on while it reaches for a deployment that did not answer', () => {
        renderSummary({ session: null, attempts: 2 });

        expect(
            screen.getByText(
                `The server did not answer. Trying again - attempt 2 of ${String(mostReconnectionAttempts)}.`,
            ),
        ).toBeDefined();
    });

    it('says another attempt is coming while the budget for one is left, and offers nothing to press', () => {
        renderSummary({ session: failing('unavailable'), attempts: 0 });

        expect(screen.getByText(/Trying again - attempt 1 of/)).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('offers the way out once it has stopped trying on its own', () => {
        renderSummary({ session: failing('unavailable'), attempts: mostReconnectionAttempts });

        expect(
            screen.getByText(`The server has not answered after ${String(mostReconnectionAttempts)} attempts.`),
        ).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('names a session answer it could not read, and offers no attempt that would repeat it', () => {
        renderSummary({ session: failing('unreadable', 200) });

        expect(
            screen.getByText('The server answered, but this client could not act on the answer: unreadable.'),
        ).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    it('says nothing about freshness where the credential may not read mail, that being said elsewhere', () => {
        renderSummary({
            session: {
                outcome: 'read',
                value: {
                    version: '0.8.7',
                    permissions: [],
                    telemetryLevel: 'info',
                    revision: null,
                    organization: null,
                    organizationName: null,
                },
            },
        });

        expect(screen.queryByText(/account/i)).toBeNull();
    });

    it('says it is reading while the accounts have not arrived', () => {
        renderSummary({ session: reading, accounts: null });

        expect(screen.getByText('Reading accounts…')).toBeDefined();
    });

    it('says every account is current when none of them is behind', () => {
        renderSummary(showing(directory(true, [workAccount])));

        expect(screen.getByText('Every account is up to date.')).toBeDefined();
    });

    it('names the one account that is not current', () => {
        renderSummary(showing(directory(true, [workAccount, archiveAccount])));

        expect(screen.getByText('Archive is not up to date.')).toBeDefined();
    });

    it('says some accounts are not current where more than one is', () => {
        renderSummary(showing(directory(true, [archiveAccount, failingAccount])));

        expect(screen.getByText('Some accounts are not up to date.')).toBeDefined();
    });

    it('says the deployment is not refreshing these accounts as its own setting rather than a missing grant', () => {
        renderSummary(showing(directory(false, [workAccount])));

        expect(
            screen.getByText(
                'This server is not refreshing the local copy of these accounts, so what you see is as current as its last run left it. That is a setting on the server rather than a permission you are missing.',
            ),
        ).toBeDefined();
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('tells a user holding no account that, and what would fill it, rather than showing a failure', () => {
        renderSummary(showing(directory(true, [])));

        expect(screen.getByText(/No mail account is configured for this user yet\./)).toBeDefined();
        expect(screen.getByText(/Your administrator declares which mailboxes the server reads for you/)).toBeDefined();
    });

    it('offers nothing to open while every account is current, the green dot being the whole of it', () => {
        renderSummary(showing(directory(true, [workAccount])));

        expect(screen.getByText('Every account is up to date.')).toBeDefined();
        expect(screen.queryByRole('button')).toBeNull();
        expect(screen.queryByText('Work')).toBeNull();
    });

    it('keeps the account-by-account reading behind the line until it is asked for', () => {
        renderSummary(showing(directory(true, [workAccount, archiveAccount])));

        expect(screen.getByRole('button', { name: 'Archive is not up to date.' })).toHaveProperty(
            'ariaExpanded',
            'false',
        );
        expect(screen.queryByText('Catching up')).toBeNull();

        reveal('Archive is not up to date.');

        expect(screen.getByRole('button', { name: 'Archive is not up to date.' })).toHaveProperty(
            'ariaExpanded',
            'true',
        );
        expect(screen.getByText('Catching up')).toBeDefined();
    });

    it('names each account that is not current and its state, then one line for the rest, and no time', () => {
        renderSummary(showing(directory(true, [workAccount, failingAccount, archiveAccount])));

        reveal('Some accounts are not up to date.');

        expect(screen.getByText('Newsletters')).toBeDefined();
        expect(screen.getByText('Stopped synchronizing')).toBeDefined();
        expect(screen.getByText('Archive')).toBeDefined();
        expect(screen.getByText('Work is up to date.')).toBeDefined();
        expect(screen.queryByText(/refreshed/)).toBeNull();
    });

    it('reads an account that has never synchronized as one with nothing taken in', () => {
        const fresh: MailAccount = {
            ...workAccount,
            synchronizationState: 'NeverSynchronized',
            lastSynchronizedAt: null,
        };

        renderSummary(showing(directory(true, [fresh])));

        reveal('Work is not up to date.');

        expect(screen.getByText('Nothing taken in yet')).toBeDefined();
    });

    it('reads an account that is merely catching up as that rather than as one that stopped', () => {
        renderSummary(showing(directory(true, [archiveAccount])));

        reveal('Archive is not up to date.');

        expect(screen.getByText('Catching up')).toBeDefined();
    });

    it('names why the accounts could not be read, and offers the way out', () => {
        renderSummary({ session: reading, accounts: failing('unavailable') });

        expect(screen.getByText(/The accounts could not be read: unavailable\./)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it.each(['unauthenticated', 'unauthorized', 'unreadable'] as const)(
        'still says what failed at a %s failure, and offers no second attempt that would repeat it',
        (reason) => {
            renderSummary({ session: reading, accounts: failing(reason, 401) });

            expect(screen.getByText(/The accounts could not be read:/)).toBeDefined();
            expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
        },
    );

    it('puts no status code on the screen', () => {
        renderSummary({ session: reading, accounts: failing('unavailable') });

        expect(screen.queryByText(/503/)).toBeNull();
    });
});

// The minute is the deployment's: each cause is measured from the instant the account list reported, and a cause that
// clears inside it never changes the line. The clock is the test's, so both sides of the minute are reached exactly.
describe('ConnectionSummary, a minute after a cause began', () => {
    const committed = '2026-08-15T09:40:00+00:00';

    beforeEach(() => {
        vi.useFakeTimers({ now: Date.parse(committed) + 30_000 });
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    const waiting: MailAccount = {
        ...workAccount,
        synchronizationStateSince: committed,
        waitingChangeCount: 2,
        oldestWaitingChangeCommittedAt: committed,
    };

    it('stays green while changes have waited less than a minute', () => {
        renderSummary(showing(directory(true, [waiting])));

        expect(screen.getByText('Every account is up to date.')).toBeDefined();
    });

    it('turns at the minute, without a new answer, and says the changes are kept and shown as made', () => {
        renderSummary(showing(directory(true, [waiting])));

        act(() => {
            vi.advanceTimersByTime(30_000);
        });

        reveal('Work is not up to date.');

        expect(screen.getByText('2 changes waiting')).toBeDefined();
        expect(
            screen.getByText(
                '2 changes have waited more than a minute for its mail server. They are kept in MailFathom, shown as made, and reach the server on their own.',
            ),
        ).toBeDefined();
    });

    it('never turns for changes that reach the mail server inside the minute', () => {
        const answerAgain = renderSummary(showing(directory(true, [waiting])));

        act(() => {
            vi.advanceTimersByTime(20_000);
        });
        answerAgain(
            showing(directory(true, [{ ...waiting, waitingChangeCount: 0, oldestWaitingChangeCommittedAt: null }])),
        );
        act(() => {
            vi.advanceTimersByTime(60_000);
        });

        expect(screen.getByText('Every account is up to date.')).toBeDefined();
    });

    it('measures from what the deployment reported, so a client that read the accounts late does not wait again', () => {
        renderSummary({ ...showing(directory(true, [waiting])), accountsReadAt: Date.parse(committed) + 61_000 });

        expect(screen.getByText('Work is not up to date.')).toBeDefined();
    });

    it('says since when a mail server has been unreachable and how many changes wait on it', () => {
        const unreachable: MailAccount = {
            ...waiting,
            synchronizationState: 'Unreachable',
            waitingChangeCount: 3,
        };

        renderSummary(showing(directory(true, [unreachable])));

        act(() => {
            vi.advanceTimersByTime(30_000);
        });

        reveal('Work is not up to date.');

        expect(screen.getByText('Mail server unreachable')).toBeDefined();
        expect(
            screen.getByText(
                /^Its mail server cannot be reached, since .+\. 3 changes are waiting\. They are kept in MailFathom and reach the mail server once it is back\.$/u,
            ),
        ).toBeDefined();
    });

    // A deployment that states no start for the outage still says what waits on it, and only the time is left out.
    it.each([
        [
            3,
            'Its mail server cannot be reached. 3 changes are waiting. They are kept in MailFathom and reach the mail server once it is back.',
        ],
        [0, 'Its mail server cannot be reached. Nothing is waiting for it.'],
    ])('says %i changes waiting on a mail server whose outage has no stated start', (count, sentence) => {
        const unreachable: MailAccount = {
            ...waiting,
            synchronizationState: 'Unreachable',
            synchronizationStateSince: null,
            waitingChangeCount: count,
            oldestWaitingChangeCommittedAt: count === 0 ? null : committed,
        };

        renderSummary(showing(directory(true, [unreachable])));
        act(() => {
            vi.advanceTimersByTime(30_000);
        });

        reveal('Work is not up to date.');

        expect(screen.getByText('Mail server unreachable')).toBeDefined();
        expect(screen.getByText(sentence)).toBeDefined();
    });
});
