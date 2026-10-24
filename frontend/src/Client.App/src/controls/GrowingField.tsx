// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useLayoutEffect, useRef, type ComponentProps } from 'react';
import { heightFor } from './fieldHeight';

// Where somebody types what an AI draft should say — an instruction, their own draft of the message, or both — so the
// field grows with what is written: one line empty, wrapping rather than scrolling sideways, a line at a time up to six,
// and a scrollbar past that. The height is measured rather than left to `field-sizing: content`, which not every engine
// a head runs on supports yet, and an empty field keeps its placeholder on its one line rather than letting a long one
// wrap the field taller than anything written in it.

// Measured from what was drawn, which is the one source of a line's height and of where the text wrapped: the field is
// collapsed first so a shorter text can shrink it again, and an engine that reports no line height leaves it at one row.
function fitToWhatIsWritten(field: HTMLTextAreaElement): void {
    const style = getComputedStyle(field);
    const line = Number.parseFloat(style.lineHeight);

    if (Number.isNaN(line)) {
        return;
    }

    const border = Number.parseFloat(style.borderTopWidth) + Number.parseFloat(style.borderBottomWidth);
    const padding = Number.parseFloat(style.paddingTop) + Number.parseFloat(style.paddingBottom);

    field.style.height = 'auto';

    const { height, scrolls } = heightFor({ written: field.scrollHeight + border, line, frame: padding + border });

    field.style.height = `${String(height)}px`;
    field.style.overflowY = scrolls ? 'auto' : 'hidden';
}

export function GrowingField({
    ref,
    value,
    className = '',
    ...field
}: Omit<ComponentProps<'textarea'>, 'rows' | 'value'> & { readonly value: string }) {
    const own = useRef<HTMLTextAreaElement | null>(null);

    // An effect because the height is read off the drawn field, which is outside React; after every change of the text,
    // because that is what moves it.
    useLayoutEffect(() => {
        if (own.current !== null) {
            fitToWhatIsWritten(own.current);
        }
    }, [value]);

    // A narrower field wraps the same text onto more lines, so a change of width refits it too. Only the width: the
    // refit itself changes the height, and answering that would be the field measuring itself forever.
    useLayoutEffect(() => {
        const element = own.current;

        if (element === null || typeof ResizeObserver !== 'function') {
            return;
        }

        let width = element.clientWidth;
        const watching = new ResizeObserver(() => {
            if (element.clientWidth !== width) {
                width = element.clientWidth;
                fitToWhatIsWritten(element);
            }
        });

        watching.observe(element);

        return () => {
            watching.disconnect();
        };
    }, []);

    // Stable for as long as the caller's ref is, so a ref that acts on attaching — focusing the field — runs when the
    // element attaches rather than on every render.
    const attach = useCallback(
        (element: HTMLTextAreaElement | null) => {
            own.current = element;

            if (typeof ref === 'function') {
                return ref(element);
            }

            if (ref !== null && ref !== undefined) {
                ref.current = element;
            }

            return undefined;
        },
        [ref],
    );

    return (
        <textarea
            {...field}
            ref={attach}
            rows={1}
            value={value}
            className={`resize-none wrap-anywhere placeholder:text-ellipsis placeholder-shown:overflow-hidden placeholder-shown:whitespace-nowrap ${className}`}
        />
    );
}
