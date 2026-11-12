// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { AgentConversationFile } from '@mailfathom/client-backend';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import { FileKind } from './FileKind';

/**
 * The files a sent question carried, under its words: one chip each, with the kind, the name and the size.
 *
 * A question names its files by identifier and the conversation's list names them, so a file the list has not answered
 * for yet is not drawn rather than drawn as the identifier it arrived as.
 *
 * @param carried What the question carried, by identifier.
 * @param files The files the conversation holds.
 */
export function CarriedFiles({
    carried,
    files,
}: {
    readonly carried: readonly string[];
    readonly files: ReadonlyMap<string, AgentConversationFile>;
}) {
    const { locale, translate } = useLocalization();
    const named = carried.flatMap((fileId) => {
        const file = files.get(fileId);

        return file === undefined ? [] : [file];
    });

    return named.length === 0 ? null : (
        <ul aria-label={translate('agent.files.carried')} className="mt-2.25 flex flex-wrap gap-1.5">
            {named.map((file) => (
                <li
                    key={file.fileId}
                    className="flex max-w-full items-center gap-2 rounded-md border border-accent-line bg-panel py-1.25 ps-1.25 pe-2.5"
                >
                    <FileKind name={file.name} mediaType={file.mediaType} carried />
                    <span className="min-w-0 truncate text-base text-text">
                        {file.name ?? translate('attachment.unnamed')}
                    </span>
                    <span className="text-sm whitespace-nowrap text-faint">{sizeOf(file.byteLength, locale)}</span>
                </li>
            ))}
        </ul>
    );
}
