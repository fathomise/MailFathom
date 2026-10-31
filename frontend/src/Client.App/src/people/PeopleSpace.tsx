// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useMemo, useRef, useState, type RefObject } from 'react';
import {
    amendContact,
    eraseContact,
    promoteContact,
    readContact,
    recordContact,
    wentUnanswered,
    type ClientFailure,
    type ClientSession,
    type Contact,
    type ContactOrigin,
    type ContactWriteOutcome,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import { ContactBook } from '../contactBook/ContactBook';
import { ContactPhotoContext, contactPhotoReader, useContactPhoto } from '../contactBook/contactPhotos';
import { ContactSelectionBar } from '../contactBook/ContactSelectionBar';
import { useContactBook, type ContactBookName, type Person } from '../contactBook/useContactBook';
import { Control } from '../controls/Control';
import type { ContactPhotoExchange } from '../deployment/portraitExchange';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useTwoPanes, useWideWorkspace } from '../shell/useWideWorkspace';
import { ContactDialog, type ContactPhotoChange, type ContactSave } from './ContactDialog';
import { rowsOf } from './contactAddresses';
import { PersonPage } from './PersonPage';
import { useColleagueStructure } from './useColleagueStructure';
import { useContactCorrespondence } from './useContactCorrespondence';
import { paneArrival, usePaneMotion } from '../shell/paneMotion';
import { useScreenStep } from '../shell/screenLayers';

// The People space as the design composes it: the address book down one side and one person's own page beside
// it, with the acts that change the book held here rather than in either.
//
// **The book and the person are one pane below the width the mail screens collapse at**, and the same width decides
// both — a person opened there comes in front of the list and carries the way back, which is why `PersonPage` draws
// that control only when this hands it one.
//
// **Every write is answered by reading the book again rather than by correcting what is held.** Where somebody sorts in
// the book is the deployment's, and a promotion moves them between the two books entirely, so a list edited in place
// would be this client's own opinion of an ordering it does not own. What a write leaves behind instead is one
// sentence saying what happened, beside the thing it happened to.
//
// **What the toolbar carries is one act, and that is the whole of what this deployment publishes a route for.** The
// design draws three acts on the book beside it — scheduling a meeting, merging duplicates, and importing a book — and
// `/api/client` answers none of them, so each is left out rather than drawn as a control that would do nothing. That
// is a correction owed to the design rather than a toolbar to fill.
//
// **Inside an organization the space opens on its members.** *Colleagues* stands between the reader's own book and the
// collected one only for a reader who belongs to an organization, and is the book they arrive at there, as the design
// draws it; outside one the space opens on *Own* as it always did. A colleague, and a contact the organization keeps
// under *Own*, are read-only: nothing here writes to either, and a write the service refused as read-only says so.
//
// **A photo is stored after the record it belongs to, and read back with it.** The photo is a file the deployment keeps
// under a contact, so the contact is written first and the photo second, and the record is then read again — what
// names the photo is the record, and that name is what every avatar of the person draws from.
//
// **A contact the reader's organization keeps is never taken out or corrected here.** It is listed among the reader's own
// and marked as the organization's, and a selection holding some of them takes out only the reader's own beside them,
// saying how many stay — the organization's book is read-only to a member, and a selection of nothing else offers no
// deletion at all.

// What one write to the book said, worded as what happened to the person rather than as an outcome name. Exhaustive by
// its own type, so an outcome added to the surface does not compile until this says what a reader is told about it.
const writeSaid: Readonly<Record<ContactWriteOutcome, MessageKey>> = {
    Written: 'people.written',
    NotFound: 'people.writeNotFound',
    AddressHeldByAnotherContact: 'people.addressHeld',
    OriginRefusesWriter: 'people.originRefuses',
    AlreadyAsserted: 'people.alreadyAsserted',
    OrganizationContactReadOnly: 'people.writeReadOnly',
};

// The books a reader has, in the order the segments stand: *Colleagues* between the other two inside an organization.
function booksFor(inOrganization: boolean): readonly ContactBookName[] {
    return inOrganization ? ['own', 'colleagues', 'collected'] : ['own', 'collected'];
}

// Whether somebody listed is in one of the reader's own books, which is what may be taken out of it: what the reader
// wrote down, or what their mail collected — never a colleague or a contact the organization keeps.
function inOwnBooks(person: Person): person is Contact {
    return person.book === 'Own' || person.book === 'Collected';
}

// Where a sentence about a write belongs: over the book for a write the book as a whole answered, and on the person's
// own page for one about the person who is open. One state rather than two, because only one write is in flight at a
// time and two would eventually disagree about which was the last.
// What a write that failed is said as: a refusal the server stated changed nothing, while a write nobody answered —
// or one a gateway answered for the server, which proves nothing about whether it was handed on — may well have
// landed, so it is said to be unknown and never that nothing was changed.
function failedSaid(failure: ClientFailure): MessageKey {
    return wentUnanswered(failure) ? 'people.writeUnknown' : 'people.writeFailed';
}

// A colleague as the directory holds them now rather than as they were when opened, because their presence moves
// while the page is open and the book is what reads it again.
function asHeld(opened: Person, held: readonly Person[]): Person {
    return opened.book === 'Member' ? (held.find(({ id }) => id === opened.id) ?? opened) : opened;
}

// Who is opened once the person who was open has been taken out of the book: whoever the list draws in their place, or
// the one before where they were the last — and nobody where the book is left empty, or where the person was not in
// the book being listed at all, which is a person opened in one book and taken out after the reader moved to the other.
function nextAfter(contacts: readonly Person[], leaving: string, erased: readonly string[]): Person | null {
    const at = contacts.findIndex((contact) => contact.id === leaving);
    const kept = (listed: readonly Person[]) => listed.filter((contact) => !erased.includes(contact.id));
    const rest = kept(contacts);

    // Counted among those kept, because a batch may also take out people listed above the one who was open.
    return at < 0 ? null : (rest[Math.min(kept(contacts.slice(0, at)).length, rest.length - 1)] ?? null);
}

// How many of the organization's contacts a deletion leaves where they are, in the forms a language has for the noun.
const keptCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'people.keptReadOnly.other',
    one: 'people.keptReadOnly.one',
    two: 'people.keptReadOnly.other',
    few: 'people.keptReadOnly.few',
    many: 'people.keptReadOnly.many',
    other: 'people.keptReadOnly.other',
};

// What a question about taking several people out says will happen, in the forms a language has for the noun — one
// table per book, because deleting somebody written down and removing somebody collected are different sentences.
const erasingCounted: Readonly<Record<ContactOrigin, Readonly<Record<Intl.LDMLPluralRule, MessageKey>>>> = {
    Asserted: {
        zero: 'people.eraseManyWhat.other',
        one: 'people.eraseManyWhat.one',
        two: 'people.eraseManyWhat.other',
        few: 'people.eraseManyWhat.few',
        many: 'people.eraseManyWhat.many',
        other: 'people.eraseManyWhat.other',
    },
    Collected: {
        zero: 'people.removeManyWhat.other',
        one: 'people.removeManyWhat.one',
        two: 'people.removeManyWhat.other',
        few: 'people.removeManyWhat.few',
        many: 'people.removeManyWhat.many',
        other: 'people.removeManyWhat.other',
    },
};

// The rest of that question, per book: what it asks, what it says about one person, what follows, and what the act is
// called.
const erasingAsked: Readonly<
    Record<
        ContactOrigin,
        {
            readonly one: MessageKey;
            readonly many: MessageKey;
            readonly oneWhat: MessageKey;
            readonly afterwards: MessageKey;
            readonly act: MessageKey;
            readonly done: MessageKey;
            readonly someRefused: MessageKey;
        }
    >
> = {
    Asserted: {
        one: 'people.eraseOne',
        many: 'people.eraseMany',
        oneWhat: 'people.eraseOneWhat',
        afterwards: 'people.erasePermanent',
        act: 'people.eraseAct',
        done: 'people.erased',
        someRefused: 'people.eraseSomeRefused',
    },
    Collected: {
        one: 'people.removeOne',
        many: 'people.removeMany',
        oneWhat: 'people.removeOneWhat',
        afterwards: 'people.removeReturns',
        act: 'people.removeFromCollected',
        done: 'people.removed',
        someRefused: 'people.removeSomeRefused',
    },
};

interface WriteSaid {
    readonly where: 'book' | 'person';
    readonly said: MessageKey;

    /** What that sentence names, where it names anybody: the holes the catalogue entry carries. */
    readonly names?: Readonly<Record<string, string>>;
}

export function PeopleSpace({
    session,
    transport,
    contactPhotos,
    writable,
    organizationName,
    inOrganization,
    onOpenThread,
    onOpenDocument,
}: {
    /** Who is asking and where, or `null` where there is nothing to ask with. */
    readonly session: ClientSession | null;

    readonly transport: MailFathomTransport;

    /** How a contact's photo is read and written, which is a second adapter because octets are not text. */
    readonly contactPhotos: ContactPhotoExchange;

    /** Whether this credential may change the book at all, which every act below is behind. */
    readonly writable: boolean;

    /** The name the reader's organization is shown by, which its contacts are marked with, or `null` where none is known. */
    readonly organizationName: string | null;
    /** Whether the reader belongs to an organization, which is what puts its members in a book of their own. */
    readonly inOrganization: boolean;

    /** Opens one conversation, which is the Mail space's to draw and therefore the frame's to perform. */
    readonly onOpenThread: (thread: {
        readonly threadId: string;
        readonly messageId: string;
        readonly subject: string | null;
    }) => void;

    /** Opens one file a person sent, named as which file of which message. */
    readonly onOpenDocument: (document: { readonly messageId: string; readonly position: number }) => void;
}) {
    const { locale, translate } = useLocalization();
    const twoPanes = useTwoPanes();
    const wide = useWideWorkspace();

    const books = booksFor(inOrganization);
    const [book, setBook] = useState<ContactBookName>(inOrganization ? 'colleagues' : 'own');
    const [opened, setOpened] = useState<Person | null>(null);
    const [selected, setSelected] = useState<readonly string[]>([]);
    const [erasing, setErasing] = useState<readonly Contact[]>([]);
    const [kept, setKept] = useState(0);
    const [said, setSaid] = useState<WriteSaid | null>(null);
    const [promoting, setPromoting] = useState(false);

    const asked = useRef<HTMLDialogElement | null>(null);
    const asking = useRef<HTMLDialogElement | null>(null);
    const amending = useRef<HTMLDialogElement | null>(null);

    // Who is open *now*, as against who was open when a write was asked for. A write answers after a wait, and the
    // reader is free to open somebody else inside it — so state read in that answer is the state the press closed
    // over, and acting on it would pull the screen back to whoever they had left. The ref is written wherever the
    // opened person changes and read only in an answer, so nothing here touches it while rendering.
    const openedNow = useRef<string | null>(null);

    function open(contact: Person | null): void {
        openedNow.current = contact?.id ?? null;
        setOpened(contact);
    }

    const reading = useContactBook(session, transport, book);
    // A member of the directory is nobody the books hold, so there is no correspondence of theirs to read.
    const correspondence = useContactCorrespondence(
        session,
        transport,
        opened === null || opened.book === 'Member' ? null : opened.id,
    );
    const structure = useColleagueStructure(session, transport, opened?.book === 'Member' ? opened.id : null);

    // One reader per session, because what it keeps is the photos that session drew: kept across renders so a book
    // read again does not ask again for a photo that did not change.
    const photoReader = useMemo(
        () => (session === null ? null : contactPhotoReader(session, contactPhotos)),
        [session, contactPhotos],
    );

    // Stores what the form said about the photo, answering the sentence owed where it did not land and `null` otherwise.
    // A write nobody answered may well have landed, so it is said to be unknown rather than refused.
    async function storePhoto(contactId: string, photo: ContactPhotoChange): Promise<MessageKey | null> {
        if (session === null || photo.kind === 'kept') {
            return null;
        }

        const answer =
            photo.kind === 'chosen'
                ? await contactPhotos.replace(session, contactId, photo.picture, photo.type)
                : await contactPhotos.remove(session, contactId);

        if (answer.outcome === 'stored') {
            return null;
        }

        return answer.reason === 'unavailable' ? 'people.photoStateUnknown' : 'people.photoNotStored';
    }

    // The record as it stands after a photo was written, since the record is what names the photo; where it cannot be
    // read, the record the write answered with stands in.
    async function readBack(contactId: string, standing: Contact | null): Promise<Contact | null> {
        if (session === null) {
            return standing;
        }

        const answer = await readContact(session, transport, contactId);

        return answer.outcome === 'read' ? answer.value : standing;
    }

    // The people picked out, read back out of the book rather than held as records: the list holds identities and the
    // book holds what each of them is, so a selection cannot outlive a read that no longer names one of them.
    const picked = reading.contacts.filter((contact) => selected.includes(contact.id));
    const pickedOwn = picked.filter(inOwnBooks);

    function record({ statement, photo }: ContactSave): void {
        if (session === null) {
            return;
        }

        void recordContact(session, transport, { ...statement, note: null }).then(async (answer) => {
            if (answer.outcome === 'failed') {
                setSaid({ where: 'book', said: failedSaid(answer.failure) });

                return;
            }

            const written = answer.value.contact;
            const photoSaid =
                answer.value.outcome === 'Written' && written !== null ? await storePhoto(written.id, photo) : null;

            setSaid({ where: 'book', said: photoSaid ?? writeSaid[answer.value.outcome] });

            if (answer.value.outcome === 'Written') {
                // Written into the book somebody asserted, which is the book this then shows: a person who has just
                // written somebody down is owed the row rather than the tab they happened to be on.
                setBook('own');
                reading.readAgain();
            }
        });
    }

    // A correction states the name, every address, and the phone numbers the form holds, and keeps the one thing the
    // form does not show: the note. A correction of the photo alone writes nothing else, so the record is not amended for it.
    function amend(contact: Contact, { statement, restated, photo }: ContactSave): void {
        if (session === null) {
            return;
        }

        const amended = restated
            ? amendContact(session, transport, contact.id, { ...statement, note: contact.note })
            : null;

        void Promise.resolve(amended).then(async (answer) => {
            if (answer?.outcome === 'failed') {
                setSaid({ where: 'person', said: failedSaid(answer.failure) });

                return;
            }

            const outcome = answer?.value.outcome ?? 'Written';
            const standing = answer?.value.contact ?? null;
            const photoSaid = outcome === 'Written' ? await storePhoto(contact.id, photo) : null;

            setSaid({
                where: 'person',
                said: photoSaid ?? (outcome === 'Written' ? 'people.amended' : writeSaid[outcome]),
            });

            const current =
                outcome === 'Written' && photo.kind !== 'kept' ? await readBack(contact.id, standing) : standing;

            if (current !== null && openedNow.current === contact.id) {
                open(current);
            }

            reading.readAgain();
        });
    }

    function promote(): void {
        if (session === null || opened?.book !== 'Collected') {
            return;
        }

        const promoted = opened.id;

        setPromoting(true);
        setSaid(null);

        void promoteContact(session, transport, promoted).then((answer) => {
            setPromoting(false);

            if (answer.outcome === 'failed') {
                setSaid({ where: 'person', said: failedSaid(answer.failure) });

                return;
            }

            setSaid({ where: 'person', said: writeSaid[answer.value.outcome] });

            // Taking somebody on writes them into the asserted book, which is the book this then shows and the record
            // it then opens — but only where they are still the person being read. Somebody who opened a second
            // person while this was in flight is reading that one, and neither the row nor the tab is ours to move
            // out from under them.
            if (answer.value.contact !== null && openedNow.current === promoted) {
                open(answer.value.contact);
                setBook('own');
            }

            reading.readAgain();
        });
    }

    // Erasing several people is several writes rather than one, so the answers are sorted rather than reduced to
    // whether any of them failed: a batch in which one refusal stood beside four erasures would otherwise say nothing
    // was changed while the book came back four people shorter, and would drop the refused person out of the selection
    // with no way left to ask again about them. So what succeeded leaves the selection and what did not stays in it,
    // and the sentence names whoever is still there.
    function erase(contacts: readonly Contact[]): void {
        if (session === null) {
            return;
        }

        void Promise.all(
            contacts.map((contact) =>
                eraseContact(session, transport, contact.id).then((answer) => ({ contact, answer })),
            ),
        ).then((answers) => {
            // A contact the organization keeps is refused rather than erased, said as the read-only rule it is where
            // that is the whole of what was refused.
            const readOnly = ({ answer }: (typeof answers)[number]) =>
                answer.outcome === 'read' && answer.value === 'OrganizationContactReadOnly';
            const notErased = answers.filter((settled) => settled.answer.outcome === 'failed' || readOnly(settled));
            const refused = notErased.map(({ contact }) => contact);
            const erased = answers.filter((settled) => !notErased.includes(settled)).map(({ contact }) => contact.id);

            // An erasure nobody answered is not known to have failed, so it is never named among those refused.
            const unknown = answers.some(({ answer }) => answer.outcome === 'failed' && wentUnanswered(answer.failure));

            setSaid(
                refused.length === 0
                    ? { where: 'book', said: erasingAsked[contacts[0]?.origin ?? 'Asserted'].done }
                    : unknown
                      ? { where: 'book', said: 'people.writeUnknown' }
                      : erased.length === 0
                        ? {
                              where: 'book',
                              said: notErased.every(readOnly) ? 'people.writeReadOnly' : 'people.writeFailed',
                          }
                        : {
                              where: 'book',
                              said: erasingAsked[contacts[0]?.origin ?? 'Asserted'].someRefused,
                              names: {
                                  names: new Intl.ListFormat(locale, { type: 'conjunction' }).format(
                                      refused.map((contact) => contact.displayName),
                                  ),
                              },
                          },
            );

            setSelected((standing) => standing.filter((contact) => !erased.includes(contact)));

            if (openedNow.current !== null && erased.includes(openedNow.current)) {
                open(twoPanes ? nextAfter(reading.contacts, openedNow.current, erased) : null);
            }
            reading.readAgain();
        });
    }

    function askErasure(contacts: readonly Contact[], keptBeside = 0): void {
        if (contacts.length === 0) {
            return;
        }

        setErasing(contacts);
        setKept(keptBeside);
        asked.current?.showModal();
    }

    // Whoever is being taken out came out of one book — a selection is cleared when the book changes — so the question
    // is asked in that book's words.
    const erasedFrom = erasing[0]?.origin ?? 'Asserted';
    const erasingQuestion = erasingAsked[erasedFrom];

    const personInFront = !twoPanes && opened !== null;
    const paneMoved = usePaneMotion(personInFront, twoPanes);

    // The person stands where the book was, so the back gesture returns to the book exactly as the way back on the page
    // does.
    useScreenStep(personInFront, () => {
        open(null);
    });

    return (
        <ContactPhotoContext value={photoReader}>
            <div className="relative flex min-h-0 flex-1 flex-col">
                {selected.length > 0 ? (
                    <ContactSelectionBar
                        book={book}
                        selected={picked}
                        erasable={writable && pickedOwn.length > 0}
                        onClear={() => {
                            setSelected([]);
                        }}
                        onSelectAll={() => {
                            setSelected(reading.contacts.map((contact) => contact.id));
                        }}
                        onAskErasure={() => {
                            askErasure(pickedOwn, picked.length - pickedOwn.length);
                        }}
                    />
                ) : wide ? (
                    <div className="flex shrink-0 items-center gap-2.25 border-b border-line bg-panel px-3 py-2">
                        {writable ? (
                            <Control
                                label={translate('people.newContact')}
                                icon="add"
                                shape="primary"
                                onPress={() => {
                                    setSaid(null);
                                    asking.current?.showModal();
                                }}
                            />
                        ) : null}
                    </div>
                ) : null}

                {said?.where === 'book' ? (
                    <p role="status" className="shrink-0 border-b border-line bg-sunken px-4 py-2 text-sm text-muted">
                        {translate(said.said, said.names)}
                    </p>
                ) : null}

                <div className="relative flex min-h-0 flex-1 flex-col panes:flex-row">
                    {/* The book stands aside rather than coming down where one pane draws the person, for the reason
                        `shell/Space.tsx` gives about a space: what it holds is the pages the deployment answered and where
                        in them the reader had scrolled to, and the platform keeps a scroller's offset only while its box
                        is there. How wide the column is belongs to this composition rather than to the list, which is why
                        the width and the boundary are written here and not inside it. */}
                    <div
                        aria-hidden={personInFront ? true : undefined}
                        inert={personInFront}
                        className={`flex min-h-0 min-w-0 flex-col panes:border-e panes:border-line ${paneArrival(paneMoved, false)} ${
                            personInFront ? 'invisible absolute inset-0' : 'flex-1 panes:w-contact-list panes:flex-none'
                        }`}
                    >
                        <ContactBook
                            books={books}
                            // Keyed by the book, so moving between the two draws a list rather than continuing one. What
                            // this list holds is where it was scrolled, which row the keyboard is on, and the height it
                            // measured — every one of them about the book that was being read, and none of them true of
                            // the one arrived at. `messageList/MessageList.tsx` writes the same reset by hand because a
                            // folder is read into the same list; here the two books are two lists, and a key is what says
                            // so. It also puts the scroller back at the top, which state alone cannot: `scrollTop` on the
                            // element the browser holds is not this component's to write.
                            key={book}
                            book={book}
                            reading={reading}
                            organizationName={organizationName}
                            opened={opened?.id ?? null}
                            selected={selected}
                            erasable={writable}
                            onBook={(chosen) => {
                                setBook(chosen);
                                setSelected([]);
                            }}
                            onOpen={(contact) => {
                                setSaid(null);
                                open(contact);
                            }}
                            onSelected={setSelected}
                            onAskErasure={askErasure}
                        />
                    </div>

                    {opened === null ? (
                        // Beside an empty pane rather than instead of one, so the book does not move under a reader the
                        // moment they open somebody. The narrow shape has no second pane to leave empty.
                        twoPanes ? (
                            <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 px-6 py-8 text-center">
                                <p className="text-base text-muted text-pretty">{translate('people.nobodyOpen')}</p>
                            </div>
                        ) : null
                    ) : (
                        // One element around the page so it can arrive the way a pane does: the page itself is drawn
                        // the same in either composition, and only the narrow one moves it.
                        <div className={`flex min-h-0 min-w-0 flex-1 flex-col ${paneArrival(paneMoved, true)}`}>
                            <PersonPage
                                contact={asHeld(opened, reading.contacts)}
                                organizationName={organizationName}
                                correspondence={correspondence}
                                structure={structure}
                                writable={writable}
                                promoting={promoting}
                                recordSaid={said?.where === 'person' ? said.said : null}
                                onBack={
                                    twoPanes
                                        ? null
                                        : () => {
                                              open(null);
                                          }
                                }
                                onPromote={promote}
                                onAskAmendment={() => {
                                    setSaid(null);
                                    amending.current?.showModal();
                                }}
                                onAskErasure={() => {
                                    askErasure(inOwnBooks(opened) ? [opened] : []);
                                }}
                                onOpenColleague={(colleague) => {
                                    setSaid(null);
                                    open(colleague);
                                }}
                                onOpenThread={onOpenThread}
                                onOpenDocument={onOpenDocument}
                            />
                        </div>
                    )}
                </div>

                {/* The narrow composition's own way to write somebody down, which is where a thumb reaches rather than at
                    the top of a column: the toolbar the wide shape carries has nowhere to stand once bottom navigation has
                    the foot of the window. */}
                {!wide && writable && selected.length === 0 && !personInFront ? (
                    <Control
                        label={translate('people.newContact')}
                        icon="person_add"
                        shape="floating"
                        className="absolute end-4 bottom-4"
                        onPress={() => {
                            setSaid(null);
                            asking.current?.showModal();
                        }}
                    />
                ) : null}

                {writable ? <ContactDialog asked={asking} onSave={record} /> : null}

                {/* Keyed by the person and by what their record says, so the form opens on whoever is open now as their
                    record now stands — a correction just saved included — rather than on whatever it was first drawn for. */}
                {writable && opened?.book === 'Own' ? (
                    <ContactAmendment
                        key={`${opened.id} ${opened.displayName} ${opened.preferredAddress} ${opened.addresses.join(' ')} ${JSON.stringify(opened.phoneNumbers)} ${opened.photo ?? ''}`}
                        contact={opened}
                        asked={amending}
                        onSave={(save) => {
                            amend(opened, save);
                        }}
                    />
                ) : null}

                <Confirmation
                    asked={asked}
                    mark="delete"
                    question={translate(erasing.length === 1 ? erasingQuestion.one : erasingQuestion.many)}
                    consequence={
                        <p className="text-base text-pretty">
                            {erasing.length === 1
                                ? translate(erasingQuestion.oneWhat, { name: erasing[0]?.displayName ?? '' })
                                : translate(
                                      erasingCounted[erasedFrom][new Intl.PluralRules(locale).select(erasing.length)],
                                      { count: new Intl.NumberFormat(locale).format(erasing.length) },
                                  )}
                            {kept === 0
                                ? null
                                : ` ${translate(keptCounted[new Intl.PluralRules(locale).select(kept)], {
                                      count: new Intl.NumberFormat(locale).format(kept),
                                      organization: organizationName ?? translate('people.yourOrganization'),
                                  })}`}
                        </p>
                    }
                    reversal={{ kind: 'permanent', said: translate(erasingQuestion.afterwards) }}
                    ways={[
                        { said: translate('act.cancel'), manner: 'back' },
                        {
                            said: translate(erasingQuestion.act),
                            manner: 'destroy',
                            run: () => {
                                erase(erasing);
                            },
                        },
                    ]}
                />
            </div>
        </ContactPhotoContext>
    );
}

// The form a correction opens, drawn inside the space's photo reader so it opens on the photo every avatar of the
// person draws rather than reading one of its own.
function ContactAmendment({
    contact,
    asked,
    onSave,
}: {
    readonly contact: Contact;
    readonly asked: RefObject<HTMLDialogElement | null>;
    readonly onSave: (save: ContactSave) => void;
}) {
    const photo = useContactPhoto(contact);

    return (
        <ContactDialog
            asked={asked}
            amending={{
                displayName: contact.displayName,
                rows: rowsOf(contact.addresses, contact.preferredAddress),
                phoneNumbers: contact.phoneNumbers,
                photo,
            }}
            onSave={onSave}
        />
    );
}
