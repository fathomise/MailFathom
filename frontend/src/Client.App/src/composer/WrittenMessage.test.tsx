// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { WrittenMessage } from './WrittenMessage';
import {
    newTable,
    withColumnInserted,
    withColumnRemoved,
    withRowAppended,
    withRowInserted,
    withRowRemoved,
} from './writtenTable';
import { htmlOf, type WrittenMarkup, type WrittenNode } from './writtenText';

// Two things this component asks the platform for are not in jsdom, and both are stated here rather than mocked away:
// what the pointer can do, which decides whether the bar or the toggle is drawn, and the editing command itself, which
// is the browser's own and is recorded so that each control can be shown to perform its own act.

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');
const declaredExecCommand = Object.getOwnPropertyDescriptor(document, 'execCommand');

let pointerIsCoarse = false;
let commanded: { readonly command: string; readonly argument: string | undefined }[] = [];
let selectedWhenCommanded: string[] = [];

/** What was selected at the moment the region was asked to act, which is what every one of these commands reads. */
function selectedNow(): string {
    const selection = window.getSelection();

    return selection === null || selection.rangeCount === 0 ? '' : selection.getRangeAt(0).toString();
}

/** The editing acts the region was asked to perform, without the styling switch every act sets first. */
function acts(): readonly { readonly command: string; readonly argument: string | undefined }[] {
    return commanded.filter((act) => act.command !== 'styleWithCSS');
}

beforeEach(() => {
    pointerIsCoarse = false;
    commanded = [];
    selectedWhenCommanded = [];

    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: pointerIsCoarse && query.includes('coarse'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });

    Object.defineProperty(document, 'execCommand', {
        configurable: true,
        value: (command: string, _showUserInterface: boolean, argument?: string) => {
            commanded.push({ command, argument });

            if (command !== 'styleWithCSS') {
                selectedWhenCommanded.push(selectedNow());
            }

            return true;
        },
    });
});

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }

    if (declaredExecCommand === undefined) {
        Reflect.deleteProperty(document, 'execCommand');
    } else {
        Object.defineProperty(document, 'execCommand', declaredExecCommand);
    }
});

function drawWords(opened: readonly WrittenNode[] = []): {
    written: ReturnType<typeof vi.fn>;
    sendAsked: ReturnType<typeof vi.fn>;
} {
    const written = vi.fn();
    const sendAsked = vi.fn();

    render(
        <LocalizationProvider>
            <WrittenMessage opened={opened} onWritten={written} onSendAsked={sendAsked} />
        </LocalizationProvider>,
    );

    return { written, sendAsked };
}

function words(): HTMLElement {
    return screen.getByRole('textbox', { name: 'Message' });
}

function press(name: string): void {
    fireEvent.click(screen.getByRole('button', { name }));
}

/** Selects one run of what is written, the way somebody dragging across it would. */
function select(text: string): void {
    const walk = document.createTreeWalker(words(), NodeFilter.SHOW_TEXT);
    const range = document.createRange();

    while (walk.nextNode()) {
        if (walk.currentNode.nodeValue === text) {
            range.selectNodeContents(walk.currentNode);
        }
    }

    const selection = window.getSelection();

    selection?.removeAllRanges();
    selection?.addRange(range);
}

describe('WrittenMessage', () => {
    it('opens holding what this tab was already writing', () => {
        drawWords([{ element: 'div', address: null, holds: [{ text: 'Half a sentence' }] }]);

        expect(words().textContent).toBe('Half a sentence');
    });

    it('reports what was typed as the closed set the message is kept in', () => {
        const { written } = drawWords();

        words().textContent = 'Here it is.';
        fireEvent.input(words());

        expect(written).toHaveBeenCalledWith([{ text: 'Here it is.' }]);
    });

    it('asks the send question from the design’s own shortcut', () => {
        const { sendAsked } = drawWords();

        fireEvent.keyDown(words(), { key: 'Enter', ctrlKey: true });

        expect(sendAsked).toHaveBeenCalledTimes(1);
    });

    it('takes what was pasted as text rather than as whatever markup it arrived in', () => {
        drawWords();

        fireEvent.paste(words(), { clipboardData: { getData: () => 'Pasted from somewhere else' } });

        expect(acts()).toEqual([{ command: 'insertText', argument: 'Pasted from somewhere else' }]);
    });
});

describe('WrittenMessage, a pasted address', () => {
    const address = 'https://example.invalid/invoice';

    // A paste lands in the region somebody is writing in, so the region holds the focus before the selection is made:
    // jsdom collapses a selection when an unfocused region takes the focus, which a browser's paste never does.
    function pasteText(text: string): void {
        fireEvent.paste(words(), { clipboardData: { getData: () => text } });
    }

    function placeTheCaret(): void {
        words().focus();
        window.getSelection()?.collapse(words(), 0);
    }

    it('makes the selected words a link to it, keeping them as they were', () => {
        drawWords([{ element: 'div', address: null, holds: [{ text: 'the invoice' }] }]);

        words().focus();
        select('the invoice');
        pasteText(address);

        expect(acts()).toEqual([{ command: 'createLink', argument: address }]);
        expect(selectedWhenCommanded).toEqual(['the invoice']);
    });

    it('inserts a link showing the address, followed by a space, where nothing is selected', () => {
        drawWords();

        placeTheCaret();
        pasteText(` ${address}\n`);

        expect(acts()).toEqual([{ command: 'insertHTML', argument: `<a href="${address}">${address}</a>\u00a0` }]);
    });

    it.each(['javascript:alert(1)', 'data:text/html,<b>', '/drafts/1', `${address} and the rest`])(
        'pastes %j as text rather than as a link',
        (pasted) => {
            drawWords([{ element: 'div', address: null, holds: [{ text: 'the invoice' }] }]);

            select('the invoice');
            pasteText(pasted);

            expect(acts()).toEqual([{ command: 'insertText', argument: pasted }]);
        },
    );

    it('pastes it as text under Ctrl+Shift+V, and as a link again on the next Ctrl+V', () => {
        drawWords();

        placeTheCaret();
        fireEvent.keyDown(words(), { key: 'V', ctrlKey: true, shiftKey: true });
        pasteText(address);
        fireEvent.keyDown(words(), { key: 'v', ctrlKey: true });
        pasteText(address);

        expect(acts().map((act) => act.command)).toEqual(['insertText', 'insertHTML']);
    });
});

describe('WrittenMessage, the formatting bar', () => {
    it.each([
        ['Bold', 'bold', undefined],
        ['Italic', 'italic', undefined],
        ['Underline', 'underline', undefined],
        ['Strikethrough', 'strikeThrough', undefined],
        ['Bulleted list', 'insertUnorderedList', undefined],
        ['Numbered list', 'insertOrderedList', undefined],
        ['Quote', 'formatBlock', 'blockquote'],
        ['Clear formatting', 'removeFormat', undefined],
    ])('performs %s on the selection', (named, command, argument) => {
        drawWords();

        press(named);

        expect(acts()).toEqual([{ command, argument }]);
    });

    it('draws the design’s controls in the design’s own order, each named for the choice it holds', () => {
        drawWords();

        const named = screen
            .getAllByRole('button')
            .map((control) => control.getAttribute('aria-label'))
            .filter((name) => name !== null);

        expect(named).toEqual([
            'Bold',
            'Italic',
            'Underline',
            'Strikethrough',
            'Text colour - Automatic',
            'Text size - Normal',
            'Alignment - Align left',
            'Bulleted list',
            'Numbered list',
            'Quote',
            'Insert a link',
            'Insert table',
            'Clear formatting',
        ]);
    });

    it('reports what the message became once a control has acted on it', () => {
        const { written } = drawWords();

        words().textContent = 'Here it is.';
        press('Bold');

        expect(written).toHaveBeenCalledWith([{ text: 'Here it is.' }]);
    });

    it('asks where a link goes before it makes one', () => {
        drawWords();

        press('Insert a link');

        expect(screen.getByLabelText('Link address')).toBeDefined();
        expect(acts()).toEqual([]);
    });

    it('makes the link once the address is written', () => {
        drawWords();

        press('Insert a link');
        fireEvent.change(screen.getByLabelText('Link address'), {
            target: { value: 'https://example.invalid/invoice' },
        });
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Enter' });

        expect(acts()).toEqual([{ command: 'createLink', argument: 'https://example.invalid/invoice' }]);
        expect(screen.queryByLabelText('Link address')).toBeNull();
    });

    it('makes no link to somewhere this client would not follow', () => {
        drawWords();

        press('Insert a link');
        fireEvent.change(screen.getByLabelText('Link address'), { target: { value: 'javascript:alert(1)' } });
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Enter' });

        expect(acts()).toEqual([]);
        expect(screen.queryByLabelText('Link address')).toBeNull();
    });

    it('acts on what is selected now, not on what was selected when a link was given up', () => {
        drawWords([
            { element: 'div', address: null, holds: [{ text: 'alpha' }] },
            { element: 'div', address: null, holds: [{ text: 'beta' }] },
        ]);

        select('alpha');
        press('Insert a link');
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Escape' });

        select('beta');
        press('Bold');

        expect(selectedWhenCommanded).toEqual(['beta']);
    });

    it('gives the address up where somebody presses escape', () => {
        drawWords();

        press('Insert a link');
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Escape' });

        expect(screen.queryByLabelText('Link address')).toBeNull();
        expect(acts()).toEqual([]);
    });
});

describe('WrittenMessage, the colour, size, and alignment menus', () => {
    function choose(name: string): void {
        fireEvent.click(screen.getByRole('menuitemradio', { name }));
    }

    it('opens a control’s choices as a menu, the choice it holds checked and focused', () => {
        drawWords();

        press('Text size - Normal');

        const menu = screen.getByRole('menu', { name: 'Text size' });

        expect(screen.getAllByRole('menuitemradio').map((choice) => choice.getAttribute('aria-label'))).toEqual([
            'Small',
            'Normal',
            'Large',
            'Larger',
            'Largest',
        ]);
        expect(screen.getByRole('menuitemradio', { name: 'Normal' }).getAttribute('aria-checked')).toBe('true');
        expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: 'Normal' }));
        expect(screen.getByRole('button', { name: 'Text size - Normal' }).getAttribute('aria-expanded')).toBe('true');
        expect(menu).toBeDefined();
    });

    it('offers Automatic and the nine named swatches, so no colour is told by colour alone', () => {
        drawWords();

        press('Text colour - Automatic');

        expect(screen.getAllByRole('menuitemradio').map((choice) => choice.getAttribute('aria-label'))).toEqual([
            'Automatic',
            'Red',
            'Orange',
            'Amber',
            'Green',
            'Teal',
            'Blue',
            'Purple',
            'Pink',
            'Grey',
        ]);
    });

    it('colours the selection with the swatch’s own value, and the control then names that swatch', () => {
        drawWords([{ element: 'p', address: null, holds: [{ text: 'urgent' }] }]);

        select('urgent');
        press('Text colour - Automatic');
        choose('Blue');

        expect(acts()).toEqual([{ command: 'foreColor', argument: '#175cd3' }]);
        expect(selectedWhenCommanded).toEqual(['urgent']);
        expect(screen.queryByRole('menu')).toBeNull();
        expect(screen.getByRole('button', { name: 'Text colour - Blue' })).toBeDefined();
    });

    it('takes the colour off by asking for the colour the words are already drawn in', () => {
        drawWords();

        press('Text colour - Automatic');
        choose('Red');
        press('Text colour - Red');
        choose('Automatic');

        expect(acts()).toEqual([
            { command: 'foreColor', argument: '#b42318' },
            { command: 'foreColor', argument: getComputedStyle(words()).color },
        ]);
        expect(screen.getByRole('button', { name: 'Text colour - Automatic' })).toBeDefined();
    });

    it.each([
        ['Small', '2'],
        ['Normal', '3'],
        ['Large', '4'],
        ['Larger', '5'],
        ['Largest', '6'],
    ])('sizes the selection %s with the step font size %s names', (named, fontSize) => {
        drawWords();

        press('Text size - Normal');
        choose(named);

        expect(acts()).toEqual([{ command: 'fontSize', argument: fontSize }]);
        expect(screen.getByRole('button', { name: `Text size - ${named}` })).toBeDefined();
    });

    it.each([
        ['Align left', 'justifyLeft'],
        ['Centre', 'justifyCenter'],
        ['Align right', 'justifyRight'],
        ['Justify', 'justifyFull'],
    ])('aligns the paragraphs the selection reaches: %s', (named, command) => {
        drawWords();

        press('Alignment - Align left');
        choose(named);

        expect(acts()).toEqual([{ command, argument: undefined }]);
        expect(screen.getByRole('button', { name: `Alignment - ${named}` })).toBeDefined();
    });

    it('is walked by the arrow keys and left by escape, focus going back to the control it opened from', () => {
        drawWords();

        press('Alignment - Align left');
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });

        expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: 'Centre' }));

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

        expect(screen.queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Alignment - Align left' }));
        expect(acts()).toEqual([]);
    });

    it.each([
        ['ArrowUp', 'Justify'],
        ['End', 'Justify'],
        ['Home', 'Align left'],
    ])('moves to the choice %s reaches from the first: %s', (key, reached) => {
        drawWords();

        press('Alignment - Align left');
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Home' });
        fireEvent.keyDown(screen.getByRole('menu'), { key });

        expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: reached }));
    });

    it('is left by Tab, focus going back to the control it opened from', () => {
        drawWords();

        press('Text size - Normal');
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Tab' });

        expect(screen.queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Text size - Normal' }));
    });

    it('closes when the same control is pressed again, and when somebody presses somewhere else', () => {
        drawWords();

        press('Text colour - Automatic');
        press('Text colour - Automatic');

        expect(screen.queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Text colour - Automatic' }));

        press('Text colour - Automatic');
        fireEvent.pointerDown(words());

        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('closes when something is pasted into the words', () => {
        drawWords();

        press('Text size - Normal');
        fireEvent.paste(words(), { clipboardData: { getData: () => 'pasted' } });

        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('opens holding a coloured, sized, aligned message drawn as the editing commands would have written it', () => {
        drawWords([
            {
                element: 'p',
                address: null,
                alignment: 'center',
                holds: [{ element: 'span', address: null, colour: '#b42318', size: 'large', holds: [{ text: 'Due' }] }],
            },
        ]);

        const line = words().querySelector('p');
        const run = words().querySelector<HTMLElement>('font');

        expect(line?.style.textAlign).toBe('center');
        expect(run?.getAttribute('color')).toBe('#b42318');
        expect(run?.getAttribute('size')).toBe('4');
    });
});

describe('WrittenMessage, tables', () => {
    function cell(text: string, element: 'td' | 'th' = 'td'): WrittenMarkup {
        return { element, address: null, holds: [{ text }] };
    }

    const hours: WrittenMarkup = {
        element: 'table',
        address: null,
        holds: [
            { element: 'tr', address: null, holds: [cell('Day', 'th'), cell('Hours', 'th')] },
            { element: 'tr', address: null, holds: [cell('Monday'), cell('9')] },
        ],
    };

    const aLineAfter: WrittenNode = {
        element: 'p',
        address: null,
        holds: [{ element: 'br', address: null, holds: [] }],
    };

    /** The caret at the end of one cell's words, and the document told the selection moved, as a browser tells it. */
    function caretAfter(text: string): void {
        select(text);
        window.getSelection()?.collapseToEnd();
        act(() => {
            document.dispatchEvent(new Event('selectionchange'));
        });
    }

    function cellAtTheCaret(): string | null | undefined {
        const anchor = window.getSelection()?.anchorNode ?? null;

        return (anchor instanceof Element ? anchor : anchor?.parentElement)?.closest('td, th')?.textContent;
    }

    it('opens *Insert a table* on three by three with a header row, and inserts what it says with a line after it', () => {
        drawWords();

        press('Insert table');

        expect(screen.getByRole('menu', { name: 'Insert a table' })).toBeDefined();

        fireEvent.click(screen.getByRole('menuitem', { name: 'One row more' }));
        fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'The first row is a header' }));

        expect(
            screen.getByRole('menuitemcheckbox', { name: 'The first row is a header' }).getAttribute('aria-checked'),
        ).toBe('false');

        fireEvent.click(screen.getByRole('menuitem', { name: 'Insert a 4 × 3 table' }));

        expect(acts()).toEqual([
            { command: 'insertHTML', argument: htmlOf([newTable(4, 3, false), aLineAfter], 'editor') },
        ]);
        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('says why a count goes no lower, and leaves the count where it is', () => {
        drawWords();

        press('Insert table');
        fireEvent.click(screen.getByRole('menuitem', { name: 'One column fewer' }));
        fireEvent.click(screen.getByRole('menuitem', { name: 'One column fewer' }));

        const least = screen.getByRole('menuitem', { name: 'Columns - at least 1' });

        expect(least.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(least);

        expect(screen.getByRole('menuitem', { name: 'Insert a 3 × 1 table' })).toBeDefined();
    });

    it('opens holding a table drawn with the grid the HTML part writes', () => {
        drawWords([hours]);

        const header = words().querySelector('th');

        expect(header?.textContent).toBe('Day');
        expect(header?.style.textAlign).toBe('left');
        expect(header?.style.verticalAlign).toBe('top');
        expect(words().querySelectorAll('tbody > tr')).toHaveLength(2);
    });

    it('stands the table’s strip while the caret is in a cell, and marks what a cell cannot hold', () => {
        drawWords([hours]);

        expect(screen.queryByRole('toolbar', { name: 'Table' })).toBeNull();

        caretAfter('9');

        const strip = screen.getByRole('toolbar', { name: 'Table' });

        expect(Array.from(strip.querySelectorAll('button'), (control) => control.textContent)).toEqual([
            'Row above',
            'Row below',
            'Column before',
            'Column after',
            'Remove row',
            'Remove column',
            'Delete table',
        ]);

        const list = screen.getByRole('button', {
            name: 'Bulleted list - a table cell holds words, colour and size only',
        });

        expect(list.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(list);
        press('Insert table - a table cell holds words, colour and size only');

        expect(acts()).toEqual([]);
        expect(screen.queryByRole('menu')).toBeNull();
    });

    it.each([
        ['Row above', withRowInserted(hours, { row: 1, cell: 1 }, 'before').table],
        ['Row below', withRowInserted(hours, { row: 1, cell: 1 }, 'after').table],
        ['Column before', withColumnInserted(hours, { row: 1, cell: 1 }, 'before').table],
        ['Column after', withColumnInserted(hours, { row: 1, cell: 1 }, 'after').table],
        ['Remove row', withRowRemoved(hours, { row: 1, cell: 1 }).table],
        ['Remove column', withColumnRemoved(hours, { row: 1, cell: 1 }).table],
    ])('writes %s over the whole table as one insertion', (named, edited) => {
        drawWords([hours]);

        caretAfter('9');
        fireEvent.click(screen.getByRole('button', { name: named }));

        expect(acts()).toEqual([
            { command: 'insertHTML', argument: htmlOf(edited === null ? [] : [edited], 'editor') },
        ]);
        expect(selectedWhenCommanded).toEqual(['DayHoursMonday9']);
    });

    it('takes the whole table away as one step', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.click(screen.getByRole('button', { name: 'Delete table' }));

        expect(acts()).toEqual([{ command: 'delete', argument: undefined }]);
    });

    it('walks the cells with Tab, and adds a row with Tab in the last one', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.keyDown(words(), { key: 'Tab' });

        expect(cellAtTheCaret()).toBe('9');
        expect(acts()).toEqual([]);

        fireEvent.keyDown(words(), { key: 'Tab' });

        expect(acts()).toEqual([
            { command: 'insertHTML', argument: htmlOf([withRowAppended(hours).table ?? hours], 'editor') },
        ]);
    });

    it('walks back with Shift+Tab, and leaves the table to the browser before the first cell', () => {
        drawWords([hours]);

        caretAfter('Hours');

        expect(fireEvent.keyDown(words(), { key: 'Tab', shiftKey: true })).toBe(false);
        expect(cellAtTheCaret()).toBe('Day');
        expect(fireEvent.keyDown(words(), { key: 'Tab', shiftKey: true })).toBe(true);
    });

    it('breaks a line in a cell on Enter, a paragraph being a block no cell holds', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.keyDown(words(), { key: 'Enter' });

        expect(acts()).toEqual([{ command: 'insertLineBreak', argument: undefined }]);
    });

    it('pastes into a cell on one line', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.paste(words(), { clipboardData: { getData: () => 'closed\nall day' } });

        expect(acts()).toEqual([{ command: 'insertText', argument: 'closed all day' }]);
    });
});

describe('WrittenMessage, the two pointer compositions', () => {
    it('draws the bar outright where a mouse drives the screen, with the hint beside it', () => {
        drawWords();

        expect(screen.getByRole('toolbar', { name: 'Formatting' })).toBeDefined();
        expect(screen.getByText('Formatting acts on whatever you have selected.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Text formatting' })).toBeNull();
    });

    it('draws the toggle in place of the bar where a finger drives the screen', () => {
        pointerIsCoarse = true;

        drawWords();

        expect(screen.getByRole('button', { name: 'Text formatting' })).toBeDefined();
        expect(screen.queryByRole('toolbar', { name: 'Formatting' })).toBeNull();
        expect(screen.queryByText('Formatting acts on whatever you have selected.')).toBeNull();
    });

    it('draws the bar under a finger once the toggle asks for it', () => {
        pointerIsCoarse = true;

        drawWords();
        press('Text formatting');

        expect(screen.getByRole('toolbar', { name: 'Formatting' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Hide formatting' })).toBeDefined();
    });
});
