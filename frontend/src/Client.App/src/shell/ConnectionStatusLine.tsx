// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState } from 'react';
import { borderedControl } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { wordElapsed } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useConnectionStatus, type ConnectionLevel, type ConnectionStatus } from './connectionStatus';
import { mostReconnectionAttempts } from './useConnection';

// The status of this client's connection to its server, as the first row of the user menu and as the first row of the
// overflow sheet's lower group on a phone. Each level is a dot and a sentence, so colour is never the only thing that
// says it; a delay or a lost server opens onto what is not working and since when, and a lost server onto *Retry*.

const sentences: Readonly<Record<ConnectionLevel, MessageKey>> = {
    connecting: 'connectionStatus.connecting',
    online: 'connectionStatus.online',
    delayed: 'connectionStatus.delayed',
    disconnected: 'connectionStatus.disconnected',
};

const dots: Readonly<Record<ConnectionLevel, string>> = {
    connecting: 'bg-faint',
    online: 'bg-healthy',
    delayed: 'bg-warning',
    disconnected: 'bg-error',
};

/** Where the row stands: the user menu's compact row, or the sheet's row sized for a finger. */
export type ConnectionStatusPlace = 'menu' | 'sheet';

export function ConnectionStatusLine({ place }: { readonly place: ConnectionStatusPlace }) {
    const { translate } = useLocalization();
    const status = useConnectionStatus();
    const disclosable = status.level === 'delayed' || status.level === 'disconnected';
    const sheet = place === 'sheet';
    const alarmed = sheet && status.level === 'disconnected';

    const row = sheet
        ? `flex min-h-13 w-full items-center gap-4 rounded-md px-3.5 text-start text-lg ${alarmed ? 'bg-error-soft text-error-text' : 'text-text'}`
        : 'flex w-full items-center gap-2 border-b border-line-soft px-3.25 py-2.5 text-start text-base text-text-soft';
    const mark = alarmed ? (
        <Icon name="cloud_off" className="size-icon-xl w-6.5 shrink-0" />
    ) : (
        <span aria-hidden="true" className={sheet ? 'flex w-6.5 shrink-0 justify-center' : 'contents'}>
            <span className={`size-1.75 shrink-0 rounded-full ${dots[status.level]}`} />
        </span>
    );
    const sentence = <span className="min-w-0 flex-1">{translate(sentences[status.level])}</span>;

    return (
        <>
            {disclosable ? (
                <button
                    type="button"
                    aria-expanded={status.detailsOpen}
                    className={`${row} transition hover:bg-hover`}
                    onClick={() => {
                        status.showDetails(!status.detailsOpen);
                    }}
                >
                    {mark}
                    {sentence}
                    <Icon
                        name="expand_more"
                        className={`size-icon-md shrink-0 text-muted transition ${status.detailsOpen ? 'rotate-180' : ''}`}
                    />
                </button>
            ) : (
                <p className={row}>
                    {mark}
                    {sentence}
                </p>
            )}

            {status.detailsOpen ? <Details status={status} place={place} /> : null}
        </>
    );
}

/** One thing that is not working, and since when. */
interface Block {
    readonly key: string;
    readonly lines: readonly string[];
    readonly since: number;
}

function Details({ status, place }: { readonly status: ConnectionStatus; readonly place: ConnectionStatusPlace }) {
    const { locale, translate } = useLocalization();
    const now = useClock();
    const count = (value: number): string => new Intl.NumberFormat(locale).format(value);
    const total = count(mostReconnectionAttempts);
    const blocks: readonly Block[] =
        status.level === 'disconnected'
            ? [
                  {
                      key: status.cause,
                      lines:
                          status.cause === 'offline'
                              ? [translate('connection.offline')]
                              : [
                                    translate('connectionStatus.unanswered'),
                                    status.attempts < mostReconnectionAttempts
                                        ? translate('connectionStatus.attempt', {
                                              attempt: count(status.attempts + 1),
                                              total,
                                          })
                                        : translate('connectionStatus.attemptsSpent', { total }),
                                ],
                      since: status.since,
                  },
              ]
            : status.level === 'delayed'
              ? status.delays.map((delay) => ({
                    key: delay.kind === 'channel' ? 'channel' : `mailServer:${delay.account}`,
                    lines: [
                        delay.kind === 'channel'
                            ? translate('connectionStatus.channel')
                            : translate('connectionStatus.mailServer', { account: delay.account }),
                    ],
                    since: delay.since,
                }))
              : [];

    return (
        <div
            className={
                place === 'sheet'
                    ? 'flex flex-col gap-2 pt-1 pr-3.5 pb-3 pl-14 text-md/normal text-text-soft'
                    : 'flex flex-col gap-1.5 border-b border-line-soft pt-2.5 pr-3.25 pb-3 pl-7 text-sm/normal text-text-soft'
            }
        >
            {blocks.map((block) => (
                <div key={block.key} className="flex flex-col gap-0.5">
                    {block.lines.map((line) => (
                        <p key={line}>{line}</p>
                    ))}
                    <p className="text-faint">
                        {translate('connectionStatus.since', { time: wordElapsed(block.since, now, locale) })}
                    </p>
                </div>
            ))}

            {status.level === 'disconnected' ? (
                <>
                    <p className="text-muted">{translate('connectionStatus.retryHint')}</p>
                    <button
                        type="button"
                        className={`${borderedControl} self-start px-3.5 py-1.75 text-base font-semibold`}
                        onClick={status.retry}
                    >
                        {translate('connectionStatus.retry')}
                    </button>
                </>
            ) : null}
        </div>
    );
}

// The details say how long ago something began, so they are drawn again while they are open; fifteen seconds is the
// design's own pace, and fine enough for a count that changes unit at a minute.
function useClock(): number {
    const [now, setNow] = useState(Date.now);

    useEffect(() => {
        const ticking = window.setInterval(() => {
            setNow(Date.now());
        }, 15_000);

        return () => {
            window.clearInterval(ticking);
        };
    }, []);

    return now;
}

/**
 * The rail's alarm, drawn only while the server is out of reach: larger than the controls around it and filled, so a
 * client that cannot reach its server says so where the eye already is. It opens the user menu onto the details.
 */
export function ConnectionAlarm() {
    const { translate } = useLocalization();
    const status = useConnectionStatus();

    if (status.level !== 'disconnected') {
        return null;
    }

    return (
        <button
            type="button"
            popoverTarget="account-menu"
            popoverTargetAction="show"
            aria-label={translate('connectionStatus.alarm')}
            className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-error text-on-accent shadow-raised transition hover:opacity-90"
            onClick={() => {
                status.showDetails(true);
            }}
        >
            <Icon name="cloud_off" className="size-icon-xl" />
        </button>
    );
}

/**
 * The level said aloud whenever it changes, mounted once at the frame: the rows above stand inside a menu and a sheet
 * that are closed most of the time, and a change nobody can hear while they are closed is a change said only in colour
 * to somebody who cannot see the alarm.
 */
export function ConnectionAnnouncement() {
    const { translate } = useLocalization();
    const { level } = useConnectionStatus();

    return (
        <p role="status" className="sr-only">
            {translate(sentences[level])}
        </p>
    );
}
