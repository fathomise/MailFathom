// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailDraftAnswer } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import type { IconName } from '../controls/icons';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import type { ListFold } from '../messageRows/listFold';
import { useLocalization } from '../localization/useLocalization';
import { MailboxActControls } from '../mailboxActs/MailboxActControls';
import { allDrawnSpam, useMailboxActs } from '../mailboxActs/useMailboxActs';
import { MoreActions } from '../messageSource/MoreActions';
import { SpamControl } from '../spamMarking/SpamControl';
import { actedMessages, useListedMail } from '../messageList/useListedMail';
import { useDesktopComposition, useTwoPanes } from '../shell/useWideWorkspace';
import { useWorkspace } from '../workspace/useWorkspace';
import type { StripFitting } from './useStripFit';

// The strip the design draws across the top of the Mail space: composing, and the eight things a person does
// to a message. Four of them write a message and five change the mailbox it is in, and both halves act here — the
// mailbox half over the message that is open, which is what the design draws this strip as being about.
//
// What is open and whether writing is offered are both read from context rather than handed down. The toolbar is three
// components below the frame that knows either, and neither is this strip's to own: the mail space already reads the
// workspace, the composer is asked for from three unrelated places, which is what `useComposing` exists for, and where
// the open message belongs is the list's, which is what `useListedMail` exists for.
//
// **How wide a control is drawn is measured rather than decided at a width.** The design keeps every name in words for
// as long as the nine of them fit the strip; when they stop fitting, composing leaves the strip for the floating
// control over the list first, and only if the eight names still do not fit do they become symbols. That measurement
// is the mail space's, because the floating control is drawn over a column this strip is not in — so the space owns
// the fitting and hands this strip the half of it that is drawn here.

// Answering a message: which of the three answers each control writes, and the symbol and name the design gives it.
const answers: readonly { readonly answer: MailDraftAnswer; readonly icon: IconName; readonly label: MessageKey }[] = [
    { answer: 'senderOnly', icon: 'reply', label: 'mail.reply' },
    { answer: 'everyone', icon: 'reply_all', label: 'mail.replyAll' },
    { answer: 'forward', icon: 'forward', label: 'mail.forward' },
];

export function MailToolbar({
    strip,
    fit,
    openMailboxes,
    listFold,
}: StripFitting & {
    /** Opens the mailboxes drawer, or `null` where the mailbox column is docked beside the list instead. */
    readonly openMailboxes: (() => void) | null;

    /** The fold's list, or `null` where the composition draws the list whole. */
    readonly listFold: ListFold | null;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const composing = useComposing();
    const listed = useListedMail();
    const twoPanes = useTwoPanes();
    const docked = useDesktopComposition();
    const open = workspace.selection;
    const folded = workspace.mailboxesFolded;
    const panelsHidden = workspace.panelsHidden;
    const actShape: ControlShape = fit === 'symbols' ? 'symbol' : 'labelled';

    // An Outbox is read-only, so while one stands in the list's place the strip keeps the column's control and
    // composing and drops every act on a message — and the panels toggle, which has no correspondence to give room to.
    const outboxOpen = workspace.outbox !== null;

    // A message marked spam cannot be answered, so *Not spam* stands where the three answers would.
    const acts = useMailboxActs();
    const openMessages = open === null ? [] : actedMessages(listed, [open]);
    const openSpam = allDrawnSpam(acts, openMessages);

    return (
        <div
            ref={strip}
            role="toolbar"
            aria-label={translate('mail.toolbar')}
            className="flex shrink-0 items-center gap-0.5 overflow-x-auto border-b border-line bg-panel px-3 py-2 shadow-raised"
        >
            {/* The controls that change the view rather than a message lead the strip: the mailbox column's own show
                and hide where it is docked beside the list, and otherwise — on the fold, with something open — the
                list's own fold, followed by the drawer's opener. Here rather than in the list, so a long tree or a long list never
                scrolls them away. The rule after them keeps them apart from composing, so a press meant for one never
                lands on the other. They and their rule are one child of the strip, which is what the fitting reads
                ahead of composing. */}
            <span className="flex shrink-0 items-center self-stretch">
                {docked ? (
                    <Control
                        label={translate(folded ? 'mailboxes.unfold' : 'mailboxes.fold')}
                        icon={folded ? 'left_panel_open' : 'left_panel_close'}
                        shape="symbol"
                        onPress={() => {
                            revise({ mailboxesFolded: !folded });
                        }}
                    />
                ) : null}

                {listFold === null ? null : <ListFoldControl fold={listFold} />}

                {openMailboxes === null ? null : (
                    <Control label={translate('mailboxes.open')} icon="menu" shape="symbol" onPress={openMailboxes} />
                )}

                <span aria-hidden="true" className="ms-1.5 me-2 w-px self-stretch bg-line" />
            </span>

            {/* Composing stands first, and only while the names fit beside it: otherwise it is the floating control
                over the list, which the mail space draws. The divider after it is what the measurement reads as the
                second child, so nothing may stand between the two. */}
            {fit !== 'labelled' ? null : (
                <>
                    {composing.offered ? (
                        <Control
                            label={translate('mail.compose')}
                            icon="edit_square"
                            shape="primary"
                            onPress={() => {
                                composing.compose({ kind: 'new' });
                            }}
                        />
                    ) : (
                        <PlannedControl label={translate('mail.compose')} icon="edit_square" shape="primary" />
                    )}

                    <span aria-hidden="true" className="mx-0.5 w-px self-stretch bg-line" />
                </>
            )}

            {/* Answering needs something to answer, so with nothing open each of the three is drawn as what it is
                rather than left out: a strip whose controls appear as a message is opened is one that moves under a
                reader's cursor. */}
            {outboxOpen ? null : openSpam ? (
                <SpamControl messages={openMessages} shape={actShape} wording="mark" />
            ) : (
                answers.map((answering) =>
                    composing.offered && open !== null ? (
                        <Control
                            key={answering.icon}
                            label={translate(answering.label)}
                            icon={answering.icon}
                            shape={actShape}
                            onPress={() => {
                                composing.compose({ kind: 'answer', answers: answering.answer, storedEmailId: open });
                            }}
                        />
                    ) : (
                        <PlannedControl
                            key={answering.icon}
                            label={translate(answering.label)}
                            icon={answering.icon}
                            shape={actShape}
                        />
                    ),
                )
            )}

            {/* The five that change the mailbox, over the one message that is open. With nothing open each of them
                says so rather than being left out, for the reason the three answers above are drawn either way. */}
            {outboxOpen ? null : <MailboxActControls messages={openMessages} shape={actShape} spam="toolbar" />}

            {/* Where two panes stand below the desktop composition the head leaves its acts to this strip, so *More
                actions* follows the spam control here instead, and every composition reaches it once. The strip holds
                no subject, so its menu is headed by its own name. */}
            {outboxOpen || !twoPanes || docked ? null : <MoreActions storedEmailId={open} subject={null} />}

            {/* The one control in the strip that changes the view rather than the message, which is why the design
                keeps it apart at the far edge and draws it as its symbol alone at every width. What it takes away is
                everything the design draws around the correspondence — the head of the thread, and beside it the state
                panels the client does not draw yet — so it is the head that goes today and the panels that join it as
                they arrive. It is a setting for the space rather than for a message, so it holds as the reader moves
                from one thread to the next, and it says which way it is set: the symbol, the name, and `aria-pressed`
                all turn over together. Drawn only where the composition has two panes, because that is where the
                design gives it something to do — a single pane keeps the head, which carries the way back to the
                list. */}
            {twoPanes && !outboxOpen ? (
                <Control
                    label={translate(panelsHidden ? 'mail.showPanels' : 'mail.hidePanels')}
                    icon={panelsHidden ? 'fullscreen_exit' : 'fullscreen'}
                    shape="symbol"
                    pressed={panelsHidden}
                    className={`ms-auto ${panelsHidden ? 'bg-accent-soft text-accent-strong' : ''}`}
                    onPress={() => {
                        revise({ panelsHidden: !panelsHidden });
                    }}
                />
            ) : null}
        </div>
    );
}

// The fold's list control, one control for both ways: it widens the strip and narrows the widened list. Held while the
// list is widened, it keeps the list open as other messages are opened, and held again it lets the list fold at the
// next one — so what it says about itself names the hold, and a list kept open is drawn the way the strip draws a view
// that is switched on.
function ListFoldControl({ fold }: { readonly fold: ListFold }) {
    const { translate } = useLocalization();

    if (fold.folded) {
        return <Control label={translate('mail.showList')} icon="left_panel_open" shape="symbol" onPress={fold.turn} />;
    }

    return (
        <Control
            label={translate('mail.narrowList')}
            hint={translate(fold.kept ? 'mail.listKept' : 'mail.narrowListHold')}
            icon="left_panel_close"
            shape="symbol"
            className={fold.kept ? 'bg-accent-soft text-accent-strong ring-1 ring-accent-line ring-inset' : ''}
            onPress={fold.turn}
            onHold={fold.keep}
        />
    );
}
