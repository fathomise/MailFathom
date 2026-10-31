// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { useComposing } from '../composer/useComposing';
import { Control } from '../controls/Control';
import { PlannedControl } from '../controls/PlannedControl';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer, useScreenStep } from '../shell/screenLayers';
import {
    useCoarsePointer,
    useDesktopComposition,
    useMonitorComposition,
    useRoomyComposition,
    useTwoPanes,
    useWideWorkspace,
} from '../shell/useWideWorkspace';
import { useMailboxesForm } from '../workspace/mailboxesForm';
import { scopeKey } from '../workspace/mailScope';
import { useWorkspace, type Workspace } from '../workspace/useWorkspace';
import { AiFilters } from './AiFilters';
import { BackToList } from './BackToList';
import { MailboxesDrawerContext } from './mailboxesDrawer';
import {
    listWidthWithin,
    narrowestList,
    readListWidth,
    storeListWidth,
    listWidthStep,
    startingListWidthIn,
    widestList,
} from './listWidth';
import {
    mailboxesWidthStep,
    mailboxesWidthWithin,
    narrowestMailboxes,
    readMailboxesWidth,
    startingMailboxesWidthIn,
    storeMailboxesWidth,
    widestMailboxes,
} from './mailboxesWidth';
import { WidthGrip } from '../controls/WidthGrip';
import { MailToolbar } from './MailToolbar';
import { SelectionBar } from './SelectionBar';
import { useStripFit } from './useStripFit';
import { paneArrival, usePaneMotion } from '../shell/paneMotion';

// The Mail space as the design composes it, out of the three regions a mail client is: the mailboxes, the list
// of what is in the one that is scoped, and what is open from it. What this component owns is the composition alone —
// each region is handed in already built — and the composition comes out of one tree, decided by the width the space
// has been given rather than by which head it runs on.
//
// **The width decides it, in three separate questions rather than one asked three ways, and the pointer decides what
// a narrower composition folds into.** The design's prototype asks exactly these, and `shell/useWideWorkspace.ts` is
// where each is asked once:
//
// - *Two panes or one.* Below that width the space draws the list **or** what is open and never both, so a row that is
//   not on the screen is not in the document either; above it the two stand side by side.
// - *The mailbox column, its strip, or a drawer.* Only the desktop composition has room for the column to stand open,
//   so below it the column is folded — to a drawer over the space under a finger, reached from the toolbar's lead and
//   from the list's own header on the phone and during a selection, where no toolbar stands; and to its strip of
//   symbols under a mouse, which the toolbar's lead opens to the column again. `workspace/mailboxesForm.ts` holds it.
// - *The toolbar at all*, which is the phone question: bottom navigation already spends the foot of a phone-width
//   window, and the toolbar's acts are on the selection bar and the message's own head there.
//
// The boundaries the columns are dragged at stand wherever two columns stand beside each other, and a mouse and a
// finger drag them alike — the finger by the grip the design draws in each one's middle. The mailboxes' boundary is
// absent only where that column is folded to its strip or into a drawer. Nothing is hidden by the width alone; what a
// narrower composition cannot show at once is reached rather than dropped.
//
// Where focus goes when the single-pane shape changes what it shows is this component's too: a message coming in front
// of the list is a view change, and so is going back, and a reader on a keyboard is put at the start of what replaced
// what they were on rather than left on an element that is no longer there. A resize that changes the shape is not, so
// the width changing moves nothing.
//
// How the wide shape divides its width between the list and the pane is the reader's, and this is where that lives: it
// is the composition's own number rather than either column's, and only the shape that draws both columns has a
// boundary to move.
//
// A person working in tabs gets one more row above all of it, and it is handed in already built for the reason every
// region is: what is open is the frame's rather than this composition's, and what this owns is that the strip stands
// over the whole space rather than over one column of it — and that the narrow shape draws none of it.

export function MailSpace({
    folders,
    list,
    mail,
    tabs,
    readingFooted,
    status,
    person,
}: {
    /** The scope selector, which is the mailbox column's first thing. */
    readonly folders: ReactNode;

    /** The mail in scope, searchable, which is the middle column. */
    readonly list: ReactNode;

    /** What is open, which is the reading column. */
    readonly mail: ReactNode;

    /**
     * The strip naming everything open, which stands across the top of the space above the toolbar.
     *
     * `null` where a person is not working in tabs, or has nothing open — which is every narrow window, since a row of
     * tabs above a mailbox column and a list needs room the composition does not have there.
     */
    readonly tabs: ReactNode;

    /**
     * Whether what is open draws the AI button at its foot, which the reading column then lays out for: the button is
     * the message's or the conversation's own, drawn last inside it because that is what knows which conversation an
     * *Ask* hands over, and the column is what keeps it at the bottom however far the reading is scrolled.
     *
     * `false` wherever the column is not reading a correspondence — nothing open, a file or a sender's markup in its
     * place, a message being written — because the design draws it under an open message and nowhere else.
     */
    readonly readingFooted: boolean;

    /** What the deployment says about the connection, which stands at the foot of the mailbox column. */
    readonly status: ReactNode;

    /**
     * Who is signed in, which is what the split is kept under: two people sharing a machine each get their own.
     *
     * `null` where the credential this client holds names nobody it composed, which is a split that lasts the run
     * rather than a screen that refuses to draw.
     */
    readonly person: string | null;
}) {
    const { locale, translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const composing = useComposing();
    const wide = useWideWorkspace();
    const twoPanes = useTwoPanes();
    const desktop = useDesktopComposition();
    const monitor = useMonitorComposition();
    const coarse = useCoarsePointer();
    const mailboxes = useMailboxesForm();

    // Each column stands at the width its band draws it at until somebody drags it, and at what was dragged after that:
    // `null` is nothing chosen. The room the list and the pane share is measured below, and the drawn width is the
    // chosen one brought inside it.
    const [chosenListWidth, setChosenListWidth] = useState(() => readListWidth(person));
    const [chosenMailboxesWidth, setChosenMailboxesWidth] = useState(() => readMailboxesWidth(person));
    const [listRoom, setListRoom] = useState(Number.POSITIVE_INFINITY);
    const roomy = useRoomyComposition();
    const startingList = startingListWidthIn(desktop, monitor, roomy);
    const startingMailboxes = startingMailboxesWidthIn(monitor);
    const listWidth = listWidthWithin(chosenListWidth ?? startingList, listRoom);
    const mailboxesWidth = chosenMailboxesWidth ?? startingMailboxes;

    const selecting = workspace.selected.length > 0;
    const [drawerOpen, setDrawerOpen] = useState(false);
    const drawer = useRef<HTMLDialogElement>(null);
    const drawerNamed = useId();

    function openDrawer(): void {
        drawer.current?.showModal();
        setDrawerOpen(true);
    }
    const listColumn = useRef<HTMLElement>(null);
    const readingColumn = useRef<HTMLElement>(null);

    // What is being written stands where what is being read stands, so the narrow shape brings the reading column
    // in front of the list for a message somebody is writing exactly as it does for one they opened.
    const outboxEntry = workspace.outbox?.entry ?? null;
    const readingOpen =
        workspace.selection !== null ||
        workspace.conversation !== null ||
        outboxEntry !== null ||
        composing.opening !== null;
    const readingInFront = !twoPanes && readingOpen;
    const paneMoved = usePaneMotion(readingInFront, twoPanes);

    // A message read in front of the list is one press of the back gesture away from it, exactly as the way back
    // above it is. Something being written is not: the composer stands there as a surface of its own and answers the
    // gesture itself, asking first where there is something to lose.
    useScreenStep(!twoPanes && (workspace.selection !== null || workspace.conversation !== null), () => {
        revise({ selection: null, conversation: null });
    });

    // A send read in front of an Outbox goes back to that Outbox the same way.
    const outbox = workspace.outbox;

    useScreenStep(!twoPanes && outbox !== null && outbox.entry !== null, () => {
        if (outbox !== null) {
            revise({ outbox: { account: outbox.account, entry: null } });
        }
    });

    // The list is the last thing the *fullscreen* control takes away, and only at a tablet-class width: the design's
    // `showThreadList` is off where a tablet has both panes and something open under that control, and on at every
    // desktop width, where there is room for the correspondence to stand alone without the list going with it. The
    // toolbar stays either way, which is what leaves the control there to turn it back off. An Outbox keeps its list,
    // because the toolbar draws no such control while one is open.
    const listGivenUp = workspace.panelsHidden && twoPanes && !desktop && readingOpen && outbox === null;

    // Whether the list stands behind what is in front of it rather than beside it. Both cases above put the reading
    // column where the list was, and neither is a reason to take the list down: what it holds is the pages the
    // deployment answered and the place in them the reader had scrolled to, so a reader who opens a message on a phone
    // and goes back is owed the list they left rather than a skeleton of it — `shell/Space.tsx` carries the whole of
    // that reasoning, including why standing a surface aside is `visibility` and not `display`.
    const listAside = readingInFront || listGivenUp;

    // How the toolbar fits its width is measured here rather than in the toolbar, because what it decides is drawn in
    // two places: the names in the strip, and — once composing no longer fits beside them — the floating control over
    // the list, which is this space's to draw. The selection bar measures itself; nothing outside it moves. The controls
    // that change the view — the mailbox column's, or the drawer's — stand ahead of composing as
    // one child of the strip at every width the toolbar is drawn at.
    const mailboxesInDrawer = mailboxes === 'drawer';
    const { strip: toolbarStrip, fit: toolbarFit } = useStripFit(
        true,
        `${locale}:${String(mailboxesInDrawer)}:${String(coarse)}`,
        1,
    );
    const toolbarDrawn = !selecting && wide;

    // Read through the form rather than held here, because the column is not the only thing that draws differently
    // once it is folded: the tree inside it draws a symbol where it drew a name, and the tree is a region handed in
    // already built rather than a child this component could pass a prop to.
    const folded = mailboxes === 'strip';

    // A width chosen on one screen is read back on whatever screen the client opens on next, so the window is measured
    // rather than trusted: what the two columns share is what they measure to, and a width the window no longer has
    // room for is drawn at what fits instead of pushing the message off the side.
    //
    // This one stays a `ResizeObserver` rather than giving way to a container query, because what it answers is a
    // number rather than a style: the grip announces the width it stands at and steps from it by key, so the width
    // has to be the one drawn, and a query can lay a column out but cannot hand a measurement back to script. An effect
    // because the observer is something outside React, and it watches both columns because what the two of them add up
    // to is the measurement — a number nothing this draws can move, since the pane takes back whatever the list gives
    // up. So it settles in one pass rather than chasing itself.
    useEffect(() => {
        const listed = listColumn.current;
        const reading = readingColumn.current;

        if (!twoPanes || listed === null || reading === null || typeof ResizeObserver !== 'function') {
            return;
        }

        const watched = new ResizeObserver(() => {
            const room = listed.offsetWidth + reading.offsetWidth;

            if (room > 0) {
                setListRoom(room);
            }
        });

        watched.observe(listed);
        watched.observe(reading);

        return () => {
            watched.disconnect();
        };
    }, [twoPanes]);

    // Focus is placed on the shape changing what it shows, and not on the width changing the shape: both are recorded
    // and only the first moves anything. A ref rather than state, because what it holds is what was last drawn.
    //
    // Both breakpoints are recorded rather than only the one that swaps the panes, because the list also comes and
    // goes on the desktop boundary while the panels are hidden — a window dragged from a desktop width to a tablet
    // one would otherwise take focus off whatever the reader was on, on the strength of a resize they asked nothing
    // of this screen by.
    const shown = useRef({ twoPanes, desktop, readingInFront, listGivenUp });

    useEffect(() => {
        const before = shown.current;
        shown.current = { twoPanes, desktop, readingInFront, listGivenUp };

        if (before.twoPanes !== twoPanes || before.desktop !== desktop) {
            return;
        }

        if (before.readingInFront !== readingInFront) {
            (readingInFront ? readingColumn : listColumn).current?.focus();
        } else if (before.listGivenUp !== listGivenUp) {
            // The *fullscreen* control takes the list away and gives it back without the shape changing, so focus is
            // placed the same way the narrow shape places it when the two columns swap: on whichever one is drawn now.
            // Without this a reader whose focus was inside the list is left on a removed element, which is where
            // keyboard and screen-reader use stops silently.
            (listGivenUp ? readingColumn : listColumn).current?.focus();
        }
    }, [twoPanes, desktop, readingInFront, listGivenUp]);

    // The drawer is a way to point at a mailbox, so pointing at one closes it: a reader who chose a folder wants the
    // folder's mail, which is behind the drawer they chose it in. The dialog is the platform's own, so closing it puts
    // focus back on the control that opened it without anything here remembering which control that was.
    // Opening an account's Outbox is pointing at it the same way, from the mark on its Sent row.
    const scope = `${scopeKey(workspace.scope)}|${workspace.outbox?.account ?? ''}`;
    const shownScope = useRef(scope);

    useEffect(() => {
        if (shownScope.current !== scope) {
            shownScope.current = scope;
            drawer.current?.close();
        }
    }, [scope]);

    // The drawer belongs to the compositions that fold the mailbox column into one, so a window widened past them — or
    // a mouse taking over from a finger — takes it off the screen without any way out of it firing `close`. What it was
    // is given up as the composition changes rather than in an effect answering that it did: the change is what caused
    // it, so a render answers it and nothing is left standing for a frame. Coming back therefore finds a drawer that is
    // shut, which is what it is.
    const [composedWithDrawer, setComposedWithDrawer] = useState(mailboxesInDrawer);

    if (composedWithDrawer !== mailboxesInDrawer) {
        setComposedWithDrawer(mailboxesInDrawer);
        setDrawerOpen(false);
    }

    // The drawer stands over the space rather than beside it, so the back gesture closes it before it takes a message
    // off the screen, and taking the navigation to another destination leaves none of it behind. Whether it is open is
    // held here because the platform publishes no such value: `close` is the one event every way out of it fires — the
    // control inside it, Escape, and the back gesture alike — so this cannot disagree with what is on the screen.
    useScreenLayer(drawerOpen, () => {
        drawer.current?.close();
    });

    // Every width a move produces is held inside the same bounds, so a drag and a key cannot disagree about where the
    // boundary may stand. A room of nothing is a layout that has not happened yet — an environment that computes no
    // sizes, or the frame before the first one — and is read as an unmeasured window rather than as no room at all.
    function withinTheRoom(width: number): number {
        const room = (listColumn.current?.offsetWidth ?? 0) + (readingColumn.current?.offsetWidth ?? 0);

        return listWidthWithin(width, room > 0 ? room : Number.POSITIVE_INFINITY);
    }

    return (
        <div className="flex min-h-0 flex-1 flex-col">
            {desktop ? tabs : null}

            {/* The bar replaces the toolbar while messages are picked out, which is the design's composition:
                one strip saying what the next press is about. It stands at every width because the narrow shape has no
                toolbar to replace and a selection still needs both a way to act on it and a way out of it. */}
            {selecting ? (
                <SelectionBar />
            ) : toolbarDrawn ? (
                <MailToolbar
                    strip={toolbarStrip}
                    fit={toolbarFit}
                    openMailboxes={mailboxesInDrawer ? openDrawer : null}
                />
            ) : null}

            <div className="relative flex min-h-0 flex-1">
                {!mailboxesInDrawer ? (
                    <>
                        <aside
                            aria-label={translate('mailboxes.open')}
                            /* Folded, the column is the rail's own width and nothing is draggable about it; open, it is
                           whatever this person dragged it to, or the width its band opens it at. */
                            className={`flex shrink-0 flex-col bg-sunken ${folded ? 'w-mailboxes-folded border-e border-line' : ''}`}
                            style={folded ? undefined : { width: `${String(mailboxesWidth)}px` }}
                        >
                            <Mailboxes folders={folders} status={status} folded={folded} drawerHead={null} />
                        </aside>

                        {/* The boundary is only there where the column is drawn as one: folded to its rail it is the
                            width the rail is, and a grip on it would offer to widen something the fold decided. */}
                        {folded ? null : (
                            <WidthGrip
                                label={translate('folders.width')}
                                hint={translate('folders.widthHint')}
                                narrowest={narrowestMailboxes}
                                widest={widestMailboxes}
                                step={mailboxesWidthStep}
                                startingWidth={startingMailboxes}
                                width={mailboxesWidth}
                                onWidth={(moved) => {
                                    setChosenMailboxesWidth(mailboxesWidthWithin(moved));
                                }}
                                onChosen={(chosen) => {
                                    const settled = chosen === startingMailboxes ? null : mailboxesWidthWithin(chosen);

                                    setChosenMailboxesWidth(settled);
                                    storeMailboxesWidth(person, settled);
                                }}
                            />
                        )}
                    </>
                ) : (
                    <dialog
                        ref={drawer}
                        onClose={() => {
                            setDrawerOpen(false);
                        }}
                        aria-labelledby={drawerNamed}
                        data-pull="start"
                        // It stands in the platform's top layer, which the frame's own safe-area padding is not around,
                        // so a drawer running the height of the screen carries the insets it crosses itself.
                        className="surface-drawer fixed inset-y-0 left-0 m-0 h-full max-h-none w-drawer max-w-full border-0 border-e border-line bg-panel p-0 pt-safe-top pb-safe-bottom pl-safe-left text-text shadow-overlay backdrop:bg-scrim"
                    >
                        <div className="flex h-full flex-col">
                            <Mailboxes
                                folders={folders}
                                status={status}
                                folded={false}
                                drawerHead={
                                    <div className="flex shrink-0 items-center gap-2 pt-3 pr-2.5 pb-2 pl-4">
                                        <h2 id={drawerNamed} className="min-w-0 flex-1 text-lg font-semibold">
                                            {translate('mailboxes.open')}
                                        </h2>

                                        <SurfaceControl
                                            label={translate('mailboxes.close')}
                                            icon="close"
                                            edged
                                            onActivate={() => {
                                                drawer.current?.close();
                                            }}
                                        />
                                    </div>
                                }
                            />
                        </div>
                    </dialog>
                )}

                <section
                    ref={listColumn}
                    tabIndex={-1}
                    aria-label={translate('mail.listColumn')}
                    aria-hidden={listAside ? true : undefined}
                    inert={listAside}
                    /* Aside, it is laid out over the row at the width it is drawn at rather than at whatever the row
                       has left, so the offset it keeps points at the row it pointed at. Only the two narrow cases
                       reach it: the width somebody dragged is read below and never here. */
                    className={`@container flex min-h-0 min-w-0 flex-col bg-panel ${paneArrival(paneMoved, false)} ${
                        listAside
                            ? `invisible absolute inset-y-0 start-0 ${
                                  twoPanes ? (roomy ? 'w-list-tablet' : 'w-list-narrow') : 'w-full'
                              }`
                            : twoPanes
                              ? ''
                              : 'flex-1'
                    }`}
                    /* The one width in the client a person sets rather than the design, which is why it is drawn
                       from a value instead of a utility: the band's own width until somebody drags it, and what was
                       dragged after that. The narrow shape takes the whole column, so it does not read this.
                       Left able to shrink on purpose: a width chosen on a wider screen is read back before
                       anything has measured this one, and a column that refuses to give way in that frame would
                       push the message off the side. Flexbox holds it inside the window until the measurement
                       above brings it back to a width that fits. */
                    style={twoPanes && !listAside ? { width: `${String(listWidth)}px` } : undefined}
                >
                    {/* The list and, wherever composing has no place in the toolbar, the control that writes a
                        message standing over its bottom corner: the narrow shape has no toolbar, and a wider one
                        gives composing up first when the names stop fitting — `useStripFit.ts` holds that order.
                        It goes while messages are picked out, because the bar standing then is about what is
                        picked out and writing is not.
                        Over the *list* rather than over the window, because the window's own bottom corner is
                        where the question field and the navigation are: a control placed against the viewport
                        would sit on top of both, and it would move whenever either changed height. Positioned
                        against this box instead, it keeps the corner a thumb reaches whatever stands under it. */}
                    <div className="relative flex min-h-0 flex-1 flex-col">
                        {/* The list's head opens the drawer only where no toolbar stands to lead with it: the phone,
                            and a selection, whose bar replaces the toolbar. The column has no drawer and offers
                            nothing. */}
                        <MailboxesDrawerContext value={!mailboxesInDrawer || toolbarDrawn ? null : openDrawer}>
                            {list}
                        </MailboxesDrawerContext>

                        {(wide && toolbarFit === 'labelled') ||
                        readingInFront ||
                        selecting ? null : composing.offered ? (
                            <Control
                                label={translate('mail.compose')}
                                icon="edit_square"
                                shape="floating"
                                className="absolute right-4.5 bottom-4.5"
                                onPress={() => {
                                    composing.compose({ kind: 'new' });
                                }}
                            />
                        ) : (
                            <PlannedControl
                                label={translate('mail.compose')}
                                icon="edit_square"
                                shape="floating"
                                className="absolute right-4.5 bottom-4.5"
                            />
                        )}
                    </div>
                </section>

                {/* The boundary is only there where both columns are: one column at a time has nothing between them.
                    A mouse and a finger drag it alike, the finger by the grip the design draws in its middle. */}
                {twoPanes && !listAside ? (
                    <WidthGrip
                        label={translate('mail.listWidth')}
                        hint={translate('mail.listWidthHint')}
                        narrowest={narrowestList}
                        widest={widestList}
                        step={listWidthStep}
                        startingWidth={startingList}
                        width={listWidth}
                        onWidth={(moved) => {
                            setChosenListWidth(withinTheRoom(moved));
                        }}
                        onChosen={(chosen) => {
                            // Set back to the band's own width is no choice at all, so another band opens at its own.
                            const settled = chosen === startingList ? null : withinTheRoom(chosen);

                            setChosenListWidth(settled);
                            storeListWidth(person, settled);
                        }}
                    />
                ) : null}

                {twoPanes || readingInFront ? (
                    <section
                        ref={readingColumn}
                        tabIndex={-1}
                        aria-label={translate('mail.readingColumn')}
                        /* The container what stands inside it measures, through the `pane-*` container sizes in
                           `styles.css`: the pane's own width rather than the window's. */
                        className={`@container flex min-h-0 min-w-0 flex-1 flex-col bg-page ${paneArrival(paneMoved, true)}`}
                    >
                        {/* The way back to the list, above whatever the column draws — except a message, whose head
                            carries it beside the subject, which is where the design puts it. */}
                        {twoPanes || headCarriesTheWayBack(workspace, composing.opening !== null) ? null : (
                            <div className="flex items-center px-2 pt-2">
                                <BackToList />
                            </div>
                        )}

                        {/* Everything the pane lays out takes the pane's own width. The ceiling a wide window needs
                            binds the words of a message rather than the column they sit in — `messageBody/Message.tsx`
                            carries it — because a head, a verdict, a row of attachment cards, and the field the reader
                            asks in are not text read line by line and have no measure to keep. */}
                        {/* Under the AI button the scroller is a column whose content never shrinks below what it holds
                            and always fills the pane: what is open grows to the pane's height, so the button stuck to
                            its foot stands at the bottom of a short message, and at the bottom of the scrollport — not
                            part-way through the thread — however long a conversation is. Without it the inner box
                            is laid out as if it were not there, rather than taken away: one tree in both cases is
                            what keeps what is open mounted while Mail goes behind another space and comes back. */}
                        <div className={`min-h-0 flex-1 overflow-y-auto ${readingFooted ? 'flex flex-col' : ''}`}>
                            <div className={readingFooted ? 'flex shrink-0 grow flex-col' : 'contents'}>{mail}</div>
                        </div>
                    </section>
                ) : null}
            </div>
        </div>
    );
}

// The mailbox column's contents, drawn once for the two places they stand: the column beside the list, and the drawer
// in front of it. The AI filters first and every account under them, scrolling together as one region once they are
// taller than the column, and the connection at the foot, which stays where it is.
//
// The column carries no head of its own: the control that hides and shows it leads the mail toolbar, where a long tree
// cannot scroll it away. The drawer is the one place with a head, its name and the way to close it, because the
// toolbar's control does not stand where the column is a drawer.
//
// Folded, it is the same tree at the rail's width rather than a column emptied of it: the point of folding is to give
// the width to the list while keeping the mailboxes one click away. What the rail does drop is the connection line at
// the foot, which is a sentence and has nowhere to wrap to.
function Mailboxes({
    folders,
    status,
    folded,
    drawerHead,
}: {
    readonly folders: ReactNode;
    readonly status: ReactNode;
    readonly folded: boolean;

    /** The drawer's name and its way out, or `null` where the mailboxes stand as a column beside the list. */
    readonly drawerHead: ReactNode;
}) {
    return (
        <>
            {drawerHead}

            <div
                className={`flex min-h-0 flex-1 flex-col gap-1 overflow-x-hidden overflow-y-auto ${
                    drawerHead !== null ? 'px-3.5 pt-1 pb-4' : folded ? 'px-2.25 py-3' : 'px-3.5 pt-3.5 pb-3'
                }`}
            >
                <AiFilters folded={folded} />
                {folders}
            </div>

            {folded ? null : <div className="border-t border-line px-3.5 py-2.5">{status}</div>}
        </>
    );
}

// Whether what the reading column draws is a message, or a send of an Outbox, whose head carries the way back to the
// list itself. Everything else the column can hold — a conversation, the sender's own markup, an attachment, a message
// being written — takes the way back above it, drawn by the column. Read from the workspace with the same questions the
// frame asks to decide what the column draws, so the two cannot disagree about which surface is in front.
function headCarriesTheWayBack(workspace: Workspace, composing: boolean): boolean {
    if (composing) {
        return false;
    }

    if (workspace.outbox !== null) {
        return workspace.outbox.entry !== null;
    }

    return (
        workspace.selection !== null &&
        workspace.conversation === null &&
        workspace.fullHtml === null &&
        workspace.attachment === null
    );
}
