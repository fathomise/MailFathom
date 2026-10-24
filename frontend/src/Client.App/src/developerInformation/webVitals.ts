// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { VitalName } from './developerRecord';

// The Web Vitals this page measured about itself, read from the platform's own `PerformanceObserver` rather than from a
// package: the five are each an entry type the browser already reports, and what a package adds over that is the exact
// windowing a field-data report needs, which a surface read by one developer at a time does not.
//
// A browser that does not report an entry type — jsdom reports none, and a WebView may lack some — leaves that vital
// unmeasured rather than failing, and the surface says it was not measured.

/**
 * Begins measuring, from whatever the page has already buffered since it loaded — which is why opening the surface late
 * still shows the paints and the shifts that happened before it — and answers how to stop.
 */
export function measureWebVitals(noticeVital: (name: VitalName, value: number) => void): () => void {
    const observers: PerformanceObserver[] = [];

    function observe(type: string, read: (entries: readonly PerformanceEntry[]) => void): void {
        if (typeof PerformanceObserver === 'undefined' || !PerformanceObserver.supportedEntryTypes.includes(type)) {
            return;
        }

        const observer = new PerformanceObserver((list) => {
            read(list.getEntries());
        });

        observer.observe({ type, buffered: true, ...(type === 'event' ? { durationThreshold: 16 } : {}) });
        observers.push(observer);
    }

    observe('largest-contentful-paint', (entries) => {
        const last = entries.at(-1);

        if (last !== undefined) {
            noticeVital('lcp', last.startTime);
        }
    });

    observe('paint', (entries) => {
        const contentful = entries.find((entry) => entry.name === 'first-contentful-paint');

        if (contentful !== undefined) {
            noticeVital('fcp', contentful.startTime);
        }
    });

    // ponytail: the sum of every unexpected shift rather than the largest five-second session window, which reads high
    // on a long-lived page; window the shifts the way the specification does if this number is ever compared to a report.
    let shifted = 0;

    observe('layout-shift', (entries) => {
        for (const entry of entries) {
            if (
                'value' in entry &&
                typeof entry.value === 'number' &&
                !('hadRecentInput' in entry && entry.hadRecentInput === true)
            ) {
                shifted += entry.value;
            }
        }

        noticeVital('cls', shifted);
    });

    // ponytail: the slowest interaction rather than the 98th percentile, which is the same number until a page has
    // seen fifty interactions; keep a sorted list of the worst ten if the difference ever matters.
    let slowest = 0;

    observe('event', (entries) => {
        for (const entry of entries) {
            if ('interactionId' in entry && typeof entry.interactionId === 'number' && entry.interactionId > 0) {
                slowest = Math.max(slowest, entry.duration);
            }
        }

        if (slowest > 0) {
            noticeVital('inp', slowest);
        }
    });

    observe('navigation', (entries) => {
        const navigation = entries[0];

        if (navigation !== undefined && 'responseStart' in navigation && typeof navigation.responseStart === 'number') {
            noticeVital('ttfb', navigation.responseStart);
        }
    });

    return () => {
        for (const observer of observers) {
            observer.disconnect();
        }
    };
}
