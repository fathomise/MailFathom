// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    longestMailSource,
    type ClientFailureReason,
    type ClientResult,
    type MailSource,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SkeletonLines, type SkeletonLine } from '../controls/Skeleton';
import { SurfaceControl } from '../controls/SurfaceControl';
import { backOutlined } from '../confirmation/wayOutShapes';
import type { MessageKey } from '../localization/en';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import type { MessageSourceReading } from './messageSourceReading';

// A message exactly as it arrived — every header and every part, in its transfer encoding — for the reader checking
// where it came from. It composes what stands in it and never where it stands: `mailSpace/SurfaceWindow.tsx` puts it
// on the screen and hands it the way out.
//
// It is read when the surface opens and at no other time, and what was read lives as long as the surface does: nothing
// keeps it, so closing and pressing again reads it again.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

// The design's own shape for the wait: blocks of header lines with gaps between them, then the body's lines.
const waitingSourceLines: readonly SkeletonLine[] = [
    38, 64, 52, 0, 70, 58, 66, 44, 0, 61, 74, 30, 0, 22, 48, 0, 96, 92, 98, 57,
].map((fills, at) => ({
    fills,
    height: fills === 0 ? 'h-1.5' : at > 15 ? 'h-2.5' : 'h-2.5 max-w-140',
}));

/** What is being read: which attempt at it. A retry is a new attempt, and only the newest attempt's answer is drawn. */
interface Answered {
    readonly attempt: number;
    readonly result: ClientResult<MailSource>;
}

export function MessageSourceSurface({
    reading,
    storedEmailId,
    subject,
    onClose,
}: {
    readonly reading: MessageSourceReading;

    /** The message whose source is shown. */
    readonly storedEmailId: string;

    /** The message's subject where whoever opened this holds it, which the head names the source by. */
    readonly subject: string | null;

    /** Leaves the surface, through whatever put it on the screen. */
    readonly onClose: () => void;
}) {
    const { locale, translate } = useLocalization();
    const [attempt, setAttempt] = useState(0);
    const [answered, setAnswered] = useState<Answered | null>(null);
    const { read, online } = reading;
    const [connected, setConnected] = useState(online);

    // A failure the network gap itself caused goes with the gap, so what stands while there is no network is the
    // offline line rather than a refusal to press through, and coming back reads the source with the skeleton up.
    // Adjusted during render, as `fullHtml/FullHtmlSurface.tsx` answers the same change.
    if (connected !== online) {
        setConnected(online);

        if (!online && answered?.result.outcome === 'failed') {
            setAnswered(null);
        }
    }

    // Opening the surface is a view change, so focus goes to the start of it.
    const region = useRef<HTMLElement>(null);

    useEffect(() => {
        region.current?.focus();
    }, []);

    useEffect(() => {
        if (!online) {
            return;
        }

        let listening = true;

        void read(storedEmailId).then((result) => {
            if (listening) {
                setAnswered({ attempt, result });
            }
        });

        return () => {
            listening = false;
        };
    }, [read, storedEmailId, attempt, online]);

    const result = answered?.attempt === attempt ? answered.result : null;
    const source = result?.outcome === 'read' && !result.value.withheld ? result.value : null;

    // How large what is shown came to, as received: the whole message where it was cut, and the text itself otherwise.
    const size =
        source === null ? null : sizeOf(source.wholeOctets ?? new TextEncoder().encode(source.text).length, locale);

    return (
        <section
            ref={region}
            tabIndex={-1}
            aria-label={translate('source.title')}
            className="flex h-full min-h-0 flex-col bg-panel"
        >
            <span aria-hidden="true" className="flex h-5 shrink-0 items-center justify-center desktop:hidden">
                <span className="h-1 w-9.5 rounded-full bg-line-strong" />
            </span>

            <header className="flex shrink-0 items-center gap-3 border-b border-line pt-1.5 pr-3 pb-2.5 pl-4 desktop:pt-2.5">
                <Icon name="data_object" className="size-icon-lg shrink-0 text-muted" />

                <div className="flex min-w-0 flex-1 flex-col gap-px">
                    <h2 className="text-lg leading-5.25 font-semibold">{translate('source.title')}</h2>

                    {subject === null && size === null ? null : (
                        <span className="truncate text-sm leading-4.25 text-muted">
                            {size === null
                                ? subject
                                : subject === null
                                  ? translate('source.asReceived', { size })
                                  : translate('source.subjectAsReceived', { subject, size })}
                        </span>
                    )}
                </div>

                <SurfaceControl label={translate('source.close')} icon="close" onActivate={onClose} />
            </header>

            {/* Focusable and named, because WebKit gives a scroll container holding nothing focusable no keyboard path,
                and a source runs far past the first screen of headers on the head the desktop shell renders in. */}
            <div
                aria-label={translate('source.text')}
                className="min-h-0 flex-1 overflow-auto"
                role="region"
                tabIndex={0}
            >
                <SourceBody
                    online={online}
                    result={result}
                    onRetry={() => {
                        setAttempt(attempt + 1);
                    }}
                />
            </div>
        </section>
    );
}

// The source in the five states a surface that waits owes somebody, plus the one a screened deployment answers with.
function SourceBody({
    online,
    result,
    onRetry,
}: {
    readonly online: boolean;
    readonly result: ClientResult<MailSource> | null;
    readonly onRetry: () => void;
}) {
    const { locale, translate } = useLocalization();

    if (result === null) {
        return online ? (
            <div role="status" aria-label={translate('source.reading')}>
                <SkeletonLines lines={waitingSourceLines} className="gap-2.25 px-5 py-4.5" />
            </div>
        ) : (
            <p role="status" className="flex items-start gap-2.25 px-5 py-4.5 text-base text-pretty text-muted">
                <Icon name="cloud_off" className="size-icon-md shrink-0" />
                {translate('source.offline')}
            </p>
        );
    }

    if (result.outcome === 'failed') {
        return (
            <div className="flex flex-col items-start gap-2.5 px-5 py-4.5">
                <p role="alert" className="flex items-start gap-2.25 text-base text-pretty text-warning-text">
                    <Icon name="warning" className="size-icon-md shrink-0" />
                    {translate('source.failed', { reason: translate(failureLabels[result.failure.reason]) })}
                </p>

                {/* Reading again is the way out of one failure alone, for the reason `shell/ConnectionSummary.tsx`
                    gives: a copy that is gone, a credential refused, and an answer that could not be read each repeat
                    identically on a second attempt. */}
                {result.failure.reason === 'unavailable' ? (
                    <button type="button" className={backOutlined} onClick={onRetry}>
                        {translate('connection.retry')}
                    </button>
                ) : null}
            </div>
        );
    }

    if (result.value.withheld) {
        return (
            <p role="status" className="flex items-start gap-2.25 px-5 py-4.5 text-base text-pretty text-muted">
                <Icon name="lock" className="size-icon-md shrink-0" />
                {translate('source.withheld')}
            </p>
        );
    }

    const { text, wholeOctets } = result.value;

    return (
        <div className="animate-content-arrival">
            {wholeOctets === null ? null : (
                <p
                    role="note"
                    className="mx-5 mt-3.5 flex items-start gap-2.25 rounded-md border border-line bg-sunken px-3 py-2.5 text-base text-pretty text-muted"
                >
                    <Icon name="info" className="size-icon-md shrink-0" />
                    {translate('source.cut', {
                        whole: sizeOf(wholeOctets, locale),
                        shown: sizeOf(longestMailSource, locale),
                    })}
                </p>
            )}

            <pre className="m-0 px-3.5 pt-3.5 pb-6 font-mono text-sm leading-relaxed wrap-anywhere whitespace-pre-wrap text-text-soft select-text workspace:px-5 workspace:pt-4 workspace:pb-7 workspace:text-base">
                {text}
            </pre>
        </div>
    );
}
