// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { htmlOf, type PictureSource, type WrittenNode } from './writtenText';

// The editing the composer's region is given, named once rather than at each call site.
//
// **`execCommand` is the editing, and it is deprecated with nothing to replace it.** No standard succeeded it, every
// engine implements it, and the alternative is a document-editing model this client would have to write and test
// itself. What it gives in exchange is the engine's own undo stack, so each act here is one Ctrl+Z.
//
// **This is the one module that may hand the region markup**, under ADR 0047: `insertHTML` parses its argument into the
// live document, so the lint rule refuses it everywhere else, and here it is given nothing but `htmlOf`'s escaped
// serialization of the closed set. Whatever the engine makes of that is read back by the same strict `writtenIn`.

/** One editing command on whatever the region has selected. */
export function edit(command: string, argument?: string): void {
    // eslint-disable-next-line @typescript-eslint/no-deprecated -- The reason is the comment at the top of this file.
    document.execCommand(command, false, argument);
}

/**
 * Puts written nodes where the caret is, in place of anything selected, as one step somebody can undo.
 *
 * @param written What is put there.
 * @param pictureSource The object URL the composer issued for each picture, which is the one source the region reads
 *   back as a picture.
 */
export function insertWritten(written: readonly WrittenNode[], pictureSource: PictureSource): void {
    // eslint-disable-next-line @typescript-eslint/no-deprecated -- The reason is the comment at the top of this file.
    document.execCommand('insertHTML', false, htmlOf(written, pictureSource, 'editor'));
}
