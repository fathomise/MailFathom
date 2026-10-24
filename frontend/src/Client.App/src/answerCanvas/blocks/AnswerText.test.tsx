// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import type { AnswerLink } from '@mailfathom/client-backend';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../../localization/Localization';
import { LinkOpenerContext, type OpenLink } from '../../shellOperations/linkOpener';
import { AnswerText } from './AnswerText';

const untracedWarning = 'This address is in none of the messages this answer cites.';

function judged(target: string, host: string, appearsInCitedSources: boolean, deceives = false): AnswerLink {
    return {
        link: {
            target,
            host,
            asciiHost: null,
            deception: deceives ? 'DisplayedHostDiffers' : 'None',
            worthWarningAbout: deceives,
        },
        appearsInCitedSources,
    };
}

function drawing(text: string, links: readonly AnswerLink[] = [], openLink: OpenLink = () => Promise.resolve()) {
    return render(
        <LocalizationProvider>
            <LinkOpenerContext value={openLink}>
                <AnswerText links={links} text={text} />
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

describe('AnswerText', () => {
    it('draws a sentence with no Markdown in it as the sentence', () => {
        drawing('The rate was agreed in April 2021.');

        expect(screen.getByText('The rate was agreed in April 2021.').tagName).toBe('P');
    });

    it('draws a list the model wrote as a list rather than as dashes', () => {
        drawing('Two messages ask for it:\n\n- the invoice from Anna\n- the reminder from Piotr');

        expect(screen.getAllByRole('listitem').map((item) => item.textContent)).toEqual([
            'the invoice from Anna',
            'the reminder from Piotr',
        ]);
    });

    it('draws emphasis as emphasis rather than as asterisks', () => {
        drawing('The meeting moved to **15 September 2026**, ~~14 September~~ no longer.');

        expect(screen.getByText('15 September 2026').tagName).toBe('STRONG');
        expect(screen.getByText('14 September').tagName).toBe('DEL');
    });

    it('draws raw HTML as the text it is rather than as markup', () => {
        const { container } = drawing("Say <b onclick='x()'>yes</b> to it.");

        expect(container.querySelector('b')).toBeNull();
        expect(container.textContent).toContain("<b onclick='x()'>yes</b>");
    });

    it('draws no image, so nothing the answer names is fetched', () => {
        const { container } = drawing('Opened ![pixel](https://tracker.example/p.gif) here.');

        expect(container.querySelector('img')).toBeNull();
    });

    it('draws a link the service judged, and a cited message carries, saying where it goes and warning about nothing', () => {
        const { container } = drawing('Read [the reports](https://northwind.example/reports).', [
            judged('https://northwind.example/reports', 'northwind.example', true),
        ]);

        expect(screen.getByRole('link', { name: 'the reports' })).toBeDefined();
        expect(screen.getByText('goes to northwind.example')).toBeDefined();
        expect(container.querySelector('.text-warning')).toBeNull();
    });

    it('warns about a link whose words name another host and whose address no cited message carries, each once', () => {
        drawing('Confirm at [northwind.example](https://northwind-billing.example/confirm).', [
            judged('https://northwind-billing.example/confirm', 'northwind-billing.example', false, true),
        ]);

        expect(
            screen.getByText('This link does not go where its words say. It goes to northwind-billing.example.', {
                exact: false,
            }),
        ).toBeDefined();
        expect(screen.getAllByText(untracedWarning, { exact: false })).toHaveLength(1);
    });

    it('warns about a link the service did not judge as one no cited message carries', () => {
        drawing('Read [the agreement](https://phish.example/login) first.');

        expect(screen.getByText('goes to phish.example')).toBeDefined();
        expect(screen.getByText(untracedWarning, { exact: false })).toBeDefined();
    });

    it('matches a judged link by where it goes, whatever spelling of the same address the text used', () => {
        const { container } = drawing('See <https://Northwind.example>.', [
            judged('https://northwind.example/', 'northwind.example', true),
        ]);

        expect(screen.getByRole('link')).toBeDefined();
        expect(container.querySelector('.text-warning')).toBeNull();
    });

    it('opens a link out of the application rather than navigating the answer to it', () => {
        const opened: string[] = [];
        drawing('Read [the agreement](https://example.invalid/agreement).', [], (target) => {
            opened.push(target);
            return Promise.resolve();
        });

        fireEvent.click(screen.getByRole('link', { name: 'the agreement' }));

        expect(opened).toEqual(['https://example.invalid/agreement']);
    });

    it('draws a link naming no web or mail address as its words alone', () => {
        const { container } = drawing('Read [the agreement](file:///etc/passwd) first.');

        expect(screen.queryByRole('link')).toBeNull();
        expect(container.querySelector('[href]')).toBeNull();
        expect(screen.getByText(/the agreement/u)).toBeDefined();
    });

    it('draws a heading without claiming a place in the screen’s heading order', () => {
        drawing('## Summary\n\nNothing is overdue.');

        expect(screen.queryByRole('heading')).toBeNull();
        expect(screen.getByText('Summary')).toBeDefined();
    });

    it('draws a table the model wrote as a table a keyboard can scroll', () => {
        drawing('| Sender | Date |\n| --- | --- |\n| Anna | 15 September 2026 |');

        expect(
            screen.getByRole('group', { name: 'A table in this answer, scrollable sideways' }).getAttribute('tabindex'),
        ).toBe('0');
        expect(screen.getByRole('cell', { name: 'Anna' })).toBeDefined();
    });

    it('draws a fenced code block as one preformatted region a keyboard can scroll', () => {
        drawing('Run this:\n\n```\nmfctl accounts list\n```');

        const region = screen.getByRole('group', { name: 'Preformatted text in this answer, scrollable sideways' });

        expect(region.tagName).toBe('PRE');
        expect(region.getAttribute('tabindex')).toBe('0');
        expect(region.textContent).toBe('mfctl accounts list');
    });
});
