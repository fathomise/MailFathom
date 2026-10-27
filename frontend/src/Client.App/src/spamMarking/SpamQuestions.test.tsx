// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { SpamQuestions, type SpamAsking } from './SpamQuestions';

const askingSpam: SpamAsking = { kind: 'spam', serial: 1, messages: 2, outside: 2, movers: 0, moved: false };
const askingNotSpam: SpamAsking = { kind: 'notSpam', serial: 1, messages: 1, moving: 1 };

function asked(asking: SpamAsking) {
    const onSpam = vi.fn();
    const onNotSpam = vi.fn();

    render(
        <LocalizationProvider>
            <SpamQuestions asking={asking} onSpam={onSpam} onNotSpam={onNotSpam} />
        </LocalizationProvider>,
    );

    return { onSpam, onNotSpam };
}

function press(said: string): void {
    fireEvent.click(screen.getByRole('button', { name: said }));
}

// The platform's close fires its event as a task of its own, so a test asserting that nothing was done hands the
// dialog its close event itself rather than asserting before the task could have run.
function closed(dialog: HTMLElement): void {
    fireEvent(dialog, new Event('close'));
}

describe('SpamQuestions', () => {
    it('asks for the kind of spam over every message it was opened for', async () => {
        asked(askingSpam);

        const chooser = await screen.findByRole('dialog', { name: 'Mark 2 messages as spam' });

        expect(chooser.textContent).toContain(
            'Pick what kind of spam they are. One kind applies to all 2. Each stays in the folder it is in.',
        );
        expect(within(chooser).getAllByRole('radio')).toHaveLength(6);
    });

    it('refuses to mark before a kind is picked, and says why', async () => {
        const { onSpam } = asked(askingSpam);
        const chooser = await screen.findByRole('dialog');
        const confirm = within(chooser).getByRole('button', { name: 'Mark as spam' });

        fireEvent.click(confirm);
        closed(chooser);

        expect(onSpam).not.toHaveBeenCalled();
        expect(confirm.getAttribute('aria-disabled')).toBe('true');
        expect(document.getElementById(confirm.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
            'Pick a kind of spam first',
        );
    });

    it('marks the messages with the kind picked once it is confirmed', async () => {
        const { onSpam } = asked(askingSpam);
        const chooser = await screen.findByRole('dialog');

        fireEvent.click(within(chooser).getByRole('radio', { name: /^Extortion/u }));
        press('Mark as spam');

        await waitFor(() => {
            expect(onSpam).toHaveBeenCalledWith('Extortion');
        });
        expect(onSpam).toHaveBeenCalledTimes(1);
    });

    it('marks nothing when it is cancelled, whatever was picked', async () => {
        const { onSpam, onNotSpam } = asked(askingSpam);
        const chooser = await screen.findByRole('dialog');

        fireEvent.click(within(chooser).getByRole('radio', { name: /^Scam/u }));
        press('Cancel');
        closed(chooser);

        expect(onSpam).not.toHaveBeenCalled();
        expect(onNotSpam).not.toHaveBeenCalled();
    });

    it('asks before marking not spam, naming the move and how it is taken back', async () => {
        const { onNotSpam } = asked(askingNotSpam);

        const question = await screen.findByRole('dialog', { name: 'Mark this message not spam?' });

        expect(question.textContent).toContain('It is marked not spam and moved to the Inbox.');
        expect(question.textContent).toContain('Marking it spam again takes this back.');
        expect(onNotSpam).not.toHaveBeenCalled();
    });

    it('marks not spam once the question is answered', async () => {
        const { onNotSpam, onSpam } = asked(askingNotSpam);

        await screen.findByRole('dialog');
        press('Not spam, move to Inbox');

        await waitFor(() => {
            expect(onNotSpam).toHaveBeenCalledTimes(1);
        });
        expect(onSpam).not.toHaveBeenCalled();
    });
});
