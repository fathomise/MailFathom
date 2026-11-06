// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailDraftAnswer } from '@mailfathom/client-backend';
import { mostRecipientsInOneHeader, type Composition } from './composition';
import {
    base64Of,
    isPictureName,
    isPictureSide,
    mostPictureOctets,
    mostPictures,
    mostPicturesOctets,
    octetsOf,
    pictureIn,
    type HeldPicture,
} from './pictures';
import { picturesNamedIn, writtenTextIn, type WrittenNode } from './writtenText';

// Where what somebody is typing survives a reload, which a single-page application makes a cold start rather than a
// way out. It is the local draft the composer keeps continuously, and it is a different thing from the draft in the
// user's own drafts folder: saving to the deployment is an act somebody asks for, because every revision of that one
// reaches their mail server.
//
// **The session's store rather than the machine's, and this is the reason it is not the device store beside it.** What
// is kept here is mail somebody is writing — words, a subject, and the addresses they are for — which is personal data
// under the same rules as the mail already on the screen. A store that dies with the tab keeps it out of a machine two
// people share and out of anything reading the origin's storage tomorrow, and signing out drops it in the same act that
// empties the workspace. It is the same bound the web head keeps its credential under, for a related reason.
//
// Reached as `window.sessionStorage` rather than as the bare global for the reason `device/deviceStore.ts` gives: Node
// publishes stores of its own that win over the document's under the test runner.
const storageKey = 'mailfathom.composition';

// The pictures a kept composition names, under a key of their own beside it, as ADR 0047 Question 4 states: a store
// that refuses a few megabytes of pictures must not take the words, the recipients, and the subject down with them, so
// the two are written apart and the platform's own quota decides what survives a reload.
const picturesKey = 'mailfathom.composition.pictures';

// What a draft the deployment wrote replaced, kept beside the composition while nobody has accepted or given up that
// draft. Its presence is what says the words are not yet the author's own, so a reload keeps the way back, the caution
// before a send, and each link's host beside its words rather than handing the draft over as written by hand.
const replacedStorageKey = 'mailfathom.composition.replacedByDraft';

// What a kept composition may carry before it is read as somebody's edit rather than as this client's own writing. The
// address is the longest one the mail standards allow and the subject is a header line, each far above anything the
// composer itself writes there. What the words are held to is `writtenText.ts`'s own, that being where the shape they
// are kept in is stated — and for them the shape is the bound that matters, a written message being the one thing here
// that goes back on the screen as elements rather than as text.
const longestAddress = 320;
const longestSubject = 998;
const longestIdentifier = 256;
const longestLabel = 256;

const answers: readonly MailDraftAnswer[] = ['senderOnly', 'everyone', 'forward'];

/** What this tab was writing, or `null` where it was writing nothing or what was kept is not a composition. */
export function rememberedComposition(): Composition | null {
    let stored: string | null;

    try {
        stored = window.sessionStorage.getItem(storageKey);
    } catch {
        return null;
    }

    if (stored === null) {
        return null;
    }

    try {
        return compositionIn(JSON.parse(stored), rememberedPictures());
    } catch {
        return null;
    }
}

/** Keeps what this tab is writing, so a reload returns to it rather than to an empty message. */
export function rememberComposition(composition: Composition): void {
    try {
        // The pictures are kept apart, by `rememberPictures`, and the words name each by the name it is kept under.
        window.sessionStorage.setItem(storageKey, JSON.stringify({ ...composition, pictures: undefined }));
    } catch {
        // A browser refusing storage still runs the client. What is being written then lasts as long as the screen
        // holding it, which is what a composer that failed over a store would have lost anyway.
    }
}

/**
 * Keeps the pictures the words of what this tab is writing name, beside the words.
 *
 * The write is attempted and its refusal caught. **A refused write removes the pictures kept before it**, so octets kept
 * for an older tree are never read back against a newer one: a reload then reads the words without the pictures, which
 * `writtenTextIn` drops rather than refuses.
 *
 * @param alsoNamedIn Words beside the composition whose pictures are kept too: what a draft nobody has accepted replaced,
 * so the way back a reload keeps brings the person's own pictures back with their words.
 * @returns Whether the pictures survive a reload, which `picturesKeptHereOnly` then answers the other way round.
 */
export function rememberPictures(composition: Composition, alsoNamedIn: readonly WrittenNode[] = []): boolean {
    const kept = keepPictures(composition, alsoNamedIn);

    noteRefusedPictures(!kept);

    return kept;
}

function noteRefusedPictures(refused: boolean): void {
    if (refusedPictures !== refused) {
        refusedPictures = refused;

        for (const listener of keepingListeners) {
            listener();
        }
    }
}

// Whether the store refused the last pictures it was handed, which is this module's to know because the store is
// outside React: the composer subscribes to it rather than copying an answer into state of its own.
let refusedPictures = false;
const keepingListeners = new Set<() => void>();

/** Whether the store refused the pictures of what this tab is writing, so a reload would lose them. */
export function picturesKeptHereOnly(): boolean {
    return refusedPictures;
}

/** Subscribes to `picturesKeptHereOnly` changing, in the shape `useSyncExternalStore` takes. */
export function subscribeToPictureKeeping(listener: () => void): () => void {
    keepingListeners.add(listener);

    return () => {
        keepingListeners.delete(listener);
    };
}

function keepPictures(composition: Composition, alsoNamedIn: readonly WrittenNode[]): boolean {
    const names = new Set([...picturesNamedIn(composition.words), ...picturesNamedIn(alsoNamedIn)]);
    const kept = [...names].flatMap((name) => {
        const picture = composition.pictures.get(name);

        return picture === undefined
            ? []
            : [
                  {
                      name,
                      content: base64Of(picture.octets),
                      width: picture.width,
                      height: picture.height,
                      // Held to the bound it is read back by, so one long name cannot cost every picture a reload.
                      label: picture.label?.slice(0, longestLabel) ?? null,
                  },
              ];
    });

    try {
        if (kept.length === 0) {
            window.sessionStorage.removeItem(picturesKey);
        } else {
            window.sessionStorage.setItem(picturesKey, JSON.stringify(kept));
        }

        return true;
    } catch {
        try {
            window.sessionStorage.removeItem(picturesKey);
        } catch {
            // A store that refuses a removal as well holds nothing a reload could read back against the new words.
        }

        return false;
    }
}

/**
 * What a draft nobody has accepted yet replaced in what this tab was writing, or `null` where no such draft stands.
 *
 * The pictures they name are kept beside the draft's own, so they come back with them; one the store could not keep is
 * dropped from them rather than read back as a name nothing holds.
 */
export function rememberedReplacedByDraft(): readonly WrittenNode[] | null {
    try {
        const stored = window.sessionStorage.getItem(replacedStorageKey);

        return stored === null
            ? null
            : writtenTextIn(JSON.parse(stored), (picture) => rememberedPictures().has(picture));
    } catch {
        return null;
    }
}

/** Keeps what a standing draft replaced, or drops it once the draft is accepted or given up. */
export function rememberReplacedByDraft(replaced: readonly WrittenNode[] | null): void {
    try {
        if (replaced === null) {
            window.sessionStorage.removeItem(replacedStorageKey);
        } else {
            window.sessionStorage.setItem(replacedStorageKey, JSON.stringify(replaced));
        }
    } catch {
        // A refused store loses only the standing over a reload, which is what a composer with no store had anyway.
    }
}

/** Drops what was being written, which is what sending it, giving it up, and signing out each do. */
export function forgetComposition(): void {
    // The refusal was about the pictures of what was being written, so it goes with them.
    noteRefusedPictures(false);

    try {
        window.sessionStorage.removeItem(storageKey);
        window.sessionStorage.removeItem(picturesKey);
        window.sessionStorage.removeItem(replacedStorageKey);
    } catch {
        // A store that refuses a removal refused the write that would have put something there.
    }
}

// The pictures kept beside the words, read back as untrusted input. Anything this client did not write reads as no
// pictures at all, which costs the pictures and nothing else: the words drop each picture they name and keep the rest.
function rememberedPictures(): ReadonlyMap<string, HeldPicture> {
    try {
        const stored = window.sessionStorage.getItem(picturesKey);

        return stored === null ? new Map() : (picturesIn(JSON.parse(stored)) ?? new Map());
    } catch {
        return new Map();
    }
}

function picturesIn(value: unknown): ReadonlyMap<string, HeldPicture> | null {
    if (!Array.isArray(value) || value.length > mostPictures) {
        return null;
    }

    const pictures = new Map<string, HeldPicture>();
    let together = 0;

    for (const entry of value) {
        const picture = keptPictureIn(entry);

        if (picture === null || pictures.has(picture.name)) {
            return null;
        }

        together += picture.held.octets.length;
        pictures.set(picture.name, picture.held);
    }

    return together > mostPicturesOctets ? null : pictures;
}

function keptPictureIn(entry: unknown): { readonly name: string; readonly held: HeldPicture } | null {
    if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) {
        return null;
    }

    const { name, content, width, height, label } = entry as Record<string, unknown>;
    const octets = typeof content === 'string' ? octetsOf(content) : null;
    const read = octets === null || octets.length > mostPictureOctets ? null : pictureIn(octets);
    const labelled =
        label === null ? null : typeof label === 'string' && label.length <= longestLabel ? label : undefined;

    if (
        typeof name !== 'string' ||
        !isPictureName(name) ||
        octets === null ||
        read === null ||
        !isPictureSide(width) ||
        !isPictureSide(height) ||
        labelled === undefined
    ) {
        return null;
    }

    return { name, held: { octets, type: read.type, width, height, label: labelled } };
}

// Read back as untrusted input, because a store is a place a person can write. Anything this client did not write is
// answered as nothing kept rather than as a message with a hole in it — which is what would otherwise reach the
// confirmation as a recipient nobody typed.
function compositionIn(value: unknown, pictures: ReadonlyMap<string, HeldPicture>): Composition | null {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return null;
    }

    const record = value as Record<string, unknown>;
    const account = record['account'];
    const subject = record['subject'];
    const words = writtenTextIn(record['words'], (picture) => pictures.has(picture));
    const answering = answeringIn(record['answering'] ?? null);
    const continuing = record['continuing'] ?? null;
    const to = addressesIn(record['to']);
    const cc = addressesIn(record['cc']);
    const bcc = addressesIn(record['bcc']);

    // An absent request reads as off, so a tab reloaded onto this client from one that kept neither still gets its words
    // back. A null, or anything else but a yes or a no, is a shape this client did not write.
    const requestsDeliveryNotification =
        record['requestsDeliveryNotification'] === undefined ? false : record['requestsDeliveryNotification'];
    const requestsReadReceipt = record['requestsReadReceipt'] === undefined ? false : record['requestsReadReceipt'];

    if (typeof requestsDeliveryNotification !== 'boolean' || typeof requestsReadReceipt !== 'boolean') {
        return null;
    }

    if (answering === undefined || to === null || cc === null || bcc === null) {
        return null;
    }

    if (typeof account !== 'string' || account.length > longestIdentifier) {
        return null;
    }

    if (typeof subject !== 'string' || subject.length > longestSubject) {
        return null;
    }

    if (words === null) {
        return null;
    }

    // An answer and a draft carried on are two messages to have started from, and nothing here writes both at once —
    // so a value carrying both is a shape this client did not write, whatever each half of it says on its own.
    if (
        continuing !== null &&
        (answering !== null || typeof continuing !== 'string' || continuing.length > longestIdentifier)
    ) {
        return null;
    }

    return {
        answering,
        continuing,
        account,
        subject,
        to,
        cc,
        bcc,
        words,
        pictures,
        requestsDeliveryNotification,
        requestsReadReceipt,
    };
}

// Answers `undefined` for a shape it refuses, because `null` is what a message of its own legitimately kept.
function answeringIn(value: unknown): Composition['answering'] | undefined {
    if (value === null) {
        return null;
    }

    if (typeof value !== 'object' || Array.isArray(value)) {
        return undefined;
    }

    const record = value as Record<string, unknown>;
    const storedEmailId = record['storedEmailId'];
    const answered = record['answers'];

    if (typeof storedEmailId !== 'string' || storedEmailId.length > longestIdentifier) {
        return undefined;
    }

    return answers.includes(answered as MailDraftAnswer)
        ? { storedEmailId, answers: answered as MailDraftAnswer }
        : undefined;
}

function addressesIn(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > mostRecipientsInOneHeader) {
        return null;
    }

    const addresses: string[] = [];

    for (const address of value) {
        if (typeof address !== 'string' || address.length === 0 || address.length > longestAddress) {
            return null;
        }

        addresses.push(address);
    }

    return addresses;
}
