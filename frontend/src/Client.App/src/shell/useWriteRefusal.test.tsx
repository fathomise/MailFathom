// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from './connectionStatus';
import { useWriteRefusal } from './useWriteRefusal';

// The one sentence every control that writes says while the server is out of reach, by the control's own name.

const shared = {
    attempts: null,
    retry: () => undefined,
    detailsOpen: false,
    showDetails: () => undefined,
};

const lost: ConnectionStatus = {
    level: 'disconnected',
    cause: 'unanswered',
    since: 0,
    ...shared,
    disconnectedNow: () => true,
};
const delayed: ConnectionStatus = { level: 'delayed', delays: [], ...shared, disconnectedNow: () => false };

function refusalUnder(status: ConnectionStatus): (control: string) => string | null {
    const { result } = renderHook(() => useWriteRefusal(), {
        wrapper: ({ children }: { readonly children: ReactNode }) => (
            <LocalizationProvider>
                <ConnectionStatusContext value={status}>{children}</ConnectionStatusContext>
            </LocalizationProvider>
        ),
    });

    return result.current;
}

describe('useWriteRefusal', () => {
    it('refuses a write by its name while the server is out of reach', () => {
        expect(refusalUnder(lost)('Save')).toBe('Save - no connection to the MailFathom server.');
    });

    // A server that answers with a delay still takes writes: only the channel or a mail server is behind.
    it('refuses nothing while the server answers, delayed or not', () => {
        expect(refusalUnder(delayed)('Save')).toBeNull();
    });
});
