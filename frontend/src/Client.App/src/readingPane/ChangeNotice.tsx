// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { useLocalization } from '../localization/useLocalization';
import { MoveChoice } from '../mailboxActs/MoveChoice';
import { useMailboxActs, type ActedMessage } from '../mailboxActs/useMailboxActs';
import { toneBox, toneInk } from '../outbox/outboxStanding';
import { useChangeDecision, wordChangeQuestion } from '../pendingChanges/useChangeDecision';

// A change the mailbox did not take, said on the message it was asked of: what did not happen, why the message is
// drawn where it is, and the two answers. It is drawn in the Outbox refusal's shape because it is the same kind of
// statement — something asked for that did not land — and it stands until it is answered, here or on its notification.
//
// The message itself is drawn as the deployment holds it, which is the whole of the correction: nothing here undoes
// anything, because the change never happened on the server and a mirrored account shows the server's state.
//
// Answering it takes the notice away with the control that answered, so focus is put on the message it stood over —
// the region the reading pane and the conversation both draw a message in — rather than left to fall to the document.

export function ChangeNotice({ message }: { readonly message: ActedMessage }) {
    const { translate } = useLocalization();
    const decision = useChangeDecision();
    const acts = useMailboxActs();
    const choosing = useRef<HTMLDialogElement>(null);
    const notice = useRef<HTMLDivElement>(null);
    const question = decision.questionAbout(message.storedEmailId);

    if (question === null) {
        return null;
    }

    function toTheMessage(): void {
        notice.current?.closest<HTMLElement>('article')?.focus();
    }

    const words = wordChangeQuestion(question);
    const tone = words.unknown ? 'warning' : 'error';
    const again = decision.askingAgain(question);

    return (
        <>
            <div
                ref={notice}
                role="status"
                className={`flex items-start gap-2.75 rounded-xl border px-3.5 py-3 ${toneBox[tone].box}`}
            >
                <Icon name={words.unknown ? 'help' : 'sync_problem'} className={`size-icon-lg ${toneInk[tone]}`} />

                <div className="flex min-w-0 flex-1 flex-col gap-1">
                    <p className={`text-md font-semibold ${toneBox[tone].title}`}>{translate(words.title)}</p>
                    <p className="text-base/relaxed text-pretty text-text-soft">{translate(words.text)}</p>

                    <div className="flex flex-wrap gap-2 pt-1.5">
                        {again === null ? null : (
                            <Control
                                label={translate(
                                    again === 'choosesFolder' ? 'change.moveAgain' : 'pendingChange.askAgain',
                                )}
                                shape="primary"
                                onPress={() => {
                                    if (again === 'choosesFolder') {
                                        choosing.current?.showModal();

                                        return;
                                    }

                                    toTheMessage();
                                    decision.askAgain(question, message);
                                }}
                            />
                        )}

                        <SecondaryButton
                            label={translate('pendingChange.letGo')}
                            shape="form"
                            onActivate={() => {
                                toTheMessage();
                                decision.letGo(question);
                            }}
                        />
                    </div>
                </div>
            </div>

            {again === 'choosesFolder' ? (
                <MoveChoice
                    asked={choosing}
                    groups={acts.destinationsOf([message])}
                    onChosen={(destination) => {
                        toTheMessage();
                        decision.askAgain(question, message, destination);
                    }}
                />
            ) : null}
        </>
    );
}
