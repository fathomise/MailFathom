// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { PersonalTask } from '@mailfathom/client-backend';

// The order the Tasks list is held in: the two halves the deployment walks put back into one. Which heading of which
// view a task then stands under is `taskViews.ts`'s.

/**
 * Two halves of the list read as one, soonest due first and the undated last.
 *
 * The surface serves what somebody committed to and what mail proposed as two walks, and the design draws one list
 * with a mark on the proposals — so the two are put back into one order here. Each walk already arrives in this order,
 * so what this does is interleave them, and a sort that keeps equal days in the order they were given is what makes a
 * proposal and a commitment due on one day draw in a stable place rather than swapping between renders.
 *
 * @param committed What the person has committed to, as the deployment walked it.
 * @param proposed What mail proposed and nobody has accepted, as the deployment walked it.
 * @returns One list in the order a reader is shown it.
 */
export function inDueOrder(
    committed: readonly PersonalTask[],
    proposed: readonly PersonalTask[],
): readonly PersonalTask[] {
    return [...committed, ...proposed].sort((left, right) => {
        if (left.dueOn === right.dueOn) {
            return 0;
        }

        // A task nobody has dated is last rather than first, which is where the deployment walks it and where a
        // reader looks for work nobody has promised a day for.
        if (left.dueOn === null) {
            return 1;
        }

        return right.dueOn === null ? -1 : left.dueOn.localeCompare(right.dueOn);
    });
}
