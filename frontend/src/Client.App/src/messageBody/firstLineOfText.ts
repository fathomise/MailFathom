// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailDocumentBlock } from '@mailfathom/client-backend';

/** The first line of text the blocks draw, or null where they draw none, as a footer that is only a picture does. */
export function firstLineOfText(blocks: readonly MailDocumentBlock[]): string | null {
    for (const block of blocks) {
        const line = firstLineIn(block);

        if (line !== null) {
            return line;
        }
    }

    return null;
}

function firstLineIn(block: MailDocumentBlock): string | null {
    switch (block.type) {
        case 'paragraph':
        case 'heading':
            return firstWrittenLine(block.content.map((run) => run.text).join(''));
        case 'preformatted':
            return firstWrittenLine(block.text);
        case 'list':
            return firstLineOfText(block.items.flatMap((item) => item.blocks));
        case 'table':
            return firstLineOfText(block.rows.flatMap((row) => row.cells.flatMap((cell) => cell.blocks)));
        case 'quote':
            return firstLineOfText(block.blocks);
        case 'image':
        case 'separator':
        case 'unimplemented':
            return null;
    }
}

// A footer in a message without markup opens with the `-- ` line RFC 3676 names a signature by, which says nothing.
function firstWrittenLine(text: string): string | null {
    return (
        text
            .split('\n')
            .map((line) => line.trim())
            .find((line) => line !== '' && line !== '--') ?? null
    );
}
