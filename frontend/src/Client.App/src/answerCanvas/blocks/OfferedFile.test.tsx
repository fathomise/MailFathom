// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    AnswerBlock,
    AnswerFile,
    BlockEvidence,
    ClientRequest,
    ClientSession,
    DeclaredSource,
} from '@mailfathom/client-backend';
import {
    AttachmentExchangeContext,
    type AttachmentDeliveryOutcome,
    type AttachmentExchange,
} from '../../deployment/attachmentExchange';
import { LocalizationProvider } from '../../localization/Localization';
import { ReadingZoneContext } from '../../localization/useReadingZone';
import { ToastContext, type ToastSurface } from '../../toasts/useToasts';
import { AnswerDownloadsContext } from '../answerDownloads';
import { AnswerSourcesContext } from '../answerSources';
import { OfferedFile } from './OfferedFile';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '0198f4a1-0000-7000-8000-000000000001';

const carried: DeclaredSource = {
    id: 'm1',
    target: { kind: 'email', email: messageId },
    label: 'Harbour Lane: completion documents',
    medium: 'Written',
    unreadable: null,
};

const cited: BlockEvidence = {
    support: 'Cited',
    citations: ['m1'],
    freshness: { staleness: 'Unknown', observedAt: null },
    conflictingClaims: [],
};

// Sent in an earlier year than any test runs in, so the date is worded the same whenever the suite runs.
const certificate: AnswerFile = {
    source: 'm1',
    attachmentPosition: 2,
    name: 'Harbour-Lane-insurance-certificate.pdf',
    mediaType: 'application/pdf',
    sizeOctets: 3_584,
    sender: 'Clara Weissmann',
    sentAt: '2025-09-11T11:45:00+00:00',
};

function offered(file: AnswerFile = certificate): AnswerBlock {
    return { type: 'file', named: 'file', evidence: cited, file };
}

/** An exchange whose delivery records what it was asked and answers when the test says so, never on its own. */
function deliveryHeldOpen(): {
    exchange: AttachmentExchange;
    asked: ClientRequest[];
    answer: (outcome: AttachmentDeliveryOutcome) => void;
} {
    const asked: ClientRequest[] = [];
    const settling: ((outcome: AttachmentDeliveryOutcome) => void)[] = [];

    return {
        asked,
        answer: (outcome) => {
            settling.at(-1)?.(outcome);
        },
        exchange: {
            deliver: (request) => {
                asked.push(request);

                return new Promise<AttachmentDeliveryOutcome>((resolve) => {
                    settling.push(resolve);
                });
            },
            read: () => Promise.reject(new Error('An answer’s file asked to show itself rather than to download.')),
            take: () => Promise.reject(new Error('An answer’s file asked to be taken rather than downloaded.')),
        },
    };
}

const quietToasts: ToastSurface = {
    raise: () => undefined,
    raiseOperation: () => () => undefined,
};

function drawing(
    block: AnswerBlock,
    exchange: AttachmentExchange,
    {
        downloadsAs = session,
        sources = [carried],
    }: { downloadsAs?: ClientSession | null; sources?: readonly DeclaredSource[] } = {},
) {
    return render(
        <LocalizationProvider>
            <ReadingZoneContext value="UTC">
                <ToastContext value={quietToasts}>
                    <AttachmentExchangeContext value={exchange}>
                        <AnswerDownloadsContext value={downloadsAs}>
                            <AnswerSourcesContext
                                value={{ sources: new Map(sources.map((source) => [source.id, source])), follow: null }}
                            >
                                <OfferedFile block={block} />
                            </AnswerSourcesContext>
                        </AnswerDownloadsContext>
                    </AttachmentExchangeContext>
                </ToastContext>
            </ReadingZoneContext>
        </LocalizationProvider>,
    );
}

describe('OfferedFile', () => {
    it('names the file, its kind, its size, and the message it came from as one control that downloads it', () => {
        drawing(offered(), deliveryHeldOpen().exchange);

        const card = screen.getByRole('button', { name: 'Download Harbour-Lane-insurance-certificate.pdf, 3.6 kB' });

        expect(card.textContent).toContain('pdf');
        expect(card.textContent).toContain('From Clara Weissmann · 9/11/25 · 3.6 kB');
    });

    it('asks for the file at its place on the message it arrived on, bounded by the size the message said', () => {
        const held = deliveryHeldOpen();
        drawing(offered(), held.exchange);

        fireEvent.click(screen.getByRole('button', { name: /^Download/u }));

        expect(held.asked[0]?.path).toBe(`https://mail.example.invalid/api/client/messages/${messageId}/attachments/2`);
        expect(held.asked[0]?.longestAnswer).toBe(3_584);
    });

    it('lets go of a file the deployment no longer holds for this reader, saying so and offering nothing to press', async () => {
        const held = deliveryHeldOpen();
        drawing(offered(), held.exchange);

        fireEvent.click(screen.getByRole('button', { name: /^Download/u }));

        await act(async () => {
            held.answer('missing');
            await Promise.resolve();
        });

        expect(screen.queryByRole('button')).toBeNull();
        expect(
            screen.getByText(
                'No longer available - the message from Clara Weissmann · 9/11/25 was removed, or you can no longer open it',
            ),
        ).toBeDefined();
    });

    it('keeps offering a file whose download the server did not answer, which a second press may well bring', async () => {
        const held = deliveryHeldOpen();
        drawing(offered(), held.exchange);

        fireEvent.click(screen.getByRole('button', { name: /^Download/u }));

        await act(async () => {
            held.answer('unavailable');
            await Promise.resolve();
        });

        expect(screen.getByRole('button', { name: /^Download/u })).toBeDefined();
    });

    it.each([
        ['a surface that offers no download', { downloadsAs: null }],
        ['a source this client cannot follow', { sources: [{ ...carried, target: null }] }],
    ] as const)('names the file and offers nothing to press on %s', (_, drawn) => {
        drawing(offered(), deliveryHeldOpen().exchange, drawn);

        expect(screen.queryByRole('button')).toBeNull();
        expect(screen.getByText('Harbour-Lane-insurance-certificate.pdf')).toBeDefined();
    });

    it('names a file its sender gave no name, from a message naming nobody, by what it is', () => {
        drawing(offered({ ...certificate, name: null, sender: null, sentAt: null }), deliveryHeldOpen().exchange);

        expect(screen.getByRole('button', { name: 'Download Unnamed file, 3.6 kB' }).textContent).toContain(
            'From an unnamed sender · 3.6 kB',
        );
    });
});
