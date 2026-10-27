// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { MailSpamVerdict } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type ActedMessage, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { AgentHandOverContext, type AgentHandOver } from '../routing/agentHandOver';
import { HeadActs } from './HeadActs';

const message: ActedMessage = {
    storedEmailId: '00000000-0000-4000-8000-000000000000',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};

function drawHead({
    offered = true,
    acting = message,
    acts = actsOffering(),
    compact = false,
    toolbarCarriesActs = false,
    handToAgent = null,
}: {
    readonly offered?: boolean;
    readonly acting?: ActedMessage | null;
    readonly acts?: MailboxActs;
    readonly compact?: boolean;
    readonly toolbarCarriesActs?: boolean;
    readonly handToAgent?: ((handOver: AgentHandOver) => void) | null;
} = {}): { composed: ReturnType<typeof vi.fn> } {
    const composed = vi.fn();
    const composing: Composing = { offered, drafts: false, opening: null, compose: composed, close: () => undefined };

    render(
        <LocalizationProvider>
            <AgentHandOverContext value={handToAgent}>
                <MailboxActsContext value={acts}>
                    <ComposingContext value={composing}>
                        <HeadActs compact={compact} toolbarCarriesActs={toolbarCarriesActs} message={acting} />
                    </ComposingContext>
                </MailboxActsContext>
            </AgentHandOverContext>
        </LocalizationProvider>,
    );

    return { composed };
}

function actsOffering(performed: MailboxActs['perform'] = () => undefined): MailboxActs {
    return { ...nothingActed, refusalOf: () => null, perform: performed };
}

describe('HeadActs', () => {
    it.each([
        ['Reply', 'senderOnly'],
        ['Forward', 'forward'],
    ])('opens the composer on %s, carrying the message it was pressed on', (name, answers) => {
        const { composed } = drawHead();

        fireEvent.click(screen.getByRole('button', { name }));

        expect(composed).toHaveBeenCalledWith({
            kind: 'answer',
            answers,
            storedEmailId: message.storedEmailId,
        });
    });

    it('flags the message being read', () => {
        const performed = vi.fn();
        drawHead({ acts: actsOffering(performed) });

        fireEvent.click(screen.getByRole('button', { name: 'Flag' }));

        expect(performed).toHaveBeenCalledWith('flag', [message]);
    });

    it('offers a flagged message the act that takes the flag off instead', () => {
        const performed = vi.fn();
        const flagged = { ...message, flagged: true };
        drawHead({ acting: flagged, acts: actsOffering(performed) });

        expect(screen.queryByRole('button', { name: 'Flag' })).toBeNull();
        fireEvent.click(screen.getByRole('button', { name: 'Unflag' }));

        expect(performed).toHaveBeenCalledWith('unflag', [flagged]);
    });

    it('says why the flag cannot be changed rather than answering nothing when it is pressed', () => {
        const performed = vi.fn();
        drawHead({ acts: { ...nothingActed, perform: performed } });

        const refused = screen.getByRole('button', {
            name: 'Flag - this credential may not change mail on your mail server.',
        });

        fireEvent.click(refused);

        expect(refused).toHaveProperty('ariaDisabled', 'true');
        expect(performed).not.toHaveBeenCalled();
    });

    // A flag already on its way is what the next press is read against, exactly as the read control reads one: the
    // control turns round at once rather than saying the first press is still travelling, so pressing it twice gives
    // the two directions rather than the same act submitted again.
    it('turns the flag round where one is already on its way, so the next press takes it off', () => {
        const performed = vi.fn();
        drawHead({
            acts: {
                ...actsOffering(performed),
                asked: new Map([
                    [message.storedEmailId, { act: 'flag', from: message.folder, leaves: false, destroys: false }],
                ]),
            },
        });

        expect(screen.queryByRole('button', { name: 'Flag' })).toBeNull();
        fireEvent.click(screen.getByRole('button', { name: 'Unflag' }));

        expect(performed).toHaveBeenCalledWith('unflag', [message]);
    });

    it('draws the three as what they are where the deployment refuses writing at all', () => {
        const { composed } = drawHead({ offered: false, acts: nothingActed });

        fireEvent.click(screen.getByRole('button', { name: 'Reply - not built yet' }));
        fireEvent.click(screen.getByRole('button', { name: 'Forward - not built yet' }));

        expect(composed).not.toHaveBeenCalled();
    });

    it('draws them as what they are in a head that is about no one message', () => {
        const performed = vi.fn();
        const { composed } = drawHead({ acting: null, acts: actsOffering(performed) });

        fireEvent.click(screen.getByRole('button', { name: 'Reply - not built yet' }));
        fireEvent.click(
            screen.getByRole('button', {
                name: 'Flag - nothing is open or selected for this to be about.',
            }),
        );

        expect(composed).not.toHaveBeenCalled();
        expect(performed).not.toHaveBeenCalled();
    });

    it('leaves the three to the toolbar where the toolbar above already offers them', () => {
        drawHead({ toolbarCarriesActs: true });

        expect(screen.queryByRole('button', { name: 'Reply' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Forward' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Flag' })).toBeNull();
    });

    // The design gives the way to the agent to the AI button at the thread's foot, so a head carries no second entry to
    // it even for a credential that has an agent to reach.
    it.each([false, true])('draws no way to the agent, compact: %s', (compact) => {
        drawHead({ compact, handToAgent: vi.fn() });

        expect(screen.queryByRole('button', { name: /Ask/u })).toBeNull();
        expect(screen.getByRole('button', { name: 'Reply' })).toBeDefined();
    });
});

describe('HeadActs, the spam control', () => {
    const judged: MailSpamVerdict = {
        verdict: 'Spam',
        decidedBy: 'Deterministic',
        spamSubcategory: 'Marketing',
        spamSubcategoryNamedBy: 'Person',
        phishingRequest: null,
        impersonatedParty: null,
    };

    it('marks the message spam from the control after its acts', () => {
        const toggleSpam = vi.fn();

        drawHead({ acts: { ...actsOffering(), spamOffered: true, toggleSpam } });
        fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

        expect(toggleSpam).toHaveBeenCalledWith([message], undefined);
    });

    it('offers a message marked spam Not spam alone, since spam is not answered', () => {
        const toggleSpam = vi.fn();
        const spam = { ...message, spam: judged };

        drawHead({ acting: spam, acts: { ...actsOffering(), spamOffered: true, toggleSpam } });

        expect(screen.queryByRole('button', { name: 'Reply' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Forward' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

        expect(toggleSpam).toHaveBeenCalledWith([spam], undefined);
    });

    it.each([false, true])('draws More actions after the spam control, marked spam: %s', (marked) => {
        drawHead({
            acting: marked ? { ...message, spam: judged } : message,
            acts: { ...actsOffering(), spamOffered: true },
        });

        const named = screen
            .getAllByRole('button')
            .map((button) => button.getAttribute('aria-label') ?? button.textContent);
        const spamAt = named.findIndex((name) => name === (marked ? 'Not spam' : 'Mark spam'));

        expect(spamAt).toBeGreaterThanOrEqual(0);
        expect(named[spamAt + 1]).toMatch(/^More actions/u);
    });
});
