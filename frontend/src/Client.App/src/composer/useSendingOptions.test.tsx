// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { FollowUp } from './followUp';
import { useSendingOptions } from './useSendingOptions';

const followLine = /^If nobody replies by .+, this waits in Tasks and reminds you that day\.$/u;

function Options({ waits = true }: { readonly waits?: boolean }) {
    const [followUp, setFollowUp] = useState<FollowUp | null>(null);
    const options = useSendingOptions({
        requested: { requestsDeliveryNotification: false, requestsReadReceipt: false },
        onTurned: () => undefined,
        sendAt: null,
        zone: 'UTC',
        onSendAt: () => undefined,
        onPick: () => undefined,
        followUp: waits ? { chosen: followUp, onChosen: setFollowUp } : undefined,
    });

    return (
        <>
            {options.followLine}
            {options.control}
        </>
    );
}

function drawOptions(waits = true): void {
    render(
        <LocalizationProvider>
            <Options waits={waits} />
        </LocalizationProvider>,
    );
}

function choose(name: RegExp): void {
    fireEvent.click(screen.getByRole('button', { name: 'Sending options' }));
    fireEvent.click(screen.getByRole('menuitemradio', { name }));
}

describe('useSendingOptions', () => {
    it('offers the three days under If nobody replies, and says the one chosen above the foot', async () => {
        drawOptions();

        fireEvent.click(screen.getByRole('button', { name: 'Sending options' }));

        expect(screen.getByRole('menuitemradio', { name: /^Remind me tomorrow/u })).toBeDefined();
        expect(screen.getByRole('menuitemradio', { name: /^Remind me on Monday/u })).toBeDefined();
        expect(screen.getByRole('menuitemradio', { name: /^Remind me in a week/u })).toBeDefined();

        fireEvent.click(screen.getByRole('menuitemradio', { name: /^Remind me in a week/u }));

        expect(await screen.findByText(followLine)).toBeDefined();
    });

    it('useSendingOptions clears the follow-up when the chosen day is picked again', async () => {
        drawOptions();

        choose(/^Remind me tomorrow/u);
        expect(await screen.findByText(followLine)).toBeDefined();

        choose(/^Remind me tomorrow/u);

        expect(screen.queryByText(followLine)).toBeNull();
    });

    it("lets the follow-up go from the line with Don't wait", async () => {
        drawOptions();

        choose(/^Remind me on Monday/u);
        fireEvent.click(await screen.findByRole('button', { name: "Don't wait" }));

        expect(screen.queryByText(followLine)).toBeNull();
    });

    it('offers no follow-up where nothing waits for a reply', () => {
        drawOptions(false);

        fireEvent.click(screen.getByRole('button', { name: 'Sending options' }));

        expect(screen.queryByRole('menuitemradio', { name: /^Remind me tomorrow/u })).toBeNull();
    });
});
