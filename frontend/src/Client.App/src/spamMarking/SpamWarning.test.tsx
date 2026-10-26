// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { SpamWarning } from './SpamWarning';

describe('SpamWarning', () => {
    it('announces a phishing message as a status with its title and every line under it', () => {
        render(
            <LocalizationProvider>
                <SpamWarning
                    spam={{
                        verdict: 'Spam',
                        decidedBy: 'Deterministic',
                        spamSubcategory: 'Phishing',
                        spamSubcategoryNamedBy: 'Model',
                        phishingRequest: 'PaymentDetails',
                        impersonatedParty: 'Courier',
                    }}
                />
            </LocalizationProvider>,
        );

        const warning = screen.getByRole('status');

        expect(warning.textContent).toContain('Phishing. This message is spam and a security risk.');
        expect(warning.textContent).toContain(
            'It asks for payment details, and it is written to look as if it came from a courier.',
        );
        expect(warning.textContent).toContain('Your mail server judged this message spam.');
        expect(warning.textContent).toContain('Do not give it anything it asks for.');
    });
});
