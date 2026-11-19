// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import type { PersonalTask } from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { wordTaskDay } from '../tasks/taskWording';

// The tasks proposed to the reader, from their mail and from the Agent, drawn beside the views as the design draws
// them: the same proposals as Tasks › *Proposed*, so one answered in either place is gone from both. Each says its
// day, where it came from, and the two answers — *Accept*, which makes it a task the calendar then draws on its day,
// and *Dismiss*, which erases it at once, nobody having taken it on.
//
// ***Plan my day*** stands beside this heading rather than under a briefing, because the briefing bar the design hangs it from has no
// route behind it on this surface: it is the way to Tasks › *Today* with the day being laid out, and is drawn only
// where the frame hands over a way there.

export function ProposedTasks({
    proposals,
    onAccept,
    onDismiss,
    onPlanDay,
}: {
    /** What is proposed and nobody has answered yet, less anything just answered here. */
    readonly proposals: readonly PersonalTask[];

    readonly onAccept: (task: PersonalTask) => void;
    readonly onDismiss: (task: PersonalTask) => void;

    /** Opens Tasks on *Today* with the day being laid out, or `undefined` where this credential cannot lay one out. */
    readonly onPlanDay: (() => void) | undefined;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());
    const refused = useWriteRefusal()(translate('tasks.accept'));

    return (
        <section aria-label={translate('calendar.tasksHeading')} className="flex flex-col gap-2.75">
            <div className="flex items-center gap-2.25">
                <h3 className="flex items-center gap-2.25 text-xs font-semibold text-muted">
                    {translate('calendar.tasksHeading')}
                    <span className="rounded-sm bg-accent px-1.5 py-0.5 text-xs text-on-accent">
                        {translate('calendar.authored')}
                    </span>
                </h3>
                {onPlanDay === undefined ? null : (
                    <Control
                        label={translate('calendar.planMyDay')}
                        shape="link"
                        className="ms-auto text-xs"
                        onPress={onPlanDay}
                    />
                )}
            </div>

            <p className="text-sm text-muted text-pretty">{translate('calendar.tasksExplained')}</p>

            {proposals.length === 0 || refused === null ? null : (
                <p role="status" className="text-sm text-muted text-pretty">
                    {refused}
                </p>
            )}

            {proposals.length === 0 ? (
                <p className="text-sm text-faint text-pretty">{translate('calendar.noProposalsLeft')}</p>
            ) : (
                <ul className="flex flex-col gap-2.25">
                    {proposals.map((task) => {
                        const fromAgent = task.sourceMessageId === null;
                        const source = translate(fromAgent ? 'tasks.fromAgent' : 'tasks.fromMail');

                        return (
                            <li
                                key={task.id}
                                className="flex flex-col gap-1.75 rounded-md border border-dashed border-accent-line bg-panel px-3 py-2.75"
                            >
                                <p className="flex items-baseline gap-2.25">
                                    <span className="min-w-0 flex-1 text-sm text-pretty">{task.title}</span>
                                    <span className="shrink-0 text-xs font-semibold whitespace-nowrap text-accent-deep">
                                        {task.dueOn === null
                                            ? translate('tasks.chipNoDay')
                                            : wordTaskDay(task.dueOn, locale, now, timeZone)}
                                    </span>
                                </p>

                                <p className="flex items-center gap-1 text-xs text-muted">
                                    <Icon name={fromAgent ? 'auto_awesome' : 'mail'} className="size-icon-xs" />
                                    {task.anchor === null
                                        ? source
                                        : translate('calendar.proposedTaskFrom', { source, label: task.anchor.label })}
                                </p>

                                <div className="flex flex-wrap gap-1.75">
                                    <Control
                                        label={translate('tasks.accept')}
                                        shape="primary"
                                        {...(refused === null ? {} : { held: true, hint: refused })}
                                        onPress={() => {
                                            onAccept(task);
                                        }}
                                    />

                                    <SecondaryButton
                                        label={translate('calendar.dismissProposal')}
                                        shape="compact"
                                        held={refused !== null}
                                        onActivate={() => {
                                            onDismiss(task);
                                        }}
                                    />
                                </div>
                            </li>
                        );
                    })}
                </ul>
            )}
        </section>
    );
}
