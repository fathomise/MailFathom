// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type RefObject } from 'react';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import {
    holdsUnusableAddress,
    noAddressWritten,
    savedAddresses,
    withAddressAdded,
    withAddressRemoved,
    withAddressTyped,
    withDefault,
    type AddressRows,
} from './contactAddresses';

// Writing somebody down, and correcting somebody already written down, which are the two ways this client states a
// person in the book and one form: a name, and every address they use with the one mail goes to by default. What it
// records is asserted by the person typing it, which is not a field on this form and never could be: the surface reads
// the origin off who is writing rather than off what they send. Amending opens on what the record holds, and *Save*
// stays flat until something in it differs.
//
// **The name and the addresses rather than everything the design draws.** The design's dialog also takes a photo and
// phone numbers, and a contact record on this deployment holds neither, so a form offering them would be asking for
// values no request could carry and no screen could ever draw back.
//
// **The addresses are a radio group**, because choosing the default is choosing one out of several: the platform
// announces the set, reports which is in force, and moves between the rows with the arrow keys. What the rows may hold
// and which of them is the default are `contactAddresses.ts`'s rules rather than this markup's.
//
// **The save is refused before the request where the client can tell**, which is an empty name, no address at all, and
// text that is not shaped like an address. Everything else the book judges — an address another contact already holds
// above all — is an outcome the screen reports rather than a rule restated here.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.

/** What a person has typed into the form: the name, and the address rows with the default among them. */
export interface ContactDraft {
    readonly displayName: string;
    readonly rows: AddressRows;
}

/** What the form is saved as, which is the whole of what a record this client writes carries beside its note. */
export interface ContactStatement {
    readonly displayName: string;
    readonly addresses: readonly string[];
    readonly preferredAddress: string;
}

const nothingWritten: ContactDraft = { displayName: '', rows: noAddressWritten };

function statementOf(draft: ContactDraft): ContactStatement | null {
    const displayName = draft.displayName.trim();
    const saved = savedAddresses(draft.rows);

    return displayName === '' || saved === null ? null : { displayName, ...saved };
}

function sameStatement(one: ContactStatement, other: ContactStatement | null): boolean {
    return (
        other !== null &&
        one.displayName === other.displayName &&
        one.preferredAddress === other.preferredAddress &&
        one.addresses.length === other.addresses.length &&
        one.addresses.every((address, at) => address === other.addresses[at])
    );
}

export function ContactDialog({
    asked,
    amending = null,
    onSave,
}: {
    /** The dialog itself, held by the caller so that every way of opening it reaches the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** What the record being corrected holds, or `null` where somebody is being written down for the first time. */
    readonly amending?: ContactDraft | null;

    /** Records what was typed, run once the dialog has closed and focus has been restored. */
    readonly onSave: (statement: ContactStatement) => void;
}) {
    const { translate } = useLocalization();
    const closesOnBack = useTopLayerSurface();
    const asks = useId();
    const names = useId();
    const addressesNamed = useId();
    const choices = useId();

    const opensOn = amending ?? nothingWritten;
    const [draft, setDraft] = useState<ContactDraft>(opensOn);

    const statement = statementOf(draft);
    const usable = !holdsUnusableAddress(draft.rows);
    const changed = amending === null || statement === null || !sameStatement(statement, statementOf(amending));
    const savable = statement !== null && changed;

    // Saving writes the person into the book, so while the server is out of reach it says why it cannot act — over the
    // buttons, where a finger reads it as well as a mouse — and the dialog keeps everything typed into it.
    const saying = translate(amending === null ? 'people.saveContact' : 'people.saveChanges');
    const refused = useWriteRefusal()(saying);

    // A row somebody added is where they are about to type, so focus goes into it once it is drawn rather than staying
    // on the control that added it. Set by the press and read once the row exists, which is the one moment both hold.
    const rowsDrawn = useRef<HTMLFieldSetElement>(null);
    const rowAdded = useRef(false);
    const rowCount = draft.rows.addresses.length;

    useEffect(() => {
        if (rowAdded.current) {
            rowAdded.current = false;
            [...(rowsDrawn.current?.querySelectorAll<HTMLInputElement>('input[type="email"]') ?? [])].at(-1)?.focus();
        }
    }, [rowCount]);

    function revised(rows: AddressRows): void {
        setDraft({ ...draft, rows });
    }

    return (
        <dialog
            onToggle={closesOnBack}
            ref={asked}
            aria-labelledby={asks}
            data-pull="down"
            className="max-workspace:surface-sheet workspace:surface-pop m-0 h-full max-h-full w-full max-w-full rounded-none border-0 bg-panel p-0 pt-safe-top pb-safe-bottom text-text open:flex open:flex-col backdrop:bg-scrim workspace:m-auto workspace:h-auto workspace:max-h-dialog-tall workspace:w-dialog workspace:max-w-dialog-narrow workspace:rounded-lg workspace:border workspace:border-line workspace:shadow-dialog"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would record the person again.
                dialog.returnValue = '';

                if (answer === 'save' && refused === null && statement !== null) {
                    onSave(statement);
                }

                // The form is put back on the way out rather than on the way in, so a dialog opened again opens on the
                // record and nothing of what was typed outlives the question it was typed into.
                setDraft(opensOn);
            }}
        >
            <div aria-hidden="true" className="flex h-5 shrink-0 items-center justify-center workspace:hidden">
                <span className="h-1 w-9.5 rounded-full bg-line-strong" />
            </div>

            <form
                className="flex min-h-0 flex-1 flex-col"
                onSubmit={(submitting) => {
                    // Closed by hand rather than by `method="dialog"`, because Enter in a field submits with no button
                    // behind it — and a form closed that way answers with nothing, which would drop a save somebody
                    // made from the keyboard.
                    submitting.preventDefault();

                    if (savable && refused === null) {
                        asked.current?.close('save');
                    }
                }}
            >
                <div className="flex shrink-0 items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                    <h2 id={asks} className="min-w-0 flex-1 truncate text-lg font-semibold">
                        {translate(amending === null ? 'people.newContact' : 'people.editContact')}
                    </h2>

                    <SurfaceControl
                        label={translate('people.closeNewContact')}
                        icon="close"
                        onActivate={() => {
                            asked.current?.close();
                        }}
                    />
                </div>

                <div className="flex min-h-0 flex-col gap-3.5 overflow-y-auto px-4.5 py-4.5">
                    <div className="flex flex-col gap-1.5">
                        <label htmlFor={names} className={dialogFieldLabel}>
                            {translate('people.contactName')}
                        </label>

                        <input
                            id={names}
                            type="text"
                            autoFocus
                            value={draft.displayName}
                            className={dialogField}
                            onChange={(typing) => {
                                setDraft({ ...draft, displayName: typing.target.value });
                            }}
                        />
                    </div>

                    <fieldset ref={rowsDrawn} aria-labelledby={addressesNamed} className="flex flex-col gap-2">
                        <legend id={addressesNamed} className={`${dialogFieldLabel} mb-1.5`}>
                            {translate('people.contactAddresses')}
                        </legend>

                        {draft.rows.addresses.map((address, at) => {
                            const preferred = at === draft.rows.preferred;

                            return (
                                // Keyed by position, because a row is a place somebody types rather than an address
                                // the book holds: two rows may hold the same text, and a row just added holds none.
                                <div key={at} className="flex items-center gap-2">
                                    <label className="relative flex size-8 shrink-0 cursor-pointer items-center justify-center rounded-md hover:bg-hover has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent">
                                        <input
                                            type="radio"
                                            name={choices}
                                            checked={preferred}
                                            aria-label={translate(
                                                preferred ? 'people.defaultAddress' : 'people.makeDefaultAddress',
                                            )}
                                            className="sr-only"
                                            onChange={() => {
                                                revised(withDefault(draft.rows, at));
                                            }}
                                        />
                                        <Icon
                                            name={preferred ? 'radio_button_checked' : 'radio_button_unchecked'}
                                            className={`size-icon-lg ${preferred ? 'text-accent' : 'text-muted'}`}
                                        />
                                    </label>

                                    <input
                                        type="email"
                                        value={address}
                                        aria-label={translate('people.contactAddress')}
                                        className={`${dialogField} min-w-0 flex-1`}
                                        onChange={(typing) => {
                                            revised(withAddressTyped(draft.rows, at, typing.target.value));
                                        }}
                                    />

                                    {preferred ? (
                                        <span className="shrink-0 rounded-sm bg-accent-soft px-1.5 py-0.5 text-xs font-semibold text-accent-deep">
                                            {translate('people.defaultChip')}
                                        </span>
                                    ) : null}

                                    {rowCount > 1 ? (
                                        <Control
                                            label={translate('people.removeAddress')}
                                            icon="close"
                                            shape="symbol"
                                            onPress={() => {
                                                revised(withAddressRemoved(draft.rows, at));
                                            }}
                                        />
                                    ) : null}
                                </div>
                            );
                        })}

                        <Control
                            label={translate('people.addAddress')}
                            icon="add"
                            shape="outlined"
                            className="self-start"
                            onPress={() => {
                                rowAdded.current = true;
                                revised(withAddressAdded(draft.rows));
                            }}
                        />
                    </fieldset>

                    {/* Said only once there is something to judge, because a complaint under a field nobody has
                        finished typing in is the client complaining about a state it put them in. The button is flat
                        meanwhile, which is what the design draws instead. */}
                    {usable ? null : (
                        <p role="alert" className="text-sm text-warning-text text-pretty">
                            {translate('people.addressNotUsable')}
                        </p>
                    )}

                    {amending === null ? (
                        <p className="rounded-md bg-accent-soft px-3 py-2.5 text-sm text-accent-deep text-pretty">
                            {translate('people.newContactHint')}
                        </p>
                    ) : null}
                </div>

                {refused === null ? null : (
                    <p role="status" className="px-4.5 pb-2.25 text-sm text-muted text-pretty">
                        {refused}
                    </p>
                )}

                <div className="flex flex-wrap justify-end gap-2.25 px-4.5 pb-4.5">
                    <button
                        type="button"
                        className={mannerDrawn.back}
                        onClick={() => {
                            asked.current?.close();
                        }}
                    >
                        {translate('act.cancel')}
                    </button>

                    <button
                        type="submit"
                        disabled={!savable}
                        aria-disabled={refused === null ? undefined : true}
                        className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100 aria-disabled:cursor-not-allowed aria-disabled:opacity-60`}
                    >
                        {saying}
                    </button>
                </div>
            </form>
        </dialog>
    );
}
