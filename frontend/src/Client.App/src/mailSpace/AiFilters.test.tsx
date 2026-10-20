// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState, type ReactNode } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { StandingView } from '../messageList/listing';
import { ListedMailContext, nothingListed } from '../messageList/useListedMail';
import { AiFiltersShownContext } from '../preferences/aiFilters';
import { emptyWorkspace, WorkspaceContext, type Workspace } from '../workspace/useWorkspace';
import { AiFilters } from './AiFilters';

/** The workspace the section folds itself in, held as state so a fold is drawn the way the frame would draw it. */
function HeldWorkspace({ children }: { readonly children: ReactNode }) {
    const [workspace, setWorkspace] = useState<Workspace>(emptyWorkspace);

    return (
        <WorkspaceContext
            value={{
                workspace,
                revise: (change) => {
                    setWorkspace((held) => ({ ...held, ...change }));
                },
            }}
        >
            {children}
        </WorkspaceContext>
    );
}

function drawing(shown = true, stand: (view: StandingView) => void = () => undefined, folded = false): void {
    render(
        <LocalizationProvider>
            <HeldWorkspace>
                <AiFiltersShownContext value={shown}>
                    <ListedMailContext value={{ ...nothingListed, stand }}>
                        <AiFilters folded={folded} />
                    </ListedMailContext>
                </AiFiltersShownContext>
            </HeldWorkspace>
        </LocalizationProvider>,
    );
}

describe('AiFilters', () => {
    it('draws the section the design names, open until somebody folds it', () => {
        drawing();

        expect(screen.getByRole('region', { name: 'AI filters' })).toBeTruthy();
        expect(screen.getByRole('button', { name: 'AI filters', expanded: true }).getAttribute('title')).toBe(
            'Collapse AI filters',
        );
    });

    it('folds its entries away from its header and opens them again, as an account group does', () => {
        drawing();

        fireEvent.click(screen.getByRole('button', { name: 'AI filters' }));

        expect(screen.getByRole('button', { name: 'AI filters', expanded: false }).getAttribute('title')).toBe(
            'Show AI filters',
        );
        expect(screen.queryByRole('button', { name: 'Commitments' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'AI filters' }));

        expect(screen.getByRole('button', { name: 'Commitments' })).toBeTruthy();
    });

    it('folds from its symbol alone in the folded rail', () => {
        drawing(true, () => undefined, true);

        fireEvent.click(screen.getByRole('button', { name: 'AI filters' }));

        expect(screen.getByRole('button', { name: 'AI filters', expanded: false })).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Deadlines this week' })).toBeNull();
    });

    it.each([
        ['Needs a decision', 'needsDecision'],
        ['Commitments', 'commitments'],
        ['Deadlines this week', 'deadlinesThisWeek'],
    ] as const)('puts %s in force on the list in front of the reader', (named, view) => {
        const stand = vi.fn<(view: StandingView) => void>();

        drawing(true, stand);
        fireEvent.click(screen.getByRole('button', { name: named }));

        expect(stand).toHaveBeenCalledWith(view);
    });

    // Turned off is the section gone rather than a heading with nothing under it: a heading over three missing entries
    // would say the views had failed, which is a different thing from a reader having asked not to be offered them.
    it('draws nothing at all where the reader has turned the section off', () => {
        drawing(false);

        expect(screen.queryByRole('region', { name: 'AI filters' })).toBeNull();
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('says what pressing an entry will do, the design drawing no lit state to say it afterwards', () => {
        drawing();

        expect(screen.getByRole('button', { name: 'Commitments' }).getAttribute('title')).toBe(
            'AI filter: Commitments',
        );
    });

    it('keeps every entry named in the folded rail, where the names are not drawn', () => {
        drawing(true, () => undefined, true);

        expect(screen.getByRole('button', { name: 'Deadlines this week' })).toBeTruthy();
    });
});
