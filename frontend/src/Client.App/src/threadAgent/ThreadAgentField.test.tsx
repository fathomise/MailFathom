// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ComposerOpening } from '../composer/composition';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { AgentHandOverContext, type AgentHandOver } from '../routing/agentHandOver';
import { ToastsProvider } from '../toasts/Toasts';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { ThreadAgentField } from './ThreadAgentField';

// What is asserted is what a reader meets: the two choices the AI button unfolds, the wording at each composition, the
// composer the drafter opens, and where focus goes when it leaves.

const answering = '00000000-0000-4000-8000-000000000042';

// jsdom answers every media query `false` unless something says otherwise, which is the phone reading; a test about a
// wider composition states which.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

function widthsMatching(matches: (query: string) => boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: matches(query),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function theDesktopComposition(): void {
    widthsMatching((query) => query.includes('min-width'));
}

/** Two panes and a wide workspace, short of the desktop: the fold and the tablet. */
function theTabletComposition(): void {
    widthsMatching((query) => query.includes('43.75rem') || query.includes('51.25rem'));
}

function Standing({ as }: { readonly as: Partial<Workspace> }) {
    const { revise } = useWorkspace();

    useEffect(() => {
        revise(as);
    }, [revise, as]);

    return null;
}

function Reporting({ onWorkspace }: { readonly onWorkspace: (workspace: Workspace) => void }) {
    const { workspace } = useWorkspace();

    onWorkspace(workspace);

    return null;
}

const thread: AgentHandOver = {
    scope: { kind: 'thread', subject: '00000000-0000-4000-8000-000000000099' },
    title: 'Service level agreement',
};

function drawing(
    as: Partial<Workspace> = {},
    offered = true,
    {
        handToAgent = null,
        drafts = true,
        takesTheKeyboard = false,
        onKeyboardTaken = () => undefined,
    }: {
        handToAgent?: ((handOver: AgentHandOver) => void) | null;
        drafts?: boolean;
        takesTheKeyboard?: boolean;
        onKeyboardTaken?: () => void;
    } = {},
): {
    composed: ReturnType<typeof vi.fn<(opening: ComposerOpening) => void>>;
    workspace: () => Workspace;
} {
    const composed = vi.fn<(opening: ComposerOpening) => void>();
    let last: Workspace | null = null;

    render(
        <LocalizationProvider>
            <ToastsProvider>
                <WorkspaceProvider>
                    <ComposingContext
                        value={{ offered, drafts: true, opening: null, compose: composed, close: () => undefined }}
                    >
                        <AgentHandOverContext value={handToAgent}>
                            <Standing as={{ selection: answering, ...as }} />
                            <ThreadAgentField
                                thread={thread}
                                drafts={drafts}
                                takesTheKeyboard={takesTheKeyboard}
                                onKeyboardTaken={onKeyboardTaken}
                            />
                            <Reporting
                                onWorkspace={(workspace) => {
                                    last = workspace;
                                }}
                            />
                        </AgentHandOverContext>
                    </ComposingContext>
                </WorkspaceProvider>
            </ToastsProvider>
        </LocalizationProvider>,
    );

    return {
        composed,
        workspace: () => {
            if (last === null) {
                throw new Error('The workspace was never reported.');
            }

            return last;
        },
    };
}

const drafterName = 'Draft a reply to this correspondence';
const aiButton = 'Draft a reply or ask about this thread';

/** Opens the drafter where it is folded into the AI button, which is how it stands until somebody asks for it. */
function openingTheDrafter(): void {
    if (screen.queryByRole('form', { name: drafterName }) === null) {
        fireEvent.click(screen.getByRole('button', { name: aiButton }));
        fireEvent.click(screen.getByRole('button', { name: 'Draft a reply' }));
    }
}

function field(): HTMLElement {
    openingTheDrafter();

    return screen.getByRole('textbox', { name: drafterName });
}

function draftingAReply(): void {
    openingTheDrafter();
    fireEvent.submit(screen.getByRole('form', { name: drafterName }));
}

describe('ThreadAgentField', () => {
    it('takes the keyboard onto the AI button as it is drawn where a composer it opened handed it back', () => {
        const taken = vi.fn<() => void>();

        drawing({}, true, { takesTheKeyboard: true, onKeyboardTaken: taken });

        expect(document.activeElement).toBe(screen.getByRole('button', { name: aiButton }));
        expect(taken).toHaveBeenCalledOnce();
    });

    it('stands folded into the AI button until somebody asks for it', () => {
        drawing();

        expect(screen.getByRole('button', { name: aiButton }).getAttribute('aria-expanded')).toBe('false');
        expect(screen.queryByRole('button', { name: 'Draft a reply' })).toBeNull();
        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
    });

    it('unfolds its two choices from the AI button, with the keyboard on the first', () => {
        drawing({}, true, { handToAgent: () => undefined });

        fireEvent.click(screen.getByRole('button', { name: aiButton }));

        const choices = screen.getByRole('group', { name: aiButton });

        expect(screen.getByRole('button', { name: aiButton }).getAttribute('aria-expanded')).toBe('true');
        expect(
            within(choices)
                .getAllByRole('button')
                .map((choice) => choice.textContent),
        ).toEqual(['Draft a reply', 'Ask']);
        expect(document.activeElement).toBe(within(choices).getByRole('button', { name: 'Draft a reply' }));
    });

    it('folds the choices again on Escape, and puts the keyboard back on the AI button', () => {
        drawing({}, true, { handToAgent: () => undefined });

        fireEvent.click(screen.getByRole('button', { name: aiButton }));
        fireEvent.keyDown(screen.getByRole('button', { name: 'Draft a reply' }), { key: 'Escape' });

        expect(screen.queryByRole('group', { name: aiButton })).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: aiButton }));
    });

    it('folds the choices when somebody presses anywhere else', () => {
        drawing({}, true, { handToAgent: () => undefined });

        fireEvent.click(screen.getByRole('button', { name: aiButton }));
        fireEvent.pointerDown(document.body);

        expect(screen.queryByRole('group', { name: aiButton })).toBeNull();
    });

    it('hands the conversation to the agent from *Ask*', () => {
        const handToAgent = vi.fn<(handOver: AgentHandOver) => void>();

        drawing({}, true, { handToAgent });

        fireEvent.click(screen.getByRole('button', { name: aiButton }));
        fireEvent.click(screen.getByRole('button', { name: 'Ask' }));

        expect(handToAgent).toHaveBeenCalledExactlyOnceWith(thread);
        expect(screen.queryByRole('group', { name: aiButton })).toBeNull();
    });

    it('offers *Ask* alone where the deployment drafts no replies', () => {
        drawing({}, true, { handToAgent: () => undefined, drafts: false });

        fireEvent.click(screen.getByRole('button', { name: aiButton }));

        expect(screen.queryByRole('button', { name: 'Draft a reply' })).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Ask' }));
    });

    it('offers *Draft a reply* alone where this credential has no agent to ask', () => {
        drawing();

        fireEvent.click(screen.getByRole('button', { name: aiButton }));

        expect(screen.getByRole('button', { name: 'Draft a reply' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Ask' })).toBeNull();
    });

    it('draws nothing where neither choice is offered', () => {
        drawing({}, true, { drafts: false });

        expect(screen.queryByRole('button', { name: aiButton })).toBeNull();
    });

    it('opens the drafter from its choice, with the keyboard in the field', () => {
        drawing();

        openingTheDrafter();

        expect(screen.getByRole('form', { name: drafterName })).toBeDefined();
        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: drafterName }));
        expect(screen.queryByRole('button', { name: aiButton })).toBeNull();
    });

    it('folds the drafter back into its button from the close badge, and puts the keyboard on the button', () => {
        drawing();
        openingTheDrafter();

        fireEvent.click(screen.getByRole('button', { name: 'Close the reply drafter' }));

        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: aiButton }));
    });

    it('opens the reply composer on the message being read, asking it for the reply that was typed', () => {
        const { composed } = drawing({ fragment: { messageId: answering, text: 'the response time is two hours' } });

        fireEvent.change(field(), { target: { value: 'accept the SLA' } });
        draftingAReply();

        expect(composed).toHaveBeenCalledExactlyOnceWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: answering,
            drafting: { asked: 'accept the SLA', passage: 'the response time is two hours' },
        });
    });

    it('asks for a reply with nothing typed, the press alone being the request', () => {
        const { composed } = drawing();

        draftingAReply();

        expect(composed).toHaveBeenCalledExactlyOnceWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: answering,
            drafting: { asked: '', passage: null },
        });
    });

    it('asks for the reply on Enter, and keeps Shift+Enter and an unfinished composition as typing', () => {
        const { composed } = drawing();

        fireEvent.change(field(), { target: { value: 'accept the SLA' } });
        fireEvent.keyDown(field(), { key: 'Enter', shiftKey: true });
        fireEvent.keyDown(field(), { key: 'Enter', isComposing: true });

        expect(composed).not.toHaveBeenCalled();

        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(composed).toHaveBeenCalledExactlyOnceWith(
            expect.objectContaining({ drafting: { asked: 'accept the SLA', passage: null } }),
        );
    });

    it('folds the drafter back on Escape', () => {
        drawing();

        fireEvent.keyDown(field(), { key: 'Escape' });

        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
    });

    it('folds the drafter back once the reply is asked for, and starts the next one empty', () => {
        drawing();

        fireEvent.change(field(), { target: { value: 'accept the SLA' } });
        draftingAReply();

        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
        expect(field()).toHaveProperty('value', '');
    });

    it('keeps what was typed while it is folded, so reopening it is not starting again', () => {
        drawing();

        fireEvent.change(field(), { target: { value: 'accept the SLA' } });
        fireEvent.click(screen.getByRole('button', { name: 'Close the reply drafter' }));

        expect(field()).toHaveProperty('value', 'accept the SLA');
    });

    it('names the press in full where two panes are drawn, and speaks only of drafting a reply', () => {
        theTabletComposition();

        drawing();

        expect(field()).toHaveProperty('placeholder', 'Describe the reply you want to send…');
        expect(screen.getByRole('button', { name: 'Draft a reply' })).toBeDefined();
    });

    it('shortens the press and the placeholder on one pane, where there is no room for the sentence', () => {
        drawing();

        expect(field()).toHaveProperty('placeholder', 'Describe the reply…');
        expect(screen.getByRole('button', { name: 'Draft' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Draft a reply' })).toBeNull();
    });

    it('says what the draft is about and where it stays in the desktop composition', () => {
        theDesktopComposition();

        drawing();
        openingTheDrafter();

        expect(screen.getByText('Scope: this thread')).toBeDefined();
        expect(screen.getByText('+ selected passage')).toBeDefined();
        expect(screen.getByText('Draft stays local until confirmed')).toBeDefined();
    });

    it('draws none of that below the desktop composition', () => {
        theTabletComposition();

        drawing();
        openingTheDrafter();

        expect(screen.queryByText('Scope: this thread')).toBeNull();
        expect(screen.queryByText('Draft stays local until confirmed')).toBeNull();
    });

    // The two fields hold separate sentences: what is typed under a correspondence is about that correspondence, and
    // what was typed into Discover's question is not something to draft a reply with.
    it('keeps what is typed here apart from Discover’s question, in both directions', () => {
        const { workspace } = drawing({ question: 'what did Nordwind send' });

        expect(field()).toHaveProperty('value', '');

        fireEvent.change(field(), { target: { value: 'accept the SLA' } });

        expect(workspace().question).toBe('what did Nordwind send');
    });
});
