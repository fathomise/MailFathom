// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    largestAgentFile,
    mostFilesPerQuestion,
    takeAgentConversationFile,
    type AgentFileRefusal,
    type ClientSession,
} from '@mailfathom/client-backend';
import { useAttachmentUpload } from '../deployment/attachmentUpload';

// The files a question will carry, from the moment somebody hands one over until the question leaves with them. Every
// way in — the control, a drop, a paste — goes through `add`, so the limits are checked once and a refusal reads the
// same however the file arrived. A file is taken into the conversation as soon as it is handed over, so a question waits
// on nothing but its own uploads; what leaves with it is the identifier each upload answered with.
//
// The checks here are the client's reading of what the service will refuse — by name for the kind, since only the
// service reads what a file holds — so a refusal the service still gives is one of its own, said in its own words.

/** The extensions the agent reads: PDF, Office, OpenDocument and text files, saved messages, and pictures. */
const readKinds: ReadonlySet<string> = new Set([
    'pdf',
    'docx',
    'xlsx',
    'pptx',
    'odt',
    'ods',
    'odp',
    'txt',
    'csv',
    'md',
    'eml',
    'png',
    'jpg',
    'jpeg',
    'webp',
    'gif',
]);

/** What the system's file chooser is asked to offer, which is the same list. */
export const acceptedFiles = [...readKinds].map((extension) => `.${extension}`).join(',');

/**
 * The most a file may hold, said as the service says it: the service bounds a file in binary megabytes and calls them
 * MB, so the decimal sizes every other screen reads would put its own round limit as 16.8 MB.
 */
export function largestFileWorded(locale: string): string {
    return new Intl.NumberFormat(locale, { style: 'unit', unit: 'megabyte', unitDisplay: 'short' }).format(
        largestAgentFile / (1024 * 1024),
    );
}

/** One file waiting under the field. */
export interface WaitingFile {
    readonly key: number;
    readonly name: string;
    readonly mediaType: string;
    readonly octets: number;
    readonly pasted: boolean;
    readonly state: 'uploading' | 'uploaded' | 'failed';

    /** What the conversation holds it as, once it is uploaded. */
    readonly fileId: string | null;
}

/** Why a file was not attached, each its own sentence on the screen. */
export type FileRefusal =
    | { readonly reason: 'kind'; readonly name: string; readonly extension: string }
    | { readonly reason: 'tooMany'; readonly count: number }
    | { readonly reason: AgentFileRefusal; readonly name: string; readonly octets: number };

export interface QuestionFiles {
    readonly waiting: readonly WaitingFile[];

    /** The refusals of the last attempt, which the next attempt replaces. */
    readonly refusals: readonly FileRefusal[];
    /**
     * Hands files over, checking each against what a question may carry.
     *
     * @param pastedAs What a picture pasted with no name of its own is called, already in the reader's language and
     * without its extension, and `null` for files that were chosen or dropped rather than pasted.
     */
    readonly add: (files: readonly File[], pastedAs: string | null) => void;
    readonly remove: (key: number) => void;
    readonly retry: (key: number) => void;
    readonly dismissRefusals: () => void;

    /**
     * Lets go of the files a question left with, and of the last refusals; a file it did not carry — one still on its
     * way, or one taken in while it was being posted — keeps waiting for the next.
     */
    readonly sent: (carried: readonly string[]) => void;
}

/**
 * @param conversation The conversation the files are taken into — the one in front, or the identifier a new one will be
 * started under. Moving to another puts the waiting files down, as it puts down what was typed.
 */
export function useQuestionFiles(session: ClientSession | null, conversation: string): QuestionFiles {
    const upload = useAttachmentUpload();
    const [waiting, setWaiting] = useState<readonly WaitingFile[]>([]);
    const [refusals, setRefusals] = useState<readonly FileRefusal[]>([]);
    const [heldFor, setHeldFor] = useState(conversation);
    const nextKey = useRef(0);

    // The octets and the way to abandon each upload, which no render reads.
    const handedOver = useRef(new Map<number, { readonly file: File; abandon: AbortController }>());

    // Adjusted while rendering, so the files of one conversation are never drawn under another.
    if (heldFor !== conversation) {
        setHeldFor(conversation);
        setWaiting([]);
        setRefusals([]);
    }

    // The uploads into a conversation stop when it is put down, which is the browser's request to abandon.
    useEffect(() => {
        const held = handedOver.current;

        return () => {
            for (const { abandon } of held.values()) {
                abandon.abort();
            }

            held.clear();
        };
    }, [conversation]);

    const settle = (key: number, change: Partial<WaitingFile>): void => {
        setWaiting((before) => before.map((file) => (file.key === key ? { ...file, ...change } : file)));
    };

    const start = (key: number, file: File, name: string): void => {
        const abandon = new AbortController();

        handedOver.current.set(key, { file, abandon });

        if (session === null) {
            settle(key, { state: 'failed' });

            return;
        }

        void takeAgentConversationFile(session, conversation, name, (request) =>
            upload(request, file, abandon.signal),
        ).then((taken) => {
            if (abandon.signal.aborted) {
                return;
            }

            if (taken.outcome === 'failed') {
                settle(key, { state: 'failed' });
            } else if (taken.value.outcome === 'taken') {
                settle(key, { state: 'uploaded', fileId: taken.value.file.fileId });
            } else {
                const refusal = taken.value.refusal;

                handedOver.current.delete(key);
                setWaiting((before) => before.filter((held) => held.key !== key));
                setRefusals((before) => [...before, { reason: refusal, name, octets: file.size }]);
            }
        });
    };

    const add = (files: readonly File[], pastedAs: string | null): void => {
        if (files.length === 0) {
            return;
        }

        const refused: FileRefusal[] = [];
        const taken: { key: number; file: File; name: string }[] = [];
        let over = 0;

        for (const file of files) {
            const name = pastedAs === null ? file.name : pastedName(file, pastedAs);
            const extension = extensionOf(name);

            if (!readKinds.has(extension)) {
                refused.push({ reason: 'kind', name, extension });
            } else if (file.size > largestAgentFile) {
                refused.push({ reason: 'tooLarge', name, octets: file.size });
            } else if (file.size === 0) {
                refused.push({ reason: 'empty', name, octets: 0 });
            } else if (waiting.length + taken.length >= mostFilesPerQuestion) {
                over += 1;
            } else {
                taken.push({ key: nextKey.current++, file, name });
            }
        }

        if (over > 0) {
            refused.push({ reason: 'tooMany', count: over });
        }

        setRefusals(refused);
        setWaiting((before) => [
            ...before,
            ...taken.map(({ key, file, name }) => ({
                key,
                name,
                mediaType: file.type,
                octets: file.size,
                pasted: pastedAs !== null,
                state: 'uploading' as const,
                fileId: null,
            })),
        ]);

        for (const { key, file, name } of taken) {
            start(key, file, name);
        }
    };

    return {
        waiting,
        refusals,
        add,
        remove: (key) => {
            handedOver.current.get(key)?.abandon.abort();
            handedOver.current.delete(key);
            setWaiting((before) => before.filter((file) => file.key !== key));
        },
        retry: (key) => {
            const held = handedOver.current.get(key);
            const file = waiting.find((waitingFile) => waitingFile.key === key);

            if (held !== undefined && file !== undefined) {
                settle(key, { state: 'uploading' });
                start(key, held.file, file.name);
            }
        },
        dismissRefusals: () => {
            setRefusals([]);
        },
        sent: (carried) => {
            const left = (file: WaitingFile): boolean => file.fileId !== null && carried.includes(file.fileId);

            for (const file of waiting.filter(left)) {
                handedOver.current.delete(file.key);
            }

            setWaiting((before) => before.filter((file) => !left(file)));
            setRefusals([]);
        },
    };
}

function extensionOf(name: string): string {
    return /\.([A-Za-z0-9]{1,8})$/u.exec(name)?.[1]?.toLowerCase() ?? '';
}

/**
 * A pasted picture with no name of its own takes the one it was given, which is what a browser writes as `image.png`
 * for one copied out of another application; a file pasted out of a file manager keeps its name.
 */
function pastedName(file: File, pastedAs: string): string {
    if (file.name !== '' && !/^image\.\w+$/u.test(file.name)) {
        return file.name;
    }

    return `${pastedAs}.${extensionOf(file.name) || 'png'}`;
}
