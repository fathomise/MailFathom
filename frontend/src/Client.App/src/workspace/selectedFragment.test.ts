// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { fragmentBeingRead } from './selectedFragment';
import { emptyWorkspace } from './useWorkspace';

const fragment = { messageId: 'message-1', text: 'by the end of the month' };

describe('fragmentBeingRead', () => {
    it('is the passage while the message it was taken from is the one being read', () => {
        expect(fragmentBeingRead({ ...emptyWorkspace, selection: 'message-1', fragment })).toEqual(fragment);
    });

    it('is the passage while a correspondence is open', () => {
        expect(
            fragmentBeingRead({
                ...emptyWorkspace,
                conversation: { threadId: 'thread-1', openAt: null },
                fragment,
            }),
        ).toEqual(fragment);
    });

    it('is nothing once another message is being read', () => {
        expect(fragmentBeingRead({ ...emptyWorkspace, selection: 'message-2', fragment })).toBeNull();
    });
});
