// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    AgentProposalDecision,
    AnswerBlock,
    BlockEvidence,
    ComposedDraft,
    DeclaredSource,
    DraftDisposition,
    MailDocument,
} from '@mailfathom/client-backend';
import { FootersInForceContext, type FootersInForce } from '../../footers/footersInForce';
import { LocalizationProvider } from '../../localization/Localization';
import { ReadingZoneContext } from '../../localization/useReadingZone';
import { AnswerSourcesContext } from '../answerSources';
import { ProposalAnsweringContext, type ProposalAnswering, type ProposalPhase } from '../proposalAnswering';
import { Draft } from './Draft';

const addendum: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Contract addendum — signatures',
    medium: 'Written',
    unreadable: null,
};

const backed: BlockEvidence = {
    support: 'Supported',
    citations: ['c-1'],
    freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
    conflictingClaims: [],
};

const composed: ComposedDraft = {
    recipients: ['anna@contoso.example'],
    subject: 'Re: proposed terms for 2027',
    body: 'We accept shortening the response time to 2 hours on business days.',
    disposition: 'Composed',
    dueAt: null,
    account: 'work',
};

/** The footer the work account adds: a name line and the company under it. */
const workFooter: MailDocument = {
    blocks: ['Karolina Kowalska', 'Nordwind Logistics'].map((text) => ({
        type: 'paragraph' as const,
        content: [
            {
                text,
                emphasis: { bold: false, italic: false, underline: false, strikethrough: false, monospace: false },
                foreground: null,
                link: null,
            },
        ],
        alignment: 'Inherited',
    })),
    refusal: 'None',
    removedRemoteReferenceCount: 0,
    retainedRemoteImageCount: 0,
    inlineImageCount: 0,
    undrawnInlineImageCount: 0,
    truncated: false,
};

function drafted(disposition: DraftDisposition = 'Composed'): AnswerBlock {
    return { type: 'draft', named: 'draft', evidence: backed, draft: { ...composed, disposition } };
}

/** A draft the person asked to leave at 08:00 on Friday 28 August in Warsaw, which is 06:00 UTC. */
function draftedToLeaveLater(): AnswerBlock {
    return {
        type: 'draft',
        named: 'draft',
        evidence: backed,
        draft: { ...composed, dueAt: '2026-08-28T06:00:00+00:00' },
    };
}

function inConversation(
    phase: ProposalPhase,
    answer: (decision: AgentProposalDecision) => void = () => undefined,
): ProposalAnswering {
    return { phase, answering: false, refused: null, answer, askAnotherTime: () => undefined, look: null };
}

function renderDraft(
    block: AnswerBlock,
    answering: ProposalAnswering | null = null,
    footers: FootersInForce = new Map(),
) {
    const declared = new Map([[addendum.id, addendum]]);

    return render(
        <LocalizationProvider>
            {/* A citation is given somewhere to follow to, as the canvas gives every block one: a chip with nowhere to
                go says so in its own name, which is `Citation`'s behaviour rather than this block's. */}
            <AnswerSourcesContext value={{ sources: declared, follow: () => undefined }}>
                <FootersInForceContext value={footers}>
                    <ProposalAnsweringContext value={answering}>
                        <Draft block={block} />
                    </ProposalAnsweringContext>
                </FootersInForceContext>
            </AnswerSourcesContext>
        </LocalizationProvider>,
    );
}

/** Every control the card offers, by the name it is announced under, so a control added later has to be accounted for. */
function controlsOffered(): readonly (string | null)[] {
    return screen.getAllByRole('button').map((control) => control.getAttribute('aria-label') ?? control.textContent);
}

describe('Draft', () => {
    it('shows who it is addressed to, what it is about, and what it says', () => {
        renderDraft(drafted());

        expect(screen.getByText('anna@contoso.example')).toBeDefined();
        expect(screen.getByText('Re: proposed terms for 2027')).toBeDefined();
        expect(screen.getByText('We accept shortening the response time to 2 hours on business days.')).toBeDefined();
    });

    it('joins several recipients as the reader’s own language joins a list', () => {
        renderDraft({
            type: 'draft',
            named: 'draft',
            evidence: backed,
            draft: { ...composed, recipients: ['anna@contoso.example', 'a.kowalska@fabrikam.example'] },
        });

        expect(screen.getByText('anna@contoso.example and a.kowalska@fabrikam.example')).toBeDefined();
    });

    it.each([
        ['Composed', 'Local draft - nothing was sent'],
        ['Saved', 'Saved to your drafts - nothing was sent'],
        ['Queued', 'Waiting in the outbox - nothing has been sent yet'],
    ] as const)('says what has become of a %s draft, and that nothing left', (disposition, said) => {
        renderDraft(drafted(disposition));

        expect(screen.getByText(said)).toBeDefined();
    });

    it('offers no way to send it, sending belonging to the surface that governs sending', () => {
        renderDraft(drafted());

        expect(controlsOffered()).toEqual(['supported', 'Citation 1: Contract addendum — signatures', 'Edit the text']);
    });

    it('offers no way to send it while the text is open either, which is where a Send would be added', () => {
        renderDraft(drafted());

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));

        expect(controlsOffered()).toEqual(['supported', 'Citation 1: Contract addendum — signatures', 'Stop editing']);
    });

    it('shows what the draft was written from, so it can be checked before anybody puts their name to it', () => {
        renderDraft(drafted());

        expect(screen.getByRole('button', { name: 'Citation 1: Contract addendum — signatures' })).toBeDefined();
    });

    it('opens the text for editing, and keeps what was typed', () => {
        renderDraft(drafted());

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));

        const written = screen.getByRole('textbox', { name: 'Draft text' });
        fireEvent.change(written, { target: { value: 'We accept, with a 5% cap.' } });

        expect(screen.getByRole('textbox', { name: 'Draft text' })).toHaveProperty(
            'value',
            'We accept, with a 5% cap.',
        );
    });

    it('puts the caret in the text it was asked to open, the control that opened it sitting after the area', () => {
        renderDraft(drafted());

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Draft text' }));
    });

    it('keeps what was typed when the editor closes, closing it being what was asked for rather than discarding it', () => {
        renderDraft(drafted());

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Draft text' }), {
            target: { value: 'We accept, with a 5% cap.' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Stop editing' }));

        expect(screen.getByText('We accept, with a 5% cap.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));

        expect(screen.getByRole('textbox', { name: 'Draft text' })).toHaveProperty(
            'value',
            'We accept, with a 5% cap.',
        );
    });

    it('says an edit goes nowhere, rather than leaving somebody to assume it was saved', () => {
        renderDraft(drafted());

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));

        expect(screen.getByText('Changes stay on this screen - nothing here saves or sends the draft.')).toBeDefined();
    });

    it('keeps saying so once the editor closes, the paragraph then drawing what the reader wrote', () => {
        renderDraft(drafted('Saved'));

        fireEvent.click(screen.getByRole('button', { name: 'Edit the text' }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Draft text' }), {
            target: { value: 'We accept, with a 5% cap.' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Stop editing' }));

        expect(screen.getByText('Changes stay on this screen - nothing here saves or sends the draft.')).toBeDefined();
    });

    it('says nothing about an edit nobody made, the card standing as the deployment holds it', () => {
        renderDraft(drafted());

        expect(screen.queryByText('Changes stay on this screen - nothing here saves or sends the draft.')).toBeNull();
    });

    it('draws another renderer’s block as an unknown block rather than as a draft', () => {
        renderDraft({ type: null, named: 'RiskScore' });

        expect(screen.getByText('Unknown block')).toBeDefined();
    });

    describe('in a conversation', () => {
        it('sends it, opens it for editing here, or discards it, each named for who it is addressed to', () => {
            const answer = vi.fn<(decision: AgentProposalDecision) => void>();
            renderDraft(drafted(), inConversation('pending', answer));

            fireEvent.click(screen.getByRole('button', { name: 'Send the draft to anna@contoso.example' }));
            fireEvent.click(screen.getByRole('button', { name: 'Discard the draft to anna@contoso.example' }));

            expect(answer.mock.calls).toEqual([['accepted'], ['declined']]);
            expect(
                screen.getByRole('button', {
                    name: 'Edit here - the draft to anna@contoso.example, inside the conversation',
                }),
            ).toBeDefined();
        });

        it('swaps the controls for sending and for stopping while the text is open, and keeps what was typed', () => {
            renderDraft(drafted(), inConversation('pending'));

            fireEvent.click(
                screen.getByRole('button', {
                    name: 'Edit here - the draft to anna@contoso.example, inside the conversation',
                }),
            );

            expect(screen.getByRole('button', { name: 'Send the edited draft to anna@contoso.example' })).toBeDefined();
            expect(screen.queryByRole('button', { name: 'Discard the draft to anna@contoso.example' })).toBeNull();

            fireEvent.change(screen.getByRole('textbox', { name: 'Draft text' }), { target: { value: 'Agreed.' } });
            fireEvent.click(screen.getByRole('button', { name: 'Stop editing the draft to anna@contoso.example' }));

            expect(screen.getByText('Agreed.')).toBeDefined();
        });

        it('holds Send once the text differs from what was proposed, and says why', () => {
            const answer = vi.fn<(decision: AgentProposalDecision) => void>();
            renderDraft(drafted(), inConversation('pending', answer));

            fireEvent.click(
                screen.getByRole('button', {
                    name: 'Edit here - the draft to anna@contoso.example, inside the conversation',
                }),
            );
            fireEvent.change(screen.getByRole('textbox', { name: 'Draft text' }), { target: { value: 'Agreed.' } });
            fireEvent.click(screen.getByRole('button', { name: 'Send the edited draft to anna@contoso.example' }));

            expect(answer).not.toHaveBeenCalled();
            expect(screen.getByText(/Your edit cannot be sent from here yet/)).toBeDefined();
        });

        it('reads the subject and the text out in the language they were written in', () => {
            renderDraft(drafted(), inConversation('pending'));

            expect(screen.getByText('Re: proposed terms for 2027').getAttribute('lang')).toBe('');
            expect(
                screen
                    .getByText('We accept shortening the response time to 2 hours on business days.')
                    .getAttribute('lang'),
            ).toBe('');
        });

        it('offers no editing once the draft was sent', () => {
            renderDraft(drafted(), inConversation('accepted'));

            expect(screen.getByText('Sent')).toBeDefined();
            expect(screen.queryAllByRole('button').map((control) => control.textContent)).not.toContain('Edit here');
            expect(screen.queryByRole('button', { name: 'Edit the text' })).toBeNull();
        });
    });

    describe('asked to leave later', () => {
        // Thursday 27 August 2026 at 10:47 in Warsaw, the design's own sample moment, read in the zone the reader's
        // record states rather than in whichever one the machine running the suite is in.
        beforeEach(() => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(Date.parse('2026-08-27T08:47:00Z'));
        });

        afterEach(() => {
            vi.useRealTimers();
        });

        function renderInWarsaw(phase: ProposalPhase) {
            return render(
                <LocalizationProvider>
                    <ReadingZoneContext value="Europe/Warsaw">
                        <AnswerSourcesContext value={{ sources: new Map(), follow: () => undefined }}>
                            <ProposalAnsweringContext value={inConversation(phase)}>
                                <Draft block={draftedToLeaveLater()} />
                            </ProposalAnsweringContext>
                        </AnswerSourcesContext>
                    </ReadingZoneContext>
                </LocalizationProvider>,
            );
        }

        it('says when it leaves, on Send and beside the text, in the reader’s own zone', () => {
            renderInWarsaw('pending');

            const send = screen.getByRole('button', {
                name: 'Send the draft to anna@contoso.example - it leaves Fri 08/28 at 8:00 AM',
            });
            expect(send.textContent).toBe('Send Fri 8:00 AM');
            expect(
                screen.getByText('This message leaves Fri 08/28 at 8:00 AM, not when you press Send.'),
            ).toBeDefined();
            expect(screen.getByText('Europe/Warsaw time')).toBeDefined();
        });

        it('keeps the time while the text is edited, editing never touching when it leaves', () => {
            renderInWarsaw('pending');

            fireEvent.click(
                screen.getByRole('button', {
                    name: 'Edit here - the draft to anna@contoso.example, inside the conversation',
                }),
            );

            expect(
                screen.getByRole('button', {
                    name: 'Send the edited draft to anna@contoso.example - it leaves Fri 08/28 at 8:00 AM',
                }),
            ).toBeDefined();
            expect(
                screen.getByText('This message leaves Fri 08/28 at 8:00 AM, not when you press Send.'),
            ).toBeDefined();
        });

        it('says it was scheduled once accepted, the result line naming the time instead of the line beside the text', () => {
            renderInWarsaw('accepted');

            expect(
                screen.getByText(
                    'Scheduled - it leaves Fri 08/28 at 8:00 AM and waits in the Outbox, where it can still be cancelled',
                ),
            ).toBeDefined();
            expect(screen.queryByText(/not when you press Send/)).toBeNull();
        });

        it('says it could not be scheduled, and how a time that passed is put right', () => {
            renderInWarsaw('failed');

            expect(
                screen.getByText('Could not schedule it. If its time has passed, ask for a later one.'),
            ).toBeDefined();
            expect(
                screen.getByText('This message leaves Fri 08/28 at 8:00 AM, not when you press Send.'),
            ).toBeDefined();
        });
    });

    describe('the footer its account adds', () => {
        const footers: FootersInForce = new Map([['work', workFooter]]);

        it('stands under the letter folded to one line, and unfolds and folds again on a press', () => {
            renderDraft(drafted(), inConversation('pending'), footers);

            const fold = screen.getByRole('button', { name: 'Your footer is hidden' });

            expect(fold.getAttribute('aria-expanded')).toBe('false');
            expect(screen.queryByText('Nordwind Logistics')).toBeNull();

            fireEvent.click(fold);

            expect(screen.getByText('Karolina Kowalska')).toBeDefined();
            expect(screen.getByText('Nordwind Logistics')).toBeDefined();

            fireEvent.click(screen.getByRole('button', { name: 'Hide your footer' }));

            expect(screen.queryByText('Nordwind Logistics')).toBeNull();
        });

        it('draws nothing where the account sends without a footer', () => {
            renderDraft(
                { ...drafted(), draft: { ...composed, account: 'board' } } as AnswerBlock,
                inConversation('pending'),
                footers,
            );

            expect(screen.queryByRole('button', { name: 'Your footer is hidden' })).toBeNull();
        });

        it('draws nothing where the draft names no account', () => {
            renderDraft(
                { ...drafted(), draft: { ...composed, account: null } } as AnswerBlock,
                inConversation('pending'),
                footers,
            );

            expect(screen.queryByRole('button', { name: 'Your footer is hidden' })).toBeNull();
        });

        it('draws nothing once the draft was discarded, it being nothing that will go', () => {
            renderDraft(drafted(), inConversation('declined'), footers);

            expect(screen.queryByRole('button', { name: 'Your footer is hidden' })).toBeNull();
        });
    });
});
