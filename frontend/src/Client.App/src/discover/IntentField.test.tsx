// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { IntentField } from './IntentField';

// jsdom answers every media query `false`, which is the phone, so a test about a wider window states one.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

function aWindowWiderThanAPhone(): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: true,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function renderField(): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <IntentField recall />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

describe('IntentField', () => {
    it('keeps the question it was given rather than clearing it on the way', () => {
        renderField();

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'what did Nordwind send' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'value',
            'what did Nordwind send',
        );
    });

    it('offers no scope to choose, because Discover asks about all mail', () => {
        renderField();

        expect(screen.queryByRole('combobox')).toBeNull();
        expect(screen.queryByRole('status')).toBeNull();
    });

    // The field is the front door, so it is reachable without tabbing out of whatever is being read. The shortcut is
    // announced on the field itself, and a promise a screen reader passes on that nothing listens for is worse than
    // promising none — so both halves are asserted together.
    it('takes focus from anywhere on the shortcut it announces', () => {
        renderField();

        const field = screen.getByRole('searchbox', { name: 'Ask your mail' });

        expect(field.getAttribute('aria-keyshortcuts')).toBe('Control+K Meta+K');

        fireEvent.keyDown(document, { key: 'k', ctrlKey: true });

        expect(field).toBe(document.activeElement);
    });
});

// The workspace carries gestures made in the Mail space — a row ticked in the list, a correspondence opened, a passage
// selected in a message — so what stands in for those here is the same write made on mount.
function Standing({ as }: { readonly as: Partial<Workspace> }) {
    const { revise } = useWorkspace();

    useEffect(() => {
        revise(as);
    }, [revise, as]);

    return null;
}

// What the workspace holds while the field is being driven, which is what a run is started from.
function Reporting({ onWorkspace }: { readonly onWorkspace: (workspace: Workspace) => void }) {
    const { workspace } = useWorkspace();

    onWorkspace(workspace);

    return null;
}

function fieldStanding(as: Partial<Workspace>, recall = true): { workspace: () => Workspace } {
    let last: Workspace | null = null;

    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <Standing as={as} />
                <IntentField recall={recall} />
                <Reporting
                    onWorkspace={(workspace) => {
                        last = workspace;
                    }}
                />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );

    return {
        workspace: () => {
            if (last === null) {
                throw new Error('The workspace was never reported.');
            }

            return last;
        },
    };
}

// Everything the Mail space has in front of somebody at once: a folder, a correspondence, the message open in it, the
// rows ticked, and a passage highlighted. None of it belongs to Discover's field.
const mailInFront: Partial<Workspace> = {
    scope: { kind: 'folder', accountId: 'work', folderId: 'Invoices' },
    conversation: { threadId: 'thread-1', openAt: null },
    selection: 'AAMkAD-42',
    selected: ['AAMkAD-42', 'AAMkAD-43'],
    fragment: { messageId: 'AAMkAD-42', text: 'by the end of the month' },
};

describe('IntentField and the Mail space', () => {
    it('shows nothing of what the Mail space has open, picked out, or highlighted', () => {
        fieldStanding(mailInFront);

        expect(screen.queryByText(/by the end of the month/)).toBeNull();
        expect(screen.queryByRole('combobox')).toBeNull();
        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty('value', '');
    });

    it('records the question alone, so the run reads all mail whatever the Mail space is showing', () => {
        const { workspace: reported } = fieldStanding(mailInFront);

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'when did they say it would arrive' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(reported().askedBefore).toEqual([{ question: 'when did they say it would arrive' }]);
    });

    it('changes nothing the Mail space holds when a question is asked', () => {
        const { workspace: reported } = fieldStanding(mailInFront);

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'who signed it' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(reported()).toMatchObject(mailInFront);
    });
});

describe('IntentField history', () => {
    it('offers nothing back before anything has been asked', () => {
        renderField();

        expect(screen.queryByRole('list', { name: 'Asked before' })).toBeNull();
    });

    it('offers what was asked back by its words alone', () => {
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        fireEvent.change(screen.getByRole('searchbox', { name: 'Ask your mail' }), {
            target: { value: 'what did they promise' },
        });
        fireEvent.submit(screen.getByRole('search'));

        expect(screen.getByRole('button', { name: 'what did they promise' })).toBeDefined();
    });

    it('records nothing for a submission with no question in it', () => {
        fieldStanding({});

        fireEvent.submit(screen.getByRole('search'));

        expect(screen.queryByRole('list', { name: 'Asked before' })).toBeNull();
    });

    it('asks a past question again, putting it back in the field and at the front of the list', () => {
        const { workspace: reported } = fieldStanding({
            askedBefore: [{ question: 'who signed it' }, { question: 'what did they promise' }],
        });

        fireEvent.click(screen.getByRole('button', { name: 'what did they promise' }));

        expect(reported().question).toBe('what did they promise');
        expect(reported().askedBefore).toEqual([{ question: 'what did they promise' }, { question: 'who signed it' }]);
    });

    it('lets go of what was asked when that is asked for', () => {
        fieldStanding({
            askedBefore: [{ question: 'what did they promise' }],
        });

        fireEvent.click(screen.getByRole('button', { name: 'Forget these' }));

        expect(screen.queryByRole('list', { name: 'Asked before' })).toBeNull();
    });

    // The design draws the row only while the field is idle: once a run is answering, it stands where that answer's own
    // context is read, so the row leaves rather than competing with it.
    it('offers nothing back while a run is answering', () => {
        fieldStanding({ askedBefore: [{ question: 'what did they promise' }] }, false);

        expect(screen.queryByRole('list', { name: 'Asked before' })).toBeNull();
    });

    // Forgetting the list takes away the control that was pressed, so focus is placed rather than left on an element
    // that is no longer in the document — which is where keyboard and screen-reader use stops without anything saying
    // so.
    it('puts the keyboard back on the question when the list it was pressed in goes', () => {
        fieldStanding({
            askedBefore: [{ question: 'what did they promise' }],
        });

        fireEvent.click(screen.getByRole('button', { name: 'Forget these' }));

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toBe(document.activeElement);
    });
});

// The field under a correspondence is a different one with state of its own, so opening a correspondence changes
// nothing this field says or does: it stays Discover's question.
describe('IntentField wording', () => {
    it('asks the Discover screen\u2019s own question even while a correspondence is being read', () => {
        aWindowWiderThanAPhone();
        fieldStanding({ conversation: { threadId: 'thread-1', openAt: null }, selection: 'AAMkAD-42' });

        expect(screen.getByRole('button', { name: 'Ask' })).toBeDefined();
        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'placeholder',
            'What do you want to ask your mail?',
        );
    });

    it('asks in words that fit one line on a phone', () => {
        fieldStanding({});

        expect(screen.getByRole('searchbox', { name: 'Ask your mail' })).toHaveProperty(
            'placeholder',
            'Ask your mail…',
        );
    });

    it('records nothing for an empty press, whatever the Mail space has open', () => {
        const { workspace: reported } = fieldStanding({ conversation: { threadId: 'thread-1', openAt: null } });

        fireEvent.submit(screen.getByRole('search'));

        expect(reported().question).toBe('');
        expect(reported().askedBefore).toEqual([]);
    });
});
