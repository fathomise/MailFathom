// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import type { SpacePeekRow, SpacePeekSection, SpaceReport } from './spaceReport';

/** Where the tile a peek comes out of stands: the rail's edge beside it, and the height of its middle. */
export interface PeekAnchor {
    readonly left: number;
    readonly middle: number;
}

/** How far the peek's head sits above the tile's middle, so the beak meets the tile level with its symbol. */
const headAboveMiddle = 30;

/** The closest the peek comes to the window's top and bottom edges. */
const windowMargin = 8;

/** Half the beak's diagonal, which is what centres it on the tile's middle. */
const beakHalf = 6;

/**
 * What a pointer resting on a rail tile slides out: the space's name and its counts, then one section per kind of thing
 * it holds, each row opening what it names. It is a mouse's alone — a finger has the dot, and the place itself draws
 * the same facts — so it is reached by no key either: the tile's own name carries the counts for anybody not pointing.
 */
export function SpacePeek({
    name,
    report,
    anchor,
    onKeep,
    onLeave,
    onOpened,
}: {
    readonly name: string;
    readonly report: SpaceReport;
    readonly anchor: PeekAnchor;
    readonly onKeep: () => void;
    readonly onLeave: () => void;
    readonly onOpened: () => void;
}) {
    const top = Math.max(windowMargin, anchor.middle - headAboveMiddle);

    return (
        <div
            role="dialog"
            aria-label={name}
            onPointerEnter={onKeep}
            onPointerLeave={onLeave}
            className="fixed z-60 box-border flex w-75 animate-pop-in flex-col rounded-lg border border-line bg-panel shadow-overlay"
            style={{
                left: anchor.left,
                top,
                maxHeight: `calc(100vh - ${String(top + windowMargin)}px)`,
                transformOrigin: `0 ${String(anchor.middle - top)}px`,
            }}
        >
            <span
                aria-hidden="true"
                className="absolute -start-1.5 size-2.75 rotate-45 rounded-bl-xs border-b border-s border-line bg-panel"
                style={{ top: anchor.middle - top - beakHalf }}
            />

            <div className="relative flex items-baseline gap-2 border-b border-line-soft px-3.5 pt-2.75 pb-2.25">
                <div className="text-base font-semibold text-text">{name}</div>
                <div className="ms-auto text-xs tabular-nums text-faint">{report.counts.join(' · ')}</div>
            </div>

            <div className="relative flex flex-col gap-1.5 overflow-auto px-1.5 pt-1.5 pb-2">
                {report.sections.map((section) => (
                    <PeekSection key={section.label} section={section} onOpened={onOpened} />
                ))}

                {report.sections.length === 0 ? (
                    <div className="px-2 py-2.5 text-base text-muted">{report.empty}</div>
                ) : null}
            </div>
        </div>
    );
}

function PeekSection({ section, onOpened }: { readonly section: SpacePeekSection; readonly onOpened: () => void }) {
    return (
        <div className="flex flex-col gap-px">
            <div className="px-2 pt-1.5 pb-1 text-xs font-semibold text-faint">{section.label}</div>

            {section.rows.map((row) => (
                <PeekRow key={row.key} row={row} working={section.kind === 'working'} onOpened={onOpened} />
            ))}
        </div>
    );
}

function PeekRow({
    row,
    working,
    onOpened,
}: {
    readonly row: SpacePeekRow;
    readonly working: boolean;
    readonly onOpened: () => void;
}) {
    return (
        <button
            type="button"
            className="flex items-start gap-2.5 rounded-md px-2 py-1.75 text-start hover:bg-hover"
            onClick={() => {
                onOpened();
                row.open();
            }}
        >
            {working ? (
                <Icon name="progress_activity" className="mt-px size-icon-sm animate-spin text-accent" />
            ) : (
                <span aria-hidden="true" className="flex h-4.5 w-4 shrink-0 items-center justify-center">
                    <span className="size-2 rounded-full bg-error" />
                </span>
            )}

            <span className="flex min-w-0 flex-1 flex-col gap-px">
                <span className={`truncate text-base/snug text-text ${working ? 'font-medium' : 'font-bold'}`}>
                    {row.title}
                </span>
                <span className={`truncate text-sm ${working ? 'text-accent-deep' : 'text-muted'}`}>{row.detail}</span>
            </span>
        </button>
    );
}
