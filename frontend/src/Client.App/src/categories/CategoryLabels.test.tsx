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
    it('draws two names and how many more there are', () => {
        rowOf(four);

        expect(screen.getByText('Finance')).not.toBeNull();
        expect(screen.getByText('Legal')).not.toBeNull();
        expect(screen.getByText('+2')).not.toBeNull();
        expect(screen.queryByText('Suppliers')).toBeNull();
    });

    it('says the rest in words to a reader who is not looking at the count', () => {
        rowOf(four);

        expect(screen.getByText('Budget 2027, Suppliers')).not.toBeNull();
    });

    it('draws no count where one name stands alone', () => {
        rowOf([finance]);

        expect(screen.queryByText(/^\+\d/u)).toBeNull();
    });

    it('keeps the longest name whole in the document, whatever the label shows of it', () => {
        rowOf([finance, { identity: longest, name: longest }]);

        expect(screen.getAllByText(longest)).not.toHaveLength(0);
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
