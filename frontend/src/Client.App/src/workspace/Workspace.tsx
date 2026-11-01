// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { rememberedWorkspace, rememberWorkspace } from './rememberedWorkspace';
import { WorkspaceContext, type Workspace, type WorkspaceChange, type WorkspaceRevision } from './useWorkspace';

// Mounted above the frame rather than inside a space, which is the whole mechanism: a space is what the address
// changes, and this is not below the address.
//
// What it opens with is what this tab was last looking at, and what it holds is written back where the next start
// reads it from. Keeping it is a browser store being synchronized rather than a value being computed, which is what an
// effect is for; the alternative — writing inside the revision — would either put a side effect in an updater React
// invokes twice under StrictMode, or make `revise` a new function on every render, which is a read restarted on every
// render for everything holding it.

// A conversation, the full-HTML surface, and a file each stand in front of the message they were opened from, so
// picking a different message is what closes any of them. Each is one decision with the selection rather than a value
// kept in step with it: left free to disagree, one would stay on the screen with nothing reacting to the click behind
// it, and the way out of it — back to the message — would return to whichever row somebody picked last rather than to
// the one they opened it from. The surface has a second reason of its own: it draws a stranger's markup for one
// message, and carrying it onto the next would draw markup nobody asked to be shown.
//
// It is decided here rather than wherever a row is clicked because this is where all three live: a screen that picks a
// message would otherwise each have to remember to close what it never knew was in front of it, and the one that
// forgets is the defect.
function withoutAStaleFront(current: Workspace, change: Partial<Workspace>): Partial<Workspace> {
    const decided = withoutAStaleOutbox(current, change);

    if (decided.selection === undefined || decided.selection === current.selection) {
        return decided;
    }

    return {
        ...decided,
        conversation: decided.conversation ?? null,
        fullHtml: decided.fullHtml ?? null,
        attachment: decided.attachment ?? null,
    };
}

// An open Outbox stands in place of the list and the pane, so whatever names a folder to list, an account whose
// conversations to list, or a message to read is what closes it — pressing a folder row, the same Sent row included, an
// account row of the list read by conversation, or a citation opening a message anywhere. A change that names the
// Outbox itself is the one that opened it or moves within it, and is left as it was asked.
function withoutAStaleOutbox(current: Workspace, change: Partial<Workspace>): Partial<Workspace> {
    const leavesForMail =
        change.scope !== undefined ||
        change.threadListAccount !== undefined ||
        (change.selection !== undefined && change.selection !== null);

    return current.outbox === null || change.outbox !== undefined || !leavesForMail
        ? change
        : { ...change, outbox: null };
}

export function WorkspaceProvider({ children }: { readonly children: ReactNode }) {
    const [workspace, setWorkspace] = useState<Workspace>(rememberedWorkspace);

    const revise = useCallback((change: WorkspaceChange) => {
        setWorkspace((current) => ({
            ...current,
            ...withoutAStaleFront(current, typeof change === 'function' ? change(current) : change),
        }));
    }, []);

    useEffect(() => {
        rememberWorkspace(workspace);
    }, [workspace]);

    const revision = useMemo<WorkspaceRevision>(() => ({ workspace, revise }), [workspace, revise]);

    return <WorkspaceContext value={revision}>{children}</WorkspaceContext>;
}
