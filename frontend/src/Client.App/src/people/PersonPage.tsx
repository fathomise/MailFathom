// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef } from 'react';
import type { Colleague } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { useContactPhoto } from '../contactBook/contactPhotos';
import { OrganizationChip } from '../contactBook/OrganizationChip';
import { useAddressCopy } from '../contactBook/useAddressCopy';
import type { Person } from '../contactBook/useContactBook';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SenderAvatar } from '../controls/SenderAvatar';
import type { MessageKey } from '../localization/en';
import { wordAbsenceEnd, wordLastSeen, wordMessageClearing, type MessageClearing } from '../localization/instants';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useCurrentMinute } from '../notifications/notificationAge';
import { PhoneAppBadge } from '../presence/PhoneAppBadge';
import { PresenceDot } from '../presence/PresenceDot';
import { presenceSaid, workModeSaid } from '../presence/presenceWords';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { ColleagueTime } from './ColleagueTime';
import { ContactDetails } from './ContactDetails';
import { OrganizationCard } from './OrganizationCard';
import { PersonCorrespondence } from './PersonCorrespondence';
import { RelationshipReading } from './RelationshipReading';
import type { ColleagueStructureInForce } from './useColleagueStructure';
import type { ContactCorrespondenceInForce } from './useContactCorrespondence';

// One person as the design draws them: who they are and how to reach them, the acts that belong to the record
// itself, and then what MailFathom and the mailbox have to say about them.
//
// **What the record admits is read off its book rather than from a control being pressed.** A contact one of this
// user's mailboxes collected is theirs to take on or to erase and nobody's to amend, and the page says so on the record
// rather than leaving somebody to discover it from a refusal. A colleague and a contact the organization keeps are
// nobody's to change here at all, so neither page draws a control that would change them, and each says whose they
// are under `lock` instead; what a colleague's page offers is the two quick acts, copying their address and writing to
// them, the time where they are, and where they sit in the organization. What MailFathom reads out of mail about a person is a book contact's alone — the
// directory serves no correspondence for a member — so a colleague's page draws neither the reading nor the threads. Taking them out is offered either way, because somebody
// asking to be taken out of a book is not answered with which book they happen to be in — but it is said as what it
// is: a contact somebody wrote down is deleted, and one their mail collected is removed from the collected book, which
// gathers them again the next time they write.
//
// **The way back is part of the page rather than of the frame**, and it is drawn only where the list and the person
// cannot stand together: at that width the two are one pane the reader moves between, so the control that returns is on
// the thing they moved to.
//
// **Opening somebody is a view change, so focus comes with it.** This page is not remounted between two people — the
// record and the correlation change underneath it — so nothing would move focus by itself, and at the width where the
// list is made inert behind this page there is not even a stale element left to hold it. The shape is the one
// `readingPane/ReadingPane.tsx` uses for the same thing: a ref holding who focus was last placed on, which survives
// StrictMode's second invocation where a flag would not. The element it is put on is this screen's own rather than
// that one's — a person's page is a region of the People screen where a message is a document in its own right, so
// this is a `section` where the reading pane is an `article`.

// When a colleague's status message clears, as the design words it after the message.
function clearingSaid(clearing: MessageClearing, translate: Translate): string {
    switch (clearing.on) {
        case 'today':
            return translate('presence.clearsAt', { time: clearing.time });
        case 'tonight':
            return translate('presence.clearsTonight');
        case 'later':
            return translate('presence.clearsOn', { day: clearing.day });
    }
}

// The line under a colleague's name: their title and their organization, each where it is stated. The design adds the
// department between the two, which the directory does not carry.
function roleOf(person: Person): string {
    return person.book === 'Member' ? [person.jobTitle, person.organizationName].filter(Boolean).join(' · ') : '';
}

// Whose a record is that this reader may not change, said under `lock` on the page — the reader's organization by the
// name the session gave it, or as theirs where it gave none.
function readOnlySaid(
    person: Person,
    readersOrganization: string,
): { readonly key: MessageKey; readonly organization?: string } {
    switch (person.book) {
        case 'Member':
            return { key: 'people.colleagueReadOnly', organization: person.organizationName };
        case 'Organization':
            return { key: 'people.organizationReadOnly', organization: readersOrganization };
        default:
            return { key: 'people.collectedReadOnly' };
    }
}

export function PersonPage({
    contact,
    organizationName,
    correspondence,
    structure,
    writable,
    promoting,
    recordSaid,
    onBack,
    onPromote,
    onAskAmendment,
    onAskErasure,
    onOpenColleague,
    onOpenThread,
    onOpenDocument,
}: {
    readonly contact: Person;

    /** The name the reader's organization is shown by, or `null` where the session answered none. */
    readonly organizationName: string | null;

    readonly correspondence: ContactCorrespondenceInForce;

    /** Where a colleague sits in the organization, which only a colleague's page draws. */
    readonly structure: ColleagueStructureInForce;

    /** Whether this credential may change the book at all, which is what the two acts on the record are behind. */
    readonly writable: boolean;

    /** Whether a promotion asked for from this page has not answered yet. */
    readonly promoting: boolean;

    /** What the last write to this record said — a promotion or a correction — or `null` where none was asked for. */
    readonly recordSaid: MessageKey | null;

    /** Returns to the list, or `null` where the list is standing beside this page rather than behind it. */
    readonly onBack: (() => void) | null;

    readonly onPromote: () => void;
    readonly onAskAmendment: () => void;
    readonly onAskErasure: () => void;

    /** Opens another member of the organization in this one's place. */
    readonly onOpenColleague: (colleague: Colleague) => void;

    readonly onOpenThread: (thread: {
        readonly threadId: string;
        readonly messageId: string;
        readonly subject: string | null;
    }) => void;

    readonly onOpenDocument: (document: { readonly messageId: string; readonly position: number }) => void;
}) {
    const { locale, translate } = useLocalization();
    const readingZone = useReadingZone();
    const now = useCurrentMinute();
    const picture = useContactPhoto(contact);
    const composing = useComposing();
    const copy = useAddressCopy();
    const collected = contact.book === 'Collected';
    const member = contact.book === 'Member';
    const own = contact.book === 'Own';
    const address = contact.preferredAddress;
    const role = roleOf(contact);
    const readOnly = readOnlySaid(contact, organizationName ?? translate('people.yourOrganization'));

    // When an offline colleague was last seen, which the deployment states only for one who did not choose to hide.
    const lastSeen =
        member && contact.lastSeen !== null ? wordLastSeen(contact.lastSeen, locale, now, readingZone) : null;

    // The last day a colleague's automatic reply runs to, which the deployment states only while it is running.
    const awayUntil =
        member && contact.outOfOfficeUntil !== null
            ? wordAbsenceEnd(contact.outOfOfficeUntil, locale, readingZone)
            : null;

    // What a colleague says they are doing and where they work from, which the deployment states only while each holds.
    const message = member ? contact.statusMessage : null;
    const workMode = member ? contact.workMode : null;
    const clearsAt = message?.clearsAt ?? null;
    const clearing = clearsAt === null ? null : wordMessageClearing(clearsAt, locale, now, readingZone);
    const clears = clearing === null ? null : clearingSaid(clearing, translate);

    // Deleting and taking somebody on write to the book, so while the server is out of reach both are drawn as
    // controls that cannot act; the one with no room for a name of its own says why on the line under the person.
    // Editing opens a form whose own *Save* says the same.
    const writeRefused = useWriteRefusal();
    const erasure = translate(collected ? 'people.removeFromCollected' : 'people.deleteContact');
    const erasureRefused = writeRefused(erasure);
    const promoteRefused = writeRefused(translate('people.promote'));

    const opened = useRef<HTMLDivElement>(null);
    // Starting at nobody rather than at whoever this mounted with, unlike the reading pane: that pane is mounted by
    // landing in the client, and this one is mounted by somebody opening a person out of the list beside it.
    const focusedOn = useRef<string | null>(null);

    useEffect(() => {
        if (focusedOn.current !== contact.id) {
            focusedOn.current = contact.id;
            opened.current?.focus();
        }
    }, [contact.id]);

    // When this person was last in touch, which the correlation answers rather than the record: the conversations come
    // back most recent first, so the first of them is the answer and no arithmetic is owed.
    const lastCorrespondedAt = correspondence.correspondence?.threads[0]?.lastCorrespondedAt ?? null;

    return (
        <section
            ref={opened}
            tabIndex={-1}
            aria-label={contact.displayName}
            className="flex min-h-0 min-w-0 flex-1 flex-col"
        >
            {onBack === null ? null : (
                <div className="shrink-0 border-b border-line bg-sunken px-3 py-2">
                    <Control label={translate('people.backToList')} icon="arrow_back" shape="named" onPress={onBack} />
                </div>
            )}

            <div className="flex shrink-0 flex-wrap items-center gap-3.5 border-b border-line bg-sunken px-4 py-3.5 workspace:px-6 workspace:py-4.5">
                <span className="relative flex shrink-0 self-start">
                    <SenderAvatar
                        displayName={contact.displayName}
                        address={contact.preferredAddress}
                        picture={picture}
                        place="person"
                    />
                    {member ? <PresenceDot presence={contact.presence} place="person" /> : null}
                    {member && contact.usesPhoneApp ? <PhoneAppBadge place="person" /> : null}
                </span>

                <div className="flex min-w-0 flex-1 basis-60 flex-col gap-0.75">
                    <div className="flex min-w-0 items-center gap-2.25">
                        <h2 className="truncate text-3xl font-semibold tracking-tighter">{contact.displayName}</h2>

                        {collected ? (
                            <span className="shrink-0 rounded-sm border border-line-strong px-1.75 py-0.5 text-xs font-semibold text-muted">
                                {translate('people.collected')}
                            </span>
                        ) : null}

                        {member ? (
                            <span className="flex shrink-0 items-center gap-1.25 rounded-sm border border-line-strong px-1.75 py-0.5 text-xs font-semibold text-text-soft">
                                <Icon name="badge" className="size-icon-xs" />
                                {translate('people.colleague')}
                            </span>
                        ) : null}

                        {contact.book === 'Organization' ? (
                            <OrganizationChip organizationName={organizationName} place="page" />
                        ) : null}
                    </div>

                    {role === '' ? null : <p className="truncate text-base text-muted">{role}</p>}

                    {address === null ? null : <p className="truncate text-sm text-accent-deep">{address}</p>}

                    {member ? (
                        <p className="flex flex-wrap items-center gap-x-3 gap-y-1.5 pt-0.75 text-sm text-text-soft">
                            <span className="flex items-center gap-1.5">
                                <PresenceDot presence={contact.presence} place="header" />
                                {translate(presenceSaid[contact.presence])}
                            </span>
                            {lastSeen === null ? null : (
                                <span className="text-muted">{translate('presence.lastSeen', { when: lastSeen })}</span>
                            )}
                            {contact.usesPhoneApp ? (
                                <span className="flex items-center gap-1 text-muted">
                                    <Icon name="smartphone" className="size-icon-sm" />
                                    {translate('presence.usesPhoneApp')}
                                </span>
                            ) : null}
                            {workMode === null ? null : (
                                <span className="flex items-center gap-1 text-muted">
                                    <Icon
                                        name={workMode.mode === 'Remote' ? 'home_work' : 'apartment'}
                                        className="size-icon-sm"
                                    />
                                    {translate(workModeSaid(workMode))}
                                </span>
                            )}
                            {awayUntil === null ? null : (
                                <span className="flex items-center gap-1 rounded-sm bg-warning-soft px-2 py-0.5 font-semibold text-warning-text">
                                    <Icon name="event_busy" className="size-icon-sm" />
                                    {translate('presence.outOfOfficeUntil', { day: awayUntil })}
                                </span>
                            )}
                        </p>
                    ) : null}

                    {message === null ? null : (
                        <p className="flex items-start gap-1.75 pt-0.5 text-base text-text text-pretty">
                            <Icon name="chat_bubble" className="mt-0.5 size-icon-sm shrink-0 text-muted" />
                            <span>
                                {message.text}
                                {clears === null ? null : (
                                    <span className="text-sm text-faint">
                                        {' '}
                                        {translate('presence.messageClearing', { clears })}
                                    </span>
                                )}
                            </span>
                        </p>
                    )}

                    {own ? null : (
                        <p className="flex items-center gap-1.5 text-sm text-muted text-pretty">
                            <Icon name="lock" className="size-icon-sm" />
                            {translate(
                                readOnly.key,
                                readOnly.organization === undefined ? {} : { organization: readOnly.organization },
                            )}
                        </p>
                    )}
                </div>

                <div className="ms-auto flex shrink-0 items-center gap-2.25 self-start">
                    {writable && (own || collected) ? (
                        <Control
                            label={erasure}
                            icon="delete"
                            shape="outlinedSymbol"
                            className="bg-panel"
                            {...(erasureRefused === null ? {} : { held: true, hint: erasureRefused })}
                            onPress={onAskErasure}
                        />
                    ) : null}

                    {writable && own ? (
                        <Control
                            label={translate('people.editContact')}
                            icon="edit_note"
                            shape="outlinedSymbol"
                            className="bg-panel"
                            onPress={onAskAmendment}
                        />
                    ) : null}

                    {collected && writable ? (
                        <SecondaryButton
                            label={translate(promoting ? 'people.promoting' : 'people.promote')}
                            shape="form"
                            held={promoteRefused !== null}
                            onActivate={onPromote}
                        />
                    ) : null}

                    {member && address !== null ? (
                        <Control
                            label={translate('people.copyAddress')}
                            icon="content_copy"
                            shape="outlinedSymbol"
                            className="bg-panel"
                            onPress={() => {
                                copy([address]);
                            }}
                        />
                    ) : null}

                    {composing.offered && address !== null ? (
                        <Control
                            label={translate(member ? 'people.writeEmail' : 'people.write')}
                            shape="primary"
                            onPress={() => {
                                composing.compose({ kind: 'new', to: [address] });
                            }}
                        />
                    ) : null}
                </div>

                {recordSaid === null ? null : (
                    <p role="status" className="basis-full text-sm text-muted text-pretty">
                        {translate(recordSaid)}
                    </p>
                )}

                {!collected || !writable || promoteRefused === null ? null : (
                    <p role="status" className="basis-full text-sm text-muted text-pretty">
                        {promoteRefused}
                    </p>
                )}
            </div>

            <div className="flex min-h-0 flex-1 flex-col gap-3.5 overflow-y-auto px-4 py-3.5 workspace:px-6 workspace:py-4.5">
                {member ? (
                    <>
                        {/* The two cards side by side where the page has room, as the design draws them, and one under
                            the other where it has not. */}
                        <div className="flex flex-col gap-3 roomy:flex-row roomy:*:flex-1">
                            <ContactDetails contact={contact} />
                            <ColleagueTime
                                timeZone={contact.timeZone}
                                workingHours={contact.workingHours}
                                displayName={contact.displayName}
                            />
                        </div>

                        <OrganizationCard colleague={contact} reading={structure} onOpen={onOpenColleague} />
                    </>
                ) : (
                    <>
                        <RelationshipReading lastCorrespondedAt={lastCorrespondedAt} reading={correspondence.reading} />

                        <ContactDetails contact={contact} />

                        <PersonCorrespondence
                            reading={correspondence}
                            onOpenThread={onOpenThread}
                            onOpenDocument={onOpenDocument}
                        />
                    </>
                )}
            </div>
        </section>
    );
}
