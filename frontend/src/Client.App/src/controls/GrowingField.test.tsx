// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { GrowingField } from './GrowingField';

// What the field is called on the screen this stands in for, held as a fixture rather than as copy in the markup.
const label = 'Field';

describe('GrowingField', () => {
    it('hands the element to a ref once rather than on every render, so a ref that focuses the field runs on attaching', () => {
        const attached = vi.fn<(element: HTMLTextAreaElement | null) => void>();
        const { rerender } = render(
            <GrowingField ref={attached} aria-label={label} value="" onChange={() => undefined} />,
        );

        rerender(<GrowingField ref={attached} aria-label={label} value="written" onChange={() => undefined} />);
        rerender(<GrowingField ref={attached} aria-label={label} value="written again" onChange={() => undefined} />);

        expect(attached).toHaveBeenCalledTimes(1);
        expect(attached).toHaveBeenCalledWith(expect.any(HTMLTextAreaElement));
    });
});
