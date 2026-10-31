// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useState, type KeyboardEvent, type ReactNode } from 'react';
import { longestContactSearch, type ClientSession, type MailFathomTransport } from '@mailfathom/client-backend';
import { useContactPhoto } from '../contactBook/contactPhotos';
import { Icon } from '../controls/Icon';
import { SenderAvatar } from '../controls/SenderAvatar';
import { SymbolControl } from '../controls/SymbolControl';
import { useTip } from '../controls/useTip';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { PresenceDot } from '../presence/PresenceDot';
import { presenceSaid } from '../presence/presenceWords';
import { looksLikeAnAddress, mostRecipientsInOneHeader } from './composition';
import {
    searchContacts,
    suggestionsFor,
    type ContactLookup,
    type RecipientSuggestion,
    type SuggestionGroupName,
} from './recipientSuggestions';

// One header of a message being written, as the design draws it: the header's name, a chip per address with a
// way to take each one back off, and a field to write the next one in.
//
// **The name stands in the composer's own label column.** The design draws the whole header as one grid — a fixed
// column of names and a column of what is written under each — so this row and the subject row below it state the same
// width rather than each measuring its own name, and the chips and the subject share a left edge.
//
// **What the field offers is a list this component draws**, as the combobox pattern the platform's accessibility
// guidance describes: the field keeps the keyboard, the arrows move through the list, Enter takes the entry in force,
// and Escape puts the list away. It opens as soon as the field has the focus, before anything is typed, and the book's
// people stand in it in the design's groups — each a `group` named by its heading, which the arrows walk through as one
// list rather than stopping at. A native `datalist` cannot do what the list is for — it matches the text of its
// options alone, so nobody is found by their name, and it draws no second line to say which address a name stands for.

/** How long the field waits after a keystroke before searching the book, so a word typed is one search rather than five. */
const searchDelay = 200;

// What each group of the book is drawn under, the organization's by the name the session gave it where it gave one.
function groupHeading(name: SuggestionGroupName, organizationName: string | null, translate: Translate): string {
    switch (name) {
        case 'colleagues':
            return translate('compose.suggestionsColleagues');
        case 'organization':
            return organizationName === null
                ? translate('compose.suggestionsOrganizationUnnamed')
                : translate('compose.suggestionsOrganization', { organization: organizationName });
        case 'own':
            return translate('compose.suggestionsOwn');
    }
}

export function RecipientField({
    label,
    placeholder,
    addresses,
    participants,
    session,
    transport,
    organizationName,
    onChanged,
    trailing,
}: {
    /** What this header is called, which is what the field is named by and what a chip's removal names. */
    readonly label: string;

    /** What the empty field says, which the design words differently for each header. */
    readonly placeholder: string;

    readonly addresses: readonly string[];

    /** The people in the conversation being answered, which are offered before anybody in the book. */
    readonly participants: readonly RecipientSuggestion[];

    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The name the reader's organization is shown by, which the group of the contacts its book keeps is headed by. */
    readonly organizationName: string | null;

    readonly onChanged: (addresses: readonly string[]) => void;

    /** What stands at the end of the row, which the design puts the reveal for the copy headers in. */
    readonly trailing?: ReactNode;
}) {
    const { translate } = useLocalization();
    const [written, setWritten] = useState('');
    const [refused, setRefused] = useState<string | null>(null);
    const [focused, setFocused] = useState(false);
    const [dismissed, setDismissed] = useState(false);
    const [active, setActive] = useState<number | null>(null);
    // The term an answer was for travels with it: its people are narrowed to what is typed now, while what it says about
    // the book is only true of the term it was asked for.
    const [lookup, setLookup] = useState<{ readonly term: string; readonly answer: ContactLookup } | null>(null);
    const fieldId = useId();
    const listId = useId();

    const term = written.trim();
    // The book is asked while the field has the focus, typed in or not: the design lists a few of each group before
    // anything is typed.
    const searchable = focused && term.length <= longestContactSearch;

    // Searching the book is a request going out, which is what an effect is for. The answer is discarded where the term
    // moved on before it arrived, and the wait before asking is what keeps a word typed from being a search per letter.
    useEffect(() => {
        if (!searchable) {
            return;
        }

        let listening = true;

        const waiting = setTimeout(() => {
            void searchContacts(session, transport, term).then((answer) => {
                if (listening) {
                    setLookup({ term, answer });
                }
            });
        }, searchDelay);

        return () => {
            listening = false;
            clearTimeout(waiting);
        };
    }, [searchable, term, session, transport]);

    const fromTheBook = lookup?.answer.groups ?? [];
    const suggested = searchable ? suggestionsFor(term, participants, fromTheBook, addresses) : null;
    // The list as the arrows walk it: the conversation's people, then each group in turn.
    const offered =
        suggested === null
            ? []
            : [...suggested.participants, ...suggested.groups.flatMap((group) => group.suggestions)];
    const unsearchable = searchable && lookup?.term === term && lookup.answer.incomplete;
    const open = focused && !dismissed && (offered.length > 0 || unsearchable);

    // The arrows move only the highlight, and the list is bounded, so the one in force is brought into the box's view.
    useEffect(() => {
        if (active !== null) {
            document.getElementById(`${listId}-${active.toFixed(0)}`)?.scrollIntoView({ block: 'nearest' });
        }
    }, [active, listId]);
    // The popup can hold the note alone, and the combobox names only a listbox that is drawn.
    const listed = open && offered.length > 0;
    const inForce = open && active !== null ? offered[active] : undefined;

    // The field's own text is committed on Enter, on a comma, and on leaving the field, because all three are ways
    // somebody signals they have finished writing one address. What it refuses says why rather than doing nothing.
    function commit(text: string): void {
        const address = text.trim().replace(/,$/u, '');

        if (address === '') {
            setRefused(null);
            setWritten('');

            return;
        }

        if (!looksLikeAnAddress(address)) {
            setRefused(translate('compose.notAnAddress'));

            return;
        }

        if (addresses.includes(address)) {
            setRefused(translate('compose.alreadyAddressed', { address }));

            return;
        }

        if (addresses.length >= mostRecipientsInOneHeader) {
            setRefused(translate('compose.tooManyAddresses', { count: mostRecipientsInOneHeader.toFixed(0) }));

            return;
        }

        setWritten('');
        setRefused(null);
        setActive(null);
        setLookup(null);
        onChanged([...addresses, address]);
    }

    // The arrows walk the list and wrap at either end, Enter takes what they rest on, and Escape puts the list away
    // without leaving the field, until the next keystroke asks for it again.
    function moveThroughTheList(event: KeyboardEvent<HTMLInputElement>): void {
        if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && (offered.length > 0 || unsearchable)) {
            event.preventDefault();
            setDismissed(false);

            if (offered.length === 0) {
                return;
            }

            const step = event.key === 'ArrowDown' ? 1 : -1;
            const from = active ?? (step === 1 ? -1 : 0);

            setActive((from + step + offered.length) % offered.length);

            return;
        }

        if (event.key === 'Escape' && open) {
            event.preventDefault();
            setDismissed(true);
            setActive(null);

            return;
        }

        if (event.key === 'Enter') {
            event.preventDefault();
            commit(inForce?.address ?? written);
        }
    }

    return (
        <div className="relative flex flex-wrap items-center gap-2.5 border-b border-line-soft px-3.75 py-2.25 focus-within:border-b-accent">
            <label htmlFor={fieldId} className="w-14.5 shrink-0 text-sm text-muted workspace:w-22">
                {label}
            </label>

            {addresses.map((address) => (
                <span
                    key={address}
                    className="flex max-w-full min-w-0 items-center gap-1.75 rounded-lg border border-line bg-rail px-2.5 py-0.75 text-base whitespace-nowrap"
                >
                    <CutAddress address={address} />
                    <SymbolControl
                        label={translate('compose.removeRecipient', { address, header: label })}
                        className="flex shrink-0 items-center rounded-xs text-faint transition hover:text-text"
                        onPress={() => {
                            onChanged(addresses.filter((kept) => kept !== address));
                        }}
                    >
                        <Icon name="close" className="size-icon-xs" />
                    </SymbolControl>
                </span>
            ))}

            <input
                id={fieldId}
                role="combobox"
                aria-autocomplete="list"
                aria-expanded={listed}
                aria-controls={listed ? listId : undefined}
                aria-activedescendant={inForce === undefined ? undefined : `${listId}-${active?.toFixed(0) ?? ''}`}
                value={written}
                inputMode="email"
                autoComplete="off"
                placeholder={placeholder}
                className="min-w-24 flex-1 border-none bg-transparent text-base text-ellipsis text-text outline-none placeholder:text-faint workspace:min-w-40"
                onChange={(event) => {
                    setRefused(null);
                    setDismissed(false);
                    setActive(null);
                    setWritten(event.target.value);

                    // A comma is how one address is written after another, so it commits what stands before it rather
                    // than becoming part of an address nothing would accept.
                    if (event.target.value.endsWith(',')) {
                        commit(event.target.value);
                    }
                }}
                onKeyDown={moveThroughTheList}
                onFocus={() => {
                    setFocused(true);
                }}
                onBlur={() => {
                    setFocused(false);
                    setActive(null);
                    commit(written);
                }}
            />

            {trailing}

            {open ? (
                <div className="absolute inset-x-3.75 top-full z-20 mt-1 overflow-hidden rounded-lg border border-line bg-panel py-1 shadow-overlay">
                    {listed && suggested !== null ? (
                        <div
                            id={listId}
                            role="listbox"
                            aria-label={translate('compose.suggestedRecipients', { header: label })}
                            className="max-h-75 overflow-y-auto"
                        >
                            {suggested.participants.map((suggestion) => {
                                const index = offered.indexOf(suggestion);

                                return (
                                    <SuggestedRecipient
                                        key={suggestion.address}
                                        id={`${listId}-${index.toFixed(0)}`}
                                        suggestion={suggestion}
                                        inForce={index === active}
                                        onChosen={() => {
                                            commit(suggestion.address);
                                        }}
                                    />
                                );
                            })}

                            {suggested.groups.map((group) => (
                                <div key={group.name} role="group" aria-labelledby={`${listId}-${group.name}`}>
                                    <div
                                        id={`${listId}-${group.name}`}
                                        role="presentation"
                                        className="px-3 pt-2 pb-0.75 text-xs font-semibold text-faint"
                                    >
                                        {groupHeading(group.name, organizationName, translate)}
                                    </div>

                                    {group.suggestions.map((suggestion) => {
                                        const index = offered.indexOf(suggestion);

                                        return (
                                            <SuggestedRecipient
                                                key={suggestion.address}
                                                id={`${listId}-${index.toFixed(0)}`}
                                                suggestion={suggestion}
                                                inForce={index === active}
                                                onChosen={() => {
                                                    commit(suggestion.address);
                                                }}
                                            />
                                        );
                                    })}
                                </div>
                            ))}
                        </div>
                    ) : null}

                    {unsearchable ? (
                        <p aria-live="polite" className="px-3 py-1.75 text-sm text-muted">
                            {translate('compose.contactsUnsearchable')}
                        </p>
                    ) : null}
                </div>
            ) : null}

            {refused === null ? null : (
                <p role="alert" className="basis-full text-sm text-warning-text">
                    {refused}
                </p>
            )}
        </div>
    );
}

// The line under a suggested person's name: a colleague's presence in words and what they do — the address where their
// record names no title, as the contact book's row does — so the dot on their avatar is never the only telling of it;
// anybody else's, the address choosing them writes in.
function lineOf(suggestion: RecipientSuggestion, translate: Translate): string {
    if (suggestion.colleague === undefined) {
        return suggestion.address;
    }

    return translate('presence.withRole', {
        presence: translate(presenceSaid[suggestion.colleague.presence]),
        role: suggestion.colleague.jobTitle ?? suggestion.address,
    });
}

// One entry of the list: who it is, and how they are told apart — a colleague by presence and role, anybody else by
// the address choosing them writes in. A press is kept from taking the focus out of the field, which is what would
// otherwise commit the half-typed text before the entry was chosen.
function SuggestedRecipient({
    id,
    suggestion,
    inForce,
    onChosen,
}: {
    readonly id: string;
    readonly suggestion: RecipientSuggestion;
    readonly inForce: boolean;
    readonly onChosen: () => void;
}) {
    const { translate } = useLocalization();
    const picture = useContactPhoto(suggestion.photo ?? null);

    return (
        <div
            id={id}
            role="option"
            aria-selected={inForce}
            className={`flex min-h-10 cursor-pointer items-center gap-2.5 px-3.75 py-1 pointer-coarse:min-h-12 ${
                inForce ? 'bg-hover' : 'hover:bg-hover'
            }`}
            onMouseDown={(event) => {
                event.preventDefault();
            }}
            onClick={onChosen}
        >
            <span className="relative flex shrink-0">
                <SenderAvatar
                    displayName={suggestion.name}
                    address={suggestion.address}
                    picture={picture}
                    place="suggestion"
                />
                {suggestion.colleague === undefined ? null : (
                    <PresenceDot presence={suggestion.colleague.presence} place="suggestion" />
                )}
            </span>
            <span className="flex min-w-0 flex-1 flex-col gap-px">
                <span className="truncate text-base font-semibold text-text">
                    {suggestion.name ?? suggestion.address}
                </span>
                {suggestion.name === null && suggestion.colleague === undefined ? null : (
                    <span className="truncate text-sm text-muted">{lineOf(suggestion, translate)}</span>
                )}
            </span>
        </div>
    );
}

/**
 * One line however long the address, cut rather than wrapped, so a chip never outgrows the row — and the whole of it
 * said to a mouse resting on it and a finger tapping it, since a tap on the address does nothing else.
 */
function CutAddress({ address }: { readonly address: string }) {
    const { anchor, tip } = useTip({ says: address });

    return (
        <>
            <span {...anchor} className="min-w-0 truncate">
                {address}
            </span>
            {tip}
        </>
    );
}
