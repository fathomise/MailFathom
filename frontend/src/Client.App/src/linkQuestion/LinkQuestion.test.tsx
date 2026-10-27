// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { LinkOpenerContext, useLinkOpener, type FollowedLink, type OpenLink } from '../shellOperations/linkOpener';
import { LinkQuestion } from './LinkQuestion';

const pressTheLink = 'Press the link';

// A surface that opens one link the way every surface does, and says how the press settled.
function Pressing({ link }: { readonly link: FollowedLink }) {
    const openLink = useLinkOpener();
    const [settled, setSettled] = useState('waiting');

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    openLink(link).then(
                        () => {
                            setSettled('settled');
                        },
                        () => {
                            setSettled('refused');
                        },
                    );
                }}
            >
                {pressTheLink}
            </button>
            <p>{settled}</p>
        </>
    );
}

function pressing(
    link: Partial<FollowedLink> & { readonly target: string },
    opened: OpenLink = () => Promise.resolve(),
) {
    const followed: FollowedLink = { picture: false, words: null, warnings: [], ...link };

    render(
        <LocalizationProvider>
            <LinkOpenerContext value={opened}>
                <LinkQuestion trustedDomains={['nordwind.example']}>
                    <Pressing link={followed} />
                </LinkQuestion>
            </LinkOpenerContext>
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: pressTheLink }));

    return followed;
}

describe('LinkQuestion', () => {
    it('opens a link to a trusted domain at once, asking nothing', () => {
        const opened = vi.fn<OpenLink>(() => Promise.resolve());
        const followed = pressing({ target: 'https://files.nordwind.example/materials.zip' }, opened);

        expect(opened).toHaveBeenCalledWith(followed);
        expect(screen.queryByRole('alertdialog')).toBeNull();
    });

    it('asks before a link to any other host, naming the host and the whole address, and opens nothing yet', () => {
        const opened = vi.fn<OpenLink>(() => Promise.resolve());
        pressing({ target: 'https://nordwind-billing.example/invoice?ref=42' }, opened);

        const asked = screen.getByRole('alertdialog', { name: 'Open this link?' });

        expect(asked.textContent).toContain('nordwind-billing.example');
        expect(asked.textContent).toContain('https://nordwind-billing.example/invoice?ref=42');
        expect(opened).not.toHaveBeenCalled();
    });

    it('puts focus on Cancel, the answer that costs nothing', async () => {
        pressing({ target: 'https://nordwind-billing.example/' });

        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Cancel' }));
        });
    });

    it('settles a press another one replaced while it was asked, and opens only the later one', async () => {
        const opened = vi.fn<OpenLink>(() => Promise.resolve());
        const followed = pressing({ target: 'https://nordwind-billing.example/' }, opened);

        fireEvent.click(screen.getByRole('button', { name: pressTheLink }));

        await waitFor(() => {
            expect(screen.getByText('settled')).toBeDefined();
        });
        fireEvent.click(screen.getByRole('button', { name: 'Open link' }));

        expect(opened).toHaveBeenCalledTimes(1);
        expect(opened).toHaveBeenCalledWith(followed);
    });

    it('opens the link once Open link is pressed, and settles the press', async () => {
        const opened = vi.fn<OpenLink>(() => Promise.resolve());
        const followed = pressing({ target: 'https://nordwind-billing.example/' }, opened);

        fireEvent.click(screen.getByRole('button', { name: 'Open link' }));

        expect(opened).toHaveBeenCalledTimes(1);
        expect(opened).toHaveBeenCalledWith(followed);
        await waitFor(() => {
            expect(screen.getByText('settled')).toBeDefined();
        });
    });

    it('opens nothing on Cancel, and settles the press as an answer rather than a failure', async () => {
        const opened = vi.fn<OpenLink>(() => Promise.resolve());
        pressing({ target: 'https://nordwind-billing.example/' }, opened);

        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        await waitFor(() => {
            expect(screen.getByText('settled')).toBeDefined();
        });
        expect(opened).not.toHaveBeenCalled();
    });

    it('closes on a press of the scrim, opening nothing', () => {
        const opened = vi.fn<OpenLink>(() => Promise.resolve());
        pressing({ target: 'https://nordwind-billing.example/' }, opened);

        fireEvent.click(screen.getByRole('alertdialog'));

        expect(screen.queryByRole('alertdialog')).toBeNull();
        expect(opened).not.toHaveBeenCalled();
    });

    it('reports an opener that refused to the surface that asked', async () => {
        pressing(
            { target: 'https://nordwind-billing.example/' },
            vi.fn<OpenLink>(() => Promise.reject(new Error('nothing opened it'))),
        );

        fireEvent.click(screen.getByRole('button', { name: 'Open link' }));

        await waitFor(() => {
            expect(screen.getByText('refused')).toBeDefined();
        });
    });

    it('asks about the link behind a picture where a picture was pressed', () => {
        pressing({ target: 'https://hotel.example/offer', picture: true });

        expect(screen.getByRole('alertdialog', { name: 'Open the link behind this picture?' })).toBeDefined();
    });

    it('repeats the warnings the link already carries', () => {
        pressing({
            target: 'https://example.invalid/agreement',
            warnings: ['This address is in none of the messages this answer cites.'],
        });

        expect(screen.getByText('This address is in none of the messages this answer cites.')).toBeDefined();
    });

    it('says so where the words of the link name another host than it goes to', () => {
        pressing({ target: 'https://click.travelmail.example/c/42', words: 'travel.example' });

        expect(
            screen.getByText('Its words name travel.example, but it goes to click.travelmail.example.'),
        ).toBeDefined();
    });
});
