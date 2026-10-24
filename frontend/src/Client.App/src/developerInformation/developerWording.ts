// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { Locale } from '../localization/locale';

// How the developer information surface words a moment, a duration, and a size: each by `Intl` under the language being
// read, and a moment relative to now rather than as a time of day, which is what somebody watching a client wants to
// know about it and needs no zone to be right.

/** How long ago or how far ahead an instant is, from `now`. */
export function wordFromNow(at: number, now: number, locale: Locale): string {
    const seconds = Math.round((at - now) / 1_000);
    const words = new Intl.RelativeTimeFormat(locale, { numeric: 'auto', style: 'short' });

    if (Math.abs(seconds) < 60) {
        return words.format(seconds, 'second');
    }

    const minutes = Math.round(seconds / 60);

    if (Math.abs(minutes) < 60) {
        return words.format(minutes, 'minute');
    }

    const hours = Math.round(minutes / 60);

    return Math.abs(hours) < 48 ? words.format(hours, 'hour') : words.format(Math.round(hours / 24), 'day');
}

/** A duration in milliseconds. */
export function wordMilliseconds(milliseconds: number, locale: Locale): string {
    return new Intl.NumberFormat(locale, { style: 'unit', unit: 'millisecond', maximumFractionDigits: 0 }).format(
        milliseconds,
    );
}

/** A size in bytes, in kilobytes once it is past one. */
export function wordBytes(bytes: number, locale: Locale): string {
    return bytes < 1_024
        ? new Intl.NumberFormat(locale, { style: 'unit', unit: 'byte' }).format(bytes)
        : new Intl.NumberFormat(locale, { style: 'unit', unit: 'kilobyte', maximumFractionDigits: 1 }).format(
              bytes / 1_024,
          );
}

/** A plain number, as the language being read writes one. */
export function wordNumber(value: number, locale: Locale, fractionDigits = 0): string {
    return new Intl.NumberFormat(locale, { maximumFractionDigits: fractionDigits }).format(value);
}
