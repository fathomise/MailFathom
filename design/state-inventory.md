# The design's state inventory

Everything here is read out of the artboards beside it in `design/`. **This file is the source a session builds a
screen from; a rendered preview is not.** A preview shows the one state it was clicked into, and most of what is below
is invisible until something reveals it — which is exactly the reading a screenshot cannot give.

It is written in this repository beside the artboards it describes, so a change that adds, removes, or re-gates a
state in an artboard changes this file in the same pull request. `design/README.md` says what else lives in `design/`.

## What the seven artboards are

| Artboard | What it settles |
|---|---|
| `prototype.dc.html` | The whole signed-in application: six screens, every overlay, every composition, and the only artboard that carries navigation. Its logic is `logic/prototype/component.js` and its sample data `data/prototype/` |
| `client-states.dc.html` | The error, empty, waiting, refusal and in-flight states the client renders and the prototype does not draw, one artboard per state at every composition |
| `sign-in.dc.html` | Sign-in, and the server screen behind it |
| `toasts.dc.html` | Toasts and the blocking overlay — the system's answer to what somebody just did |
| `notification-gesture.dc.html` | The phone's notification-panel gesture, as four frames plus the numbers behind them |
| `ai-blocks.dc.html` | The block catalogue both AI surfaces draw: the nine types Discover renders read-only, and the four the Agent renders with controls, each in its three phases |
| `mail-search.html` | Mail search: results, empty, waiting, lexical-only, failure |

## How a state is named here

The prototype is one component. A screen is a region of one template gated by a flag, and every flag
is computed once in `renderVals()`. So a state's name in this file **is** the flag name in the
source: `grep -n 'showThreadPane' design/prototype.dc.html design/logic/prototype/component.js` gives both
the gate in the template and the one line that decides it. *Reveal* is what makes it true.

## How a window is composed

**Width, height, and pointer are three independent axes, and every composition rule reads exactly one of them.**
Width decides how many columns there are, height decides vertical density, and the pointer decides target sizes,
gestures, and hover. No rule reads orientation or the window's aspect, so a device upright and the same device on its
side need no rules of their own: each is one width band combined with one height band. All of it is decided once, in
`renderVals`, before any state is.

### Width — the columns

| Name | Condition | What it changes |
|---|---|---|
| `isMobile` | `w < 700` | Bottom navigation instead of the rail, every form a full-screen surface, the folder list always a drawer |
| `singlePane` | `w < 820` | One pane at a time — the list and the content never stand together |
| `narrow` | `700 ≤ w < 1020` | Two panes in little room: while a thread is open the list folds to a strip of senders (`listStrip`) |
| `compact` | `700 ≤ w < 1180` | The 66 px rail with a 30 px mark, the list at 284 px until a mouse drags it, toolbar controls without labels once their words no longer fit the strip — each still naming itself in a bubble, *A control's name* under § *What every artboard shares* — and the folder list folded rather than standing as a column |
| `wide` | `w ≥ 1920` | The folder column at 240 px and the list at 420 px |

From 1180 to 1920 the rail is 96 px, the folder column 210 px (draggable to 420 under a mouse) and the list 340 px
(draggable from 232 to 560). Past 1920 the window's extra width goes to the thread pane, whose reading column is
capped — § *Width inside a pane* below — so a 2560 px window shows a 96 px rail, a 240 px folder column, a 420 px list,
and the thread's 880 px column centred in what is left. Settings, which fills the window below that, stands past 1440 px as a 1440 px surface centred on `--bg` between two `--line` edges (`settingsCardStyle`).

**The folder list has three forms, and each question about it reads one axis.** Width says whether it stands: as a
column from 1180, folded below it, and in the phone's drawer under 700, where there is no rail to fold beside. The
pointer says what *folded* is between 700 and 1180: a drawer under a finger (`drawer`), because a finger has no hover
to tell a strip of glyphs apart by, and a 58 px strip of glyphs under a mouse (`sideCollapsed`), each named by its
title, which the toolbar's `left_panel_open` widens to the column again. So `drawer` is
`isMobile || (compact && touch)`, and `sideCollapsed` starts true wherever the column does not stand.

### Height — vertical density

| Name | Condition | What it changes |
|---|---|---|
| `low` | `h < 480` | Everything below folds to fit; the phone on its side is the case it is drawn for |
| `tall` | `h ≥ 960` | Lists and threads take the height; dialogs, sheets and the composer stop growing |

Between the two — 480 to 960, every framed preview included — the composition is the one drawn everywhere else in
this file: a dialog up to the window less 44 px, a sheet up to `82dvh`, the docked composer up to `86dvh`, and a
message's original HTML up to `90dvh`. The caps below apply in the tall band and nowhere else, so no frame moves.

**In a low window** (`low`):

| What | The rule |
|---|---|
| The rail | Glyphs alone: each destination keeps its label as its accessible name but draws none, its target is 44 px under a finger and 36 px under a mouse, and the product mark and the *Prototype* caption go (`railLogoShown`, `railCaptionShown`). The destinations scroll between the top of the rail and its pinned foot — refresh, the bell, the avatar — so every one stays reachable |
| The thread's head | Folded from the start: the subject on one line at 16 px, the address line gone, 8 px above and below (`thCollapsed`, `metaWrapStyle`). On one pane it stays folded whatever the scroll |
| A sheet | Up to the whole height less 8 px (`threadSheetStyle`); it scrolls, and the grabber stays at its top |
| A dialog | Up to the whole height less 16 px, standing 8 px off every edge rather than 22 (`dlgMaxH`, `ovStyle`); it scrolls as a whole |
| The composer | Its head shrinks to the height of its close control (`composeHeadStyle`); the fields, the AI drafting and the editor scroll together (`composeScrollStyle`) above a foot that stays pinned, so *Send* is on screen however far the keyboard shortens the window; the editor's floor drops from 170 to 72 px (`composeEditorStyle`). Docked, the composer takes up to the whole height less 8 px |
| The AI reading | Up to the whole height less 16 px |

**A phone on its side is held to *usable* rather than to a composition** — its 390 px of height leave no room for one.
It is the `compact`, `narrow`, two-pane width under a finger and in the low band, and it owes three things: nothing is
cut off or overlaps, every action is reachable (the rail scrolls, a dialog scrolls, the folder drawer opens from the
toolbar), and the composer can be written in and sent with the keyboard open. Nothing about it is drawn beyond what the
three axes already give.

**In a tall window** (`tall`):

| What | The rule |
|---|---|
| Lists | Take the height and show more rows; a row keeps its height and nothing stretches to fill |
| The thread | Takes the height; on one pane its head stays open as the thread scrolls instead of folding (`thCollapsed`), since there is room to keep it |
| A dialog | At most 760 px tall (`dlgMaxH`, `min(calc(100dvh - 44px), 760px)`), centred |
| A sheet | At most 640 px tall (`threadSheetStyle`, `min(82dvh, 640px)`) |
| The composer, docked | At most 760 px tall (`min(86dvh, 760px)`) |
| The AI reading | At most 860 px tall, as everywhere |
| A message's original HTML | At most 1100 px tall (`htmlWindowStyle`, `min(90dvh, 1100px, 100%)`): a message is laid out for a mail client's window, not for a monitor on its side |
| An opened document | Keeps `88dvh`: a page is tall, and the height is what it is read in |

### Pointer — targets, gestures and hover

`touch` is `pointer: coarse`, as the client asks it, and nothing else: a desktop window narrowed to the tablet band
keeps a mouse's sizes and panels, and a 1366 px touch tablet keeps a finger's. The framed previews say which they are —
the `layout` property's `phone`, `fold` and `tablet` frames are touch devices, `desktop` is a mouse, and `auto` asks
the browser (`st.coarse`, kept current by a `matchMedia` listener). Under `touch`: every target meets § *What every
touch composition holds to* below; sheets and drawers follow the finger; a row swipes and holds; what stands under tabs swipes between them; and nothing exists
only under a hover — not a control's name, not the reason a control cannot act, not a fact a row folds away (*A control's name* under
§ *What every artboard shares*). Without it: the desktop's sizes, hover affordances, and the two resizers (`showResizer`,
`showSideResizer`), which are drag handles and so a fine pointer's alone.

### Width inside a pane

What stands inside the thread pane responds to the width **that pane** was given (`paneW`), never to the window's,
because the same window gives the pane more or less as the list folds to its strip, is dragged wider, or goes into a
drawer. `paneW` is the window less the rail, the folder column or strip, and the list or its strip; on one pane it is
the window less the rail.

| Name | Condition | What it changes |
|---|---|---|
| `paneCompact` | `paneW < 760` | Off the phone (`stateCards`), the thread's state rides at the top of the thread as cards (`stateInBody`) rather than in a bar over it (`showThreadStateBar`); the head's *Thread state* label goes |
| `paneNarrow` | `paneW < 560` | The state cards stand in one column; the head's *Reply*, *Forward* and *Flag* become their glyphs in 34 px boxes (44 under a finger), named by title |
| The reading column | `paneW > 924` | The thread's messages stand in an 880 px column centred in the pane: their side padding is `max(22px, calc((100% - 880px) / 2))`. The head and the state bar do not: they run across the whole pane at every width, 22 px in from either edge — the head over its `--line` border, the state bar as a `--sub` band with a `--line` border under it — so what frames the thread stays on the pane's edges and only the conversation is centred. A table in a message keeps to the column at every pane width: its cells share the width and wrap, so the pane never scrolls sideways |

The Agent's thread and field centre the same way on their own pane, at 1290 px — `max(26px, calc((100% - 1290px) / 2))` — and a person and the task list, whose columns and figures want more than a message does, at 1160 px — `max(24px, calc((100% - 1160px) / 2))` on their head and body (`personHeadStyle`, `personBodyStyle`, and `tasksMainStyle` at 26 px, whose percentage is the whole body's and so leaves out the 310 px side column beside it). Neither reaches the 1440 px desktop, whose panes are narrower than the column.

### The matrix read through the three axes

Every size of the window-size matrix in `frontend/design-parity/screens.json`, with what the rules above give it at
Mail with a thread open. Each was captured from the design and reads as intended. `paneW` is the thread pane's width.

| Size | Window | Pointer | Height band | Rail | Folder list | List | `paneW` |
|---|---|---|---|---|---|---|---|
| `phone_360x780` | 360 × 780 | touch | regular | bottom bar | drawer | one pane | 360 |
| `telefon` | 390 × 844 | touch | regular | bottom bar | drawer | one pane | 390 |
| `phone_430x932` | 430 × 932 | touch | regular | bottom bar | drawer | one pane | 430 |
| `phone_844x390` | 844 × 390 | touch | low | 66 | drawer | 76 strip | 702 |
| `fold_832x884` | 832 × 884 | touch | regular | 66 | drawer | 76 strip | 690 |
| `fold` | 884 × 832 | touch | regular | 66 | drawer | 76 strip | 742 |
| `tablet_744x1133` | 744 × 1133 | touch | tall | 66 | drawer | one pane | 678 |
| `tablet_1133x744` | 1133 × 744 | touch | regular | 66 | drawer | 284 | 783 |
| `tablet_820x1180` | 820 × 1180 | touch | tall | 66 | drawer | 76 strip | 678 |
| `tablet_1180x820` | 1180 × 820 | touch | regular | 96 | 210 column | 340 | 534 |
| `tablet` | 1024 × 768 | touch | regular | 66 | drawer | 284 | 674 |
| `tablet_1024x1366` | 1024 × 1366 | touch | tall | 66 | drawer | 284 | 674 |
| `tablet_1366x1024` | 1366 × 1024 | touch | tall | 96 | 210 column | 340 | 720 |
| `desktop_1280x720` | 1280 × 720 | mouse | regular | 96 | 210 column | 340 | 634 |
| `desktop` | 1440 × 900 | mouse | regular | 96 | 210 column | 340 | 794 |
| `desktop_1920x1080` | 1920 × 1080 | mouse | tall | 96 | 240 column | 420 | 1164 |
| `desktop_2560x1440` | 2560 × 1440 | mouse | tall | 96 | 240 column | 420 | 1804 |
| `desktop_960x1040` | 960 × 1040 | mouse | tall | 66 | 58 strip | 76 strip | 760 |
| `desktop_1280x1400` | 1280 × 1400 | mouse | tall | 96 | 210 column | 340 | 634 |
| `desktop_1080x1800` | 1080 × 1800 | mouse | tall | 66 | 58 strip | 284 | 672 |
| `desktop_1440x2460` | 1440 × 2460 | mouse | tall | 96 | 210 column | 340 | 794 |

What the upright tablets are, drawn rather than fallen into:

- **744 × 1133** is one pane under a finger in a tall window: the rail beside the list, and the thread pushed over it
  when opened, its head kept open as it scrolls, its state as cards at its top.
- **820 × 1180** is the fold's composition in a tall window: two panes, the list folded to its strip of senders while a
  thread is open, the thread 678 px wide.
- **1024 × 1366** is the landscape tablet's composition in a tall window: the 284 px list beside a 674 px thread, the
  folder list a drawer, and the height given to more rows and more thread rather than to taller dialogs.

A mouse in the tablet band — **960 × 1040**, half a desktop screen, and **1080 × 1800**, a monitor on its side — gets
the mouse's sizes and panels: the 58 px folder strip, the resizer between the list and the thread, and below 1020 the
same strip of senders a finger gets. **1180 × 820** — the tablet on its side at the edge of the desktop band — is the
tightest two-column composition, a 534 px thread, and its pane's own rules carry it.

`FRAMES` is the design's own four sizes: `phone` 390 × 844, `fold` 884 × 832, `tablet` 1024 × 768, and the
artboard's 1440 × 900. All four are in the regular height band, so none of the rules above changes them.

### What every touch composition holds to

The phone, the fold and the tablet are drawn for a finger rather than narrowed from the desktop, and each of these is
true of every screen at all three — under `touch`, with the phone's own values under `isMobile`. The desktop
composition keeps its own sizes.

| What | The rule |
|---|---|
| A control's target | 44 × 44 px at least, and the target is not always the drawing. An icon button, a field, a standalone button, a row of a navigation and a text action such as *Mark all read* are drawn at 44; a chip in a wrapping group and a segment in its track are drawn at 36 and take the rest of the 44 from the space around them — 8 px between chips, the track's padding around a segment — so a group keeps the weight of the text beside it rather than outgrowing it. The phone keeps 48 where it already drew 48 — the back button and the thread's three actions; the folders button and the search field are 44 there as everywhere else under a finger. The glyph inside stays 20–24 px and the target grows around it; a label is centred in the target's height, never left at its top |
| The phone's bar | Every space names itself at the left of its top bar at 20 px semibold (`screenTitleStyle`, `tbTitleStyle`, `agentTitleStyle`), the bar at least 56 px tall with 16 px before the title, and its actions stand at the far end. Mail is the exception: its search field is its bar |
| The bottom navigation | Five places, each a 56 × 32 px pill behind its glyph (`--nav-mark`) that marks the current one, and a 12 px label beneath — the tile around it is not filled. The bar pads its foot by `env(safe-area-inset-bottom)` |
| Controls the bar has no place for | Stand in the *More* sheet, below the spaces and a rule: *Refresh*, then *Settings*. Each row of the sheet is 52 px. Nothing floats over the content of a screen except the one action that screen is for |
| A prompt | Fits one line at the width it is drawn at, and ends in an ellipsis where it cannot: the phone and the narrow list panes take the short form (*Search mail…*, *Ask your mail…*, *Tell me what to do…*, *Write what this is about…*) |
| Keys | No control names a key under `touch` — *Send*, and the composer's foot without *Ctrl+Enter sends* — because the screen has no keyboard to press it on |
| One choice among several | A segmented control — `--rail` track, the chosen segment raised on `--panel` — rather than loose bordered buttons: People's *Own* and *Collected*, the calendar's four views |
| Swiping between tabs | A swipe across what a strip of tabs or a segmented control shows steps to the tab beside it — left to the next one on the right, right to the one before — and does nothing past either end: People's *Own* and *Collected* across the list (`peopleSwipe`), the calendar's four views across its body (`calSwipe`), and Settings' sections across the section on one pane (`settingsSwipe`). It answers to a row's swipe numbers — counted at 96 px across, given up as a scroll once it drifts 26 px and more down than across — and acts the moment it counts, and once it engages at 14 px it calls off a row's long press, so one finger is still one gesture. A finger that lands in a field — a name, a number of seconds — is dragging the field's caret or a selection, and takes no swipe. The tabs above it stay and are pressed as before. A strip of open things — Mail's open tabs, the Agent's conversations — takes no swipe, because bringing one forward opens it, and neither does a choice that sets something, such as *Message view* |
| Symbols alone | Where a glyph names the action beyond doubt — attach, archive, delete, close, back, and the thread's reply, forward and flag — the phone draws the symbol alone in its 44 box, and the name stays its title and its accessible name. An action no glyph names keeps its words for as long as the strip it stands in has room for them, and narrows to its symbol only once the strip does not — measured rather than decided at a width. Either way its name is one hold away, as every symbol's is: a finger held on it for 420 ms shows it in the bubble without pressing it (*A control's name* under § *What every artboard shares*). The Mail toolbar's list control is the one symbol whose hold also does something: held while the list is widened, it keeps the list open as well (*The list kept open, below 1020*). *Select all* is words at every width, since no glyph names it |
| A sheet | Whatever rises from the bottom under a finger — the *More* sheet, the thread state, the phone's dialogs, Settings on one pane — closes by being pulled down as well as by its *X*, and shows where to take hold: a 38 × 4 px `--border2` grabber centred in a 20 px band at its top (`sheetGrabber`, `settingsGrabber`). The pull takes over only once the sheet's content is scrolled to its top; let go past a third of its height, or flung down, and it closes as its *X* would, otherwise it springs back. A drawer closes the same way, pushed back toward the edge it came from |
| Folded away | What a wider window stands in place but a narrow one folds away and slides out — the mail folders, the Agent's history (`historyScrimShown`), the list's filters, a drawer, a sheet — closes on its *X* and on a press anywhere beside it, which is a press on the scrim behind it or, for the filters band on the phone, anywhere outside the band and its control (`onDocDown`). A scrim takes the press it closes on, so nothing under it is pressed too |
| A long press | A finger held on a row opens the row's own menu and nothing closes it but a choice, a press beside it, Escape or the back gesture — the platform's own menu gesture that follows the same press is that press, not a second one. A finger held on a control inside the row shows that control's name instead, and the row's menu does not open as well: one held finger is one surface |
| A row's hidden facts | What a row draws only for a mouse to rest on — the sender a strip folds to an avatar, the instant behind a notification's relative age, the address behind an account's name, a folder's account, whole path and count — is said again under its menu's header (`ctxMenuDetail`), in `--faint` at 11 px, because the hold a finger would ask with is the hold that opens that menu. So a row folded to the strip still opens the mail menu on a hold, though it takes no swipe, and a row that opens no menu at all — the unified *All accounts* group and its folders — carries no title saying more than it draws |
| The fold's split | Two panes in less than 1020 px leave the thread the width, under a finger and a mouse alike: while a thread is open the list folds to a strip of senders (`listStrip`), and the toolbar's list control widens it again — § *The panes* under Mail. With nothing open the whole list stays |
| Duplicated actions | A two-pane composition under a finger does not repeat in the thread head what the toolbar above it already carries, so the subject keeps the width |
| Text | 12 px is the smallest size a reader is asked to read; 11 px is left to captions over something larger and to counts on a badge |
| What a hover explains | Is reached by a tap as well: a chip that explains itself under a mouse (`[data-tip]`) shows the same sentence when tapped, drawn at 36 with a 44 target, and the sentence goes by itself 4 s later or at the next press elsewhere. So does a control that cannot act (`aria-disabled`): a tap says why rather than doing nothing silently |

## What every artboard shares — `styles/base.css`

Every page links `styles/base.css` ahead of its own stylesheet. It holds what is decided once for the whole design
rather than per screen, and the scales below are what every inline value in an artboard is drawn from.

| What | Where it is stated | The rule |
|---|---|---|
| The palette | the `:root` tokens | Each token states its light and its dark value as one `light-dark()` pair and `color-scheme` picks between them: an artboard sets it through `data-theme`, and a page that sets none — `mail-search.html` — follows the browser. Every neutral is hue 262 in both themes, and the accent is the one blue |
| Text on a surface | `--text`, `--text2`, `--muted`, `--faint` | Four steps, and the lightest of them still reads at 4.5:1 on `--bg`, `--panel`, `--rail`, `--sub` and `--hover` in both themes, so no step is decoration only |
| The typeface | `body` | Geist, with Geist Mono for a literal — a state id, a file name — and tabular figures throughout |
| Text sizes | every `font-size` on text | 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28 and 34 px, and nothing between |
| Icon sizes | every `font-size` on a Material Symbols glyph | 12, 14, 16, 18, 20, 22, 24, 32 and 40 px |
| Radii | every `border-radius` | 4, 6, 8, 12, 16 and 24 px, `50%` on a circle and `999px` on a pill |
| Labels | every section caption | Sentence case at weight 600, never capitals and never tracked out |

A message shown as its sender wrote it is somebody else's HTML, so the bodies in `data/prototype/mail-bodies.js` follow
none of these scales and are not meant to. Neither does an opened attachment: its page is the document's own, typed in
`data/prototype/documents.js` and sized by `docZoom`, so its lines land wherever the zoom puts them.

Five reactions are stated there as well, and no rendered preview shows any of them at rest:

| State | Selector | What happens |
|---|---|---|
| Pressed | `:active` on a `button` and on `role="button"`, `"tab"` and `"menuitem"` | the control scales to 0.98 over 140 ms. A list row does not: it is swiped and long-pressed, and shrinking under the finger would read as a fault |
| Keyboard focus | `:focus-visible` on a control and on anything carrying `tabindex` | a 2 px `--accent` outline, 2 px off the edge. A text field keeps its own treatment instead — its border turns `--accent` (`style-focus`), and one drawn with no edge of its own hands the outline to the box around it, inset. The message body being written shows its caret and nothing else |
| Less motion asked for | `prefers-reduced-motion: reduce` | every animation and transition ends at once: the pulses, the shimmer, the row and toast movement, the press itself, and every arrival and departure below |
| A chip explaining itself | `[data-tip]` and its `role="tooltip"` child; `data-tip-dismissed` (set by `design.js`) | Every verdict chip and every confidence chip on an AI block is focusable and carries one sentence saying what its state means. A dark bubble — `--text` ground, `--panel` text, 13 px, radius 8, 7 × 10 px padding, at most 288 px wide — stands 6 px under the chip, from its start edge (the client also moves it back inside the window where it would spill). It shows once a mouse has rested on it for 400 ms, on keyboard focus, and on a tap; it goes when the pointer leaves, when focus moves on, on Escape, and 4 s after a tap. Hover is the shortcut and never the only way in |
| A control's name | every `[title]` but a frame's, written as the bubble `.mf-name-tip` by `design.js`; `data-tip-hover` | Every artboard writes a control's name as its `title`, and no touch browser draws a title, so the name is drawn in the same dark bubble as a chip's sentence instead, fixed to the window so a scrolling strip never cuts it, one at a time. A mouse rests on the control for 400 ms; a key focuses it; a finger holds it for 420 ms — the row around it then opens no menu and starts no swipe, the platform's own menu does not appear, and the tap the hold ends in does not press the control. One control's hold does a second thing beside reading its name: the Mail toolbar's list control, held while the list is widened, keeps the list open, and its bubble then names what the hold did (*The list kept open, below 1020*). A control that cannot act (`aria-disabled`) shows its reason on a tap. It goes at the next press anywhere, on scroll, on Escape, when the finger starts travelling, and 4 s after a finger brought it up. An element inside a row whose own hold opens a menu — an account or folder header, the twist that opens one, a sender's name, the strip of avatars, a flag mark — carries `data-tip-hover`: a mouse still reads it, and a finger reads it another way — a fact under that menu's header (*A row's hidden facts*), the twist's sentence in the turn of its chevron, the flag's in the menu's own *Remove the flag* |

### How a surface arrives and leaves

Every navigation and every overlay moves, and all of them move by one vocabulary stated in `styles/base.css`. An
element names its **kind** in `data-motion`; one that can leave names its **phase** in `data-phase`, `in` or `out`. A
screenshot shows none of this, so the numbers are the contract the client matches.

| Token | Value |
|---|---|
| `--motion-fast` | 120 ms |
| `--motion-base` | 180 ms |
| `--motion-slow` | 240 ms |
| `--ease-out` | `cubic-bezier(0.22, 1, 0.36, 1)` — arrivals |
| `--ease-in` | `cubic-bezier(0.4, 0, 1, 1)` — departures |
| `--ease-sheet` | `cubic-bezier(0.32, 0.72, 0, 1)` — anything that travels from an edge; the notification gesture's own spring |

| Kind | Arrives | Leaves | Used by |
|---|---|---|---|
| `scrim` | the colour fades up from transparent, base, ease-out | fades back to transparent, base, ease-in | every backdrop: the drawer, both sheets, every dialog, the context menu, Settings on two panes |
| `sheet` | from 100 % below its place, slow, ease-sheet | back down 100 %, slow, ease-in | the thread actions sheet (`threadSheetOpen`), the *More* sheet (`moreOpen`), and on the phone every full-screen surface: compose, the form dialogs, Settings on one pane; the AI reading wherever it fills the screen, the fold included |
| `drawer` | from 100 % to the left, slow, ease-sheet | back out to the left, slow, ease-in | the folder list where it is a drawer (`showSide` under `drawer`) |
| `pop` | from opacity 0 at 96 % scale, fast, ease-out | to opacity 0 at 96 %, fast, ease-in | the context menu (`ctxMenuOpen`), the user menu (`userMenuOpen`), the return pill (`convReturnShown`), every short question at every width — discarding a draft (`closeAskOpen`), the send warnings (`sendAskOpen`), deleting a folder, a conversation or an item (`delFolderOpen`, `deleteConvAskOpen`, `confirmDelOpen`), closing every tab (`closeAllAsk`), aborting a running operation (`cancelAskOpen`), marking spam not spam (`notSpamAskOpen`), showing the original message (`htmlAskOpen`), the composer's changed-context mark (`composeCtxChanged`), which arrives this way and goes at once rather than leaving — and off the phone every dialog (new folder, new task, new event, new contact, reminder, move, the spam chooser, AI summary, event) and Settings on two panes |
| `rise` | from opacity 0, 18 px below, base, ease-out | back down 18 px to opacity 0, base, ease-in | the composer where it docks over the screen rather than filling a pane, and the reply drafter (`replyDock`) |
| `fade` | from opacity 0, base, ease-out | to opacity 0, base, ease-in | the composer in a pane, the list replacing its skeleton (`listReady`), the composer body on restore (`composeExpanded`) |
| `veil` | at once | fades out, base, ease-out | the message skeleton (`msgLoading`) laid over a thread that is already there |
| `push` | from opacity 0, 48 px to the right, slow, ease-sheet | — | one pane at a time: the thread (`showThreadPane` under `singlePane`), a person (under `isMobile`) when it is opened from its list |
| `back` | from opacity 0, 48 px to the left, slow, ease-sheet | — | the same two lists when *Back* returns to them |
| `screen` | from opacity 0, 6 px below, base, ease-out | — | each space's root when the rail or the bottom navigation switches to it |
| `disclose` | from no height and opacity 0, base, ease-out | back to no height, base, ease-in | the list's filters (`filtersOpen`), so the list under them moves with them, and the composer's changed-context line (`composeCtxChanged`), which unfolds this way and goes at once rather than folding back; an engine that cannot interpolate a height to `auto` fades the band in place instead |
| `unfold` | from opacity 0, 10 px above its place, base, ease-out | back up 10 px to opacity 0, base, ease-in | the earlier messages of a conversation shown by its pill (`histUnfolded`) and folded away by it (`histFolding`); the message being read stays still |

**A closing surface stays drawn until it has left.** Each surface is gated by a state key, and when the key empties the
prototype keeps rendering its last value for 240 ms — the slowest kind's time, a departure taking exactly as long as its arrival — with `data-phase="out"` — `motionHold` in its component, over the
keys in `MOTION_HELD` — so a close animates exactly as an open does and the content does not empty under the
departure. A surface leaving takes no pointer events. Two surfaces can therefore overlap for that window: a context menu
leaving while the dialog it opened arrives, the *More* sheet going down while Settings comes up.

**Push and back are the shared horizontal axis rather than a slide across.** The pane being left is gone at once and
the arriving one fades through from 48 px, so the direction reads without anything sliding over an empty frame. They
play only for 600 ms after the pane actually moved (`paneMotion`), so a pane that mounts for any other reason — a space
switch, the first render, a wider composition — does not slide. Two panes side by side never animate.

**Arrivals fill backwards only.** Once one ends nothing of it is left on the element, so an animated root neither forms
a stacking context nor becomes the containing block of a fixed child.

### What back does

The browser's back — and on Android the system back gesture, which reaches the page as the same thing — steps back
**one thing at a time** and leaves the space only once nothing stands in it. That holds at every width: a tablet in the
desktop composition steps back the same way a phone does, and what differs is only what counts as standing.

| Press | What it takes away |
|---|---|
| A surface stands over the screen | That surface: the drawer, a sheet, the composer, a dialog, Settings, a menu, the notification centre |
| A view stands where another was | That view, as its own *Back* does: the thread in front of the list (`singlePane`), a person in front of its list (`isMobile`) |
| Nothing stands in the space | The space, back to the one before it — closing every surface still open, as switching space does |

Each thing standing records one history entry at the same address, and a press consumes one; a surface closed by its
own control gives its entry back, so the next press is never spent on something already gone. Two panes side by side
add no step, because opening a thread there replaced nothing. `backSteps` in the component is the list, and
`backSync` keeps the history in step with it.

A press closes a surface the way its own control would. The composer with anything written in it asks first
(`closeAsk`) and stays until that question is answered, and a surface standing in a tab closes its tab.

## Discover — `isDiscover`

The run is a phase machine (`st.phase`), and the blocks arrive one at a time (`ready.<block>`).

**The field carries no scope control.** Discover always asks about all of the reader's mail, and
nothing the Mail space has open, picked out, or highlighted narrows it. A question about one
correspondence is asked in that correspondence's own field, and a narrower follow-up is the Agent's.
The line under the field stands only once a run is over (`runFinished`), and says what the run read, in plain counts (*41 messages read*, *6 files found*). It names no run identifier, no plan, no model, and no spend, and no card traces how the answer was reached — a person asking about their mail is told what was read, not how the pipeline ran. An evidence entry likewise shows its source, the passage, and how current it is, and no relevance score.

| State | Flag | Reveal |
|---|---|---|
| Nothing asked yet | `showIdle` | `st.phase === "idle"` — the state the prototype opens in |
| Running | `running` | `st.phase === "running"`, entered by submitting a question |
| A run has happened | `hasRun` | `st.phase !== "idle"`; gates the run's own chrome |
| A run is over | `runFinished` | `hasRun && st.phase === "done"`; gates the run line under the field |
| Answer block | `answerReady` | `ready.answer`, set as the stream reaches it |
| Timeline block | `timelineReady` | `ready.timeline` |
| Fact table | `tableReady` | `ready.table` |
| Attachment gallery | `galleryReady` | `ready.gallery` |
| People block | `peopleReady` | `ready.people` |
| Proposed action | `actionReady` | `ready.action` |
| What the sources do **not** settle | `gapNote` | the chosen plan carries a `gap` string — `PLANS.contract` and `PLANS.people` do, `PLANS.files` does not |
| Evidence inspector open | `inspectorOpen` | a citation is pressed |
| The confidence explained | `confidenceTip` | the answer's confidence chip — tinted by its band (`confidenceChipStyle`: high `--ok-soft`, moderate `--rail`, low `--warn-soft`) — hovered, focused or tapped; see `[data-tip]` under § *What every artboard shares* |
| Evidence inspector closed | `inspectorClosed` | nothing selected — the resting state |

Which blocks a run produces is the plan, and there are three (`PLANS`): `contract`
(answer + timeline + table + action), `files` (answer + gallery + action), `people`
(answer + people + action). **No single run draws every block**, so a screenshot of one plan is not
the block set to build against; `ai-blocks.dc.html` is where the block types stand
together. **Discover draws them read-only.** The same catalogue with controls on it is the Agent's,
and the blocks screen carries both halves — see *Agent* below. Discover's blocks wear the Agent's chrome
(`blk`, built from the same `BLK` as `bkView`): the card, the icon-tile header, the timeline's rail of dots, and
for the proposed action a pending card with its *needs confirmation* chip in the header and *Dismiss* on an
`--accent-soft` footer tray. The Answer keeps its prominence through an accent icon tile rather
than the 3 px accent edge it used to carry.

## Mail — `isMail`

### The panes

| State | Flag | Reveal |
|---|---|---|
| Thread list shown | `showThreadList` | `!singlePane \|\| mailPane === "list"` |
| Content pane shown | `showContentPane` | `!singlePane`, or the thread, composer, or document is open |
| Thread shown | `showThreadPane` | as above, and nothing else has taken the pane — the Outbox included (`outboxOn`) |
| Back to the list | `showMailBack` | `singlePane && mailPane === "thread"` — phone and fold only |
| Toolbar | `showMailToolbar` | `!selectionOn && !singlePane` |
| Primary actions in the bar | `tbPrimaryShown` | the toolbar has room for them |
| Resizer between panes | `showResizer` | `!singlePane && !touch && !listStrip` — a mouse drags the list between 232 and 560 px, double-click returns it to its band's width (`st.listW` back to `null`) |
| The list as a strip, below 1020 | `listStrip` | `!singlePane && narrow && !emptyMail && !st.listWide && !selectionOn && !stateBarOff && !outboxOn` — while a thread is open the list folds to a 76 px `--sub` column of senders: per thread a 36 px avatar in a 52 px target, the open one on `--accent-soft` inside an `--accent-line` ring, an unread one with a 10 px `--accent` dot; picking one opens its thread. The strip has no head of its own: the toolbar's list control widens it |
| An unread row | `t.isUnread` / `fromStyle` / `subjStyle` | the sender and the subject in weight 700 and `--text`, a read row's in 400 and `--text2` — weight and ink together, as every mail client draws it, because 600 against 400 in one colour read as the same row at a glance; the row carries no dot. The strip below 1020 keeps its dot, having no words to set in weight |
| The list widened, below 1020 | `st.listWide` | *Show the list* — the list comes back at 284 px, the thread narrows beside it, and the toolbar's list control turns to `left_panel_close`, named *Narrow the list*; picking a thread folds it to the strip again unless the list is kept open |
| The list kept open, below 1020 | `st.listKept` / `keepList` | holding the toolbar's list control while the list is widened — a held finger, a right-click, or the context-menu key — keeps the list at 284 px as other threads are opened. The control is then drawn on `--accent-soft` in `--accent-strong` inside an `--accent-line` ring, and its title reads *The list stays open as you open other messages. Hold to let it fold again, or tap to narrow it.*; before it, *Narrow the list. Hold to keep it open as you open other messages.* Holding again lets it fold at the next thread; a tap narrows it and lets go of the hold. The hold is the session's and is not stored |
| Side list expanded | `sideExpanded` | not collapsed; collapsed is 58 px, expanded is 210 px (240 under `wide`) and draggable to 420. It starts collapsed below 1180 (`compact`) and expanded above |
| Side list show/hide | `sideToggleShown` / `toggleSide` | `!drawer` on Mail — in the mail toolbar's lead, after the list control where there is one, a 32 px icon button (44 px on touch) reading `left_panel_close` while the list is open and `left_panel_open` while it is collapsed, titled *Hide folders and filters* and *Show folders and filters*, and set apart from *New message* by a 1 px rule with 6 px before it and 8 px after, so a press meant for one never lands on the other. It is never inside the list, so a long tree cannot scroll it away |
| Side list scrolls | `sideScrollStyle` | the AI filters and every account scroll together inside the panel once they are taller than it, docked or as a drawer; the panel itself does not move |
| AI filters | `aiFilters` | the first group in the side list, above every account, under an `auto_awesome` *AI filters* header — rows the same size as folder rows; a collapsed side list draws the glyph alone |
| AI filters collapsed | `aiFiltersOpen` / `toggleAiFilters` | the header, exactly as an account header collapses its folders — `expand_more` against `chevron_right`, titled *Collapse AI filters* and *Show AI filters*; open by default (`st.aiFiltersOpen !== false`) |
| Side list resizer | `showSideResizer` | `!drawer && !sideCollapsed && !touch` — drag widens the folder pane between 210 and 420 px, double-click returns it to 210 |
| Side list as a drawer | `showSide` / `sideScrim` | `drawer && drawerOpen` — its head reads *Folders and filters* beside a close control (`sideDrawerHead`) |
| View controls in the toolbar | `viewLeadShown` / `listToggleShown` / `drawerOpenerShown` | Mail with the toolbar standing. The toolbar leads with the list control while the list is a strip or widened below 1020 — under a mouse as well as a finger, the strip having no head of its own — (`left_panel_open` *Show the list* on the strip, `left_panel_close` *Narrow the list* widened), then the folder pane's own control: the side list show/hide where the pane stands beside the list, *Folders and filters* (`menu`) where it is a drawer (`drawer`, the tablet and the fold). Each is the side list toggle's size, and the same 1 px rule (6 px before, 8 px after) sets them apart from *New message* |
| Folder button | `showFolderBtn` | `drawer && (singlePane \|\| selectionOn)` — the list's head carries it only where no toolbar leads with it: the phone, and a selection, whose bar replaces the toolbar |
| Account menu on a mailbox | `ctxMenu` through `g.ctxOpen` | right-click a mailbox header, or hold it for 460 ms — *New folder* and *Mark all as read*; **All accounts carries none** |
| Menu on a folder row | `folderMenu` | right-click a folder, or hold it — *Mark all as read* always, *New folder inside* while the path is under three segments, and *Edit folder* and *Delete folder* only on a folder the user made; **All accounts carries none** |
| A folder the user made | `st.extraFolders[<mailbox>]` | the two menus above, or *New folder here* in the move sheet; it is drawn below the standard set. **The side list draws no *New folder* row** — an account is given a folder from its own menu, so the tree holds folders and nothing else |

An account draws six folders in the same order — Inbox, Sent, Drafts, Archive, Spam, Trash
(`STD_FOLDERS`) — and a count only where the mock data gives one. **The unified view
draws three of them.** `STD_FOLDERS` takes a `unified` flag, and the last three carry `perAccount`:
*All accounts* is Inbox, Sent and Drafts alone, because Archive, Spam and Trash are always opened in
the account they live in rather than merged across accounts. Anything under either set is a folder
the user made. An account's Sent row may carry one more thing, the Outbox mark — § *The Outbox* below.

### The Outbox

Each account has one, and it is MailFathom's own rather than a folder on the mail server — [ADR
0040](../docs/decisions/0040-each-account-outbox-as-a-read-only-list-beside-its-folders.md) is the
decision this section draws: every send from that account that has not left it yet, including one the
server refused, until it is sent, withdrawn, or somebody dismisses it. It holds no stored mail, so it
has **no row in the tree**. It is a mark on that account's Sent row, and the unified *All accounts*
group carries none, because an Outbox belongs to one account. The sample is `outbox` in
`data/prototype/workspace.js`: five entries in the work account, one of every state but *scheduled*,
and two in the personal one, *sending* and *scheduled*.

| State | Flag | Reveal |
|---|---|---|
| The mark on Sent | `mb.outboxMark` / `mb.ob` | the account's Outbox holds anything (`outboxMarkFor`): a pill at the row's end — the `outbox` glyph and how many have not left — in accent, and in `--err-soft` with `--err-text` once anything in it was refused. The count is the ADR's two numbers added up; its name, the `title` and `aria-label`, says both — *Outbox - 4 waiting to leave, 1 refused* — and the same sentence joins the row's own detail under its menu's header, so a finger reads it without the hover. 22 px tall under a mouse; under a finger a 36 px pill inside a 44 × 44 target, the target taken from the row's padding so the row keeps its height. Filled while its Outbox is open, and marked `aria-current` rather than pressed, since a second press does not close it. Gone the moment nothing waits |
| The mark on a folded side list | `mb.outboxStrip` | `sideCollapsed`: the strip has no room at the end of a row, so the mark stands as its own `outbox` glyph under Sent, with its count as a badge |
| The Outbox open | `outboxOn` | the mark pressed (`openOutbox`). It takes the list's place (`outboxListShown`) and the reading pane's (`outboxPaneShown`); neither the strip below 1020 (`listStrip`) nor the thread is drawn while it is open. Picking a folder, or a thread opened from anywhere else (`leaveOutbox`), closes it |
| Its head | `outboxHeadStyle` | *Outbox* as the title, the account under it; on the phone the title is 20 px semibold in the 56 px bar, the folders button before it, as everywhere in Mail |
| Read-only, said | `outboxReadOnlyStyle` | one `lock` line under the head, on `--rail`, at every composition: *Read-only. What has not left yet waits here - nothing in it can be flagged, moved or deleted.* The preview ends on the same lock line. Missing actions read as the folder's nature, not a fault |
| A row | `outboxRows` | newest first: *To* and the address of the first recipient in `To` — else `Cc`, else `Bcc` — with *+N* for the rest, the time it was handed over, the subject, and its state on a line of its own (`OB_STAGE[…].line`), with how many attempts it has taken wherever one was made. *Outcome unknown* and *Refused* are at weight 600; a sending row's glyph pulses. **No mail act on a row**: no checkbox, no selection, no AI summary, no hover actions, no drag, no swipe on the phone, no hold menu, and no row is a drop target. Pressing one opens it; on one pane it pushes the preview as a thread would |
| The toolbar | `toolbarActions` | keeps the folder toggle and *New message* and drops every mail act — *Reply*, *Reply all*, *Forward*, *Archive*, *Delete*, *Flag*, *Unread*, *Move* and the panels toggle |
| Nothing open | `outboxNothingShown` | two panes and nothing picked: the *Nothing open* layout with the `outbox` glyph and *Pick a message to read it. Outbox is read-only: what is here can be read, and nothing can be replied to, moved or deleted.* — or, once the Outbox is empty, *Outbox is empty, so there is nothing to read here. What went out is in Sent.* |
| The preview | `outboxEntryShown` | the subject (with *Back* on one pane), the state box with its decisions, then `outboxFacts` — *From* (the address it is sent as), every recipient by address under its own header — the address being all a send says of who it is for — with *Not taken* in `--err-text` beside one the server refused, since it gives no reason per recipient, *Handed*, *Leaves* where a time was named, *Tried* where an attempt was made — then each attachment by name, type and size (`outboxAttachments`), which opens nothing, then the message itself. Nothing in it is a mail act |
| The preview not read | — | the preview could not be read, or the machine is offline: the reading pane's *Nothing open* layout (`m_pane`), with *Back* above it on one pane — a `warning` glyph, *Not read* and the reason, with *Try again* where the server could not be reached; or a `cloud_off` glyph, *Offline*, and that it opens on its own once the network is back |
| Waiting | `outboxCanCancel` | a neutral box with `schedule`: *Waiting to send* — *Handed over at 10:44 and not sent yet. It goes out as soon as the mail server takes it.* — and *Cancel sending*, which withdraws it (`cancelOutboxEntry`): the row goes and a success toast says *Nothing was sent to …* |
| Scheduled | `outboxCanCancel` | a neutral box with `schedule_send`: *Sends Mon 08:00* — written to leave at a time somebody named, and *Cancel sending* until then |
| Trying again | `outboxCanCancel` | a `--warn` box with `update`: *Trying again at 10:55* — the server's failure in words, that MailFathom tries again on its own, how many attempts so far, and that nothing has reached anybody — and *Cancel sending* |
| Sending | — | an accent box with a pulsing `send` glyph: *Sending now* — an attempt holds it, so it can no longer be cancelled. No action. In the prototype it leaves six seconds after the Outbox is opened (`outboxLeaveSoon`), with a *Message sent* toast |
| Outcome unknown | `outboxCanDecide` | a `--warn` box with `help`: *Outcome unknown* — it went out and no answer came back, so nobody knows whether it arrived, and sending it again may deliver it twice — with *Send again* and *Dismiss* |
| Refused | `outboxCanDecide` / `outboxCanEdit` | the `--err` box with `error`: *Not sent - the server refused it*, then the server's reason in words — never a status code — with *Send again*, which turns it into *Sending* (`sendOutboxEntryAgain`), *Edit* (`edit` glyph, the quiet button style), and *Dismiss* |
| Taken back to edit | `editOutboxEntry` | *Edit* on a refused entry — **refused only**, never *Outcome unknown*, which may have arrived. The decision under way first, then the entry leaves the Outbox and the composer opens on the message as a draft (`openDraft`: recipients by address with *Cc* open where it had any, subject, words, attachments under *Files*), and a neutral toast titled *Back in Drafts* says *“…” left the Outbox and nothing was sent. Send it when it is ready.* Reached on the work account's refused entry, or by the refused-send notification's *Edit* |
| A decision under way | `outboxDecidingShown` / `outboxHeld` | *Cancel sending*, *Send again* or *Edit* pressed and the server has not answered yet (`decideOutboxEntry`, 900 ms in the prototype): the entry's controls stay where they are, focus included, at 60 % opacity and `aria-disabled`, a second press does nothing, and a muted status line under them says what is under way — *Cancelling sending…*, *Sending it again…* or *Taking it back to edit…*. The answer then lands as its own row says |
| Dismissed | `dismissOutboxEntry` | *Dismiss* on a refused or unknown entry takes it out of the Outbox and changes nothing else — a refusal may follow a partial acceptance, so nothing says it was not sent. The row goes, and a neutral toast titled *Taken out of Outbox* says *“…” is no longer listed here. Nothing else about the send changes.* with *Undo* for its eight seconds, because the person cannot offer a dismissed send again |
| Emptied while open | `outboxEmpty` | the last entry leaves while the Outbox is open: the list says *Outbox is empty* — *Nothing is waiting to leave Nordwind · work any more. What went out is in Sent.* — with *Go to Sent* (`outboxGoSent`), and the mark on Sent is gone. Two panes show *Nothing open* beside it, with the sentence for an empty Outbox. Reached in the prototype by cancelling the two that can be cancelled (waiting, trying again), dismissing — or sending again, or for the refused one editing — the other two (outcome unknown, refused), and leaving the one sending to go; the personal account's empties by cancelling the scheduled one and waiting |
| From the send | `confirmSend` / `doneAction` | a message sent from the composer is handed to the account's Outbox as a *waiting* entry, so the *Message sent* toast carries *Show in Outbox* — where the session may read mail, since that is what an Outbox is read under: pressed, it opens Mail on that account's Outbox with the entry picked, which is where withdrawing it is offered for as long as it can be |

*Cancel sending*, *Send again*, *Edit* and *Dismiss* are the Outbox's own decisions about a send rather than
mailbox acts, offered on exactly the states ADR 0040 offers each one on — *Edit* on a refusal alone.

### Folders the user made

Added on 2026-09-10. A folder is no longer a name in a flat
list: it nests, it is placed somewhere in the account's own tree, and it can be edited and deleted.

| State | Flag | Reveal |
|---|---|---|
| The folder tree | `st.folderOpen[<mailbox·path>]` | a folder with children draws a twisty; each level is indented 15 px, and **three path segments is the ceiling** (`MAX_DEPTH`) |
| Children shown | `mb.hasKids` / `mb.twisty` | `expand_more` against `chevron_right`, titled *Collapse subfolders* and *Show subfolders*; a collapsed side list draws depth 0 only |
| New folder dialog | `newFolderOpen` | *New folder* in an account's menu, *New folder inside* on a folder, or *New folder here* in the move sheet |
| Editing one | the same dialog in `mode: "edit"` | *Edit folder* — the title reads *Edit folder* and the button *Save changes* |
| Its two fields | `nfName` / `nfParent` | *Folder name*, and *Inside* as a select rather than a text field |
| Where it may sit | `nfLocations`, from `folderLocations` | the select's options: *Top level of the account* first, then the account's folders drawn as `Parent / Child`. Editing one drops the folder itself, everything under it, and any parent too shallow to hold its subtree |
| Nothing typed yet | `nfCreateStyle` / `nfCreateHover` | the create button is drawn flat and refuses the pointer while the name is empty |
| The name is taken | the `warning` notification *Pick another name* | saving an edit onto a name already used in that place — *There is already a folder called “<name>” inside …* |
| Delete confirmation | `delFolderOpen` / `delFolderText` | *Delete folder* — four outcomes computed from the state rather than one sentence |

**The design's own statement about what a folder is: the user places it, MailFathom puts it on the
server.** The hint under the select reads *MailFathom creates the folder on this account's mail
server and picks where it sits there — you only choose where it shows up in your folder list.*, and
the source says why in as many words — accounts are administrator-managed and the server path is not
the user's business. So nothing about a folder the user made shows a remote path or takes one — the
account editor's own folder mapping is the administrator's setting rather than theirs — and creating,
renaming and deleting one each post a success toast naming the mailbox and the place
(`locLabel` —
*inside Parent / Child*, or *top level*), never a path. `MAX_DEPTH` still caps the tree at three
segments, and `folderLocations` enforces it against the subtree being moved rather than against the
folder alone.

The delete confirmation states what goes with the folder, in three parts. *The folder* — or *The
folder and the folder nested inside it*, or *…and the N folders nested inside it* — *will be deleted
in `<mailbox>` and on your mail server.* Then the mail: where the folder holds stored conversations,
they go with it, permanently on both servers with nothing left to restore while the account's hard
delete is on, or to Trash and recoverable while it is off; where it holds none, *It holds no mail,
and deleting it cannot be undone.*

### The per-message AI reading

| State | Flag | Reveal |
|---|---|---|
| AI summary | `aiSumOpen` | the `auto_awesome` control on a message row, or *AI summary* at the top of that row's context menu |
| Its readings | `aiSumCards` | **three at most**, each a small heading, the claim, the reason in a muted line, and the fragment of the message it rests on — no card around a reading, a hairline between readings, and the footer saying once that AI wrote them (`aiSumAuthor`, *Written by AI*) rather than on every reading, naming no model or agent |
| Which headings exist | — | *What is settled*, *What is still open*, *What is the commitment*, *What the deadline is*, *What changed between versions* for a `Version difference` state, and *What this is about* where the list's own reading fills a card out. The cards follow the thread's state list and stop at three, so a fourth state never reaches the dialog — which is where both seeded `Version difference` states sit |
| Jump to the source | `ac.showSrc` | only where the thread holds more than one message — *Open message N in the thread* |

The dialog is *What MailFathom read*, with the message's subject under the title. **Wide** (900 px
and up) it opens centred at 920 px (760 below 1020), at most `min(86dvh, 860px)` tall, and sets each
reading beside its passage — the claim at 19 px on the left, the quote on `--sub` behind a 2 px
`--accent-line` edge on the right, *Open message N in the thread* under the quote. **On the phone and
the fold** it takes the whole screen and stacks each reading, the claim at 16 px and the passage held
to four lines. Its footer states the contract, *Read from this message alone - every reading quotes
the passage it rests on.*, shortened to *Quoted from this message alone* on the small compositions.

### The thread

| State | Flag | Reveal |
|---|---|---|
| Thread head | `showThreadHead` | `!st.stateBarOff \|\| singlePane` |
| The subject on one pane | `threadHeadRowStyle` / `subjectStyle` | `singlePane` — the back button and the three actions share the first row, and the subject takes a row of its own under them at 18 px, two lines before it ends in an ellipsis; scrolled (`thCollapsed`) it folds to one line at 16 px |
| The head's actions under a finger | `threadActions` | `!singlePane && touch` draws none — the toolbar above carries *Reply*, *Forward* and *Flag*, so the head is the subject alone |
| State bar beside the thread | `showThreadStateBar` | `(!singlePane \|\| !thCollapsed) && !stateCards && !stateBarOff` — `stateCards` is `!isMobile && paneCompact`, a pane narrower than 760 px off the phone |
| State cards | `showStateCards` | `!singlePane && !stateStale` |
| State inside the body | `stateInBody` | `stateCards && !stateBarOff && !stateStale` — a pane under 760 px answers the same content at the top of the thread; under 560 px (`paneNarrow`) the cards stand in one column |
| The AI line on one pane | `mobileStateLine` | `singlePane && !stateStale` — the state bar's one-line reading, on the phone |
| Reading held back | `stateStale` / `stateFresh` | the thread carries `stateStale`: a message joined or left the conversation after its state was derived. The state cards, the in-body chips, the one-pane AI line and the thread sheet's state list stop drawing, and each place says instead *This conversation has changed since where it stands was last derived, so that reading is held back until it is derived again.* The prototype reaches it through *Refresh*, whose second step marks the thread it changes |
| Four states or more | `stateRowStyle` / `stateBodyRowStyle` / `sc.valueStyle` | `thread.state.length >= 4` — the state cards beside the thread and the chips inside the body stand as a two-column grid, and each chip's value wraps (`text-wrap:pretty`, line height 1.35) rather than ending in an ellipsis on one line. The phone keeps its single column. `contoso` and `piotr` reach it, each carrying a fourth state, `Version difference`, that says what a later message corrected in an earlier one |
| Held back, in a narrow pane | `staleInBody` | `stateCards && !stateBarOff && stateStale` — the same sentence as a note at the top of the thread body, where `stateInBody` would have drawn the chips |
| Panels hidden | `st.stateBarOff` | the *fullscreen* toolbar control; nothing else sets it |
| Arrived from a citation | `showCitationTag` / `cameFromResult` | reached from a Discover citation, and only for the `contoso` thread |
| Answer drawn in mail | `mailAnswer` | `st.mailAnswer` and the `contoso` thread — reached by the thread sheet's *Summarise where this stands* and *What the other side expects from me*; a drafted reply never lands here, it opens in the composer |
| Thread actions sheet | `threadSheetOpen` | the sheet control, phone-shaped |
| Remote content removed | `m.remoteBlocked` | a message that is not the user's own and carries remote references, until they are loaded — never one marked spam, whose remote content stays blocked with no card (§ *Spam*, *Read-only spam*). A card at the top of the body says *This message asked to fetch content from another server. That was removed, so opening it told the sender nothing.*, then *Removed references: N*, then *Loading them tells the sender that this message was opened. This choice applies to this message only and is not remembered anywhere.*, over one accent button, *Load images from the sender*, 44 px tall on touch. The count comes from `m.remoteRefs` where a message sets one, and none does; otherwise a hash of sender and time gives about a third of the messages none and the rest between 3 and 18. *Images loaded automatically* in the settings does not reach it |
| Remote content loaded | `m.remoteLoaded` | pressing that button — the card gives way to one muted line with an `image` glyph, *Images from the sender are loaded for this message.* The choice is kept per message in `st.remoteLoaded` for the session and nowhere else |
| A change the mailbox did not take | `m.changeShown` | a move, archive, delete, flag or read mark MailFathom stopped trying (`CHANGE_NOTICES` in `data/prototype/workspace.js`, fate `exhausted`), on the message it was asked for — the thread's newest — at the top of its body, above the remote-content card: the Outbox refused box's shape, `--err` with `sync_problem`, *Not moved - your mailbox did not take this change* over *The mail server would not take it, so this message is shown where the server holds it.*, then *Ask again* (accent) and *Let it go* (quiet), 44 px tall under a finger. A move reads *Move again…* instead and asks for the folder once more, because the change record names the act and not where it was going. The title names the act: *Not moved*, *Not archived*, *Not deleted*, *Not marked read*, *Not marked unread*, *Not flagged*, *Not unflagged* (`CHANGE_ACT`). Nothing local changed on a mirrored account, so the message simply stands where the server holds it. Reached by opening the change notification, which leads to `travel1` |
| A change whose outcome is unknown | `m.changeShown` (fate `unknown`) | the same box in `--warn` with `help`: *Flagging may not have happened* — *Moving*, *Archiving*, *Deleting*, *Marking read*, *Marking unread*, *Flagging*, *Unflagging* — over *The mail server never answered, so nobody knows whether it took the change. The message is shown as the server last reported it.*, with the same two buttons. Reached by opening the `devops1` thread, *Nightly report - build #2418* |
| The change decided | `changeDecided` / `decideChange` | *Ask again* or *Let it go*, on the message or on its notification row: the notice goes, the durable row is marked read and loses its pills, so one failure is never shown as two. *Ask again* raises a neutral toast titled *Asked again*, *MailFathom is telling your mailbox again.*; *Let it go* does nothing else |
| Reveal the earlier messages | `showEarlier` in the thread head | *Show N earlier messages* / *Hide earlier messages*, in the pane already open |
| Expand every message | `showExpandAll` | **constant `false`** — the source hands the screen no per-message collapse at all, so a revealed message is drawn in full |

### Waiting

Added to the prototype after this inventory was first written, and read against the new source on
2026-09-08: every wait in mail draws a skeleton rather than leaving the screen unchanged.

| State | Flag | What it draws |
|---|---|---|
| The folder's list is arriving | `listLoading` / `listReady` | `listSkelRows` — eight shimmering rows at the desktop, seven at the phone, each an avatar circle and two lines |
| A message is arriving | `msgLoading` | `msgSkel*` over the content pane: subject, meta, head with an avatar, then body lines |
| The full HTML surface is arriving | `htmlLoading` / `htmlDoc` | the HTML view's own skeleton before the document |
| An attachment is opening | `docLoading` / `docZoom` | the document surface's skeleton, and the zoom the surface opens at |

### Selection, search and filters

| State | Flag | Reveal |
|---|---|---|
| Selection running | `selectionOn` | one or more rows selected; it *replaces* the toolbar |
| Selection bar | `selBarShown` | the same, and it appears on five screens, not only mail — Mail, Tasks, Calendar, People, and the Agent's conversation list |
| Search idle | `listSearchIdle` | `!st.listSearchOn` |
| Search active | `listSearchActive` | the search control |
| Filters open | `filtersOpen` | the filter control |
| Filters in force | `filtersActive` | `filterCount > 0` over unread, flagged, attachment, date range and sort |
| Undo after archiving | `undoShown` | `isMobile` and the last archived thread is still archived — **phone only** |
| A thread being dragged | `st.dragIds` (`dragThreadsStart`) | a row is `draggable` and starts a drag; the dragged rows go to `opacity:0.45` while it lasts, and a drag started on a row inside a selection of more than one carries **the whole selection** rather than that row |
| A folder under the drag | `st.dragOverKey` (`dragOverFolder`) | the pointer over a folder row in the side list while `dragIds` is set — that row draws the accent-soft background, the accent text and an `inset 0 0 0 2px var(--accent)` ring, which is the only affordance saying a drop lands here |

Dropping is a move and nothing new: `dropOnFolder` hands the ids to `moveThreads`,
which now takes them as an argument rather than reading `moveFor`, so a drag and the
move dialog end in the same act, the same row animation and the same toast. The drop targets are the
folder rows alone — a mailbox header carries none of the four handlers. `draggable` and the two drag
handlers sit on every row at every width, beside the long-press and swipe the touch compositions
already use, so the design states no separate touch gesture for a move and leaves the move dialog as
what a finger reaches.

### Spam

A message judged spam carries its verdict (`spamOf`): who judged it — a model, the mail server, or the person — and,
where one was named, its **spam subcategory** (`SPAM_KINDS`): *Phishing*, *Business email compromise* and *Malware
lure*, the three dangerous ones, then *Scam*, *Extortion* and *Marketing*. The mail server names none. The person's
own word, spam with a subcategory or not spam, outranks what the message arrived with. Nothing here is named
*category* on its own: that word is kept for a later feature that sorts all mail by topic. The sample is the eight
`spam…` threads at the end of `data/prototype/threads.js` — four in the work account's Spam folder (`box: "spam"`),
phishing, business email compromise, malware lure and extortion; two in the personal account's, a scam and the
server's with no kind; and two folded into every other list, a phishing message and a marketing one.

Whether *Spam* and *Not spam* also move a message is its account's own setting (`movesSpam` in
`data/prototype/workspace.js`, read through `accountOf`), the same switch that files automated spam, and it is off by
default. The work account has it on and the personal account (`acc: "acc2"` on a thread) has it off, so both are
reachable. A selection spanning accounts follows each message's own account. A person's own move into Spam is *Spam*
whatever the setting says, and a move out of Spam never lifts the mark.

| State | Flag | Reveal |
|---|---|---|
| The Spam folder's list | `inSpamBox` / `listedThreads` | an account's Spam folder picked: the list holds what sits in that account's Spam (`sitsInSpam`, `accountOf`) and nothing else, and every other list leaves it out. *All accounts* has no Spam folder, so each account's spam is listed in its own. Moving a row into or out of Spam takes it out of the list it stood in, with the archive row's exit; any other move keeps the row and names its folder, as before |
| A row's spam subcategory | `t.spamBadge` / `t.spamBadgeDanger` | a spam row in the Spam folder whose verdict names a kind: a square label on the row's third line, where another row draws its *AI* tag, and in the same form — 12 px, a 4 px radius, a hairline border, no pill. A dangerous kind takes the error tone (`--err-text` on `--err-soft` inside an `--err` border) **and** a `warning` glyph, so the danger is in the glyph and the word and never in the colour alone; a quiet kind takes `--muted` inside a `--border2` border. A spam message with no kind carries no label, because the folder already says spam. The row keeps everything else, its unread weight included |
| A folded row | `t.folded` | a spam row in any list but the Spam folder — every other folder, *All accounts*, and search results (`mail-search.html`): one line in place of the row, a `report` glyph in `--faint` where the avatar stood, then the sender and the subject at 13 px in `--muted`, both ending in an ellipsis, and the time. The AI line, the unread weight and the *AI summary* control go. 34 px tall under a mouse and 44 px under a finger. No badge: the change of shape is the cue that does not rest on colour, the verdict is in the row's accessible name (`t.spamName`, a visually hidden *Spam: Phishing, judged by a model.*), and its kind joins the hold menu's header (`ctxMenuDetail`, *Payroll Team - Phishing spam*), since a finger has no hover to ask with. Below 1020 the strip draws the same `report` glyph on `--rail` in place of the avatar, with no unread dot. Strikethrough was rejected: it reads as deleted, and it crosses out the sender and subject a person needs to recognise a message judged wrongly |
| No AI reading of spam | `t.aiBtnShown` / `threadSpam` | nothing that reads mail for the person reads spam, so a spam message offers no *AI summary* on its row or in its menu, no *Ask the agent*, and, opened, no thread-state bar or cards and no AI button over the thread |
| The opened message's warning | `m.spamWarn` | a spam message opened from any folder: a box above its body. It always says the message is spam and a security risk, and who judged it — *A model judged this message spam and named it phishing. The kind is the model's judgement and can be wrong.*, *Your mail server judged this message spam.*, or *You marked this message spam and named it malware lure.* A dangerous kind takes the strong form: `--err-soft` inside an `--err` border, a `gpp_bad` glyph, the title in `--err-text` — *Phishing. This message is spam and a security risk.* — then, where the record says so (`asks`, `poses`), what it asks for and whom it is written to look like, never repeating its own claims as fact (*It asks for your mailbox password, and it is written to look as if it came from the Nordwind IT service desk.*), and *Do not give it anything it asks for.* A quiet kind, or none, takes the calm form: `--sub` inside a `--line` border, a `report` glyph in `--muted`, *Scam. This message is spam and may be a security risk.* (no kind: *This message is spam and may be a security risk.*), and the judge. Both end, in `--muted`, on what the mark holds back (`SPAM_READ_ONLY`): *While it is marked spam it is read-only: its images are not loaded, its links and attachments do not open, and it cannot be answered or forwarded.* A message that is not spam shows nothing new |
| Read-only spam | `refuseSpam` | a message marked spam can be read, moved, flagged, archived and deleted, and nothing else. Its remote content stays blocked with no *Load images* card (`m.remoteBlocked` is false for spam). It keeps the simple view even where *Message view* is set to the original, and its *Show the original message* button stays where it is, and a press answers with a `warning` toast — *This message is marked spam* over *Its original does not open. Mark it not spam first.* — instead of the HTML confirmation. The thread's head draws *Not spam* and nothing else for it, and the toolbar *Not spam* in place of *Reply*, *Reply all* and *Forward*, beside its other acts; flagging an open spam message is the toolbar's, the row's menu, the hold menu and the selection strip's, which on a phone are the hold menu and the strip. *Reply* and *Forward* in the row's and the hold menu, and a phone's swipe to reply, answer the same way with *Spam cannot be answered.* or *Spam cannot be forwarded.*; an attachment's open, its download, and *Download all* with *Its attachments do not open.* or *Its attachments are not downloaded.* Nothing composes and nothing opens. Marking it not spam lifts all of it at once |
| A link in spam | `unlinked` | every link in a spam message's body drawn as words in `--text2`, never as an anchor, with where it would have gone beside it in the link note's form — *Keep my current password (a link to nordwind-support.example, not opened)* — so a lookalike address is readable and nothing can be pressed |
| The toggle | `report` / `report_off` | one control in five places: the row's menu and its hold menu (*Spam…*, or *Not spam…* where it will move the message and *Not spam* where it will not, before *Move…*), the selection strip (*Spam* or *Not spam*, its symbol alone where the strip has no room for words), and for the open message the toolbar and the thread's head (`threadActions`). There a message that is not spam carries *Mark spam* after its other acts — in `--err-text`, its `report` glyph and its word carrying it rather than the colour, the glyph alone wherever the head draws glyphs — and a message marked spam carries *Not spam* in the toolbar in place of *Reply*, *Reply all* and *Forward*, and in the head as its one action. It reads *Not spam* only while every message it acts on is spam, and pressed anywhere it is the same act: the chooser, or the confirmation where *Not spam* moves something |
| A mixed selection refused | `toggleSpam` | the toggle pressed on a selection holding spam and other mail: nothing changes, the selection stays, and an `error` toast — the form `toasts.dc.html` draws for a failed operation — says *Nothing was marked* over *The selection mixes spam with other mail. Select only one of the two.* |
| The spam chooser | `spamAskOpen` / `spamPick` | *Spam…* pressed: *Mark 1 message as spam* or *Mark N messages as spam*, then *Pick what kind of spam it is.* (for many, *Pick what kind of spam they are. One kind applies to all N.*) and what happens to the message by its account's setting: *It moves to Spam.*, *It stays in the folder it is in.*, or for a selection on both kinds of account, *The K on an account that moves spam go to Spam, and the rest stay where they are.* Then the six kinds as a radio group (`spamKinds`) — each its name, the dangerous ones in `--err-text` behind a `warning` glyph, and one line saying what it covers — and *Cancel* beside *Mark as spam*. A kind picked draws its row on `--accent-soft` inside an `--accent-line` ring with its radio filled. *Mark as spam* stays `aria-disabled` until a kind is picked and says *Pick a kind of spam first* on a tap. One kind applies to every message; confirming marks them. A message on an account that moves spam leaves the list with the archive row's exit and lands in Spam, and the toast says *It is in Spam now.*; any other row stays, washes as an edit does, and folds, and the toast says *It stays where it is, folded out of the way.* The toast is a success one, *Marked as spam - N threads* over the kind and that line. Closing it — *Cancel*, its *X*, the scrim, back — changes nothing. 440 px off the phone and a full-screen sheet with its grabber on it, every row at least 52 px under a finger |
| A move into Spam | `spamAsk.moved` | a message moved into Spam — dropped on a Spam folder, or *Spam* in the move dialog, which now offers it in both accounts — is marked spam by the person with no kind, and the chooser opens as *Moved to Spam*: *It is in Spam now and marked spam. Pick what kind of spam it is, or close this to leave it with no kind.*, with *No kind* beside *Set the kind*. Confirming it names the kind, and the success toast reads *Kind set - N threads* over the kind. Closing it leaves the message moved and marked spam with no kind; a message already spam is moved and asked nothing. *Undo* on the move toast takes the mark back with the move |
| A move out of Spam | `moveThreads` | moving a message out of the Spam folder does not mark it not spam: it lands still spam, and so folds where it lands. *Not spam* is the one way out of spam |
| The *Not spam* confirmation | `notSpamAskOpen` | *Not spam…* pressed where it moves something — a message sitting in Spam on an account that moves spam: a short question with the `report_off` glyph — *Mark this message not spam?* or *Mark N messages not spam?* — that says what happens: *It is marked not spam and moved to the Inbox.*, *N messages are marked not spam and moved to the Inbox.*, or for a selection where some of it stays, *N messages are marked not spam. K move from Spam to the Inbox, and the rest stay where they are.* *Cancel* changes nothing; the confirm reads *Not spam, move to Inbox* |
| Landed in the Inbox | `confirmNotSpam` / `markNotSpam` | the confirmation accepted: the kind goes, the person's *not spam* is recorded, and every message that sat in Spam on an account that moves spam leaves the Spam list and lands in the Inbox as an ordinary row — unfolded, its unread weight back, the *AI summary* control back, and *Inbox* under it as any moved row names its folder. A message already outside Spam stays where it is and stops folding. A success toast says *Marked not spam - N threads* over *It is in the Inbox now.* — nothing more to do, so it carries no action |
| *Not spam* where nothing moves | `markNotSpam` | *Not spam* pressed where it moves nothing — a message outside Spam, or one in Spam on an account that does not move spam: no confirmation, it applies at once as *Spam* does. The mark goes, a folded row unfolds where it stands with an edit's wash, and a success toast says *Marked not spam - N threads* over *It stays where it is and no longer folds away.* |
| Left in Spam, not spam | `markNotSpam` | a message marked not spam inside the Spam folder of an account that does not move spam: it stays in that Spam list as an ordinary row — no badge, unread weight and *AI summary* back — until the person moves it, and the toast says *It stays in Spam until you move it, because this account does not move spam.* Reached from the personal account's Spam folder, *Not spam* on *Unclaimed funds held in your name* |

### Composing

| State | Flag | Reveal |
|---|---|---|
| Composer open | `composeOpen` | the compose control |
| A draft opened | `composeDraft` / `openDraft` | a refused send taken back to edit, a row under *Out of step with your server*, or the draft notification — the composer opens as *Draft* filled from the draft: *To* by address, *Cc* open where it carries any, the subject, the words in the editor, and its attachments on a *Files* row under the subject (`composeHasAtts`), each a `description` chip with name and size, 36 px tall under a finger |
| A draft out of step with the server | `composeNoServerCopy` | the draft MailFathom keeps but whose copy in the account's Drafts folder on the mail server may be missing (its filing was never answered) or out of date (an older copy could not be removed after an edit): a `--warn-soft` line with a `--warn` rule as the first thing in the composer, above *To*, at every composition — `cloud_off`, *Your mail server's copy of this draft may be missing or out of date, so another mail app may not show what you see here. It is kept in MailFathom.* No action: the next save tries again. Reached by opening the draft notification or a row of the group below |
| The Drafts list's out-of-step group | `draftsLocalShown` / `draftsLocalRows` | a Drafts folder open whose account holds such drafts (`localDrafts` in `data/prototype/workspace.js`; the unified Drafts gathers every account's): at the foot of the list column, under the scrolling rows and outside them, a heading on `--warn-soft` in `--warn-text`, `cloud_off` and *Out of step with your server*, then one row per draft in the Outbox row's shape — *To* and the first address (*+N* for more), the time, the subject, and the `cloud_off` mark at the time's side. It stands below the ordinary drafts, which are unchanged, so its arrival and its going move none of them; it takes at most half the column and scrolls within itself past that. Pressing a row opens the composer on it with the line above. One sample, *Q4 supplier review - agenda* in Nordwind · work |
| Composer expanded | `composeExpanded` | open and not minimised |
| Minimise offered | `composeMinShown` | `!composeInPane && !isMobile` |
| Composer chrome | `composeChromeShown` | the composer is not the active tab |
| The AI button | `replyFabShown` | a thread is open and the panels are not hidden. The thread's one AI entry point, and the head carries none: a floating accent square holding the `auto_awesome` glyph alone, titled *Draft a reply or ask about this thread* (48 px on a 14 px radius, 22 px from the right and 20 from the bottom on two panes, 16 and 16 on one; 56 px on an 18 px radius on the phone); it arrives with `pop`. It hangs over the thread rather than in it, pinned to the pane's bottom-right corner, so it stays there however far the conversation scrolls. The thread keeps its last line clear of it with its bottom padding: 84 px on one pane and 86 px on two panes |
| Its two choices | `replyChooserOpen` / `mo.replyChooser` | pressing the AI button — the glyph turns to `close` (`replyFabIcon`) and two choices unfold above it with `pop`, right-aligned, 8 px apart: *Draft a reply* (`edit_note`), which opens the reply drafter (`replyDockOpen`), and *Ask* (`forum`), which goes to the agent with the thread as its context. Each is a `--panel` pill on a 12 px radius with a `--line` border and the lifted shadow, 40 px tall under a mouse and 48 under a finger (`replyChoiceStyle`). A choice, the button again, a press anywhere else in the pane, back, or leaving the thread folds them |
| The reply drafter | `replyDockOpen` / `mo.replyDock` | *Draft a reply* among the AI button's choices, or *Draft a reply for me* in the phone's thread sheet — a card that arrives with `rise` and hangs over the bottom of the thread in place of the AI button (`composerStyle`: 18 px from the pane's sides and 16 from its bottom on two panes, 10 px on one, on a 16 px radius with the dialog shadow). It holds one field asking what the reply should say (`composerPlaceholder`, *Describe the reply you want to send…*, *Describe the reply…* on one pane) with *Draft a reply* (*Draft* on one pane) at its end — a growing field (see *A drafting field*), whose bar is on a 24 px radius on the phone so one line still reads as a pill and more lines as a rounded box; Enter in it asks for the draft and Shift+Enter starts a new line — and — where the pane is wide — a row under it: *Scope: this thread*, *+ selected passage*, and *Draft stays local until confirmed*. A round close badge hangs off its top-right corner (`replyDockCloseStyle`, 26 px, 32 px and higher under `touch`). Its close, back, leaving the thread or opening another one folds it, and while it stands the thread keeps its last line clear of it with 96 px of bottom padding on one pane and 150 px on two |
| Drafting a reply | `draftReply` | the drafter's *Draft a reply* — the card folds and the composer opens, expanded, as *Reply* to the open conversation (recipients and `Re:` subject as *Reply* fills them) and its editor shows *AI drafting* at once, written from what the card was asked, the draft streaming into the editor itself. No result is drawn in the thread. When it finishes, *AI draft standing* is what remains |
| The reply's conversation | `replyThreadId` | a reply, a reply to all or a forward — the conversation it answers is fixed when it opens, so its recipients and subject stay that conversation's whatever the list is pointed at afterwards |
| Cc/Bcc | `ccOpen` | the Cc control |
| Formatting bar | `fmtBarShown` | `!touch`, or the touch toggle was pressed |
| Formatting toggle | `fmtToggleShown` | `touch` |
| Formatting hint | `fmtHintShown` | `!touch` |
| AI drafting | `aiBusy` / `aiBusyLabel` | an AI action or *Draft a reply*, while it streams — a band right above the editor on `--accent-soft` with an `--accent-line` rule: the `auto_awesome` glyph pulsing, *Generating a reply* for a reply, a reply to all or a forward and *Drafting* for a new message, at 13 px semibold in `--accent-d`, and three 4 px dots rising in turn in place of an ellipsis. Under reduced motion the glyph and the dots stand still. The draft's paragraphs arrive in the editor below it, under the accent edge that marks an AI draft |
| AI draft standing | `aiDraftShown` | the same, once it finishes |
| A drafting field | `growingFieldStyle` / `composerPhStyle` | the composer's AI prompt (`composeAiText`, *Write what this is about…*) and the reply drafter's field (`replyDockText`) — what is typed may be an instruction, the person's own draft of the message, or both. Empty, it is one line and its placeholder stays on that line, cut with an ellipsis. Written in, it wraps rather than scrolling sideways and grows a line at a time up to six lines; past six it stops growing and scrolls vertically. In the composer Enter starts a new line, because the AI acts are the presses there |
| The AI's context | `composeCtxChips` | always, as the last rows of the composer's AI section: *Context* at 12 px semibold, then one chip per message — `--panel` on a 6 px radius with an `--accent-line` border, a `mail` glyph, the subject ending in an ellipsis at 260 px (220 on the phone) and titled with the whole of it, and a `close` titled *Remove from the context*; under `touch` the chip is 36 px tall and its `close` a 32 px box. A reply starts with the conversation it answers; a new message starts with none. Every way the composer closes — its own close, its tab's close, closing every tab — empties the context, its starting point and the reply's conversation, so the next composition starts clean |
| No context yet | `composeCtxEmpty` | the context holds no message — *none yet - click a message in the list to draft from it*. On the phone the composer fills the screen and no list stands beside it, so a new message there takes no context at all and says so: *none - the AI drafts from what you write above*. A reply on the phone still starts with the conversation it answers |
| The list sets the context | `composeCtxPatch` | while the composer is open, a plain click on a row in the list makes that message the context (and opens it, as a click always does), and a selection — a modified click, the row's *Select messages*, any row while a selection runs — makes the whole selection the context. Emptying the selection leaves the context as it stood. The first context a composition takes is its starting point (`composeCtxBase`) |
| Context steady | `composeCtxSteady` | the context is its starting point, or has none yet — the row ends in *the draft stays local until you send it* |
| Context changed | `composeCtxChanged` | the context differs from its starting point as a set: another message clicked, several selected, a chip removed. Two marks, never colour alone: a 22 px disc on `--warn-soft` with a 1 px `--warn` ring holding `priority_high` in `--warn-text` at the end of the AI prompt field, arriving with `pop` and named *The context changed since you started* — and, under the context row with `disclose`, *This message started from “…” - the context has changed since. Check it before the AI drafts.* in `--warn-text`, naming the starting message (and *and N more* for a selection), beside *Restore* (`undo`, 44 px tall under `touch`), which puts the starting context back. Arriving back at the starting set by any route takes both marks away |
| The composer's foot | `composeSendStyle`, `composeAttachStyle`, `composeSaveStyle` | always; under `touch` one 44 px row — *Send* filled, *Attach* bordered and on the phone the `attach_file` symbol alone (`composeAttachLabel` is `!isMobile`), *Save draft* a text action as tall as the row |
| A focused field | `data-compose-fields` | a field with keyboard focus — the composer's rows are one sheet rather than boxes, so the ring every other borderless field takes is not drawn: a row holding the focused field turns its bottom edge `--accent`, and the editor turns a 2 px start edge, transparent until then, `--accent` |
| Send confirmation | `sendAskOpen` | pressing send with something to warn about |
| Warnings in it | `sendHasWarn` | the warning list is not empty — an unaccepted AI draft is one |

### Documents and HTML

| State | Flag | Reveal |
|---|---|---|
| Document open | `docOpen` | an attachment is opened |
| HTML view open | `htmlOpen` | the `code` control on a message head, titled *Show the original message* |
| HTML mode warning | `htmlModeWarn` | `st.msgView === "html"`, chosen in Settings |
| HTML confirmation | `htmlAskOpen` | opening the original from a message — *Show the original message?*, answered by *Stay in simplified* or *Show the original*. A message marked spam answers with a toast instead (§ *Spam*, *Read-only spam*) |

### Tabs

Tabs are a **work mode**, not a layout: `tabsMode = w >= 1180 && workMode === "tabs"`. The
prototype's initial state is `workMode: "classic"`, so **every tab state is invisible until the user
menu's *Tab mode* switch is turned on at a width of at least 1180 px**.

| State | Flag | Reveal |
|---|---|---|
| Tab bar | `tabsBarShown` | tabs mode with at least one tab |
| Nothing open | `noTabsOpen` | tabs mode with none — the tabbed empty state |
| Close one | `closeAskOpen` | closing a tab with unsaved work |
| Close all | `closeAllAsk` | the close-all control |
| The row does not fit | `tabsRowNote` | `w < 1180`, and it says *available on a wider screen* |

## Tasks — `isTasks`

| State | Flag | Reveal |
|---|---|---|
| More menu | `moreOpen` | the overflow control |
| New task | `newTaskOpen` | the new-task control |
| Context menu | `ctxMenuOpen` | long press, or right-click; the header names the row, and a second line under it (`ctxMenuHasDetail`) says what the row only shows a mouse — the sender on a message, the full instant on a notification, the address on an account, the account, path and count on a folder |
| Delete confirmation | `confirmDelOpen` | a delete action |
| Reminder dialog | `remModalOpen` | the reminder control on a task |

## Calendar — `isCal`

| State | Flag | Reveal |
|---|---|---|
| Week | `calIsWeek` | the default view. Under a finger an event in the week's columns carries no link to its thread (`calWeekLinks`) — its detail does — and *Detected in mail* stands under the week rather than beside it (`calSideStyle`), so the tablet's seven columns fit |
| The four views | `calViews` / `calViewsRowStyle` | four loose buttons with the current one on `--accent` at the desktop; under a finger one segmented control, 44 px segments, the current one raised on `--panel` |
| Day | `calIsDay` | the view control |
| Month | `calIsMonth` | the view control |
| Agenda | `calIsAgenda` | the view control |
| A month day's own list | `calMonthDayShown` | `isMobile` — **phone only**, and invisible at every other width |
| That day is empty | `calMonthDayEmpty` | the selected day has no events |
| **No proposals left** | `calNoProposals` | every proposed slot has been dealt with — reachable only by clearing them all |
| Event open | `eventOpen` | an event is pressed |
| Event has a source | `eventHasSrc` | the event carries the mail it came from |
| Event has reminders | `eventRemAny` / `eventRemNone` | the reminder list for that event |
| Parsed from a message | `evParsedShown` | the event was read out of mail |
| New event | `newEventOpen` | the new-event control. Title, a description (`evNotesRef`), *All day*, then *Starts* and *Ends* each a day and a time — the same fields as editing one (`c_edit` on the client-states artboard) — with how long it lasts under them, reminders, and *Generate* filling the fields from one sentence |
| New event, all day | `evAllDay` / `evTimeStyle` | *All day* ticked: the two times fold away and the two days stand side by side, so one event spans several days |

## People — `isPeople`

| State | Flag | Reveal |
|---|---|---|
| List | `showPeopleList` | `!isMobile \|\| peoplePane !== "detail"` |
| Detail | `showPersonDetail` | `!isMobile \|\| peoplePane === "detail"` |
| Back | `showPeopleBack` | `isMobile && peoplePane === "detail"` |
| Avatar | `personHasAvatar` / `personNoAvatar` | whether an avatar file exists for the name — **both states are in the mock data** |
| Collected person | `personCollected` | the person was gathered rather than added; it gates three regions |
| Next step | `personHasNext` | the person's AI reading proposes one |
| **No documents** | `personNoDocs` | that person's `docs` is empty |
| New contact | `newContactOpen` | the new-contact control |
| Edit contact | `personOwn` / `ctEditing` | the *Edit contact* symbol in the header of a person the reader wrote down (never a collected one) — the new-contact dialog titled *Edit contact*, opened on the record, saved with *Save changes* |

## Agent — `isAgent`

| State | Flag | Reveal |
|---|---|---|
| Working | `agentThinking` | a question was sent, or the scripted example is playing — a spinning `progress_activity` glyph beside `agentStatus` |
| Context chip | `agentCtxShown` | the agent was opened from a screen that carries context, **or** the open conversation carries its own `ctx` and it was not cleared |
| Starter chips | `agentShowChips` | fewer than two messages in the conversation |
| The scripted example playing | `st.demoRun` (`playDemo`) | the *Real example* starter chip |
| Cancel offered | `agentCancelActive` | `agentThinking \|\| demoRun`; the control itself is drawn at all times and is disabled otherwise |
| Proposed next steps | `agentNextShown`, from `nextActs` | the agent has stopped and the last reply carries `next` — at most three, under a `Proposed next` label |
| An answer with prose | `m.hasMd`, drawing `m.md` | the bot message carries `text`, `paras`, or both — each is read as Markdown and drawn as one list of blocks, a paragraph's blocks animating in together as it lands. `m.hasText`, `m.hasParas` and `m.paras` are still computed beside it and nothing reads them |
| An answer still being worked | `m.working` | the message carries `working` — it draws an `autorenew` glyph and *The agent is going through your mail…* instead of prose |
| An answer carrying blocks | `m.hasBlocks` | the message carries `blocks` |
| An answer carrying an Answer block | `bk.isAnswer` | a block of type `answer` — seeded in *Summarise the addendum thread* (`c-summary`), a thread-scoped *Summarise*. The same chrome as Discover's answer: the `auto_awesome` accent tile, *Answer*, and *N citations*; then two chips that explain themselves (`[data-tip]`) — the confidence the run reported (`answerConfLabel`, moderate on `--rail`) and *cited* (`citedLabel`); then the prose with its citation numbers at the end of it (`answerCites`), each opening the evidence inspector (`openAgentEv`); then the verdict's sentence under it in `--muted` (`citedTip`). The Agent's answer is never *supported* |
| Back to the conversation | `convReturnShown` | a block's `nav` row was pressed, which is what `goLook` records in `agentReturn`; the pill stands fixed over whatever screen was opened until `backToConv` is pressed |
| History open | `historyOpen` | `!isMobile` by default, and the history control otherwise. On the phone it is a drawer from the start edge over the conversation, so **picking a conversation, *Open conversation* in its menu, or *New conversation* closes it** |
| History close | `historyCloseShown` | `isMobile` |
| History scrim | `historyScrimShown` | `isMobile` with the history open — a press on it closes the drawer as *X* does |
| History header | `historyCount` | *History* and the count of conversations not archived — the header carries no second *New*, the toolbar already has one |
| History search has a value | `historySearchValue` | typing in *Search conversations* — a 34 px field (44 px on the phone) with 12 px under it (16 px on the phone) before the list |
| Nothing matches | `historyEmpty` | a search that matches no conversation — *No conversation matches.* |
| History by day | `convHistoryGroups` | the rows fall under *Today*, *Yesterday* and *Earlier*, newest first, read off each conversation's `when`; an empty group is not drawn and a row under *Yesterday* repeats no date |
| A history row | `h.style` / `h.titleStyle` | the title, the time in tabular figures, and one line of the last message under it; the open conversation carries `--accent-soft` and an accent title — no left edge, no card |
| Row actions | `h.openMenu` | one `more_horiz` control per row opening the same menu as a right-click or a long press — *Select*, *Open*, *Archive* or *Restore*, *Delete* — instead of an archive and a delete glyph on every row |
| **Archive section exists** | `hasArchived` | at least one conversation is archived |
| Archive open | `archiveSectionOpen` | pressing the archive section |
| History resizer | `showHistoryResizer` | `!isMobile` |
| Conversation bar | `convBarShown` | tabs mode with more than one conversation — see the tabs note above |
| Delete a conversation | `deleteConvAskOpen` | *Delete conversation* in a conversation's menu |

### The thread is flat, and the blocks are the only cards in it

The agent's turn carries no card at all — no panel, no border, no shadow, no tail. It is plain text
on the page, `width:100%` to a `max-width` of 900 px on the desktop and the whole width on the phone
(`bubbleStyle`). The reader's own turn stands on the right instead: `rowStyle`
justifies `flex-end` and `colStyle` aligns the column, and the turn is drawn as a lightly
tinted block on `--accent-soft` in `--text` at radius 12, 775 px wide on the desktop and 92 % on the
phone. The line above a reply is `AI · <scope>` in 11 px semibold `--muted` with the `AI` alone in
`--accent-d`, where it used to be a filled accent pill.

**The column centres itself with padding rather than a width cap.** Off the phone, the scrolling
thread (`agentScrollStyle`) and the field under it (`agentInputWrapStyle`) take the same
side padding, `max(26px, calc((100% - 1290px) / 2))` — a rule on the pane's own width, so it reads no breakpoint
of the window's. So the field lines up with the thread at every width, and a wide window keeps both in the middle instead of stretching them. The
thread used to stop at a `max-width` of 1520 px, or 780 px under `compact`, and the framed box around
the field (`agentBoxStyle`) carried the same cap; neither has one now.

**An answer's prose is Markdown.** `m.md` runs the reply's `text` and each of its `paras` through
`mdView`, the same helpers a mail body is drawn with (`mdParse`, `mdBlock`): headings, paragraphs, bold, italic, strikethrough, inline and fenced code, plain,
ordered and task lists, quotes, tables, rules and links. The template draws the blocks
12 px apart, and each text run is either a `span` or, for a link, an `a`. `DEMO_SCRIPT` exercises
most of it: a bold lead-in, a quoted clause, a bulleted list with inline code, a level-two heading
over a numbered list, a task list of the three things waiting on the reader, and italics.

**A link in an answer is judged as a link in mail is** (`isJudged`). A reply's `links` map each target to the
service's judgement, and `mdView` hands it to every run, so a link is followed by *goes to `<host>`* in 13 px
`--muted` and by each warning that applies in 13 px `--warn-text`: *This link does not go where its words say. It
goes to `<host>`.* where its words name another host, and *This address is in none of the messages this answer
cites.* where the cited mail does not carry it (`linkWarnings`). A link the map does not name was judged by nobody,
so it takes the second warning. An answer carries no picture: the service turns one into a link before the client
sees it, so the template has nothing to draw for one. `c-links` shows both links; a mail body passes no map, so its
links draw as before.

A mail body in the simplified view now draws its runs the same way: a paragraph's or a
list item's runs sit side by side with no whitespace between them, so only a run's own text separates
it from the next. The template used to break a line between runs, and that break rendered as a space,
which put a stray gap before the punctuation after a bold, code or link run.

So the separation between one answer and the next is carried by the blocks, and `bkView` says so in
as many words. Every block is drawn from one set of styles, `BLK` in `logic/prototype/component.js`
(mirrored in `logic/ai-blocks.js`): a card at radius 12 with 16 × 18 px padding (14 × 14 on the phone)
on `--panel` behind a `--line` edge, `0 1px 2px var(--sh-1)` and a one-pixel `--inset` highlight; a
header of a 26 px icon tile (one glyph per type), the type's name in 13 px semibold `--text`, its meta
in 12 px `--faint`, and the phase chip pushed to the right; and, where the block has anything to press
or a result to state, a footer tray flush with the card's edges. A pending block keeps the
`--panel` body behind an `--accent-line` edge and moves the accent onto its tile, its chip and its
tray (`--accent-soft`), so the controls sit on the accent rather than the whole card; a settled
block's tray is a `--line2` rule. The draft's paragraphs, and the `textarea` that replaces them,
sit in one `--sub` well at radius 10. The conversation history follows the same rule and is no stack of cards:
rows grouped by day, the open one tinted `--accent-soft` with an accent title — the history rows in the table above say the rest.

### What a run looks like while it runs

**The status line says what the agent is doing right now.** `agentStatus` falls back to
*Going through your mail* and is replaced as the work moves on, inside a `role="status"`
`aria-live="polite"` region beside a spinning `progress_activity` glyph. It stands for the whole run:
content landing is not the end of the work, so the indicator goes only when there is nothing left to
load.

**Content arrives whole, never token by token.** `DEMO_SCRIPT` is a scripted conversation
played back in real time by `playDemo`, `demoNext` and `demoApply`,
one timer at a time. Its step kinds are `u` a reader's turn, `s` a status, `b` a new
reply, `t` text appended to one, `p` a whole paragraph appended, `k` a block appended, and `n` the
proposed next steps. The source states the rule itself: a widget appears only once it is whole, and a
longer answer lands one paragraph at a time. `mfmsgin` animates a turn or a paragraph in
and `mfblkin` a block; `mfdot` beside them is read by the composer's generating band (`aiBusy`).

**Cancel stands beside Send at all times.** `agentCancel` stops the playback or a pending
reply where it stands. Disabled it is `tabIndex` `-1` with `aria-disabled` and the label
*Cancel - nothing is running*; active it is filled `--err` and labelled *Stop what the agent is
doing*. Nothing is rolled back — what arrived stays, and one reply scoped *stopped by you* is written
reading *Stopped. What arrived so far stays - tell me where to pick it up.*.

**The thread pins itself to the bottom while a run is in flight.** `scrollAgentDown`
takes two `requestAnimationFrame`s so the new block has been laid out before `pinAgentBottom`
measures; the hop is smooth under 85 % of the viewport and an instant jump above it,
which is what stops a large block leaving the status line below the fold. `startAgentPin`
re-pins every 140 ms until the run ends, and the pinning stands aside the moment the reader scrolls
more than 40 px up (`onAgentWheel`), taking it back under 40 px (`onAgentScroll`).

**Proposed next steps arrive only once the agent has stopped.** `nextActs` reads the last
reply's own `next`, at most three, and draws nothing while `agentThinking` or `demoRun` is set;
`NEXT_DEFAULT` is what a reply naming none falls back to. Each chip — and each starter
chip beside it — is a `role="button"` carrying an `aria-label` that names the ask, with Enter and
Space bound and a focus outline.

### The answer is blocks, and three of them are actionable

An answer is not prose with buttons under it. `bkView(b, key, isMobile)` draws one block,
and the catalogue is the Discover one: `Answer`, `Timeline`, `Evidence`, `Thread state`, `Fact table`,
`Attachments`, `Draft`, `Event proposal`, `Task proposal`, `Suggested action`. What a conversation
adds is that the last four carry controls, and therefore a phase.

| State | Flag | Reveal |
|---|---|---|
| Pending | `phase === "pending"` | the block's own `phase`, or `bkPhase[key]` once something was pressed — an `--accent-line` edge, an accent icon tile, a `help` chip on `--accent-soft` reading *needs confirmation*, and the type's controls on an `--accent-soft` footer tray (`hasFoot`, `footStyle`) with any `nav` row at its far end |
| Accepted | `accepted` | `acceptBk` — the controls go, a `check_circle` chip on `--ok-soft` reads *done*, and the footer holds one `--ok-text` result line stating what happened and at what time, beside the `nav` row |
| Accepted but not done | `failed` | the block carries `failed` — an `error` chip on `--warn-soft` reading *not done*, the result line in `--warn-text`, and *Try again* as the only control beside it |
| Declined | `isDeclined` | `declineBk` — the card collapses to a dashed outline with a muted tile and a *declined* chip, holding one struck line, *Declined - nothing was done*, and its `nav` rows go with it |
| Loading | `isLoading` | the block's `state` is `loading` — three shimmering skeleton bars |
| Partial | `isPartial` | `state` is `partial` — an `hourglass_top` note on `--sub`, `partialNote`, under the body it did return |
| Empty | `isEmpty` | `state` is `empty` — a per-type icon in a 32 px `--sub` tile beside its sentence, `event_busy`, `task_alt`, `edit_note` or `find_in_page` |
| Error | `isError` | `state` is `error` — a `--warn-soft` icon tile, `errorText`, and a *Try again* control that `retryBk` puts back to `ready` |
| Editing a draft | `editing` | *Edit here* — `editBk` swaps the paragraphs for a `textarea` inside the conversation, and the controls become *Send* and *Stop editing* |
| Cited | `citedLabel` | every `Evidence` block the Agent writes carries a *cited* chip (`format_quote`, `--rail` ground, `--muted` text) above its sources, never *supported*: the Agent names what it read, and nothing checked that it backs the answer. The chip explains itself (`citedTip`, `[data-tip]`) |
| A source pressed | `e.open` | each source's number in an Agent `Evidence` block is a citation — a `role="button"` named *Citation n: source* — and opens the evidence inspector (`openAgentEv`); 36 px under a finger |
| Evidence inspector open | `agentEvOpen` | a citation was pressed in the conversation in front (`agentEv` names that conversation, so opening another one leaves the source behind). From 1180 px it stands **beside** the conversation as a 380 px column with a `--line` start border, scrolling on its own (`agentEvBeside`); below that it stands **over** the conversation as the phone's full sheet Discover uses (`agentEvOver`). Header *Evidence* with a back control and *Source n*, the source's name and kind, when it was read, the passage on `--hl`, and *Open in mail*, which goes to the thread with the *Back to the conversation* pill (`goLook`). Back (`closeAgentEv`) returns the keyboard to the citation that opened it |
| Evidence inspector closed | `!agentEvOpen` | the resting state — the Agent draws no empty evidence column |

**The controls are the type's.** `Event proposal` takes *Add to calendar*, *Another time* and
*Decline*; `Task proposal` takes *Add* and *Decline*; `Draft` takes *Send*, *Edit here* and
*Discard*; `Suggested action` takes the block's own `cta` and *Decline*. Each is a `role="button"`
with its own `aria-label` naming the object, and on touch they stack full width at 44 px.

**A phase is not stored in the message.** `bkPhase`, `bkState`, `bkResult` and `bkEdit` are keyed by
`<conversation>#<message>#<block>` (`setBk`), which is what lets the seeded histories in
`SEED_CONVS()` stay immutable and lets the same block object be proposed twice.
*Another time* is the case that shows why: `anotherTimeBk` declines the block, writes the
user's own line into the conversation, and comes back with the same proposal at a new hour, pending
again.

**The starter chips open a seeded conversation** rather than asking the question (`agentChips`). Eight conversations are seeded, and three exist for states nothing else reaches:
`c-states`, whose blocks came back loading, error, empty, partial and failed, `c-invoice`, which
is archived, and `c-links`, *Where are the Northwind reports?*, whose answer carries one link the cited
mail carries and one it does not — opened from the history. Three chips open one of them; the fourth is *Real example*, which carries `play_arrow` and
plays the script above instead of opening anything.

### States the blocks screen holds and the prototype does not

`ai-blocks.dc.html` draws the catalogue rather than a conversation, so it is where a
phase is read against its siblings. Thirteen `PROPOSAL_SPECS` cards stand
under the heading *Proposal blocks - the Agent's three phases*, two to a row, one per
type and phase. Its `state` property — `ready`, `loading`, `partial`, `empty`, `error`, `offline` —
drives every card at once, so the empty and error wording of each type is reachable there and
nowhere else. It also states the rule for which types earn a proposal block at all: `Event proposal` and
`Task proposal` exist only because the object has to be seen and corrected before it is approved, and moving
to a folder and marking as read stay a one-line `Suggested action`.

## Chrome that stands over every screen

| State | Flag | Reveal |
|---|---|---|
| Phone composition | `isPhone` / `notMobile` | width alone |
| Single pane | `isMobileState` | `singlePane` |
| Screen FAB | `showScreenFab` | `isMobile` or the toolbar forced it, **and** no dialog is open |
| Mail FAB | `showFab` | no composer, no document, no selection; on one pane it needs the list |
| User menu | `userMenuOpen` | the avatar |
| Settings | `settingsOpen` | the settings control; closing it also drops the account draft. It clears `acctDel` too, which nothing sets any more — removing an account left the design and that one line stayed behind |
| Settings: its five sections | `tabProfil` / `tabAccounts` / `tabAi` / `tabNotif` / `tabApp` | *Profile*, *Accounts*, *AI*, *Notifications*, *Application*, `tabProfil` being the default. On two panes a section navigation (`settingsNavShown`) groups them under *You*, *Mail*, *Assistant* and *This device* (`settingsNavGroups`), each row an icon and a label; on one pane they are a tab strip that scrolls sideways (`settingsStripShown`) |
| Settings: finding a section | `settingsQuery` / `settingsNavEmpty` | *Find a setting* at the top of the section navigation narrows it by a section's name and description; nothing left reads *No section matches.* |
| Settings: the section head | `settingsSectionTitle` / `settingsSectionHint` | two panes only — the section's name at 22 px over one sentence saying what it holds |
| Own photo | `meHasPhoto` | a photo was uploaded |
| **Photo rejected** | `photoError` | a file over 1 MB — a real file has to be chosen, so **no click reaches this** |
| Telemetry off | `telemetryOptOut` | the telemetry switch |
| Notification centre | `notifCenterOpen` | the bell, or the phone's upward gesture |
| Unread notifications | `notifHasUnread` | the unread count; it gates three regions including the rail badge. The panel's own badge is one composed string, `notifNewLabel`, reading *1 new* or *N new* |
| The centre's filter | `notifFilter` / `notifTabs` | **it opens on *Unread*** — the first of the two, *Unread* then *All*; `st.notifFilter` falls back to `"unread"` |
| **No notifications** | `notifEmpty` | the shown list is empty — *Nothing new. All read.*, reached by opening the centre with nothing unread |
| A new message | `notifItems` | **one row per arrived message** while an account has five or fewer unread arrivals: the sender's name as the headline (their address where the message names none), the subject as the second line — *No subject* where it has none — and *Mail · \<account\>* as the source; opening it opens that message. A toast arriving with it says the same two lines |
| **Merged arrivals** | `n.merged` | the sixth unread arrival of one account folds all of them into **one row**, *\<N\> new messages* over *More than five arrived unread, so they are gathered here.*, and later arrivals fold into it while it stays unread; once it is read the next arrival starts a row of its own again. It names no sender and no subject. Opening it opens the mail list; reached in the sample as *8 new messages* from Fabrikam |
| Summarize | `n.summarize` / `summarizeNotif` | the merged row's only extra control, an `auto_awesome` pill reading *Summarize* under the source line (30 px, 44 px under a finger), and *Summarize* in that row's menu. It marks the row read, closes the centre, and opens the Agent space on a new conversation under the chip *\<N\> new messages · \<account\>*, asking *Summarize these \<N\> new messages* at once — the summary covers exactly the messages the row gathered |
| A failure after the answer | `n.failure` / `NKIND.failure` | three failures land after the request was already answered, and each is a durable row — unread, counted in the badge and under *Unread* like any other, with the shared menu. A refused send and an abandoned change take the `failure` kind (`error` on `--err-soft` in `--err-text`); a draft out of step takes `draftcopy` (`cloud_off` on `--warn-soft` in `--warn-text`), matching its toast's warning. **A refused send**: *Not sent - it waits in the Outbox* over the entry's subject, *Outbox · \<account\>*; opening it opens that Outbox with the entry picked, as *Show in Outbox* does. **An abandoned change**: *A change did not reach your mailbox* over *Move - the mail server would not take it, so the message is still where the server holds it.* — the act the change record names, never its destination or its message, which the durable row does not carry —, *Mail · \<account\>*; opening it opens the thread, whose message carries the notice (`m.changeShown`). **A draft out of step**: *Draft out of step with your server* over the draft's subject, *Drafts · \<account\>*; opening it opens that account's Drafts and the composer on the draft (`composeNoServerCopy`) |
| Its decisions | `n.pills` / `n.hasPills` | under the source line, in the *Summarize* pill's style and size (30 px, 44 under a finger), always drawn rather than on a hover, and repeated in the row's menu. The refused send: *Send again* (`send`) and *Edit* (`edit`) — each marks the row read, closes the centre, and does the Outbox's own decision on that entry there, so its decision under way is seen (`decideRefusedNotif`). The abandoned change: *Ask again* (`refresh`) — *Move again…* for a move, as on its message — and *Let it go* (`close`) — the centre stays open, the row is marked read and loses its pills, the message's notice goes, and *Ask again* raises *Asked again* (`decideChange`). The draft row has none: pressing it is the whole act |
| Raised while the client is open | `raiseFailure` / the `raise` property | the open client raises the durable row's own toast (`n.toast`), held 8 s: the refused send as `error`, *Not sent - it waits in the Outbox* — *“…” is in the Outbox of \<account\>. Send it again or edit it there.* — with *Show in Outbox*; the abandoned change as `error`, *A change did not reach your mailbox* — the row's own sentence, *Move - the mail server would not take it, so the message is still where the server holds it.* — with *Show message*; the draft as `warning`, *Draft out of step with your server* — *“…” is kept in MailFathom, but your mail server's copy may be missing or out of date.* — with *Open draft*. Each action opens what the row opens and marks it read |
| One failure, one voice | — | **when the open tab is already following a change** — its pending-change line in the connection summary (`m_pending` in the Client States file) — that line and its toast stand for the failure, and no second toast is raised for the durable row. Deciding it there, *Ask again* or *Let it go*, marks the durable row read, so one failure is never shown as two. The durable row is still listed in the centre. The prototype draws no pending-change line, so it always raises |
| Connection summary | — | a green dot and *Every account is up to date.* at the foot of the folder column while it is expanded (`sideExpanded`; a collapsed column draws none), and always in the Discover head — and nowhere else: Agent, Tasks, Calendar and People draw none, because how current the mailboxes are is said where mail is read and asked about rather than on every screen. **When every account is current it is green and nothing more** — no disclosure, no per-account rows, no times. The prototype draws only that state; an account behind or stopped is `m_sync` in the Client States file, and changes on their way are `m_pending` |
| Refresh | `refreshAll` / `refreshBusy` | the `refresh` control in the rail, titled *Refresh* (44 px under a finger), and on the phone the *Refresh* row of the *More* sheet (`moreRefresh`), which closes the sheet as it starts — it re-reads the mail in front of the reader and the notification centre, pulses its glyph (`mfpulse`, 1 s) rather than spinning while it waits, and refuses a second press until the re-read lands |
| Move dialog | `moveOpen` | the move action on a selection — grouped by mailbox (`moveGroups`), each group ending in *New folder here*, and the toast it produces names the mailbox |
| Permanent delete | `confirmDel` under `inTrash` | a delete action while the open folder is a Trash — *Delete permanently*, no undo offered, and the toast reads *Permanently deleted* rather than *Moved to trash* |
| The order of the spaces | `railSepStyle` | *Discover* and *Agent* first — the two places that ask questions of the mail — then a 1 px `--line` hairline inset 16 px from either side of the rail, then *Mail*, *Tasks*, *Calendar* and *People*. The bottom bar carries *Discover*, *Agent* and *Mail* in that order and draws no hairline: its five places are counted |
| The rail's nav scrolls | `railNavStyle` | a viewport too short for the six destinations — the nav scrolls and the bottom cluster stays pinned |
| The *More* sheet | `moreOpen` / `moreItems` | the phone's *More* — *Tasks*, *Calendar* and *People*, the current one on `--accent-soft`, then under a rule *Refresh* (`moreRefresh`) and *Settings* (`moreSettings`); every row 52 px |
| A rail destination | `railBase` / `activeStyle` | on the phone a bottom-bar place whose glyph sits in a 56 × 32 px pill, filled `--accent-soft` on the current one (`--nav-mark`); on the rail a tile inset 7 px from either side of the rail on a 12 px radius, 4 px apart (2 px below 1020) — the current one on `--accent-soft` with an accent glyph and a semibold label, hover on `--hover`. No full-width slab and no edge bar, so neighbouring tiles never meet across a dark gap |
| Cancel confirmation | `cancelAskOpen` | closing a toast that carries a running operation |
| Toasts | `toasts` | any operation that reports; `toasts.dc.html` is the whole of it |

Every label in the prototype is English, among them the Settings tabs *Profile* and *Application*, the user menu's
*Sign out*, and the single-pane state toggle beside the thread head, *Hide panels - correspondence only* against
*Show thread panels* — and `design/parity.json` presses two of those under their English names.

### The settings panel

Added on 2026-09-11, where the panel had held two tabs and a fixed card.

**Settings take the whole window at every composition**, the desktop included, because the list of
sections is meant to grow well past five and a dialog leaves no room for a navigation that grows with
it. There is no card, no grip, no drag and no stored size: the panel is a full-window surface on
`--bg` that arrives with `pop` on two panes and `sheet` on one. Its head is a 22 px `settings` glyph,
*Settings* and the close control. On two panes a 264 px section navigation (220 below 1020) runs down
the left on `--sub`, with *Find a setting* at its top and the version and build at its foot, and the
section itself scrolls on the right in a column at most 760 px wide. On one pane the sections are a
tab strip under the head and the version sits in a footer.

| State | Flag | Reveal |
|---|---|---|
| The account list | `acctListShown` | the *Accounts* tab with no draft open — one row per account carrying a colour dot, the display name, the address, `IMAP host:port · SMTP host:port` and `chevron_right`, under a `Mail accounts` caption beside `acctCount`, and under the rows the sentence that settles what this tab is: *Accounts are set up by your administrator - here you can open one and change its settings.* |
| The account editor | `acctEditShown` | a row. `acctTitle` reads *Edit account* and nothing else, there being no second way in |
| The address is fixed | the `E-mail address` input | it is `readOnly` with `tabIndex` `-1`, drawn on `--bg` rather than `--sub`, under *The address identifies the account and cannot be changed here.* |
| Save refused | `acctSaveStyle` | the display name or the address is empty — the button is drawn flat and refuses the pointer |
| The password shown | `acctPassType` / `acctPassIcon` | the `visibility` control beside the field |
| Login left empty | `acctLoginHint` | nothing typed in *Login* — *Empty = the e-mail address is used as the login.* |
| Connection test refused | `acctTestBad` | *Test connection* while a host, a port, a login or a password is missing — it names every field still empty |
| Connection test passed | `acctTestOk` | *Test connection* with all six filled — it names the two hosts |
| An earliest date set | `acctEarliestClearShown` / `acctEarliestHint` | the date field — the hint states that older mail stays on the mail server, and reverts to *No limit* when cleared |
| The folder mapping | `acctFolders` | six rows pairing Inbox, Sent, Drafts, Archive, Spam and Trash with `INBOX`, `Sent`, `Drafts`, `Archive`, `Junk` and `Trash` |
| The three switches over what it deletes and sends | `acctHardDelete` / `acctPurgeGone` / `acctSaveSent` | each is on by default and each carries a hint that changes with its position — hard delete states that a message goes from both servers with nothing left to restore |
| The secret check | `acctSecretScan` / `acctSecretScanWarn` | the account editor's own switch, under `Before sending to the model - this account only`, on by default; while it is on, a warning states the check is best-effort and can both miss a secret and hide ordinary text |
| The AI language | `aiLangOptions` / `aiMatchReply` | *AI* — English or Polski, **English first and English selected** (`st.aiLang` falls back to `"en"`), separate from the interface language, with a note that a change applies only to content written from now on, and *Match the language of the message* beneath it. `langOptions`, the interface language in the account menu, is drawn and defaulted the same way |
| Notifications cleared on read | `notifAutoClear` | the *Notifications* tab's switch; while it is on, a `Remove` select offers Right away, After 1 hour, After 24 hours and After 7 days |
| Notified on this device | `notifDevice` | the *Notifications* tab's `This device` group, off by default — *When the window is not in front of you, this device tells you how many things arrived and of what kind - never from whom, never what it is about.* |
| How long one stays up | `notifDuration` / `notifDurationLabel` | the range beneath it, 1 to 30 whole seconds and 5 by default, drawn as `<n> s` in the accent; its copy ties the value to the undo window, which *waits exactly as long as its notification is up* |
| The message view | `viewAiStyle` / `viewSimpleStyle` / `viewHtmlStyle` | *Application* — a `Message view` strip of three, *AI simplified*, *Simplified* and *Original* in that order, each drawn by one `segStyle` helper that is the only place the segment's own styling lives |
| Images loaded automatically | `autoImages` | *Application* — while it is on, a warning states the image is fetched from the sender's server and is how tracking pixels work |
| Layout reset | `resetLayout` / `resetLayoutDone` | *Reset layout* under `Layout on this device`; the confirmation *Layout restored to the defaults.* stands for 3 200 ms and then goes |

**An account is neither added nor removed here any more.** The dashed *Add account* row, `acctAdd`,
*Remove this account* and the two-step removal dialog that made the name be typed out are all gone
from the source, so the *Accounts* tab opens an existing account and changes its settings and does
nothing else. A client screen offering either act is offering something the design does not have.

**The secret check is per account, and the AI tab says so rather than holding it.** The switch and
its warning moved into the account editor, and where the global switch stood the *AI* tab now draws a
`shield` note reading that the check *is set separately for each mail account - open
Accounts and pick the account*. Which is why the editor's own group is captioned
`Before sending to the model - this account only` and its copy says *from this account*.

The message view is **three** options rather than two, drawn *AI simplified*, *Simplified*,
*Original* — the last reading **Original** and not *HTML*. `msgView` carries `"ai"`, `"simple"` and
`"html"` behind them, and *Simplified* is what an unset value draws: `viewSimpleStyle` is on whenever
`msgView` is neither of the other two, rather than testing for its own value.

`viewHint` changes with the choice and `htmlModeWarn` does not: the warning, the inline original and
the *Show the original message?* confirmation all still turn on `msgView === "html"` alone, so
*AI simplified* reveals none of them and behaves as *Simplified* does everywhere but the hint. That
hint is the panel's longest copy and states what the view is: the same deterministic cleanup as
*Simplified*, with AI deciding only what to keep and what to drop, never rewriting a word, so the
text stays exactly as the sender wrote it and only the presentation changes — usually cleaner than
plain *Simplified*, especially on newsletters and long reply chains.

### What a refresh turns up, and which row animates

*Refresh* is a product control (the table above), and what a re-read turns up in the prototype is one
scripted batch out of three (`DEMO_BATCHES`) — a message lands at the top of the list and
in the notification centre, a second after 1 000 ms an existing thread is replaced in place and its
reading held back, and a third after 2 050 ms is simply no longer in the list, with the toast *The
sender withdrew a message - the thread has left the list*.

**The batch is the prototype's stand-in for live data, not a state to build.** What the client owes
here is the states it reveals, each of which the client reaches from its own live data:

| State | Flag | What it draws |
|---|---|---|
| A row arriving | `rowAnim[<id>] === "in"` | `mfrowin` — 480 ms, opening from zero height with a 7 px drop |
| A row deleted | `rowAnim[<id>] === "outdel"` | the collapse below, and `mfrowdel` over it — 460 ms, an error bar and an error wash that come up and stay while the row goes |
| A row archived | `rowAnim[<id>] === "outarc"` | the same collapse, and `mfrowarc` — the warning bar and wash, otherwise identical |
| **A row removed with no act named** | `rowAnim[<id>] === "out"` | the collapse alone, with no wash — the fallback `animateRowsOut` takes when no act is passed, and **all four of its callers pass one**, so nothing in the prototype reaches it |
| A row moved to a folder | `rowAnim[<id>] === "move"` | `mfrowmoved` — 900 ms, a warning wash that comes up and fades back out; the row stays, so nothing collapses |
| A row changing in place | `rowAnim[<id>] === "edit"` | `mfrowedit` — 1 700 ms, an accent bar and a soft accent wash that fade out |
| A notification arriving | `notifAnim[<id>] === "in"` | the same `mfrowin` on the notification-centre row |

The collapse the three removals share is `mfrowout` — 440 ms, closing to zero height and sliding
22 px left, pointer events off while it goes — and the state change itself lands at 380 ms, before
the wash has finished (`animateRowsOut`). The wash is drawn on the row rather than on the
wrapper that collapses (`rowWash`), which is what lets the two run at different lengths.

The comment above them is the requirement rather than the animation: *Only the touched tile
animates — the list is never re-rendered as a whole, so scroll position and every other row stay
exactly where they were.*

**The three `out` states belong to a removal the person performed** — archive, delete, delete
permanently, and the swipe that archives: the row collapses under the gesture that removed it, and
then the state change lands. A row that goes because a re-read returned a list without it plays
nothing at all, since that arrives as a new list rather than as an act — which is why the batch's
third row leaves without it. An undo pressed while the row is still leaving cancels the removal that
was pending.

**The wash names the act, and the colour is the whole of the naming**: the error pair for a deletion
and for a permanent deletion, the warning pair for an archive. The swipe that archives draws the same
warning colours behind the row as it is dragged (`swipeBgStyle`) rather than the success
green it used to, so the gesture and the wash that follows it are one colour rather than two. A move
is the one act that washes a row it does not remove: the row keeps its place under its new folder
chip while `mfrowmoved` fades back out behind it.

## Sign-in — `sign-in.dc.html`

Two views in one file, `isLogin` and `isServer`, and the server view is unreachable when the address
is forced by configuration.

| State | Flag | Reveal |
|---|---|---|
| Sign-in | `isLogin` | the opening view |
| Server address | `isServer` | *Change server* under *Advanced* — and `envLocked` refuses it |
| SSO offered | `showSso` | the server declares it (prop `ssoAvailable`, on by default) — the accent primary button above everything else, `shield_person` and `open_in_new` around `{{ ssoLabel }}`, under a hint reading *<provider> opens in your browser.* The provider's name is the prop `ssoName`, *MailFathom SSO* by default |
| SSO handing off | `ssoBusy` / `ssoIdle` | pressing it — the two glyphs give way to the spinner, the label becomes *Opening <provider>…*, the button drops to `opacity:0.85`, and the hint becomes *Finish signing in in the browser window - this screen picks up the session when it returns.* It returns to idle after 2 000 ms |
| SSO, then something else | `showSsoDivider` | SSO offered beside OAuth or a password — the divider reads *or another method* |
| OAuth offered | `showOauth` | the server declares it (prop `oauthAvailable`) |
| Password offered | `showBasic` | the server declares it (prop `basicAvailable`) |
| Both, so a divider | `showDivider` | OAuth and a password both — this one reads *or with a password* |
| **No method at all** | `noMethods` | none of the three — a **prop triple** now, so only a property change reaches it |
| Kept signed in | `remember` | the *Keep me signed in* checkbox, drawn under the password field and **only where the password form is** — checked it reads *This device stays signed in for 30 days. Applies to password sign-in only.*, unchecked *Applies to password sign-in only — provider sessions follow their own rules.* |
| Connecting | `connecting` | pressing *Connect*, or an OAuth provider |
| Advanced open | `advanced` | the *Advanced* control |
| Insecure allowed | `insecure` | the checkbox on the server view; it changes the lock glyph, the port hint and the certificate row |
| Server was customised | `customized` | a non-default address or the TLS exception; it reveals *Restore defaults* |
| Probing an address | `probing` | *Connect* on the server view |
| An error | `hasError` | an empty login, an empty password, or an address with no host |
| Theme is auto | — | `themePicks` carries `auto`, `light`, `dark`; `auto` resolves through `prefers-color-scheme` |

The layout collapses twice rather than once: `stacked` at `w < 940` puts the brand above the form,
and `phone` at `w < 700` changes every target size. The pitch beside the form (`showPitch`) exists
**only when not stacked**.

## Toasts — `toasts.dc.html`

Six kinds — `neutral`, `success`, `error`, `warning`, `info`, `loading` — and the kind decides the
glyph, the colour and the bar. What is worth reading in the source rather than in a screenshot:

- **A `loading` toast is sticky.** Its close control does not dismiss it; it asks whether to abort
  the operation, and aborting posts a `warning` toast saying what was and was not saved.
- **A failure toast names where the thing stands and the way to it, never a status code.** The error sample is the
  refused send — *Not sent - it waits in the Outbox*, *“…” is in the Outbox of \<account\>. Send it again or edit it
  there.*, *Show in Outbox* — and so is the end of *Running operation → error*, whose abort says only that it stops
  the send (*Aborting stops it here, so it is not sent.*), never that it saves a draft. Two samples beside it are the
  other failures a notification raises: *A change did not reach your mailbox* (`error`, *Show message*) and *Draft
  out of step with your server* (`warning`, *Open draft*).
- The stack is newest-first and capped at `maxStack` (4 by default); past it the oldest is dropped.
- Resting opacity is 0.8 and 1.0 under the pointer, so the content beneath stays readable.
- The **blocking overlay** is a separate thing from a toast: determinate (a percentage) or
  indeterminate, no dismissal by scrim or Escape, and its *Cancel* asks before it aborts.
- On the phone and the fold (under 900 px) a toast is a **slim strip**: one row of a 28 px glyph, the
  title at 14 px over a body held to two lines, the action inline at the right rather than under the
  text, and the close control — 6 px apart, 8 px from the top, full-width on the phone and a centred
  440 px column on the fold, and fully opaque, there being no pointer to raise it. Wider, the stack
  is a 400 px column on the right. The prototype's toasts follow the same rule (`toastCompact`).

## The notification gesture — `notification-gesture.dc.html`

Numbers the implementation reads, not decoration: 1 : 1 finger tracking with no easing, scrim opacity
**equal to** the travelled fraction, a **0.32** distance threshold, a **0.5 px/ms** velocity
threshold, a **260 ms** `cubic-bezier(.32,.72,0,1)` spring back, a **12 px** navigation-bar slop and a
**10 px** row slop that cancels the 420 ms long press. Three hand-overs are stated: a scrolled list
gives the gesture up at its own top *without lifting the finger*, a row's long press and the drag are
mutually exclusive, and the navigation bar hands over past 12 px and cancels its own tap.

**Phone composition and `pointer: coarse` only.** The fold, the tablet and the desktop keep the panel
they draw today, and there is no drag gesture there at all.

## Mail search — `mail-search.html`

Five states, all drawn side by side in the file: results, empty, waiting, a lexical-only banner, and
a failure banner. Worth carrying out of it:

- The result row **is** the folder row, and the third line — whose height is reserved always — carries
  the reason for the match.
- An empty result never stands alone: the scope that produced it stands beside it, with one press
  that widens it.
- *This server does not search by meaning* and *you may not see this* are different sentences and are
  never merged.
- Earlier searches live only in client state and go with the credential; no typed phrase reaches a log
  or telemetry.
- The field follows what the deployment can do. A deployment that reads phrases gets *Search, or
  describe what you need*, the ranking row *What this search is ranked by*, and one line naming what
  the sentence could not use. A words-only deployment keeps *Words from the message you are looking
  for*, and the ranking row never appears. The file points at `m_search` in the Client States file for
  both, which is where they are drawn.

## Client states — `client-states.dc.html`

It draws the states the client renders and the prototype does not: refusals, failures, waits, empties,
and the in-flight states of a run. Each one is drawn at the full width of each composition, with the
navigation rail in place. The prototype carries the same list as a comment block right after its
Markdown helpers, so a session reading the prototype finds its way here.

**How a state is reached.** Three component properties decide what the artboard draws:

| Property | Values | Default |
|---|---|---|
| `state` | `all`, or one state id below | `all` |
| `layout` | `all`, `telefon`, `fold`, `tablet`, `desktop` | `all` |
| `theme` | `light`, `dark`, `both` | `dark` |

`state: all` draws a ledger first: one row per item, with a *Drawn*, *Changed* or *Merged* verdict and a
sentence. Clicking a row sets the same focus the property does. Each state's gate is
`show.<id>`, set in `renderVals()` from the `STATES` table. The ledger is `LEDGER`. The compositions use the prototype's four sizes — `W` and `H` — and
`f.notPhone` hides the side columns at `telefon`.

It reuses the prototype's tokens, notice box, chips, buttons, skeleton shimmer, toast, confirm dialog,
and *Nothing open* layout. **The one new component is the radio list in *Kind of folder*.**

### Discover

| State | Gate | What it settles |
|---|---|---|
| `d_refusal` 1.1 | `show.d_refusal` | A question refused before a run is one line directly under the field — never a toast, never a card, because no run exists. The question stays in the field. Signed out and not permitted read neutral; unavailable and unreadable read as a warning with *Try again*. On a phone the action drops under the text at 44 px |
| `d_cancel` 1.2a | `show.d_cancel` | The running bar keeps its slot through running → cancelling → ended. While cancelling, the pulse becomes a muted spinner and *Cancel run* becomes a disabled *Cancelling…*. A failed stop turns the bar into a warning with *Try stopping again*, while blocks keep arriving underneath |
| `d_ended` 1.2b | `show.d_ended` | The ending card replaces the running bar in the same slot, above the blocks that arrived, and nothing below it moves. The answer that arrived gains a *cut short* chip. Eleven reasons fall into five tones (`ENDINGS`): *Stopped by you*, *Limit reached* (period and run allowance), *Try again* (timed out, temporarily unavailable), *Could not finish* (failed, gone, stopped) and *Not possible here* (unavailable, retrieval refused). Only the period allowance carries the when-line |
| `d_finished` 1.2c | `show.d_finished` | A completed run gets **no card**. The bar leaves, and how many messages the answer was read from goes into the run line (*Read 214 messages*). The allowance count shows only where the deployment meters questions. No endpoint, model, duration, or spend is shown |
| `d_coming` 1.3a | `show.d_coming` | One dashed card trails the last arrived block. Loading is the prototype shimmer. Error and offline keep the dashed outline and swap the shimmer for a sentence, and **only offline offers Retry** |
| `d_empty` 1.3b | `show.d_empty` | *Nothing composed* is a plain card with no button, because asking again gives the same result. A newer-version answer (the whole answer) and an unknown block (one block among others) share one dashed neutral card, and neither names a version number or the block's type |
| `ev_opening` 1.4a | `show.ev_opening` | The inspector header paints at once from what the citation knows. Only the body waits, as shimmer with *Opening the source…*, and the footer appears once the source opens. On a phone the inspector covers the screen |
| `ev_failed` 1.4b | `show.ev_failed` | The failure takes the quote's place under the header. Unavailable and unreadable are faults (warning, *Try again*); signed out and missing are conditions (neutral). There is no footer |
| `ev_private` 1.4c | `show.ev_private` | The lock sits in the badge slot the inspector and evidence list already have, and the body names the mailbox and points to the administrator. **A "not permitted" read failure renders exactly this** — the ledger marks 1.4 *Merged* for that reason |
| `ev_outdated` 1.4d | `show.ev_outdated` | The warning comes first. Below it is the quoted passage in a plain box, not the highlight, because it can no longer be pointed to. *Open in mail* stays |
| `ev_kinds` 1.4e | `show.ev_kinds` | A whole-message citation shows the message unhighlighted, with one line saying so. An attachment file row, a source the client cannot open, and text recognised in an image each replace only the body; header and footer keep their places |
| `d_asked` 1.5 | `show.d_asked` | *Asked before* is kept, **only while the field is idle**, under the field. A chip carries the question alone, and tapping it asks again. The stored answer and its freshness are deliberately not shown, and the ledger says `savedAnswers.a` and `.fresh` can go. *Forget these* clears the row on this device without confirmation |

### Mail

| State | Gate | What it settles |
|---|---|---|
| `m_verdict` 2.1 | `show.m_verdict` | The sender verdict sits directly under the sender line, above the body. A warning is the prototype's notice box; **a healthy verdict is one quiet line**. On a phone both wrap under the sender line at full width |
| `m_more` 2.2a | `show.m_more` | The paging control ends the thread, with how much has been read beside it. While loading it becomes a status line in the same place, and once everything is read one faint line closes the thread. On a phone the button is 44 px and the count drops underneath |
| `m_partial` 2.2b | `show.m_partial` | A partial failure sits **where the missing messages would be**, between the ones read. The two head notes go under the subject: *more people* in muted ink, *more messages than were assembled* as a warning line |
| `m_pane` 2.2c | `show.m_pane` | Failed, offline and *nothing you are allowed to see* reuse the *Nothing open* layout, with an icon for the logo and one action where one exists. The list stays usable beside it |
| `m_folders` 2.3a | `show.m_folders` | The folder column loads as **a skeleton shaped like the tree**. *Reading mailboxes and folders…* stays only as live-region text for screen readers. Failed, offline (the last tree dimmed) and *No account configured* are drawn too. The column is in place on desktop, a drawer over the list on fold and tablet, and full screen on a phone |
| `m_list` 2.3b | `show.m_list` | The list's empty states are centred in the column, one sentence each. *Nothing matches* names what is narrowing and offers *Clear filters*. A partial failure is a warning strip above the rows that did arrive. An empty list that also failed says the folder could not be read — never that it is empty |
| `m_kind` 2.4a | `show.m_kind` | When a mailbox lacks a role folder, *Kind of folder* becomes **the first field**, as a radio list. Picking a role folds *Folder name* and *Inside* away and puts the hint in their place, and *An ordinary folder* brings them back. On a phone the dialog is full screen |
| `m_roles` 2.4b | `show.m_roles` | Flagged (`flag`), Important (`label_important`) and All mail (`all_inbox`) join the standard folders in reading order: what needs you, what you wrote, then everything. The Outbox is no row of its own: it is the `outbox` mark on each account's Sent row, drawn only while something waits or was refused — § *The Outbox* under Mail draws the screen behind it |
| `m_search` 2.5 | `show.m_search` | A described search turns the sentence into two kinds of chip. Filters keep the Mail Search chip (rail tint) and decide what is in. Ranking criteria get their own labelled row in accent tint with a sort icon, and each one's × reads *Stop ranking by {criterion}*. One muted line under both names what was not used. *Reading what you wrote…* and the words-only field are drawn below |
| `m_pending` 2.6 | `show.m_pending` | **The connection summary itself becomes the indicator**: its dot and sentence take the most urgent state, and clicking it opens a panel under it — a bottom sheet on a phone, where the summary is a chip at the right of the header. The panel lists accounts first, then changes on their way, most urgent first. Toasts still announce each action, and a failure landing after its toast is gone raises one warning toast whose action opens the panel. A change this tab follows that the server never took also becomes a durable row in the notification centre (*A change did not reach your mailbox*), but **this line and its toast stand for it**: no second toast is raised for the row, and deciding it here — *Ask again* or *Let it go* — marks that row read, so one failure is never shown as two (§ *Chrome that stands over every screen*, *One failure, one voice*) |
| `m_sync` 2.7 | `show.m_sync` | **Green is the whole message when every account is current**: a dot and one sentence, nothing to open. An account catching up or stopped turns the dot amber and makes the sentence a control that opens onto one row per account — its name and its state, never a time or an *oldest refresh* line |

### Tasks, Calendar and People

| State | Gate | What it settles |
|---|---|---|
| `c_edit` 3.1 | `show.c_edit` | *Edit* turns the open event's dialog into the form in place: same dialog, title *Edit event*, *Cancel* and *Save*. Starts and Ends each take a day and a time, and *All day* hides both times. *Save* stays disabled until something changes and says why when the times conflict. **The new-event form should take the same fields** |
| `t_layout` 3.2 | `show.t_layout` | *Lay out today* happens inside the capacity panel, which grows to hold the offer. Nothing is written until *Add to calendar*, and what did not fit is named, not only counted. The panel draws waiting, the offer, nothing left and allowance spent. Unavailable hides the button and keeps the capacity line. On a phone the panel sits above the list |
| `t_proposed` 3.3a | `show.t_proposed` | A proposal has **no checkbox**; the AI tag takes its place. *Accept* is on the row, and the menu has *Accept* and *Dismiss task*. Undated tasks gather under *No day* at the end. The status line sits between the list head and the first group and is replaced by the next change; a partly refused change is a warning line |
| `t_states` 3.3b | `show.t_states` | The two empties point at different things — nothing at all, versus everything done and hidden (naming *Show done*). Loading is the list skeleton; reading more is a spinner line at the foot |
| `p_none` 3.4a | `show.p_none` | From fold upward the detail pane shows *Nothing open* with one sentence before anyone is picked. A phone shows the list alone. After a change, one status line sits under the name: green when it worked, red when refused |
| `p_states` 3.4b | `show.p_states` | Each address book says why it is empty in its own terms. Loading and failure look the same for the book (in the list) and for correspondence (in a person's *Conversations*), and each fails on its own |

### Agent

| State | Gate | What it settles |
|---|---|---|
| `a_steer` 4.1 | `show.a_steer` | While a run is going, *Send* reads **Steer**. The note appears as a user turn tagged *Joins at the next turn*, then *Taken in*, and the status line names what it is taking in. *Cancel* is unchanged and still stops the whole answer, note included |
| `a_failed` 4.2a | `show.a_failed` | A failed answer keeps what it wrote and ends with an error notice in the flow, with *Try again*. A message that was not sent never enters the conversation: it stays in the field, and one red line under the field says why, for any of five reasons |
| `a_read` 4.2b | `show.a_read` | A conversation that could not be read fills its pane with the *Nothing open* layout, and all five reasons share it. History reads and fails inside its own column (a drawer on smaller compositions). Refused deletes, archives and proposal decisions use the prototype's error toast. Deleting several uses the confirm dialog with a plural count |

## States the source has and a preview does not reach

This is the class the inventory exists for. Each is in the source, and none of them can be reached by
clicking a preview.

- **The application with no mailbox at all.** `const emptyApp = false;` is a constant, and
  every one of the six screen gates is `st.screen === "…" && !emptyApp`. Nothing anywhere draws the
  true branch, so **the design does not cover a deployment with no mailbox** — a session that needs
  that screen is designing something the design has not settled, and it is drawn in `design/` — ahead of
  the client or in the pull request that builds it — rather than invented in the client. The folder column alone has one: `m_folders` in the Client States file draws
  *No account configured*, which is the column's state and not the application's.
- **A rejected profile photo** (`photoError`). Set only by choosing a file over 1 MB; no control in
  the prototype can produce one.
- **Every tab state** (`tabsBarShown`, `noTabsOpen`, `convBarShown`, `closeAllAsk`). Two conditions at
  once: the user menu's *Tab mode* switch turned on and a width of at least 1180 px. The initial state
  is `classic`, so a preview opened at any width shows none of it.
- **No sign-in method offered** (`noMethods`). Three properties turned off together — `ssoAvailable` joined `oauthAvailable` and `basicAvailable`.
- **A person with no documents** (`personNoDocs`). No mock record has an empty
  `docs`, so the empty state is drawn by no preview at any click.
- **An arrival with no subject, and the moment arrivals merge.** Every sample mail row carries a subject, so *No
  subject* is drawn by no preview; and the sample centre holds its merged row from the start, so the sixth arrival
  folding five separate rows into one is a rule the inventory states rather than a click that plays it.
- **The spam that folds, and what a verdict changes.** The two folded sample rows stand at the very end of the mail
  list, so no preview opened at the top shows one; the Spam folder's badges need that folder picked, the move into
  Spam needs a move, and the refused selection, the *Not spam* confirmation, the landing in the Inbox, both acts on
  the personal account, which does not move spam, and the read-only refusals each need a selection or a press first —
  § *Spam* under Mail states every one of them.
- **A failure raised while the client is open** (`raiseFailure`). The three failure rows start in the sample
  centre, so their toasts are raised only by the prototype's `raise` property — `send`, `change` or `draft` —
  and the rule that a tab already following a change raises none (*One failure, one voice*) has no pending-change
  line in the prototype to follow.
- **No proposals left** (`calNoProposals`). Reachable only by dealing with every proposed slot first.
- **A conversation archive** (`hasArchived`, `archiveSectionOpen`). Needs a conversation archived first.
- **The `fold` composition's own case.** 884 px is `compact`, `narrow` and `!singlePane`: two panes with the list
  folded to its strip, and under a finger the folder drawer. Neither the phone artboard nor the desktop artboard
  shows it, and no click produces it — only a width does.
- **Every height band and every pointer a frame does not carry.** The four frames sit in the regular height band and
  pair width with pointer the way their devices do, so the low band, the tall band, a mouse in the tablet band and a
  finger on a desktop-wide window are reached only by a window of that size and pointer — § *How a window is
  composed* lists the matrix sizes that reach each one.
- **Pressed, keyboard focus, and reduced motion.** All three are rules in `styles/base.css` rather than regions of a
  template, so no artboard draws them at rest — *What every artboard shares* above states each one.
- **Every arrival and departure.** A surface in motion lasts 120 to 240 ms and a capture is taken at rest, so no
  screenshot holds one — *How a surface arrives and leaves* above is where they are stated, by number.
- **What back does.** A press of back is a history traversal rather than a click on anything drawn, so no capture
  reaches the state before or after one — *What back does* above states each step.
