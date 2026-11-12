// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailAccount, MailTrustSource } from '@mailfathom/client-backend';
import { externalMarkingOf, externalMarkOf, organizationSignatureStepOf } from './externalMark';

const work: MailAccount = {
    id: 'work',
    displayName: 'Nordwind · work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: '2026-11-12T09:00:00+00:00',
    behind: false,
    filesJunk: false,
    shared: false,
    synchronizationStateSince: null,
    behindSince: null,
    waitingChangeCount: 0,
    oldestWaitingChangeCommittedAt: null,
    marksExternalMail: true,
};
const personal: MailAccount = { ...work, id: 'personal', displayName: 'Personal', marksExternalMail: false };

const marking = externalMarkingOf([work, personal], 'Nordwind');

function sender(trustSource: MailTrustSource | null, trustMember: string | null = null) {
    return { trustSource, trustMember, trustOrganization: trustSource === 'OrganizationMember' ? 'Nordwind' : null };
}

describe('externalMarkOf', () => {
    it('marks a message on a marking account that the organization did not sign, naming the organization', () => {
        expect(externalMarkOf(marking, 'work', sender('PersonalContact'))).toStrictEqual({
            short: { key: 'senderMark.external.short', values: { organization: 'Nordwind' } },
            name: { key: 'senderMark.external.name', values: { organization: 'Nordwind' } },
            why: { key: 'senderMark.external.why', values: { organization: 'Nordwind' } },
        });
    });

    it('marks a message nothing recognized as it marks a contact’s, since the mark asks about the signature alone', () => {
        expect(externalMarkOf(marking, 'work', sender(null))).toStrictEqual(
            externalMarkOf(marking, 'work', sender('OrganizationContact')),
        );
    });

    it('says how a colleague’s message comes to be marked where the member directory names the address', () => {
        const mark = externalMarkOf(marking, 'work', sender('OrganizationDirectory', 'Marta Nowak'));

        expect([mark?.name, mark?.why]).toStrictEqual([
            { key: 'senderMark.external.nameColleague', values: { organization: 'Nordwind', member: 'Marta Nowak' } },
            { key: 'senderMark.external.whyColleague', values: { organization: 'Nordwind', member: 'Marta Nowak' } },
        ]);
    });

    it('draws no chip on a message the organization’s signature proves', () => {
        expect(externalMarkOf(marking, 'work', sender('OrganizationMember'))).toBeNull();
    });

    it('draws no chip on an account whose organization did not turn the mark on', () => {
        expect(externalMarkOf(marking, 'personal', sender(null))).toBeNull();
    });

    it('draws no chip where the session names no organization, since the chip never stands without its explanation', () => {
        expect(externalMarkOf(externalMarkingOf([work], null), 'work', sender(null))).toBeNull();
    });
});

describe('organizationSignatureStepOf', () => {
    it('passes a message the organization signed, naming the organization that signed it', () => {
        expect(organizationSignatureStepOf(marking, 'work', sender('OrganizationMember'))).toStrictEqual({
            name: 'senderMark.step.organizationSignature',
            result: 'Passed',
            detail: { key: 'senderMark.trust.organizationSigned', values: { organization: 'Nordwind' } },
            facts: null,
        });
    });

    it('fails a message the organization did not sign, with the colleague’s sentence where a colleague is named', () => {
        expect(
            [sender(null), sender('OrganizationDirectory', 'Marta Nowak')].map(
                (each) => organizationSignatureStepOf(marking, 'work', each)?.detail,
            ),
        ).toStrictEqual([
            { key: 'senderMark.trust.organizationUnsigned', values: { organization: 'Nordwind' } },
            {
                key: 'senderMark.trust.organizationUnsignedColleague',
                values: { organization: 'Nordwind', member: 'Marta Nowak' },
            },
        ]);
    });

    it('names no organization for a member whose organization was since deleted, rather than the reader’s own', () => {
        expect(
            organizationSignatureStepOf(marking, 'work', {
                trustSource: 'OrganizationMember',
                trustMember: null,
                trustOrganization: null,
            })?.detail,
        ).toStrictEqual({ key: 'senderMark.trust.organizationSignedDeleted' });
    });

    it('adds no step on an account that does not mark, so the group stands as it did', () => {
        expect(organizationSignatureStepOf(marking, 'personal', sender('OrganizationMember'))).toBeNull();
    });
});
