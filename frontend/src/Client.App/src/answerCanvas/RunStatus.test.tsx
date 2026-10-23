// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { nothingRead, type FollowedAnswer } from './followedRun';
import { RunStatus } from './RunStatus';
import type { RunStopping } from './useRunStopping';

const spend = { providerCalls: 3, tokens: 1_200, retrievedCharacters: 900, messagesRetrieved: 4 };

function renderStatus(
    answer: Partial<FollowedAnswer> = {},
    status: { readonly stopping?: RunStopping; readonly onStop?: () => void } = {},
) {
    return render(
        <LocalizationProvider>
            <RunStatus answer={{ ...nothingRead, ...answer }} {...status} />
        </LocalizationProvider>,
    );
}

describe('RunStatus', () => {
    it('says the run is working rather than drawing a bare spinner', () => {
        renderStatus();

        expect(screen.getByRole('status').textContent).toContain('Working through your mail');
    });

    it('says the mail is being searched and how many matching passages it has found, without lookups', () => {
        renderStatus({
            retrieval: { lookupsRun: 2, lookupsRefused: 0, lookupsPlanned: 5, passagesFound: 41 },
        });

        const said = screen.getByRole('status').textContent;
        expect(said).toContain('Searching your mail');
        expect(said).toContain('41');
        expect(said).not.toContain('lookups');
    });

    it('offers a control that stops the run while it is working', () => {
        const stop = vi.fn();
        renderStatus({}, { onStop: stop });

        fireEvent.click(screen.getByRole('button', { name: 'Cancel run' }));

        expect(stop).toHaveBeenCalledOnce();
    });

    it('says the run is being stopped, and offers the control no second time', () => {
        renderStatus({}, { stopping: 'asking', onStop: () => undefined });

        expect(screen.getByRole('status').textContent).toContain('Cancelling this run');
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('says the run is still going when the stop did not land, and offers the control again', () => {
        renderStatus({}, { stopping: 'refused', onStop: () => undefined });

        expect(screen.getByRole('status').textContent).toContain('still going');
        expect(screen.getByRole('button', { name: 'Cancel run' })).toBeDefined();
    });

    it('states that a stopped run was stopped rather than that it failed', () => {
        renderStatus({ running: false, ending: 'cancelled', spend }, { onStop: () => undefined });

        expect(screen.getByRole('status').textContent).toContain('You cancelled this run');
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('names when a spent period turns over instead of offering a retry that will be refused', () => {
        renderStatus({
            running: false,
            ending: 'periodSpent',
            retryAt: '2026-09-21T13:00:00+00:00',
            spend,
        });

        expect(screen.getByRole('status').textContent).toContain('allows answering to cost');
        expect(screen.getByText(/Questions can be asked again/u)).toBeDefined();
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('names no instant for the ceiling one question reaches, because asking again reaches it the same way', () => {
        renderStatus({ running: false, ending: 'runSpent', spend });

        expect(screen.getByRole('status').textContent).toContain('one question may cost');
        expect(screen.queryByText(/Questions can be asked again/u)).toBeNull();
    });

    it('says a run the deployment no longer holds is gone rather than letting it disappear', () => {
        renderStatus({ running: false, ending: 'gone' });

        expect(screen.getByRole('status').textContent).toContain('no longer holds this run');
    });

    it('says how many messages a finished run read, and nothing about calls, tokens, or what answered it', () => {
        renderStatus({ running: false, ending: 'completed', spend });

        expect(screen.getByText('Read 4 messages')).toBeDefined();
        expect(document.body.textContent).not.toMatch(/tokens|calls|characters|Answered by/u);
    });

    it('draws no read line while the run is still working, the working line already saying the search moves', () => {
        renderStatus({ spend, retrieval: { lookupsRun: 1, lookupsRefused: 0, lookupsPlanned: 5, passagesFound: 9 } });

        expect(screen.queryByText(/^Read \d/u)).toBeNull();
    });

    it('draws no read line for a run that read nothing', () => {
        renderStatus({ running: false, ending: 'retrievalRefused', spend: { ...spend, messagesRetrieved: 0 } });

        expect(screen.queryByText(/^Read \d/u)).toBeNull();
    });

    it('takes the keyboard off the control that stopped the run when the run ends', () => {
        const { rerender } = render(
            <LocalizationProvider>
                <RunStatus answer={{ ...nothingRead }} onStop={() => undefined} />
            </LocalizationProvider>,
        );

        screen.getByRole('button', { name: 'Cancel run' }).focus();

        rerender(
            <LocalizationProvider>
                <RunStatus answer={{ ...nothingRead, running: false, ending: 'completed' }} onStop={() => undefined} />
            </LocalizationProvider>,
        );

        expect(document.activeElement).toBe(screen.getByRole('status').closest('[tabindex="-1"]'));
    });
});
