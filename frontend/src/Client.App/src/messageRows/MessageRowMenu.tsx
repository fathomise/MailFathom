// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { isSpam, type MailTimelineEntry } from '@mailfathom/client-backend';
import { useComposing, type Composing } from '../composer/useComposing';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { actsDrawn, actsInARowMenu, actsSaidInAMenu, flagActFor, readActFor, underway } from '../mailboxActs/drawnActs';
import {
    drawnSpam,
    useMailboxActs,
    type ActedMessage,
    type MailboxAct,
    type MailboxActs,
} from '../mailboxActs/useMailboxActs';
import type { Translate } from '../localization/useLocalization';
import { useLocalization } from '../localization/useLocalization';
import { useReadMarking, type ReadMarking } from '../readMarking/useReadMarking';
import { useAgentHandOver } from '../routing/agentHandOver';
import { spamMenuDetail } from '../spamMarking/spamWords';
import { useSpamRefusal, type SpamRefused } from '../spamMarking/useSpamRefusal';
import { correspondent } from './rowContents';

// What a message row offers, which is the design's own menu for it: picking messages out, answering the
// message, and the five acts that change the mailbox it is in. It is this row's items and nothing else — where the
// menu stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s, because five more lists draw
// the same menu with items of their own.
//
// **Nothing here performs an act.** Every item routes through the same calls the toolbar presses and reports through
// the same toast surface, so filing a message from a row and filing it from the strip above are one act asked for two
// ways rather than two implementations that will come to disagree.
//
// **An item the client cannot yet perform is left out rather than drawn inert.** That is the opposite of what a strip
// does, and deliberately: a strip is a fixed row whose controls would otherwise appear and disappear under a reader's
// cursor, while a menu is read top to bottom in the moment it opens, and a column of sentences nobody can act on is
// not a menu.
//
// ***Ask the agent* hands the conversation this message belongs to over to the agent**, second as the design draws it,
// and only where there is both an agent to reach and a conversation to name: a message the deployment has not placed
// in one has nothing the question could be scoped to.
//
// **A message marked spam offers nothing that reads it for the person** — neither the agent nor its readings — and its
// answers stay in the menu but are refused when chosen, saying why. Its verdict joins the header's detail, since a folded
// row draws no badge and a finger has no hover to ask with.
//
// **The two acts that stand behind a question are asked outside this menu**, because the question outlives it: the
// menu is gone the moment an item is chosen, and a dialog inside it would go with it.

/** The two acts a question stands in front of, which are raised by the surface the menu was opened from. */
export type ActAsked = 'delete' | 'move';

export function MessageRowMenu({
    email,
    messages,
    at,
    onSelect,
    onAsk,
    onCheckReadings,
    onClose,
}: {
    readonly email: MailTimelineEntry;

    /** Where this row's message belongs, which every act has to name. Empty for a row the client cannot place. */
    readonly messages: readonly ActedMessage[];

    readonly at: MenuPoint;

    /** Puts this row into the selection the list already models, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    /** Raises the question an act stands behind, over the messages it is about. */
    readonly onAsk: (act: ActAsked, messages: readonly ActedMessage[]) => void;

    /**
     * Opens what MailFathom made of this message, with the evidence behind each reading.
     *
     * This menu is where checking a reading is reached from, and that is a platform constraint rather than a choice:
     * the row is an `option` of a listbox and holds no focusable descendant, so the sentence on it cannot be a control
     * of its own. Absent where the message carries no reading, and absent on a list that offers no menu at all.
     */
    readonly onCheckReadings?: (() => void) | undefined;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const marking = useReadMarking();
    const composing = useComposing();
    const handToAgent = useAgentHandOver();
    const refuse = useSpamRefusal();
    const thread = email.threadId;
    const spam = drawnSpam(acts, email);
    const marked = isSpam(spam);

    const askAgent =
        marked || handToAgent === null || thread === null
            ? null
            : () => {
                  handToAgent({
                      scope: { kind: 'thread', subject: thread },
                      title: email.subject ?? translate('list.noSubject'),
                  });
              };

    return (
        <ContextMenu
            header={email.subject ?? translate('list.noSubject')}
            // Who wrote it, which the row folded to a strip of avatars no longer says in words.
            detail={
                spam !== null && marked ? spamMenuDetail(correspondent(email), spam, translate) : correspondent(email)
            }
            at={at}
            items={rowItems({
                email,
                messages,
                acts,
                marking,
                composing,
                translate,
                onSelect,
                askAgent,
                onAsk,
                onCheckReadings: marked ? undefined : onCheckReadings,
                refusing: marked ? refuse : null,
            })}
            onClose={onClose}
        />
    );
}

/** Everything this row offers, in the order the design draws it. */
function rowItems({
    email,
    messages,
    acts,
    marking,
    composing,
    translate,
    onSelect,
    askAgent,
    onAsk,
    onCheckReadings,
    refusing,
}: {
    readonly email: MailTimelineEntry;
    readonly messages: readonly ActedMessage[];
    readonly acts: MailboxActs;
    readonly marking: ReadMarking;
    readonly composing: Composing;
    readonly translate: Translate;
    readonly onSelect: () => void;
    readonly askAgent: (() => void) | null;
    readonly onAsk: (act: ActAsked, messages: readonly ActedMessage[]) => void;
    readonly onCheckReadings: (() => void) | undefined;

    /** Answers a press on what a message marked spam refuses, or `null` where the message is not spam. */
    readonly refusing: ((refused: SpamRefused) => void) | null;
}): readonly ContextMenuItem[] {
    const answering: readonly ContextMenuItem[] = composing.offered
        ? [
              {
                  icon: 'reply',
                  label: translate('mail.reply'),
                  choose: () => {
                      if (refusing === null) {
                          composing.compose({ kind: 'answer', answers: 'senderOnly', storedEmailId: email.id });
                      } else {
                          refusing('spam.refusedAnswer');
                      }
                  },
              },
              {
                  icon: 'forward',
                  label: translate('mail.forward'),
                  choose: () => {
                      if (refusing === null) {
                          composing.compose({ kind: 'answer', answers: 'forward', storedEmailId: email.id });
                      } else {
                          refusing('spam.refusedForward');
                      }
                  },
              },
          ]
        : [];

    // The spam control, before *Move* as the design draws it. *Not spam* says it asks first where it would move the
    // message out of the Spam folder, and *Spam* always does, since it opens the chooser.
    const out = refusing !== null;
    const spamItem: ContextMenuItem = {
        icon: out ? 'report_off' : 'report',
        label: translate(
            out ? (acts.notSpamMoves(messages) ? 'spam.notSpamAsking' : 'spam.notSpam') : 'spam.spamAsking',
        ),
        choose: () => {
            acts.toggleSpam(messages);
        },
    };
    const spamItems: readonly ContextMenuItem[] = acts.spamOffered && messages.length > 0 ? [spamItem] : [];

    // The spam item takes *Move*'s place in the order whether or not *Move* is offered: marking spam is the flag grant
    // rather than the move grant, so a credential that may not file mail, or a move still on its way, keeps it.
    const spamPlace = actsInARowMenu.indexOf('move');
    const offeredActs = (slots: readonly MailboxAct[]): readonly ContextMenuItem[] =>
        slots
            // The read item and the flag item are the two slots that go both ways, and which way is the message's own
            // state rather than the menu's — `mailboxActs/drawnActs.ts` holds both rules, and they are the rules a
            // strip reads, so a row's menu and the toolbar over it never offer opposite directions for one message.
            .map((slot) =>
                slot === 'markUnread'
                    ? readActFor(acts, marking, messages)
                    : slot === 'flag'
                      ? flagActFor(acts, messages)
                      : slot,
            )
            .filter((act) => acts.refusalOf(act, messages) === null && !underway(acts, act, messages))
            .map((act) => ({
                icon: actsDrawn[act].icon,
                label: translate(actsSaidInAMenu[act]),
                destroys: act === 'delete',
                choose: () => {
                    if (act === 'delete' || act === 'move') {
                        onAsk(act, messages);
                    } else {
                        acts.perform(act, messages);
                    }
                },
            }));

    const mailbox = [
        ...offeredActs(actsInARowMenu.slice(0, spamPlace)),
        ...spamItems,
        ...offeredActs(actsInARowMenu.slice(spamPlace)),
    ];

    // Where a reading is checked. It stands at the foot rather than among the acts, because it changes nothing about
    // the mailbox: what it opens is an explanation of a sentence the row is drawing, and putting it beside archiving
    // and deleting would read as a sixth thing that happens to the message.
    const readings: readonly ContextMenuItem[] =
        onCheckReadings === undefined
            ? []
            : [{ icon: 'auto_awesome', label: translate('reading.check'), choose: onCheckReadings }];

    const asking: readonly ContextMenuItem[] =
        askAgent === null ? [] : [{ icon: 'auto_awesome', label: translate('agent.askAgent'), choose: askAgent }];

    return [
        { icon: 'check_box', label: translate('mail.selectMessages'), choose: onSelect },
        ...asking,
        ...answering,
        ...mailbox,
        ...readings,
    ];
}
