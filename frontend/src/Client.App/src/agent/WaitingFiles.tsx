// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { mostFilesPerConversation, mostFilesPerQuestion } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import type { MessageKey } from '../localization/en';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import { FileKind } from './FileKind';
import { largestFileWorded, type FileRefusal, type QuestionFiles, type WaitingFile } from './questionFiles';

const tooMany: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'agent.files.refused.tooMany.other',
    one: 'agent.files.refused.tooMany.one',
    two: 'agent.files.refused.tooMany.other',
    few: 'agent.files.refused.tooMany.few',
    many: 'agent.files.refused.tooMany.many',
    other: 'agent.files.refused.tooMany.other',
};

const uploadFailures: Readonly<Record<Extract<FileRefusal, { reason: 'failed' }>['failure'], MessageKey>> = {
    unauthenticated: 'agent.files.refused.unauthenticated',
    unauthorized: 'agent.files.refused.unauthorized',
    unreadable: 'agent.files.refused.unreadable',
    missing: 'agent.files.refused.missing',
};

const control =
    'flex size-6.5 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text';

/**
 * What the files a question will carry look like under the field: the refusals of the last attempt above the cards of
 * the files waiting, each of which can be taken off again, and one that did not upload tried again.
 *
 * @param onLeft Puts focus back in the question field, where it goes once what held it is gone and nothing is beside it.
 */
export function WaitingFiles({ files, onLeft }: { readonly files: QuestionFiles; readonly onLeft: () => void }) {
    const { locale, translate } = useLocalization();
    const removers = useRef(new Map<number, HTMLButtonElement>());

    // Taking a card off takes away the control holding focus, so focus moves first — to the card that takes its place,
    // else the one before it, else the field — and is where it belongs before the card has gone.
    const remove = (at: number): void => {
        const file = files.waiting[at];

        if (file === undefined) {
            return;
        }

        const beside = files.waiting[at + 1] ?? files.waiting[at - 1];
        const next = beside === undefined ? undefined : removers.current.get(beside.key);

        if (next === undefined) {
            onLeft();
        } else {
            next.focus();
        }

        files.remove(file.key);
    };

    // Every refusal is a sentence of its own, so the lookup is a function rather than a table: each reason fills
    // different holes.
    const said = (refusal: FileRefusal): string => {
        switch (refusal.reason) {
            case 'kind':
                return refusal.extension === ''
                    ? translate('agent.files.refused.kindUnnamed', { name: refusal.name })
                    : translate('agent.files.refused.kind', {
                          name: refusal.name,
                          kind: refusal.extension.toUpperCase(),
                      });
            case 'tooMany':
                return translate(tooMany[new Intl.PluralRules(locale).select(refusal.count)], {
                    count: new Intl.NumberFormat(locale).format(refusal.count),
                    most: new Intl.NumberFormat(locale).format(mostFilesPerQuestion),
                });
            case 'tooLarge':
                return translate('agent.files.refused.tooLarge', {
                    name: refusal.name,
                    size: sizeOf(refusal.octets, locale),
                    largest: largestFileWorded(locale),
                });
            case 'conversationFull':
                return translate('agent.files.refused.conversationFull', {
                    name: refusal.name,
                    count: new Intl.NumberFormat(locale).format(mostFilesPerConversation),
                });
            case 'empty':
                return translate('agent.files.refused.empty', { name: refusal.name });
            case 'notRead':
                return translate('agent.files.refused.notRead', { name: refusal.name });
            case 'failed':
                return translate(uploadFailures[refusal.failure], { name: refusal.name });
        }
    };

    return (
        <>
            {files.refusals.length === 0 ? null : (
                <div
                    role="alert"
                    className="flex items-start gap-2 rounded-field bg-warning-soft py-1.5 ps-3 pe-1 text-warning-text"
                >
                    <Icon name="block" className="mt-1 size-icon-md shrink-0" />
                    <div className="flex min-w-0 flex-1 flex-col gap-0.75 py-1">
                        {files.refusals.map((refusal, at) => (
                            <p key={at} className="text-base text-pretty">
                                {said(refusal)}
                            </p>
                        ))}
                    </div>
                    <SymbolControl
                        label={translate('agent.files.dismiss')}
                        className="flex size-6.5 shrink-0 items-center justify-center rounded-md transition hover:bg-hover"
                        onPress={() => {
                            onLeft();
                            files.dismissRefusals();
                        }}
                    >
                        <Icon name="close" className="size-icon-sm" />
                    </SymbolControl>
                </div>
            )}

            {files.waiting.length === 0 ? null : (
                <ul aria-label={translate('agent.files.waiting')} className="flex touch-strip gap-2">
                    {files.waiting.map((file, at) => (
                        <li key={file.key} className="shrink-0">
                            <WaitingFileCard
                                file={file}
                                removerRef={(element) => {
                                    if (element === null) {
                                        removers.current.delete(file.key);
                                    } else {
                                        removers.current.set(file.key, element);
                                    }
                                }}
                                onRemove={() => {
                                    remove(at);
                                }}
                                onRetry={() => {
                                    files.retry(file.key);
                                }}
                            />
                        </li>
                    ))}
                </ul>
            )}
        </>
    );
}

function WaitingFileCard({
    file,
    removerRef,
    onRemove,
    onRetry,
}: {
    readonly file: WaitingFile;
    readonly removerRef: (element: HTMLButtonElement | null) => void;
    readonly onRemove: () => void;
    readonly onRetry: () => void;
}) {
    const { locale, translate } = useLocalization();
    const failed = file.state === 'failed';
    const size = sizeOf(file.octets, locale);
    const line = failed
        ? translate('agent.files.failed')
        : file.state === 'uploading'
          ? translate('agent.files.uploading')
          : file.pasted
            ? translate('agent.files.pasted', { size })
            : size;

    return (
        <div
            className={`relative flex min-h-11.5 max-w-waiting-file-narrow items-center gap-2.5 overflow-hidden rounded-field border bg-panel py-1.5 ps-1.75 pe-1 workspace:max-w-waiting-file pointer-coarse:min-h-13 ${
                failed ? 'border-error' : 'border-line'
            }`}
        >
            <FileKind name={file.name} mediaType={file.mediaType} failed={failed} />

            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span className="truncate text-base text-text">{file.name}</span>
                <span className={`text-sm whitespace-nowrap ${failed ? 'text-error-text' : 'text-faint'}`}>{line}</span>
            </span>

            {failed ? (
                <SymbolControl
                    label={translate('agent.files.retry', { name: file.name })}
                    className={control}
                    onPress={onRetry}
                >
                    <Icon name="refresh" className="size-icon-sm" />
                </SymbolControl>
            ) : null}

            <SymbolControl
                ref={removerRef}
                label={translate('agent.files.remove', { name: file.name })}
                className={control}
                onPress={onRemove}
            >
                <Icon name="close" className="size-icon-sm" />
            </SymbolControl>

            {file.state === 'uploading' ? (
                <span aria-hidden="true" className="absolute inset-x-0 bottom-0 h-0.75">
                    <span className="block h-full rounded-full bg-accent progress-unknown" />
                </span>
            ) : null}
        </div>
    );
}
