// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    AgentProposalDecision,
    ClientResult,
    DraftPassageRewriteRequest,
    DraftPassageRewriting,
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
import { PassageRewritingContext, type PassageRewriter } from '../../rewriting/passageRewriting';
import { ToastsProvider } from '../../toasts/Toasts';
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
                size: 'Normal',
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
    footer: null,
    quotation: null,
    authorIsProven: false,
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
    rewriter: PassageRewriter | null = null,
) {
    const declared = new Map([[addendum.id, addendum]]);

    return render(
        <LocalizationProvider>
            <ToastsProvider>
                {/* A citation is given somewhere to follow to, as the canvas gives every block one: a chip with nowhere
                    to go says so in its own name, which is `Citation`'s behaviour rather than this block's. */}
                <AnswerSourcesContext value={{ sources: declared, follow: () => undefined }}>
                    <FootersInForceContext value={footers}>
                        <PassageRewritingContext value={rewriter}>
                            <ProposalAnsweringContext value={answering}>
                                <Draft block={block} />
                            </ProposalAnsweringContext>
                        </PassageRewritingContext>
                    </FootersInForceContext>
                </AnswerSourcesContext>
            </ToastsProvider>
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

        it('stops saying nothing was sent once it was, the result line saying what became of it', () => {
            renderDraft(drafted(), inConversation('accepted'));

            expect(screen.getByText('Sent')).toBeDefined();
            expect(screen.queryByText('Local draft - nothing was sent')).toBeNull();
        });

        it('still says nothing was sent where sending it failed', () => {
            renderDraft(drafted(), inConversation('failed'));

            expect(screen.getByText('Local draft - nothing was sent')).toBeDefined();
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
                    <ToastsProvider>
                        <ReadingZoneContext value="Europe/Warsaw">
                            <AnswerSourcesContext value={{ sources: new Map(), follow: () => undefined }}>
                                <ProposalAnsweringContext value={inConversation(phase)}>
                                    <Draft block={draftedToLeaveLater()} />
                                </ProposalAnsweringContext>
                            </AnswerSourcesContext>
                        </ReadingZoneContext>
                    </ToastsProvider>
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

describe('Draft, rewriting a selected part', () => {
    const twoSentences = 'We accept the two-hour response time. We need the signed addendum by Friday.';

    function withTwoSentences(): AnswerBlock {
        return { type: 'draft', named: 'draft', evidence: backed, draft: { ...composed, body: twoSentences } };
    }

    /** A deployment that answers each ask with the next of these, and remembers what it was asked. */
    function answeringWith(...answers: ClientResult<DraftPassageRewriting>[]) {
        const asked: DraftPassageRewriteRequest[] = [];
        const rewriter: PassageRewriter = (request) => {
            asked.push(request);

            return Promise.resolve(answers.shift() ?? { outcome: 'read', value: { outcome: 'notRewritten' } });
        };

        return { asked, rewriter };
    }

    function rewritten(...passages: string[]): ClientResult<DraftPassageRewriting> {
        return { outcome: 'read', value: { outcome: 'rewritten', passages } };
    }

    /** Selects the given words of the letter as a mouse would, and lets the mouse go. */
    function select(words: string): void {
        const letter = screen.getByText(
            (_, element) => element?.tagName === 'P' && element.textContent === twoSentences,
        );
        const text = letter.firstChild;
        const start = twoSentences.indexOf(words);

        if (text === null) {
            throw new Error('The letter holds no text to select.');
        }

        const range = document.createRange();
        range.setStart(text, start);
        range.setEnd(text, start + words.length);
        window.getSelection()?.removeAllRanges();
        window.getSelection()?.addRange(range);

        const released = new Event('pointerup', { bubbles: true });
        Object.defineProperty(released, 'pointerType', { value: 'mouse' });
        fireEvent(document, released);
    }

    function openTheBar(words: string): void {
        select(words);
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite this part with AI' }));
    }

    afterEach(() => {
        window.getSelection()?.removeAllRanges();
    });

    it('says a part can be rewritten under a pending draft, where the deployment rewrites at all', () => {
        renderDraft(withTwoSentences(), inConversation('pending'), new Map(), answeringWith().rewriter);

        expect(screen.getByText('Select any part and the agent rewrites just that')).toBeDefined();
    });

    it('offers nothing to rewrite where the deployment rewrites none', () => {
        renderDraft(withTwoSentences(), inConversation('pending'));
        select('signed addendum');

        expect(screen.queryByText('Select any part and the agent rewrites just that')).toBeNull();
        expect(screen.queryByRole('button', { name: 'Rewrite this part with AI' })).toBeNull();
    });

    it('raises the AI button by a selection, and its press opens the bar with the four presets and a field', () => {
        renderDraft(withTwoSentences(), inConversation('pending'), new Map(), answeringWith().rewriter);
        openTheBar('signed addendum');

        const bar = screen.getByRole('dialog', { name: 'Rewrite the selected part of the draft' });

        expect(within(bar).getByText('“signed addendum”')).toBeDefined();
        expect(
            ['Shorter', 'More formal', 'Softer', 'Firmer'].map(
                (preset) =>
                    within(bar).getByRole('button', { name: `Rewrite the selection: ${preset.toLowerCase()}` })
                        .textContent,
            ),
        ).toStrictEqual(['Shorter', 'More formal', 'Softer', 'Firmer']);
        expect(within(bar).getByRole('textbox', { name: 'Say how to rewrite the selection' })).toBeDefined();
        expect(document.activeElement).toBe(
            within(bar).getByRole('button', { name: 'Rewrite the selection: shorter' }),
        );
    });

    it('rewrites the whole sentence the selection touches and shows it struck beside the new one, holding Send', async () => {
        const deployment = answeringWith(rewritten('The signed addendum is needed by Friday.'));

        renderDraft(withTwoSentences(), inConversation('pending'), new Map(), deployment.rewriter);
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: shorter' }));

        expect(await screen.findByText('The signed addendum is needed by Friday.')).toBeDefined();
        expect(deployment.asked).toStrictEqual([
            {
                text: twoSentences,
                passages: [{ start: 38, length: 38 }],
                instruction: 'Make it shorter, keeping everything it says.',
            },
        ]);
        expect(screen.getByText('We need the signed addendum by Friday.').tagName).toBe('DEL');
        expect(screen.getByText('Shorter · the struck text stays until you keep the change')).toBeDefined();
        expect(controlsOffered()).toEqual(
            expect.arrayContaining([
                'Keep the rewritten text in the draft to anna@contoso.example',
                'Ask the agent for another version of the same sentences',
                'Leave the draft as it was',
            ]),
        );
        expect(screen.queryByRole('button', { name: 'Send the draft to anna@contoso.example' })).toBeNull();
    });

    it('keeps the change as the draft’s text, and the toast’s Undo puts the text before it back', async () => {
        renderDraft(
            withTwoSentences(),
            inConversation('pending'),
            new Map(),
            answeringWith(rewritten('The signed addendum is needed by Friday.')).rewriter,
        );
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: shorter' }));
        fireEvent.click(await screen.findByRole('button', { name: /^Keep the rewritten text/ }));

        expect(
            screen.getByText('We accept the two-hour response time. The signed addendum is needed by Friday.'),
        ).toBeDefined();
        expect(screen.getByText('rewritten with the agent')).toBeDefined();

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        expect(screen.getByText(twoSentences)).toBeDefined();
        expect(screen.queryByText('rewritten with the agent')).toBeNull();
    });

    it('leaves an edit made after the keep alone when the toast’s Undo is pressed', async () => {
        renderDraft(
            withTwoSentences(),
            inConversation('pending'),
            new Map(),
            answeringWith(rewritten('The signed addendum is needed by Friday.')).rewriter,
        );
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: shorter' }));
        fireEvent.click(await screen.findByRole('button', { name: /^Keep the rewritten text/ }));
        fireEvent.click(screen.getByRole('button', { name: /^Edit here/ }));
        fireEvent.change(screen.getByRole('textbox', { name: 'Draft text' }), {
            target: { value: 'We accept. The signed addendum is needed by Thursday.' },
        });
        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        expect(screen.getByRole('textbox', { name: 'Draft text' })).toHaveProperty(
            'value',
            'We accept. The signed addendum is needed by Thursday.',
        );
        expect(screen.queryByText('rewritten with the agent')).toBeNull();
    });

    it('leaves the draft as it was on Undo', async () => {
        renderDraft(
            withTwoSentences(),
            inConversation('pending'),
            new Map(),
            answeringWith(rewritten('The signed addendum is needed by Friday.')).rewriter,
        );
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: shorter' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Leave the draft as it was' }));

        expect(screen.getByText(twoSentences)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Send the draft to anna@contoso.example' })).toBeDefined();
    });

    it('asks for another version of the same sentences under the next preset on Try again', async () => {
        const deployment = answeringWith(
            rewritten('The signed addendum is needed by Friday.'),
            rewritten('We kindly ask for the signed addendum by Friday.'),
        );

        renderDraft(withTwoSentences(), inConversation('pending'), new Map(), deployment.rewriter);
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: shorter' }));
        fireEvent.click(
            await screen.findByRole('button', { name: 'Ask the agent for another version of the same sentences' }),
        );

        expect(await screen.findByText('We kindly ask for the signed addendum by Friday.')).toBeDefined();
        expect(deployment.asked.map(({ instruction }) => instruction)).toStrictEqual([
            'Make it shorter, keeping everything it says.',
            'Make it more formal.',
        ]);
        expect(
            screen.getByText('More formal · another version · the struck text stays until you keep the change'),
        ).toBeDefined();
    });

    it('asks with the words somebody typed into the bar', async () => {
        const deployment = answeringWith(rewritten('We need the signed 2023 addendum by Friday.'));

        renderDraft(withTwoSentences(), inConversation('pending'), new Map(), deployment.rewriter);
        openTheBar('signed addendum');
        fireEvent.change(screen.getByRole('textbox', { name: 'Say how to rewrite the selection' }), {
            target: { value: 'mention the 2023 addendum' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite' }));

        expect(await screen.findByText('We need the signed 2023 addendum by Friday.')).toBeDefined();
        expect(deployment.asked[0]?.instruction).toBe('mention the 2023 addendum');
    });

    it('says the agent found nothing to change where every sentence came back the same, and leaves the text', async () => {
        renderDraft(
            withTwoSentences(),
            inConversation('pending'),
            new Map(),
            answeringWith(rewritten('We need the signed addendum by Friday.')).rewriter,
        );
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: softer' }));

        expect(await screen.findByText('The agent found nothing to change there.')).toBeDefined();
        expect(screen.getByText(twoSentences)).toBeDefined();
    });

    it('says the allowance is spent rather than rewriting, and leaves the text with the keyboard back on the card', async () => {
        renderDraft(
            withTwoSentences(),
            inConversation('pending'),
            new Map(),
            answeringWith({ outcome: 'read', value: { outcome: 'allowanceSpent' } }).rewriter,
        );
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: firmer' }));

        expect(await screen.findByText(/has spent what it allows its writer for now/)).toBeDefined();
        expect(screen.getByText(twoSentences)).toBeDefined();
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByText(twoSentences).closest('article'));
        });
    });

    it('forgets a selection the text was edited under, rather than raising the button over other words', () => {
        renderDraft(withTwoSentences(), inConversation('pending'), new Map(), answeringWith().rewriter);
        select('signed addendum');

        expect(screen.getByRole('button', { name: 'Rewrite this part with AI' })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: /^Edit here/ }));
        fireEvent.click(screen.getByRole('button', { name: /^Stop editing/ }));

        expect(screen.queryByRole('button', { name: 'Rewrite this part with AI' })).toBeNull();
    });

    it('says a request that failed failed, and leaves the text', async () => {
        renderDraft(
            withTwoSentences(),
            inConversation('pending'),
            new Map(),
            answeringWith({ outcome: 'failed', failure: { reason: 'unavailable', status: null } }).rewriter,
        );
        openTheBar('signed addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Rewrite the selection: firmer' }));

        expect(
            await screen.findByText('The server did not answer. What you wrote is kept here; try again.'),
        ).toBeDefined();
        expect(screen.getByText(twoSentences)).toBeDefined();
    });
});
