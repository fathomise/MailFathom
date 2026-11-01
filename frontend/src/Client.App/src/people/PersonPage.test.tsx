// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, type RenderResult } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ReactNode } from 'react';
import type { Colleague, Contact } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import type { Person } from '../contactBook/useContactBook';
import { LocalizationProvider } from '../localization/Localization';
import { ReadingZoneContext } from '../localization/useReadingZone';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { ToastContext } from '../toasts/useToasts';
import { PersonPage } from './PersonPage';
import type { ContactCorrespondenceInForce } from './useContactCorrespondence';
import type { ColleagueStructureInForce } from './useColleagueStructure';

function personCalled(origin: Contact['origin'], id = 'anna', displayName = 'Anna Kowalska'): Contact {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        phoneNumbers: [],
        origin,
        book: origin === 'Collected' ? 'Collected' : 'Own',
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
        photo: null,
        organization: null,
    };
}

const piotr: Colleague = {
    id: 'piotr',
    book: 'Member',
    displayName: 'Piotr Zieliński',
    addresses: ['p.zielinski@nordwind.example', 'piotr@nordwind.example'],
    preferredAddress: 'p.zielinski@nordwind.example',
    phoneNumbers: [{ number: '+48 600 100 200', label: 'Mobile' }],
    jobTitle: 'Head of Delivery',
    organizationName: 'Nordwind',
    timeZone: 'Europe/London',
    workingHours: null,
    photo: null,
    presence: 'Busy',
    lastSeen: null,
    outOfOfficeUntil: null,
    usesPhoneApp: false,
    statusMessage: null,
    workMode: null,
};

const officeHours = {
    days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
    start: 9 * 60,
    end: 17 * 60 + 30,
} as const;

afterEach(() => {
    vi.useRealTimers();
});

const nothingCorrelated: ContactCorrespondenceInForce = {
    correspondence: null,
    reading: false,
    failure: null,
    readAgain: () => undefined,
};

const nothingStructured: ColleagueStructureInForce = {
    structure: null,
    reading: false,
    failure: null,
    readAgain: () => undefined,
};

function composingWhere(offered: boolean, compose = vi.fn()): Composing {
    return { offered, drafts: false, opening: null, compose, close: vi.fn() };
}

function pageOf({
    contact = personCalled('Asserted'),
    organizationName = 'Nordwind',
    writable = true,
    promoting = false,
    recordSaid = null,
    onBack = null,
    composing = composingWhere(true),
    onPromote = vi.fn(),
    onAskAmendment = vi.fn(),
    onAskErasure = vi.fn(),
    readerZone = null,
    structure = nothingStructured,
    onOpenColleague = vi.fn(),
}: {
    contact?: Person;
    readerZone?: string | null;
    organizationName?: string | null;
    writable?: boolean;
    promoting?: boolean;
    recordSaid?: 'people.written' | null;
    onBack?: (() => void) | null;
    composing?: Composing;
    onPromote?: () => void;
    onAskAmendment?: () => void;
    onAskErasure?: () => void;
    structure?: ColleagueStructureInForce;
    onOpenColleague?: (colleague: Colleague) => void;
} = {}): ReactNode {
    return (
        <LocalizationProvider>
            <ToastContext value={{ raise: vi.fn(), raiseOperation: () => () => undefined }}>
                <ReadingZoneContext value={readerZone}>
                    <LinkOpenerContext value={() => Promise.resolve()}>
                        <ComposingContext value={composing}>
                            <PersonPage
                                contact={contact}
                                organizationName={organizationName}
                                correspondence={nothingCorrelated}
                                structure={structure}
                                onOpenColleague={onOpenColleague}
                                writable={writable}
                                promoting={promoting}
                                recordSaid={recordSaid}
                                onBack={onBack}
                                onPromote={onPromote}
                                onAskAmendment={onAskAmendment}
                                onAskErasure={onAskErasure}
                                onOpenThread={vi.fn()}
                                onOpenDocument={vi.fn()}
                            />
                        </ComposingContext>
                    </LinkOpenerContext>
                </ReadingZoneContext>
            </ToastContext>
        </LocalizationProvider>
    );
}

function drawPage(page: Parameters<typeof pageOf>[0] = {}): RenderResult {
    return render(pageOf(page));
}

// A contact the reader's organization keeps in its own book.
const keptByNordwind: Person = {
    ...personCalled('Asserted'),
    book: 'Organization',
    organization: '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f',
};

describe('PersonPage', () => {
    it('draws who the person is and how to reach them', () => {
        drawPage();

        expect(screen.getByRole('heading', { name: 'Anna Kowalska' })).toBeDefined();
        expect(screen.getByRole('heading', { name: 'Contact details' })).toBeDefined();
        expect(screen.getAllByText('anna@contoso.example')).toHaveLength(2);
    });

    // Only a member of the organization has a place in it, so a contact of any book draws no structure.
    it('draws no organization for somebody who is not a colleague', () => {
        drawPage();

        expect(screen.queryByRole('region', { name: 'Organization' })).toBeNull();
    });

    // A record a mailbox collected is nobody's to amend, and the page says so on the record rather than leaving
    // somebody to discover it from a refusal.
    // Opening somebody is a view change, and this page is not remounted between two of them — so nothing would move
    // focus by itself, and at the width where the book behind is made inert there is not even a stale element left
    // holding it. Both moves are asserted: the one that opens the first person, and the one that swaps a second in.
    it('takes focus into the person opened, and again when a second person is opened in the same pane', () => {
        const { rerender } = drawPage();

        expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Anna Kowalska' }));

        rerender(pageOf({ contact: personCalled('Asserted', 'bartek', 'Bartek Nowak') }));

        expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Bartek Nowak' }));
    });

    // The other half of it: a render that is not a new person leaves focus wherever the reader put it, or every
    // answer arriving about the person already open would pull focus back out of whatever they had reached.
    it('leaves focus alone where the person has not changed', () => {
        const { rerender } = drawPage();

        screen.getByRole('button', { name: 'Delete contact' }).focus();
        rerender(pageOf({ promoting: true }));

        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Delete contact' }));
    });

    // A member reads the organization's book and changes nothing in it, so the page names whose book the contact is in
    // and offers neither act that writes to it — however much the credential could write to the reader's own.
    it('names the organization that keeps a contact and offers no edit and no deletion of it', () => {
        drawPage({ contact: keptByNordwind });

        expect(screen.getByText('Nordwind')).toBeDefined();
        expect(screen.getByText('Kept by Nordwind - read-only')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Delete contact' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Edit contact' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Write' })).toBeDefined();
    });

    // The session may answer no name — the organization deleted since — and the page then says whose book it is in
    // words rather than by the identifier the contact carries.
    it('names the organization in words where the session answered no name for it', () => {
        drawPage({
            contact: keptByNordwind,
            organizationName: null,
        });

        expect(screen.getByText('Organization')).toBeDefined();
        expect(screen.getByText('Kept by your organization - read-only')).toBeDefined();
        expect(screen.queryByText(/0197f0c4/)).toBeNull();
    });

    it('says a collected record is not the reader’s to amend, and offers taking it on', () => {
        drawPage({ contact: personCalled('Collected') });

        expect(screen.getByText('Read-only - editable once added to your contacts')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Add to my contacts' })).toBeDefined();
    });

    it('offers taking on nothing for a record the reader already asserted', () => {
        drawPage();

        expect(screen.queryByRole('button', { name: 'Add to my contacts' })).toBeNull();
    });

    it('offers correcting a record the reader asserted, through the question that holds it', () => {
        const onAskAmendment = vi.fn();
        drawPage({ onAskAmendment });

        fireEvent.click(screen.getByRole('button', { name: 'Edit contact' }));

        expect(onAskAmendment).toHaveBeenCalledTimes(1);
    });

    it('offers no correction of a record a mailbox collected', () => {
        drawPage({ contact: personCalled('Collected') });

        expect(screen.queryByRole('button', { name: 'Edit contact' })).toBeNull();
    });

    it('says a promotion is in flight rather than leaving the control silent', () => {
        drawPage({ contact: personCalled('Collected'), promoting: true });

        expect(screen.getByRole('button', { name: 'Adding…' })).toBeDefined();
    });

    it('offers neither act on the record where the credential may not change the book', () => {
        drawPage({ contact: personCalled('Collected'), writable: false });

        expect(screen.queryByRole('button', { name: 'Add to my contacts' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Delete contact' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Edit contact' })).toBeNull();
    });

    it('offers deleting a record the reader asserted, and only removing one a mailbox collected', () => {
        drawPage({ contact: personCalled('Collected') });

        expect(screen.queryByRole('button', { name: 'Delete contact' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Remove from collected' })).toBeDefined();
    });

    it('raises the question an erasure stands behind rather than erasing on the press', () => {
        const onAskErasure = vi.fn();
        drawPage({ onAskErasure });

        fireEvent.click(screen.getByRole('button', { name: 'Delete contact' }));

        expect(onAskErasure).toHaveBeenCalledTimes(1);
    });

    it('opens a message addressed to the person whose page this is', () => {
        const compose = vi.fn();
        drawPage({ composing: composingWhere(true, compose) });

        fireEvent.click(screen.getByRole('button', { name: 'Write' }));

        expect(compose).toHaveBeenCalledWith({ kind: 'new', to: ['anna@contoso.example'] });
    });

    // The way back belongs to the page rather than to the frame, and only where the list and the person cannot stand
    // together.
    it('draws the way back only where the list is behind this page rather than beside it', () => {
        drawPage();

        expect(screen.queryByRole('button', { name: 'Back to the address book' })).toBeNull();

        const onBack = vi.fn();
        drawPage({ onBack });

        fireEvent.click(screen.getByRole('button', { name: 'Back to the address book' }));

        expect(onBack).toHaveBeenCalledTimes(1);
    });

    it('says what the last write to this record did, beside the record it did it to', () => {
        drawPage({ recordSaid: 'people.written' });

        expect(screen.getByRole('status').textContent).toBe('Contact added.');
    });

    describe('a colleague', () => {
        it('names them a colleague, says what they do and where, and whose directory they come from', () => {
            drawPage({ contact: piotr });

            expect(screen.getByText('Colleague')).toBeDefined();
            expect(screen.getByText('Head of Delivery · Nordwind')).toBeDefined();
            expect(screen.getByText("From Nordwind's directory - read-only")).toBeDefined();
        });

        // Read-only in the client as in the service: nothing on the page would change the member.
        it('draws no control that would change them, whatever the credential may do', () => {
            drawPage({ contact: piotr, writable: true });

            expect(screen.queryByRole('button', { name: 'Edit contact' })).toBeNull();
            expect(screen.queryByRole('button', { name: 'Delete contact' })).toBeNull();
            expect(screen.queryByRole('button', { name: 'Remove from collected' })).toBeNull();
            expect(screen.queryByRole('button', { name: 'Add to my contacts' })).toBeNull();
        });

        it('draws where they sit in the organization, and opens a member of it in their place', () => {
            const zofia: Colleague = { ...piotr, id: 'zofia', displayName: 'Zofia Nowak', jobTitle: null };
            const onOpenColleague = vi.fn();
            drawPage({
                contact: piotr,
                structure: {
                    ...nothingStructured,
                    structure: { managers: [zofia], directReports: [], readerId: 'anna' },
                },
                onOpenColleague,
            });

            expect(screen.getByRole('region', { name: 'Organization' })).toBeDefined();

            fireEvent.click(screen.getByRole('button', { name: /Zofia Nowak/u }));

            expect(onOpenColleague).toHaveBeenCalledWith(zofia);
        });

        it('offers the two quick acts: copying the address, and an email addressed to them', () => {
            const compose = vi.fn();
            drawPage({ contact: piotr, composing: composingWhere(true, compose) });

            expect(screen.getByRole('button', { name: 'Copy address' })).toBeDefined();

            fireEvent.click(screen.getByRole('button', { name: 'Write an email' }));

            expect(compose).toHaveBeenCalledWith({ kind: 'new', to: ['p.zielinski@nordwind.example'] });
        });

        it('offers neither act to a member who holds no address', () => {
            drawPage({ contact: { ...piotr, addresses: [], preferredAddress: null } });

            expect(screen.queryByRole('button', { name: 'Copy address' })).toBeNull();
            expect(screen.queryByRole('button', { name: 'Write an email' })).toBeNull();
        });

        it('adds the organization and the title to the contact details', () => {
            drawPage({ contact: piotr });

            const details = screen.getByRole('heading', { name: 'Contact details' }).parentElement;

            expect(details?.textContent).toContain('OrganizationNordwind');
            expect(details?.textContent).toContain('TitleHead of Delivery');
            expect(details?.textContent).toContain('+48 600 100 200');
        });

        it('leaves the title out of the details and the line under the name where none is stated', () => {
            drawPage({ contact: { ...piotr, jobTitle: null } });

            expect(screen.queryByText('Title')).toBeNull();
            expect(screen.queryByText(/Head of Delivery/u)).toBeNull();
        });

        it("says their local time and how far it stands from the reader's", () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-31T08:41:00Z'));
            drawPage({ contact: piotr, readerZone: 'Europe/Warsaw' });

            expect(screen.getByRole('heading', { name: 'Time and working hours' })).toBeDefined();
            expect(screen.getByText('8:41 AM, 1h behind you')).toBeDefined();
        });

        it('says their presence in words beside the dot on their avatar', () => {
            drawPage({ contact: piotr });

            expect(screen.getByText('Busy')).toBeDefined();
            expect(screen.queryByText(/Last seen/u)).toBeNull();
        });

        it('says when an offline colleague was last seen, against the reader’s own day', () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-31T09:00:00Z'));
            drawPage({
                contact: { ...piotr, presence: 'Offline', lastSeen: '2026-10-30T17:20:00Z' },
                readerZone: 'Europe/Warsaw',
            });

            expect(screen.getByText('Offline')).toBeDefined();
            expect(screen.getByText('Last seen yesterday 6:20 PM')).toBeDefined();
        });

        it('says a colleague is out of office until the last day their automatic reply runs to, in the reader’s zone', () => {
            drawPage({
                contact: { ...piotr, outOfOfficeUntil: '2026-11-05T00:00:00+01:00' },
                readerZone: 'Europe/Warsaw',
            });

            expect(screen.getByText('Out of office until 11/04')).toBeDefined();
        });

        it('says in words that an offline colleague uses the phone app', () => {
            drawPage({ contact: { ...piotr, presence: 'Offline', usesPhoneApp: true } });

            expect(screen.getByText('Uses the phone app')).toBeDefined();
        });

        it('says nothing of the phone app for a colleague who does not use it', () => {
            drawPage({ contact: piotr });

            expect(screen.queryByText('Uses the phone app')).toBeNull();
        });

        it('says where a colleague works from, and what they say they are doing with when it clears', () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-31T09:00:00Z'));
            drawPage({
                contact: {
                    ...piotr,
                    workMode: { mode: 'Remote', endsAt: '2026-10-31T23:00:00Z' },
                    statusMessage: { text: 'In workshops', clears: 'today', clearsAt: '2026-10-31T23:00:00Z' },
                },
                readerZone: 'Europe/Warsaw',
            });

            expect(screen.getByText('Remote today')).toBeDefined();
            expect(screen.getByText('In workshops')).toBeDefined();
            expect(screen.getByText('· clears tonight')).toBeDefined();
        });

        it('says no clearing for a message that never clears, and nothing for a colleague who stated neither', () => {
            const { rerender } = drawPage({
                contact: { ...piotr, statusMessage: { text: 'Ask me about invoices', clears: null, clearsAt: null } },
            });

            expect(screen.getByText('Ask me about invoices')).toBeDefined();
            expect(screen.queryByText(/clears/u)).toBeNull();

            rerender(pageOf({ contact: piotr }));

            expect(screen.queryByText('Ask me about invoices')).toBeNull();
            expect(screen.queryByText(/today|until changed/u)).toBeNull();
        });

        it('says nothing about being away for a colleague whose automatic reply is not running', () => {
            drawPage({ contact: piotr });

            expect(screen.queryByText(/Out of office/u)).toBeNull();
        });

        it('says nothing about when a member was last seen where the deployment withholds it, as for one hidden', () => {
            drawPage({ contact: { ...piotr, presence: 'Offline', lastSeen: null } });

            expect(screen.getByText('Offline')).toBeDefined();
            expect(screen.queryByText(/Last seen/u)).toBeNull();
        });

        it('draws no time at all for a member who states neither a zone nor working hours', () => {
            drawPage({ contact: { ...piotr, timeZone: null } });

            expect(screen.queryByRole('heading', { name: 'Time and working hours' })).toBeNull();
        });

        it("says their working hours on their clock and on the reader's, and that it is outside them", () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-28T08:41:00Z'));
            drawPage({ contact: { ...piotr, workingHours: officeHours }, readerZone: 'Europe/Warsaw' });

            expect(screen.getByText('Mon–Fri 9:00 AM–5:30 PM')).toBeDefined();
            expect(screen.getByText('(10:00 AM–6:30 PM your time)')).toBeDefined();
            expect(screen.getByText('Outside working hours')).toBeDefined();
        });

        it("names the reader's days beside hours the reader's clock moves onto the evening before", () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-28T08:41:00Z'));
            drawPage({
                contact: { ...piotr, timeZone: 'Australia/Sydney', workingHours: officeHours },
                readerZone: 'America/Los_Angeles',
            });

            expect(screen.getByText('(Sun–Thu 3:00 PM–11:30 PM your time)')).toBeDefined();
        });

        it('says which hours a zone this device does not know leaves unconverted, and marks nothing', () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-28T08:41:00Z'));
            drawPage({
                contact: { ...piotr, timeZone: 'Mars/Olympus_Mons', workingHours: officeHours },
                readerZone: 'Europe/Warsaw',
            });

            expect(
                screen.getByText(
                    "The time zone Piotr states is not one this device knows, so these hours are Piotr's own and are not converted to yours.",
                ),
            ).toBeDefined();
            expect(screen.queryByText(/your time/u)).toBeNull();
            expect(screen.queryByText('Outside working hours')).toBeNull();
        });

        it('marks nothing while it is inside their working hours', () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-28T10:00:00Z'));
            drawPage({ contact: { ...piotr, workingHours: officeHours }, readerZone: 'Europe/Warsaw' });

            expect(screen.getByText('Mon–Fri 9:00 AM–5:30 PM')).toBeDefined();
            expect(screen.queryByText('Outside working hours')).toBeNull();
        });

        it('shows the hours of a member who states no zone as stated, says they are not converted, and marks nothing', () => {
            vi.useFakeTimers({ toFake: ['Date'] });
            vi.setSystemTime(new Date('2026-10-31T08:41:00Z'));
            drawPage({ contact: { ...piotr, timeZone: null, workingHours: officeHours }, readerZone: 'Europe/Warsaw' });

            expect(screen.getByRole('heading', { name: 'Time and working hours' })).toBeDefined();
            expect(screen.getByText('Mon–Fri 9:00 AM–5:30 PM')).toBeDefined();
            expect(
                screen.getByText(
                    "No time zone is stated, so these hours are Piotr's own and are not converted to yours.",
                ),
            ).toBeDefined();
            expect(screen.queryByText(/your time/u)).toBeNull();
            expect(screen.queryByText(/behind you/u)).toBeNull();
            expect(screen.queryByText('Outside working hours')).toBeNull();
        });

        it('draws neither the relationship reading nor the correspondence, which the directory serves none of', () => {
            drawPage({ contact: piotr });

            expect(screen.queryByText('Relationship state')).toBeNull();
            expect(screen.queryByText('Shared threads')).toBeNull();
        });
    });
});
