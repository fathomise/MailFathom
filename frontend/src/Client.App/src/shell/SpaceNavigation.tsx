// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Fragment, useEffect, useRef, useState, type PointerEvent, type ReactNode } from 'react';
import { BrandMark } from '../controls/BrandMark';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { addressOf, askingSpaces, barSpaces, overflowSpaces, spaceLabels, type Space } from '../routing/spaces';
import { ConnectionAlarm, ConnectionAnnouncement } from './ConnectionStatusLine';
import { spaceIcons } from './spaceIcons';
import { NavigationMark } from './NavigationMark';
import { SpacePeek, type PeekAnchor } from './SpacePeek';
import type { SpaceReport } from './spaceReport';
import { SpaceOverflow } from './SpaceOverflow';
import { useNavigationRail, useWideWorkspace } from './useWideWorkspace';

// One list of links, laid out two ways: a rail down the side of a wide window, and bottom navigation across a narrow
// one — or across a fold or a small tablet held upright under a finger, which is where `rail:` and `useNavigationRail`
// carry the one exception to the width deciding it. Nothing here asks which head it is running on, and nothing is dropped at either
// width — everything the rail offers is reachable from the bar too, which is what makes the two shapes one navigation.
//
// **The two shapes hold a different number of things, and that is the one part CSS cannot lay out.** A rail has room
// for all six spaces, the bell, and the account down the side of the window. A bar has five places across the foot
// of a phone, and the design spends them on three spaces, the bell, and an overflow — so what the bar has no room
// for stands in a sheet behind that fifth item rather than being hidden by width, which is what the width question is
// asked for. Which three the bar carries is `routing/spaces.ts`, beside the order they are drawn in. A bar across a
// wider window — the upright fold and tablet — has room for every space, so only the account and what stands at the
// rail's foot go behind the overflow there.
//
// Which destinations there are at all is the session's answer rather than this component's: a space this credential
// may not open is absent from both shapes, because offering it would offer an action the deployment is going to refuse.
//
// Links rather than buttons, because these navigate: the browser then supplies the keyboard path, the history entry,
// and opening one in a window of its own, none of which a click handler would have.
//
// The account stands at the foot of the rail and at the foot of the overflow sheet, which is where the design
// puts what is about the person rather than about a space: last in both, and never in the bar itself, where the five
// places are spent. It is offered before the deployment has answered which spaces there are, because signing out and
// pointing the client elsewhere are the way out of a deployment that never answers.

export function SpaceNavigation({
    offered,
    current,
    account,
    presence = null,
    notifications,
    refresh,
    developer,
    reports = {},
    onPointerDown,
    onClickCapture,
}: {
    readonly offered: readonly Space[];

    /** The space being shown, or `null` while the deployment has not yet said which spaces there are. */
    readonly current: Space | null;

    /** The control that opens the account menu, which the navigation places rather than draws. */
    readonly account: ReactNode;

    /**
     * The row leading to the member's status, which heads the overflow sheet on a phone. In the rail it stands in the
     * account menu instead, so nothing is placed for it there.
     */
    readonly presence?: ReactNode;

    /** The bell, which is the one thing here that is not a place to go and stands beside the account for that reason. */
    readonly notifications: ReactNode;

    /** The control that reads again what the client is already showing, which stands above the bell for the same reason. */
    readonly refresh: ReactNode;

    /** The control that opens the developer information surface, which draws nothing until Settings has unlocked it. */
    readonly developer: ReactNode;

    /** What each space reports on its tile from wherever the reader is, and nothing for a space that reports nothing. */
    readonly reports?: Partial<Readonly<Record<Space, SpaceReport>>>;

    /**
     * What an upward swipe anywhere on the bar begins, which is the second way into the notification centre on a
     * phone. It is bound here rather than on the bell because the design gives the gesture the whole bar.
     */
    readonly onPointerDown: (event: PointerEvent) => void;

    /** What keeps the tap ending such a swipe from also following the link it started on. */
    readonly onClickCapture: (event: { preventDefault: () => void; stopPropagation: () => void }) => void;
}) {
    const { translate } = useLocalization();
    const wide = useWideWorkspace();
    const rail = useNavigationRail();

    // The rail draws everything it was offered in the order it was offered; the bar draws the three it has room for,
    // and the overflow draws the rest in its own order. Every one of the three is what the session offered filtered
    // against a list in `routing/spaces.ts`, so a space this credential may not open is absent from whichever shape is
    // on the screen without either shape having to be told about it twice.
    const drawn = wide ? offered : offered.filter((space) => barSpaces.includes(space));
    const behindMore = wide ? [] : overflowSpaces.filter((space) => offered.includes(space));
    const firstHolding = drawn.findIndex((space) => !askingSpaces.includes(space));
    const destinations = drawn.map((space, index) => (
        <Fragment key={space}>
            {/* A hairline between the spaces that ask and the spaces that hold, drawn in the rail alone: the bar's places
                are counted, and a rule there would read as one more. */}
            {rail && index > 0 && index === firstHolding ? (
                <hr aria-hidden="true" className="mx-4 my-1 border-line" />
            ) : null}

            <SpaceLink space={space} current={space === current} report={reports[space] ?? null} peeks={rail} />
        </Fragment>
    ));

    return (
        <nav
            aria-label={translate('shell.spaces')}
            // The bar is `pan-x` so a finger moving up it is this navigation's rather than the page's, which is what a
            // gesture that has to be followed one to one needs; the rail above the workspace breakpoint has no such
            // gesture and is unaffected by it.
            className="flex shrink-0 touch-pan-x justify-center gap-0.75 border-t border-line bg-rail px-1.5 pt-1.5 pb-2 rail:order-first rail:min-h-0 rail:w-rail-narrow rail:touch-auto desktop:w-rail rail:flex-col rail:justify-start rail:gap-0.5 roomy:gap-1 rail:overflow-y-auto rail:border-t-0 rail:border-e rail:px-0 rail:pt-4 rail:pb-3 min-h-navigation"
            onPointerDown={onPointerDown}
            onClickCapture={onClickCapture}
        >
            {/* The mark stands at the top of the rail and nowhere in the bottom bar: a narrow window gives the row to
                destinations, and a logo taking one of five places there would cost a reader a space to reach. A low
                window gives its height to them for the same reason. */}
            <BrandMark
                label={translate('shell.title')}
                className="mb-3 hidden size-10 self-center rail:block low:hidden"
            />

            {/* In the rail the destinations scroll between its top and its foot, so the bell and the account stay where
                they are however short the window is; in the bar they are the bar's own places, laid out by it. The
                region clips at its padding, so it carries room for a focus ring above the first and below the last and
                gives the same room back outside, which leaves the rail laid out as it was. It stands in both shapes,
                drawing no box of its own in the bar, so a window crossing the workspace width keeps the same tree and
                remounts no destination — a remount there is a render the list keeping its place would have to race. */}
            <div className="contents rail:-my-1 rail:flex rail:min-h-0 rail:flex-col rail:gap-0.5 rail:overflow-y-auto rail:py-1 roomy:gap-1">
                {destinations}
            </div>

            {/* The bell and the account, in that order at both widths: what happened while nobody was looking stands
                beside who is looking. In the rail that is the foot of it, with the bell above the account; in the bar
                the bell is the fourth place and the account has moved behind the fifth, which is what the design
                draws and what leaves the bar five items wide however many spaces the session offers. */}
            {rail ? (
                <div className="mt-auto flex flex-none flex-col items-center justify-center gap-3.5 pt-3">
                    {developer}
                    <ConnectionAlarm />
                    {refresh}
                    {notifications}
                    {account}
                </div>
            ) : (
                <>
                    {notifications}
                    <SpaceOverflow
                        spaces={behindMore}
                        current={current}
                        account={account}
                        presence={presence}
                        refresh={refresh}
                        developer={developer}
                    />
                </>
            )}

            <ConnectionAnnouncement />
        </nav>
    );
}

/** How long a peek stays once the pointer has left both it and its tile, so the pointer can cross the gap between. */
const peekLingers = 160;

/** The gap between the tile's edge and the peek beside it. */
const peekGap = 10;

function SpaceLink({
    space,
    current,
    report,
    peeks,
}: {
    readonly space: Space;
    readonly current: boolean;
    readonly report: SpaceReport | null;

    /** Whether a resting pointer slides a peek out, which only the rail does. */
    readonly peeks: boolean;
}) {
    const { locale, translate } = useLocalization();
    const name = translate(spaceLabels[space]);
    const [anchor, setAnchor] = useState<PeekAnchor | null>(null);
    const leaving = useRef<ReturnType<typeof setTimeout> | null>(null);

    // The timer the pointer leaving starts is the one thing here outside React, so it goes with the tile.
    useEffect(
        () => () => {
            if (leaving.current !== null) {
                clearTimeout(leaving.current);
            }
        },
        [],
    );

    function keep(): void {
        if (leaving.current !== null) {
            clearTimeout(leaving.current);
            leaving.current = null;
        }
    }

    function leave(): void {
        keep();
        leaving.current = setTimeout(() => {
            leaving.current = null;
            setAnchor(null);
        }, peekLingers);
    }

    function close(): void {
        keep();
        setAnchor(null);
    }

    const counted =
        report === null || report.counts.length === 0
            ? name
            : translate('shell.spaceWithCounts', {
                  space: name,
                  counts: new Intl.ListFormat(locale, { type: 'unit', style: 'short' }).format(report.counts),
              });

    return (
        <>
            <a
                href={addressOf(space)}
                aria-current={current ? 'page' : undefined}
                aria-label={counted === name ? undefined : counted}
                onPointerEnter={(event) => {
                    // A finger has no hover, and a pointer it fakes one for would open the peek on the press that
                    // navigates; the bar has no room beside it either.
                    if (report === null || !peeks || event.pointerType !== 'mouse') {
                        return;
                    }

                    const box = event.currentTarget.getBoundingClientRect();

                    keep();
                    setAnchor({ left: box.right + peekGap, middle: box.top + box.height / 2 });
                }}
                onPointerLeave={anchor === null ? undefined : leave}
                onClick={anchor === null ? undefined : close}
                // In the rail each destination is a tile inset from both edges, so two neighbours never meet across a
                // dark gap and the current one needs no edge bar to stand out — the tint and the weight carry it. The
                // bottom bar marks it with the pill behind the symbol instead.
                // A low window draws the symbol alone, the name staying the link's, in a target the design sizes by the
                // pointer: 36 under a mouse and 44 under a finger, stated here because the touch floor reaches controls
                // and a link is not one.
                className={`group relative flex max-w-30 flex-1 flex-col items-center gap-1 rounded-xl px-0.5 py-1.5 text-sm transition rail:mx-1.75 rail:flex-none rail:px-0 rail:gap-1 rail:rounded-lg rail:py-2 desktop:py-2.25 rail:low:min-h-9 rail:low:justify-center rail:low:py-1 rail:low:pointer-coarse:min-h-11 rail:low:pointer-coarse:py-0 ${
                    current
                        ? 'font-semibold text-accent-deep rail:bg-accent-soft'
                        : 'text-muted hover:bg-hover hover:text-text'
                }`}
            >
                <NavigationMark current={current} className="relative rail:static">
                    <Icon name={spaceIcons[space]} className="size-icon-2xl rail:size-icon-xl" />

                    {/* In the corner of the bar's pill, and of the rail's tile, inset so the pulse's ring never crosses the
                    edge it sits in. */}
                    {report?.dot == null ? null : (
                        <span
                            aria-hidden="true"
                            className={`pointer-events-none absolute top-1.5 end-1.5 size-2 rounded-full bg-error rail:top-2 rail:end-2 ${
                                report.dot === 'working' ? 'animate-rail-ring' : ''
                            }`}
                        />
                    )}
                </NavigationMark>
                <span className="max-w-full truncate rail:overflow-visible rail:low:sr-only">{name}</span>
            </a>

            {anchor === null || report === null ? null : (
                <SpacePeek name={name} report={report} anchor={anchor} onKeep={keep} onLeave={leave} onOpened={close} />
            )}
        </>
    );
}
