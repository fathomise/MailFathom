// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { OpenedCategories, RowCategories } from './CategoryLabels';

const finance = { identity: 'Finance', name: 'Finance' };
const four = [finance, ...['Legal', 'Budget 2027', 'Suppliers'].map((name) => ({ identity: name, name }))];
const longest = 'Contract renewals, addenda and indexation clauses for the master';

function rowOf(categories: readonly { readonly identity: string; readonly name: string }[]): void {
    render(
        <LocalizationProvider>
            <RowCategories categories={categories} />
        </LocalizationProvider>,
    );
}

describe('RowCategories', () => {
    it('draws two names, and both counts of the rest for the list’s width to choose between', () => {
        rowOf(four);

        expect(screen.getByText('Finance')).not.toBeNull();
        expect(screen.getByText('Legal').className).toContain('@max-list-compact:hidden');
        expect(screen.getByText('+2').parentElement?.className).toContain('@max-list-compact:hidden');
        expect(screen.getByText('+3').parentElement?.className).toContain('@max-list-compact:block');
        expect(screen.queryByText('Suppliers')).toBeNull();
    });

    it('says the rest in words to a reader who is not looking at the count', () => {
        rowOf(four);

        expect(screen.getByText('Budget 2027, Suppliers').className).toContain('sr-only');
    });

    it('draws no count where one name stands alone', () => {
        rowOf([finance]);

        expect(screen.queryByText(/^\+\d/u)).toBeNull();
    });

    it('lets the longest name give way first and a short one beside it stay whole', () => {
        rowOf([finance, { identity: longest, name: longest }]);

        const label = screen.getAllByText(longest)[0];

        expect(label?.className).toContain('truncate');
        expect(label?.className).toMatch(/(^| )shrink( |$)/u);
        expect(screen.getByText('Finance').className).toContain('shrink-0');
    });

    it('draws nothing at all for a message with no label to draw', () => {
        const { container } = render(
            <LocalizationProvider>
                <RowCategories categories={[]} />
            </LocalizationProvider>,
        );

        expect(container.textContent).toBe('');
    });
});

describe('OpenedCategories', () => {
    it('draws every name and no count', () => {
        render(
            <LocalizationProvider>
                <OpenedCategories categories={four} />
            </LocalizationProvider>,
        );

        expect(four.map(({ name }) => screen.getByText(name).textContent)).toEqual(four.map(({ name }) => name));
        expect(screen.queryByText(/^\+\d/u)).toBeNull();
    });
});
