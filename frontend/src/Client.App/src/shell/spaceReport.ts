// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What a space says about itself on its rail tile, from wherever the reader is: a dot, the counts the tile's name
// carries, and the peek a resting pointer slides out. One shape for every space, so a second space reporting on its
// tile supplies its own sections rather than a second dot or a second peek.

/** Whether something there is under way, which pulses, or only waits to be read, which stands still. */
export type SpaceReportDot = 'working' | 'waiting';

/** One row of a peek: what it is called, the line under it, and the press that opens it. */
export interface SpacePeekRow {
    readonly key: string;
    readonly title: string;
    readonly detail: string;
    readonly open: () => void;
}

/** One kind of thing a peek lists, under its own heading, every row in it led the way its kind is. */
export interface SpacePeekSection {
    readonly label: string;
    readonly kind: SpaceReportDot;
    readonly rows: readonly SpacePeekRow[];
}

/** What one space reports on its tile. */
export interface SpaceReport {
    /** The dot the tile carries, and `null` where nothing is under way and nothing waits. */
    readonly dot: SpaceReportDot | null;

    /** Each count, worded, which the tile's name and the peek's head both carry; empty where there is nothing to count. */
    readonly counts: readonly string[];

    /** The sections the peek lists, each with at least one row. */
    readonly sections: readonly SpacePeekSection[];

    /** What the peek says where it has no section at all. */
    readonly empty: string;
}
