// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

/**
 * Whether a scroll has reached the rows already read, which is what asks the deployment for the page after them. It is
 * read off the scroll rather than off a render, because a read going out is something a person's gesture caused rather
 * than something a commit should start — and off whichever element scrolls: the list itself beside the day, the whole
 * screen on one pane, where the day follows the list.
 */
export function reachedTheRowsRead(element: Pick<Element, 'scrollHeight' | 'scrollTop' | 'clientHeight'>): boolean {
    return element.scrollHeight - element.scrollTop - element.clientHeight <= element.clientHeight;
}
