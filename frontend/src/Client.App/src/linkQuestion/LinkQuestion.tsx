// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { backOutlined, mannerDrawn } from '../confirmation/wayOutShapes';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { LinkOpenerContext, useLinkOpener, type FollowedLink, type OpenLink } from '../shellOperations/linkOpener';
import { askingFor, type LinkAsked } from './linkTrust';

// No link that leaves MailFathom opens on the press that found it. Every surface that draws a link opens it through the
// one operation `shellOperations/linkOpener.ts` declares, so the question stands there rather than in any surface: this
// supplies that operation again for everything beneath it, wrapped around the one the head resolved, and asks before
// handing it anything that leaves for the web to a host the person does not trust. A surface added later inherits the
// question by opening its links the way every other one already does.
//
// It is not `confirmation/Confirmation.tsx`, because nothing about following a link is an act on the deployment with a
// way back to state: what the question shows is where the link goes, and what the person decides is whether they meant
// to go there.

interface Pending {
    readonly link: FollowedLink;
    readonly opened: () => void;
    readonly refused: (reason: unknown) => void;
}

const openAnswer = 'open';

export function LinkQuestion({
    trustedDomains,
    children,
}: {
    /** The person's own list, held by the deployment; a link to one of them, or to a subdomain of one, opens unasked. */
    readonly trustedDomains: readonly string[];
    readonly children: ReactNode;
}) {
    const open = useLinkOpener();
    const asked = useRef<HTMLDialogElement>(null);
    const cancel = useRef<HTMLButtonElement>(null);

    // What the dialog draws is kept after it closes, so it does not empty while it leaves, and each press asks with a
    // question of its own, which is what stands the dialog up again; the press waiting on an answer is a separate
    // thing, and is let go of once it has one.
    const [asking, setAsking] = useState<LinkAsked | null>(null);
    const pending = useRef<Pending | null>(null);

    // Held stable against the list, because every surface below subscribes with it and a new operation per render would
    // have each of them unsubscribe and subscribe again for nothing.
    const follow = useCallback<OpenLink>(
        (link) => {
            const question = askingFor(link, trustedDomains);

            if (question === null) {
                return open(link);
            }

            return new Promise<void>((opened, refused) => {
                // A press arriving while one is asked replaces it, and the one replaced settles as declined.
                pending.current?.opened();
                pending.current = { link, opened, refused };
                setAsking(question);
            });
        },
        [open, trustedDomains],
    );

    // The dialog is the platform's own, so standing it up is an imperative call once the question has been drawn into
    // it. Focus goes to *Cancel*, which is the answer that costs nothing.
    useEffect(() => {
        const dialog = asked.current;

        if (asking !== null && dialog !== null && !dialog.open) {
            dialog.showModal();
            cancel.current?.focus();
        }
    }, [asking]);

    return (
        <LinkOpenerContext value={follow}>
            {children}
            <LinkAsking
                asked={asked}
                cancel={cancel}
                asking={asking}
                onAnswered={(opening) => {
                    const answered = pending.current;

                    pending.current = null;

                    if (answered === null) {
                        return;
                    }

                    // Declining is an answer rather than a failure, so the press that asked settles as one that did
                    // what was wanted; an opener that refused is still reported to the surface, which says so.
                    if (opening) {
                        open(answered.link).then(answered.opened, answered.refused);
                    } else {
                        answered.opened();
                    }
                }}
            />
        </LinkOpenerContext>
    );
}

function LinkAsking({
    asked,
    cancel,
    asking,
    onAnswered,
}: {
    readonly asked: React.RefObject<HTMLDialogElement | null>;
    readonly cancel: React.RefObject<HTMLButtonElement | null>;
    readonly asking: LinkAsked | null;
    readonly onAnswered: (opening: boolean) => void;
}) {
    const { translate } = useLocalization();
    const closesOnBack = useTopLayerSurface();
    const names = useId();
    const explains = useId();

    const title = translate(asking?.picture === true ? 'link.askPictureTitle' : 'link.askTitle');
    const warnings =
        asking === null
            ? []
            : asking.wordsName === null
              ? asking.warnings
              : [translate('link.askWordsNameAnotherHost', { named: asking.wordsName, host: asking.host })];

    return (
        <dialog
            ref={asked}
            role="alertdialog"
            aria-labelledby={names}
            aria-describedby={explains}
            onToggle={closesOnBack}
            className="surface-pop m-auto w-link-question max-w-dialog-narrow rounded-lg border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim max-workspace:w-link-question-phone max-workspace:rounded-xl"
            // The scrim closes it as *Cancel* does. A press that lands on the dialog element itself, rather than on
            // anything inside it, can only have landed on the backdrop, since the content fills the element.
            onClick={(pressed) => {
                if (pressed.target === pressed.currentTarget) {
                    pressed.currentTarget.close();
                }
            }}
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const opening = dialog.returnValue === openAnswer;

                // Emptied rather than left, because a return value outlives the dialog it was set on.
                dialog.returnValue = '';
                onAnswered(opening);
            }}
        >
            <div className="flex flex-col gap-3.25 p-4.5 max-workspace:py-4.75">
                <div className="flex items-start gap-2.75">
                    <Icon name="open_in_new" className="mt-px size-icon-xl shrink-0 text-warning" />

                    <div className="flex min-w-0 flex-col gap-1.25">
                        <h2 id={names} className="text-xl font-semibold text-pretty">
                            {title}
                        </h2>
                        <p id={explains} className="text-base/normal text-muted text-pretty">
                            {translate('link.askExplanation')}
                        </p>
                    </div>
                </div>

                <div className="flex flex-col gap-1 rounded-md border border-line bg-sunken px-3.25 py-2.75">
                    <p className="text-lg font-semibold wrap-anywhere">{asking?.host}</p>
                    <p className="font-mono text-sm/normal text-muted wrap-anywhere select-text">{asking?.target}</p>
                </div>

                {warnings.map((warning) => (
                    <p key={warning} className="flex items-start gap-2 text-base/normal text-warning-text text-pretty">
                        <Icon name="warning" className="mt-0.5 size-icon-sm shrink-0" />
                        <span>{warning}</span>
                    </p>
                ))}

                <div className="mt-0.5 flex flex-wrap justify-end gap-2.25">
                    <button
                        ref={cancel}
                        type="button"
                        className={backOutlined}
                        onClick={() => {
                            asked.current?.close();
                        }}
                    >
                        {translate('act.cancel')}
                    </button>
                    <button
                        type="button"
                        className={mannerDrawn.act}
                        onClick={() => {
                            asked.current?.close(openAnswer);
                        }}
                    >
                        {translate('link.askOpen')}
                    </button>
                </div>
            </div>
        </dialog>
    );
}
