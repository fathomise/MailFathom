// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { DraftingBlock, DraftingStanding, type DraftingContextStart } from './DraftingBlock';

// What the block asks for, which is the one thing a screen cannot see: the acts and the tone are a sentence composed
// here and sent as one instruction, and what somebody typed is the rest of it. The wording of each act is asserted by
// what it contains rather than word for word, so an instruction reworded for the writer does not fail a test about
// the control that sends it.

const invoice = { storedEmailId: 'm1', subject: 'Quarterly invoice' };

function drawBlock(
    busy = false,
    {
        context = [invoice],
        changedFrom = null,
        asked,
    }: {
        context?: readonly { storedEmailId: string; subject: string }[];
        changedFrom?: DraftingContextStart | null;
        asked?: string;
    } = {},
): {
    drafted: ReturnType<typeof vi.fn>;
    removed: ReturnType<typeof vi.fn>;
    restored: ReturnType<typeof vi.fn>;
} {
    const drafted = vi.fn();
    const removed = vi.fn();
    const restored = vi.fn();

    render(
        <LocalizationProvider>
            <DraftingBlock
                context={context}
                noContextSaid="compose.aiContextNone"
                changedFrom={changedFrom}
                {...(asked === undefined ? {} : { asked })}
                busy={busy}
                onDraft={drafted}
                onRemove={removed}
                onRestore={restored}
            />
        </LocalizationProvider>,
    );

    return { drafted, removed, restored };
}

describe('DraftingBlock', () => {
    it('offers the four acts, the three tones, and says what the draft is written against', () => {
        drawBlock();

        for (const act of ['Write a draft', 'Shorten', 'Expand', 'Fix the language']) {
            expect(screen.getByRole('button', { name: act })).toBeDefined();
        }

        for (const tone of ['Formal', 'Neutral', 'Direct']) {
            expect(screen.getByRole('radio', { name: tone })).toBeDefined();
        }

        expect(screen.getByText('Quarterly invoice')).toBeDefined();
        expect(screen.getByText('the draft stays local until you send it')).toBeDefined();
    });

    it('takes a message out of the context from its chip', () => {
        const { removed } = drawBlock();

        fireEvent.click(screen.getByRole('button', { name: 'Remove Quarterly invoice from the context' }));

        expect(removed).toHaveBeenCalledExactlyOnceWith('m1');
    });

    it('says how to give it a context while it holds none', () => {
        drawBlock(false, { context: [] });

        expect(screen.getByText('none yet - click a message in the list to draft from it')).toBeDefined();
        expect(screen.queryByRole('button', { name: /^Remove .+ from the context$/ })).toBeNull();
    });

    it('marks a context that changed in the field and in words, and offers the way back', () => {
        const { restored } = drawBlock(false, { changedFrom: { subject: 'Quarterly invoice', more: 2 } });

        expect(screen.getByRole('img', { name: 'The context changed since you started' })).toBeDefined();
        expect(screen.getByRole('status').textContent).toContain(
            'This message started from “Quarterly invoice” and 2 more - the context has changed since.',
        );
        expect(screen.queryByText('the draft stays local until you send it')).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Restore' }));

        expect(restored).toHaveBeenCalledOnce();
    });

    it('draws no mark while the context is where it started', () => {
        drawBlock();

        expect(screen.queryByRole('img', { name: 'The context changed since you started' })).toBeNull();
        expect(screen.queryByRole('status')).toBeNull();
    });

    it('opens holding what the reply was asked to say before the composer opened', () => {
        drawBlock(false, { asked: 'accept the SLA' });

        expect(screen.getByRole('textbox', { name: 'What the draft should say' })).toHaveProperty(
            'value',
            'accept the SLA',
        );
    });

    // jsdom answers every width query `false`, which is the phone.
    it('asks in words that fit one line on a phone', () => {
        drawBlock();

        expect(screen.getByRole('textbox', { name: 'What the draft should say' })).toHaveProperty(
            'placeholder',
            'Write what this is about…',
        );
    });

    it('stands on the tone nobody chose, which is the one the design draws as in force', () => {
        drawBlock();

        expect(screen.getByRole('radio', { name: 'Neutral', checked: true })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Formal', checked: false })).toBeDefined();
    });

    it('sends what the act asks for and what was typed as one instruction', () => {
        const { drafted } = drawBlock();

        fireEvent.change(screen.getByRole('textbox', { name: 'What the draft should say' }), {
            target: { value: 'ask for a 5% cap' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Shorten' }));

        const asked = drafted.mock.calls[0]?.[0] as string;

        expect(asked).toContain('short');
        expect(asked.endsWith('ask for a 5% cap')).toBe(true);
    });

    it('carries the tone that is in force, and carries none where nobody chose one', () => {
        const { drafted } = drawBlock();

        fireEvent.click(screen.getByRole('button', { name: 'Write a draft' }));

        expect(drafted.mock.calls[0]?.[0]).not.toContain('tone');

        fireEvent.click(screen.getByRole('radio', { name: 'Formal' }));
        fireEvent.click(screen.getByRole('button', { name: 'Write a draft' }));

        expect(drafted.mock.calls[1]?.[0]).toContain('formal');
    });

    it('asks for nothing on its own, however the composer was opened', () => {
        const { drafted } = drawBlock();

        expect(drafted).not.toHaveBeenCalled();
    });

    // A radio group is announced as one control the arrows choose inside, so the keyboard has to keep that promise:
    // the tone in force is the group's only tab stop, and an arrow moves both the choice and the focus.
    it('moves the tone with the arrows and takes the focus with it, the group being one tab stop', () => {
        drawBlock();

        const neutral = screen.getByRole('radio', { name: 'Neutral' });

        expect(neutral).toHaveProperty('tabIndex', 0);
        expect(screen.getByRole('radio', { name: 'Formal' })).toHaveProperty('tabIndex', -1);

        fireEvent.keyDown(neutral, { key: 'ArrowLeft' });

        const formal = screen.getByRole('radio', { name: 'Formal', checked: true });

        expect(document.activeElement).toBe(formal);
        expect(formal).toHaveProperty('tabIndex', 0);
    });

    it('wraps at the ends rather than stopping, which is what a radio group does', () => {
        drawBlock();

        fireEvent.keyDown(screen.getByRole('radio', { name: 'Neutral' }), { key: 'ArrowRight' });
        fireEvent.keyDown(screen.getByRole('radio', { name: 'Direct' }), { key: 'ArrowRight' });

        expect(screen.getByRole('radio', { name: 'Formal', checked: true })).toBeDefined();
    });

    it('refuses a second act while one is still being written', () => {
        drawBlock(true);

        expect(screen.getByRole('button', { name: 'Write a draft' })).toHaveProperty('disabled', true);
    });
});

describe('DraftingStanding', () => {
    it('says a reply is being generated, and offers nothing to act on while it is', () => {
        render(
            <LocalizationProvider>
                <DraftingStanding busy answering drafted onRestore={vi.fn()} onAccept={vi.fn()} />
            </LocalizationProvider>,
        );

        expect(screen.getByRole('status')).toHaveProperty('textContent', 'Generating a reply');
        expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull();
    });

    it('says a new message is being drafted rather than a reply', () => {
        render(
            <LocalizationProvider>
                <DraftingStanding busy answering={false} drafted={false} onRestore={vi.fn()} onAccept={vi.fn()} />
            </LocalizationProvider>,
        );

        expect(screen.getByRole('status')).toHaveProperty('textContent', 'Drafting');
    });

    it('offers the way back and the way forward once a draft stands', () => {
        const restored = vi.fn();
        const accepted = vi.fn();

        render(
            <LocalizationProvider>
                <DraftingStanding busy={false} answering drafted onRestore={restored} onAccept={accepted} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: 'Restore my version' }));
        fireEvent.click(screen.getByRole('button', { name: 'Accept' }));

        expect(restored).toHaveBeenCalledOnce();
        expect(accepted).toHaveBeenCalledOnce();
    });

    it('says nothing where no draft has been written and none is being written', () => {
        render(
            <LocalizationProvider>
                <DraftingStanding busy={false} answering drafted={false} onRestore={vi.fn()} onAccept={vi.fn()} />
            </LocalizationProvider>,
        );

        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull();
    });
});
