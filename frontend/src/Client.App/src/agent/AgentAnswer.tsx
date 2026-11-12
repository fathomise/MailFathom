// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useMemo, useState } from 'react';
import type { AgentMessageScope, DeclaredSource } from '@mailfathom/client-backend';
import { DrawnBlock } from '../answerCanvas/AnswerCanvas';
import { answerBlockRenderers } from '../answerCanvas/answerBlocks';
import { AnswerSourcesContext } from '../answerCanvas/answerSources';
import { Containment } from '../containment/Containment';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { AnswerableBlock } from './AnswerableBlock';
import { ranDeep, type Answer, type AnsweredBlock } from './threadTurns';

const scopes: Readonly<Record<AgentMessageScope['kind'], MessageKey>> = {
    mailbox: 'agent.scope.mailbox',
    thread: 'agent.scope.thread',
    calendarEvent: 'agent.scope.calendarEvent',
    discoveryRun: 'agent.scope.discoveryRun',
    notification: 'agent.scope.notification',
};

/** The mode an answer ran in, as the line above it says it. */
interface RanIn {
    readonly said: MessageKey;
    readonly icon: IconName;
    readonly deep: boolean;
}

/**
 * What the line above an answer says about the effort it ran with: the mode its question was asked in, and for `auto`
 * whether the run moved to deep — from the moment it moved — and nothing for a question asked before modes existed.
 */
function ranInOf(answer: Answer): RanIn | null {
    switch (answer.mode) {
        case null:
            return null;
        case 'fast':
            return { said: 'agent.ranIn.fast', icon: 'bolt', deep: false };
        case 'deep':
            return { said: 'agent.ranIn.deep', icon: 'manage_search', deep: true };
        case 'auto':
            return answer.deepened === null
                ? { said: 'agent.ranIn.autoFast', icon: 'auto_mode', deep: false }
                : { said: 'agent.ranIn.autoDeep', icon: 'manage_search', deep: true };
    }
}

const ceilingsFast: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.ceilingFast.other',
    one: 'agent.ceilingFast.one',
    two: 'agent.ceilingFast.other',
    few: 'agent.ceilingFast.few',
    many: 'agent.ceilingFast.many',
    other: 'agent.ceilingFast.other',
};

const ceilingsDeep: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.ceilingDeep.other',
    one: 'agent.ceilingDeep.one',
    two: 'agent.ceilingDeep.other',
    few: 'agent.ceilingDeep.few',
    many: 'agent.ceilingDeep.many',
    other: 'agent.ceilingDeep.other',
};

/**
 * The line above everything the agent wrote, which says it was the agent, the mode an answer ran in where it carries
 * one, and what it was working across.
 */
export function AnswerScope({ scope, ranIn = null }: { readonly scope: MessageKey; readonly ranIn?: RanIn | null }) {
    const { translate } = useLocalization();

    return (
        <p className="flex items-center gap-1.75 text-xs text-muted">
            <span className="font-semibold text-accent-deep">{translate('ai.badge')}</span>
            {ranIn === null ? null : (
                <>
                    <span aria-hidden="true">{translate('agent.lineSeparator')}</span>
                    <span className={`inline-flex items-center gap-1 ${ranIn.deep ? 'text-accent-deep' : ''}`}>
                        <Icon name={ranIn.icon} className="size-icon-xs" />
                        {translate(ranIn.said)}
                    </span>
                </>
            )}
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
 * The files it hands over stand under everything else it wrote, in one row that wraps, whichever turn of the run
 * placed them: the design draws an answer's files after its words, and a file placed before the words were composed
 * would otherwise stand above the sentence that says why it is there.
 *
 * @param settledThrough What was already there when the conversation was opened, which draws without animating in.
 * @param onFollowSource What following one of this answer's sources does, which is opening it for checking.
 * @param onAskInDeep Asks this answer's question again in `deep`, about what it was asked about and with the files it
 * carried, answering once it was
 * sent or refused, and `null` while the conversation cannot take a question — an answer is being composed in it.
 */
export function AgentAnswer({
    answer,
    settledThrough,
    onFollowSource,
    onAskInDeep,
}: {
    readonly answer: Answer;
    readonly settledThrough: number;
    readonly onFollowSource: (source: DeclaredSource) => void;
    readonly onAskInDeep:
        ((question: string, scope: AgentMessageScope | null, files: readonly string[]) => Promise<unknown>) | null;
}) {
    const { locale, translate } = useLocalization();
    const deep = ranDeep(answer);
    const deepNow = deep && answer.ending === null;

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

    const files = answer.blocks.filter((answered) => answered.block.type === 'file');
    const written = answer.blocks.filter((answered) => answered.block.type !== 'file');

    return (
        <article
            aria-label={translate('agent.answer')}
            className={`flex w-full max-w-agent-answer flex-col gap-2.75 ${
                answer.sequence > settledThrough ? 'animate-arrival' : ''
            }`}
        >
            <AnswerScope
                scope={answer.scope === null ? 'agent.scope.mailbox' : scopes[answer.scope.kind]}
                ranIn={ranInOf(answer)}
            />

            {answer.deepened === null || answer.ending === null ? null : (
                <p className="-mt-1 text-base text-muted text-pretty">{answer.deepened}</p>
            )}

            {answer.blocks.length === 0 && answer.ending === null && !deepNow ? (
                <p className="flex items-center gap-2 text-md text-muted">
                    <Icon name="autorenew" className="size-icon-sm animate-spin text-accent-deep" />
                    {translate('agent.working')}
                </p>
            ) : null}

            {answer.blocks.length > 0 ? (
                <AnswerSourcesContext value={declared}>
                    {written.length > 0 ? (
                        <ol aria-label={translate('answerCanvas.blocks')} className="flex flex-col gap-2.75">
                            {written.map((answered) => (
                                <li key={answered.sequence} className={arrivalOf(answered, settledThrough)}>
                                    <Drawn answered={answered} />
                                </li>
                            ))}
                        </ol>
                    ) : null}

                    {files.length > 0 ? (
                        <ul aria-label={translate('agent.answerFiles')} className="flex flex-wrap gap-2">
                            {files.map((answered) => (
                                <li
                                    key={answered.sequence}
                                    className={`flex w-full workspace:w-answer-file workspace:max-w-full ${arrivalOf(answered, settledThrough) ?? ''}`}
                                >
                                    <Drawn answered={answered} />
                                </li>
                            ))}
                        </ul>
                    ) : null}
                </AnswerSourcesContext>
            ) : null}

            {answer.ceiling === null ? null : (
                <div className="flex flex-col gap-2.5 rounded-field border border-line bg-panel px-3.5 py-3">
                    <p className="flex items-start gap-2.25 text-md text-text-soft text-pretty">
                        <Icon name="hourglass_bottom" className="size-icon-md text-warning-text" />
                        {translate(
                            (deep ? ceilingsDeep : ceilingsFast)[
                                new Intl.PluralRules(locale).select(answer.ceiling.messagesRead)
                            ],
                            {
                                count: new Intl.NumberFormat(locale).format(answer.ceiling.messagesRead),
                            },
                        )}
                    </p>
                    {deep || answer.question === null ? null : (
                        <AskInDeep
                            question={answer.question}
                            scope={answer.scope}
                            files={answer.files}
                            onAsk={onAskInDeep}
                        />
                    )}
                </div>
            )}

            {deepNow ? (
                <div className="flex max-w-agent-deep-run flex-col gap-2">
                    <p className="flex items-center gap-2.25">
                        <Icon name="progress_activity" className="size-icon-md animate-spin text-accent-deep" />
                        <span role="status" aria-live="polite" className="min-w-0 text-lg text-text-soft">
                            {answer.status ?? translate('agent.statusFallback')}
                        </span>
                    </p>
                    <p className="ms-6.75 flex items-start gap-2 text-base text-muted text-pretty">
                        <Icon name="directions_walk" className="mt-0.25 size-icon-sm" />
                        {translate('agent.deepLeave')}
                    </p>
                </div>
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

/**
 * The offer under a fast answer that stopped at its ceiling: the same question, asked again in `deep`. It stands aside
 * while that question is being sent, so one press asks once, and while the conversation cannot take a question.
 */
function AskInDeep({
    question,
    scope,
    files,
    onAsk,
}: {
    readonly question: string;
    readonly scope: AgentMessageScope | null;
    readonly files: readonly string[];
    readonly onAsk:
        ((question: string, scope: AgentMessageScope | null, files: readonly string[]) => Promise<unknown>) | null;
}) {
    const { translate } = useLocalization();
    const [asking, setAsking] = useState(false);

    async function ask(
        send: (question: string, scope: AgentMessageScope | null, files: readonly string[]) => Promise<unknown>,
    ): Promise<void> {
        setAsking(true);
        await send(question, scope, files);
        setAsking(false);
    }

    return (
        <button
            type="button"
            disabled={asking || onAsk === null}
            className="inline-flex h-8 items-center gap-1.5 self-start rounded-md border border-accent-line bg-accent-soft px-3 text-base font-semibold whitespace-nowrap text-accent-deep transition hover:border-accent disabled:opacity-60"
            onClick={() => {
                if (onAsk !== null) {
                    void ask(onAsk);
                }
            }}
        >
            <Icon name="manage_search" className="size-icon-sm" />
            {translate('agent.askAgainDeep')}
        </button>
    );
}

/** One block, contained so a renderer that throws takes only itself down, and answerable where it proposes something. */
function Drawn({ answered }: { readonly answered: AnsweredBlock }) {
    return (
        <Containment drawing={String(answered.sequence)} region="answer_block">
            <AnswerableBlock phase={answered.phase} proposedAt={answered.sequence}>
                <DrawnBlock block={answered} renderers={answerBlockRenderers} />
            </AnswerableBlock>
        </Containment>
    );
}

/** A block written after the conversation was opened arrives animated; one already there when it was opened does not. */
function arrivalOf(answered: AnsweredBlock, settledThrough: number): string | undefined {
    return answered.sequence > settledThrough ? 'animate-block-arrival' : undefined;
}
