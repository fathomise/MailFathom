// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The emoji the composer's *Emoji* menu offers, and the pictures it draws them with. A choice is written into the
// message as the character itself, so a recipient's own client draws it; the menu draws each from Noto Emoji's
// Apache-2.0 outlines committed under `assets/emoji/`, so what the picker shows is an openly licensed drawing on every
// head rather than whichever emoji font the operating system happens to carry. `THIRD_PARTY_LICENSES.md` records them.

import type { MessageKey } from '../localization/en';

/** One emoji the menu offers: the text it puts at the caret, and what it is called for whoever cannot see it. */
export interface OfferedEmoji {
    readonly text: string;
    readonly named: MessageKey;
}

/** The design's fixed set of common emoji, in the order its grid draws them; anything else is the system's own picker. */
export const offeredEmoji: readonly OfferedEmoji[] = [
    { text: '😀', named: 'compose.emojiGrinning' },
    { text: '😂', named: 'compose.emojiLaughing' },
    { text: '🙂', named: 'compose.emojiSmiling' },
    { text: '😉', named: 'compose.emojiWinking' },
    { text: '😊', named: 'compose.emojiBlushing' },
    { text: '😍', named: 'compose.emojiHeartEyes' },
    { text: '🤔', named: 'compose.emojiThinking' },
    { text: '😮', named: 'compose.emojiSurprised' },
    { text: '😢', named: 'compose.emojiCrying' },
    { text: '😅', named: 'compose.emojiSweat' },
    { text: '🙏', named: 'compose.emojiFoldedHands' },
    { text: '👍', named: 'compose.emojiThumbsUp' },
    { text: '👎', named: 'compose.emojiThumbsDown' },
    { text: '👏', named: 'compose.emojiClapping' },
    { text: '🙌', named: 'compose.emojiRaisingHands' },
    { text: '👋', named: 'compose.emojiWaving' },
    { text: '💪', named: 'compose.emojiBiceps' },
    { text: '🤝', named: 'compose.emojiHandshake' },
    { text: '❤️', named: 'compose.emojiHeart' },
    { text: '🔥', named: 'compose.emojiFire' },
    { text: '🎉', named: 'compose.emojiParty' },
    { text: '✅', named: 'compose.emojiCheck' },
    { text: '❌', named: 'compose.emojiCross' },
    { text: '⚠️', named: 'compose.emojiWarning' },
    { text: '⭐', named: 'compose.emojiStar' },
    { text: '💡', named: 'compose.emojiLightBulb' },
    { text: '📌', named: 'compose.emojiPushpin' },
    { text: '📎', named: 'compose.emojiPaperclip' },
    { text: '📅', named: 'compose.emojiCalendar' },
    { text: '⏰', named: 'compose.emojiAlarmClock' },
    { text: '☕', named: 'compose.emojiCoffee' },
    { text: '🚀', named: 'compose.emojiRocket' },
];

// Each picture is a file of its own the bundle serves, read only once the menu draws it, rather than a string inlined
// into the script every reader downloads whether they open the menu or not.
const pictures: Readonly<Record<string, string | undefined>> = Object.fromEntries(
    Object.entries(
        import.meta.glob<string>('../assets/emoji/*.svg', { query: '?url', import: 'default', eager: true }),
    ).map(([path, address]) => [path.slice(path.lastIndexOf('/') + 1), address]),
);

/** The file an emoji is drawn from, named as upstream names it: its code points, the presentation selector left out. */
export function pictureFileOf(text: string): string {
    // By code point rather than by grapheme, because the file name spells every code point a sequence is made of.
    const codePoints = Array.from(text, (character) => character.codePointAt(0) ?? 0)
        .filter((codePoint) => codePoint !== 0xfe0f)
        .map((codePoint) => codePoint.toString(16).padStart(4, '0'));

    return `emoji_u${codePoints.join('_')}.svg`;
}

/** The address the bundle serves an emoji's picture at, or undefined for one it carries no picture for. */
export function pictureOf(text: string): string | undefined {
    return pictures[pictureFileOf(text)];
}

/** Every picture file committed beside the client, by its file name. */
export const committedPictures: readonly string[] = Object.keys(pictures);
