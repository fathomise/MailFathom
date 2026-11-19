// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import { useMailTasks } from './useMailTasks';

// The mark a list row draws after its subject where the thread it stands for carries an open task; the thread's own
// chips say which. A mark rather than a control, named in words for a screen reader as part of the row's own name, and
// its name bubble answers the mouse alone, since a tap inside a row opens the row.

export function TaskMark({ threadId }: { readonly threadId: string | null }) {
    const tasks = useMailTasks();

    return threadId !== null && tasks?.threadsWithTasks.has(threadId) === true ? <Marked /> : null;
}

function Marked() {
    const { translate } = useLocalization();
    const name = translate('tasks.hasOpenTask');
    const { anchor, tip } = useTip({ says: name, tapReveals: false });

    return (
        <>
            <span {...anchor} className="flex shrink-0 items-center text-accent-deep">
                <Icon name="add_task" className="size-icon-sm" />
                <span className="sr-only">{name}</span>
            </span>
            {tip}
        </>
    );
}
