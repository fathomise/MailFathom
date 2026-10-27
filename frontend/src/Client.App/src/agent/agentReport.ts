// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { AgentConversationSummary } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { wordRecentInstant } from '../localization/instants';
import type { Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import type { SpaceReport } from '../shell/spaceReport';

// What the Agent reports on its rail tile: the conversations a run is under way in, and the finished ones nobody has
// read. Running pulses, unread alone is a still dot, and neither draws nothing. A conversation put away reports
// neither, since the reader chose not to be told about it.

/** How the agent's words and instants are worded, which is the screen's rather than this function's. */
export interface AgentReportWording {
    readonly locale: Locale;
    readonly timeZone: string | null;
    readonly now: number;
    readonly translate: Translate;
}

const workingCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.workingCount.other',
    one: 'agent.workingCount.one',
    two: 'agent.workingCount.other',
    few: 'agent.workingCount.few',
    many: 'agent.workingCount.many',
    other: 'agent.workingCount.other',
};

const unreadCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.unreadCount.other',
    one: 'agent.unreadCount.one',
    two: 'agent.unreadCount.other',
    few: 'agent.unreadCount.few',
    many: 'agent.unreadCount.many',
    other: 'agent.unreadCount.other',
};

/**
 * @param open Opens one conversation in the Agent space, which is what a row in the peek does.
 */
export function agentReport(
    conversations: readonly AgentConversationSummary[],
    wording: AgentReportWording,
    open: (conversation: string) => void,
): SpaceReport {
    const { locale, timeZone, now, translate } = wording;
    const kept = conversations.filter((listed) => !listed.archived);
    const running = kept.filter((listed) => listed.running);
    const unread = kept.filter((listed) => listed.unread && !listed.running);
    const plurals = new Intl.PluralRules(locale);
    const numbers = new Intl.NumberFormat(locale);
    const titleOf = (listed: AgentConversationSummary): string => listed.title ?? translate('agent.newConversation');

    const counts = [
        ...(running.length === 0
            ? []
            : [translate(workingCounted[plurals.select(running.length)], { count: numbers.format(running.length) })]),
        ...(unread.length === 0
            ? []
            : [translate(unreadCounted[plurals.select(unread.length)], { count: numbers.format(unread.length) })]),
    ];

    const sections = [
        {
            label: translate('agent.peekWorking'),
            kind: 'working' as const,
            rows: running.map((listed) => ({
                key: listed.id,
                title: titleOf(listed),
                detail: translate('agent.runningStatus', {
                    status: listed.status ?? translate('agent.statusFallback'),
                }),
                open: () => {
                    open(listed.id);
                },
            })),
        },
        {
            label: translate('agent.peekUnread'),
            kind: 'waiting' as const,
            rows: unread.map((listed) => ({
                key: listed.id,
                title: titleOf(listed),
                detail: wordRecentInstant(listed.lastActivityAt, locale, now, timeZone) ?? '',
                open: () => {
                    open(listed.id);
                },
            })),
        },
    ].filter((section) => section.rows.length > 0);

    return {
        dot: running.length > 0 ? 'working' : unread.length > 0 ? 'waiting' : null,
        counts,
        sections,
        empty: translate('agent.peekEmpty'),
    };
}
