// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext } from 'react';

/**
 * Whether the links in the words below are drawn as words that open nothing, which is how a message marked spam is read:
 * where a link would have gone is still said beside it, and nothing can be pressed to go there.
 *
 * A context rather than a prop, because a link sits several blocks deep in the document tree and the one fact it needs
 * belongs to the whole message rather than to any block between them.
 */
export const LinksNotOpened = createContext(false);
