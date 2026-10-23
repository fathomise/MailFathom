// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { ComposerOpening } from '../composer/composition';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { ReplyDraftCard } from './ReplyDraftCard';
import { ReplyDraftingContext } from './replyDrafting';
import { ThreadAgentField } from './ThreadAgentField';
import { useReplyDraftingState } from './useReplyDraftingState';

// The field and the card are drawn a scroller apart and share one draft, so they are driven together here, through the
// state the frame holds for them and a transport answering the one route they reach. What is asserted is what a reader
// meets: the wording at each composition, the card that appears, and where focus goes when it leaves.

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const answering = '00000000-0000-4000-8000-000000000042';
const another = '00000000-0000-4000-8000-000000000043';

const draftedWords = 'We accept the two-hour response time.';

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

function answeringDrafts(drafting: { readonly status: number; readonly body: string } | null = null): {
    readonly transport: MailFathomTransport;
    readonly asked: ClientRequest[];
} {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const answer = drafting ?? {
                status: 200,
                body: JSON.stringify({ drafted: true, body: draftedWords, claims: [], proposedRecipients: [] }),
            };

            return Promise.resolve({ ...answer, headers: {} });
        },
    };
}

/** A deployment that has not answered yet, and will not while the test is looking. */
const neverAnswering: MailFathomTransport = () => new Promise(() => undefined);

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

const signedInAs = 'https://mail.example.invalid\nagata';

function Frame({
    person,
    presented,
    transport,
}: {
    readonly person: string;
    readonly presented: ClientSession;
    readonly transport: MailFathomTransport;
}) {
    const drafting = useReplyDraftingState(person, presented, transport);

    return (
        <ReplyDraftingContext value={drafting}>
            <ReplyDraftCard under={answering} />
            <ThreadAgentField />
        </ReplyDraftingContext>
    );
}

function drawing(
    transport: MailFathomTransport,
    as: Partial<Workspace> = {},
    offered = true,
): {
    composed: ReturnType<typeof vi.fn<(opening: ComposerOpening) => void>>;
    workspace: () => Workspace;
    opening: (selection: string) => void;
    renewing: () => void;
    signingIn: (person: string) => void;
} {
    const composed = vi.fn<(opening: ComposerOpening) => void>();
    let last: Workspace | null = null;

    const tree = (standing: Partial<Workspace>, presented: ClientSession = session, person = signedInAs) => (
        <LocalizationProvider>
            <ToastsProvider>
                <WorkspaceProvider>
                    <ComposingContext
                        value={{ offered, drafts: true, opening: null, compose: composed, close: () => undefined }}
                    >
                        <Standing as={standing} />
                        <Frame person={person} presented={presented} transport={transport} />
                        <Reporting
                            onWorkspace={(workspace) => {
                                last = workspace;
                            }}
                        />
                    </ComposingContext>
                </WorkspaceProvider>
            </ToastsProvider>
        </LocalizationProvider>
    );

    const { rerender } = render(tree({ selection: answering, ...as }));

    return {
        composed,
        opening: (selection) => {
            rerender(tree({ selection }));
        },
        // A renewal replaces the credential an hour before it expires, and the frame builds a new session object
        // for it, with nobody having signed out or in.
        renewing: () => {
            rerender(tree({ selection: answering, ...as }, { ...session, authorization: 'Bearer renewed' }));
        },
        signingIn: (person) => {
            rerender(tree({ selection: answering, ...as }, { ...session, authorization: 'Basic b3RoZXI=' }, person));
        },
        workspace: () => {
            if (last === null) {
                throw new Error('The workspace was never reported.');
            }

            return last;
        },
    };
}

const drafterName = 'Draft a reply to this correspondence';

/** Opens the drafter where it is folded into its button, which is how it stands until somebody asks for it. */
function openingTheDrafter(): void {
    if (screen.queryByRole('form', { name: drafterName }) === null) {
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
    it('stands folded into its button until somebody asks for it', () => {
        drawing(answeringDrafts().transport);

        expect(screen.getByRole('button', { name: 'Draft a reply' })).toBeDefined();
        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
    });

    it('opens the drafter from its button, with the keyboard in the field', () => {
        drawing(answeringDrafts().transport);

        fireEvent.click(screen.getByRole('button', { name: 'Draft a reply' }));

        expect(screen.getByRole('form', { name: drafterName })).toBeDefined();
        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: drafterName }));
        expect(screen.queryByRole('button', { name: 'Draft a reply' })).toBeNull();
    });

    it('folds the drafter back into its button from the close badge, and puts the keyboard on the button', () => {
        drawing(answeringDrafts().transport);
        openingTheDrafter();

        fireEvent.click(screen.getByRole('button', { name: 'Close the reply drafter' }));

        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Draft a reply' }));
    });

    it('puts the keyboard on the drafter’s field when a draft is discarded with the drafter open', async () => {
        drawing(answeringDrafts().transport);

        draftingAReply();
        const discard = await screen.findByRole('button', { name: 'Discard draft' });
        openingTheDrafter();
        fireEvent.click(discard);

        expect(screen.queryByRole('region', { name: 'Draft · local' })).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: drafterName }));
    });

    it('folds the drafter back on Escape', () => {
        drawing(answeringDrafts().transport);

        fireEvent.keyDown(field(), { key: 'Escape' });

        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
    });

    it('folds the drafter back once the reply is asked for', () => {
        drawing(answeringDrafts().transport);

        draftingAReply();

        expect(screen.queryByRole('form', { name: drafterName })).toBeNull();
        expect(screen.getByRole('button', { name: 'Draft a reply' })).toBeDefined();
    });

    it('keeps what was typed while it is folded, so reopening it is not starting again', () => {
        drawing(answeringDrafts().transport);

        fireEvent.change(field(), { target: { value: 'accept the SLA' } });
        fireEvent.click(screen.getByRole('button', { name: 'Close the reply drafter' }));

        expect(field()).toHaveProperty('value', 'accept the SLA');
    });

    it('names the press in full where two panes are drawn, and speaks only of drafting a reply', () => {
        theTabletComposition();

        drawing(answeringDrafts().transport);

        expect(field()).toHaveProperty('placeholder', 'Describe the reply you want to send…');
        expect(screen.getByRole('button', { name: 'Draft a reply' })).toBeDefined();
    });

    it('shortens the press and the placeholder on one pane, where there is no room for the sentence', () => {
        drawing(answeringDrafts().transport);

        expect(field()).toHaveProperty('placeholder', 'Describe the reply…');
        expect(screen.getByRole('button', { name: 'Draft' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Draft a reply' })).toBeNull();
    });

    it('says what the draft is about and where it stays in the desktop composition', () => {
        theDesktopComposition();

        drawing(answeringDrafts().transport);
        openingTheDrafter();

        expect(screen.getByText('Scope: this thread')).toBeDefined();
        expect(screen.getByText('+ selected passage')).toBeDefined();
        expect(screen.getByText('Draft stays local until confirmed')).toBeDefined();
    });

    it('draws none of that below the desktop composition', () => {
        theTabletComposition();

        drawing(answeringDrafts().transport);
        openingTheDrafter();

        expect(screen.queryByText('Scope: this thread')).toBeNull();
        expect(screen.queryByText('Draft stays local until confirmed')).toBeNull();
    });

    // The two fields hold separate sentences: what is typed under a correspondence is about that correspondence, and
    // what was typed into Discover's question is not something to draft a reply with.
    it('keeps what is typed here apart from Discover’s question, in both directions', () => {
        const { workspace } = drawing(answeringDrafts().transport, { question: 'what did Nordwind send' });

        expect(field()).toHaveProperty('value', '');

        fireEvent.change(field(), { target: { value: 'accept the SLA' } });

        expect(workspace().question).toBe('what did Nordwind send');
    });
});

describe('ReplyDraftCard', () => {
    it('drafts the reply into a card in the thread, asking the deployment once for it', async () => {
        const { transport, asked } = answeringDrafts();

        drawing(transport);
        fireEvent.change(field(), { target: { value: 'accept the SLA' } });
        draftingAReply();

        const card = await screen.findByRole('region', { name: 'Draft · local' });

        await waitFor(() => {
            expect(card.textContent).toContain(draftedWords);
        });

        expect(asked).toHaveLength(1);
        expect(asked[0]?.path.endsWith('/replies/drafting')).toBe(true);
        expect(JSON.parse(asked[0]?.body ?? '')).toMatchObject({
            answeredEmailId: answering,
            selection: null,
            instruction: 'Write a reply to this message. accept the SLA',
        });
    });

    it('says a reply is being written while the deployment writes it, and offers nothing to act on yet', async () => {
        drawing(neverAnswering);
        draftingAReply();

        expect(await screen.findByText('AI is drafting…')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Discard draft' })).toBeNull();
    });

    it('asks once however often the press is made while a reply is being written', async () => {
        const asked: ClientRequest[] = [];

        drawing((request) => {
            asked.push(request);

            return new Promise(() => undefined);
        });
        draftingAReply();
        await screen.findByText('AI is drafting…');
        draftingAReply();

        expect(asked).toHaveLength(1);
    });

    it('lets a reply be asked under another message while the first is still being written', async () => {
        const asked: ClientRequest[] = [];

        const { opening } = drawing((request) => {
            asked.push(request);

            return new Promise(() => undefined);
        });
        draftingAReply();
        await screen.findByText('AI is drafting…');
        opening(another);
        openingTheDrafter();

        expect(screen.getByRole('button', { name: 'Draft' }).getAttribute('aria-disabled')).toBe('false');

        draftingAReply();

        expect(asked).toHaveLength(2);
        expect(JSON.parse(asked[1]?.body ?? '')).toMatchObject({ answeredEmailId: another });
    });

    it('carries the passage somebody selected into what the reply is written about', async () => {
        const { transport, asked } = answeringDrafts();

        drawing(transport, { fragment: { messageId: answering, text: 'the response time is two hours' } });
        draftingAReply();

        await screen.findByRole('region', { name: 'Draft · local' });

        expect(JSON.parse(asked[0]?.body ?? '')).toMatchObject({ selection: 'the response time is two hours' });
    });

    it('opens the composer holding the draft, and lets the card go', async () => {
        const { composed } = drawing(answeringDrafts().transport);

        draftingAReply();
        fireEvent.click(await screen.findByRole('button', { name: 'Open in composer' }));

        expect(composed).toHaveBeenCalledWith({
            kind: 'answer',
            answers: 'senderOnly',
            storedEmailId: answering,
            drafted: draftedWords,
        });
        expect(screen.queryByRole('region', { name: 'Draft · local' })).toBeNull();
    });

    it('discards the draft and puts the keyboard back on the button it was asked for from', async () => {
        drawing(answeringDrafts().transport);

        draftingAReply();
        fireEvent.click(await screen.findByRole('button', { name: 'Discard draft' }));

        expect(screen.queryByRole('region', { name: 'Draft · local' })).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Draft a reply' }));
    });

    it('offers no composer where the deployment offers none, and still lets the draft go', async () => {
        drawing(answeringDrafts().transport, {}, false);

        draftingAReply();

        expect(await screen.findByRole('button', { name: 'Discard draft' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Open in composer' })).toBeNull();
    });

    it('draws the card under the message it answers and under no other', async () => {
        const { opening } = drawing(answeringDrafts().transport);

        draftingAReply();
        await screen.findByRole('region', { name: 'Draft · local' });
        opening(another);

        expect(screen.queryByRole('region', { name: 'Draft · local' })).toBeNull();
    });

    it('keeps the draft through a renewal of the credential, which signs nobody out', async () => {
        const { renewing } = drawing(answeringDrafts().transport);

        draftingAReply();
        const card = await screen.findByRole('region', { name: 'Draft · local' });
        await waitFor(() => {
            expect(card.textContent).toContain(draftedWords);
        });

        renewing();

        expect(screen.getByRole('region', { name: 'Draft · local' }).textContent).toContain(draftedWords);
    });

    it('drops the draft when another person signs in', async () => {
        const { signingIn } = drawing(answeringDrafts().transport);

        draftingAReply();
        const card = await screen.findByRole('region', { name: 'Draft · local' });
        await waitFor(() => {
            expect(card.textContent).toContain(draftedWords);
        });

        signingIn('https://mail.example.invalid\nbelinda');

        expect(screen.queryByRole('region', { name: 'Draft · local' })).toBeNull();
    });

    it('says nothing has been sent on the card where the workspace is wide', async () => {
        theTabletComposition();

        drawing(answeringDrafts().transport);
        draftingAReply();

        const card = await screen.findByRole('region', { name: 'Draft · local' });

        expect(within(card).getByText('nothing has been sent')).toBeDefined();
    });

    it.each([
        {
            case: 'a deployment that drafted nothing',
            answer: { status: 200, body: JSON.stringify({ drafted: false }) },
            said: 'The server wrote no draft this time.',
        },
        {
            case: 'a message the deployment no longer holds',
            answer: { status: 404, body: '' },
            said: 'The server wrote no draft this time.',
        },
        {
            case: 'a deployment that did not answer',
            answer: { status: 503, body: '' },
            said: 'The server did not answer.',
        },
        {
            case: 'a refused credential',
            answer: { status: 401, body: '' },
            said: 'This client is no longer signed in.',
        },
    ])('says why nothing was drafted for $case, and draws no card', async ({ answer, said }) => {
        drawing(answeringDrafts(answer).transport);

        draftingAReply();

        expect(await screen.findByText('Nothing was drafted')).toBeDefined();
        expect(screen.getByText(said, { exact: false })).toBeDefined();
        expect(screen.queryByRole('region', { name: 'Draft · local' })).toBeNull();
    });

    it('says why nothing was drafted where the allowance is spent, and draws no card', async () => {
        drawing(answeringDrafts({ status: 429, body: '' }).transport);

        draftingAReply();

        expect(await screen.findByText('Nothing was drafted')).toBeDefined();
        expect(screen.queryByRole('region', { name: 'Draft · local' })).toBeNull();
    });
});
