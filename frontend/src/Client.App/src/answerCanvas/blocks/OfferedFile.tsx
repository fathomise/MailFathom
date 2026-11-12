// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import {
    readAgentConversationFile,
    type AnswerBlock,
    type AnswerFile,
    type AnswerReport,
    type ClientSession,
} from '@mailfathom/client-backend';
import { Icon } from '../../controls/Icon';
import { deliveryFailureOf } from '../../deployment/attachmentExchange';
import type { MessageKey } from '../../localization/en';
import { wordRecentInstant } from '../../localization/instants';
import { sizeOf } from '../../localization/octets';
import { useLocalization } from '../../localization/useLocalization';
import { useReadingZone } from '../../localization/useReadingZone';
import { useAttachmentDownloads, useFileDownloads } from '../../readingPane/downloadingAttachment';
import { kindOf } from '../../readingPane/fileKind';
import { savedAs } from '../../readingPane/savedFileName';
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
//
// **A report the Agent wrote is the same card, downloaded from its conversation.** It names the conversation's file it
// was stored as rather than a message, so it is asked for on that file's route, through the same hook and so under the
// same bound and notices; what tells it apart is its tile on the accent and the line saying the Agent wrote it, and its
// `404` — a conversation deleted since, or a file it no longer holds — asks the reader to have it written again.

export function OfferedFile({ block }: { readonly block: AnswerBlock }) {
    const { sources } = useAnswerSources();
    const downloads = useAnswerDownloads();

    if (block.type !== 'file') {
        return <UnrecognisedAnswerBlock />;
    }

    const target = sources.get(block.file.source)?.target ?? null;

    // A source this client cannot follow names no message to ask for, so the file is named and offered nowhere — the
    // rule `AttachmentGallery.tsx` states for the entries it cannot follow either.
    return downloads === null || target === null ? (
        <MessageFileCard file={block.file} state="named" />
    ) : (
        <DownloadableFile file={block.file} session={downloads.session} storedEmailId={target.email} />
    );
}

/** A report the Agent wrote into the conversation, which is downloaded from that conversation's files. */
export function WrittenReport({ block }: { readonly block: AnswerBlock }) {
    const downloads = useAnswerDownloads();

    if (block.type !== 'report') {
        return <UnrecognisedAnswerBlock />;
    }

    const conversation = downloads?.conversation ?? null;

    return downloads === null || conversation === null ? (
        <ReportCard report={block.report} state="named" />
    ) : (
        <DownloadableReport report={block.report} session={downloads.session} conversation={conversation} />
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
        return <MessageFileCard file={file} state="gone" />;
    }

    return (
        <MessageFileCard
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

/** A report the reader can ask for, which stays offered until the deployment answers that it no longer holds it. */
function DownloadableReport({
    report,
    session,
    conversation,
}: {
    readonly report: AnswerReport;
    readonly session: ClientSession;
    readonly conversation: string;
}) {
    const downloads = useFileDownloads((attachment, exchange, abandoned) =>
        readAgentConversationFile(
            session,
            conversation,
            report.fileId,
            attachment.sizeOctets,
            (request) =>
                exchange.deliver(
                    request,
                    savedAs(attachment.fileName, attachment.position),
                    () => undefined,
                    abandoned,
                ),
            deliveryFailureOf,
        ),
    );

    // Held by this card alone, for the reason `DownloadableFile` gives.
    const [gone, setGone] = useState(false);

    if (gone) {
        return <ReportCard report={report} state="gone" />;
    }

    // One card offers one file, so the position the hook keys its downloads by is the same for every press.
    const position = 0;

    return (
        <ReportCard
            report={report}
            state={downloads.arriving(position) ? 'arriving' : 'offered'}
            onDownload={() => {
                void downloads
                    .start({
                        position,
                        fileName: report.name,
                        wasFileNameNormalized: false,
                        mediaType: report.mediaType,
                        sizeOctets: report.sizeOctets,
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

type CardState = 'offered' | 'arriving' | 'gone' | 'named';

/** A file a message carried, worded by who sent it and when. */
function MessageFileCard({
    file,
    state,
    onDownload,
}: {
    readonly file: AnswerFile;
    readonly state: CardState;
    readonly onDownload?: () => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());

    const size = sizeOf(file.sizeOctets, locale);
    const sent = wordRecentInstant(file.sentAt, locale, now, timeZone);
    const message =
        file.sender !== null && sent !== null
            ? translate('answer.fileMessage', { sender: file.sender, sent })
            : (file.sender ?? sent ?? translate('answer.fileMessageUnnamed'));

    return (
        <FileCard
            name={file.name ?? translate('attachment.unnamed')}
            kind={kindOf(file.name, file.mediaType)}
            size={size}
            origin="message"
            line={
                state === 'gone'
                    ? translate('answer.fileGone', { message })
                    : translate('answer.fileFrom', { message, size })
            }
            state={state}
            onDownload={onDownload}
        />
    );
}

const pagesCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'answer.reportWritten.other',
    one: 'answer.reportWritten.one',
    two: 'answer.reportWritten.other',
    few: 'answer.reportWritten.few',
    many: 'answer.reportWritten.many',
    other: 'answer.reportWritten.other',
};

/** A report the Agent wrote, worded by how many pages it was set on. */
function ReportCard({
    report,
    state,
    onDownload,
}: {
    readonly report: AnswerReport;
    readonly state: CardState;
    readonly onDownload?: () => void;
}) {
    const { locale, translate } = useLocalization();
    const size = sizeOf(report.sizeOctets, locale);

    return (
        <FileCard
            name={report.name}
            kind={kindOf(report.name, report.mediaType)}
            size={size}
            origin="report"
            line={
                state === 'gone'
                    ? translate('answer.reportGone')
                    : translate(pagesCounted[new Intl.PluralRules(locale).select(report.pageCount)], {
                          count: new Intl.NumberFormat(locale).format(report.pageCount),
                          size,
                      })
            }
            state={state}
            onDownload={onDownload}
        />
    );
}

/**
 * The card itself, one for every origin, in whichever of its states it is: offered, on its way, gone, or named with
 * nowhere to ask. The origin chooses the glyph of its line and whether its tile stands on the accent.
 */
function FileCard({
    name,
    kind,
    size,
    origin,
    line,
    state,
    onDownload,
}: {
    readonly name: string;
    readonly kind: string;
    readonly size: string;
    readonly origin: 'message' | 'report';
    readonly line: string;
    readonly state: CardState;
    readonly onDownload?: (() => void) | undefined;
}) {
    const { translate } = useLocalization();
    const gone = state === 'gone';
    const tile = gone
        ? 'bg-sunken text-faint'
        : origin === 'report'
          ? 'bg-accent-soft text-accent-deep'
          : 'bg-sunken text-muted';

    const said = (
        <>
            <span
                aria-hidden="true"
                className={`flex size-9.5 shrink-0 items-center justify-center rounded-md text-xs font-semibold uppercase ${tile}`}
            >
                {kind}
            </span>

            <span className="flex min-w-0 flex-1 flex-col gap-0.75 text-start">
                <span className={`truncate text-md leading-natural font-medium ${gone ? 'text-muted' : 'text-text'}`}>
                    {name}
                </span>

                <span className="flex min-w-0 items-center gap-1.25 text-sm leading-natural text-faint">
                    <Icon name={origin === 'report' ? 'auto_awesome' : 'mail'} className="size-icon-xs shrink-0" />
                    <span className="min-w-0 text-pretty">{line}</span>
                </span>
            </span>

            <span
                className={`flex size-8.5 shrink-0 items-center justify-center ${gone ? 'text-faint' : 'text-accent-deep'}`}
            >
                <Icon name={gone ? 'cloud_off' : 'download'} className="size-icon-lg" />
            </span>
        </>
    );

    const card =
        'flex w-full min-w-0 items-center gap-3 rounded-lg py-2.25 ps-2.5 pe-2 pointer-coarse:py-2 pointer-coarse:pe-1.5 workspace:max-w-answer-file';

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
