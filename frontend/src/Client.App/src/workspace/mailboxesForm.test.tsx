// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { useMailboxesForm, type MailboxesForm } from './mailboxesForm';
import { WorkspaceProvider } from './Workspace';
import { useWorkspace } from './useWorkspace';

// The width and the pointer are what decide the form, and jsdom answers neither, so each query is answered here: a
// width out of the number the query names, and the pointer as the test says.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

function formAt(pixels: number, finger: boolean, folded: boolean | null = null): MailboxesForm {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => {
            const named = /([\d.]+)rem/.exec(query)?.[1];

            return {
                media: query,
                matches: query.includes('pointer: coarse')
                    ? finger
                    : named !== undefined && pixels >= Number(named) * 16,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            };
        },
    });

    const { result } = renderHook(
        () => {
            const { revise } = useWorkspace();

            return { form: useMailboxesForm(), revise };
        },
        { wrapper: WorkspaceProvider },
    );

    act(() => {
        result.current.revise({ mailboxesFolded: folded });
    });

    return result.current.form;
}

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }

    window.sessionStorage.clear();
    window.localStorage.clear();
});

describe('useMailboxesForm', () => {
    it('keeps the mailboxes in a drawer on the phone, under a mouse as under a finger', () => {
        expect(formAt(390, false)).toBe('drawer');
        expect(formAt(390, true)).toBe('drawer');
    });

    it('keeps them in a drawer wherever one pane stands, under a mouse as under a finger', () => {
        expect(formAt(760, false)).toBe('drawer');
        expect(formAt(760, true)).toBe('drawer');
    });

    it('folds them into a drawer below the desktop composition under a finger', () => {
        expect(formAt(1024, true)).toBe('drawer');
    });

    it('folds them to the strip below the desktop composition under a mouse', () => {
        expect(formAt(1024, false)).toBe('strip');
    });

    it('stands them open as a column in the desktop composition, under either pointer', () => {
        expect(formAt(1440, false)).toBe('column');
        expect(formAt(1440, true)).toBe('column');
    });

    it('keeps what somebody chose over what the width would choose, wherever the column stands', () => {
        expect(formAt(1024, false, false)).toBe('column');
        expect(formAt(1440, false, true)).toBe('strip');
    });

    it('keeps the drawer whatever was chosen, the drawer having no fold of its own', () => {
        expect(formAt(1024, true, true)).toBe('drawer');
    });
});
