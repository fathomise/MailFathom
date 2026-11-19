// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { longestTaskTitle } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { NewTask, type TaskContext, type TaskDraft } from './NewTask';

// What opens the dialog in the test, named here rather than written into the markup: the localization rule holds over
// every `.tsx` under the packages, and a label a test invented is not a catalogue entry.
const asks = 'Ask';

function Asking({
    onSave,
    context,
}: {
    readonly onSave: (draft: TaskDraft) => void;
    readonly context?: TaskContext | undefined;
}) {
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                {asks}
            </button>

            <NewTask asked={asked} context={context} onSave={onSave} />
        </>
    );
}

function drawForm(context?: TaskContext): { readonly saved: ReturnType<typeof vi.fn> } {
    const saved = vi.fn();

    render(
        <LocalizationProvider>
            <Asking onSave={saved} context={context} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: asks }));

    return { saved };
}

function typeInto(field: string, text: string): void {
    fireEvent.change(screen.getByLabelText(field), { target: { value: text } });
}

describe('NewTask', () => {
    // Reminders are written from the row once the task exists, a lead being measured back from a day the record has
    // to hold first, so the form asks for none.
    it('asks which way the task runs, its line, its day and how long it takes', () => {
        drawForm();

        expect(screen.getByRole('radio', { name: 'I owe this' })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'I am waiting for this' })).toBeDefined();
        expect(screen.getByLabelText('Task description')).toBeDefined();
        expect(screen.getByLabelText('Due date')).toBeDefined();
        expect(screen.getByLabelText('Duration (minutes)')).toBeDefined();
        expect(screen.queryByLabelText('Reminders')).toBeNull();
    });

    it('records a task somebody is waiting for, under the head that says so', () => {
        const { saved } = drawForm();

        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));
        typeInto('Task description', 'Signed addendum from Anna');
        typeInto('Duration (minutes)', '30');

        expect(screen.getByRole('heading', { name: 'New waiting task' })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Add task' }));

        expect(saved).toHaveBeenCalledWith({
            title: 'Signed addendum from Anna',
            dueOn: '',
            direction: 'Awaited',
            estimatedMinutes: 30,
            anchor: null,
            counterpart: null,
        });
    });

    it('refuses a duration outside the minutes the deployment takes, and says what it takes', () => {
        drawForm();

        typeInto('Task description', 'Answer the tender');
        typeInto('Duration (minutes)', '3');

        expect(screen.getByRole('button', { name: 'Add task' }).hasAttribute('disabled')).toBe(true);
        expect(screen.getByText('A duration is between 5 and 720 minutes.')).toBeDefined();
    });

    it('starts from the context it was opened on, which can be let go of', () => {
        const { saved } = drawForm({
            anchor: { kind: 'Thread', id: '0190f3c2-8a7b-7cde-9f01-23456789abcd', label: 'Q4 delivery dates' },
            counterpart: { address: 'tomasz@fabrikam.example', displayName: 'Tomasz Bąk' },
            title: 'Confirm the Fabrikam delivery date',
        });

        expect(screen.getByText('Belongs to the thread')).toBeDefined();
        expect(screen.getByLabelText<HTMLInputElement>('Task description').value).toBe(
            'Confirm the Fabrikam delivery date',
        );

        fireEvent.click(screen.getByRole('button', { name: 'Remove the link to Q4 delivery dates' }));
        fireEvent.click(screen.getByRole('button', { name: 'Add task' }));

        expect(saved).toHaveBeenCalledWith(
            expect.objectContaining({
                title: 'Confirm the Fabrikam delivery date',
                anchor: null,
                counterpart: { address: 'tomasz@fabrikam.example', displayName: 'Tomasz Bąk' },
            }),
        );
    });

    it('records what was typed once the question has been answered', () => {
        const { saved } = drawForm();

        typeInto('Task description', '  Answer the tender  ');
        typeInto('Due date', '2026-09-24');
        fireEvent.click(screen.getByRole('button', { name: 'Add task' }));

        expect(saved).toHaveBeenCalledWith({
            title: 'Answer the tender',
            dueOn: '2026-09-24',
            direction: 'Owed',
            estimatedMinutes: null,
            anchor: null,
            counterpart: null,
        });
    });

    it('records a task nobody has named a day for', () => {
        const { saved } = drawForm();

        typeInto('Task description', 'Answer the tender');
        fireEvent.click(screen.getByRole('button', { name: 'Add task' }));

        expect(saved).toHaveBeenCalledWith(expect.objectContaining({ title: 'Answer the tender', dueOn: '' }));
    });

    it('records nothing at all where the question was left rather than answered', () => {
        const { saved } = drawForm();

        typeInto('Task description', 'Answer the tender');
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(saved).not.toHaveBeenCalled();
    });

    it('refuses to record a line nobody typed', () => {
        drawForm();

        expect(screen.getByRole('button', { name: 'Add task' }).hasAttribute('disabled')).toBe(true);

        typeInto('Task description', '   ');

        expect(screen.getByRole('button', { name: 'Add task' }).hasAttribute('disabled')).toBe(true);
    });

    it('holds the line to the length the deployment states rather than sending one it would refuse', () => {
        drawForm();

        expect(screen.getByLabelText('Task description').getAttribute('maxLength')).toBe(longestTaskTitle.toFixed(0));
    });

    it('empties the form on the way out, so a dialog opened again is a blank one', () => {
        const { saved } = drawForm();

        typeInto('Task description', 'Answer the tender');
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
        fireEvent.click(screen.getByRole('button', { name: asks }));

        expect(screen.getByLabelText<HTMLInputElement>('Task description').value).toBe('');
        expect(saved).not.toHaveBeenCalled();
    });

    it('takes nothing from the reader until the dialog is opened', () => {
        render(
            <LocalizationProvider>
                <Asking onSave={vi.fn()} />
            </LocalizationProvider>,
        );

        expect(document.activeElement).toBe(document.body);
    });
});
