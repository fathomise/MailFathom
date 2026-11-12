// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState, type DragEvent, type ReactNode } from 'react';
import { mostFilesPerQuestion } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { largestFileWorded } from './questionFiles';

// Only a drag that carries files raises the surface: text or a link dragged over the conversation is somebody moving
// words, and a surface promising to attach them would be wrong.
function carriesFiles(event: DragEvent): boolean {
    return event.dataTransfer.types.includes('Files');
}

/**
 * The conversation column — the thread and the field, never the history — as somewhere files can be dropped onto the
 * question. A drag carrying files raises the design's surface over it, saying what dropping does in a status region a
 * screen reader announces; leaving takes it down, and a drop hands the files over.
 */
export function FileDropTarget({
    onDrop,
    children,
}: {
    readonly onDrop: (files: readonly File[]) => void;
    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const [over, setOver] = useState(false);

    const raise = (event: DragEvent): void => {
        if (carriesFiles(event)) {
            event.preventDefault();
            setOver(true);
        }
    };

    return (
        <div
            className="relative flex min-h-0 min-w-0 flex-1 flex-col"
            onDragEnter={raise}
            onDragOver={raise}
            onDragLeave={(event) => {
                // Moving between the column's own children is not leaving it.
                if (!(event.relatedTarget instanceof Node && event.currentTarget.contains(event.relatedTarget))) {
                    setOver(false);
                }
            }}
            onDrop={(event) => {
                if (carriesFiles(event)) {
                    event.preventDefault();
                    setOver(false);
                    onDrop([...event.dataTransfer.files]);
                }
            }}
        >
            {children}

            <div role="status" className={over ? 'contents' : 'sr-only'}>
                {over ? (
                    <div className="pointer-events-none absolute inset-2.5 z-40 flex animate-pop-in flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed border-accent bg-accent-soft p-6 text-center">
                        <Icon name="upload_file" className="size-icon-4xl text-accent-deep" />
                        <p className="text-xl font-semibold text-text">{translate('agent.files.drop')}</p>
                        <p className="text-base text-muted">
                            {translate('agent.files.dropLimits', {
                                count: new Intl.NumberFormat(locale).format(mostFilesPerQuestion),
                                size: largestFileWorded(locale),
                            })}
                        </p>
                    </div>
                ) : null}
            </div>
        </div>
    );
}
