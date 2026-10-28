// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { useFocusKeptWhenWithdrawn } from './useFocusKeptWhenWithdrawn';

// A group withdrawn while somebody is on one of its controls hands focus to the sentence saying why, rather than letting
// the platform drop it to the body.

// The words are the test's own rather than the client's, so they are values rather than markup the catalogue rule reads.
const said = { reason: 'No connection.', choose: 'Choose', elsewhere: 'Elsewhere' };

function Group({ withdrawn }: { readonly withdrawn: boolean }) {
    const reason = useFocusKeptWhenWithdrawn<HTMLParagraphElement>(withdrawn);

    return (
        <>
            {withdrawn ? (
                <p ref={reason} tabIndex={-1}>
                    {said.reason}
                </p>
            ) : null}
            <fieldset disabled={withdrawn}>
                <button type="button">{said.choose}</button>
            </fieldset>
            <button type="button">{said.elsewhere}</button>
        </>
    );
}

describe('useFocusKeptWhenWithdrawn', () => {
    it('hands focus to the reason when the control somebody was on is withdrawn', () => {
        const view = render(<Group withdrawn={false} />);

        screen.getByRole('button', { name: 'Choose' }).focus();
        view.rerender(<Group withdrawn />);

        expect(document.activeElement).toBe(screen.getByText('No connection.'));
    });

    it('hands focus back to the group once it is offered again and the reason has gone', () => {
        const view = render(<Group withdrawn={false} />);

        screen.getByRole('button', { name: said.choose }).focus();
        view.rerender(<Group withdrawn />);
        view.rerender(<Group withdrawn={false} />);

        expect(document.activeElement).toBe(screen.getByRole('button', { name: said.choose }));
    });

    it('leaves focus where it is when it was outside the group', () => {
        const view = render(<Group withdrawn={false} />);
        const elsewhere = screen.getByRole('button', { name: 'Elsewhere' });

        elsewhere.focus();
        view.rerender(<Group withdrawn />);

        expect(document.activeElement).toBe(elsewhere);
    });
});
