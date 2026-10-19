// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext } from 'react';

/** What a surface rendered inside `Departing` reads to learn it is leaving. Outside one, nothing ever is. */
export const DepartureContext = createContext(false);
