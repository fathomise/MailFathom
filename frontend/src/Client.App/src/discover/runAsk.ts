// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { DiscoveryRunAsk } from '@mailfathom/client-backend';
import type { AskedQuestion } from '../workspace/askedQuestions';

// What a question Discover recorded becomes on the wire.
//
// **Discover asks about all of the reader's mail.** Nothing the Mail space has open, picked out, or highlighted narrows
// it, and the field offers no scope to choose: a question about one correspondence is asked in that correspondence's
// own field, and a narrower follow-up is the Agent's.
//
// **The scope is stated rather than defaulted.** Every field the deployment requires is sent, and what asks about
// everything is the empty list rather than an absent one: a run asked with a field left out would read whatever the
// service defaults to, which is mail the person never chose.

/** The run one recorded question would be asked as. */
export function runAskedFor(asked: AskedQuestion): DiscoveryRunAsk {
    return { question: asked.question, accounts: [], folders: [], thread: null, emails: [] };
}
