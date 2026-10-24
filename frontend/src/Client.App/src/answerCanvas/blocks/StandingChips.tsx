// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { AnswerConfidence, BlockSupport } from '@mailfathom/client-backend';
import { useEffect, useId, useLayoutEffect, useRef, useState, type ReactNode } from 'react';
import { Icon } from '../../controls/Icon';
import { useLocalization } from '../../localization/useLocalization';
import { confidenceBands, supportVerdicts } from './blockWording';

// The chips a block says where it stands with, each of which explains itself. A label such as *moderate confidence*
// or *cited* is a word somebody may not know the meaning of, and the explanation stands one gesture away rather than in
// a legend on another screen.
//
// It is a tooltip, and it takes every way in the design's pointer rule allows: a mouse's hover is the shortcut, and a
// tap or keyboard focus reaches the same sentence, so nothing exists only under a hover. Like any tooltip it goes by
// itself — when the pointer leaves, when focus moves on, on Escape, and a while after a tap, since a finger has no hover
// to leave.

/** How long a sentence a press brought up stands before it goes by itself. */
const pressedFor = 4000;

/** How far a sentence keeps from the window's edge when it is moved back inside it, in CSS pixels. */
const windowMargin = 8;

function ExplainedChip({
    label,
    explanation,
    tint,
    children,
}: {
    readonly label: string;
    readonly explanation: string;
    readonly tint: string;
    readonly children?: ReactNode;
}) {
    const explained = useId();
    const [shown, setShown] = useState(false);
    const tappedWith = useRef<string | null>(null);
    const pressed = useRef<number | undefined>(undefined);
    const sentence = useRef<HTMLSpanElement>(null);
    const [pulledBack, setPulledBack] = useState(0);

    // The sentence stands from the chip's start edge, and a chip far along a phone's line would push it past the window.
    // It is measured once it is drawn and moved back by what spills over, which is an imperative read of the layout
    // and so an effect's to make.
    useLayoutEffect(() => {
        if (!shown || sentence.current === null) {
            setPulledBack(0);
            return;
        }

        const drawn = sentence.current.getBoundingClientRect();
        const spill = drawn.right - (window.innerWidth - windowMargin);
        setPulledBack(spill > 0 ? Math.min(spill, drawn.left - windowMargin) : 0);
    }, [shown]);

    const forgetPress = () => {
        window.clearTimeout(pressed.current);
        pressed.current = undefined;
    };

    useEffect(() => forgetPress, []);

    const hide = () => {
        tappedWith.current = null;
        forgetPress();
        setShown(false);
    };

    return (
        <span className="relative inline-flex w-fit">
            <button
                aria-describedby={explained}
                className={`touch-extended flex cursor-help items-center gap-1 rounded-full px-2.25 py-0.5 text-xs ${tint}`}
                type="button"
                onBlur={hide}
                onClick={() => {
                    // A mouse already brought the sentence up by hovering and a key by focusing, so only a finger or a
                    // pen — which have no hover to leave — needs the press to show it and the clock to take it away.
                    if (tappedWith.current === null) {
                        return;
                    }

                    tappedWith.current = null;
                    forgetPress();
                    setShown(true);
                    pressed.current = window.setTimeout(() => {
                        pressed.current = undefined;
                        setShown(false);
                    }, pressedFor);
                }}
                onFocus={() => {
                    setShown(true);
                }}
                onKeyDown={(event) => {
                    if (event.key === 'Escape' && shown) {
                        event.stopPropagation();
                        hide();
                    }
                }}
                onPointerDown={(event) => {
                    tappedWith.current = event.pointerType === 'mouse' ? null : event.pointerType;
                }}
                onPointerCancel={() => {
                    tappedWith.current = null;
                }}
                onPointerEnter={(event) => {
                    if (event.pointerType === 'mouse') {
                        setShown(true);
                    }
                }}
                onPointerLeave={(event) => {
                    if (event.pointerType === 'mouse') {
                        hide();
                    }
                }}
            >
                {children}
                {label}
            </button>

            {/* ponytail: moved back inside the window by measuring it; CSS anchor positioning would do it without a
                measurement once every head's engine supports it. */}
            <span
                className="absolute start-0 top-full z-20 mt-1.5 w-max max-w-72 rounded-md bg-text px-2.5 py-1.75 text-base text-pretty text-panel shadow-raised"
                hidden={!shown}
                id={explained}
                ref={sentence}
                role="tooltip"
                style={pulledBack > 0 ? { translate: `${String(-pulledBack)}px 0` } : undefined}
            >
                {explanation}
            </span>
        </span>
    );
}

/** What the correspondence does for a block, as a chip that says what that verdict means. */
export function SupportVerdictChip({ support }: { readonly support: BlockSupport }) {
    const { translate } = useLocalization();
    const verdict = supportVerdicts[support];

    return (
        <ExplainedChip explanation={translate(verdict.note)} label={translate(verdict.label)} tint={verdict.tint}>
            <Icon className="size-icon-xs" name={verdict.icon} />
        </ExplainedChip>
    );
}

/** How far an answer is worth trusting, as a chip that says what that band means. */
export function ConfidenceBandChip({ confidence }: { readonly confidence: AnswerConfidence }) {
    const { translate } = useLocalization();
    const band = confidenceBands[confidence];

    return <ExplainedChip explanation={translate(band.note)} label={translate(band.label)} tint={band.tint} />;
}
