// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ModelProvenance } from './ModelProvenance';

function drawn(announced?: boolean): HTMLElement {
    return render(
        <LocalizationProvider>
            <ModelProvenance {...(announced === undefined ? {} : { announced })} />
        </LocalizationProvider>,
    ).container;
}

describe('ModelProvenance', () => {
    it('is named for what it says, that a model wrote the text beside it', () => {
        drawn();

        expect(screen.getByRole('img', { name: 'Written by AI' })).toBeDefined();
    });

    it('says nothing to a screen reader where the surface already says it in a sentence of its own', () => {
        const container = drawn(false);

        expect(screen.queryByRole('img', { name: 'Written by AI' })).toBeNull();
        expect(container.firstElementChild?.getAttribute('aria-hidden')).toBe('true');
    });

    it('draws the glyph rather than a text badge, and is not a control', () => {
        const container = drawn();

        expect(container.textContent).toBe('');
        expect(container.querySelectorAll('svg')).toHaveLength(1);
        expect(container.querySelectorAll('button, a, input, [tabindex]')).toHaveLength(0);
    });
});
