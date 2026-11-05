// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useState, type ReactNode } from 'react';
import type { ClientFailureReason, MailAccount } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { wordRecentInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { synchronizationStateLabel } from '../synchronization/synchronizationState';
import { nextFreshnessChangeIn, staleAccounts, type StaleAccount } from './accountFreshness';
import { offers } from './capabilities';
import { disconnected, useConnectionStatus } from './connectionStatus';
import { mostReconnectionAttempts, type Connection } from './useConnection';

// The line at the foot of Mail's folder column saying what the client is looking at and how current it is, and the short
// panel above it naming each account once one of them needs a look. Three different things blur into one spinner unless
// they are kept apart, so each has its own sentence: whether this client can reach its deployment at all, whether that
// deployment is refreshing these accounts, and which of them is not current.
//
// It is the one place that says a change MailFathom holds has not reached a mail server: no message, row, or toast
// marks one. `accountFreshness.ts` holds the rule for when an account is not current — a cause that clears inside a
// minute never shows — and this line draws what the rule answers. Sends are not among them; the Outbox reports those.
//
// A credential that may not read mail says nothing here. There is no freshness to report and the reason is not about
// this line, so it is said once, where everything this credential may not do is said.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

type Tone = 'healthy' | 'attention' | 'quiet';

const toneColours: Readonly<Record<Tone, string>> = {
    healthy: 'bg-healthy',
    attention: 'bg-warning',
    quiet: 'bg-faint',
};

/** The forms one count is said in, by the rule `Intl.PluralRules` selects. */
type Counted = Readonly<Record<Intl.LDMLPluralRule, MessageKey>>;

function counted(family: 'freshness.waiting' | 'freshness.waitingText' | 'freshness.unreachableText'): Counted {
    return {
        zero: `${family}.other`,
        one: `${family}.one`,
        two: `${family}.other`,
        few: `${family}.few`,
        many: `${family}.many`,
        other: `${family}.other`,
    };
}

const waitingStates = counted('freshness.waiting');
const waitingTexts = counted('freshness.waitingText');
const unreachableTexts = counted('freshness.unreachableText');

export function ConnectionSummary({ connection }: { readonly connection: Connection }) {
    const { locale, translate } = useLocalization();
    const status = useConnectionStatus();
    const { session, accounts, online, attempts, reread } = connection;

    // Once a session stands, whether the server is reachable is the status's to say, in the user menu; what this line
    // owes then is only that, without the server, it cannot know how current the mailboxes are. Before one stands this
    // line is the only thing on the screen, so it still says what the connection is doing.
    if (session?.outcome === 'read' && disconnected(status)) {
        return (
            <Line tone="quiet" announced>
                {translate('connection.unknown')}
            </Line>
        );
    }

    // Nothing is being read and nothing will be until the network comes back, which is a different sentence from a
    // deployment that is not answering: one of them is this machine's to fix and the other is not.
    if (!online) {
        return (
            <Line tone="attention" announced>
                {translate('connection.offline')}
            </Line>
        );
    }

    if (session === null) {
        return (
            <Line tone="quiet" announced>
                {attempts === 0
                    ? translate('connection.connecting')
                    : translate('connection.reconnecting', {
                          attempt: formatCount(locale, attempts),
                          total: formatCount(locale, mostReconnectionAttempts),
                      })}
            </Line>
        );
    }

    if (session.outcome === 'failed') {
        return <Unreachable failure={session.failure.reason} attempts={attempts} reread={reread} />;
    }

    if (!offers(session.value, 'readMail')) {
        return null;
    }

    if (accounts === null) {
        return (
            <Line tone="quiet" announced>
                {translate('accounts.reading')}
            </Line>
        );
    }

    if (accounts.outcome === 'failed') {
        return (
            <Line tone="attention">
                {translate('accounts.failed', { reason: translate(failureLabels[accounts.failure.reason]) })}

                {/* Reading again is the way out of exactly one of the five failures. A refused credential, a missing
                    grant, an answer this client cannot parse, and something the deployment no longer holds each repeat
                    identically on a second attempt, so offering the button there hands somebody an action that cannot
                    work and says nothing about why. Their next steps — signing in again, saying the grant is missing,
                    reporting a defect, letting go of what is gone — are actions this frame has nowhere to send anybody
                    to yet, and each arrives with the screen that can. */}
                {accounts.failure.reason === 'unavailable' && (
                    <SecondaryButton label={translate('connection.retry')} onActivate={reread} />
                )}
            </Line>
        );
    }

    const directory = accounts.value;

    // A user holding no account is told so and told what would fill it, rather than being handed a disclosure with
    // nothing behind it.
    if (directory.accounts.length === 0) {
        return (
            <Line tone="quiet">
                {translate('connection.noAccounts')} {translate('accounts.noneDeclared')}
            </Line>
        );
    }

    // A deployment not refreshing them is one sentence about the deployment rather than about any account.
    if (!directory.synchronizationEnabled) {
        return <Line tone="quiet">{translate('accounts.notRefreshing')}</Line>;
    }

    return <AccountsFreshness accounts={directory.accounts} readAt={connection.accountsReadAt} />;
}

// Every account current is said by the green dot and nothing more, as the design draws it: a disclosure there would
// open onto a list of accounts each saying the same thing. Only an account that needs a look earns the details, which
// open above the line, inside the folder column, and go on their own once every account is current again.
function AccountsFreshness({
    accounts,
    readAt,
}: {
    readonly accounts: readonly MailAccount[];
    readonly readAt: number | null;
}) {
    const { translate } = useLocalization();
    const [clock, setClock] = useState(() => Date.now());

    // The answer can only be as old as the read that brought it, so a clock that has not ticked since is never read as
    // earlier than that.
    const now = Math.max(clock, readAt ?? 0);
    const stale = staleAccounts(accounts, now);
    const nextChange = nextFreshnessChangeIn(accounts, now);

    // The clock moves at the instant some cause ends its minute, so the line turns when it is due rather than at the
    // next unrelated render, and is waited for again each time it moves, since a second cause may still be inside its
    // minute.
    useEffect(() => {
        if (nextChange === null) {
            return;
        }

        const waiting = window.setTimeout(() => {
            setClock(Date.now());
        }, nextChange);

        return () => {
            window.clearTimeout(waiting);
        };
    }, [nextChange]);

    if (stale.length === 0) {
        return <Line tone="healthy">{translate('connection.current')}</Line>;
    }

    return <NotCurrent accounts={accounts} stale={stale} now={now} />;
}

/**
 * The line once some account is not current: a control naming it, which opens the details above itself.
 *
 * A component of its own so that whether the details are open belongs to one stretch of not being current: once every
 * account is current again the line is green and this goes with what it held, so the next account to fall behind is
 * said closed rather than opening onto it.
 */
function NotCurrent({
    accounts,
    stale,
    now,
}: {
    readonly accounts: readonly MailAccount[];
    readonly stale: readonly StaleAccount[];
    readonly now: number;
}) {
    const { translate } = useLocalization();
    const [open, setOpen] = useState(false);
    const detailsId = useId();
    const first = stale[0];
    const sentence =
        stale.length === 1 && first !== undefined
            ? translate('freshness.notCurrentOne', { account: first.account.displayName })
            : translate('freshness.notCurrentSome');
    const current = accounts.filter((account) => !stale.some((one) => one.account.id === account.id));
    const onlyCurrent = current.length === 1 ? current[0] : undefined;

    return (
        <div className="flex flex-col gap-2.25 text-sm">
            {open ? (
                <div id={detailsId} className="flex flex-col gap-2.25 rounded-md bg-sunken px-3 py-2.5">
                    <ul className="flex flex-col gap-2.25">
                        {stale.map((one) => (
                            <StaleAccountRow key={one.account.id} stale={one} now={now} />
                        ))}
                    </ul>

                    {current.length === 0 ? null : (
                        <p className="border-t border-line-soft pt-2 text-xs text-muted">
                            {onlyCurrent === undefined
                                ? translate('freshness.restAll')
                                : translate('freshness.restOne', { account: onlyCurrent.displayName })}
                        </p>
                    )}
                </div>
            ) : null}

            <button
                type="button"
                aria-expanded={open}
                aria-controls={open ? detailsId : undefined}
                onClick={() => {
                    setOpen((was) => !was);
                }}
                className="-mx-1.5 flex min-h-7.5 items-center gap-2 rounded-md px-1.5 text-start text-text-soft transition hover:bg-hover"
            >
                <Dot tone="attention" />
                <span role="status" className="min-w-0 flex-1 text-pretty">
                    {sentence}
                </span>
                <Icon name={open ? 'expand_more' : 'expand_less'} className="size-icon-sm shrink-0 text-muted" />
            </button>
        </div>
    );
}

/** One account that is not current: its name, what holds it up, and where changes wait, that they are kept. */
function StaleAccountRow({ stale: { account, cause }, now }: { readonly stale: StaleAccount; readonly now: number }) {
    const { locale, translate } = useLocalization();
    const zone = useReadingZone();
    const plural = new Intl.PluralRules(locale);
    const formatted = (count: number): string => new Intl.NumberFormat(locale).format(count);

    function heldUp(): { readonly state: string; readonly text: string | null } {
        switch (cause.kind) {
            case 'unreachable': {
                const since = wordRecentInstant(cause.since, locale, now, zone);

                if (since === null) {
                    return { state: translate('freshness.unreachable'), text: null };
                }

                return {
                    state: translate('freshness.unreachable'),
                    text:
                        cause.waiting === 0
                            ? translate('freshness.unreachableNothingWaiting', { since })
                            : translate(unreachableTexts[plural.select(cause.waiting)], {
                                  since,
                                  count: formatted(cause.waiting),
                              }),
                };
            }
            case 'waiting':
                return {
                    state: translate(waitingStates[plural.select(cause.waiting)], { count: formatted(cause.waiting) }),
                    text: translate(waitingTexts[plural.select(cause.waiting)], { count: formatted(cause.waiting) }),
                };
            case 'state':
                return {
                    state: translate(synchronizationStateLabel(account.synchronizationState, account.behind)),
                    text: null,
                };
        }
    }

    const { state, text } = heldUp();

    return (
        <li className="flex min-w-0 flex-col gap-0.75">
            <span className="flex min-w-0 items-center gap-2">
                <Dot tone="attention" />
                <span className="truncate font-semibold text-text">{account.displayName}</span>
            </span>
            <span className="ps-3.75 text-xs font-semibold text-warning-text">{state}</span>
            {text === null ? null : <span className="ps-3.75 text-xs/relaxed text-pretty text-text-soft">{text}</span>}
        </li>
    );
}

// A deployment that did not answer is reached for again on its own, so what this says is which of those two moments it
// is: another attempt is coming, or the budget is spent and it is a person's turn to ask. Every other failure of the
// session read repeats identically however often it is asked, so it is named and offers nothing.
function Unreachable({
    failure,
    attempts,
    reread,
}: {
    readonly failure: ClientFailureReason;
    readonly attempts: number;
    readonly reread: () => void;
}) {
    const { locale, translate } = useLocalization();
    const total = formatCount(locale, mostReconnectionAttempts);

    // What could not be read here is the session rather than the accounts, and the sentence says so: an answer this
    // client cannot act on is a different situation from a deployment that would not give up the mailboxes.
    if (failure !== 'unavailable') {
        return (
            <Line tone="attention">
                {translate('connection.unreadable', { reason: translate(failureLabels[failure]) })}
            </Line>
        );
    }

    if (attempts < mostReconnectionAttempts) {
        return (
            <Line tone="attention" announced>
                {translate('connection.reconnecting', { attempt: formatCount(locale, attempts + 1), total })}
            </Line>
        );
    }

    return (
        <Line tone="attention">
            {translate('connection.lost', { total })}
            <SecondaryButton label={translate('connection.retry')} onActivate={reread} />
        </Line>
    );
}

// Small whole numbers, formatted rather than interpolated, for the reason no date is spelled into a catalogue: which
// digits a language writes is `Intl`'s answer rather than JavaScript's default one.
function formatCount(locale: string, count: number): string {
    return new Intl.NumberFormat(locale).format(count);
}

function Dot({ tone }: { readonly tone: Tone }) {
    return <span aria-hidden="true" className={`size-1.75 shrink-0 rounded-full ${toneColours[tone]}`} />;
}

function Line({
    tone,
    announced = false,
    children,
}: {
    readonly tone: Tone;
    readonly announced?: boolean;
    readonly children: ReactNode;
}) {
    return (
        <p className="flex items-center gap-2 text-sm text-muted" role={announced ? 'status' : undefined}>
            <Dot tone={tone} />
            {children}
        </p>
    );
}
