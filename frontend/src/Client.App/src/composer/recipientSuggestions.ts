// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    readColleagues,
    readCollectedContacts,
    readOwnContacts,
    type ClientResult,
    type ClientSession,
    type Colleague,
    type Contact,
    type MailFathomTransport,
} from '@mailfathom/client-backend';

// Who a recipient field offers, as the design draws it: the people in the conversation being answered first, then the
// address book in three groups under their names — the members of the reader's organization, the contacts the
// organization keeps, and the reader's own, with what their mailboxes picked up after the reader's own in that last
// group, which is where they stood when the list was one. Three a group while nothing is typed and six once something
// is, narrowed by a name or an address. The book is searched by the deployment, so what is matched there is its own
// rule; what is matched here is only the narrowing of an answer already on the screen, which is what keeps the list
// moving with every keystroke rather than with every answer.

/** One person a recipient field offers: the address choosing them writes in, and the name they go by where known. */
export interface RecipientSuggestion {
    readonly address: string;
    readonly name: string | null;
}

/** The groups the book is offered in, in the order the design draws them. */
export const suggestionGroupNames = ['colleagues', 'organization', 'own'] as const;

export type SuggestionGroupName = (typeof suggestionGroupNames)[number];

/** One group of the book's people, under the name it is drawn under. */
export interface SuggestionGroup {
    readonly name: SuggestionGroupName;
    readonly suggestions: readonly RecipientSuggestion[];
}

/**
 * What searching the address book answered: its people by group, and whether a book the person holds went unread —
 * which a list of the other books' people does not say on its own.
 */
export interface ContactLookup {
    readonly groups: readonly SuggestionGroup[];
    readonly incomplete: boolean;
}

/** What a field lists: the conversation's people, then the book's groups that still hold anybody. */
export interface OfferedRecipients {
    readonly participants: readonly RecipientSuggestion[];
    readonly groups: readonly SuggestionGroup[];
}

/** The most a group lists: three while nothing is typed and six once something is, as the design draws it. */
export function mostInAGroup(term: string): number {
    return term.trim() === '' ? 3 : 6;
}

// Somebody one of the routes found: a contact in one of the books, or a member of the directory.
type Found = Contact | Colleague;

// The group each person found is offered in, by the book they were found in.
const groupOf: Readonly<Record<Found['book'], SuggestionGroupName>> = {
    Member: 'colleagues',
    Organization: 'organization',
    Own: 'own',
    Collected: 'own',
};

/**
 * Searches the address book's three routes for a term — the reader's own book with the organization's beside it, the
 * organization's members, and what their mail collected — as the groups the design draws them in.
 *
 * A person whose grants do not reach the book is offered nobody rather than told it failed: they have no book to search,
 * and a sentence saying so under every keystroke would be a refusal nobody asked about. Any other failure of a route is
 * said, because a list that leaves out a book that went unread reads as a book with nobody else in it. A reader in no
 * organization has an empty directory rather than a failed one, which is what that route answers them.
 */
export async function searchContacts(
    session: ClientSession,
    transport: MailFathomTransport,
    term: string,
): Promise<ContactLookup> {
    const most = mostInAGroup(term);
    const answers: readonly ClientResult<{ readonly contacts: readonly Found[] }>[] = await Promise.all([
        // The reader's own route answers two groups, the organization's contacts and their own.
        readOwnContacts(session, transport, { search: term, pageSize: most * 2 }),
        readColleagues(session, transport, { search: term, pageSize: most }).then((answer) =>
            answer.outcome === 'failed'
                ? answer
                : { outcome: 'read' as const, value: { contacts: answer.value.colleagues } },
        ),
        readCollectedContacts(session, transport, { search: term, pageSize: most }),
    ]);

    const found = answers.flatMap((answer) => (answer.outcome === 'read' ? answer.value.contacts : []));

    return {
        incomplete: answers.some((answer) => answer.outcome === 'failed' && answer.failure.reason !== 'unauthorized'),
        groups: suggestionGroupNames.map((name) => ({
            name,
            suggestions: found
                .filter((person) => groupOf[person.book] === name)
                .flatMap((person) => offeredAt(person, term)),
        })),
    };
}

/**
 * What a field lists for a term: the conversation's people who match it, then the book's groups, each address once and
 * none the header already holds, each group up to what the design lists of one and a group left with nobody left out.
 *
 * The book's answer is narrowed here as well as by the deployment, because it may be the answer to the term before the
 * last keystroke — and a person who typed one more letter should not be offered somebody that letter rules out while
 * the next answer is on its way.
 */
export function suggestionsFor(
    term: string,
    participants: readonly RecipientSuggestion[],
    fromTheBook: readonly SuggestionGroup[],
    addressed: readonly string[],
): OfferedRecipients {
    const taken = new Set(addressed.map(addressKey));
    const most = mostInAGroup(term);

    const narrowed = (suggestions: readonly RecipientSuggestion[]): readonly RecipientSuggestion[] => {
        const kept: RecipientSuggestion[] = [];

        for (const suggestion of suggestions) {
            if (kept.length < most && matches(suggestion, term) && !taken.has(addressKey(suggestion.address))) {
                taken.add(addressKey(suggestion.address));
                kept.push(suggestion);
            }
        }

        return kept;
    };

    return {
        participants: narrowed(participants),
        groups: fromTheBook
            .map((group) => ({ name: group.name, suggestions: narrowed(group.suggestions) }))
            .filter((group) => group.suggestions.length > 0),
    };
}

// Two spellings of one address are one person to offer, which is how the book itself compares them.
function addressKey(address: string): string {
    return address.toLowerCase();
}

// One person as the list offers them. Somebody found by one of their addresses is offered at that address, which is
// the one the person was typing; somebody found by their name is offered at the address they prefer — and a colleague
// who holds no address is not offered at all, there being nothing to write to.
function offeredAt(contact: Found, term: string): readonly RecipientSuggestion[] {
    const typed = term.trim() === '' ? [] : contact.addresses.filter((address) => contains(address, term));
    const addresses = typed.length > 0 ? typed : contact.preferredAddress === null ? [] : [contact.preferredAddress];

    return addresses.map((address) => ({ address, name: contact.displayName }));
}

function matches(suggestion: RecipientSuggestion, term: string): boolean {
    return contains(suggestion.address, term) || (suggestion.name !== null && contains(suggestion.name, term));
}

function contains(text: string, term: string): boolean {
    return text.toLocaleLowerCase().includes(term.trim().toLocaleLowerCase());
}
