// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    ClientFailureReason,
    ClientResult,
    DeploymentSession,
    MailAccount,
    MailAccountDirectory,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { PendingChangesContext, nothingPending, type PendingChanges } from '../pendingChanges/usePendingChanges';
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
    shared: false,
};

const archiveAccount: MailAccount = { ...workAccount, id: 'archive', displayName: 'Archive', behind: true };

const failingAccount: MailAccount = {
    ...workAccount,
    id: 'failing',
    displayName: 'Newsletters',
    synchronizationState: 'Failing',
};

const unreachableAccount: MailAccount = { ...failingAccount, synchronizationState: 'Unreachable' };

const reading: ClientResult<DeploymentSession> = {
    outcome: 'read',
    value: {
        version: '0.8.7',
        permissions: ['mailfathom.mail.read'],
        telemetryLevel: 'info',
        revision: null,
        organization: null,
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

function renderSummary(
    connection: Partial<Connection>,
    pending: PendingChanges = nothingPending,
    status: ConnectionStatus | null = null,
): void {
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
                ...connection,
            }}
        />
    );

    render(
        <LocalizationProvider>
            <PendingChangesContext value={pending}>
                {status === null ? (
                    summary
                ) : (
                    <ConnectionStatusContext value={status}>{summary}</ConnectionStatusContext>
                )}
            </PendingChangesContext>
        </LocalizationProvider>,
    );
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
    fireEvent.click(screen.getByText(freshness));
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
        renderSummary({ ...showing(directory(true, [workAccount])), online: false }, nothingPending, {
            ...disconnectedStatus,
            cause: 'offline',
        });

        expect(screen.getByText('Mailbox status unknown: no connection to the MailFathom server.')).toBeDefined();
        expect(screen.queryByText(/offline/u)).toBeNull();
        expect(screen.queryByText('Every account is up to date.')).toBeNull();
    });

    it('says the mailboxes are unknown while a session that stands went unanswered on its quiet refresh', () => {
        renderSummary(
            { ...showing(directory(true, [workAccount])), unansweredSince: 0 },
            nothingPending,
            disconnectedStatus,
        );

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

    it('says some accounts are behind when one of them is', () => {
        renderSummary(showing(directory(true, [workAccount, archiveAccount])));

        expect(screen.getByText('Some accounts are behind.')).toBeDefined();
    });

    it('says an account stopped synchronizing rather than reporting it as merely behind', () => {
        renderSummary(showing(directory(true, [workAccount, failingAccount])));

        expect(screen.getByText('Some accounts stopped synchronizing.')).toBeDefined();
        expect(screen.queryByText('Some accounts are behind.')).toBeNull();
    });

    it('reads an account it cannot reach the same way as one that is failing', () => {
        renderSummary(showing(directory(true, [workAccount, unreachableAccount])));

        expect(screen.getByText('Some accounts stopped synchronizing.')).toBeDefined();
    });

    it('says an account stopped synchronizing even where another is only behind', () => {
        renderSummary(showing(directory(true, [archiveAccount, failingAccount])));

        expect(screen.getByText('Some accounts stopped synchronizing.')).toBeDefined();
    });

    it('says the deployment is not refreshing these accounts as its own setting rather than a missing grant', () => {
        renderSummary(showing(directory(false, [workAccount])));

        expect(
            screen.getByText(
                'This server is not refreshing the local copy of these accounts, so what you see is as current as its last run left it. That is a setting on the server rather than a permission you are missing.',
            ),
        ).toBeDefined();
        expect(screen.queryByRole('group')).toBeNull();
    });

    it('tells a user holding no account that, and what would fill it, rather than showing a failure', () => {
        renderSummary(showing(directory(true, [])));

        expect(screen.getByText(/No mail account is configured for this user yet\./)).toBeDefined();
        expect(screen.getByText(/Your administrator declares which mailboxes the server reads for you/)).toBeDefined();
    });

    it('offers nothing to open while every account is current, the green dot being the whole of it', () => {
        renderSummary(showing(directory(true, [workAccount])));

        expect(screen.getByText('Every account is up to date.')).toBeDefined();
        expect(screen.queryByRole('group')).toBeNull();
        expect(screen.queryByText('Work')).toBeNull();
    });

    it('keeps the account-by-account reading behind the line that summarizes them until it is asked for', () => {
        renderSummary(showing(directory(true, [workAccount, archiveAccount])));

        expect(screen.getByRole('group')).not.toHaveProperty('open', true);

        reveal('Some accounts are behind.');

        expect(screen.getByRole('group')).toHaveProperty('open', true);
    });

    it('names each account and its state behind the line that summarizes them, and no time', () => {
        renderSummary(showing(directory(true, [workAccount, unreachableAccount])));

        reveal('Some accounts stopped synchronizing.');

        expect(screen.getByText('Work')).toBeDefined();
        expect(screen.getByText('Up to date')).toBeDefined();
        expect(screen.getByText('Newsletters')).toBeDefined();
        expect(screen.getByText('The mail server did not answer')).toBeDefined();
        expect(screen.queryByText(/refreshed/)).toBeNull();
    });

    it('reads an account that has never synchronized as one with nothing taken in', () => {
        const fresh: MailAccount = {
            ...workAccount,
            synchronizationState: 'NeverSynchronized',
            lastSynchronizedAt: null,
        };

        renderSummary(showing(directory(true, [fresh])));

        reveal('Some accounts are behind.');

        expect(screen.getByText('Nothing taken in yet')).toBeDefined();
    });

    it('reads an account that is merely catching up as that rather than as one that stopped', () => {
        renderSummary(showing(directory(true, [archiveAccount])));

        reveal('Some accounts are behind.');

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

    // A change the mailbox has not taken yet is at its most worth reading exactly when this line has nothing else to
    // report, so it stands above every sentence below rather than inside one of their branches.
    it('says a change is still waiting even while nothing about the deployment has answered', () => {
        renderSummary(
            { session: null },
            {
                ...nothingPending,
                waiting: [{ recordId: 'record', storedEmailId: 'message', act: 'markRead' }],
            },
        );

        expect(screen.getByText('Reaching the server…')).toBeDefined();
        expect(screen.getByText('One change has not reached your mailbox yet.')).toBeDefined();
    });
});
