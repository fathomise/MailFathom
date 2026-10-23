// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { AnswerBlock, EventProposal as ProposedEvent } from '@mailfathom/client-backend';
import { wordInstant, wordInstantRange, wordLasting } from '../../localization/instants';
import type { Locale } from '../../localization/locale';
import { useLocalization } from '../../localization/useLocalization';
import { useReadingZone } from '../../localization/useReadingZone';
import { UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { useAnswerSources } from '../answerSources';
import { ProposalCard, ProposalFields, ProposalTitle, type ProposalControl } from '../ProposalCard';
import { citedThread, useProposalAnswering } from '../proposalAnswering';

// A date the agent would put on the person's calendar, drawn with everything they check before agreeing: when, for how
// long, and which thread it came out of. It is the one proposal that can be answered with *another time*, which declines
// this one and asks again in the reader's own words rather than moving the hour silently.

const minutesInADay = 24 * 60;

/** How long the event would last, and nothing where the proposal states no end. */
function wordDuration(event: ProposedEvent, locale: Locale): string | null {
    if (event.end === null) {
        return null;
    }

    const minutes = Math.round((Date.parse(event.end) - Date.parse(event.start)) / 60_000);

    return wordLasting(
        event.isAllDay
            ? { unit: 'day', count: Math.max(1, Math.round(minutes / minutesInADay)) }
            : { unit: 'minute', count: minutes },
        locale,
        'long',
    );
}

export function EventProposal({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();
    const zone = useReadingZone();
    const { sources } = useAnswerSources();
    const answering = useProposalAnswering();

    if (block.type !== 'eventProposal') {
        return <UnrecognisedAnswerBlock />;
    }

    const { proposal, evidence } = block;
    const title = proposal.title;
    const when =
        (proposal.isAllDay
            ? wordInstant(proposal.start, locale, 'day', zone)
            : wordInstantRange(proposal.start, proposal.end, locale, 'full', zone)) ?? proposal.start;
    const lasts = wordDuration(proposal, locale);
    const thread = citedThread(evidence.citations, sources);

    const controls: readonly ProposalControl[] =
        answering === null
            ? []
            : [
                  {
                      said: translate('eventProposal.add'),
                      name: translate('eventProposal.addName', { title, when }),
                      primary: true,
                      run: () => {
                          answering.answer('accepted');
                      },
                  },
                  {
                      said: translate('eventProposal.anotherTime'),
                      name: translate('eventProposal.anotherTimeName', { title }),
                      run: () => {
                          answering.askAnotherTime(title);
                      },
                  },
                  {
                      said: translate('proposal.decline'),
                      name: translate('eventProposal.declineName', { title }),
                      run: () => {
                          answering.answer('declined');
                      },
                  },
              ];

    return (
        <ProposalCard
            citations={evidence.citations}
            controls={controls}
            kind="eventProposal"
            label={translate('eventProposal.label')}
            title={title}
        >
            <ProposalTitle>{title}</ProposalTitle>

            <ProposalFields
                fields={[
                    { name: translate('eventProposal.when'), value: when },
                    ...(lasts === null ? [] : [{ name: translate('eventProposal.duration'), value: lasts }]),
                    ...(thread === null
                        ? []
                        : [{ name: translate('eventProposal.from'), value: <q lang="">{thread.label}</q> }]),
                ]}
            />
        </ProposalCard>
    );
}
