// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { DeploymentSession } from '@mailfathom/client-backend';
import { spacesOffered } from './capabilities';

// A credential carrying every grant a space is reached under, so what a test below finds missing is missing for the
// reason the test names rather than for a grant.
const everyGrant: DeploymentSession = {
    version: '0.9.0',
    permissions: [
        'mailfathom.mail.read',
        'mailfathom.assistant.use',
        'mailfathom.tasks.read',
        'mailfathom.tasks.write',
        'mailfathom.calendar.read',
        'mailfathom.calendar.write',
        'mailfathom.contacts.read',
    ],
    telemetryLevel: 'none',
    revision: null,
    organization: null,
    organizationName: null,
};

describe('spacesOffered', () => {
    it('offers no space still at an early stage while developer mode is off', () => {
        expect(spacesOffered(everyGrant, false)).toStrictEqual(['agent', 'mail', 'people']);
    });

    it('offers every space the credential may open once developer mode is on', () => {
        expect(spacesOffered(everyGrant, true)).toStrictEqual([
            'discover',
            'agent',
            'mail',
            'tasks',
            'calendar',
            'people',
        ]);
    });

    it('offers a developer no early space the credential may not open', () => {
        expect(spacesOffered({ ...everyGrant, permissions: ['mailfathom.mail.read'] }, true)).toStrictEqual(['mail']);
    });
});
