// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type RefObject } from 'react';
import {
    contactPhoneLabels,
    mostPhoneNumbersPerContact,
    type ContactPhoneNumber,
    type PortraitImageType,
} from '@mailfathom/client-backend';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { dialogField, dialogFieldLabel } from '../controls/chrome';
import { chosenPortrait } from '../controls/chosenPortrait';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { SymbolControl } from '../controls/SymbolControl';
import { asDataUrl } from '../deployment/dataUrl';
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
import { looksLikeAPhoneNumber, phoneLabelSaid } from './phoneNumbers';

// Writing somebody down, and correcting somebody already written down, which are the two ways this client states a
// person in the book and one form: a photo, a name, every address they use with the one mail goes to by default, and
// their phone numbers. What it records is asserted by the person typing it, which is not a field on this form and never
// could be: the surface reads the origin off who is writing rather than off what they send. Amending opens on what the
// record holds, and *Save* stays flat until something in it differs.
//
// **The addresses are a radio group**, because choosing the default is choosing one out of several: the platform
// announces the set, reports which is in force, and moves between the rows with the arrow keys. What the rows may hold
// and which of them is the default are `contactAddresses.ts`'s rules rather than this markup's.
//
// **The phone numbers are rows**, each a label and the number as typed, added and removed one at a time in the order
// the design draws: the first added is *Mobile*, the next *Work*, and a row left empty is dropped on save rather than
// refused, because an empty row is a row nobody finished rather than a number somebody wrote.
//
// **The save is refused before the request where the client can tell**, which is an empty name, no address at all,
// text that is not shaped like an address, and a number that is not shaped like one. Everything else the book judges —
// an address another contact already holds above all — is an outcome the screen reports rather than a rule restated
// here.
//
// **The photo is a change the form carries rather than a write it makes.** A file chosen here is judged against the
// two bounds the deployment holds a photo to and drawn in the circle at once, but nothing is sent until *Save*: the
// contact has to exist before a photo can be stored under it, and a form somebody cancels leaves the record exactly as
// it was. What *Save* hands over is therefore whether the photo is kept, replaced by the file chosen, or removed.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on the control that opened it. Whether it is open is therefore the
// element's state rather than a second copy of it, which is why the caller hands over the reference.

/** What happens to the photo when the form is saved. */
export type ContactPhotoChange =
    | { readonly kind: 'kept' }
    | {
          readonly kind: 'chosen';
          readonly picture: Blob;
          readonly type: PortraitImageType;

          /** The address the chosen file is drawn at in the form. */
          readonly shown: string;
      }
    | { readonly kind: 'removed' };

/** What a person has typed into the form: the name, the address rows with the default among them, and the numbers. */
export interface ContactDraft {
    readonly displayName: string;
    readonly rows: AddressRows;

    /** The numbers in the order the rows stand, the preferred one first. */
    readonly phoneNumbers: readonly ContactPhoneNumber[];
}

/** What the record being corrected holds as the form opens on it. */
export interface ContactHeld extends ContactDraft {
    /** The address the person's photo is drawn at, or `null` where they have none or it has not been read yet. */
    readonly photo: string | null;
}

/** What the form is saved as, which is the whole of what a record this client writes carries beside its note. */
export interface ContactStatement {
    readonly displayName: string;
    readonly addresses: readonly string[];
    readonly preferredAddress: string;

    /** Trimmed, and without the rows nobody typed into. */
    readonly phoneNumbers: readonly ContactPhoneNumber[];
}

/** What *Save* hands over: the record as the form states it, whether that differs from the record held, and the photo. */
export interface ContactSave {
    readonly statement: ContactStatement;
    readonly restated: boolean;
    readonly photo: ContactPhotoChange;
}

const nothingWritten: ContactDraft = { displayName: '', rows: noAddressWritten, phoneNumbers: [] };

/** The numbers a draft would save: trimmed, and without the rows nobody typed into. */
function savedNumbers(phoneNumbers: readonly ContactPhoneNumber[]): ContactPhoneNumber[] {
    return phoneNumbers
        .map((phoneNumber) => ({ ...phoneNumber, number: phoneNumber.number.trim() }))
        .filter((phoneNumber) => phoneNumber.number !== '');
}

const photoKept: ContactPhotoChange = { kind: 'kept' };

/** Why a chosen file was not taken, with its size where that is the reason, since the sentence names it. */
type PhotoRefusal =
    { readonly reason: 'notAnImageKind' | 'notRead' } | { readonly reason: 'tooLarge'; readonly octets: number };

const octetsPerMegabyte = 1_048_576;

// The two photo controls are one shape, the design's small bordered button, whether it opens a file or removes one.
const photoControl =
    'flex cursor-pointer items-center gap-1.5 rounded-md border border-line-strong bg-panel px-3 py-1.75 text-base whitespace-nowrap text-text-soft transition hover:bg-hover has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-accent';

function statementOf(draft: ContactDraft): ContactStatement | null {
    const displayName = draft.displayName.trim();
    const saved = savedAddresses(draft.rows);
    const phoneNumbers = savedNumbers(draft.phoneNumbers);
    const numbersUsable = phoneNumbers.every((phoneNumber) => looksLikeAPhoneNumber(phoneNumber.number));

    return displayName === '' || saved === null || !numbersUsable ? null : { displayName, ...saved, phoneNumbers };
}

function sameStatement(one: ContactStatement, other: ContactStatement | null): boolean {
    return (
        other !== null &&
        one.displayName === other.displayName &&
        one.preferredAddress === other.preferredAddress &&
        one.addresses.length === other.addresses.length &&
        one.addresses.every((address, at) => address === other.addresses[at]) &&
        one.phoneNumbers.length === other.phoneNumbers.length &&
        one.phoneNumbers.every((phoneNumber, at) => {
            const held = other.phoneNumbers[at];

            return phoneNumber.number === held?.number && phoneNumber.label === held.label;
        })
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
    readonly amending?: ContactHeld | null;

    /** Records what was typed, run once the dialog has closed and focus has been restored. */
    readonly onSave: (save: ContactSave) => void;
}) {
    const { locale, translate } = useLocalization();
    const closesOnBack = useTopLayerSurface();
    const asks = useId();
    const names = useId();
    const addressesNamed = useId();
    const choices = useId();
    const numbering = useId();

    const opensOn = amending ?? nothingWritten;
    const [draft, setDraft] = useState<ContactDraft>(opensOn);
    const [photo, setPhoto] = useState<ContactPhotoChange>(photoKept);
    const [photoRefused, setPhotoRefused] = useState<PhotoRefusal | null>(null);

    const statement = statementOf(draft);
    const usable = !holdsUnusableAddress(draft.rows);
    const numbersUsable = savedNumbers(draft.phoneNumbers).every((phoneNumber) =>
        looksLikeAPhoneNumber(phoneNumber.number),
    );
    const restated = amending === null || statement === null || !sameStatement(statement, statementOf(amending));
    const savable = statement !== null && (restated || photo.kind !== 'kept');

    const heldPhoto = amending?.photo ?? null;
    const shownPhoto = photo.kind === 'chosen' ? photo.shown : photo.kind === 'removed' ? null : heldPhoto;

    function choosePhoto(picture: File | undefined): void {
        if (picture === undefined) {
            return;
        }

        const choice = chosenPortrait(picture.type, picture.size);

        if (choice.outcome !== 'admissible') {
            setPhotoRefused(
                choice.outcome === 'notAnImageKind'
                    ? { reason: 'notAnImageKind' }
                    : { reason: 'tooLarge', octets: picture.size },
            );

            return;
        }

        setPhotoRefused(null);

        // Drawn from the file itself, so the circle shows what *Save* will send rather than what is stored.
        void asDataUrl(picture).then(
            (shown) => {
                setPhoto({ kind: 'chosen', picture, type: choice.type, shown });
            },
            () => {
                setPhotoRefused({ reason: 'notRead' });
            },
        );
    }

    function removePhoto(): void {
        setPhotoRefused(null);

        // A photo chosen for somebody who has none stored is simply let go of: there is nothing to remove from them.
        setPhoto(heldPhoto === null ? photoKept : { kind: 'removed' });
    }

    // Saving writes the person into the book, so while the server is out of reach it says why it cannot act — over the
    // buttons, where a finger reads it as well as a mouse — and the dialog keeps everything typed into it.
    const saying = translate(amending === null ? 'people.saveContact' : 'people.saveChanges');
    const refused = useWriteRefusal()(saying);

    // A row somebody added is where they are about to type, so focus goes into it once it is drawn rather than staying
    // on the control that added it; a row removed takes its remove control with it, so focus goes to the row that
    // stands in its place, or to the one above where it was the last. Set by the press and read once the rows are
    // drawn, which is the one moment both hold.
    const rowsDrawn = useRef<HTMLFieldSetElement>(null);
    const rowToFocus = useRef<number | null>(null);
    const rowCount = draft.rows.addresses.length;

    useEffect(() => {
        if (rowToFocus.current !== null) {
            const rows = [...(rowsDrawn.current?.querySelectorAll<HTMLInputElement>('input[type="email"]') ?? [])];

            rows[Math.min(rowToFocus.current, rows.length - 1)]?.focus();
            rowToFocus.current = null;
        }
    }, [rowCount]);

    // The numbers follow the same rule, and a number removed where none remain hands focus to the control that adds one.
    const numbersDrawn = useRef<HTMLDivElement>(null);
    const addsNumber = useRef<HTMLButtonElement>(null);
    const numberToFocus = useRef<number | null>(null);
    const numberCount = draft.phoneNumbers.length;

    useEffect(() => {
        if (numberToFocus.current !== null) {
            const rows = [...(numbersDrawn.current?.querySelectorAll<HTMLInputElement>('input[type="tel"]') ?? [])];

            (rows[Math.min(numberToFocus.current, rows.length - 1)] ?? addsNumber.current)?.focus();
            numberToFocus.current = null;
        }
    }, [numberCount]);

    function revised(rows: AddressRows): void {
        setDraft({ ...draft, rows });
    }

    function withNumbers(phoneNumbers: readonly ContactPhoneNumber[]): void {
        setDraft({ ...draft, phoneNumbers });
    }

    return (
        <dialog
            onToggle={closesOnBack}
            ref={asked}
            aria-labelledby={asks}
            data-pull="down"
            className="max-workspace:surface-sheet workspace:surface-pop m-0 h-full max-h-full w-full max-w-full rounded-none border-0 bg-panel p-0 pt-safe-top pb-safe-bottom text-text open:flex open:flex-col backdrop:bg-scrim workspace:m-auto workspace:h-fit workspace:max-h-dialog-height workspace:w-dialog workspace:max-w-dialog-narrow workspace:rounded-lg workspace:border workspace:border-line workspace:shadow-dialog"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on and not every
                // engine clears it on the next `showModal`: an answer read twice would record the person again.
                dialog.returnValue = '';

                if (answer === 'save' && refused === null && statement !== null) {
                    onSave({ statement, restated, photo });
                }

                // The form is put back on the way out rather than on the way in, so a dialog opened again opens on the
                // record and nothing of what was typed outlives the question it was typed into.
                setDraft(opensOn);
                setPhoto(photoKept);
                setPhotoRefused(null);
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
                    <div className="flex items-center gap-3.5">
                        {shownPhoto === null ? (
                            <span
                                aria-hidden="true"
                                className="flex size-14 shrink-0 items-center justify-center rounded-full border border-dashed border-line-strong bg-rail text-faint"
                            >
                                <Icon name="add_a_photo" className="size-icon-2xl" />
                            </span>
                        ) : (
                            <img
                                src={shownPhoto}
                                alt={translate('people.contactPhoto')}
                                className="size-14 shrink-0 rounded-full bg-line-strong object-cover"
                            />
                        )}

                        <div className="flex min-w-0 flex-1 flex-col gap-1.5">
                            <div className="flex flex-wrap gap-2">
                                <label className={photoControl}>
                                    <Icon name="image" className="size-icon-sm" />
                                    {translate(shownPhoto === null ? 'people.choosePhoto' : 'people.changePhoto')}

                                    {/* The file input carries the label rather than being clicked by a handler, for the
                                        reason the settings screen gives about the portrait's: a label already names
                                        the input and opens it, from the keyboard as from a pointer. */}
                                    <input
                                        type="file"
                                        accept="image/jpeg,image/png"
                                        className="sr-only"
                                        onChange={(event) => {
                                            choosePhoto(event.target.files?.[0]);

                                            // Cleared so that picking the same file twice is a second choice.
                                            event.target.value = '';
                                        }}
                                    />
                                </label>

                                {shownPhoto === null ? null : (
                                    <button
                                        type="button"
                                        className={`${photoControl} hover:text-warning-text`}
                                        onClick={removePhoto}
                                    >
                                        {translate('people.removePhoto')}
                                    </button>
                                )}
                            </div>

                            <span className="text-sm text-muted text-pretty">{translate('people.photoBounds')}</span>

                            {photoRefused === null ? null : (
                                <span role="alert" className="text-sm text-error-text">
                                    {photoRefused.reason === 'tooLarge'
                                        ? translate('people.photoTooLarge', {
                                              size: new Intl.NumberFormat(locale, {
                                                  minimumFractionDigits: 1,
                                                  maximumFractionDigits: 1,
                                              }).format(photoRefused.octets / octetsPerMegabyte),
                                          })
                                        : translate(
                                              photoRefused.reason === 'notRead'
                                                  ? 'people.photoNotRead'
                                                  : 'people.photoNotAnImageKind',
                                          )}
                                </span>
                            )}
                        </div>
                    </div>

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
                                                rowToFocus.current = at;
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
                                rowToFocus.current = rowCount;
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

                    <div ref={numbersDrawn} role="group" aria-labelledby={numbering} className="flex flex-col gap-2">
                        <span id={numbering} className={dialogFieldLabel}>
                            {translate('people.contactPhoneNumbers')}
                        </span>

                        {draft.phoneNumbers.map((phoneNumber, position) => (
                            <PhoneNumberRow
                                // A row is its place in the order, which is what the service keys it by too.
                                key={position}
                                phoneNumber={phoneNumber}
                                onChange={(changedNumber) => {
                                    withNumbers(
                                        draft.phoneNumbers.map((held, at) => (at === position ? changedNumber : held)),
                                    );
                                }}
                                onRemove={() => {
                                    numberToFocus.current = position;
                                    withNumbers(draft.phoneNumbers.filter((_, at) => at !== position));
                                }}
                            />
                        ))}

                        {draft.phoneNumbers.length === 0 ? (
                            <span className="text-base text-faint">{translate('people.noPhoneNumbersYet')}</span>
                        ) : null}

                        {draft.phoneNumbers.length < mostPhoneNumbersPerContact ? (
                            <button
                                ref={addsNumber}
                                type="button"
                                className="flex items-center gap-1.5 self-start rounded-md border border-line-strong bg-panel px-3 py-1.75 text-base whitespace-nowrap text-text-soft transition hover:bg-hover"
                                onClick={() => {
                                    numberToFocus.current = numberCount;
                                    withNumbers([
                                        ...draft.phoneNumbers,
                                        { label: draft.phoneNumbers.length === 0 ? 'Mobile' : 'Work', number: '' },
                                    ]);
                                }}
                            >
                                <Icon name="add" className="size-icon-md" />
                                {translate('people.addPhoneNumber')}
                            </button>
                        ) : null}

                        {numbersUsable ? null : (
                            <p role="alert" className="text-sm text-warning-text text-pretty">
                                {translate('people.phoneNotUsable')}
                            </p>
                        )}
                    </div>

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

/** One number on the form: which phone it reaches, the number, and the control that takes the row away. */
function PhoneNumberRow({
    phoneNumber,
    onChange,
    onRemove,
}: {
    readonly phoneNumber: ContactPhoneNumber;
    readonly onChange: (phoneNumber: ContactPhoneNumber) => void;
    readonly onRemove: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex items-center gap-2">
            <select
                aria-label={translate('people.phoneLabel')}
                value={phoneNumber.label}
                className="h-9.5 w-26 shrink-0 rounded-md pointer-coarse:w-28 border border-line-strong bg-sunken px-2 text-md text-text outline-none focus:border-accent"
                onChange={(choosing) => {
                    const chosen = contactPhoneLabels.find((label) => label === choosing.target.value);

                    if (chosen !== undefined) {
                        onChange({ ...phoneNumber, label: chosen });
                    }
                }}
            >
                {contactPhoneLabels.map((label) => (
                    <option key={label} value={label}>
                        {translate(phoneLabelSaid[label])}
                    </option>
                ))}
            </select>

            <input
                type="tel"
                aria-label={translate('people.phoneNumber')}
                placeholder={translate('people.phoneNumberPlaceholder')}
                value={phoneNumber.number}
                className={`${dialogField} min-w-0 flex-1`}
                onChange={(typing) => {
                    onChange({ ...phoneNumber, number: typing.target.value });
                }}
            />

            <SymbolControl
                label={translate('people.removePhoneNumber')}
                className="flex size-9 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                onPress={onRemove}
            >
                <Icon name="close" className="size-icon-md" />
            </SymbolControl>
        </div>
    );
}
