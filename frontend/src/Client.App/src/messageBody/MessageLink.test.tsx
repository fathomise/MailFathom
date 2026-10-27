// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailDocumentLink } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { LinksNotOpened } from './linksNotOpened';
import { MessageLink } from './MessageLink';
import { LinkOpenerContext, type FollowedLink, type OpenLink } from '../shellOperations/linkOpener';

const honest: MailDocumentLink = {
    target: 'https://example.invalid/offer',
    host: 'example.invalid',
    asciiHost: null,
    deception: 'None',
    worthWarningAbout: false,
};

function drawing(link: MailDocumentLink, openLink: OpenLink = () => Promise.resolve()) {
    return render(
        <LocalizationProvider>
            <LinkOpenerContext value={openLink}>
                <MessageLink link={link}>{link.host ?? link.target}</MessageLink>
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

describe('MessageLink', () => {
    it('shows where the link goes before anybody follows it', () => {
        drawing(honest);

        expect(screen.getByText('goes to example.invalid')).toBeDefined();
    });

    it('shows the whole target for a scheme that has no host to name', () => {
        drawing({
            target: 'mailto:someone@example.invalid',
            host: null,
            asciiHost: null,
            deception: 'NotApplicable',
            worthWarningAbout: false,
        });

        expect(screen.getByText('goes to mailto:someone@example.invalid')).toBeDefined();
    });

    it('says so when the words of a link name one place and the link goes to another', () => {
        drawing({
            target: 'https://offers.invalid/claim',
            host: 'offers.invalid',
            asciiHost: null,
            deception: 'DisplayedHostDiffers',
            worthWarningAbout: true,
        });

        expect(screen.getByText('This link does not go where its words say. It goes to offers.invalid.')).toBeDefined();
    });

    it('names the written form of a host that reads as one thing and is spelled as another', () => {
        drawing({
            target: 'https://xn--nave-6pa.invalid/',
            host: 'naïve.invalid',
            asciiHost: 'xn--nave-6pa.invalid',
            deception: 'None',
            worthWarningAbout: true,
        });

        expect(
            screen.getByText('This link goes to naïve.invalid, which is written xn--nave-6pa.invalid.'),
        ).toBeDefined();
    });

    it('says nothing about deception for a link whose words claim nothing', () => {
        drawing(honest);

        expect(screen.queryByText(/does not go where its words say/)).toBeNull();
    });

    it('warns on a link the service judged worth warning about for a reason this client does not enumerate', () => {
        drawing({ ...honest, worthWarningAbout: true });

        expect(
            screen.getByText('This link is worth checking before you follow it. It goes to example.invalid.'),
        ).toBeDefined();
    });

    it('says nothing about a link the service did not judge worth warning about', () => {
        drawing({ ...honest, deception: 'DisplayedHostDiffers', asciiHost: 'xn--nave-6pa.invalid' });

        expect(screen.queryByText(/does not go where its words say/)).toBeNull();
        expect(screen.queryByText(/which is written/)).toBeNull();
    });

    it('asks for the link to be opened out of the application rather than letting the document navigate', () => {
        const asked: FollowedLink[] = [];
        drawing(honest, (link) => {
            asked.push(link);

            return Promise.resolve();
        });

        // `fireEvent` answers false for an event something cancelled, which is how the default navigation being
        // prevented is asserted without reaching into the handler.
        const followed = fireEvent.click(screen.getByRole('link', { name: 'example.invalid' }));

        expect(followed).toBe(false);
        expect(asked).toEqual([
            { target: 'https://example.invalid/offer', picture: false, words: 'example.invalid', warnings: [] },
        ]);
    });

    it('hands the question the warnings it drew beside the link, so they are read again before it opens', () => {
        const asked: FollowedLink[] = [];
        drawing({ ...honest, deception: 'DisplayedHostDiffers', worthWarningAbout: true }, (link) => {
            asked.push(link);

            return Promise.resolve();
        });

        fireEvent.click(screen.getByRole('link', { name: 'example.invalid' }));

        expect(asked.map((link) => link.warnings)).toEqual([
            ['This link does not go where its words say. It goes to example.invalid.'],
        ]);
    });

    it('says a picture was pressed where the link is drawn around one', () => {
        const asked: FollowedLink[] = [];
        const banner = 'Hotel banner';

        render(
            <LocalizationProvider>
                <LinkOpenerContext
                    value={(link) => {
                        asked.push(link);

                        return Promise.resolve();
                    }}
                >
                    <MessageLink link={honest}>
                        <img alt={banner} src="data:," />
                    </MessageLink>
                </LinkOpenerContext>
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('img', { name: banner }));

        expect(asked.map((link) => link.picture)).toEqual([true]);
    });

    it('answers a middle press the way it answers a click, and leaves the right button to the browser', () => {
        const asked: FollowedLink[] = [];
        drawing(honest, (link) => {
            asked.push(link);

            return Promise.resolve();
        });
        const anchor = screen.getByRole('link', { name: 'example.invalid' });

        fireEvent(anchor, new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 2 }));
        const middle = fireEvent(anchor, new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 }));

        expect(middle).toBe(false);
        expect(asked.map((link) => link.target)).toEqual(['https://example.invalid/offer']);
    });

    it('says so when the link could not be opened, rather than appearing to have done nothing', async () => {
        drawing(honest, () => Promise.reject(new Error('nothing opened it')));

        fireEvent.click(screen.getByRole('link', { name: 'example.invalid' }));

        expect(await screen.findByText('This link could not be opened.')).toBeDefined();
    });
});

describe('MessageLink, in a message marked spam', () => {
    const words = 'Claim your prize';

    function notOpened(link: MailDocumentLink) {
        return render(
            <LocalizationProvider>
                <LinkOpenerContext value={() => Promise.resolve()}>
                    <LinksNotOpened value>
                        <MessageLink link={link}>{words}</MessageLink>
                    </LinksNotOpened>
                </LinkOpenerContext>
            </LocalizationProvider>,
        );
    }

    it('draws the words of the link and where it would have gone, with nothing to press', () => {
        notOpened(honest);

        expect(screen.getByText(words)).toBeDefined();
        expect(screen.getByText('(a link to example.invalid, not opened)')).toBeDefined();
        expect(screen.queryByRole('link')).toBeNull();
    });

    it('names the whole target of a link whose scheme has no host', () => {
        notOpened({ ...honest, target: 'mailto:someone@example.invalid', host: null, deception: 'NotApplicable' });

        expect(screen.getByText('(a link to mailto:someone@example.invalid, not opened)')).toBeDefined();
    });
});
