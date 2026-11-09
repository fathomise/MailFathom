// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailDocumentBlock } from '@mailfathom/client-backend';
import { splitQuotedHistory } from './quotedHistory';

function paragraph(text: string): MailDocumentBlock {
    return {
        type: 'paragraph',
        alignment: 'Inherited',
        content: [
            {
                text,
                emphasis: { bold: false, italic: false, underline: false, strikethrough: false, monospace: false },
                foreground: null,
                size: 'Normal',
                link: null,
            },
        ],
    };
}

function quote(text: string): MailDocumentBlock {
    return { type: 'quote', depth: 1, blocks: [paragraph(text)] };
}

const separator: MailDocumentBlock = { type: 'separator' };

describe('splitQuotedHistory', () => {
    it('keeps what a reply added and folds the conversation it quoted underneath it', () => {
        const written = paragraph('Yes, Thursday works.');

        expect(splitQuotedHistory([written, quote('Does Thursday work?')])).toStrictEqual({
            contribution: [written],
            quotedHistory: [quote('Does Thursday work?')],
            attribution: null,
        });
    });

    it('folds the rule a mail client draws between a reply and what it answers along with the quotation', () => {
        const written = paragraph('Yes, Thursday works.');

        expect(splitQuotedHistory([written, separator, quote('Does Thursday work?')])).toStrictEqual({
            contribution: [written],
            quotedHistory: [separator, quote('Does Thursday work?')],
            attribution: null,
        });
    });

    it('folds every quotation a reply ended on rather than only the last of them', () => {
        const written = paragraph('Both of those are fine.');
        const older = quote('And the week after?');
        const oldest = quote('Does Thursday work?');

        expect(splitQuotedHistory([written, older, oldest])).toStrictEqual({
            contribution: [written],
            quotedHistory: [older, oldest],
            attribution: null,
        });
    });

    it('leaves a quotation somebody replied underneath where it is, because there it is what the message said', () => {
        const blocks = [quote('Does Thursday work?'), paragraph('Yes, Thursday works.')];

        expect(splitQuotedHistory(blocks)).toStrictEqual({
            contribution: blocks,
            quotedHistory: [],
            attribution: null,
        });
    });

    it('leaves a message that ends on a rule rather than on a quotation whole', () => {
        const blocks = [paragraph('Yes, Thursday works.'), separator];

        expect(splitQuotedHistory(blocks)).toStrictEqual({
            contribution: blocks,
            quotedHistory: [],
            attribution: null,
        });
    });

    it('draws a message that is nothing but quotation whole, so a forward is not a screen with nothing on it', () => {
        const blocks = [quote('The figures you asked for are attached.')];

        expect(splitQuotedHistory(blocks)).toStrictEqual({
            contribution: blocks,
            quotedHistory: [],
            attribution: null,
        });
    });

    it('folds a quotation that is all there is under a footer, the message’s own words standing above that footer', () => {
        const blocks = [quote('Does Thursday work?')];

        expect(splitQuotedHistory(blocks, true)).toStrictEqual({
            contribution: [],
            quotedHistory: blocks,
            attribution: null,
        });
    });

    it('leaves a message that quoted nothing whole', () => {
        const blocks = [paragraph('The figures you asked for are attached.')];

        expect(splitQuotedHistory(blocks)).toStrictEqual({
            contribution: blocks,
            quotedHistory: [],
            attribution: null,
        });
    });

    it('answers an empty document as one that added nothing and quoted nothing', () => {
        expect(splitQuotedHistory([])).toStrictEqual({ contribution: [], quotedHistory: [], attribution: null });
    });

    it('splits where the service named the quotation and takes the line MailFathom wrote for the fold', () => {
        const written = paragraph('Yes, Thursday works.');
        const attribution = paragraph('On Monday, Sam wrote:');
        const quoted = quote('Does Thursday work?');

        expect(splitQuotedHistory([written, attribution, quoted], false, 1)).toStrictEqual({
            contribution: [written],
            quotedHistory: [quoted],
            attribution: 'On Monday, Sam wrote:',
        });
    });

    it('keeps a named quotation with nothing written above it on the screen', () => {
        const blocks = [paragraph('Forwarded message from Sam:'), quote('The report.')];

        expect(splitQuotedHistory(blocks, false, 0)).toStrictEqual({
            contribution: blocks,
            quotedHistory: [],
            attribution: null,
        });
    });

    it('folds a named quotation with no attribution paragraph behind the generic line', () => {
        const written = paragraph('Yes.');
        const quoted = quote('Does Thursday work?');

        expect(splitQuotedHistory([written, quoted], false, 1)).toStrictEqual({
            contribution: [written],
            quotedHistory: [quoted],
            attribution: null,
        });
    });
});
