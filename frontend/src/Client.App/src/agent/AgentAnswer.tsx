// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useMemo } from 'react';
import type { AgentMessageScope, DeclaredSource } from '@mailfathom/client-backend';
import { DrawnBlock } from '../answerCanvas/AnswerCanvas';
import { answerBlockRenderers } from '../answerCanvas/answerBlocks';
import { AnswerSourcesContext } from '../answerCanvas/answerSources';
import { Containment } from '../containment/Containment';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { AnswerableBlock } from './AnswerableBlock';
import type { Answer } from './threadTurns';

const scopes: Readonly<Record<AgentMessageScope['kind'], MessageKey>> = {
    mailbox: 'agent.scope.mailbox',
    thread: 'agent.scope.thread',
    calendarEvent: 'agent.scope.calendarEvent',
    discoveryRun: 'agent.scope.discoveryRun',
    notification: 'agent.scope.notification',
};

/**
 * The line above everything the agent wrote, which says it was the agent and what it was working across.
 */
export function AnswerScope({ scope }: { readonly scope: MessageKey }) {
    const { translate } = useLocalization();

    return (
        <p className="flex items-center gap-1.75 text-xs text-muted">
            <span className="font-semibold text-accent-deep">{translate('ai.badge')}</span>
            <span>{translate('agent.scopeLine', { scope: translate(scope) })}</span>
        </p>
    );
}

/**
 * One answer, as far as it has been composed: the blocks it has published, each drawn by the renderer the catalogue
 * registers for its type, and a working line where it has published nothing yet.
 *
 * It carries no card of its own. The thread is flat and the blocks are the only cards in it, so the separation between
 * one answer and the next is carried by them.
 *
 * @param settledThrough What was already there when the conversation was opened, which draws without animating in.
 * @param onFollowSource What following one of this answer's sources does, which is opening it for checking.
 */
export function AgentAnswer({
    answer,
    settledThrough,
    onFollowSource,
}: {
    readonly answer: Answer;
    readonly settledThrough: number;
    readonly onFollowSource: (source: DeclaredSource) => void;
}) {
    const { translate } = useLocalization();

    // Memoized against what it is composed of, because every renderer reads the context and a fresh object each render
    // would redraw the whole answer whenever anything above it rendered. A citation names a source by the name this
    // answer declared it under, so it is resolved here, where those names mean something.
    const declared = useMemo(
        () => ({
            sources: answer.sources,
            follow: (name: string) => {
                const source = answer.sources.get(name);

                if (source !== undefined) {
                    onFollowSource(source);
                }
            },
        }),
        [answer.sources, onFollowSource],
    );

    return (
        <article
            aria-label={translate('agent.answer')}
            className={`flex w-full max-w-agent-answer flex-col gap-2.75 ${
                answer.sequence > settledThrough ? 'animate-arrival' : ''
            }`}
        >
            <AnswerScope scope={answer.scope === null ? 'agent.scope.mailbox' : scopes[answer.scope.kind]} />

            {answer.blocks.length === 0 && answer.ending === null ? (
                <p className="flex items-center gap-2 text-md text-muted">
                    <Icon name="autorenew" className="size-icon-sm animate-spin text-accent-deep" />
                    {translate('agent.working')}
                </p>
            ) : null}

            {answer.blocks.length > 0 ? (
                <AnswerSourcesContext value={declared}>
                    <ol aria-label={translate('answerCanvas.blocks')} className="flex flex-col gap-2.75">
                        {answer.blocks.map((answered) => (
                            <li
                                key={answered.sequence}
                                className={answered.sequence > settledThrough ? 'animate-block-arrival' : undefined}
                            >
                                <Containment drawing={String(answered.sequence)} region="answer_block">
                                    <AnswerableBlock phase={answered.phase} proposedAt={answered.sequence}>
                                        <DrawnBlock block={answered} renderers={answerBlockRenderers} />
                                    </AnswerableBlock>
                                </Containment>
                            </li>
                        ))}
                    </ol>
                </AnswerSourcesContext>
            ) : null}

            {answer.ending === 'failed' ? (
                <p role="status" className="flex items-start gap-1.75 text-md text-warning-text">
                    <Icon name="error" className="mt-0.5 size-icon-sm shrink-0" />
                    {translate('agent.failed')}
                </p>
            ) : null}
        </article>
    );
}
