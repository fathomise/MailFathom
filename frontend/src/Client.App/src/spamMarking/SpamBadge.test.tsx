// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { glyphOf } from '../controls/icons';
import { LocalizationProvider } from '../localization/Localization';
import { SpamBadge } from './SpamBadge';

describe('SpamBadge', () => {
    // The danger is said by a glyph and by the words beside the tone, so a reader who cannot tell the colour still
    // reads it.
    it('draws a dangerous kind with the warning glyph beside its words', () => {
        render(
            <LocalizationProvider>
                <SpamBadge subcategory="MalwareLure" />
            </LocalizationProvider>,
        );

        const badge = screen.getByText('Malware lure');

        expect(badge.querySelector('path')?.getAttribute('d')).toBe(glyphOf('warning')?.outline);
    });

    it('draws a harmless kind with its words alone', () => {
        render(
            <LocalizationProvider>
                <SpamBadge subcategory="Marketing" />
            </LocalizationProvider>,
        );

        expect(screen.getByText('Marketing').querySelector('svg')).toBeNull();
    });
});
