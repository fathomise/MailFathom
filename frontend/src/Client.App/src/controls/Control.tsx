// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from './Icon';
import type { IconName } from './icons';
import { controlShapes, labelledShape, symbolShown, type ControlShape } from './controlShapes';
import { useTip } from './useTip';

// A control that does something, in the shapes the design draws one. It is `PlannedControl`'s counterpart and
// the two share the shape table rather than a resemblance: what a reader sees when something becomes real is the same
// button gaining an action, not a second button that looks nearly like the first.
//
// A shape drawing the symbol alone carries the name for the accessibility tree, because a control nobody can name is
// one a reader cannot reach and a suite cannot assert on — and for the eye in `useTip`'s bubble, which a mouse rests
// on and a finger holds, because a symbol somebody has to press to learn the meaning of is one they will not press. A
// shape drawing its words says nothing more about itself unless it has a hint.
//
// A control that turns something on and off says which it is, and the platform has an attribute for exactly that. It
// is optional because most controls do a thing rather than hold a state, and a button that reported one it does not
// have would be read out as a switch by everything that speaks the screen.

export function Control({
    label,
    icon,
    shape = 'labelled',
    className,
    pressed,
    hint,
    onPress,
    onHold,
}: {
    readonly label: string;
    readonly icon?: IconName;
    readonly shape?: ControlShape;
    readonly className?: string;

    /** What the control does at more length than its name, where a short name leaves that unsaid. */
    readonly hint?: string;

    /** Whether what this control turns on is on, for a control that turns something on, and absent for one that does not. */
    readonly pressed?: boolean;

    readonly onPress: () => void;

    /** What holding the control does, for one that does a second thing on a hold — `useTip` says how it is reached. */
    readonly onHold?: () => void;
}) {
    const labelled = labelledShape(shape);
    const { anchor, tip } = useTip({ says: hint ?? label, describes: hint !== undefined, onPress, onHold });
    const spoken = !labelled || hint !== undefined;

    return (
        <>
            <button
                {...(spoken ? anchor : { onClick: onPress })}
                type="button"
                aria-label={labelled ? undefined : label}
                aria-pressed={pressed}
                className={`flex shrink-0 items-center whitespace-nowrap transition ${controlShapes[shape]} ${className ?? ''}`}
            >
                {icon === undefined || !symbolShown(shape) ? null : (
                    <Icon name={icon} className={shape === 'floating' ? 'size-icon-2xl' : 'size-icon-md'} />
                )}
                {labelled ? <span>{label}</span> : null}
            </button>
            {spoken ? tip : null}
        </>
    );
}
