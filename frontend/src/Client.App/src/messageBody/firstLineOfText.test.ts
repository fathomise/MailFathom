// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailDocumentBlock } from '@mailfathom/client-backend';
import { firstLineOfText } from './firstLineOfText';

function paragraph(...texts: readonly string[]): MailDocumentBlock {
    return {
        type: 'paragraph',
        alignment: 'Inherited',
        content: texts.map((text) => ({
            text,
            emphasis: { bold: false, italic: false, underline: false, strikethrough: false, monospace: false },
            foreground: null,
            size: 'Normal',
            link: null,
        })),
    };
}

const picture: MailDocumentBlock = {
    type: 'image',
    image: { source: 'data:image/png;base64,AAAA', alternativeText: 'A logo', width: null, height: null },
    link: null,
    alignment: 'Inherited',
};

describe('firstLineOfText', () => {
    it('joins the runs of a paragraph and stops at its first line break', () => {
        expect(firstLineOfText([paragraph('Anna ', 'Kowalska\nHead of Sales')])).toBe('Anna Kowalska');
    });

    it('passes over the signature delimiter a message without markup opens its footer with', () => {
        expect(firstLineOfText([paragraph('-- \nAnna Kowalska')])).toBe('Anna Kowalska');
    });

    it('reaches the first words behind a picture', () => {
        expect(firstLineOfText([picture, paragraph('Anna Kowalska')])).toBe('Anna Kowalska');
    });

    it('reaches the words of a footer laid out as a table', () => {
        const table: MailDocumentBlock = {
            type: 'table',
            columns: [{ widthShare: null }],
            rows: [
                {
                    isHeader: false,
                    cells: [
                        {
                            blocks: [paragraph('Anna Kowalska')],
                            alignment: 'Inherited',
                            background: null,
                            columnSpan: 1,
                            rowSpan: 1,
                        },
                    ],
                },
            ],
        };

        expect(firstLineOfText([table])).toBe('Anna Kowalska');
    });

    it('answers nothing for blocks that draw no line of text', () => {
        expect(firstLineOfText([picture, { type: 'separator' }, paragraph('  \n--')])).toBeNull();
    });
});
