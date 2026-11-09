// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext } from 'react';

/**
 * Whether the words below are their proven author's own to colour, which the service states about a whole message and
 * a quotation inside it takes back: a signature vouches for what its author wrote and only covers what they quoted.
 *
 * A context for the reason `LinksNotOpened` is one: a run sits several blocks deep and nothing between it and the
 * message reads this.
 */
export const AuthorsPresentation = createContext(false);
