// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { regionText } from './regionText';

function regionOf(...children: Node[]): HTMLElement {
    const region = document.createElement('div');

    region.append(...children);

    return region;
}

function paragraph(...children: (Node | string)[]): HTMLParagraphElement {
    const element = document.createElement('p');

    element.append(...children);

    return element;
}

describe('regionText', () => {
    it('reads a line to a block, across the runs of formatting inside one', () => {
        const bold = document.createElement('b');
        bold.append('accept');

        const read = regionText(regionOf(paragraph('We ', bold, ' the price.'), paragraph('See you soon.')));

        expect(read.text).toBe('We accept the price.\nSee you soon.\n');
        expect(read.rangeOf(3, 9)?.toString()).toBe('accept');
    });

    it('stands a picture on a line of its own, so no sentence around it takes it in', () => {
        const picture = document.createElement('img');
        const region = regionOf(paragraph('Here is the plan. ', picture, 'It is final.'));

        expect(regionText(region).text).toBe('Here is the plan. \n\nIt is final.\n');
    });
});
