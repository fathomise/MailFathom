// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState } from 'react';
import {
    readAgentConversationFiles,
    type AgentConversationFile,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';

const none: ReadonlyMap<string, AgentConversationFile> = new Map();

/**
 * The files the conversation in front holds, by identifier, which is what a question's files are named from.
 *
 * It is read when a question names a file it does not hold yet — a conversation opened, or a question just sent with
 * files — and not again for the same set, so a list that never answers for an identifier is asked once rather than for
 * ever. Until it answers, and where it fails, nothing is named, which draws the question without its chips.
 *
 * @param named Every file the conversation's questions carried, in the order they were asked.
 */
export function useConversationFiles(
    session: ClientSession | null,
    transport: MailFathomTransport,
    conversation: string | null,
    named: readonly string[],
): ReadonlyMap<string, AgentConversationFile> {
    const [held, setHeld] = useState<{
        readonly conversation: string | null;
        readonly files: ReadonlyMap<string, AgentConversationFile>;
    }>({ conversation: null, files: none });

    const files = held.conversation === conversation ? held.files : none;
    const unknown = named.some((fileId) => !files.has(fileId)) ? named.join(',') : '';

    useEffect(() => {
        if (session === null || conversation === null || unknown === '') {
            return;
        }

        let listening = true;

        void readAgentConversationFiles(session, transport, conversation).then((listed) => {
            if (listening && listed.outcome === 'read') {
                setHeld({ conversation, files: new Map(listed.value.map((file) => [file.fileId, file])) });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, conversation, unknown]);

    return files;
}
