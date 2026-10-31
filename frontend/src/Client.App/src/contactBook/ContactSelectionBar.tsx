// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { useAddressCopy } from './useAddressCopy';
import type { ContactBookName, Person } from './useContactBook';

// What stands where the toolbar stands while people are picked out, laid out as the mail list's own bar: the way out
// with how many are selected, the acts over the whole of them, and a way to take the whole book in at once.
//
// It replaces the toolbar rather than standing beside it, for the reason `mailSpace/SelectionBar.tsx` gives about its
// own: the two answer the same question, and a screen offering both would be asking a reader which of two rows their
// next press belongs to. It is drawn at every width, because a selection with no way to act on it or leave it is a
// state a person cannot get out of.
//
// **Leaving is the close control and nothing else.** It clears the selection rather than acting on it, which is what
// makes picking people out safe to do by accident.
//
// **The acts follow the book in front.** Writing to everybody picked out is offered in each; what takes them out of
// it is a deletion from the book somebody wrote and a removal from the one their mail collected, because the second
// fills itself again the next time they write. Nobody here may change a colleague, so under *Colleagues* copying their
// addresses stands where taking them out would; and under *Own* only the reader's own contacts can go, so deleting is
// not offered while every one picked out is the organization's.

// How many people are picked out, in the forms a language has for the noun. The mail list counts its own selection with
// the same four entries, which is deliberate: *how many are selected* is one sentence in this client rather than one
// per list.
const selectionCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'select.count.other',
    one: 'select.count.one',
    two: 'select.count.other',
    few: 'select.count.few',
    many: 'select.count.many',
    other: 'select.count.other',
};

/** What taking the people picked out of the book is called, in the two books anybody can be taken out of. */
const erasureCalled: Readonly<Record<Exclude<ContactBookName, 'colleagues'>, MessageKey>> = {
    own: 'people.deleteSelected',
    collected: 'people.removeSelected',
};

export function ContactSelectionBar({
    book,
    selected,
    erasable,
    onClear,
    onSelectAll,
    onAskErasure,
}: {
    /** The book the selection was made in, which decides what taking people out of it is. */
    readonly book: ContactBookName;

    /** The people picked out, in the order the list draws them. */
    readonly selected: readonly Person[];

    /** Whether this credential may take somebody out of the book at all. */
    readonly erasable: boolean;

    readonly onClear: () => void;

    /** Picks out everybody the book in front lists. */
    readonly onSelectAll: () => void;

    /** Raises the question the erasure stands behind, over everybody picked out the reader may take out. */
    readonly onAskErasure: () => void;
}) {
    const { locale, translate } = useLocalization();
    const composing = useComposing();
    const wide = useWideWorkspace();
    const copy = useAddressCopy();
    const addresses = selected.flatMap((person) => (person.preferredAddress === null ? [] : [person.preferredAddress]));

    // Taking people out writes, so while the server is out of reach it is drawn as a control that cannot act, named
    // by why.
    const writeRefused = useWriteRefusal();
    const erasure = book === 'colleagues' ? null : translate(erasureCalled[book]);
    const erasureRefused = erasure === null ? null : writeRefused(erasure);
    const takenOut = selected.some((person) => person.book === 'Own' || person.book === 'Collected');

    // Words beside the symbols where the composition has room for them, and the symbols alone where it has not, which
    // is how the design draws this bar on a phone. The name is on the control either way.
    const shape = wide ? 'selected' : 'selectedSymbol';

    return (
        <div
            role="toolbar"
            aria-label={translate('people.selectionBar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-accent-line bg-accent-soft px-3 py-2"
        >
            <Control label={translate('people.clearSelection')} icon="close" shape="selectedSymbol" onPress={onClear} />

            {/* Said rather than only drawn: a reader who picked four people out hears how many they are holding, and
                hears it change as they pick out a fifth. */}
            <p role="status" className="me-1.5 ps-0.5 text-base font-semibold whitespace-nowrap text-accent-deep">
                {translate(selectionCounted[new Intl.PluralRules(locale).select(selected.length)], {
                    count: new Intl.NumberFormat(locale).format(selected.length),
                })}
            </p>

            <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-accent-line" />

            {composing.offered && addresses.length > 0 ? (
                <Control
                    label={translate('people.write')}
                    icon="edit"
                    shape={shape}
                    onPress={() => {
                        composing.compose({ kind: 'new', to: addresses });
                    }}
                />
            ) : null}

            {book === 'colleagues' && addresses.length > 0 ? (
                <Control
                    label={translate('people.copyAddresses')}
                    icon="content_copy"
                    shape={shape}
                    onPress={() => {
                        copy(addresses);
                    }}
                />
            ) : null}

            {erasable && erasure !== null && takenOut ? (
                <Control
                    label={erasure}
                    icon="delete"
                    shape={shape}
                    {...(erasureRefused === null ? {} : { held: true, hint: erasureRefused })}
                    onPress={onAskErasure}
                />
            ) : null}

            {/* Against the far edge, and words at every width as the mail list's bar draws it: no symbol names it, so
                narrowed to one it would be a button with nothing on it. */}
            <Control label={translate('select.all')} shape="selected" className="ms-auto" onPress={onSelectAll} />
        </div>
    );
}
