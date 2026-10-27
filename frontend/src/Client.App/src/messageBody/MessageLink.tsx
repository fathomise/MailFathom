// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useContext, useState, type MouseEvent, type ReactNode } from 'react';
import type { MailDocumentLink } from '@mailfathom/client-backend';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { useLinkOpener } from '../shellOperations/linkOpener';
import { LinksNotOpened } from './linksNotOpened';

// A link whose words say one thing and whose target says another is the oldest trick in mail, and a reading pane that
// opens it without showing where it goes is participating. So where the link goes is drawn beside it rather than
// hidden in a tooltip nothing reaches from a keyboard, and the judgement about whether it deceives is the service's,
// carried on the link, so two clients cannot come to disagree about it.

// Which sentence says why. Whether to warn at all is `worthWarningAbout` at the call site, so a deployment that flags a
// link for a reason this client does not enumerate still warns somebody — under the last sentence here.
function warningsFor(link: MailDocumentLink, place: string, translate: Translate): string[] {
    const said: string[] = [];

    if (link.deception === 'DisplayedHostDiffers') {
        said.push(translate('link.warningDisplayedHostDiffers', { host: place }));
    }

    if (link.asciiHost !== null) {
        said.push(translate('link.warningAsciiHost', { host: place, asciiHost: link.asciiHost }));
    }

    return said.length === 0 ? [translate('link.warningWorthChecking', { host: place })] : said;
}

export function MessageLink({
    link,
    cautions = [],
    children,
}: {
    readonly link: MailDocumentLink;

    /** Warnings the surface draws beside the link itself, which the question before opening it repeats. */
    readonly cautions?: readonly string[];
    readonly children: ReactNode;
}) {
    const { translate } = useLocalization();
    const openLink = useLinkOpener();
    const [refused, setRefused] = useState(false);
    const notOpened = useContext(LinksNotOpened);

    // The place the link goes, as a person recognizes it. A host is absent for the one scheme that has none, and the
    // whole target is what says where a `mailto:` link goes.
    const place = link.host ?? link.target;
    const warnings = link.worthWarningAbout ? warningsFor(link, place, translate) : [];

    // The application asks for the link to be opened rather than letting the document navigate, which is what makes
    // the answer the same on both heads. What was pressed is read off the element at the press, because the words and
    // whether a picture stood in for them are what the anchor drew rather than anything the link itself carries.
    function follow(pressed: MouseEvent<HTMLAnchorElement>): void {
        pressed.preventDefault();
        setRefused(false);

        void openLink({
            target: link.target,
            picture: pressed.currentTarget.querySelector('img, svg, picture') !== null,
            words: pressed.currentTarget.textContent,
            warnings: [...warnings, ...cautions],
        }).catch(() => {
            setRefused(true);
        });
    }

    if (notOpened) {
        return (
            <>
                <span className="text-text-soft">{children}</span>{' '}
                <span className="text-sm text-muted">{translate('link.notOpened', { host: place })}</span>
            </>
        );
    }

    return (
        <>
            <a
                className="text-accent-deep underline decoration-accent-deep/50 underline-offset-2"
                href={link.target}
                rel="noopener noreferrer"
                target="_blank"
                onClick={follow}
                // A middle press opens a tab through `auxclick` rather than `click`, so it is answered the same way;
                // the right button opens the browser's own menu and is left to it.
                onAuxClick={(pressed) => {
                    if (pressed.button === 1) {
                        follow(pressed);
                    }
                }}
            >
                {children}
            </a>{' '}
            <span className="text-sm text-muted">{translate('link.goesTo', { host: place })}</span>
            {warnings.map((warning) => (
                <span className="text-sm text-warning" key={warning}>
                    {' '}
                    {warning}
                </span>
            ))}
            {refused ? <span className="text-sm text-warning"> {translate('link.couldNotOpen')}</span> : null}
        </>
    );
}
