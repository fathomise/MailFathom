// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import type { AnswerBlock, AnswerFile, ClientSession } from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { wordRecentInstant } from '../../localization/instants';
import { sizeOf } from '../../localization/octets';
import { useLocalization } from '../../localization/useLocalization';
import { useReadingZone } from '../../localization/useReadingZone';
import { useAttachmentDownloads } from '../../readingPane/downloadingAttachment';
import { kindOf } from '../../readingPane/fileKind';
import { UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { useAnswerDownloads } from '../answerDownloads';
import { useAnswerSources } from '../answerSources';

// One file an answer hands the person: the design's card for an answer's file, which is one control downloading the
// whole file, and nothing else. It carries no card of the catalogue's around it because the design draws none — an
// answer's files stand under its words as cards of their own.
//
// **What it downloads is the mail screen's own download.** The block names the message the file arrived on and its
// place there, and `useAttachmentDownloads` is the hook the reading pane downloads through: the same route, the same
// bound, the same notices, and the reader's grant at the moment they press. Nothing about the file was copied when the
// answer was written, so whether it is still there is the download's answer rather than the card's guess.
//
// **A file the deployment no longer holds for this reader is let go of where it is drawn.** A `404` is the one answer
// for a message removed since, one withheld from them, and a file no longer stored, and pressing again changes none of
// them — so the card turns to the design's dashed, unpressable state and says so, while every other refusal is the
// notice the download already raised and leaves the card as it was.

export function OfferedFile({ block }: { readonly block: AnswerBlock }) {
    const { sources } = useAnswerSources();
    const session = useAnswerDownloads();

    if (block.type !== 'file') {
        return <UnrecognisedAnswerBlock />;
    }

    const target = sources.get(block.file.source)?.target ?? null;

    // A source this client cannot follow names no message to ask for, so the file is named and offered nowhere — the
    // rule `AttachmentGallery.tsx` states for the entries it cannot follow either.
    return session === null || target === null ? (
        <FileCard file={block.file} state="named" />
    ) : (
        <DownloadableFile file={block.file} session={session} storedEmailId={target.email} />
    );
}

/** A file the reader can ask for, which stays offered until the deployment answers that it holds no such file. */
function DownloadableFile({
    file,
    session,
    storedEmailId,
}: {
    readonly file: AnswerFile;
    readonly session: ClientSession;
    readonly storedEmailId: string;
}) {
    const downloads = useAttachmentDownloads(session, storedEmailId);

    // Held by this card alone, so a file found missing is offered again once the conversation is drawn anew. It moves
    // beside the conversation's own state if meeting that second refusal turns out to happen often enough to matter.
    const [gone, setGone] = useState(false);

    if (gone) {
        return <FileCard file={file} state="gone" />;
    }

    return (
        <FileCard
            file={file}
            state={downloads.arriving(file.attachmentPosition) ? 'arriving' : 'offered'}
            onDownload={() => {
                void downloads
                    .start({
                        position: file.attachmentPosition,
                        fileName: file.name,
                        wasFileNameNormalized: false,
                        mediaType: file.mediaType,
                        sizeOctets: file.sizeOctets,
                    })
                    .then((outcome) => {
                        if (outcome === 'missing') {
                            setGone(true);
                        }
                    });
            }}
        />
    );
}

/** The card itself, in whichever of its states it is: offered, on its way, gone, or named with nowhere to ask. */
function FileCard({
    file,
    state,
    onDownload,
}: {
    readonly file: AnswerFile;
    readonly state: 'offered' | 'arriving' | 'gone' | 'named';
    readonly onDownload?: () => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());

    const name = file.name ?? translate('attachment.unnamed');
    const size = sizeOf(file.sizeOctets, locale);
    const sent = wordRecentInstant(file.sentAt, locale, now, timeZone);
    const message =
        file.sender !== null && sent !== null
            ? translate('answer.fileMessage', { sender: file.sender, sent })
            : (file.sender ?? sent ?? translate('answer.fileMessageUnnamed'));
    const gone = state === 'gone';

    const said = (
        <>
            <span
                aria-hidden="true"
                className={`flex size-9.5 shrink-0 items-center justify-center rounded-md bg-sunken text-xs font-semibold uppercase ${
                    gone ? 'text-faint' : 'text-muted'
                }`}
            >
                {kindOf(file.name, file.mediaType)}
            </span>

            <span className="flex min-w-0 flex-1 flex-col gap-0.75 text-start">
                <span className={`truncate text-md leading-snug font-medium ${gone ? 'text-muted' : 'text-text'}`}>
                    {name}
                </span>

                <span className="flex min-w-0 items-center gap-1.25 text-sm leading-snug text-faint">
                    <Icon name="mail" className="size-icon-xs shrink-0" />
                    <span className="min-w-0 text-pretty">
                        {gone
                            ? translate('answer.fileGone', { message })
                            : translate('answer.fileFrom', { message, size })}
                    </span>
                </span>
            </span>

            <span
                className={`flex size-8.5 shrink-0 items-center justify-center ${gone ? 'text-faint' : 'text-accent-deep'}`}
            >
                <Icon name={gone ? 'cloud_off' : 'download'} className="size-icon-lg" />
            </span>
        </>
    );

    const card = 'flex w-full min-w-0 items-center gap-3 rounded-lg py-2.25 ps-2.5 pe-2 workspace:max-w-answer-file';

    if (onDownload === undefined) {
        return (
            <div
                className={`${card} ${gone ? 'border border-dashed border-line-strong' : 'border border-line bg-panel'}`}
            >
                {said}
            </div>
        );
    }

    return (
        <button
            type="button"
            aria-label={translate('answer.fileDownload', { name, size })}
            aria-disabled={state === 'arriving'}
            className={`${card} border border-line bg-panel shadow-raised transition hover:border-accent-line`}
            onClick={() => {
                if (state !== 'arriving') {
                    onDownload();
                }
            }}
        >
            {said}
        </button>
    );
}
