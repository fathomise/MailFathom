// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import {
    longestResponseBody,
    mostReconnectionAttempts,
    mostRetries,
    type DeploymentTelemetryLevel,
    type MailAccount,
    type MailFathomPermission,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { usePendingChanges } from '../pendingChanges/usePendingChanges';
import { useTheme } from '../theme/useTheme';
import {
    useCoarsePointer,
    useDesktopComposition,
    useLowWindow,
    useRoomyComposition,
    useTallWindow,
    useTwoPanes,
    useWideWorkspace,
} from '../shell/useWideWorkspace';
import {
    isPending,
    useDeveloperRecord,
    type DeveloperRecord,
    type HeaderLine,
    type VitalName,
    type WireRequest,
} from './developerRecord';
import { wordBytes, wordFromNow, wordMilliseconds, wordNumber } from './developerWording';

// What the developer information surface shows, which is the same in a window of its own and inside the page.
//
// **It shows identifiers.** That is this surface's exception to the rule that no identifier reaches a reader: it is a
// developer's tool, and an identifier is what finds the same session in the deployment's own records. It shows nothing
// else a person wrote or was sent — no mail, no subject, no address, no display name of an account, which is often an
// address, and no headline of a notification — and no credential. Every list is drawn newest first.
//
// The six sections stand side by side where the window is desktop-wide and are tabs below that. Which of the two is
// decided in script, by `sideBySide`, measured in the window that draws the surface rather than in the client's, and only
// that one shape is rendered: six named regions side by side, or a tab list with the selected section as its panel.

/** What the frame knows about the session, which is everything here that the record does not hold. */
export interface DeveloperContext {
    readonly deploymentAddress: string | null;
    readonly deploymentVersion: string | null;

    /** The source revision the deployment's image was built from. */
    readonly deploymentRevision: string | null;
    readonly permissions: readonly MailFathomPermission[];
    readonly telemetryLevel: DeploymentTelemetryLevel | null;
    readonly telemetryAllowed: boolean;
    readonly person: string | null;
    readonly organization: string | null;
    readonly credential: {
        readonly kind: 'session' | 'grant';
        readonly expiresAt: string;
        readonly issuer: string | null;
    } | null;
    readonly reachable: boolean;
    readonly synchronizationEnabled: boolean | null;
    readonly accounts: readonly MailAccount[];
    readonly space: string | null;
}

const sections = ['signals', 'requests', 'telemetry', 'session', 'data', 'environment'] as const;

type DeveloperSection = (typeof sections)[number];

const sectionNames: Readonly<Record<DeveloperSection, MessageKey>> = {
    signals: 'developer.signals',
    requests: 'developer.requests',
    telemetry: 'developer.telemetry',
    session: 'developer.session',
    data: 'developer.data',
    environment: 'developer.environment',
};

/** How often what the surface shows is read again while it is open, which is what keeps every "ago" true. */
const rereadEvery = 1_000;

function useNow(): number {
    const [now, setNow] = useState(Date.now);

    useEffect(() => {
        const ticking = window.setInterval(() => {
            setNow(Date.now());
        }, rereadEvery);

        return () => {
            window.clearInterval(ticking);
        };
    }, []);

    return now;
}

export function DeveloperSections({
    context,
    sideBySide,
    onClose,
}: {
    readonly context: DeveloperContext;

    /** Whether the window it is drawn in has room for every section at once, rather than one tab of them at a time. */
    readonly sideBySide: boolean;
    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const record = useDeveloperRecord();
    const now = useNow();
    const tabsId = useId();
    const [selected, setSelected] = useState<DeveloperSection>('signals');

    const bodies: Readonly<Record<DeveloperSection, ReactNode>> = {
        signals: <SignalsSection record={record} now={now} />,
        requests: <RequestsSection record={record} now={now} />,
        telemetry: <TelemetrySection record={record} context={context} now={now} />,
        session: <SessionSection context={context} now={now} />,
        data: <DataSection context={context} now={now} />,
        environment: <EnvironmentSection record={record} />,
    };

    return (
        <div className="flex h-full min-h-0 flex-col bg-panel text-text">
            <header className="flex shrink-0 items-center gap-3 border-b border-line px-4 py-3">
                <Icon name="code" className="size-icon-lg text-muted" />
                <h2 className="min-w-0 flex-1 truncate text-2xl font-semibold">{translate('developer.title')}</h2>
                <SymbolControl
                    label={translate('developer.close')}
                    className="flex size-9 items-center justify-center rounded-md text-muted hover:bg-hover hover:text-text"
                    onPress={onClose}
                >
                    <Icon name="close" className="size-icon-lg" />
                </SymbolControl>
            </header>

            {sideBySide ? (
                <div className="grid min-h-0 flex-1 grid-cols-3 content-start gap-4 overflow-y-auto p-4">
                    {sections.map((section) => (
                        <section
                            key={section}
                            aria-labelledby={`${tabsId}-${section}-heading`}
                            className="flex min-w-0 flex-col gap-3 rounded-lg border border-line p-3.5"
                        >
                            <h3 id={`${tabsId}-${section}-heading`} className="text-xl font-semibold">
                                {translate(sectionNames[section])}
                            </h3>
                            {bodies[section]}
                        </section>
                    ))}
                </div>
            ) : (
                <>
                    <SectionTabs id={tabsId} selected={selected} onSelect={setSelected} />
                    <div
                        id={`${tabsId}-${selected}-panel`}
                        role="tabpanel"
                        aria-labelledby={`${tabsId}-${selected}`}
                        className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto p-4"
                    >
                        {bodies[selected]}
                    </div>
                </>
            )}
        </div>
    );
}

/**
 * The sections as a tab strip, which is the shape below the desktop width. A tab list's own keyboard contract: one tab
 * stop, the arrow keys moving between the tabs, and Home and End reaching the ends.
 */
function SectionTabs({
    id,
    selected,
    onSelect,
}: {
    readonly id: string;
    readonly selected: DeveloperSection;
    readonly onSelect: (section: DeveloperSection) => void;
}) {
    const { translate } = useLocalization();
    const tabs = useRef<Partial<Record<DeveloperSection, HTMLButtonElement | null>>>({});

    function moved(event: KeyboardEvent): void {
        const at = sections.indexOf(selected);
        const next =
            event.key === 'ArrowRight'
                ? sections[(at + 1) % sections.length]
                : event.key === 'ArrowLeft'
                  ? sections[(at - 1 + sections.length) % sections.length]
                  : event.key === 'Home'
                    ? sections[0]
                    : event.key === 'End'
                      ? sections.at(-1)
                      : undefined;

        if (next === undefined) {
            return;
        }

        event.preventDefault();
        onSelect(next);
        tabs.current[next]?.focus();
    }

    return (
        <div
            role="tablist"
            aria-label={translate('developer.sections')}
            className="flex shrink-0 gap-1 overflow-x-auto border-b border-line px-3 py-2"
            onKeyDown={moved}
        >
            {sections.map((section) => (
                <button
                    key={section}
                    ref={(tab) => {
                        tabs.current[section] = tab;
                    }}
                    type="button"
                    role="tab"
                    id={`${id}-${section}`}
                    aria-selected={section === selected}
                    aria-controls={`${id}-${section}-panel`}
                    tabIndex={section === selected ? 0 : -1}
                    className={`touch-extended shrink-0 rounded-full px-3 py-1.5 text-md whitespace-nowrap transition ${
                        section === selected
                            ? 'bg-accent-soft font-semibold text-accent-deep'
                            : 'text-muted hover:bg-hover hover:text-text'
                    }`}
                    onClick={() => {
                        onSelect(section);
                    }}
                >
                    {translate(sectionNames[section])}
                </button>
            ))}
        </div>
    );
}

/** A list of named values. */
function Facts({ children }: { readonly children: ReactNode }) {
    return <dl className="flex flex-col gap-1.5 text-md">{children}</dl>;
}

function Fact({ name, children }: { readonly name: MessageKey; readonly children: ReactNode }) {
    const { translate } = useLocalization();

    return (
        <div className="flex items-baseline justify-between gap-3">
            <dt className="shrink-0 text-muted">{translate(name)}</dt>
            <dd className="min-w-0 text-end wrap-anywhere">{children}</dd>
        </div>
    );
}

/** A value a developer reads as it was sent: an identifier, a route, a closed-set value. */
function Literal({ children }: { readonly children: ReactNode }) {
    return <code className="font-mono text-base">{children}</code>;
}

function Absent({ name = 'developer.nothing' }: { readonly name?: MessageKey }) {
    const { translate } = useLocalization();

    return <span className="text-faint">{translate(name)}</span>;
}

function SubHeading({ name }: { readonly name: MessageKey }) {
    const { translate } = useLocalization();

    return <h4 className="pt-1 text-base font-semibold tracking-wide text-faint uppercase">{translate(name)}</h4>;
}

/** An instant, or nothing where there is none or what the deployment sent does not read as one. */
function Moment({ at, now }: { readonly at: number | null; readonly now: number }) {
    const { locale } = useLocalization();

    return at === null || Number.isNaN(at) ? (
        <Absent />
    ) : (
        <time dateTime={new Date(at).toISOString()}>{wordFromNow(at, now, locale)}</time>
    );
}

function YesNo({ value }: { readonly value: boolean }) {
    const { translate } = useLocalization();

    return <>{translate(value ? 'developer.yes' : 'developer.no')}</>;
}

/** Copies a trace identifier, from whichever window the surface stands in. */
function CopyTrace({ traceId }: { readonly traceId: string }) {
    const { translate } = useLocalization();
    const [copied, setCopied] = useState(false);

    return (
        <span className="inline-flex items-center gap-1.5">
            <Literal>{traceId}</Literal>
            <button
                type="button"
                aria-label={translate('developer.copyTrace')}
                className="shrink-0 rounded-sm px-1.5 py-0.5 text-base text-accent hover:bg-hover"
                onClick={(event) => {
                    const clipboard = event.currentTarget.ownerDocument.defaultView?.navigator.clipboard;

                    void clipboard?.writeText(traceId).then(() => {
                        setCopied(true);
                    });
                }}
            >
                {translate(copied ? 'developer.copied' : 'developer.copy')}
            </button>
        </span>
    );
}

function SignalsSection({ record, now }: { readonly record: DeveloperRecord; readonly now: number }) {
    const { translate, locale } = useLocalization();
    const { signals } = record;
    const kinds = Object.entries(signals.received).sort(([first], [second]) => first.localeCompare(second));

    return (
        <>
            <Facts>
                <Fact name="developer.standing">
                    <Literal>{signals.standing}</Literal>
                </Fact>
                <Fact name="developer.since">
                    <Moment at={signals.since} now={now} />
                </Fact>
                <Fact name="developer.refusals">
                    {translate('developer.countOf', {
                        count: wordNumber(signals.refusals, locale),
                        most: wordNumber(mostReconnectionAttempts, locale),
                    })}
                </Fact>
                <Fact name="developer.nextAttempt">
                    <Moment at={signals.nextAttemptAt} now={now} />
                </Fact>
                <Fact name="developer.lastSignal">
                    <Moment at={signals.lastReceivedAt} now={now} />
                </Fact>
                <Fact name="developer.refusedPayloads">{wordNumber(signals.refusedPayloads, locale)}</Fact>
            </Facts>

            <SubHeading name="developer.signalKinds" />
            {kinds.length === 0 ? (
                <Absent />
            ) : (
                <dl className="flex flex-col gap-1 text-md">
                    {kinds.map(([kind, count]) => (
                        <div key={kind} className="flex justify-between gap-3">
                            <dt>
                                <Literal>{kind}</Literal>
                            </dt>
                            <dd>{wordNumber(count, locale)}</dd>
                        </div>
                    ))}
                </dl>
            )}

            <SubHeading name="developer.deliveries" />
            <SignalDeliveries record={record} now={now} />

            <SubHeading name="developer.lastRun" />
            {signals.lastRun === null ? (
                <Absent />
            ) : (
                <Facts>
                    <Fact name="developer.run">
                        {signals.lastRun.run === null ? <Absent /> : <Literal>{signals.lastRun.run}</Literal>}
                    </Fact>
                    <Fact name="developer.conversation">
                        {signals.lastRun.conversation === null ? (
                            <Absent />
                        ) : (
                            <Literal>{signals.lastRun.conversation}</Literal>
                        )}
                    </Fact>
                    <Fact name="developer.sequence">{wordNumber(signals.lastRun.sequence, locale)}</Fact>
                </Facts>
            )}
        </>
    );
}

function RequestsSection({ record, now }: { readonly record: DeveloperRecord; readonly now: number }) {
    const { translate, locale } = useLocalization();
    const pending = record.requests.filter((request) => isPending(request.phase));
    const over = record.requests.filter((request) => !isPending(request.phase)).toReversed();
    const templates = Object.entries(record.byTemplate).sort(([first], [second]) => first.localeCompare(second));

    return (
        <>
            <SubHeading name="developer.pending" />
            {pending.length === 0 ? (
                <Absent name="developer.nothingPending" />
            ) : (
                <RequestList requests={pending.toReversed()} record={record} now={now} />
            )}

            <SubHeading name="developer.recent" />
            {over.length === 0 ? <Absent /> : <RequestList requests={over} record={record} now={now} />}

            <SubHeading name="developer.byOperation" />
            {templates.length === 0 ? (
                <Absent />
            ) : (
                <dl className="flex flex-col gap-1 text-md">
                    {templates.map(([template, counted]) => (
                        <div key={template} className="flex justify-between gap-3">
                            <dt className="min-w-0 wrap-anywhere">
                                <Literal>{template}</Literal>
                            </dt>
                            <dd className="shrink-0">
                                <span className={counted.failed > 0 ? 'text-error-text' : ''}>
                                    {translate('developer.readAndFailed', {
                                        read: wordNumber(counted.read, locale),
                                        failed: wordNumber(counted.failed, locale),
                                    })}
                                </span>
                            </dd>
                        </div>
                    ))}
                </dl>
            )}
        </>
    );
}

function RequestList({
    requests,
    record,
    now,
}: {
    readonly requests: readonly WireRequest[];
    readonly record: DeveloperRecord;
    readonly now: number;
}) {
    const { translate, locale } = useLocalization();

    return (
        <ul className="flex flex-col gap-2">
            {requests.map((request) => {
                const operation = request.traceId === null ? undefined : record.outcomes[request.traceId];
                const took = Math.max(0, (request.endedAt ?? now) - request.askedAt);
                const failed = request.phase === 'failed' || (request.status !== null && request.status >= 400);

                return (
                    <li key={request.key} className="flex flex-col gap-1 rounded-md bg-sunken px-2.5 py-2 text-base">
                        <div className="flex items-baseline gap-2">
                            <Literal>{request.method}</Literal>
                            <span className="min-w-0 flex-1 wrap-anywhere">
                                <Literal>{request.path}</Literal>
                            </span>
                            <span className={failed ? 'text-error-text' : 'text-muted'}>
                                {request.status ?? <Literal>{request.phase}</Literal>}
                            </span>
                        </div>
                        <div className="flex flex-wrap gap-x-3 gap-y-0.5 text-muted">
                            <Literal>{request.phase}</Literal>
                            <span>{wordMilliseconds(took, locale)}</span>
                            {request.attempts > 1 ? (
                                <span>
                                    {translate('developer.attemptOf', {
                                        count: wordNumber(request.attempts, locale),
                                        most: wordNumber(mostRetries + 1, locale),
                                    })}
                                </span>
                            ) : null}
                            {request.retryAt === null ? null : (
                                <span>
                                    {translate('developer.retryAt', {
                                        when: wordFromNow(request.retryAt, now, locale),
                                    })}
                                </span>
                            )}
                            {request.declaredBytes === null ? null : (
                                <span className={request.declaredBytes > longestResponseBody ? 'text-error-text' : ''}>
                                    {wordBytes(request.declaredBytes, locale)}
                                </span>
                            )}
                            {operation === undefined ? null : (
                                <span>
                                    <Literal>{operation.request}</Literal>
                                    {operation.failure === null ? null : (
                                        <span className="text-error-text"> {operation.failure}</span>
                                    )}
                                </span>
                            )}
                        </div>
                        {request.traceId === null ? null : <CopyTrace traceId={request.traceId} />}
                        <details className="text-muted">
                            <summary className="cursor-pointer">{translate('developer.headers')}</summary>
                            <HeaderList name="developer.sentHeaders" headers={request.sentHeaders} />
                            <HeaderList name="developer.receivedHeaders" headers={request.receivedHeaders} />
                        </details>
                    </li>
                );
            })}
        </ul>
    );
}

function HeaderList({ name, headers }: { readonly name: MessageKey; readonly headers: readonly HeaderLine[] | null }) {
    const { translate } = useLocalization();

    return (
        <>
            <SubHeading name={name} />
            {headers === null || headers.length === 0 ? (
                <Absent />
            ) : (
                <dl className="flex flex-col gap-0.5">
                    {headers.map((header, at) => (
                        // A header may arrive more than once, so its place is what tells two of one name apart.
                        <div key={`${header.name}-${String(at)}`} className="flex gap-2">
                            <dt className="shrink-0">
                                <Literal>{header.name}</Literal>
                            </dt>
                            <dd className="min-w-0 wrap-anywhere text-text">
                                {header.value === null ? (
                                    <span className="text-faint">{translate('developer.withheld')}</span>
                                ) : (
                                    <Literal>{header.value}</Literal>
                                )}
                            </dd>
                        </div>
                    ))}
                </dl>
            )}
        </>
    );
}

function SignalDeliveries({ record, now }: { readonly record: DeveloperRecord; readonly now: number }) {
    const { translate } = useLocalization();
    const deliveries = record.deliveries.toReversed();

    return deliveries.length === 0 ? (
        <Absent />
    ) : (
        <ul className="flex flex-col gap-1.5 text-base">
            {deliveries.map((delivery) => (
                <li key={delivery.key} className="rounded-md bg-sunken px-2.5 py-2">
                    <details>
                        <summary className="flex cursor-pointer justify-between gap-2">
                            <Literal>{delivery.kind}</Literal>
                            <span className="text-faint">
                                <Moment at={delivery.at} now={now} />
                            </span>
                        </summary>
                        {delivery.preview === null ? (
                            <p className="pt-1 text-faint">{translate('developer.unreadSignal')}</p>
                        ) : (
                            <pre className="overflow-x-auto pt-1 font-mono text-base wrap-anywhere whitespace-pre-wrap">
                                {JSON.stringify(delivery.preview, null, 2)}
                            </pre>
                        )}
                    </details>
                </li>
            ))}
        </ul>
    );
}

function TelemetrySection({
    record,
    context,
    now,
}: {
    readonly record: DeveloperRecord;
    readonly context: DeveloperContext;
    readonly now: number;
}) {
    const latestTrace = record.requests.findLast((request) => request.traceId !== null)?.traceId ?? null;
    const latest = record.events.toReversed();

    return (
        <>
            <Facts>
                <Fact name="developer.level">
                    {context.telemetryLevel === null ? <Absent /> : <Literal>{context.telemetryLevel}</Literal>}
                </Fact>
                <Fact name="developer.recording">
                    <YesNo value={context.telemetryAllowed} />
                </Fact>
                <Fact name="developer.latestTrace">
                    {latestTrace === null ? <Absent /> : <CopyTrace traceId={latestTrace} />}
                </Fact>
            </Facts>

            <SubHeading name="developer.events" />
            {latest.length === 0 ? (
                <Absent />
            ) : (
                <ul className="flex flex-col gap-1 text-base">
                    {latest.map((noticed) => (
                        <li key={noticed.key} className="flex flex-col gap-0.5">
                            <span className="flex justify-between gap-2">
                                <Literal>{noticed.event}</Literal>
                                <span className="text-faint">
                                    <Moment at={noticed.at} now={now} />
                                </span>
                            </span>
                            {Object.keys(noticed.attributes).length === 0 ? null : (
                                <span className="wrap-anywhere text-muted">
                                    <Literal>
                                        {Object.entries(noticed.attributes)
                                            .map(
                                                ([name, value]) =>
                                                    `${name.replace('mailfathom.client.', '')}=${String(value)}`,
                                            )
                                            .join(' ')}
                                    </Literal>
                                </span>
                            )}
                        </li>
                    ))}
                </ul>
            )}
        </>
    );
}

function SessionSection({ context, now }: { readonly context: DeveloperContext; readonly now: number }) {
    const { translate } = useLocalization();
    const expiresAt = context.credential === null ? null : Date.parse(context.credential.expiresAt);

    return (
        <>
            <Facts>
                <Fact name="developer.clientVersion">
                    <Literal>{__MAILFATHOM_VERSION__}</Literal>
                </Fact>
                <Fact name="developer.serverVersion">
                    {context.deploymentVersion === null ? <Absent /> : <Literal>{context.deploymentVersion}</Literal>}
                </Fact>
                <Fact name="developer.serverRevision">
                    {context.deploymentRevision === null ? <Absent /> : <Literal>{context.deploymentRevision}</Literal>}
                </Fact>
                <Fact name="developer.address">
                    {context.deploymentAddress === null ? <Absent /> : <Literal>{context.deploymentAddress}</Literal>}
                </Fact>
                <Fact name="developer.reachable">
                    <YesNo value={context.reachable} />
                </Fact>
                <Fact name="developer.person">
                    {context.person === null ? <Absent /> : <Literal>{context.person}</Literal>}
                </Fact>
                <Fact name="developer.organization">
                    {context.organization === null ? <Absent /> : <Literal>{context.organization}</Literal>}
                </Fact>
                <Fact name="developer.credential">
                    {context.credential === null ? (
                        <Absent />
                    ) : (
                        translate(
                            context.credential.kind === 'session'
                                ? 'developer.credentialSession'
                                : 'developer.credentialGrant',
                        )
                    )}
                </Fact>
                {context.credential?.issuer == null ? null : (
                    <Fact name="developer.issuer">
                        <Literal>{context.credential.issuer}</Literal>
                    </Fact>
                )}
                <Fact name="developer.expires">
                    <Moment at={expiresAt} now={now} />
                </Fact>
                <Fact name="developer.userAgent">
                    <Literal>{navigator.userAgent}</Literal>
                </Fact>
            </Facts>

            <SubHeading name="developer.accountIdentifiers" />
            {context.accounts.length === 0 ? (
                <Absent />
            ) : (
                <ul className="flex flex-col gap-0.5">
                    {context.accounts.map((account) => (
                        <li key={account.id}>
                            <Literal>{account.id}</Literal>
                        </li>
                    ))}
                </ul>
            )}

            <SubHeading name="developer.permissions" />
            {context.permissions.length === 0 ? (
                <Absent />
            ) : (
                <ul className="flex flex-col gap-0.5">
                    {context.permissions.map((permission) => (
                        <li key={permission}>
                            <Literal>{permission}</Literal>
                        </li>
                    ))}
                </ul>
            )}
        </>
    );
}

function DataSection({ context, now }: { readonly context: DeveloperContext; readonly now: number }) {
    const { translate, locale } = useLocalization();
    const changes = usePendingChanges();

    return (
        <>
            <Facts>
                <Fact name="developer.space">
                    {context.space === null ? <Absent /> : <Literal>{context.space}</Literal>}
                </Fact>
                <Fact name="developer.synchronizationEnabled">
                    {context.synchronizationEnabled === null ? (
                        <Absent />
                    ) : (
                        <YesNo value={context.synchronizationEnabled} />
                    )}
                </Fact>
            </Facts>

            <SubHeading name="developer.accounts" />
            {context.accounts.length === 0 ? (
                <Absent />
            ) : (
                <ul className="flex flex-col gap-2">
                    {context.accounts.map((account) => (
                        <li
                            key={account.id}
                            className="flex flex-col gap-0.5 rounded-md bg-sunken px-2.5 py-2 text-base"
                        >
                            <Literal>{account.id}</Literal>
                            <span className="flex flex-wrap gap-x-3 text-muted">
                                <Literal>{account.synchronizationState}</Literal>
                                {account.behind ? <span>{translate('developer.behind')}</span> : null}
                                <span>
                                    <Moment
                                        at={
                                            account.lastSynchronizedAt === null
                                                ? null
                                                : Date.parse(account.lastSynchronizedAt)
                                        }
                                        now={now}
                                    />
                                </span>
                            </span>
                        </li>
                    ))}
                </ul>
            )}

            <SubHeading name="developer.changes" />
            <Facts>
                <Fact name="developer.changesWaiting">{wordNumber(changes.waiting.length, locale)}</Fact>
                <Fact name="developer.changesUndecided">{wordNumber(changes.undecided.length, locale)}</Fact>
                <Fact name="developer.followingStopped">
                    <YesNo value={changes.stoppedFollowing} />
                </Fact>
            </Facts>
            {changes.waiting.length + changes.undecided.length === 0 ? null : (
                <ul className="flex flex-col gap-0.5 text-base">
                    {[...changes.waiting, ...changes.undecided].map((change) => (
                        <li key={change.recordId} className="flex flex-wrap gap-x-2">
                            <Literal>{change.act}</Literal>
                            <span className="text-muted">
                                <Literal>{change.recordId}</Literal>
                            </span>
                            <span className="text-faint">
                                <Literal>{change.storedEmailId}</Literal>
                            </span>
                        </li>
                    ))}
                </ul>
            )}
        </>
    );
}

const vitalOrder: readonly VitalName[] = ['lcp', 'inp', 'cls', 'fcp', 'ttfb'];

function EnvironmentSection({ record }: { readonly record: DeveloperRecord }) {
    const { translate, locale } = useLocalization();
    const { theme, choice } = useTheme();
    const reached = {
        workspace: useWideWorkspace(),
        panes: useTwoPanes(),
        roomy: useRoomyComposition(),
        desktop: useDesktopComposition(),
        low: useLowWindow(),
        tall: useTallWindow(),
    };
    const breakpoints = Object.entries(reached)
        .filter(([, holds]) => holds)
        .map(([name]) => name)
        .join(' ');
    const coarse = useCoarsePointer();
    const failures = Object.entries(record.renderFailures);

    return (
        <>
            <Facts>
                <Fact name="developer.viewport">
                    <Literal>
                        {translate('developer.measuredSize', {
                            width: String(window.innerWidth),
                            height: String(window.innerHeight),
                        })}
                    </Literal>
                </Fact>
                <Fact name="developer.pixelRatio">{wordNumber(window.devicePixelRatio, locale, 2)}</Fact>
                <Fact name="developer.breakpoints">
                    {breakpoints === '' ? <Absent /> : <Literal>{breakpoints}</Literal>}
                </Fact>
                <Fact name="developer.pointer">
                    <Literal>{coarse ? 'coarse' : 'fine'}</Literal>
                </Fact>
                <Fact name="developer.theme">
                    <Literal>{translate('developer.themeChoice', { theme, choice })}</Literal>
                </Fact>
                <Fact name="developer.language">
                    <Literal>{locale}</Literal>
                </Fact>
                <Fact name="developer.reducedMotion">
                    <YesNo value={window.matchMedia('(prefers-reduced-motion: reduce)').matches} />
                </Fact>
                <Fact name="developer.online">
                    <YesNo value={navigator.onLine} />
                </Fact>
                <Fact name="developer.visibility">
                    <Literal>{document.visibilityState}</Literal>
                </Fact>
            </Facts>

            <SubHeading name="developer.vitals" />
            <Facts>
                {vitalOrder.map((name) => {
                    const reading = record.vitals[name];

                    return (
                        <div key={name} className="flex items-baseline justify-between gap-3">
                            <dt className="text-muted">
                                <Literal>{name.toUpperCase()}</Literal>
                            </dt>
                            <dd>
                                {reading === undefined ? (
                                    <Absent name="developer.notMeasured" />
                                ) : name === 'cls' ? (
                                    wordNumber(reading.value, locale, 3)
                                ) : (
                                    wordMilliseconds(reading.value, locale)
                                )}
                            </dd>
                        </div>
                    );
                })}
            </Facts>

            <SubHeading name="developer.renderFailures" />
            {failures.length === 0 ? (
                <Absent />
            ) : (
                <dl className="flex flex-col gap-1 text-md">
                    {failures.map(([region, count]) => (
                        <div key={region} className="flex justify-between gap-3">
                            <dt>
                                <Literal>{region}</Literal>
                            </dt>
                            <dd className="text-error-text">{wordNumber(count, locale)}</dd>
                        </div>
                    ))}
                </dl>
            )}
        </>
    );
}
