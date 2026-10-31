// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useContext } from 'react';
import type { MailDraftAnswer } from '@mailfathom/client-backend';
import { ComposingContext } from '../composer/useComposing';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';
import { flagActFor, refusalSaid, standsInTheWay } from '../mailboxActs/drawnActs';
import { allDrawnSpam, useMailboxActs, type ActedMessage } from '../mailboxActs/useMailboxActs';
import { MoreActions } from '../messageSource/MoreActions';
import { SpamControl } from '../spamMarking/SpamControl';

// What stands at the end of the head of a message or a conversation, beside its subject: the three things the design
// offers to do with it from there. One component because two heads draw it — a message opened from the list and the
// conversation it belongs to — and the acts have to be the same acts from both. Asking the agent about the thread is not
// one of them: the design gives that to the AI button at the thread's foot, so the head carries no second entry to it.
//
// The design draws the three as words alone where the head has a column to itself, as symbols alone where that column
// is narrower than 560 pixels — the pane's width rather than the window's, which `controlShapes.ts`'s `paneNamed` asks —
// and as symbols alone where the column is the whole screen and the head is one compact bar over the message. Two panes
// under a finger leave the three to the toolbar above, which already offers them, and draw them again while a selection
// has taken the toolbar's place; under a mouse the head keeps them beside the toolbar's.
//
// The other three act on the message the head is about, and each goes the way every other surface already goes: an
// answer is `useComposing`'s, exactly as the toolbar's three answers and a row's own menu are, and the flag is a
// mailbox act, exactly as the toolbar's five are. Neither is a second implementation — a head that composed its own
// reply or wrote its own flag is how two surfaces come to answer a message differently.
//
// **The flag goes both ways here and on every other surface**, under the one rule `mailboxActs/drawnActs.ts` states:
// a message drawn flagged is offered the act that takes the flag off. A head is about exactly one message, which is
// the simplest case of that rule rather than a case of its own — reading it here would be a second answer to which
// direction one message's flag goes.
//
// The head's own words for forwarding and flagging differ from the toolbar's — *Przekaż* and *Oflaguj* against
// *Prześlij dalej* and *Flaga* — which is why they are keys of their own rather than the toolbar's reused.

export function HeadActs({
    compact,
    toolbarCarriesActs = false,
    message,
    subject = null,
}: {
    readonly compact: boolean;

    /**
     * Whether the toolbar standing above the head already offers the three acts, which is where the head leaves them
     * to it: the subject keeps the width a second copy would take from it.
     */
    readonly toolbarCarriesActs?: boolean;

    /**
     * The message these acts are about, or `null` where the head stands over nothing one could be taken on.
     *
     * A conversation hands the message its head is drawn from, which is the message being read rather than the thread
     * around it: the head says who wrote *that* message and when, so the three acts under it answer, forward, and flag
     * the same one. An act over a whole conversation is a screen of its own and is not what this draws.
     */
    readonly message: ActedMessage | null;

    /** What the head names the message by, which heads the *More actions* menu over it. */
    readonly subject?: string | null;
}) {
    const { translate } = useLocalization();
    const acts = useMailboxActs();

    // Read rather than required, for the reason `mailSpace/NothingOpen.tsx` reads it that way: a head is drawn in
    // surfaces a test mounts on their own, and writing is offered by a deployment rather than by this component.
    const composing = useContext(ComposingContext);

    const actShape: ControlShape = compact ? 'symbol' : 'paneNamed';

    // What each of the three does, or `null` where it cannot be done: the answers need a deployment that lets this
    // credential write a draft, and both need a message to be about. Written as the act rather than as a condition
    // read again inside the markup, which is what keeps the message's own identity out of every branch below.
    const answer =
        composing?.offered === true && message !== null
            ? (answers: MailDraftAnswer) => () => {
                  composing.compose({ kind: 'answer', answers, storedEmailId: message.storedEmailId });
              }
            : null;

    // Which way the flag goes, read through the one rule every surface reads it through, so the head and the toolbar
    // over the same message never offer opposite directions.
    const flagging = flagActFor(acts, message === null ? [] : [message]);
    const flagLabel = translate(flagging === 'unflag' ? 'message.unflag' : 'message.flag');

    // Either the act or the sentence saying why it cannot be taken, decided here rather than inside the markup: a
    // control that cannot act says so before it is pressed, which is the rule `mailboxActs/MailboxActControls.tsx`
    // states for the strips, and the two shapes are exclusive.
    const flag = whatFlaggingComesTo();

    function whatFlaggingComesTo(): { readonly press: () => void } | { readonly why: string } {
        if (message === null) {
            return { why: translate(refusalSaid.nothingToActOn, { control: flagLabel }) };
        }

        const inTheWay = standsInTheWay(acts, flagging, [message]);

        if (inTheWay === null) {
            return {
                press: () => {
                    acts.perform(flagging, [message]);
                },
            };
        }

        return {
            why: translate(inTheWay === 'underway' ? 'act.underway' : refusalSaid[inTheWay], { control: flagLabel }),
        };
    }

    // A message marked spam is read-only, so its head offers *Not spam* alone; any other adds *Mark spam* after its acts.
    // *More actions* follows the spam control either way, drawn as its symbol alone however much room the head has.
    const spam = message !== null && allDrawnSpam(acts, [message]);
    const more = <MoreActions storedEmailId={message?.storedEmailId ?? null} subject={subject} />;

    return (
        <div className="ms-auto flex shrink-0 items-center gap-2">
            {toolbarCarriesActs ? null : spam ? (
                <>
                    <SpamControl messages={[message]} shape={actShape} wording="mark" />
                    {more}
                </>
            ) : (
                <div className="flex items-center gap-0.75">
                    {answer === null ? (
                        <PlannedControl label={translate('mail.reply')} icon="reply" shape={actShape} />
                    ) : (
                        <Control
                            label={translate('mail.reply')}
                            icon="reply"
                            shape={actShape}
                            onPress={answer('senderOnly')}
                        />
                    )}

                    {answer === null ? (
                        <PlannedControl label={translate('message.forward')} icon="forward" shape={actShape} />
                    ) : (
                        <Control
                            label={translate('message.forward')}
                            icon="forward"
                            shape={actShape}
                            onPress={answer('forward')}
                        />
                    )}

                    {'press' in flag ? (
                        <Control label={flagLabel} icon="flag" shape={actShape} onPress={flag.press} />
                    ) : (
                        <PlannedControl label={flagLabel} icon="flag" shape={actShape} why={flag.why} />
                    )}

                    <SpamControl messages={message === null ? [] : [message]} shape={actShape} wording="mark" />
                    {more}
                </div>
            )}
        </div>
    );
}
