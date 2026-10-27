// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it, vi } from 'vitest';
import type { AgentConversationSummary } from '@mailfathom/client-backend';
import { en } from '../localization/en';
import { pl } from '../localization/pl';
import { agentReport, type AgentReportWording } from './agentReport';

const quiet: AgentConversationSummary = {
    id: 'quiet',
    title: 'Supplier quotes',
    startedAt: '2026-08-31T09:40:00+00:00',
    lastActivityAt: '2026-08-31T09:42:00+00:00',
    archived: false,
    running: false,
    status: null,
    unread: false,
};

const running: AgentConversationSummary = {
    ...quiet,
    id: 'running',
    title: 'Overdue invoices',
    running: true,
    status: 'Searching your mail',
};

const unread: AgentConversationSummary = { ...quiet, id: 'unread', title: null, unread: true };

function wordedIn(catalogue: Readonly<Record<string, string>>, locale: 'en' | 'pl'): AgentReportWording {
    return {
        locale,
        timeZone: 'UTC',
        now: Date.parse('2026-08-31T12:00:00Z'),
        translate: (key, values = {}) =>
            Object.entries(values).reduce(
                (text, [name, value]) => text.replace(`{${name}}`, value),
                catalogue[key] ?? key,
            ),
    };
}

const english = wordedIn(en, 'en');

describe('agentReport', () => {
    it('pulses while a run is under way, stands still while only something unread waits, and draws nothing otherwise', () => {
        expect(agentReport([running, unread], english, () => undefined).dot).toBe('working');
        expect(agentReport([quiet, unread], english, () => undefined).dot).toBe('waiting');
        expect(agentReport([quiet], english, () => undefined).dot).toBeNull();
    });

    it('lists what runs under its status and what waits unread under when it finished', () => {
        const report = agentReport([running, unread, quiet], english, () => undefined);

        expect(report.counts).toEqual(['1 working', '1 unread']);
        expect(
            report.sections.map((section) => [
                section.label,
                section.kind,
                section.rows.map((row) => [row.title, row.detail]),
            ]),
        ).toEqual([
            ['Working on', 'working', [['Overdue invoices', 'Searching your mail…']]],
            ['Unread', 'waiting', [['New conversation', '9:42 AM']]],
        ]);
    });

    it('reports nothing about a conversation the reader put away', () => {
        const report = agentReport(
            [
                { ...running, archived: true },
                { ...unread, archived: true },
            ],
            english,
            () => undefined,
        );

        expect([report.dot, report.counts, report.sections]).toEqual([null, [], []]);
    });

    it('words a run that has said nothing yet as going through the mail', () => {
        const report = agentReport([{ ...running, status: null }], english, () => undefined);

        expect(report.sections[0]?.rows[0]?.detail).toBe('Going through your mail…');
    });

    it('counts by the language’s own plural forms', () => {
        const many = Array.from({ length: 5 }, (_, at) => ({ ...unread, id: `u${String(at)}` }));

        expect(agentReport(many, wordedIn(pl, 'pl'), () => undefined).counts).toEqual(['5 nieprzeczytanych']);
        expect(agentReport(many.slice(0, 2), wordedIn(pl, 'pl'), () => undefined).counts).toEqual(['2 nieprzeczytane']);
    });

    it('opens the conversation a row names', () => {
        const open = vi.fn();

        agentReport([running], english, open).sections[0]?.rows[0]?.open();

        expect(open).toHaveBeenCalledWith('running');
    });
});
