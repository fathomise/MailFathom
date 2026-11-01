// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { clientKind } from './clientKind';

// The shell is the seam, so a test supplies one. What is proven is that only a shell clearly naming the phone is the
// phone: anything else it says, or its saying nothing, must never mark somebody as using the phone app.

function shellAnswering(answer: unknown): void {
    Object.defineProperty(window, '__TAURI__', {
        configurable: true,
        value: { core: { invoke: () => Promise.resolve(answer) } },
    });
}

function shellRefusing(): void {
    Object.defineProperty(window, '__TAURI__', {
        configurable: true,
        value: { core: { invoke: () => Promise.reject(new Error('no such command')) } },
    });
}

describe('clientKind', () => {
    afterEach(() => {
        Reflect.deleteProperty(window, '__TAURI__');
    });

    it('is the web head where no shell is hosting the page', async () => {
        await expect(clientKind()).resolves.toBe('web');
    });

    it('is the phone where the shell names itself the phone', async () => {
        shellAnswering('android');

        await expect(clientKind()).resolves.toBe('android');
    });

    it('is the desktop where the shell names itself the desktop', async () => {
        shellAnswering('desktop');

        await expect(clientKind()).resolves.toBe('desktop');
    });

    it.each([
        ['an answer this client does not know', 'ios'],
        ['an answer that is not a name at all', 7],
    ])('is the desktop rather than the phone for %s', async (_, answer) => {
        shellAnswering(answer);

        await expect(clientKind()).resolves.toBe('desktop');
    });

    it('is the desktop rather than the phone where the shell would not answer', async () => {
        shellRefusing();

        await expect(clientKind()).resolves.toBe('desktop');
    });
});
