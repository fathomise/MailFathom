// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';

// The four ways the rewrite bar offers to rewrite a passage before anybody types one of their own.
//
// What a preset asks the model is an instruction in English whatever language the client reads in: it is read by the
// model rather than by the person, and the service has the passage rewritten in the language of the draft, never the
// instruction's. What the person reads is the label, which is a catalogue entry like every other.

/** One way of rewriting a passage the bar offers by name. */
export type RewritePreset = 'shorter' | 'moreFormal' | 'softer' | 'firmer';

/** The presets in the order the bar draws them, which is also the order *Try again* steps through. */
export const rewritePresets: readonly RewritePreset[] = ['shorter', 'moreFormal', 'softer', 'firmer'];

/** What each preset is called, drawn with, and asks the model for. */
export const presetShapes: Readonly<
    Record<RewritePreset, { readonly said: MessageKey; readonly icon: IconName; readonly instruction: string }>
> = {
    shorter: {
        said: 'rewrite.shorter',
        icon: 'short_text',
        instruction: 'Make it shorter, keeping everything it says.',
    },
    moreFormal: {
        said: 'rewrite.moreFormal',
        icon: 'work',
        instruction: 'Make it more formal.',
    },
    softer: {
        said: 'rewrite.softer',
        icon: 'sentiment_satisfied',
        instruction: 'Make it softer and warmer, without changing what it asks for or agrees to.',
    },
    firmer: {
        said: 'rewrite.firmer',
        icon: 'front_hand',
        instruction: 'Make it firmer and more direct, without changing what it asks for or agrees to.',
    },
};

/** The preset *Try again* asks for after this one: the next in the bar's order, round to the first. */
export function presetAfter(preset: RewritePreset): RewritePreset {
    return rewritePresets[(rewritePresets.indexOf(preset) + 1) % rewritePresets.length] ?? 'shorter';
}
