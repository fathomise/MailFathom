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
gestures, and hover. Exactly one rule reads the window's aspect — where the navigation stands (`navBottom`, below) — and
every other rule reads one axis, so a device upright and the same device on its side need no rules of their own: each is
one width band combined with one height band. All of it is decided once, in `renderVals`, before any state is.

### Width — the columns

| Name | Condition | What it changes |
|---|---|---|
| `isMobile` | `w < 700` | Bottom navigation instead of the rail, every form a full-screen surface, the folder list always a drawer |
| `singlePane` | `w < 820` | One pane at a time — the list and the content never stand together |
| `narrow` | `700 ≤ w < 1020` | Two panes in little room: the list stays a list, at a fixed 248 px until its boundary is dragged, and the thread takes the rest. It never folds away, and a row too narrow for one line of everything it carries draws less on that line (`listCompact`, § *The panes* under Mail) |
| `compact` | `700 ≤ w < 1180` | The 66 px rail with a 30 px mark (the bottom bar instead where `navBottom` holds below 1020), the list at 284 px from 1020 until its boundary is dragged (248 px below that — the `narrow` row), toolbar controls without labels once their words no longer fit the strip — each still naming itself in a bubble, *A control's name* under § *What every artboard shares* — and the folder list folded rather than standing as a column |
| `wide` | `w ≥ 1920` | The folder column at 240 px and the list at 420 px |

**The navigation is the one rule that reads the aspect.** `navBottom` is `isMobile || (narrow && touch && w <= h * 1.15)`:
the phone, and under a finger a window in the `narrow` band that is upright or near square — the fold and the small
tablet — carry the navigation as a bar along the bottom rather than as the rail, so it stands under the thumb and the
panes take the whole width. Near square counts because a fold's inner screen is near square: a browser's address bar
and the system's own bars take enough height that the fold held upright reaches the page a little wider than tall, and
it is still held upright. A window more than 1.15 times as wide as it is tall — the fold, the small tablet and the phone
on their side — keeps the 66 px rail, because there the height is what is short and the width is not;
a mouse keeps the rail at every size, having no thumb to reach with; and a tablet of 1020 px and wider keeps it upright as
well. The phone's bar holds five places and puts *Tasks*, *Calendar* and *People* in *More*; the fold's and the
tablet's bar holds all six spaces, the bell and *More* — § *What every touch composition holds to*.

From 1180 to 1920 the rail is 96 px, the folder column 210 px (draggable to 420 under a mouse) and the list 340 px
(draggable from 232 to 560). Past 1920 the window's extra width goes to the thread pane, whose reading column is
capped — § *Width inside a pane* below — so a 2560 px window shows a 96 px rail, a 240 px folder column, a 420 px list,
and the thread's 880 px column centred in what is left. Settings, which fills the window below that, stands past 1440 px as a 1440 px surface centred on `--bg` between two `--line` edges (`settingsCardStyle`).

**The folder list has three forms, and each question about it reads one axis.** Width says whether it stands: as a
column from 1180, folded below it, and always a drawer on one pane under 820, where the window shows the list or the
message and nothing may stand beside either. The pointer says what *folded* is between 820 and 1180: a drawer under a
finger (`drawer`), because a finger has no hover to tell a strip of glyphs apart by, and a 58 px strip of glyphs under a
mouse (`sideCollapsed`), each named by its title, which the toolbar's `left_panel_open` widens to the column again. So
`drawer` is `singlePane || (compact && touch)`, and `sideCollapsed` starts true wherever the column does not stand.

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
touch composition holds to* below; sheets and drawers follow the finger; a row swipes and holds; what stands under tabs swipes between them; the message list pulled down from its top refreshes; a toast is dragged aside to dismiss it; a pane boundary carries a grip to drag it by; and nothing exists
only under a hover — not a control's name, not the reason a control cannot act, not a fact a row folds away (*A control's name* under
§ *What every artboard shares*). Without it: the desktop's sizes and hover affordances, and pane boundaries drawn as the
bare 5 px line.

### Width inside a pane

What stands inside the thread pane responds to the width **that pane** was given (`paneW`), never to the window's,
because the same window gives the pane more or less as the folder column folds to its strip, the list is dragged wider,
or either goes into a drawer. `paneW` is the window less the rail, the folder column or strip, and the list; on one pane
it is the window less the rail.

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
| `phone_844x390` | 844 × 390 | touch | low | 66 | drawer | 248 | 530 |
| `fold_832x884` | 832 × 884 | touch | regular | bottom bar | drawer | 248 | 584 |
| `fold` | 884 × 832 | touch | regular | bottom bar | drawer | 248 | 636 |
| `tablet_744x1133` | 744 × 1133 | touch | tall | bottom bar | drawer | one pane | 744 |
| `tablet_1133x744` | 1133 × 744 | touch | regular | 66 | drawer | 284 | 783 |
| `tablet_820x1180` | 820 × 1180 | touch | tall | bottom bar | drawer | 248 | 572 |
| `tablet_1180x820` | 1180 × 820 | touch | regular | 96 | 210 column | 340 | 534 |
| `tablet` | 1024 × 768 | touch | regular | 66 | drawer | 284 | 674 |
| `tablet_1024x1366` | 1024 × 1366 | touch | tall | 66 | drawer | 284 | 674 |
| `tablet_1366x1024` | 1366 × 1024 | touch | tall | 96 | 210 column | 340 | 720 |
| `desktop_1280x720` | 1280 × 720 | mouse | regular | 96 | 210 column | 340 | 634 |
| `desktop` | 1440 × 900 | mouse | regular | 96 | 210 column | 340 | 794 |
| `desktop_1920x1080` | 1920 × 1080 | mouse | tall | 96 | 240 column | 420 | 1164 |
| `desktop_2560x1440` | 2560 × 1440 | mouse | tall | 96 | 240 column | 420 | 1804 |
| `desktop_960x1040` | 960 × 1040 | mouse | tall | 66 | 58 strip | 248 | 588 |
| `desktop_1280x1400` | 1280 × 1400 | mouse | tall | 96 | 210 column | 340 | 634 |
| `desktop_1080x1800` | 1080 × 1800 | mouse | tall | 66 | 58 strip | 284 | 672 |
| `desktop_1440x2460` | 1440 × 2460 | mouse | tall | 96 | 210 column | 340 | 794 |

What the upright tablets are, drawn rather than fallen into:

- **744 × 1133** is one pane under a finger in a tall window: the bottom bar under the list, and the thread pushed over
  it when opened, its head kept open as it scrolls, its state as cards at its top.
- **820 × 1180** is the upright fold's composition in a tall window: the bottom bar, two panes, the 248 px list beside a
  572 px thread.
- **1024 × 1366** is the landscape tablet's composition in a tall window: the 284 px list beside a 674 px thread, the
  folder list a drawer, and the height given to more rows and more thread rather than to taller dialogs.

A mouse in the tablet band — **960 × 1040**, half a desktop screen, and **1080 × 1800**, a monitor on its side — gets
the mouse's sizes and panels: the 58 px folder strip, the side rail
upright as well as on its side, and below 1020 the same 248 px list a finger gets. **1180 × 820** — the tablet on its side at the edge of the desktop band — is the
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
| The bottom navigation | Wherever `navBottom` holds. On the phone five places — *Discover*, *Agent*, *Mail*, the bell and *More* — spread across the bar; on the upright fold and small tablet eight — the six spaces, the bell and *More* — each at most 120 px wide and centred together. Each is a 56 × 32 px pill behind its glyph (`--nav-mark`) that marks the current one, and a 12 px label beneath — the tile around it is not filled. The bar pads its foot by `env(safe-area-inset-bottom)`, and what floats over the foot of the screen stands above it wherever it stands — the notification centre and a whole-screen window — the source, and the markup on one pane — stop at its top edge, and the *Back to the conversation* pill sits 78 px up rather than 22 |
| Controls the bar has no place for | Stand in the *More* sheet, below the spaces and a rule: *Refresh*, then *Settings*. Each row of the sheet is 52 px, and the sheet is never wider than 560 px, centred over the bar on the fold and the tablet. The status menu its first row opens is a sheet wherever the bar is. Nothing floats over the content of a screen except the one action that screen is for |
| A prompt | Fits one line at the width it is drawn at, and ends in an ellipsis where it cannot: the phone and the narrow list panes take the short form (*Search mail…*, *Ask your mail…*, *Tell me what to do…*, *Write what this is about…*) |
| Keys | No control names a key under `touch` — *Send*, and the composer's foot without *Ctrl+Enter sends* — because the screen has no keyboard to press it on |
| One choice among several | A segmented control — `--rail` track, the chosen segment raised on `--panel` — rather than loose bordered buttons: People's *Own* and *Collected*, the calendar's four views |
| Swiping between tabs | A swipe across what a strip of tabs or a segmented control shows steps to the tab beside it — left to the next one on the right, right to the one before — and does nothing past either end: People's *Own* and *Collected* across the list (`peopleSwipe`), the calendar's four views across its body (`calSwipe`), and Settings' sections across the section on one pane (`settingsSwipe`). It answers to a row's swipe numbers — counted at 96 px across, given up as a scroll once it drifts 26 px and more down than across — and acts the moment it counts, and once it engages at 14 px it calls off a row's long press, so one finger is still one gesture. A finger that lands in a field — a name, a number of seconds — is dragging the field's caret or a selection, and takes no swipe. The tabs above it stay and are pressed as before. A strip of open things — Mail's open tabs, the Agent's conversations — takes no swipe, because bringing one forward opens it, and neither does a choice that sets something, such as *Message view* |
| Pulling the list to refresh | The Mail message list pulled down from its top asks for the one refresh (`refreshAll`), as *Refresh* does — the third way in beside the rail's control and the *More* sheet's row, which stay as its single-pointer alternative. It starts only on a list already at its first row, and the touch is decided once the finger has travelled 14 px: down and more down than across is a pull, which calls off the row's swipe and its long press, so one finger is still one gesture; up is a scroll, and more across than down is the row's swipe, and that touch takes no pull. A disc of 40 px — `--panel`, a `--line` border, the `refresh` glyph in `--text2` — comes down from above the list with resistance, its top edge at half the finger's travel less its own 40 px and never lower than 56 px below the list's top, its glyph turning with the pull and its opacity growing as it clears the list's top (`listPullShown`, `listPullStyle`). Past 128 px of the finger's travel its line and its glyph take the accent (`--accent-line`, `--accent-d`), and a release there refreshes; released short of it the disc springs back above the list in 260 ms on `cubic-bezier(.32,.72,0,1)`, the notification gesture's spring, and nothing is asked. While the refresh is under way (`listPullSettled` with `refreshBusy`) the disc waits 16 px below the list's top and its glyph pulses (`mfpulse`, 1 s) as the *Refresh* glyph does, rather than spinning, and it goes when the re-read lands. A pull while a refresh is already under way shows that waiting disc and asks nothing more. The list keeps its rows while the read is on the wire and is `overscroll-behavior: contain`, so a browser's own pull-to-refresh never reloads the page under it. Under `prefers-reduced-motion` the disc does not travel and does not spring: it stands at its waiting place from the moment the pull is decided and goes at once. It is drawn under `aria-hidden`, holds nothing focusable, and announces nothing a press of *Refresh* would not. Under a fine pointer nothing about the list changes, and no other list — search results, the notification centre, the Outbox, the Agent's conversations — takes the pull |
| Symbols alone | Where a glyph names the action beyond doubt — attach, archive, delete, close, back, and the thread's reply, forward and flag — the phone draws the symbol alone in its 44 box, and the name stays its title and its accessible name. An action no glyph names keeps its words for as long as the strip it stands in has room for them, and narrows to its symbol only once the strip does not — measured rather than decided at a width. Either way its name is one hold away, as every symbol's is: a finger held on it for 420 ms shows it in the bubble without pressing it (*A control's name* under § *What every artboard shares*). *Select all* is words at every width, since no glyph names it |
| A sheet | Whatever rises from the bottom under a finger — the *More* sheet, the thread state, the phone's dialogs, Settings on one pane — closes by being pulled down as well as by its *X*, and shows where to take hold: a 38 × 4 px `--border2` grabber centred in a 20 px band at its top (`sheetGrabber`, `settingsGrabber`). The pull takes over only once the sheet's content is scrolled to its top; let go past a third of its height, or flung down, and it closes as its *X* would, otherwise it springs back. A drawer closes the same way, pushed back toward the edge it came from |
| Folded away | What a wider window stands in place but a narrow one folds away and slides out — the mail folders, the Agent's history (`historyScrimShown`), the list's filters, a drawer, a sheet — closes on its *X* and on a press anywhere beside it, which is a press on the scrim behind it or, for the filters band on the phone, anywhere outside the band and its control (`onDocDown`). A scrim takes the press it closes on, so nothing under it is pressed too |
| A long press | A finger held on a row opens the row's own menu and nothing closes it but a choice, a press beside it, Escape or the back gesture — the platform's own menu gesture that follows the same press is that press, not a second one. A finger held on a control inside the row shows that control's name instead, and the row's menu does not open as well: one held finger is one surface |
| A row's hidden facts | What a row draws only for a mouse to rest on — the instant behind a notification's relative age, the address behind an account's name, a folder's account, whole path and count — is said again under its menu's header (`ctxMenuDetail`), in `--faint` at 11 px, because the hold a finger would ask with is the hold that opens that menu. A row that opens no menu at all — the unified *All accounts* group and its folders — carries no title saying more than it draws |
| The fold's split | Two panes in less than 1020 px keep the list a list beside the thread, under a finger and a mouse alike: 248 px wide, never folded, widened or narrowed only by dragging its boundary, its rows drawing less where one line cannot carry everything (`listCompact`) — § *The panes* under Mail |
| A pane boundary | Every line a reader drags to give one pane more room — the folder column's (`showSideResizer`), the list's (`showResizer`), the Agent history's (`showHistoryResizer`) — follows a finger as it follows a mouse, and stands wherever its two panes stand side by side under either pointer. Under a mouse it is the bare 5 px `--line`, lit in `--accent` (`--accent-line` for the list's) while the mouse rests on it; under a finger it is never lit (`boundaryHover`, `boundaryListHover`) — a finger has no hover, and a lit line would stay lit after the drag — and it is the same line with a 25 px reach along its whole length and, in its middle, a grip to take hold of (`boundaryGrip`): a 12 × 44 px pill on `--panel` with a `--border2` edge and a 2 × 18 px `--muted` stroke, its own target 44 × 44. The page does not scroll under a finger dragging it. Its name — *Drag to widen the folder pane*, *Drag to resize the list (double-click to reset)*, *Drag to resize the history (double-click to reset)* — is a mouse's hover alone (`data-tip-hover`), because a finger's hold on a boundary is the drag and one held finger opens one surface |
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
| A control's name | every `[title]` but a frame's, written as the bubble `.mf-name-tip` by `design.js`; `data-tip-hover` | Every artboard writes a control's name as its `title`, and no touch browser draws a title, so the name is drawn in the same dark bubble as a chip's sentence instead, fixed to the window so a scrolling strip never cuts it, one at a time. A mouse rests on the control for 400 ms; a key focuses it; a finger holds it for 420 ms — the row around it then opens no menu and starts no swipe, the platform's own menu does not appear, and the tap the hold ends in does not press the control. A control that cannot act (`aria-disabled`) shows its reason on a tap. It goes at the next press anywhere, on scroll, on Escape, when the finger starts travelling, and 4 s after a finger brought it up. An element inside a row whose own hold opens a menu — an account or folder header, the twist that opens one, a sender's name, a flag mark — carries `data-tip-hover`: a mouse still reads it, and a finger reads it another way — a fact under that menu's header (*A row's hidden facts*), the twist's sentence in the turn of its chevron, the flag's in the menu's own *Remove the flag* |

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
| `sheet` | from 100 % below its place, slow, ease-sheet | back down 100 %, slow, ease-in | the thread actions sheet (`threadSheetOpen`), the *More* sheet (`moreOpen`), and on the phone every full-screen surface: compose, the form dialogs, Settings on one pane; the AI reading wherever it fills the screen, the fold included; the message source (`sourceOpen`) below 1180 px, where it fills the screen |
| `drawer` | from 100 % to the left, slow, ease-sheet | back out to the left, slow, ease-in | the folder list where it is a drawer (`showSide` under `drawer`) |
| `pop` | from opacity 0 at 96 % scale, fast, ease-out | to opacity 0 at 96 %, fast, ease-in | the context menu (`ctxMenuOpen`), the user menu (`userMenuOpen`), the return pill (`convReturnShown`), every short question at every width — discarding a draft (`closeAskOpen`), the send warnings (`sendAskOpen`), deleting a folder, a conversation or an item (`delFolderOpen`, `deleteConvAskOpen`, `confirmDelOpen`), closing every tab (`closeAllAsk`), aborting a running operation (`cancelAskOpen`), marking spam not spam (`notSpamAskOpen`), showing the original message (`htmlAskOpen`), the message source on the desktop (`sourceOpen`), following a link that leaves MailFathom (`linkAskOpen`), the composer's changed-context mark (`composeCtxChanged`), which arrives this way and goes at once rather than leaving — and off the phone every dialog (new folder, new task, new event, new contact, reminder, move, the spam chooser, AI summary, event) and Settings on two panes |
| `rise` | from opacity 0, 18 px below, base, ease-out | back down 18 px to opacity 0, base, ease-in | the composer where it docks over the screen rather than filling a pane, and the reply drafter (`replyDock`) |
| `fade` | from opacity 0, base, ease-out | to opacity 0, base, ease-in | the composer in a pane, the list replacing its skeleton (`listReady`), the composer body on restore (`composeExpanded`), the message source replacing its skeleton (`sourceLoaded`) |
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
| Resizer between panes | `showResizer` | `!singlePane` — a mouse or a finger drags the list between 232 and 560 px, double-click returns it to its band's width (`st.listW` back to `null`); a pane boundary, as *A pane boundary* under § *What every touch composition holds to* draws it |
| The list below 1020 | `listW` | `!singlePane && narrow` — the list stands at 248 px beside the thread whether or not one is open, and never folds; its boundary still drags it between 232 and 560 px |
| A compact row | `listCompact` / `t.iconsInline` | the list narrower than 270 px (`listW < 270`) — read from the list's own width, so a list dragged wider draws everything again. The row keeps the one height every row has, because the client's list is windowed at one row height and a row that grew where it had something to carry would put every row below it out of place. Its first line keeps the avatar, the sender, the sender mark, the receipt mark, the attachment mark, the flag and the time, and the sender gives way to an ellipsis rather than pushing the time out; the subject and the AI line stand under it as before. The message count and the *AI summary* button are not drawn — the thread names its own count when opened, and the summary stays one press away in the row's menu and the thread |
| A colleague's row | `t.fromMember` / `t.fromDotStyle` | a message from a member of the reader's organization carries the sender's presence dot at the avatar's lower right (8 px, 11 on the phone), ringed in `--panel`; the opened message's head does not. Outside an organization no row does |
| An unread row | `t.isUnread` / `fromStyle` / `subjStyle` | the sender and the subject in weight 700 and `--text`, a read row's in 400 and `--text2` — weight and ink together, as every mail client draws it, because 600 against 400 in one colour read as the same row at a glance; the row carries no dot |
| The attachment mark | `t.attShown` / `t.attLabel` | a row whose message carries an attachment draws a 16 px `attach_file` glyph in `--muted` among the markers on its first line, before the flag, as the client draws it; its name — *1 attachment*, *N attachments* — is the glyph's title and is read aloud from a visually hidden label. A row whose message carries none draws nothing in its place. Where the first line runs short, the sender's organization gives way to an ellipsis first and the sender after it, so the markers and the time are never pushed out |
| Side list expanded | `sideExpanded` | not collapsed; collapsed is 58 px, expanded is 210 px (240 under `wide`) and draggable to 420. It starts collapsed below 1180 (`compact`) and expanded above |
| Side list show/hide | `sideToggleShown` / `toggleSide` | `!drawer` on Mail — in the mail toolbar's lead, a 32 px icon button (44 px on touch) reading `left_panel_close` while the list is open and `left_panel_open` while it is collapsed, titled *Hide folders and filters* and *Show folders and filters*, and set apart from *New message* by a 1 px rule with 6 px before it and 8 px after, so a press meant for one never lands on the other. It is never inside the list, so a long tree cannot scroll it away |
| Side list scrolls | `sideScrollStyle` | the AI filters and every account scroll together inside the panel once they are taller than it, docked or as a drawer; the panel itself does not move |
| AI filters | `aiFilters` | the first group in the side list, above every account, under an `auto_awesome` *AI filters* header — rows the same size as folder rows; a collapsed side list draws the glyph alone |
| AI filters collapsed | `aiFiltersOpen` / `toggleAiFilters` | the header, exactly as an account header collapses its folders — `expand_more` against `chevron_right`, titled *Collapse AI filters* and *Show AI filters*; open by default (`st.aiFiltersOpen !== false`) |
| Side list resizer | `showSideResizer` | `!drawer && !sideCollapsed` — a pane boundary; a drag widens the folder pane between 210 and 420 px, double-click returns it to 210 |
| Side list as a drawer | `showSide` / `sideScrim` | `drawer && drawerOpen` — its head reads *Folders and filters* beside a close control (`sideDrawerHead`) |
| View controls in the toolbar | `viewLeadShown` / `drawerOpenerShown` | Mail with the toolbar standing. The toolbar leads with the folder pane's own control: the side list show/hide where the pane stands beside the list, *Folders and filters* (`menu`) where it is a drawer (`drawer`, the tablet and the fold). Each is the side list toggle's size, and the same 1 px rule (6 px before, 8 px after) sets them apart from *New message* |
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

### Listed by conversation

An experiment: the list can be read by conversation rather than by message, each row recognised by a title a model
wrote for the whole conversation and by the opening of its newest message.

| State | Flag | Reveal |
|---|---|---|
| The mode's control | `listModeLabel` / `toggleConvList` | one large button at the top of the side list, above the AI filters and the accounts, outside its scroll so it never scrolls away — docked and as a drawer alike, so on the phone and the fold it is the drawer's first row. It names the mode the list is in: `mail` and *Messages*, or `forum` and *Threads*, the glyph in `--accent-d`, the name at 15 px 600, and a `swap_vert` glyph in `--muted` at its end saying a press turns it to the other; 44 px tall (48 under a finger), full width, `--panel` inside a `--line` border at 10 px radius. Its title says both: *Messages - switch to threads*, *Threads - switch to single messages*. Collapsed to 58 px it is the glyph alone in a 40 px square. Messages by default, and the session's alone |
| Listed by conversation | `st.convMail` / `t.convShown` | the row's first line names who took part — first names, *me* for the user, one full name where only one person wrote — beside the sender mark, the flag, the message count and the time — no AI summary control, which the opened thread carries on its newest received message; under it the conversation's topic; under that its standing, where it has one; then two lines at most of its newest message, whatever stands above it. The per-message AI line (`showAi`) is not drawn |
| A title written by AI | `t.convTitled` | the title (`conversationTitles` in `data/prototype/threads.js`) is two parts a model writes: a **topic**, what the conversation is about, set early and kept as replies arrive so a new message never changes what the row means; and a **standing**, where it is now, written again with each new message. The topic stands after a 15 px `auto_awesome` glyph in `--muted`, read aloud as *AI title:*, 700 in `--text` while unread and 600 once read, on one line ending in an ellipsis. The topic and its standing — or the line holding the standing back — stand together in a light frame, so what a model wrote is never read as the sender's words: a 1 px `--accent-line` border at 8 px radius, `--accent-soft` at 40 % over the row's own background, 6 px above and 7 px below the words, 8 px before and 10 px after them, 2 px above the frame and 3 px under it. A row with no title draws no frame |
| Where it stands | `t.convStandingShown` | the standing on its own line under the topic, 13 px 400 in `--text2`, one line ending in an ellipsis — *Sign by 28.08 · CPI cap still open* — set in by 21 px so it starts where the topic's words do, after the glyph: the topic and the standing read as the one AI title, apart from the message under them. A title with no standing draws no line: nothing is left open (`travel1`, `devops1`) |
| The standing held back | `t.convStandingStale` | the thread carries `stateStale` — a message joined or left it after it was read. The topic stays, framed as ever; in the standing's place, set in as the standing is, a 13 px `--faint` line with an `hourglass_top` glyph reads *Updating with the new message…*: the standing is not ready yet rather than gone, and the stale one is never shown as if it were current. Reached through *Refresh*, whose second step marks the thread it changes |
| An unread thread opened | `st.newOnOpen` / `firstNew` | picking an unread thread from the list reads the whole of it at once, and what arrived since it was last read (`newInThread` in `data/prototype/threads.js`, counted back from the newest received message) opens unfolded: every message from the first new one to the newest stands open, the user's own replies among them included as context, and only what was read before folds behind the pill, *Show N earlier messages*. Where every message is new there is no pill. No entry into the middle of the thread (`piotr`'s) — the new messages are where it opens |
| Opened from the list, by thread | `isActive` | read by thread, the row was the conversation, so *Opened from the list* always marks the thread's newest message, whichever folder holds it and the user's own reply included; it stands wherever more than that one message is open — the new messages of an unread thread, or every message of a short one — and nowhere when only the newest stands open below the pill |
| A new message | `m.newShown` | each received message among the new ones carries *New* beside its sender: 11 px 600 in `--onaccent` on `--accent`, 6 px radius. The user's own messages never do. It is shown once: the thread is read the moment it opens, so the mark stays while that thread stays open and is gone once another thread is opened, and the thread opens folded as any read thread from then on. A thread opened from anywhere but the list — a notification, a citation, a tab — carries none |
| No title yet | `!t.convTitled` | the conversation's subject in the title's place, with no glyph: a conversation not yet given one (`jacek`, `bank1`), every spam conversation, which is never given one, and every conversation while *AI hints* is off |
| Whose message the snippet is | `t.convWho` | the conversation's newest message, whichever folder holds it, always prefixed with who wrote it at 600 in `--text2`: *You:* where the user sent it, the sender's whole name where they are the only one in the conversation (*Business Bank:*), and their first name where the first line names several (*Anna:*). It is set off as a quotation of the message rather than a line of the title: 2 px above it, and a 2 px `--border2` rule down its left edge with 8 px before the words |
| Ending in the user's reply | `sentLast` / `convMsgsFor` | a thread whose newest message is the user's own (`north1`, `fabrikam1` in `data/prototype/threads.js`, drawn only when the list is read by thread): the snippet reads *You:*, the row's time is that reply's, and the opened thread ends in it |
| What came back, on the row | `t.rcptShown` | where the newest message is the user's own, the row draws its receipt mark — the glyph alone, among the markers before the flag, opening the same details as a Sent row's — from what came back for that reply (`receipts.js`): *Not delivered* on `north1`, *Delayed* on `fabrikam1`. A thread whose newest message is received draws none, whatever came back for an earlier reply; a reply nothing came back for yet draws none either, as on a Sent row |
| The attachment mark, by thread | `t.attShown` / `t.attLabel` | the same `attach_file` glyph as a message row, wherever **any** message in the thread carries an attachment, however far back — named *Attachments in this thread*, with no count, since the thread list says only whether any message carries one. `piotr` is the case that tells the two apart: its newest message carries none, so its message row draws no mark, while its thread row does for the status report earlier in it |
| No folders, only accounts | `convAcc` / `sideGroups` | a conversation gathers its messages from every folder, Spam and Trash included, so the side list draws no folder: one row for *All accounts* (`all_inbox`) and one per account (`account_circle`, `group` for a shared mailbox), no account headers, no folder or account menu, and no drop target. The picked row is on `--accent-soft` in `--accent-d`; collapsed, each row is its glyph under the account's dot. *All accounts* is picked first, and the pick is the session's |
| Which conversations are listed | `listedThreads` / `convOnlySpamOrTrash` | every conversation of the picked account with at least one message that is neither spam nor in Trash. Archive is an ordinary folder, so an archived conversation stays. A conversation whose every message is spam — judged spam wherever it sits, or sitting in Spam — or in Trash, or some of each, is left out, and with it every folded spam row. The folder last picked in message mode decides nothing here: no Spam badges, no Trash behaviour on delete, no recipient rows of Sent and Drafts |

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
| The Outbox open | `outboxOn` | the mark pressed (`openOutbox`). It takes the list's place (`outboxListShown`) and the reading pane's (`outboxPaneShown`); the thread is not drawn while it is open. Picking a folder, or a thread opened from anywhere else (`leaveOutbox`), closes it |
| Its head | `outboxHeadStyle` | *Outbox* as the title, the account under it; on the phone the title is 20 px semibold in the 56 px bar, the folders button before it, as everywhere in Mail |
| Read-only, said | `outboxReadOnlyStyle` | one `lock` line under the head, on `--rail`, at every composition: *Read-only. What has not left yet waits here - nothing in it can be flagged, moved or deleted.* The preview ends on the same lock line. Missing actions read as the folder's nature, not a fault |
| A row | `outboxRows` | newest first: *To* and the address of the first recipient in `To` — else `Cc`, else `Bcc` — with *+N* for the rest, the time it was handed over, the subject, and its state on a line of its own (`OB_STAGE[…].line`), with how many attempts it has taken wherever one was made. *Outcome unknown* and *Refused* are at weight 600; a sending row's glyph pulses. **No mail act on a row**: no checkbox, no selection, no AI summary, no hover actions, no drag, no swipe on the phone, no hold menu, and no row is a drop target. Pressing one opens it; on one pane it pushes the preview as a thread would |
| The toolbar | `toolbarActions` | keeps the folder toggle and *New message* and drops every mail act — *Reply*, *Reply all*, *Forward*, *Archive*, *Delete*, *Flag*, *Unread*, *Move* and the panels toggle |
| Nothing open | `outboxNothingShown` | two panes and nothing picked: the *Nothing open* layout with the `outbox` glyph and *Pick a message to read it. Outbox is read-only: what is here can be read, and nothing can be replied to, moved or deleted.* — or, once the Outbox is empty, *Outbox is empty, so there is nothing to read here. What went out is in Sent.* |
| The preview | `outboxEntryShown` | the subject (with *Back* on one pane), the state box with its decisions, then `outboxFacts` — *From* (the address it is sent as), every recipient by address under its own header — the address being all a send says of who it is for — with *Not taken* in `--err-text` beside one the server refused, since it gives no reason per recipient, *Handed*, *Leaves* where a time was named, *Tried* where an attempt was made — then each attachment by name, type and size (`outboxAttachments`), which opens nothing, then the message itself. Nothing in it is a mail act |
| The preview not read | — | the preview could not be read, or the machine is offline: the reading pane's *Nothing open* layout (`m_pane`), with *Back* above it on one pane — a `warning` glyph, *Not read* and the reason, with *Try again* where the server could not be reached; or a `cloud_off` glyph, *Offline*, and that it opens on its own once the network is back |
| Waiting | `outboxCanCancel` | a neutral box with `schedule`: *Waiting to send* — *Handed over at 10:44 and not sent yet. It goes out as soon as the mail server takes it.* — and *Cancel sending*, which withdraws it (`cancelOutboxEntry`): the row goes and a success toast says *Nothing was sent to …* |
| Scheduled | `outboxCanCancel` | a neutral box with `schedule_send`: *Sends Mon 08:00* — written to leave at a time somebody named, and *Cancel sending* until then. The composer's *Send later* is how one is made (§ *Composing*, *Scheduled*), and its toast's *Show in Outbox* opens the entry |
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
| Delete confirmation | `delFolderOpen` / `delFolderText` | *Delete folder* — the folder, what is nested in it, and the mail stored there, by the account's folder-delete setting, computed from the state rather than one sentence |

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

The delete confirmation states what goes with the folder, and follows the account's folder-delete
setting, which the administrator sets (`folderDelete` on the account). On the mail server too, as
*Karolina Kowalska*'s does: *The folder* — or
*The folder and the folder nested inside it*, or *…and the N folders nested inside it* — *will be
deleted in `<mailbox>` and on your mail server.*, then the mail: *The conversation stored in it* — or
*All N conversations…* — *will be deleted on both sides - there will be nothing left to restore.*, or
*It holds no mail, and deleting it cannot be undone.* where it holds none. In MailFathom only:
*… will leave `<mailbox>` here and stay on your mail server with the mail in it. MailFathom stops
reading it, and nothing on the server is touched.*, as *Karolina - private*'s does.

### The per-message AI reading

| State | Flag | Reveal |
|---|---|---|
| AI summary | `aiSumOpen` | the `auto_awesome` control on a message row (a thread row draws none), *AI summary* at the top of that row's context menu, or — however the list is read — the same control in the head of the opened thread's newest received message, before its attachment count and the original-message control, titled *AI summary*, 26 px (44 under a finger) in `--muted`, turning `--accent-d` on `--accent-soft` under the pointer. Never on the user's own messages and never on spam |
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
| Where a message is kept | `m.place` | at the foot of every open message, in either mode, held close under its last block with no rule above it, so it never reads as the head of the message below: a 12 px `--faint` line with a `folder_open` glyph naming its folder and its account's name — *Inbox · Nordwind · work*. The user's own message reads *Sent*; a received one reads where the thread was moved, archived, deleted, or judged spam, and otherwise the folder the list stands for, or *Inbox* read by thread. The account is the one the list was opened under, or the thread's own under *All accounts* and by thread. A folded message draws none |
| The subject on one pane | `threadHeadRowStyle` / `subjectStyle` | `singlePane` — the back button and the three actions share the first row, and the subject takes a row of its own under them at 18 px, two lines before it ends in an ellipsis; scrolled (`thCollapsed`) it folds to one line at 16 px |
| The head's actions under a finger | `threadActions` | `!singlePane && touch` draws none — the toolbar above carries *Reply*, *Forward*, *Flag*, *Mark spam* and *More actions*, so the head is the subject alone |
| *More actions* | `threadActions` / `toolbarActions` / `openMoreMenu` | the three-dot `more_vert` control, titled *More actions*, after the spam control in the thread's head — after *Mark spam*, and after *Not spam* on a message marked spam — drawn as its glyph alone at every width, in the size and tone of the glyphs beside it. Where the head draws no actions (two panes under a finger), or is not drawn at all (two panes with the panels hidden), the toolbar carries it instead, after its spam control and as its glyph alone, so every composition reaches it exactly once. A press opens the context menu (`ctxMenuOpen`, `pop`) under the control with its end edge on the control's, headed by the message's subject — or, opened from the toolbar, which holds no subject, by *More actions* — holding one item: *Show source* with the `data_object` glyph. On the phone the menu stands in the middle of the screen as every context menu there does. It closes on a press outside, on `Esc`, and after the choice; the arrow keys walk its items, `Enter` takes one, and closing it puts focus back on the control |
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
| Remote content removed | `m.remoteBlocked` | a message that is not the user's own and carries remote references, until they are loaded — never one marked spam, whose remote content stays blocked with no card (§ *Spam*, *Read-only spam*). A card at the top of the body says *This message asked to fetch content from another server. That was removed, so opening it told the sender nothing.*, then *Removed references: N*, then *Loading them tells the sender that this message was opened. This choice applies to this message only and is not remembered anywhere.*, over one accent button, *Load images from the sender*, 44 px tall on touch. The count comes from `m.remoteRefs` where a message sets one, and none does; otherwise a hash of sender and time gives about a third of the messages none and the rest between 3 and 18. While *Load images automatically* is on in Settings, *Application*, the card is not drawn at all: the message opens with its images, and a message marked spam still keeps them blocked with no card |
| Remote content loaded | `m.remoteLoaded` / `m.remoteLoadedText` | pressing that button — the card gives way to one muted line with an `image` glyph, *Images from the sender are loaded for this message.* The choice is kept per message in `st.remoteLoaded` for the session and nowhere else. With *Load images automatically* on, every such message opens on this line instead of the card, reading *Images from the sender load automatically - Settings, Application.* |
| A change the mailbox did not take | `m.changeShown` | a move, archive, delete, flag or read mark MailFathom stopped trying (`CHANGE_NOTICES` in `data/prototype/workspace.js`, fate `exhausted`), on the message it was asked for — the thread's newest — at the top of its body, above the remote-content card: the Outbox refused box's shape, `--err` with `sync_problem`, *Not moved - your mailbox did not take this change* over *The mail server would not take it, so this message is shown where the server holds it.*, then *Ask again* (accent) and *Let it go* (quiet), 44 px tall under a finger. A move reads *Move again…* instead and asks for the folder once more, because the change record names the act and not where it was going. The title names the act: *Not moved*, *Not archived*, *Not deleted*, *Not marked read*, *Not marked unread*, *Not flagged*, *Not unflagged*, *Not copied*, *Tags not changed* (`CHANGE_ACT`). *Not archived* is a change this tab followed. One known only from its notification is named by the eight acts the server records, where archiving is a move and reads *Not moved*. Nothing local changed on a mirrored account, so the message simply stands where the server holds it. Reached by opening the change notification, which leads to `travel1` |
| A change whose outcome is unknown | `m.changeShown` (fate `unknown`) | the same box in `--warn` with `help`: *Flagging may not have happened* — *Moving*, *Archiving*, *Deleting*, *Marking read*, *Marking unread*, *Flagging*, *Unflagging* — over *The mail server never answered, so nobody knows whether it took the change. The message is shown as the server last reported it.*, with the same two buttons. Reached by opening the `devops1` thread, *Nightly report - build #2418* |
| The change decided | `changeDecided` / `decideChange` | *Ask again* or *Let it go*, on the message or on its notification row: the notice goes, the durable row is marked read and loses its pills, so one failure is never shown as two. *Ask again* raises a neutral toast titled *Asked again*, *MailFathom is telling your mailbox again.*; *Let it go* does nothing else |
| Reveal the earlier messages | `showEarlier` in the thread head | *Show N earlier messages* / *Hide earlier messages*, in the pane already open |
| Expand every message | `showExpandAll` | **constant `false`** — the source hands the screen no per-message collapse at all, so a revealed message is drawn in full. The one exception is a report that came back as mail, folded to one line until pressed (§ *The receipt mark*, *Folded*) |
| The reader's own footer, folded | `m.footerShown` / `m.footerOpen` | a message the reader sent that names the footer MailFathom appended to it (`footer: true` in `data/prototype/threads.js`, and the reader's *Thank you. Still open: …* reply `msgsFor` writes into every thread whose history is not written out; the lines per account in `footers` in `data/prototype/workspace.js`), in Sent and read by thread alike: the footer stands as one quiet line under the message's own words — `chevron_right` and *Your footer is hidden* at 13 px in `--muted`, with no rule or box around it, 28 px tall under a mouse and 44 under a finger. A press opens the footer under the line as it went out, with `disclose` — the name at 14 px 600 in `--text`, every other line at 14 px in `--text2`, the fine print at 12 px in `--muted` — and the line reads `expand_more` and *Hide your footer*; a second press folds it again. Open or folded is kept per message for the session. Reached on *Contract addendum - signatures* after *Show N earlier messages* (Karolina's message of 26.08), on the user's own reply that ends *Northwind reporting module quote* read by thread, and on the generated *Still open* reply of any other thread of several messages once its earlier messages are shown |
| A message naming no footer | `m.footerShown` false | somebody else's message, and one of the reader's own sent before the footer existed, is drawn whole, its typed sign-off included — Karolina's message of 21.08 in *Contract addendum - signatures* |
| The conversation a message quoted, folded | `m.quotedShown` / `m.quotedOpen` | a message carrying the conversation it quoted (`quoted` in `data/prototype/threads.js`) folds it to a line of the same shape under its words and under the folded footer, never above it: *The conversation this message quoted*, `chevron_right` folded and `expand_more` open. Open, the quoted text stands under it at 14 px in `--text2` behind a 2 px `--border2` rule. Reached on Karolina's message of 26.08 in *Contract addendum - signatures* |
| What the original carries | `htmlFallback(original, …)` | *Show the original message* on a message with a folded footer or quoted conversation draws both in full: the original is what went out, and nothing in it folds |

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
| Selection bar | `selBarShown` / `selClear` / `selAll` | the same, and it appears on five screens, not only mail — Mail, Tasks, Calendar, People, and the Agent's conversation list — laid out as Mail's everywhere: `close` with *N selected* at the start (`selClear`, *Cancel selection*), an `--accent-line` rule 6 px either side, the actions, and *Select all* at the end (`selAll`, every row the screen lists). On People the actions follow the segment: *Write* and *Delete* under *Own* — *Delete* takes only the contacts the reader wrote down, says how many of the organization's selected beside them stay, and is not offered while only the organization's are selected — *Write* and *Copy addresses* under *Colleagues*, *Write* and *Remove* (from collected) under *Collected*. People has no *Export*, in the bar or the toolbar |
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
| A folded row | `t.folded` | a spam row in any list but the Spam folder — every other folder, *All accounts*, and search results (`mail-search.html`): one line in place of the row, a `report` glyph in `--faint` where the avatar stood, then the sender, its sender mark (§ *The sender mark*) and the subject at 13 px in `--muted`, both ending in an ellipsis, and the time. The AI line, the unread weight and the *AI summary* control go. As tall as every other row of its list, the one line centred in it: the client's list is windowed at one row height, so a shorter row is one it cannot place, and the change of shape is in the line rather than in the height. No badge: the change of shape is the cue that does not rest on colour, the verdict is in the row's accessible name (`t.spamName`, a visually hidden *Spam: Phishing, judged by a model.*), and its kind joins the hold menu's header (`ctxMenuDetail`, *Payroll Team - Phishing spam - Sender not recognized*, the sender mark's name following the kind), since a finger has no hover to ask with. Strikethrough was rejected: it reads as deleted, and it crosses out the sender and subject a person needs to recognise a message judged wrongly |
| No AI reading of spam | `t.aiBtnShown` / `threadSpam` | nothing that reads mail for the person reads spam, so a spam message offers no *AI summary* on its row or in its menu, no *Ask the agent*, and, opened, no thread-state bar or cards and no AI button over the thread |
| The opened message's warning | `m.spamWarn` | a spam message opened from any folder: a box above its body, drawn from that message's own verdict, so every spam message of a conversation carries its own. It always says the message is spam and a security risk, and who judged it — *A model judged this message spam and named it phishing. The kind is the model's judgement and can be wrong.*, *Your mail server judged this message spam.*, or *You marked this message spam and named it malware lure.* A dangerous kind takes the strong form: `--err-soft` inside an `--err` border, a `gpp_bad` glyph, the title in `--err-text` — *Phishing. This message is spam and a security risk.* — then, where the record names one, what a phishing message asks for (`request`: a password, payment details or a one-time code) and whom a phishing or business email compromise message is written to look like (`party`: a bank, a courier, an online service, a colleague, a manager or a supplier) — the closed values the deployment publishes, never the message's own words, so nothing it claims is repeated as fact (*It asks for a password, and it is written to look as if it came from an online service.*); a value naming nothing says nothing — and *Do not give it anything it asks for.* A quiet kind, or none, takes the calm form: `--sub` inside a `--line` border, a `report` glyph in `--muted`, *Scam. This message is spam and may be a security risk.* (no kind: *This message is spam and may be a security risk.*), and the judge. Both end, in `--muted`, on what the mark holds back (`SPAM_READ_ONLY`): *While it is marked spam it is read-only: its images are not loaded, its links and attachments do not open, and it cannot be answered or forwarded.* A message that is not spam shows nothing new |
| Read-only spam | `refuseSpam` | a message marked spam can be read, moved, flagged, archived and deleted, and nothing else. Its remote content stays blocked with no *Load images* card (`m.remoteBlocked` is false for spam). It keeps the simple view even where *Message view* is set to the original, and its *Show the original message* button stays where it is, and a press answers with a `warning` toast — *This message is marked spam* over *Its original does not open. Mark it not spam first.* — instead of the HTML confirmation. The thread's head draws *Not spam* and *More actions* and nothing else for it, and the toolbar *Not spam* in place of *Reply*, *Reply all* and *Forward*, beside its other acts; flagging an open spam message is the toolbar's, the row's menu, the hold menu and the selection strip's, which on a phone are the hold menu and the strip. *Reply* and *Forward* in the row's and the hold menu, and a phone's swipe to reply, answer the same way with *Spam cannot be answered.* or *Spam cannot be forwarded.*; an attachment's open, its download, and *Download all* with *Its attachments do not open.* or *Its attachments are not downloaded.* Nothing composes and nothing opens. Marking it not spam lifts all of it at once |
| A link in spam | `unlinked` | every link in a spam message's body drawn as words in `--text2`, never as an anchor, with where it would have gone beside it in the link note's form — *Keep my current password (a link to nordwind-support.example, not opened)* — so a lookalike address is readable and nothing can be pressed |
| The toggle | `report` / `report_off` | one control in five places: the row's menu and its hold menu (*Spam…*, or *Not spam…* where it will move the message and *Not spam* where it will not, before *Move…*), the selection strip (*Spam* or *Not spam*, its symbol alone where the strip has no room for words), and for the open message the toolbar and the thread's head (`threadActions`). There a message that is not spam carries *Mark spam* after its other acts — in `--err-text`, its `report` glyph and its word carrying it rather than the colour, the glyph alone wherever the head draws glyphs — and a message marked spam carries *Not spam* in the toolbar in place of *Reply*, *Reply all* and *Forward*, and in the head as its one action. It reads *Not spam* only while every message it acts on is spam, and pressed anywhere it is the same act: the chooser, or the confirmation where *Not spam* moves something |
| A mark that did not go through | the client's `useSpamMarks` | the deployment refused a mark, or never answered: the rows it was not recorded for are drawn as they were, and an `error` toast says *Nothing was marked* — or *Not every message was marked* where some of it was — over the reason, or over how many messages it was not recorded for. The prototype has no deployment to refuse it, so no press reaches it |
| A mixed selection refused | `toggleSpam` | the toggle pressed on a selection holding spam and other mail: nothing changes, the selection stays, and an `error` toast — the form `toasts.dc.html` draws for a failed operation — says *Nothing was marked* over *The selection mixes spam with other mail. Select only one of the two.* |
| The spam chooser | `spamAskOpen` / `spamPick` | *Spam…* pressed: *Mark 1 message as spam* or *Mark N messages as spam*, then *Pick what kind of spam it is.* (for many, *Pick what kind of spam they are. One kind applies to all N.*) and what happens to the message by its account's setting: *It moves to Spam.*, *It stays in the folder it is in.*, or for a selection on both kinds of account, *The K on an account that moves spam go to Spam, and the rest stay where they are.* Then the six kinds as a radio group (`spamKinds`) — each its name, the dangerous ones in `--err-text` behind a `warning` glyph, and one line saying what it covers — and *Cancel* beside *Mark as spam*. A kind picked draws its row on `--accent-soft` inside an `--accent-line` ring with its radio filled. *Mark as spam* stays `aria-disabled` until a kind is picked and says *Pick a kind of spam first* on a tap. One kind applies to every message; confirming marks them. A message on an account that moves spam leaves the list with the archive row's exit and lands in Spam, and the toast says *It is in Spam now.*; any other row stays, washes as an edit does, and folds, and the toast says *It stays where it is, folded out of the way.* The toast is a success one, *Marked as spam - N threads* over the kind and that line. Closing it — *Cancel*, its *X*, the scrim, back — changes nothing. 440 px off the phone and a full-screen sheet with its grabber on it, every row at least 52 px under a finger |
| A move into Spam | `spamAsk.moved` | a message moved into Spam — dropped on a Spam folder, or *Spam* in the move dialog, which now offers it in both accounts — is marked spam by the person with no kind, and the chooser opens as *Moved to Spam*: *It is in Spam now and marked spam. Pick what kind of spam it is, or close this to leave it with no kind.*, with *No kind* beside *Set the kind*. Confirming it names the kind, and the success toast reads *Kind set - N threads* over the kind. Closing it leaves the message moved and marked spam with no kind; a message already spam is moved and asked nothing. *Undo* on the move toast moves the message back and leaves it marked spam: the mark is recorded with the move, and a move out of Spam lifts no verdict, so it lands where it came from and folds there |
| A move out of Spam | `moveThreads` | moving a message out of the Spam folder does not mark it not spam: it lands still spam, and so folds where it lands. *Not spam* is the one way out of spam |
| The *Not spam* confirmation | `notSpamAskOpen` | *Not spam…* pressed where it moves something — a message sitting in Spam on an account that moves spam: a short question with the `report_off` glyph — *Mark this message not spam?* or *Mark N messages not spam?* — that says what happens: *It is marked not spam and moved to the Inbox.*, *N messages are marked not spam and moved to the Inbox.*, or for a selection where some of it stays, *N messages are marked not spam. K move from Spam to the Inbox, and the rest stay where they are.* then, as every confirmation does, how it is taken back — *Marking it spam again takes this back.* *Cancel* changes nothing; the confirm reads *Not spam, move to Inbox* |
| Landed in the Inbox | `confirmNotSpam` / `markNotSpam` | the confirmation accepted: the kind goes, the person's *not spam* is recorded, and every message that sat in Spam on an account that moves spam leaves the Spam list and lands in the Inbox as an ordinary row — unfolded, its unread weight back, the *AI summary* control back, and *Inbox* under it as any moved row names its folder. A message already outside Spam stays where it is and stops folding. A success toast says *Marked not spam - N threads* over *It is in the Inbox now.* — nothing more to do, so it carries no action |
| *Not spam* where nothing moves | `markNotSpam` | *Not spam* pressed where it moves nothing — a message outside Spam, or one in Spam on an account that does not move spam: no confirmation, it applies at once as *Spam* does. The mark goes, a folded row unfolds where it stands with an edit's wash, and a success toast says *Marked not spam - N threads* over *It stays where it is and no longer folds away.* |
| Left in Spam, not spam | `markNotSpam` | a message marked not spam inside the Spam folder of an account that does not move spam: it stays in that Spam list as an ordinary row — no badge, unread weight and *AI summary* back — until the person moves it, and the toast says *It stays in Spam until you move it, because this account does not move spam.* Reached from the personal account's Spam folder, *Not spam* on *Unclaimed funds held in your name* |

### The sender mark

Every received message carries one mark saying what is known about who sent it, and never more than that: a mark on
its row, a badge beside its sender once it is opened, and a details popup either of them opens. Three facts the
service publishes decide it (`data/prototype/sender-marks.js`, read by `senderRecord`): **sender authentication** —
who checked it, DKIM, SPF and DMARC, whether the domain in *From* was established as the author, and whether SPF alone
established it; **trust** — the author a contact somebody wrote down (the reader's own, or another user of this
mailbox's, ADR 0043), a contact of the account's organization, one of the owner's own addresses, or a domain of the
owner's own accounts on an account that opted into them, each only for an author tied to the message by the domain's
DMARC pass or an aligned DKIM signature, or by the author's own verified signature where those did not hold — never by
SPF alone — or a member of the account's organization, which the organization's signature proves whatever sender
authentication found (ADR 0042) and which is named before the others where it holds; whether the sender is **known** — on an
account that is not strict, an author whose authentication did not hold whose *From* address is in the reader's
contacts, the organization's contacts, one of the owner's own addresses, or at a domain of the owner's own accounts on
an account that opted into them, which says only that the address is one somebody wrote down and never that its domain
sent the message (ADR 0043); and the **signature** — S/MIME or OpenPGP,
whether it matches its content, whether its certificate or key is trusted here, and whether its signer is the sender. Authentication alone never earns a positive mark, because a lookalike
domain authenticates as readily as the real one — *Payroll Team* from `nordwlnd-hr.example` is authenticated and
neutral.

`markKeyOf` reads six rules in order and the first that holds is the mark: a signature that does not match its
content is a **warning**; trust is **Trusted**; a signature that verified, by the address it is from, is **Verified**,
whatever sender authentication says, because a forward or a mailing list breaks DKIM and SPF and leaves the signature
whole; a sender known by their address alone is **Known**, above a failure because the service reaches it under one
only where the domain publishes a policy that merely monitors (`p=none`); failed sender authentication is a **warning**; everything else — authenticated but not recognized, not
established, signed by somebody else, a signature not verifiable here — is **neutral**. Trusted outranks Verified
where both hold. No state is told apart by colour alone: each has its own glyph, and every non-neutral badge its word.

| Mark | Glyph | Tone | Badge word | Name (`aria-label` and `title`) |
|---|---|---|---|---|
| Warning, the signature | `gpp_bad` | `--err-text`; the badge on `--err-soft` inside an `--err` border | *Suspicious* | *Suspicious - the signature does not match* |
| Trusted | `verified_user` | `--ok-text`; the badge on `--ok-soft` | *Trusted* | *Trusted sender* |
| Verified | `signature` | `--accent-d`; the badge on `--accent-soft` | *Verified* | *Verified signature* |
| Known | `person` | `--muted`; the badge on `--sub` inside a `--line` border, the neutral tone with no check mark and no shield, so it never reads as Trusted | *Known* | *Known sender* |
| Warning, authentication | `gpp_bad` | as the signature warning | *Suspicious* | *Suspicious - sender authentication failed* |
| Neutral | `shield` | `--muted`, no ground | none | *Sender not recognized* |

| State | Flag | Reveal |
|---|---|---|
| The mark on a row | `t.markShown` / `t.markGlyph` | every received message's row, in every folder, *All accounts* and search results (`mail-search.html`): the glyph alone, straight after the sender, in its tone. 18 px in a 24 px target under a mouse; 20 px in a 44 × 44 target under a finger, the target taken from the row's padding so the row keeps its height. Pressing it opens the details and nothing else — it neither opens nor selects the message. It answers a pointer alone and is hidden from the accessibility tree, because a row is one option with nothing focusable inside it: the mark's name is read as part of the row's own name, and the keyboard reaches the details through the row's menu, whose last item is *Sender details* under the mark's glyph. It carries `data-tip-hover`, since the row's hold opens the row's menu, and its name joins that menu's header (`ctxMenuDetail`, *Anna Kowalska - Trusted sender*). Search results draw no menu, so there the keyboard reaches the details through the opened message's badge. Beside it the sender no longer pushes the time out: under a mouse the organisation ends in an ellipsis first and the sender at 60 % of the line, on the phone the sender alone |
| No mark on recipient rows | `recipientList` | Sent and Drafts, where a row stands for what was sent rather than who sent it, and the Outbox, which draws its own rows: no mark, and nothing of the sender mark added to the menu header. A received message opened from there still carries its badge. A *Sent* row carries the receipt mark instead where a report came back (§ *The receipt mark*) |
| The mark on a folded spam row | `t.folded` + `t.markShown` | a folded row keeps the mark after the sender, because a lookalike sender is exactly what somebody doubting a verdict wants to judge. The fold is still the cue that it is spam; the mark says only who sent it |
| The badge beside the sender | `m.markShown` / `m.markWord` | every opened message that is not the user's own, right after the sender's name, in place of the notice box the sender verdict used to draw under it: 22 px tall, a 6 px radius, the glyph at 15 px and the word in 12 px semibold; neutral is the glyph alone in a 22 px square with no ground. Under a finger a 44 px target around it, taken from the head's line height. A message from somebody else in the thread reads that person's own record (*Marta Nowak* in the `contoso` thread). The head's line now wraps, so on a narrow pane the sender and the badge stay whole and the attachment count, the original's button and the time drop under them, right-aligned |
| The details, anchored | `senderPopOpen` + `!senderPopSheet` | the mark or the badge pressed under a mouse: a 392 px panel under the mark, its left edge 8 px before it, kept 10 px inside the window; where fewer than 320 px are left below and more above, it opens above the mark instead. At most 560 px tall, its steps scrolling under a head that stays. It arrives as a dialog does (`pop`), takes focus, and closes on *Escape*, on its *X*, on a press anywhere outside it, on a second press of its own mark, and on back, handing focus back to the mark. A press on another mark moves it there. Asked from the row's menu there is no mark to stand under, so it opens centred in the window instead |
| The details, as a sheet | `senderPopOpen` + `senderPopSheet` | the same under a finger, at the phone, the fold and the tablet: a sheet from the bottom over the scrim, at most 600 px wide and centred, as tall as `threadSheetStyle` allows, with its grabber; it closes as the anchored one does and on a press on the scrim |
| What the details open with | `senderPopTitle` / `senderPopWhy` | a 36 px tile with the mark's glyph in its tone, the mark's name as the title, the sender's address under it, and one sentence on why (`senderWhy`) — *contoso.example authenticated this message, and a.kowalska@contoso.example is your contact.*; *company.example authenticated this message, and invoices@company.example is a contact of your organization.*; where the author's own signature rather than the domain's checks tied it, *Its signature verified, made by p.zielinski@company.example, the address it is from, and that address is a contact of another user of this mailbox.*; *nordwind.example authenticated this message, and k.kowalska@nordwind.example is one of your own addresses.*; *The sender is a member of your organization, Nordwind, and the organization's signature proves this message comes from this address.*, the same without *, Nordwind* where the organization has since been deleted; *It carries a signature that verified, made by p.zielinski@company.example, the address it is from. Sender authentication failed, which a forward or a mailing list often causes.*; *Its signature does not match its content: it was changed after it was signed, or the signature is forged.*; *bank.example did not authenticate this message, so it may not come from who it names.*; for Known one sentence naming where the address was found and that nothing confirmed its domain — *r.lis@northwind.example is your contact, but nothing confirmed that northwind.example sent this message.*, with *is a contact of another user of this mailbox* where another user's book holds it, and *is in your organization's contacts*, *is one of your own addresses* and *is at nordwind.example, a domain of your own accounts* for the other three sources — and where the domain's checks failed under a monitoring-only policy a second, *Its checks failed, and lawoffice.example publishes a monitoring-only policy.*; and for neutral whichever holds of *It is signed, but by …, not by the address it is from.*, *It is signed, but the signature is not verifiable here.*, *… authenticated this message, but this account does not recognize the sender.* and *Nothing established who sent this message.* Nothing in it says a message is safe |
| The steps | `senderPopGroups` (`senderSteps`) | every step in order, in three groups, each a card on `--sub`: *Sender authentication* — *Who checked*, *DKIM*, *SPF*, *DMARC*, *Author domain*; *Author domain* reads, where SPF alone established the author, *parcel-track.example is established as the author by SPF alone. SPF says only which server handed the message over, so it makes nobody trusted.*; *Trust* — *Trusted sender*, which for a contact reads *a.kowalska@contoso.example is your contact.*, *p.zielinski@company.example is a contact of another user of this mailbox.* — nothing else from that user's record, not its name, its note or whose it is — or *invoices@company.example is a contact of your organization.*, for one of the owner's own addresses *k.kowalska@nordwind.example is one of your own addresses.*, where nothing recognized an author tied to the message *This account does not recognize … : it is not one of your own addresses, and it is not among your contacts or your organization's.*, and for a member of the account's organization reads *A member of your organization, Nordwind. The organization's signature proves the message comes from a.bielska@company.example.*, and where that organization has since been deleted *A member of your organization.* with *; the organization has since been deleted, so its name is not shown.* ending the line, and passes whatever sender authentication found, the organization's signature proving the address itself (ADR 0042) — and for a Known sender it is skipped, *Skipped, because nothing confirmed that northwind.example sent this message.*, and a second step follows it, *Known sender*, passed, naming where the address was found and *It is known by that address alone.*; *Signature* — *Signed*, *Content unchanged*, then *Certificate authority*, *Validity and purpose*, *Not revoked* and *Signer is the sender* for S/MIME, or *Known key*, *Validity*, *Not revoked* and *Signer is the sender* for OpenPGP. A message that is not signed has the one *Signed* row, skipped, saying so. Each row is the step's name, its result as a glyph and a word, and one line of what it found |
| A step's result | `s.word` / `s.glyph` | *Passed* (`check_circle`, `--ok-text`), *Failed* (`cancel`, `--err-text`), *Not checked* (`help`, `--muted`) and *Skipped* (`do_not_disturb_on`, `--muted`). A step that could not run says why: nobody checked the message because no trusted receiving server is configured and it carried no DKIM signature; SPF and DMARC are not checked where MailFathom ran its own DKIM check; a DMARC lookup that failed, a policy that cannot be read, or a domain that publishes none; a DMARC failure beside a Known mark adds that the policy only monitors, *The message fails the DMARC policy lawoffice.example publishes, a policy that only monitors (p=none).*; trust skipped because the author was not authenticated and no signature of theirs verified, or because SPF alone established the author; the signature's later steps skipped where its certificate chains to no trusted authority or its key is not known here |
| A step's detail | `s.hasMore` / `s.moreOpen` / `senderMore` | *Certificate authority* and *Known key* carry *Certificate details* or *Key details* under their line, a 44 px target under a finger: pressed, or Enter or Space, it unfolds in place a grid of the certificate's name, email, issuer, valid from, valid until, serial, fingerprint and algorithms, or the key's fingerprint, key ID, user ID, created, expires and algorithms, the values in Geist Mono; *Hide details* folds it. What unfolded is forgotten when the details close |
| Add to contacts | `senderAddShown` / `senderAddOffer` | under the steps, where the service offers it (`add` in the sample): a received message whose sender is not one of the owner's own addresses and is not already written down — by the reader or an administrator — in the reader's book or their organization's. Offered on a warning too, since the reader may be writing down the real correspondent a forgery imitated, and never on Sent or Drafts, which draw no mark. A secondary control with `person_add`, *Add to contacts*, 34 px tall under a mouse and 44 under a finger. It is the details' one act: nothing in them removes a contact or trusts a domain |
| Add to contacts, asked | `senderAddAsking` (`senderAddTitle`, `senderAddWhat`) | pressed, a card on `--sub` replaces it, brought into view with focus on its first answer: *Add HR Department to your contacts?* and what follows before anything is written — *Mail from hr@company.example is then marked trusted whenever its sender is authenticated, and known whenever it shows this address, unless this account is strict.* On a warning it goes on *This message stays a warning on a strict account, or where bank.example publishes a quarantine or reject policy; on a relaxed account under a monitoring-only policy it becomes known.*, and where a contact of the reader's already holds the address but they did not write it down themselves (an agent did, or it predates MailFathom recording who wrote it) it opens with *e.sikora@adventure.example is already in your contacts, but you did not write it down yourself, so it counts only once you add it.* Two answers: *Add to contacts* (accent) and *Cancel*, which brings the act back and focus to it |
| Add to contacts, adding | `senderAddBusy` | the accent answer reads *Adding…*, dimmed and `aria-disabled`, and *Cancel* is dimmed and `aria-disabled` beside it, until the write answers — a write already sent is not taken back, so the card is not left while it is in flight |
| Add to contacts, failed | `senderAddFailed` (`senderAddFailText`) | the write did not land: the card stays, an alert in `--err-text` under the sentence naming why, as every failed read here does — *HR Department could not be added to your contacts: unavailable*, or, where the reader's contacts changed between reading the message and pressing, *…: your contacts changed since this message was read* — and the accent answer reads *Try again* |
| Add to contacts, added | `senderAddDone` (`senderAddDoneText`) | a line on `--ok-soft` with `check_circle` in `--ok-text`, focused: *HR Department is in your contacts now. The mark changes once your mail is checked again.* The mark, the title, the why and the steps do not move: they read the verdict the message carries, which changes once stored mail is judged again, not on the press. Reopened, the details no longer offer the act for that address |

The sample seeds every mark and every result. Trusted and verified at once, drawn as Trusted: `contoso`. Trusted with
a signature that does not match, drawn as a warning: `finanse`. Verified alone: `klient3` (OpenPGP). Known, in the reader's contacts, where a DMARC lookup that failed left the
author not established beside a signature whose key expired: `north1`. Known, in the organization's contacts, under
a DMARC failure the law office's monitoring-only policy turns into Known, its invoices sent through an invoicing
service that signs and sends in its own name: `jacek`. Trusted as the
reader's own contact (`contoso`, `anna2`), as a contact of the organization (`finanse`), as a contact of another user
of this mailbox whose own verified signature, rather than the domain's checks, tied the message to the address after a
list broke DKIM and DMARC failed (`piotr`), as one of the owner's own addresses — a note
sent from the work address to the personal account, which checks DKIM itself (`selfNote1`) — as a domain of the
owner's own accounts on the work account, which opted into them, with a certificate authority the operator configured
and a CRL (`devops1`), and as a member of the account's organization:
named, *Nordwind* (`rekrutacja1`), and nameless because the organization has since been deleted, on a message nobody
checked on the personal account, so no author domain is established and only the organization's signature vouches for
it (`handover1`). A warning from
authentication: `bank1`, `spamDesk`, `spamThreat`. Neutral, authenticated and not recognized: `spamPayroll`;
authenticated by SPF alone, so trust is skipped: `spamParcel`;
signed by somebody else: `hr1`, an opaque S/MIME message; not verifiable here because the certificate chains to no
trusted authority (`prawnik2`), expired (`fabrikam1`), is not meant for email (`audyt1`), was revoked (`marta`), or
its revocation could not be checked (`travel1`); an unknown OpenPGP key (`marketing1`) and an expired one
(`north1`). Who checked: the account's trusted server on the work account, MailFathom's own DKIM check on the personal
account (`travel1`, `selfNote1`, `spamChairs`), and nobody (`spamEstate`, `spamSignals`, `handover1`). DMARC: pass, fail, no policy published
(`prawnik2`), not reported, a temporary error (`north1`) and a permanent one (`spamSupplier`). *Add to
contacts* is offered for a sender recorded afresh (`hr1`), on a warning (`bank1`), and as a restatement of an address
a contact of the reader's holds but they did not write down (`klient3`).

### The receipt mark

A message the account sent may have asked for a delivery status notification (RFC 3461) and a read receipt (RFC 8098)
— § *Composing*, *Sending options*. When a report comes back, the message carries one mark saying what came back: on its
row in *Sent*, and as a badge on its head wherever the conversation is opened. What the service publishes about each
recipient decides it (`data/prototype/receipts.js`, read by `receiptOf`): the outcome a delivery status notification
reported — delivered, delayed, or failed, with the server that reported it and the reason it gives — and the time a
read receipt reported the message displayed. A recipient's outcome is the furthest a report took it: read, then failed,
delayed or delivered, then nothing back. The message's mark is the outcome that most needs the sender, in this order:
**Not delivered**, **Delayed**, **Read**, **Delivered**, **Nothing back yet**. With several recipients the word counts
the ones it names — *Read by 2 of 3*, *Not delivered to 1 of 2* — and drops the count where it names all of them; a
failure outranks a read, because it is the one that asks for something. Each outcome has its own glyph and word, so
none is told apart by colour alone.

| Mark | Glyph | Tone | Word |
|---|---|---|---|
| Not delivered | `error` | `--err-text`; the badge on `--err-soft` inside an `--err` border | *Not delivered*, *Not delivered to N of M* |
| Delayed | `hourglass_top` | `--warn-text`; the badge on `--warn-soft` inside a `--warn` border | *Delayed*, *Delayed for N of M* |
| Read | `done_all` | `--ok-text`; the badge on `--ok-soft` | *Read*, *Read by N of M* |
| Delivered | `done` | `--muted`; the badge on `--sub` inside a `--line` border | *Delivered*, *Delivered to N of M* |
| Nothing back yet | `schedule` | `--muted` on no ground, inside a dashed `--border2` border; on the opened message alone | *Requested* |

**Decided here, as #2858 asked: *delayed* gets a mark, *requested, nothing back yet* gets one on the opened message and
none on a row.** A delay is a report — the recipient's server has not taken the message yet and is still being tried —
and the sender may want to act on it before the failure arrives, so it is drawn in the warning tone, and a later
delivered or failed report replaces it. Nothing back is not a report: most read receipts never come, and a server that
does not support delivery notifications sends none, so on a *Sent* row a mark for it would stand for ever beside every
request and stop meaning anything. On the opened message it answers the question the reader is asking there — did this
one ask? — and its details say what was asked and that nothing came.

| State | Flag | Reveal |
|---|---|---|
| The mark on a Sent row | `t.rcptShown` / `t.rcptGlyph` / `t.rcptName` | a *Sent* folder open, on the row of a message a report came back for: the glyph alone, the first of the markers at the row's end, before the flag, the count and the AI summary control. 17 px in a 24 px target under a mouse; 20 px in a 44 × 44 target under a finger, taken from the row's padding so the row keeps its height. Pressing it opens the details and nothing else — it neither opens nor selects the message. As the sender mark does, it answers a pointer alone and is hidden from the accessibility tree: its name — the word alone, *Read by 2 of 3* — is its name bubble and a visually hidden part of the row's own name, since what the list carries for a row is the counts rather than who each recipient is; each recipient's own outcome is named by the badge on the opened message and by the details, and the keyboard reaches the details through the row's menu, whose last item is *Receipt details* under the mark's glyph. It carries `data-tip-hover`, and its word joins that menu's header (`ctxMenuDetail`, *Anna Kowalska - Read by 2 of 3*). No other folder draws it |
| No mark for nothing back, on a row | `t.rcptShown` false | a sent message that asked and got nothing back: its *Sent* row draws no receipt mark, and its menu no *Receipt details* |
| The badge on an own message | `m.rcptShown` / `m.rcptWord` | the newest message the account sent in an opened conversation, in any folder, a report having come back or a request standing: the first thing in the right-aligned span of its head, before the attachment clip, the original's button and the time — 22 px tall, a 6 px radius, the glyph at 15 px and the word in 12 px semibold. A button named with the mark's name (*Receipts requested, nothing back yet - Ewa Sikora nothing back yet* for nothing back), `aria-haspopup="dialog"`, reached by `Tab` and opened with `Enter` or `Space`; under a finger a 44 px target around it, taken from the head's line height. An earlier own message carries none: what came back belongs to the newest one |
| The details, anchored | `receiptPopOpen` + `!receiptPopSheet` / `receiptPop` | the mark or the badge pressed under a mouse: a 360 px panel under the mark with its end edge on the mark's, kept 10 px inside the window; where fewer than 300 px are left below and more above, it opens above the mark instead. At most 520 px tall, its body scrolling under a head that stays. It arrives as a dialog does (`pop`), takes focus, and closes on *Escape*, on its *X*, on a press anywhere outside it, on a second press of its own mark, and on back, handing focus back to the mark. Asked from the row's menu there is no mark to stand under, so it opens centred in the window |
| The details, as a sheet | `receiptPopOpen` + `receiptPopSheet` | the same under a finger, at the phone, the fold and the tablet: a sheet from the bottom over the scrim, at most 600 px wide and centred, with its grabber; it closes as the anchored one does and on a press on the scrim |
| What the details hold | `receiptPopTitle` / `receiptPopAsked` / `receiptPopRows` / `receiptPopNotes` | a 36 px tile with the mark's glyph in its tone and the mark's word as the title, then what the message asked for — *You asked for a delivery notification and a read receipt.* Under *Recipients*, one card on `--sub` holding a row per recipient: the name, its outcome word and glyph in its tone at the row's end (*Nothing back yet* for none), the address, then a line per report — *Displayed 26.08 16:48 · their read receipt*, *Delivered 26.08 16:21 · mx1.contoso.example*, a failure's or a delay's status code as the report gives it (*The server reported status 5.1.1.*) over *Reported 25.08 10:15 · mx2.northwind.example* — the code rather than the sentence a server wrote beside it, which MailFathom does not keep — and *No delivery notification back yet.* or *No read receipt back yet.* for what was asked and has not come. Last, in `--muted`, what a missing report does not mean: *A read receipt comes back only if the recipient's mail app sends one, so none back does not mean the message was not read.* where a read receipt was asked, *A delivery notification comes back only where every server on the way supports it.* where a delivery notification was |
| A colleague marked without a receipt | `r.internal` | a recipient whose account belongs to the same organization on this deployment: MailFathom records their delivery and their opening itself, with no request and no report, and the mark draws it exactly as a report's. In the details their lines read *Opened 26.08 11:40 · in MailFathom* and *Delivered 26.08 11:02 · to their mailbox in your organization*, with no *back yet* line whatever was asked; what was asked reads *You asked for no receipt.* where nothing was; and the notes end on *A colleague in your organization is marked by MailFathom itself, without a receipt, once their mailbox holds the message and once they open it here.* |

The sample seeds every mark, reached in *Sent* and on the newest own message of each conversation (the earlier
messages revealed with *Show N earlier messages*). Read by some of several, the third delivered with a read receipt
asked and none back: `contoso`. Read by a colleague, nothing asked: `piotr`. Read, with only a read receipt asked: `marta`. Not delivered: `north1`.
Not delivered to one of two, the other read: `audyt1`. Delayed: `fabrikam1`. Asked and nothing back, drawn on the
opened message alone: `klient3`.

A received message can ask the reader for a read receipt (`Disposition-Notification-To`). MailFathom answers one only
when the reader says so, once, and keeps saying afterwards that it did (`data/prototype/receipts.js`,
`receiptRequests`).

| State | Flag | Reveal |
|---|---|---|
| A read receipt asked of the reader | `m.mdnAskShown` / `m.mdnAskText` | the newest received message of a conversation asks and no receipt was sent for it: a card at the top of its body, under the remote-content card or its line, `--sub` inside a `--line` border on a 12 px radius — the `mark_email_read` glyph in `--muted`, *Jacek Wrona asked for a read receipt.*, and under it in `--muted` *Sending one tells them you opened this message. Nothing is sent unless you send it.*, beside a bordered *Send read receipt* at the card's end (32 px tall under a mouse, 44 under a finger), which wraps under the words on a narrow pane. Where the request names an address other than the sender's, the sentence says where the receipt would go: *Marketing asked for a read receipt, to be sent to campaign-tracking@mailer.example rather than to them.* Nothing sends one on its own, and opening the message is not consent |
| Sending it | `mdnSending` / `m.mdnBtnOff` | *Send read receipt* pressed: the button reads *Sending…*, dimmed, `aria-disabled` and a progress cursor, and a second press does nothing, until the server takes it |
| Sent | `m.mdnSentShown` / `m.mdnSentText` / `mdnSent` | the server took it, or it was sent earlier: the card gives way, in its place, to one `--muted` line with the `mark_email_read` glyph, *Read receipt sent today 10:57 to j.wrona@lawoffice.example.*, which takes focus as it arrives and stays on that message for good, so a receipt is never offered twice. A receipt sent before the message was opened in this session reads its own time (`anna2`, *29.08 08:52*) |
| Not sent | `m.mdnFailShown` / `m.mdnFailText` | the server refused the receipt or could not be reached: the card stays, *Send read receipt* can be pressed again, and under the note a line in `--err-text` says why — *The read receipt was not sent: the server refused to send it.*, or the reason a request fails for, *… unavailable.* It is announced as it arrives |
| Offline | `writeRefused("send")` | the status offline: *Send read receipt* is a control that cannot act — `aria-disabled`, at 60 % with a not-allowed cursor — named *Send read receipt - no connection to the MailFathom server.* |
| Never on spam | `threadSpam.spam` | a message marked spam draws no card, whatever it asks: answering would confirm to its sender that the address reads mail (`spamPayroll` asks and draws nothing) |

Seeded: asked of the reader by the sender, `jacek`; asked to go to an address other than the sender's,
`marketing1`; sent earlier, `anna2`; asked by spam and never offered, `spamPayroll`.

A report that comes back as mail — a read receipt (`multipart/report; report-type=disposition-notification`) or a
delivery status notification (`report-type=delivery-status`), which a failure and a delay send unasked and a success
only when one was requested — is drawn from what it reports, never from the subject the reader's mail app or a mail
server wrote (`reportOf`, `threads.js` § `report`). The words are the receipt mark's, and so is the summary over
several recipients.

| State | Flag | Reveal |
|---|---|---|
| A report's row | `t.report` / `t.subject` | the headline takes the subject's place in the row and at the head of the conversation, in the subject's own style: *Read receipt: Marta Nowak read “CPI calculation 2027”*; *Not delivered to r.lis@northwind.example: “Quote for the reporting module”*, *Delayed for t.bak@fabrikam.example: “Q4 delivery dates”*, *Delivered to j.wrona@lawoffice.example: “Contract addendum - signatures”*; several recipients, *Not delivered to 1 of 2: “Request for the contract register”*. The message quoted is the account's own one the report names. The row draws no AI line, since a report carries nothing to read into |
| A report naming no message here | `report.of === null` | the headline still says what kind of report it is and what it says: *Read receipt from Karol Wiśniewski · for a message not in this mailbox*, *Delivery report: not delivered to orders@old-supplier.example · for a message not in this mailbox* |
| A report it cannot word | none: the artboard draws no such report | a read receipt saying the message was deleted or not displayed, or a delivery report naming an outcome none of the four: drawn as the message it is, under its own subject, unfolded and with no headline, rather than as a report |
| Folded | `m.rptShown` / `m.rptLine` / `reportOpen` | the report as opened, by default: one line in place of the whole message, `--sub` inside a `--line` border on a 10 px radius, 38 px tall under a mouse and 48 under a finger — the mark's glyph in its tone (`done_all` in `--ok-text` for a read receipt, `error`, `hourglass_top`, `done` for a delivery report), what it reports and when, *Marta Nowak read your message · 25.08 17:40*, *Not delivered to 1 of 2 · 24.08 09:13*, and `expand_more` at the end. It is a `role="button"` with `aria-expanded="false"`; a click, `Enter` or `Space` unfolds it. A head with no state draws no state strip and, on the phone, no empty AI line |
| Unfolded | `m.rptOpen` | the line stays, its chevron turned to `expand_less` and `aria-expanded="true"`, with focus kept on it, and the report follows it written out as any message is — its sender, its time, the original's button, and its body, the human-readable part and the machine-readable fields. It loads nothing remote and offers no read receipt. Another press folds it again. This is the one message that folds: every other message is drawn in full (`showExpandAll`) |

Seeded, each in the inbox under *Transfer confirmation €18,400*: a read receipt for `marta`, `mdnMarta`; one naming
no message here, `mdnStray`; not delivered, `dsnNorth`; delayed, `dsnFabrikam`; delivered as requested,
`dsnContoso`; several recipients, `dsnAudit`; a delivery report naming no message here, `dsnStray`.

### Composing

| State | Flag | Reveal |
|---|---|---|
| Composer open | `composeOpen` | the compose control |
| Recipient suggestions | `rcptSuggestShown` / `rcptGroups` | the *To* row's own field (*add a recipient…*) focused, empty or typed in: a `--sub` list under the row, at most 300 px tall, in three groups each under its name — *Colleagues*, *Nordwind contacts* (the organization's book), *Your contacts* — three a group while nothing is typed and six once something is, matching the name or the address. A colleague's row carries the avatar with the presence dot and *Offline · Head of Delivery · out until 04.09*; anybody else's, the address. A row is 40 px, 48 under a finger, and pressing one adds them to *To*. Outside an organization only *Your contacts* stands |
| Nobody matches | `rcptNoMatch` | something typed that nobody matches: *Nobody in your contacts or your organization matches “…”* |
| A recipient out of office | `composeOooShown` / `composeOooRows` | a colleague out of office among *To*: under the recipients, one `--warn-soft` line per such colleague with a `--warn` rule, `event_busy` and *Piotr Zieliński is out of office until 04.09.* — the end date and never the automatic reply's text. Reached by choosing Piotr Zieliński from the suggestions |
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
| An AI draft's closing | `signOffHtml` / `composeDrafts.signOff` | an AI draft — *Write a draft*, *Shorten* and the other acts, or *Draft a reply* — ends at its own words on an account with a footer, with no closing phrase and no name, because the footer carries the closing. On an account without one it closes with *Best regards,* and the name, outside the accent edge as before |
| The sending account | `composeAccKey` | the account a composition sends from and whose Outbox *Send* hands it to: a reply's, a reply to all's or a forward's conversation's account, a draft's own account, or else the account whose folder is open — the work account where none is, unified folders included. The composer names no account of its own; its footer is what says which |
| The account's footer, folded | `composeFooterShown` / `composeFooterOpen` | the sending account has a footer — the person's own, or the one their organization's template fills in, the same lines sent mail folds (`footers` in `data/prototype/workspace.js`). Under the editor and above the composer's foot, at every composition: a dashed `--line` rule at the editor's text inset (19 px), and under it the one line every footer folds to (`footerFoldStyle`, § *The thread*, *The reader's own footer, folded*) — `chevron_right` and *Your footer is hidden* at 13 px in `--muted`, a `role="button"` with `aria-expanded`, 28 px tall under a mouse and 44 under a finger. Every composition starts folded: closing the composer folds it again. Reached by *New message* with a *Nordwind · work* or *Personal mail* folder open, or by replying to a conversation of either |
| The account's footer, open | `composeFooterOpen` / `composeFooterLines` | a press on that line — `expand_more` and *Hide your footer*, and the footer's lines unfold under it with `disclose`, in `--muted` — the name line semibold at 13 px, the others 13 px, the fine print 12 px — so every line is quieter than the 15 px editor. It is shown, never written in — outside the editor, no caret, no text selection, and the formatting bar does not reach it. It scrolls with the editor rather than standing pinned (`composeWritingStyle`), so a long footer never pushes the foot with *Send* off the screen. Another press folds it |
| No footer | `!composeFooterShown` | the sending account has none — the shared *Board* in the sample: nothing is drawn and the composer is as it is without one. Reached by *New message* with a *Board* folder open |
| A drafting field | `growingFieldStyle` / `composerPhStyle` | the composer's AI prompt (`composeAiText`, *Write what this is about…*) and the reply drafter's field (`replyDockText`) — what is typed may be an instruction, the person's own draft of the message, or both. Empty, it is one line and its placeholder stays on that line, cut with an ellipsis. Written in, it wraps rather than scrolling sideways and grows a line at a time up to six lines; past six it stops growing and scrolls vertically. In the composer Enter starts a new line, because the AI acts are the presses there |
| The AI's context | `composeCtxChips` | always, as the last rows of the composer's AI section: *Context* at 12 px semibold, then one chip per message — `--panel` on a 6 px radius with an `--accent-line` border, a `mail` glyph, the subject ending in an ellipsis at 260 px (220 on the phone) and titled with the whole of it, and a `close` titled *Remove from the context*; under `touch` the chip is 36 px tall and its `close` a 32 px box. A reply starts with the conversation it answers; a new message starts with none. Every way the composer closes — its own close, its tab's close, closing every tab — empties the context, its starting point and the reply's conversation, so the next composition starts clean |
| No context yet | `composeCtxEmpty` | the context holds no message — *none yet - click a message in the list to draft from it*. On the phone the composer fills the screen and no list stands beside it, so a new message there takes no context at all and says so: *none - the AI drafts from what you write above*. A reply on the phone still starts with the conversation it answers |
| The list sets the context | `composeCtxPatch` | while the composer is open, a plain click on a row in the list makes that message the context (and opens it, as a click always does), and a selection — a modified click, the row's *Select messages*, any row while a selection runs — makes the whole selection the context. Emptying the selection leaves the context as it stood. The first context a composition takes is its starting point (`composeCtxBase`) |
| Context steady | `composeCtxSteady` | the context is its starting point, or has none yet — the row ends in *the draft stays local until you send it* |
| Context changed | `composeCtxChanged` | the context differs from its starting point as a set: another message clicked, several selected, a chip removed. Two marks, never colour alone: a 22 px disc on `--warn-soft` with a 1 px `--warn` ring holding `priority_high` in `--warn-text` at the end of the AI prompt field, arriving with `pop` and named *The context changed since you started* — and, under the context row with `disclose`, *This message started from “…” - the context has changed since. Check it before the AI drafts.* in `--warn-text`, naming the starting message (and *and N more* for a selection), beside *Restore* (`undo`, 44 px tall under `touch`), which puts the starting context back. Arriving back at the starting set by any route takes both marks away |
| The composer's foot | `composeSendStyle`, `composeAttachStyle`, `composeSaveStyle`, `composeMoreStyle` | always; under `touch` one 44 px row — *Send* filled, *Attach* bordered and on the phone the `attach_file` symbol alone (`composeAttachLabel` is `!isMobile`), *Save draft* a text action as tall as the row — and, last at the row's end, *Sending options*: the `more_vert` glyph alone in `--muted`, a 32 px square under a mouse after *Ctrl+Enter sends*, a 44 px one under a finger pushed to the end |
| Sending options | `openComposeMore` / `ctxMenu.kind === "composeMore"` | the foot's `more_vert`: the context menu (`ctxMenuOpen`, `pop`) standing above the control with its end edge on the control's — the foot being the composer's last row — headed *Sending options*, 336 px wide (352 under a finger, and on the phone at most the screen less 24 px), holding two checkable items, each `role="menuitemcheckbox"` with `aria-checked`: *Request delivery notification* (`move_to_inbox`) and *Request read receipt* (`mark_email_read`), then the *Send later* group below them. A request that is on carries a `check` in `--accent-d` at the item's end. Pressing an item turns it over and leaves the menu standing, so both are set in one visit; it closes on a press outside and on `Esc`, putting focus back on whichever opened it — the control or the line above the foot, and the control where the line went because both requests were turned off. From the keyboard, `Enter` or `Space` on the control or the line opens it with focus on its first item, the arrow keys walk the items, `Enter` or `Space` turns the focused one over, and `Tab` closes it as `Esc` does. On the phone it stands in the middle of the screen as every context menu there does. Both start off for every message — a new one, a reply, a forward — and belong to that message alone: there is no account-wide default, and a draft keeps what it asked for (`composeReceipts`, the out-of-step draft *Q4 supplier review - agenda* opening with a read receipt asked) |
| A request on | `composeReceiptsOn` / `composeReceiptsText` / `composeReceiptsNote` | either request on, with the menu closed or open — two marks, so nobody sends unaware of it. The foot's `more_vert` turns `--accent-d` on `--accent-soft` inside an `--accent-line` ring, its name then *Sending options - asks for a read receipt*; and a line stands directly above the foot, under the offline band where that stands, on `--panel` with a `--line` rule: the request's glyph in `--accent-d` (`mark_email_read` where a read receipt is asked, `move_to_inbox` otherwise) and *This message asks for a delivery notification and a read receipt.* — or *a delivery notification.*, or *a read receipt.* — followed, where a read receipt is asked, by *A read receipt comes back only if the recipient's mail app sends one.* in `--muted`. 44 px tall at least under a finger. The line is a button named *Change sending options* that opens the same menu |
| A focused field | `data-compose-fields` | a field with keyboard focus — the composer's rows are one sheet rather than boxes, so the ring every other borderless field takes is not drawn: a row holding the focused field turns its bottom edge `--accent`, and the editor turns a 2 px start edge, transparent until then, `--accent` |
| Without a connection | `composeRefused` / `composeRefusedNote` | the status offline — a `--warn-soft` band with a `--line` rule directly above the composer's foot, the `cloud_off` glyph and *No connection to the server. What you write is not saved on the server, and closing this tab or window loses it. It can be saved or sent once the connection is back.* in `--warn-text`. It no longer promises the words are kept, because they are kept in this tab alone |
| Send later | `openComposeMore` / `SEND_LATER.presets` | the same menu, under its two requests: a `--line` rule and the group's name, *Send later*, in 11 px semibold `--muted` (a `role="separator"` named *Send later*), then four `role="menuitemradio"` items with `aria-checked` — *Tomorrow morning* (`wb_twilight`), *Tomorrow afternoon* (`light_mode`), *Monday morning* (`date_range`), each with the date it resolves to at its end in 12 px `--muted` tabular figures (*Fri 28.08, 08:00*, *Fri 28.08, 13:00*, *Mon 31.08, 08:00*), and *Pick a date and time…* (`edit_calendar`), which carries the picked date and time at its end once one is chosen. The time taken carries the `check` in `--accent-d`, and every time keeps the check's room so the dates stand in one column. Unlike a request, pressing a time takes it and closes the menu, focus going back to whichever opened it; *Pick a date and time…* closes it and opens the picker. The ready times are 08:00 the next day, 13:00 the next day, and 08:00 the next Monday, said against the sample's now, Thursday 27.08 at 10:47 (`data/prototype/compose.js`, `sendLater`) |
| The picker | `sendLaterAsk` / `sendLaterOpen` | *Pick a date and time…*, or *Change* on the line above the foot: a form dialog (`dlgForm`, `pop`; on the phone the whole screen, the composer's foot included, with the sheet's grabber), `role="dialog"` named *Send later*, headed by `schedule_send` in `--accent-d`, *Send later* and *Choose when this message leaves. Until then it waits in the Outbox.*, with a close control. Two fields side by side, wrapping under each other where fewer than 312 px are left, each labelled above: *Date*, a native date field whose earliest day is today, and *Time*, a native time field — 36 px tall under a mouse, 44 px at 16 px under a finger. It opens on the time already chosen, or on the first ready time, with focus on *Date*. Under the fields, in an `aria-live` region: *Leaves Fri 28.08 at 08:00.* in 14 px semibold beside `schedule_send`, then the zone the time is named in — `public` and *In your time zone, Europe/Warsaw (UTC+02:00), as your profile sets it.* in 12 px `--muted` — the profile's *Time zone* setting rather than this device's. *Cancel* bordered and *Schedule* filled at its end, 34 px tall under a mouse, 44 px under a finger; *Schedule* takes the time onto the composer, *Cancel*, the close control, `Esc`, back and a press on the scrim leave it as it was, and focus goes back to the foot's three-dot control. A date and time matching a ready time is taken as that ready time |
| A time already gone | `sendLaterGone` | a date and time at or before now, today 09:00 in the sample: both fields `aria-invalid` inside an `--err` border, and in place of when it leaves, a `role="alert"` line in `--err-text` with `error`: *That time has already passed - it is 10:47 today. Choose a later time.* *Schedule* is a control that cannot act — `aria-disabled`, at 45 % with a not-allowed cursor — named *Schedule - choose a time that has not passed yet.* Either field emptied reads *Choose a date and a time.* in `--muted`, and *Schedule* is named *Schedule - choose a date and a time first.* |
| A time chosen | `composeSendAtOn` / `composeSendAtText` / `composeSendLabel` | a time taken, with the menu closed or open — three marks, so the message is never scheduled unaware or sent at once by mistake. *Send* reads the time it leaves, `schedule_send` before *Send Fri 08:00* (*Send Today 17:00* the same day, *Send 03.09 09:30* a week or more out), named *Send Fri 28.08 at 08:00*; *Ctrl+Enter sends* reads *Ctrl+Enter schedules*. A line stands directly above the foot, below the requests' line where that stands, on `--accent-soft` with an `--accent-line` rule: `schedule_send` in `--accent-d`, *This message leaves Fri 28.08 at 08:00, not when you press Send.* and *Europe/Warsaw time* in `--muted`, then *Change*, a text action in `--accent-d` that opens the picker on the chosen time, and *Clear time*, bordered on `--panel` with `close`. They are 28 px tall under a mouse and 44 px under a finger, where the line is 52 px tall at least and they wrap under the words on the phone. The foot's `more_vert` stands on the accent ground as for a request, its name then *Sending options - leaves Fri 08:00* |
| Clearing the time | `clearSendAt` | *Clear time*: the line goes, *Send* reads *Send* again, and focus goes to the foot's three-dot control. Nothing else about the message changes |
| A draft keeps no time | `saveDraftNow` / `saveDraftAndClose` | *Save draft*, or saving on close, with a time chosen: the time belongs to the send rather than the draft, so the draft keeps none, and the toast says so — *Draft saved*, *The message is waiting in Drafts. The time you chose is not kept with it - choose it again when you send.* A draft opened again sends at once until a time is chosen |
| Send confirmation | `sendAskOpen` / `sendAskTitle` | pressing send with something to warn about. With a time chosen it reads *Schedule this message?* under `schedule_send`, adds *Leaves Fri 28.08 at 08:00, Europe/Warsaw time* in semibold `--text` under the recipients and the subject, and confirms with *Schedule* — *Schedule anyway* with warnings — rather than *Send* |
| Warnings in it | `sendHasWarn` | the warning list is not empty — an unaccepted AI draft is one |
| Scheduled | `confirmSend` with `composeSendAt` | *Schedule* confirmed: the composer closes as it does after *Send*, the message is handed to the account's Outbox at once as a *Scheduled* entry (§ *The Outbox*, *Sends Fri 08:00*), and a success toast stands for 8 s — *Message scheduled*, *It leaves Fri 28.08 at 08:00. Until then it waits in the Outbox, where it can still be cancelled.*, with *Show in Outbox*, which opens that account's Outbox on the entry, where *Cancel sending* stands until the time comes. No *Sending message…* phase runs, since nothing is sent yet. A time that passed while the message was being written is refused before anything is handed over: confirming it opens the picker on that time with its refusal, which the service's refusal of a due time already gone is the reason for |
| Offline with a time chosen | `writeRefused("send")` | as without one: *Send Fri 08:00* is a control that cannot act and says why. Choosing, changing and clearing a time stay open, since nothing is handed over until the message is sent |

### Documents and HTML

| State | Flag | Reveal |
|---|---|---|
| Document open | `docOpen` | an attachment is opened |
| HTML view open | `htmlOpen` | the `code` control on a message head, titled *Show the original message* |
| HTML mode warning | `htmlModeWarn` | `st.msgView === "html"`, chosen in Settings |
| HTML confirmation | `htmlAskOpen` | opening the original from a message — *Show the original message?*, answered by *Stay in simplified* or *Show the original*. A message marked spam answers with a toast instead (§ *Spam*, *Read-only spam*) |
| A link inside the original | `embedLinks` / `LINK_SCRIPT` | the original's frame, inline or in the preview, follows no link itself. Both frames carry `sandbox="allow-scripts"` and nothing else, and the one script either runs is the page's own: the message's scripts are removed before its markup reaches a frame, so *scripts and remote content are blocked* still holds of the message. In the frame, a press on a link, or on a picture inside one, is handed to the page and asks as any other link does (§ *A link that leaves MailFathom*). *Tickets Kraków-Berlin* carries such a picture, a hotel banner that goes to another host than the sender's |

### The message source

*Show source* in *More actions* opens the message exactly as it was received — every header and every MIME part, as
raw text. It is fetched when the item is pressed and never with the message, so the surface always opens on a wait.

| State | Flag | Reveal |
|---|---|---|
| Source open | `sourceOpen` / `sourceMotion` | *Show source* pressed. On the desktop (1180 px and wider) a dialog over the scrim, 880 px wide and as tall as every dialog (`dlgMaxH`), arriving with `pop`; below 1180 — the tablet, the fold and the phone — the whole screen, above the bottom navigation wherever it stands (`navBottom`), rising with `sheet` and carrying the grabber (`sourceGrabber`) so it is pulled down to close. Its head is the `data_object` glyph, *Message source*, the subject under it at 12 px in `--muted` (with *· as received, 2.9 kB* once it has arrived; opened from the toolbar, *As received, 2.9 kB* alone, there being no subject there to name it by), and *X* titled *Close the message source*. It closes on *X*, `Esc`, the scrim, back, and a pull down where it is a sheet |
| Reading the source | `sourceLoading` / `sourceSkelLines` | the request is on the wire: a skeleton of header-like lines — short and long bars at 10 px in groups, as a header block and the start of a body sit — with the shimmer every skeleton has, under `prefers-reduced-motion` standing still. Never a spinner and never an empty box. *Invoice 08/2026* stands on it, its source never arriving |
| The source | `sourceLoaded` / `sourceText` | the text in `Geist Mono` at 13 px (12 on the phone), line height 1.625, in `--text2`, preserving every line break, selectable, wrapping long lines rather than scrolling sideways (`white-space:pre-wrap`, `overflow-wrap:anywhere`), and scrolling inside the surface. It replaces the skeleton with `fade` |
| Cut short | `sourceCut` / `sourceCutNote` | the message is larger than one answer carries: a note above the text, `info` glyph, `--sub` inside a `--line` border — *This message is 8.4 MB, more than can be shown at once, so its source stops after the first 1 MB.* The first part follows as above. *Trade show materials 24.09* reaches it |
| Could not be read | `sourceFailed` / `retrySource` | the deployment holds no readable copy, refused the credential, did not answer, or was not reached: a `warning` line in `--warn-text` — *The message source could not be read: unavailable.*, the reason the client names for that failure. *Try again* stands under it only where the deployment did not answer or was not reached, and reads it again with the skeleton back; a copy that is no longer there, a refused credential, and an answer that could not be read repeat identically, so they offer none. *Nightly report - build #2418* reaches it, and its second try arrives |
| Not shown here | `sourceScreened` | the deployment screens what leaves it for sensitive content on any account the reader is assigned, whichever account the message came from, and a raw source cannot be screened: a `lock` line in `--muted` — *Your administrator screens what leaves this server for sensitive content, and a message's raw source cannot be screened, so it is not shown.* — and no way to try again, since another try answers the same. *Transfer confirmation €18,400* reaches it: the prototype draws the state on that one sample message, where a reader it applies to meets it on every message |
| Offline | `sourceOffline` | the machine has no network when *Show source* is pressed: nothing is asked for and no skeleton stands, only a `cloud_off` line in `--muted` — *This machine is offline, so the message source cannot be read. It is read once the network comes back.* — and no *Try again*, since the read starts on its own when the network returns. *Tickets Kraków-Berlin 12.09* reaches it |

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
| Event has a meeting link | `eventHasLink` / `eventLink` | the event carries a link to join it: a `videocam` glyph, *Meeting link* in 12 px `--muted`, and the link's host as an underlined `--accent-d` link under it. Pressed, it goes where every link that leaves MailFathom goes (§ *A link that leaves MailFathom*) — *Fabrikam rollout review* asks, *Rollout status with Piotr* is on a trusted domain and opens at once |
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
| Avatar | `personHasAvatar` / `personNoAvatar` | whether a photo exists for the name — **both states are in the mock data**. The rule holds wherever a person is drawn, not only here: the photo where there is one and the initials otherwise, on a message row, an opened message's head, a People row, a person's page, a row of the organization's structure, a recipient suggestion, and an AI answer's *People* rows and run of participants — in the prototype's Discover answer and in `ai-blocks.dc.html` alike |
| Collected person | `personCollected` | the person was gathered rather than added; it gates three regions |
| Next step | `personHasNext` | the person's AI reading proposes one |
| **No documents** | `personNoDocs` | that person's `docs` is empty — Piotr Zieliński under *Colleagues* |
| New contact | `newContactOpen` | the new-contact control |
| Edit contact | `personOwn` / `ctEditing` | the *Edit contact* symbol in the header of a person the reader wrote down (never a collected one, a colleague, or the organization's) — the new-contact dialog titled *Edit contact*, opened on the record, saved with *Save changes* |
| Delete contact | `personOwn` / `deleteContact` | a `delete` symbol titled *Delete contact* beside *Edit contact*, on a person the reader wrote down only: it asks *Delete contact?* — *\<name\> will disappear from your contacts. Mail with them stays where it is.* — and *Delete* takes them off the list and opens the next one |
| The contact dialog's fields | `ctPhoneRows` / `ctHasPhoto` | New and Edit alike hold, in order: the photo, the full name, the email addresses, and the phone numbers. **No company and no role** — those are a colleague's, read from their profile, so a contact the reader writes down carries neither, and neither does its header or its row |
| Its photo | `ctHasPhoto` / `ctNoPhoto` / `ctPhotoError` | a 56 px circle — the chosen photo, or a dashed `--rail` circle holding `add_a_photo` — beside *Choose photo* (`image`; *Change photo* once there is one), *Remove photo* while there is one, and *JPEG or PNG, up to 1 MB. Without a photo the initials stand in.* A file over 1 MB is refused under them in `--err-text` — *The file is 1.4 MB - the limit is 1 MB.* — and so is a file that is neither kind, *That file is neither a JPEG nor a PNG.*, and one that could not be read, *That file could not be read. Choose it again, or choose another.* Nothing is sent until the dialog is saved, and saved, the photo is what every avatar of that person draws. What a save of the photo alone and a photo the server did not take look like are under *States the source has and a preview does not reach* |
| Its email addresses | `ctMailRows` / `ctAddMail` | *Email addresses*, a radio group with one row per address: a radio (`radio_button_checked` in `--accent` on the default, titled *Default address*; *Make this the default address* on the others), the address (`type="email"`), a *Default* chip on the default one, and a `close` titled *Remove address* while there is more than one; *Add an email address* (`add`) under them. Exactly one is the default — the first row on a new contact, and removing the default hands it to the first left. A row left empty is dropped on save, and the default is what *Contact details* marks *Preferred*. Every control is 44 px under a finger |
| Its phone numbers | `ctPhoneRows` / `ctNoPhones` / `ctAddPhone` | *Phone numbers*, then one row per number: a label chosen from *Mobile*, *Work*, *Home* and *Other*, the number (`type="tel"`), and a `close` titled *Remove number*; *No phone numbers yet* where there is none, and *Add a phone number* (`add`) under them — the first added is *Mobile*, the next *Work*. A row left empty is dropped on save. Every control is 44 px under a finger |
| Contact details | `personAddresses` / `personPhones` / `personNoPhones` / `personPhonesShown` | a card on every contact: each address with `mail`, the preferred one marked *Preferred* where there is more than one; each phone number with `call` as a `tel:` link in `--accent-d`, its label at the row's end; *No phone numbers* where there is none. A collected contact's card holds its address alone — no phone rows and no *No phone numbers*, since nothing collected from mail headers can carry one. A colleague's card adds *Organization*, *Title* and *Department* (`personFacts`) |
| **Colleagues** | `peopleMembersTab` / `showMemberContacts` | a third segment between *Own* and *Collected*, titled *Members of your organization*, drawn only while the reader belongs to an organization (`inOrganization`), and **the segment People opens on** there — *Own* is the first one only outside an organization: every member, mapped from their profile — *N colleagues* and *Nordwind's directory* above them. The finger's swipe walks the three in order |
| A colleague's row | `p.isMember` / `p.dotStyle` / `p.phoneApp` / `p.ooo` | the avatar carries the presence dot at its lower right (11 px, ringed in `--panel`) and, for one who uses the phone app, the `smartphone` badge at its upper right (14 px); the second line names the presence in words before the title — *Busy · Controller* — so the dot is never the only telling. An absence takes *Out until 04.09* in `--warn-text` on `--warn-soft` in the place of the waiting chip. Its menu has no *Delete contact* and no *Propose a meeting*; it has *Copy address* |
| The organization's contact | `p.orgOwned` / `personOrgContact` | a contact the organization keeps in its own book sits under *Own* beside the reader's, with a `corporate_fare` chip naming the organization at its row's end and the same chip beside the name in its header (titled *From Nordwind's address book*). The chip takes at most half its line and cuts a long name with an ellipsis, so a name of any length leaves the address and the person's name standing at 320 px. Read-only: no *Edit contact*, no delete in the header or the menu, and *Kept by Nordwind - read-only* under `lock`. Globe Travel desk and Lena Wójcik |
| A colleague's contact | `personMember` / `personKindLabel` | the header's chip is `badge` *Colleague* (titled *A member of Nordwind*), the line under the name is title · department · organization, then the preferred address, then one row of what connects the contact to a user — below — and *From Nordwind's directory - read-only* under `lock`. No edit control. Its two actions are *Copy address* (`content_copy`, a 38 px symbol) and *Write an email*, which opens the composer addressed to them. The relationship card is drawn only where a reading exists, and the threads and documents only where there is correspondence |
| Presence | `personPresenceLabel` / `personDotStyle` / `PRESENCE` | a 15 px dot on the 52 px avatar and the state in words in the header's row: *Active* (`--ok`), *Away* (`--warn`), *Busy* (`--err`), *Do not disturb* (`--err` crossed by a `--panel` bar), *Offline* (a `--faint` ring). A member who chose *Hidden* is drawn *Offline* to everybody else — the same dot, the same word, and no *Last seen* |
| Last seen | `personLastSeenShown` | an *Offline* colleague who is not hidden: *Last seen yesterday 18:20* beside the state — Piotr Zieliński, Zofia Krawczyk; never Igor Pawlak, who is hidden |
| The phone app | `personPhoneApp` | the `smartphone` badge at the avatar's upper right (19 px) and *Uses the phone app* in words in the row — an icon of its own, apart from the presence |
| Work mode | `personWorkModeShown` | `apartment` *Office today* or `home_work` *Remote until changed* in the row |
| Out of office | `personOoo` | `event_busy` *Out of office until 04.09* in `--warn-text` on `--warn-soft` at the row's end — the end date and never the reply's text |
| Status message | `personStatusShown` / `personStatusClearsShown` | under the row, `chat_bubble` and the message in `--text`, then when it clears in `--faint`, read on the reader's clock: *· clears at 14:00* on the day it clears, *· clears tonight* where that is the coming midnight, and *· clears Friday* for a later day — Marta Nowak, Hanna Mazur. A message that never clears is drawn with nothing after it — Oliver Grant. One that has cleared is not drawn at all |
| Time and working hours | `personTimeCardShown` / `personClockShown` / `personHoursYoursShown` | a card beside *Contact details*: `schedule` and the member's local time with its offset — *09:41, 1 h behind you*, *10:41, same time as you* — then `work_history` and the days and hours in the member's time, followed in `--muted` by the same hours in the reader's where they differ — *Mon–Fri 9:00–17:30 (10:00–18:30 your time)*. Where the reader's clock puts them on other days, those days are said too — *Mon–Fri 9:00–17:00 (Sun–Thu 15:00–23:00 your time)* — and where they run past the reader's midnight, so is that — *Mon–Fri 9:00–17:00 (17:00–01:00 the next day, your time)* |
| Outside working hours | `personOutsideHours` | `bedtime` *Outside working hours* in `--warn-text` on `--warn-soft` at the card's head — Zofia Krawczyk, six hours behind |
| No time zone stated | `personNoZone` | no local time at all, and under the hours *No time zone is stated, so these hours are Hanna's own and are not converted to yours.* — Hanna Mazur. A zone the reader's device does not know reads the same, except the note: *The time zone Hanna states is not one this device knows, so these hours are Hanna's own and are not converted to yours.* |
| The organization | `personOrgCardShown` / `personChain` / `personReports` / `personNoReports` | a card of its own under the two: *Organization*, the chain of managers from the top down to the open member — each a row with its avatar, presence dot, name, and *title · presence*, joined by a 2 px `--line` stroke, and leading to that member's own contact — the open member marked on `--accent-soft` with the accent edge and leading nowhere; then *Direct reports*, indented behind a `--line` rule, ordered by name, each leading to their contact, or *No direct reports*. The reader stands in the chain or among the reports like anybody else and is drawn the same way, leading nowhere, because the reader has no colleague's page of their own. The structure is read apart from the member, so until it answers the card holds three skeleton lines under its title, and where it fails it says why in a warning notice — *Your session is no longer accepted, so this could not be read. Sign in again.*, *This credential may not read who reports to whom.*, *The server did not answer.*, *The server answered with something this client could not read. This is worth reporting as a defect.* — above *Try again*, or *This person is no longer a member of your organization.* with nothing to retry. Rows are 40 px, 44 under a finger |

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
| An unread conversation | `c.unread` | a run that ended while its conversation was not in front of the reader (`markUnreadAway`, `finishBackgroundRuns`) — as unread mail is, the title is 700 in `--text`, the time 600 in `--text`, and the preview `--text2`; no dot and no tint. Opening it reads it. Seeded on *Where are the Northwind reports?* (`c-links`) |
| A conversation running in the background | `c.running` | a run the agent is still working on — its preview line is what the agent is doing right now, in `--accent-d`. Seeded on *Which invoices are overdue?* (`c-overdue`), which ends 40 s after the prototype opens and turns unread unless it is the one in front |
| Row actions | `h.openMenu` | one `more_horiz` control per row opening the same menu as a right-click or a long press — *Select*, *Open*, *Archive* or *Restore*, *Delete* — instead of an archive and a delete glyph on every row |
| **Archive section exists** | `hasArchived` | at least one conversation is archived |
| Archive open | `archiveSectionOpen` | pressing the archive section |
| History resizer | `showHistoryResizer` | `!isMobile` — a pane boundary; a drag sets the history between 200 and 480 px, double-click returns it to 250 |
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
Pressed, a link in an answer asks as every link that leaves MailFathom does, and the question repeats the warnings
drawn beside it (§ *A link that leaves MailFathom*).

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
| A draft's footer, folded | `hasFooter` / `b.acc` | a `Draft` block whose sending account has a footer (`b.acc`, the work account in the sample), while it is not declined and its body came back: under the letter — or the field *Edit here* puts in its place — the line sent mail folds the reader's own footer to (§ *The thread*, *The reader's own footer, folded*): `chevron_right` and *Your footer is hidden* at 13 px in `--muted`, with no rule or box around it, a `role="button"` with `aria-expanded`, 28 px tall under a mouse and 44 under a finger. It starts folded so the footer does not lengthen the block; the letter above it ends at its own words, as the composer's AI draft does where the account has a footer. `ai-blocks.dc.html` draws the same folded line under each of its drafts |
| A draft's footer, open | `footerOpen` / `bkFooterOpen` | a press on that line — `expand_more` and *Hide your footer*, and the footer's lines unfold under it with `disclose`, drawn as sent mail draws them. Another press folds it. Each block keeps its own until the page is reloaded |
| A draft that leaves later | `hasSendAt` / `b.sendAt` | the person asked the Agent for a later send — *send it tomorrow at 8*, the *Reply to Contoso tomorrow at 8* conversation, or that ask typed anywhere — so the draft carries the time it leaves at. Under the letter, a line on `--accent-soft` inside an `--accent-line` border, radius 9: `schedule_send` in `--accent-d`, *This message leaves Fri 28.08 at 08:00, not when you press Send.* and *Europe/Warsaw time* in 12 px `--muted` — the profile's zone, worded as the composer's line is (§ *Composing*, *A time chosen*). *Send* reads the time as the composer's does, *Send Fri 08:00*, named *Send the draft to Anna Kowalska - it leaves Fri 28.08 at 08:00*. The time is the proposal's: nothing in the block clears or moves it, so a person wanting it sent at once or at another time asks the Agent, which proposes again. *Edit here* keeps it — the line stays under the field and *Send* still reads the time |
| A draft that leaves later, accepted | `accepted` with `b.sendAt` | *Send Fri 08:00* pressed: the time line goes and the result line says *Scheduled - it leaves Fri 28.08 at 08:00 and waits in the Outbox, where it can still be cancelled · 10:47*. The message is in the account's Outbox as a *Scheduled* entry (§ *The Outbox*), whose *Cancel sending* stops it |
| A draft that leaves later, not done | `failed` with `b.sendAt` | the service refused it — most often because the time passed before *Send* was pressed: the *not done* chip, the time line still standing, the result line *Could not schedule it. If its time has passed, ask for a later one. · 07:31* in `--warn-text`, and *Try again*. The *Blocks that did not come back whole* conversation carries it |
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
| User menu | `userMenuOpen` | the avatar, or the rail's alarm (below), which opens it with the details already open. Its first row, above the name, is the connection status |
| The connection status | `connStatus` / `connSentence` / `connDotStyle` | the first row of the user menu, and on the phone the first row of the *More* sheet's lower group — a 7 px dot and one sentence, `role="status"`, so a change of level is announced and no level is said by colour alone. Four values: **connecting** (`--faint`, *Connecting to the server…*) from start or sign-in until the first session read answers and the first opening of the live channel has had its 10 seconds, so a start-up never flashes amber; **online** (`--ok`, *Connected to the server.*); **delayed** (`--warn`, *Connected, with a delay.*); **offline** (`--err`, *Not connected to the server.*). It is this client's connection to MailFathom and nothing else — how current the mailboxes are is the freshness line's (*Connection summary*). The prototype cycles it on its own — online → delayed (channel) → delayed (mail server) → offline → online, every 60 s (`connCycleAt`) — and the `connection` property pins one |
| Its details | `connDisclosable` / `connDetailsOpen` / `connBlocks` | delayed and offline only: the row is a disclosure, its `expand_more` turning a half-turn open (`connChevronStyle`), and it opens one block per cause that holds, each its sentence over a `--faint` *Started … ago.* (a relative time from when that cause began). **Delayed, the live channel**: *Live updates from the server are not arriving, so what you see is current to the last refresh.* **Delayed, a mail account** that cannot reach its mail server for 60 s (`connCause` `delayedMailServer`), one block per such account: *\<account\> cannot reach its mail server. Its changes are kept in MailFathom and reach the mail server once it is back.* — the sample names *Nordwind board*. **Offline** outranks both and is drawn alone: *The server did not answer.* over *Trying again - attempt 2 of 5.* while the automatic attempts last, then the hint *Retry asks again now. Otherwise the client keeps asking on its own, at the latest at its next refresh within five minutes.* and a *Retry* button (`connRetry`, 44 px under a finger) that reads everything again. A machine with no network is the same level with its own sentence in the client; the prototype draws the server not answering. On a server with no live channel, the status that comes back after the server answers again is delayed, never online — the prototype's *Retry* goes to online |
| The rail's alarm | `connAlarmShown` / `openConnAlarm` | offline only, on a composition with a rail: a 40 px square (44 under a finger, the touch floor) on a 12 px radius, filled `--err`, the `cloud_off` glyph at 22 px in `--onaccent`, standing **directly above *Refresh*** in the rail's pinned foot and titled *Not connected to the server*. Pressing it opens the user menu with the details open |
| The phone's alarm | `moreConnStyle` / `moreTileDotShown` / `moreTileLabel` | the phone has no rail, so the status stands as the first row of the *More* sheet's lower group, above *Refresh* — 52 px like every row there, its dot in the glyph column — and opens the same details inline under itself. While offline that row is the alarm: `--err-soft`, the `cloud_off` glyph and its words in `--err-text`; and the bar's *More* tile carries the still `dot` of *A rail tile's status dot*, its name becoming *More - not connected to the server* |
| The reader's presence | `meStatusShown` / `meRailDotStyle` | a member of an organization only: the presence dot on the rail's avatar (13 px, 14 under a finger), and under the name in the user menu a row with the state in words and the status message — *Set a status message* in `--faint` where there is none — leading with `chevron_right` to the status menu. On the phone the same row heads the *More* sheet, 52 px with the avatar |
| The status menu | `statusMenuOpen` / `statusOnMain` / `mo.statusMenu` | that row — a 272 px `pop` beside the rail where the user menu stood, a `sheet` on the phone. **It is a menu, never the Settings screen, and it has no *Done* but *Duration*'s: every other choice takes the moment it is made.** Its first page: the avatar with its dot, the name, and the state in words (*Busy · 1 hour* once a while is chosen); the six states as menu radios, each a dot and its word, the current one on `--accent-soft` with `check` — *Hidden* adding *Others see you as Offline*; then under a rule *Duration*, *Message* and *Working from*, each a row showing its value and leading with `chevron_right` to a page of the same menu. **Picking a state takes it and closes the menu**, the way any menu's item does. Rows are 32 px, 44 under a finger. A press outside or back closes it, and on the phone, being a sheet, it is also pulled down to close as every sheet is (*A sheet*, under *What every touch composition holds to*: the grabber is what it draws, the pull is that row's rule rather than a handler of its own) |
| Its pages | `statusPageHead` / `statusBack` | *Duration*, *Message* and *Working from* replace the first page in place, under `arrow_back` and the page's name. A choice on *Working from* takes and returns to the first page, which then shows it; *Duration* takes on its *Done*; `arrow_back` returns without a choice |
| Duration | `statusDurationValue` / `statusOnDuration` / `durDone` | a *Duration* row (`timer`) under the six states and a rule, **whatever the current state**, showing the while — *Until changed*, or the state and its reset such as *Busy · 1 hour*. As Teams has it, it opens a page in place of the first one, headed `arrow_back` and *Duration*: *Status*, a combobox showing the chosen state's dot and word (`durPresenceOpen`), which opens in place under itself a list of the six states, each with its dot as on the first page and the chosen one on `--accent-soft` with `check` — not a native select, which cannot draw the dots — opened on the current state; *Reset status after*, a select of *30 minutes*, *1 hour*, *2 hours*, *Today* and *This week*, opened on *Today*; and **Done** (`--accent`), which takes both together and closes the menu. `arrow_back` returns without taking either. **A state picked from the first page always ends the while**, back to *Until changed* |
| Status message | `statusOnMessage` / `statusDraft` / `onStatusKey` | the *Message* row (`chat_bubble`, *Set a status message* in `--faint` where there is none): a two-line field of at most 280 characters, *Saved on Enter or when you go back* beside its count (*22 / 280*, `--warn-text` from 260), *Clears* as four equal segments — *Never*, *1 hour*, *Today*, *This week*, taking at once — and *Clear status message* (`backspace`, `--warn-text`) while there is one. Enter, `arrow_back`, or closing the menu keeps what was typed |
| Working from | `statusOnWork` / `statusWorkRows` / `statusWorkValueStyle` | the *Working from* row (`apartment`), showing the choice, or *Not stated* in `--faint` until the member says: a page that is a dialog rather than a menu, like *Duration* and *Message*, holding four radios in a group named *Working from* — *Office today*, *Office until changed*, *Remote today*, *Remote until changed* — each taking at once and returning, none of them checked while nothing is stated |
| Settings | `settingsOpen` | the settings control; closing it also drops the open account, the folder being edited and the footer being edited |
| Settings: its seven sections | `tabProfil` / `tabAway` / `tabAccounts` / `tabReading` / `tabAi` / `tabNotif` / `tabApp` | *Profile*, *Availability*, *Accounts*, *Reading*, *AI*, *Notifications*, *Application*, `tabProfil` being the default. On two panes a section navigation (`settingsNavShown`) groups them under *You* (*Profile*, *Availability*), *Mail* (*Accounts*, *Reading*), *Assistant* (*AI*, *Notifications*) and *General* (*Application*) (`settingsNavGroups`), each row an icon and a label; on one pane they are a tab strip that scrolls sideways (`settingsStripShown`). Changing section closes an open account, folder or footer |
| Settings: finding a section | `settingsQuery` / `settingsNavEmpty` | *Find a setting* at the top of the section navigation narrows it by a section's name, its description and the names of the settings in it — *phone* finds *Profile*, *footer* finds *Accounts*, *theme* finds *Application*; nothing left reads *No section matches.* |
| Settings: the section head | `settingsSectionTitle` / `settingsSectionHint` | two panes only — the section's name at 22 px over one sentence saying what it holds |
| Own photo | `meHasPhoto` | a photo was uploaded |
| **Picture rejected** | `photoError` | a file that is neither a JPEG nor a PNG (*That file is neither a JPEG nor a PNG, so it was not sent.*) or one over 1 MB (*That picture is larger than 1 MB, so it was not sent.*) — a real file has to be chosen, so **no click reaches this** |
| Telemetry off | `telemetryOptOut` | the telemetry switch |
| Notification centre | `notifCenterOpen` | the bell, or the phone's upward gesture; on the phone it covers the whole screen, the bottom bar included (§ *The notification gesture*); on the upright fold and tablet it slides in from the start edge and stops above the bottom bar |
| Unread notifications | `notifHasUnread` | the unread count; it gates three regions including the rail badge. The panel's own badge is one composed string, `notifNewLabel`, reading *1 new* or *N new* |
| The centre's filter | `notifFilter` / `notifTabs` | **it opens on *Unread*** — the first of the two, *Unread* then *All*; `st.notifFilter` falls back to `"unread"` |
| **No notifications** | `notifEmpty` | the shown list is empty — *Nothing new. All read.*, reached by opening the centre with nothing unread |
| A notification's row | `notifListStyle` / `n.style` | every notification is a row of its own, separated from the next the way the Mail list's rows are: the list is `--sub` with 1 px between rows, and each row is `--panel` with the list row's `--inset` hairline along its top and a `0 1px 2px` `--sh-1` shadow, 13 × 16 px inside. Unread is said by the headline's weight, the dot and the read control drawn in `--accent-soft` with its `--accent-line` border, never by tinting the row. A pointer resting on a row takes `--hover`; a selected row takes the selection's `--accent-soft` and its accent edge (`selMark`) |
| Swiping a notification | `n.swiping` / `notifSwipeId` | under a finger (`touch`) a row is carried left or right on a row's swipe numbers — engaged at 14 px, given up as a scroll once it drifts 26 px and more down than across, carried at most 148 px. **Either way it toggles the row's read state**, as its read control does: an unread row is marked read, a read one unread. Behind the row, on the side it leaves, `--accent-soft` shows `mark_email_read` and *Mark read* — `mark_email_unread` and *Mark unread* on a read row — in `--accent-d`, at 55% until the finger passes 96 px, where the label is full and the glyph heavier. Released past 96 px it acts, and the lift does not also open the notification; short of it the row springs back in 160 ms. Under *Unread* a row swiped read leaves the list as it would on its read control. A finger that has gone more across than down is the row's, so the panel's own pull-down does not take it, and the long press is called off as the finger moves. A mouse never swipes: the read control and *Mark as read* in the row's menu stay its way to the same thing |
| A new message | `notifItems` | **one row per arrived message** while an account has five or fewer unread arrivals: the sender's name as the headline (their address where the message names none), the subject as the second line — *No subject* where it has none — and *Mail · \<account\>* as the source; opening it opens that message. A toast arriving with it says the same two lines, and pressing that toast anywhere opens the message as the row does (`pushNotif`) |
| **Merged arrivals** | `n.merged` | the sixth unread arrival of one account folds all of them into **one row**, *\<N\> new messages* over *More than five arrived unread, so they are gathered here.*, and later arrivals fold into it while it stays unread; once it is read the next arrival starts a row of its own again. It names no sender and no subject. Opening it opens the mail list; reached in the sample as *8 new messages* from Fabrikam |
| Summarize | `n.summarize` / `summarizeNotif` | the merged row's only extra control, an `auto_awesome` pill reading *Summarize* under the source line (30 px, 44 px under a finger), and *Summarize* in that row's menu. It marks the row read, closes the centre, and opens the Agent space on a new conversation under the chip *\<N\> new messages · \<account\>*, asking *Summarize these \<N\> new messages* at once — the summary covers exactly the messages the row gathered |
| A failure after the answer | `n.failure` / `NKIND.failure` | three failures land after the request was already answered, and each is a durable row — unread, counted in the badge and under *Unread* like any other, with the shared menu. A refused send and an abandoned change take the `failure` kind (`error` on `--err-soft` in `--err-text`); a draft out of step takes `draftcopy` (`cloud_off` on `--warn-soft` in `--warn-text`), matching its toast's warning. **A refused send**: *Not sent - it waits in the Outbox* over the entry's subject, *Outbox · \<account\>*; opening it opens that Outbox with the entry picked, as *Show in Outbox* does. **An abandoned change**: *A change did not reach your mailbox* over *Move - the mail server would not take it, so the message is still where the server holds it.* — the act the change record names, never its destination or its message, which the durable row does not carry —, *Mail · \<account\>*; opening it opens the thread, whose message carries the notice (`m.changeShown`). **A draft out of step**: *Draft out of step with your server* over the draft's subject, *Drafts · \<account\>*; opening it opens that account's Drafts and the composer on the draft (`composeNoServerCopy`) |
| Its decisions | `n.pills` / `n.hasPills` | under the source line, in the *Summarize* pill's style and size (30 px, 44 under a finger), always drawn rather than on a hover, and repeated in the row's menu. The refused send: *Send again* (`send`) and *Edit* (`edit`) — each marks the row read, closes the centre, and does the Outbox's own decision on that entry there, so its decision under way is seen (`decideRefusedNotif`). The abandoned change: *Ask again* (`refresh`) — *Move again…* for a move, as on its message — and *Let it go* (`close`) — the centre stays open, the row is marked read and loses its pills, the message's notice goes, and *Ask again* raises *Asked again* (`decideChange`). The draft row has none: pressing it is the whole act |
| Raised while the client is open | `raiseFailure` / the `raise` property | the open client raises the durable row's own toast (`n.toast`), held 8 s: the refused send as `error`, *Not sent - it waits in the Outbox* — *“…” is in the Outbox of \<account\>. Send it again or edit it there.*; the abandoned change as `error`, *A change did not reach your mailbox* — the row's own sentence, *Move - the mail server would not take it, so the message is still where the server holds it.*; the draft as `warning`, *Draft out of step with your server* — *“…” is kept in MailFathom, but your mail server's copy may be missing or out of date.* None draws a button: the card pressed anywhere opens what the row opens and marks it read (§ *Toasts*, which also says what the client names it) |
| One failure, one voice | — | **when the open tab is already following a change** — its pending-change line in the connection summary (`m_pending` in the Client States file) — that line and its toast stand for the failure, and no second toast is raised for the durable row. Deciding it there, *Ask again* or *Let it go*, marks the durable row read, so one failure is never shown as two. The durable row is still listed in the centre. The prototype draws no pending-change line, so it always raises |
| Writes refused while offline | `writeRefused` / `a.refused` / `composeSendRefused` | **while the status is offline every control that writes says so before it is pressed**, rather than failing after: it is drawn as a control that cannot act — `aria-disabled`, at 60% with a not-allowed cursor — and its name bubble, which a finger reaches with a tap, names the control and the reason, *Archive - no connection to the MailFathom server.* The prototype draws it on the toolbar's and the selection bar's mail acts (archive, delete, flag, unread, move, spam) and on the composer's *Send* and *Save draft*; the rule holds for every control that writes, in every space — a folder act, a row's menu, a task, an event, a contact, a setting, a notification's read state, an Agent question — and the client meets it everywhere. Reading, opening, and composing stay as they are. A write that was already on its way when the connection went is *outcome unknown* (§ *Toasts*) |
| Connection summary | `freshText` / `freshDotStyle` | the account freshness line: a green dot and *Every account is up to date.* at the foot of Mail's folder column while it is expanded (`sideExpanded`; a collapsed column draws none) — and nowhere else: Discover, Agent, Tasks, Calendar and People draw none, because how current the mailboxes are is said where mail is read. **When every account is current it is green and nothing more** — no disclosure, no per-account rows, no times. **Without a connection to MailFathom** (`freshUnknown`, the status offline) it does not know, and says so: a `--faint` dot and *Mailbox status unknown: no connection to the MailFathom server.* — never green. It names MailFathom on purpose, the one sentence that has to be told apart from the mail server it is otherwise about. It says nothing about this client's connection beyond that; the status in the user menu does. An account behind or stopped is `m_sync` in the Client States file, and changes on their way are `m_pending` |
| Refresh | `refreshAll` / `refreshBusy` | the `refresh` control in the rail, titled *Refresh* (44 px under a finger), and on the phone the *Refresh* row of the *More* sheet (`moreRefresh`), which closes the sheet as it starts, and under a finger the Mail message list pulled down from its top (*Pulling the list to refresh* under § *What every touch composition holds to*) — it re-reads the mail in front of the reader and the notification centre, pulses its glyph (`mfpulse`, 1 s) rather than spinning while it waits, and refuses a second press until the re-read lands. While the status is offline it does what *Retry* does as well: it asks the server again |
| Move dialog | `moveOpen` | the move action on a selection — grouped by mailbox (`moveGroups`), each group ending in *New folder here*, and the toast it produces names the mailbox |
| Permanent delete | `confirmDel` under `inTrash` | a delete action while the open folder is a Trash — *Delete permanently*, no undo offered, and the toast reads *Permanently deleted* rather than *Moved to trash* |
| The order of the spaces | `railSepStyle` | *Discover* and *Agent* first — the two places that ask questions of the mail — then a 1 px `--line` hairline inset 16 px from either side of the rail, then *Mail*, *Tasks*, *Calendar* and *People*. The bottom bar carries the spaces in the same order, the bell and *More* after them, and draws no hairline: its places are counted |
| A rail tile's status dot | `agentDotShown` / `agentDotKind` (`data-rail-dot`) | an 8 px `--err` dot in the top end corner of the tile, 8 px in from both edges — on the bottom bar in the corner of the glyph's pill, 6 px in — clear of the glyph. **`pulse`** while something there is under way, a 5 px ring going out of the dot every 1.6 s (`mf-rail-ring`), which never crosses the tile's edge; **`dot`**, still, while nothing runs and something waits to be read; nothing otherwise. Under reduced motion `pulse` is the still dot. The tile is a `role="button"` that takes focus and answers Enter and Space (`goAgentKey`), and its name carries the counts (`agentTileLabel`, *Agent - 1 working, 1 unread*), so a screen reader hears what the dot and the peek show. Drawn on *Agent* today — working is a background run (`c.running`), the scripted example, or a pending reply; unread is `c.unread` — and written so any other tile can carry one |
| A rail tile's peek | `railPeekShown` / `railPeekSections` | a pointer resting on the tile, **under a fine pointer only** — a finger gets the dot and opens the place itself, where the same facts are drawn (the history's weight and preview line). A 300 px `--panel` card 10 px beside the rail, its beak level with the tile's middle, arriving as `pop`; the pointer may cross into it, and it goes 160 ms after the pointer leaves both, or when the tile is pressed. Its head is the place's name and its counts (`railPeekMeta`), then one section per kind with rows under it — for *Agent*, *Working on* (a spinning `progress_activity` and the status in `--accent-d`) and *Unread* (an `--err` dot, a 700 title, and when the reply landed — never its words, which the history list a deployment serves does not carry); a row opens that conversation. With neither it reads *Nothing running and nothing unread.* Another place supplies its own sections to `railPeeks` |
| The rail's nav scrolls | `railNavStyle` | a viewport too short for the six destinations — the nav scrolls and the bottom cluster stays pinned |
| A shared mailbox | `g.shared` / `mb.shared` / `mg.shared` / `SHARED_MARK` | an account several people read and send from — *Board*, `board@nordwind.example` — carries *Shared* in a bordered 11 px chip beside its name wherever an account is drawn or chosen: its group in the folder column, the user menu's *Mailboxes*, and the Move dialog's groups. The Settings screen is left as it stands |
| The *More* sheet | `moreOpen` / `moreItems` | the phone's *More* — the reader's presence row first for a member of an organization, then *Tasks*, *Calendar* and *People*, the current one on `--accent-soft`, then under a rule the connection status (*The phone's alarm*), *Refresh* (`moreRefresh`) and *Settings* (`moreSettings`); every row 52 px |
| A rail destination | `railBase` / `activeStyle` | on the bottom bar a place whose glyph sits in a 56 × 32 px pill, filled `--accent-soft` on the current one (`--nav-mark`); on the rail a tile inset 7 px from either side of the rail on a 12 px radius, 4 px apart (2 px below 1020) — the current one on `--accent-soft` with an accent glyph and a semibold label, hover on `--hover`. No full-width slab and no edge bar, so neighbouring tiles never meet across a dark gap |
| Cancel confirmation | `cancelAskOpen` | closing a toast that carries a running operation |
| Toasts | `toasts` | any operation that reports; `toasts.dc.html` is the whole of it |

Every label in the prototype is English, among them the Settings tabs *Profile* and *Application*, the user menu's
*Sign out*, and the single-pane state toggle beside the thread head, *Hide panels - correspondence only* against
*Show thread panels* — and `design/parity.json` presses two of those under their English names.

### A link that leaves MailFathom

**No link that leaves MailFathom opens on the press that found it.** A text link or a picture inside one, an `http` or
`https` address, pressed with the main button or the middle one, asks first wherever it is drawn — the simplified text of a message, its original, an agent's answer,
an event's detail, and every surface added after them, because the rule is one interception in the artboard's logic
(`onLinkClick`, and the frame's `LINK_SCRIPT` for the original) rather than a dialog any screen owns. A screen that
draws a link therefore draws nothing extra for it. Spam is outside the rule rather than an exception to it: its links
are words, never anchors (§ *Spam*, *A link in spam*), so nothing in it can be pressed.

| State | Flag | Reveal |
|---|---|---|
| The question | `linkAskOpen` | a link pressed whose host the person does not trust: a short question, `pop` at every width, 400 px off touch and 344 px under a finger, centred on the whole screen with its scrim over the bottom bar on the phone, as a modal dialog is drawn. An `open_in_new` glyph in `--warn` beside the title, *Open this link?*, or *Open the link behind this picture?* where what was pressed is a picture, over *It leaves MailFathom and opens in your browser. Open it only if you expected to go there.* Under it, on `--sub` inside a `--line` border, the host in 15 px semibold and the whole address beneath it in 12 px Geist Mono `--muted`, wrapped anywhere and selectable, so a lookalike host or a long tracking address is read rather than guessed. Then *Cancel*, which takes focus, beside *Open link* in `--accent`, both 34 px tall and 44 px under a finger, and each pressed by Enter or Space as well as by a click, with a 2 px `--accent` focus ring. *Cancel*, its scrim, *Escape* and back close it and nothing opens |
| What the link already said | `linkAskWarnings` | each warning the link carries, one line each in 13 px `--warn-text` behind a `warning` glyph: an agent's answer passes its judgement on (`data-link-warn` — *This link does not go where its words say. It goes to …*, *This address is in none of the messages this answer cites.*), and a link carrying no judgement whose own words name another host than the one it goes to says so — *Its words name travel.example, but it goes to click.travelmail.example.*, which the tracked check-in link in *Tickets Kraków-Berlin* draws A picture has no words to read, so it carries none of the second kind |
| Opened | `confirmLinkAsk` | *Open link*: the question closes and the address opens in a new browser context with no referrer (`noopener`, `noreferrer`), so the page reached learns neither where it was linked from nor how to reach back |
| A trusted host | `hostTrusted` / `trustedLinkDomains` | the host is on the person's own list of trusted domains, or a subdomain of one: the link opens at once, the same way *Open link* opens it, and no question is drawn. The sample list is *nordwind.example*, so *files.nordwind.example* in *Trade show materials 24.09* opens straight away, and *northwind-billing.example*, which only borrows a name like it, still asks. The list is kept per person by the service, and **no screen edits it yet** — Settings draws nothing for it |

### The settings panel

**Settings take the whole window at every composition**, the desktop included, because the list of
sections grows past what a dialog has room to navigate. There is no card, no grip, no drag and no
stored size: the panel is a full-window surface on `--bg` that arrives with `pop` on two panes and
`sheet` on one. Its head is a 22 px `settings` glyph, *Settings* and the close control. On two panes a
264 px section navigation (220 below 1020) runs down the left on `--sub`, with *Find a setting* at its
top and the version and build at its foot, and the section itself scrolls on the right in a column at
most 760 px wide. On one pane the sections are a tab strip under the head and the version sits in a
footer.

**The panel draws every setting a person may change, and nothing the client cannot write.** The
sections, their settings, and every write's answer are `logic/prototype/settings.js`; the person's
sample settings are `data/prototype/settings.js`, and each account's folders and footer are
`mailAccounts`, `footers` and `ownFooters` beside them. Every control in a section stands at 44 under
a finger — a field, a select, a button, a switch row, a navigation row — and a segment in its track
and a day chip stand at 36 with the rest of the 44 around them. One choice among several is a
segmented track: the person's language, *Message view*, the interface language, the theme, the list
mode, and how a folder is found.

#### Profile — `tabProfil`

| State | Flag | Reveal |
|---|---|---|
| The name | `meName` / `onMeName` / `saveMeName` | the field beside the picture; a change is written when the field is left |
| A name refused | `nameProblem` | a name left blank or past 128 characters, at once and without a write — *That name was not accepted. It cannot be blank or longer than 128 characters.* — and the same sentence when the server refuses one (`settingsWrites` `refused`) |
| The name held by the administrator | `nameLocked` / `nameManagedNote` | `settingsAccess` `nameManaged` — the field is read-only on `--bg` under *Your name is managed by your administrator, so it is shown here rather than offered for you to change.* The rest of the section stays writable |
| The picture | `pickPhoto` / `meHasPhoto` / `removePhoto` | *Picture*, then *Remove* while there is one, which asks first; *JPG/PNG, up to 1 MB* beside them |
| The language | `personLangOptions` | *Language* — English or Polski as one segmented track. **It is the person's own language**: what MailFathom writes to them in. The screens' language is the interface language under *Application*, a separate setting kept on the device |
| The time zone | `tzValue` / `tzOptions` | a select of zones by place and name, under which every date is written and from which *this week* is reckoned |
| The phone numbers | `phoneRows` / `phonesEmpty` | one row per number — a label select (*Mobile*, *Work*, *Home*, *Other*), the number, and `close` to remove it. The first carries *First* (`call`, `--accent-soft`) and *The number colleagues call*; every other carries *Move to first*. *No number yet - your card shows none.* where there is none |
| A number refused | `ph.problem` | a number with no digit, with a character other than digits, `+`, spaces, dashes, brackets and dots, or past 32 characters — said under that row at once, and the number is not written until it is put right |
| Sixteen numbers | `addPhoneFull` | *Add a number* is `aria-disabled` and its tap says *A card holds 16 numbers at most - remove one first.* |
| The preferred address | `addrOptions` | a radio per address of the accounts assigned to the person, the account's name under it and *Shared* on a shared mailbox |

#### Availability — `tabAway`

| State | Flag | Reveal |
|---|---|---|
| The working days | `whDays` | seven day chips, Monday first, each toggling on a tap |
| The working hours | `whStart` / `whEnd` / `whTimesShown` | two time fields, *to* between them — one range for every day worked |
| No working days | `whNone` | every chip off — *No working days - your card says nothing about when you work.* and the time fields go |
| Hours refused | `whProblem` | an end at or before the start — *The end has to come after the start - one range for every day, and none across midnight.* — and nothing is written while it holds |
| The hours read in the zone | `whHint` | always — the hours are read in the person's own time zone, named, and decide nothing but what a colleague reads on the card |
| The automatic reply switched | `arOn` / `toggleAr` | *Answer mail automatically* |
| Where the reply stands | `arStatus` / `arStatusIcon` | the saved reply, not the draft: *Off - nobody is answered.*, *Starts … , and answers until …*, *Answering now, until …* in `--ok-text`, or *The period ended …, so nobody is answered.* |
| The period | `arStarts` / `arEnds` | *From* and *Until*, a date and a time each |
| The accounts it answers on | `arAccounts` | a checkbox row per account; a shared mailbox is drawn dimmed and `aria-disabled`, and its tap says *A shared mailbox is never one person's to answer for.* |
| The two texts | `arOutside` / `arInside` / `arInsideShown` | *To people outside your organization* and *To colleagues in your organization*, each with its count out of 8000. The second may stay empty — *Left empty, colleagues get the text above.* |
| Outside an organization | `arNoOrgNote` / `arOutsideCaption` | `inOrganization` off — one text, captioned *The reply*, and *You are in no organization, so one text answers everybody.* |
| A reply that cannot be saved | `arSaveOff` / `arProblem` | no outside text, a text past 8000 characters, no account, or a period that ends before it starts — *Save automatic reply* is `aria-disabled` and one sentence names everything still missing |
| A reply saved | `arSaved` | *Save automatic reply* — *Saved. Every sender is answered once under this reply, even one an earlier reply already answered.*, because every save starts the reply over |

#### Accounts — `tabAccounts`

| State | Flag | Reveal |
|---|---|---|
| The account list | `acctListShown` | the section with no account open — one row per account carrying a colour dot, the display name and *Shared* where it is one, the address, and what it holds (*9 folders · Footer set by your organization*, *4 folders · Your own footer*, *1 folder · No footer*), under a `Mail accounts` caption beside `acctCount`, and under the rows *Your administrator sets up each account, with its address, its mail server and how MailFathom signs in to it. Open one to decide everything else about it.* |
| One account | `acctEditShown` / `acctTitle` | a row — `arrow_back` and the account's name |
| The address | `acctMail` | a card on `--sub` stating it as a fact, not a field, under *The address is what identifies the account, and your administrator sets it, with the mail server and how MailFathom signs in to it. Everything below is yours to change.* |
| The account's own settings | `acctGroups` | seven groups under the card, each under a `--line` rule and a caption, every control a row of one kind — a field, a number, a select, a segmented track, a switch row or a row of chips — with its label above and its hint below. *The account*: *Name* (the account's display name), *Language* (*English* / *Polski* — what MailFathom writes about the mailbox's mail on its own, for everyone it is assigned to), *New mail* (*On a schedule* / *As it arrives*, its hint following the pick). *Deleting*: one switch, *Delete mail permanently - here and on the mail server*, off by default — off, a message deleted is only flagged on the mail server, so another client that offers an undelete can still bring it back; on, it is removed there at once; either way MailFathom's own copy is erased, and the hint says so for the side picked. *Recognizing senders*: the server whose checks the account believes, in monospace, empty for what MailFathom knows; *Recognize everybody at your own domains*; *Mark only senders something established*. *Contacts*: *Collect contacts from this account's mail*, the messages before somebody is recorded, the contacts recorded per run, and *Never recorded* — one row per exclusion, its text in monospace, *Domain* or *Pattern*, *With subdomains* as a chip on a domain, and `close`, then a field whose button reads *Add pattern* or *Add domain* by what is typed (`@`, `*` or `?` make a pattern), or *Nobody is left out.* *Spam*: *Classify this account's mail as spam*, *Folders it reads* as a chip per folder (the inbox picked where none is named), *Move spam to the junk folder*, *Junk folder* (*The account's Spam folder* or a named one), *Mark spam read*, *Tag spam for other mail clients*, *Ask a model about mail the rules did not decide*, *Let the model name what kind of spam it is*, and *Model's threshold*. *Sensitive content*: *Credentials, tokens and keys* and *Personal data*, each *As the deployment* / *On* / *Off*, and *Stop outgoing mail that carries* as two chips, *Credentials* and *Personal data*. *Sending*: *Send from this account*, *Send as* (placeholder the account's own address), *Name beside it*, *File a copy in Sent*, *Take it back when the provider files one too*, *Sign with S/MIME* and *Sign with OpenPGP*, each a select of *None* and the material provisioned for the person |
| A setting that depends on another | `it.dim` / `it.refused` | the contact numbers and *Never recorded* while collection is off, *Folders it reads* while classification is off and *Junk folder* while spam is not moved are drawn dimmed and still change; *Let the model name what kind of spam it is* while the model is off (*Turn on the model first.*) and *Take it back when the provider files one too* while no copy is filed (*Turn on filing a copy first.*) are `aria-disabled` and say why on a tap |
| Unsaved changes | `acctDeclSaveOff` / `acctDeclDiscardShown` | the groups are one draft: *Save account* under them is drawn flat and says *Nothing has changed yet.* on a tap until something changes, and *Discard changes* stands beside it while something has. *Folders and the footer are saved on their own, below.* under both |
| An account that cannot be saved | `acctDeclProblem` | no name or one past 128 characters, a checked-server value with a space, fewer than one message or one contact per run, no folder to classify while classification is on, a threshold outside 0 to 1, or a sending address that is not one — the first such reason stands above *Save account*, which is `aria-disabled` |
| An account answered | `acctDeclOutcome` / `acctDeclOk` / `acctDeclBad` | *Save account* — saved: *Saved. The account works this way from its next run.*, and a new name is the account's title at once; refused, the service's sentence (*The server did not take that change: this deployment screens outgoing mail for personal data, so this account may not stop screening for it.*), the draft kept; not stored or not answered, the draft kept; moved, *This account was changed somewhere else since this screen read it, so nothing was saved. It is shown as it stands now - make the change again.*, the draft dropped |
| The folders | `fdRows` | one row per folder in a bordered list: the role's glyph or `folder`, the name, and where it is found — *Found by its role - the server's Spam folder*, or its path on the mail server in monospace, levels split by ` / `. A chip on the row names every switch off its default: *Not synchronized*, *Hidden from AI*, *Not indexed*, *Created if missing* |
| A folder open | `fd.editing` | a press on its row — the row takes `--accent-soft` and opens beneath it: *Name*; *How MailFathom finds it*, *By its role* or *By its path*, with a role select (*Inbox*, *Sent*, *Drafts*, *Archive*, *Spam*, *Trash*) or a monospace path field; and four switch rows — *Synchronize*, *Visible to AI*, *Index for search by meaning*, *Create it if it is missing*. One folder is open at a time |
| A switch that cannot act | `fs.refused` | *Index for search by meaning* while *Visible to AI* is off (*Turn on Visible to AI first - an index is a model's reading of the folder.*), and *Create it if it is missing* on a folder found by its role (*A folder found by its role is one the mail server already has.*) — drawn dimmed, `aria-disabled`, saying why on a tap |
| A folder that cannot be saved | `feProblem` / `feSaveOff` | no name, a name the account already has, no path, or a role another folder of the account already plays (*A role belongs to one folder of an account, and Spam is already Spam's.*) — *Save folder* is `aria-disabled` and the reason stands above it |
| A new folder | `addFolder` | *Add a folder*, under the list while no folder is open — a row *New folder* opens at the foot of the list, found by its path, with three switches on and *Create it if it is missing* off |
| A folder no longer read | `feRemove` | *Stop reading it* in an open folder, which asks first: *Stop reading Projects?* — the account stops reading it on its next run, and nothing on the mail server is touched |
| A folder change answered | `folderOut` / `folderOutOk` / `folderOutBad` | *Save folder* or a confirmed removal — saved: *Saved. The account takes the change on its next run.*; and one sentence per refusal below |
| The organization's footer | `ftOrg` / `ftLines` | an account whose organization sets the footer — a `corporate_fare` note that a footer of the person's own cannot be written there and that it fills in their name, job title and number, over the footer as it goes out, in a dashed frame |
| The person's own footer | `ftOwn` / `ftOwnActions` | the footer as it goes out, an uploaded image drawn as an `image` chip naming it, over *Edit footer* and *Remove footer*, which asks first |
| No footer | `ftNone` / `ftNoneActions` | *Messages from this account go out without a footer.* and *Write a footer* |
| The footer being written | `ftEditing` | *Edit footer* or *Write a footer* — *Markup* in a monospace field; under it how an image is referred to (`cid:` and its name) and why a linked one is refused; *Images*, one row per uploaded image (`cid:wave`, its file and size, `close`), or *No image uploaded.*; *Upload an image*; and *Preview*, the footer as the markup reads now |
| A linked image | `ftProblem` / `ftSaveOff` | markup with an image whose source is a web address — *An image is linked rather than uploaded: …* names it, and *Save footer* is `aria-disabled` |
| A footer answered | `ftOutcome` / `ftOutcomeOk` / `ftOutcomeBad` | *Save footer* or a confirmed removal — saved: *Saved. The next message you send from this account carries it.*; and one sentence per refusal below |

**An account is neither added nor removed here, and nothing about reaching its mail server is.** Its
address, login, password, servers, ports, transport security and OAuth, *Test connection*, how far
back it fetches, what becomes of MailFathom's copy when mail or a folder is deleted, what its rules
may do, both of its audit trails and the mail rules themselves are the
administrator's, and the client has no way to write any of them, so none is drawn — the address alone
is shown, as what identifies the account. A client screen offering one is offering something the
design does not have. A setting a settings policy holds is not drawn as held: the service refuses the
write and its sentence is what the person reads.

#### Reading — `tabReading`, AI — `tabAi`, Notifications — `tabNotif`, Application — `tabApp`

| State | Flag | Reveal |
|---|---|---|
| The message view | `viewAiStyle` / `viewSimpleStyle` / `viewHtmlStyle` | *Reading* — a `Message view` track of three, *AI simplified*, *Simplified* and *Original* in that order, each drawn by one `segStyle` helper that is the only place the segment's own styling lives |
| The whole thread expanded | `autoExpand` | *Reading* — *Expand the whole thread automatically* |
| Read when opened | `markReadOnOpen` | *Reading* — *Mark a message read when it opens*, on by default; off, opening a message in the list leaves it unread |
| The AI filters shown | `aiFiltersShown` | *Reading* — *Show the AI filters in the folder tree*, on by default; off, the folder tree draws no *AI filters* section at all |
| The AI's language | `aiLangName` / `goProfile` | *AI* — a card naming the person's language, *Your language, set in Profile*, and *Change*, which opens *Profile*. **There is no second language here**: the AI writes in the person's language, and its copy says that what MailFathom reads out of a mailbox on its own follows the language set for that mailbox |
| Matching the message | `aiMatchReply` | *AI* — *Match the language of the message*: draft replies follow the sender's language, everything else keeps the person's |
| Notifications cleared on read | `notifAutoClear` | *Notifications* — the switch; while it is on, a `Remove` select offers Right away, After 1 hour, After 24 hours and After 7 days |
| Notified on this device | `notifDevice` | *Notifications* — the `This device` group, off by default — *When the window is not in front of you, this device tells you how many things arrived and of what kind - never from whom, never what it is about.* |
| Notifications blocked by the browser | `notifBlocked` | `notificationPermission` `blocked` — the switch is drawn off, dimmed and `aria-disabled` (*This browser blocks notifications from here.*), over a warning: *This browser has been told to block notifications from here. Allow them in its own site settings, and this switch will work again.* |
| How long one stays up | `notifDuration` / `notifDurationLabel` | *Notifications* — the range, 1 to 30 whole seconds and 5 by default, drawn as `<n> s` in the accent; its copy ties the value to the undo window, which *waits exactly as long as its notification is up* |
| The interface language | `langOptions` | *Application* — English or Polski as a track, **English first and English selected**; kept on the device, so the sign-in screen is in it too |
| The theme | `settingsThemeSegs` / `themeHint` | *Application* — *System*, *Light*, *Dark*; *System* follows the device. The user menu's own *Theme* row sets the same value |
| Mail in tabs | `openInTabs` / `tabsWidthNote` | *Application* — *Open mail in tabs*, the same value as the user menu's *Tab mode*; below 1180 px a note says tabs need that width and mail opens beside the list either way |
| The list mode | `listSegs` | *Application* — *The mail list shows*: *One message per row* or *One conversation per row*, the same value as the list's own switch |
| Images loaded automatically | `autoImages` | *Application* — while it is on, a warning states the image is fetched from the sender's server and is how tracking pixels work, and every message opens with its images rather than the *Remote content removed* card, a message marked spam excepted |
| Telemetry forwarded | `telemetryForwarded` / `telemetryDestText` | `telemetry` `forwarded` — *Don't send telemetry data*, and under it where telemetry goes and what it never carries |
| Telemetry not forwarded | `telemetryNotForwarded` | `telemetry` `notForwarded` — no switch, and a `cloud_off` note: *This server takes no telemetry from this client, so it sends none and there is nothing to turn off.* |
| Telemetry not answered yet | `telemetryUnanswered` | `telemetry` `unanswered` — the switch, and a pulsing `hourglass_empty` note: *Waiting for this server to say whether it forwards telemetry. Until it answers, your own decision is what holds.* |
| Telemetry withheld | `telemetryOptOut` | the switch on — a warning that support gets harder without it |
| Layout reset | `resetLayout` / `resetLayoutDone` | *Reset layout* under `Layout on this device`; the confirmation *Layout restored to the defaults.* stands for 3 200 ms and then goes |

#### What a write answers, and what this sign-in may change

Every setting the server holds is written when it changes — a field when it is left — except the
automatic reply, an account's own settings, a folder and a footer, which each have a save of their own. A write answers as the
`settingsWrites` property says, and **a refused write leaves the value as it was and says so where the
setting stands**:

| Answer | `settingsWrites` | What is said |
|---|---|---|
| Saved | `saved` | nothing for a setting written as it changes; a sentence in `--ok-text` for the automatic reply, an account, a folder and a footer |
| Refused | `refused` | the setting's own *was not accepted* sentence — *That time zone was not accepted, so your dates are still written in the old one.* and the like for the name, the language, the numbers, the address and the hours; *That was not changed. A notification stands for a whole number of seconds, between 1 and 30.* for the duration; for a folder the service's reason (*the mail server names no folder at that path, and this folder may not create one*); for an account the service's sentence (*… this account may not stop screening for it.*); for a footer *Your organization now sets the footer on this account, so yours was not saved.*; for the reply the account it named (*Nordwind board is a shared mailbox …*) |
| Not stored | `notStored` | the setting's *was not saved to the server* sentence — *That name was not saved to the server, so it still holds you under the old one.*, *That picture was not saved to the server, so you are still drawn as you were.*, and one for every other setting |
| Not answered | `unanswered` | *The server did not answer, so it is not known whether that … was saved.*, naming the setting |
| Moved | `moved` | a folder or an account's own settings only: *These folders were changed somewhere else since this screen read them, so nothing was saved. They are shown as they stand now - make the change again.*, and the account's sentence above. Any other setting answers it as *not stored* |

A setting that follows the person between machines — *Reading*, *AI*, the theme, tabs, the list mode,
images, telemetry and the notification-centre settings — says its answer once, under the section it
was changed in (`readingProblem`, `aiProblem`, `notifProblem`, `appProblem`).

**What waits on the server is shown, not offered, when it cannot be written**, and each such section
opens on a `lock` note saying why. The interface language, notifications on this device and *Reset
layout* stay writable in every case, being the device's own.

| Case | Reached by | Profile and Availability | Accounts | Reading, AI, Notifications, Application |
|---|---|---|---|---|
| No connection | `connection` `offline` | *There is no connection to the MailFathom server, so your name, your picture, your language, your time zone, your numbers and your address are shown here rather than offered for you to change until it is back.* | *… so this account is shown as it stands rather than offered for you to change until it is back.* | *There is no connection to the MailFathom server, so the settings that follow you between machines are shown here as they stand until it is back. The interface language and what this device does are still yours to set.* |
| This sign-in may only read | `settingsAccess` `readOnly` | *This sign-in may read what the server holds about you and may not change it, so …* | *This sign-in may read how this account is set up and may not change it, so it is shown here rather than offered for you to change.* | *This sign-in may not change the settings that follow you between machines, so they are shown here as they stand. …* |

In both cases the controls are drawn dimmed or read-only, each `aria-disabled` control saying on a tap
*Not connected to the server - this waits until it is back.* or *This sign-in may not change it.*; the
picture's controls, the phone rows' removal and *Move to first*, *Add a number*, *Save automatic reply*, a folder's
chevron, *Add a folder*, an exclusion's `close` and its field, *Save account* and the footer's actions are not drawn at all.

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
  there.* — and so is the end of *Running operation → error*, whose abort says only that it stops the send
  (*Aborting stops it here, so it is not sent.*), never that it saves a draft. Two samples beside it are the other
  failures a notification raises: *A change did not reach your mailbox* (`error`) and *Draft out of step with your
  server* (`warning`), and a fourth is a new message's (`info`, the sender over the subject).
- **A toast raised for a notification is pressed as a whole** (`t.opens` / `t.open`). It is the notification on the
  screen, so it draws no action button: a press anywhere on the card opens what the notification's row opens and marks
  it read — the message, the Outbox entry, the thread whose change did not land, the draft — and the card goes. It
  carries `cursor:pointer`. Its close control only closes. The artboards draw the pointer's press and nothing more: in
  the client the card is one button, reached by a key like any other, named by its kind, its words and what pressing
  does — *Show*, *Show in Outbox*, *Show message*, *Open draft*. A toast whose action is an act rather than a way somewhere —
  *Undo*, *Retry*, *Ask again*, and an operation's own toast such as *Message sent* with *Show in Outbox* — keeps its
  button, and pressing beside that button does nothing.
- **Any toast is dragged aside to dismiss it** (`MailFathomDesign.toastDrag` in `design.js`, `t.dragDown` /
  `t.dragMove` / `t.dragUp`), beside its close control and never instead of it. Once the pointer has travelled 14 px
  across, the card follows it and fades — carried at most 148 px, at 40% opacity there — and let go past 96 px it slides
  out that way and is gone; short of that, or once the pointer has gone more than 26 px up or down and more down than
  across, it springs back on `--ease-sheet`. Every pointer drags, a mouse included. A drag never also counts as a press
  on the card, and a press on the close control stays the close control's. A `loading` toast springs back and asks its
  abort question, exactly as its close control does.
- **A write that got no answer is *outcome unknown*, never *nothing was saved*** (*Warning - outcome unknown*). A client
  that heard nothing back cannot know the change did not land — and once acts commit on arrival, it most likely did. So
  the `warning` reads *Not known whether this reached the server* over *The connection went as this change was made. It
  stays shown as you asked until the server answers again, and then as the server holds it.*, and it draws **no *Try
  again***, which could record the act twice. A `502`, `503`, or `504` on a write says the same, since a gateway may
  have handed the request on first. What the act drew stays drawn until the read made once the server answers again
  lands; then what the server holds is drawn.
- **A write still waiting says so** (*Neutral - still sending*). After 10 s without an answer a quiet `neutral` toast,
  *Still sending…* over *The server has not answered yet.*, stands — with no timer bar, since nothing is running out for
  it (`lasting`) — until the write is answered or ends as unknown. Its close control only closes it.
- **What the re-read turned up is said once** (*Information - what the server holds*). When the read made once the
  server answers again changes what an unknown write had drawn — a message an act took away comes back, a flag it set
  goes — one `info` toast says *The screen now shows what the server holds* over *Changes made as the connection went
  are shown as the server holds them now.* It claims nothing about whether they arrived: a change the mail server has
  not taken yet reads the same as one that never reached MailFathom. A read that confirms every such write says nothing.
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

**On the phone the panel covers the bottom navigation bar.** It runs from the top of the screen to its bottom edge,
over the bar it came out of rather than stopping above it (`notifPanelStyle`, `bottom:0` with the safe-area inset as
padding), and the scrim beneath it covers the bar too — so while the panel stands, or travels under a finger, no bar
item can be pressed. It is the same panel whether the gesture or the bell opened it.

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
- Every result carries the sender mark beside its sender, folded spam included, as the folder row does — § *The
  sender mark* under Mail. The specimen draws the glyph; the prototype draws the details it opens.
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
| `m_verdict` 2.1 | — | **Gone from this artboard.** The notice box under the sender line is replaced by the sender mark the prototype draws on every received row and as a badge beside the sender, with its details popup — § *The sender mark* under Mail |
| `m_more` 2.2a | `show.m_more` | The paging control ends the thread, with how much has been read beside it. While loading it becomes a status line in the same place, and once everything is read one faint line closes the thread. On a phone the button is 44 px and the count drops underneath |
| `m_partial` 2.2b | `show.m_partial` | A partial failure sits **where the missing messages would be**, between the ones read. The two head notes go under the subject: *more people* in muted ink, *more messages than were assembled* as a warning line |
| `m_pane` 2.2c | `show.m_pane` | Failed, offline and *nothing you are allowed to see* reuse the *Nothing open* layout, with an icon for the logo and one action where one exists. The list stays usable beside it |
| `m_folders` 2.3a | `show.m_folders` | The folder column loads as **a skeleton shaped like the tree**. *Reading mailboxes and folders…* stays only as live-region text for screen readers. Failed — kept for an answer the tree cannot use — and *No account configured* are drawn too. **Without a connection the tree is never replaced by a sentence**: the tree already drawn stands as it was, undimmed, a tree with nothing drawn yet keeps its skeleton, and what is wrong is said by the status and by the freshness line under it (*Mailbox status unknown: no connection to the MailFathom server.*). The column is in place on desktop, a drawer over the list on fold and tablet, and full screen on a phone |
| `m_list` 2.3b | `show.m_list` | The list's empty states are centred in the column, one sentence each. *Nothing matches* names what is narrowing and offers *Clear filters*. A partial failure is a warning strip above the rows that did arrive. An empty list that also failed says the folder could not be read — never that it is empty |
| `m_kind` 2.4a | `show.m_kind` | When a mailbox lacks a role folder, *Kind of folder* becomes **the first field**, as a radio list. Picking a role folds *Folder name* and *Inside* away and puts the hint in their place, and *An ordinary folder* brings them back. On a phone the dialog is full screen |
| `m_roles` 2.4b | `show.m_roles` | Flagged (`flag`), Important (`label_important`) and All mail (`all_inbox`) join the standard folders in reading order: what needs you, what you wrote, then everything. The Outbox is no row of its own: it is the `outbox` mark on each account's Sent row, drawn only while something waits or was refused — § *The Outbox* under Mail draws the screen behind it |
| `m_search` 2.5 | `show.m_search` | A described search turns the sentence into two kinds of chip. Filters keep the Mail Search chip (rail tint) and decide what is in. Ranking criteria get their own labelled row in accent tint with a sort icon, and each one's × reads *Stop ranking by {criterion}*. One muted line under both names what was not used. *Reading what you wrote…* and the words-only field are drawn below |
| `m_pending` 2.6 | `show.m_pending` | **The connection summary itself becomes the indicator**: its dot and sentence take the most urgent state, and clicking it opens a panel under it — a bottom sheet on a phone, where the summary is a chip at the right of the header. The panel lists accounts first, then changes on their way, most urgent first. Toasts still announce each action, and a failure landing after its toast is gone raises one warning toast whose action opens the panel. A change this tab follows that the server never took also becomes a durable row in the notification centre (*A change did not reach your mailbox*), but **this line and its toast stand for it**: no second toast is raised for the row, and deciding it here — *Ask again* or *Let it go* — marks that row read, so one failure is never shown as two (§ *Chrome that stands over every screen*, *One failure, one voice*) |
| `m_sync` 2.7 | `show.m_sync` | **Green is the whole message when every account is current**: a dot and one sentence, nothing to open. An account catching up or stopped turns the dot amber and makes the sentence a control that opens onto one row per account — its name and its state, never a time or an *oldest refresh* line. Without a connection to MailFathom it is a grey dot and *Mailbox status unknown: no connection to the MailFathom server.* |

### Tasks, Calendar and People

| State | Gate | What it settles |
|---|---|---|
| `c_edit` 3.1 | `show.c_edit` | *Edit* turns the open event's dialog into the form in place: same dialog, title *Edit event*, *Cancel* and *Save*. Starts and Ends each take a day and a time, and *All day* hides both times. *Save* stays disabled until something changes and says why when the times conflict. **The new-event form should take the same fields** |
| `t_layout` 3.2 | `show.t_layout` | *Lay out today* happens inside the capacity panel, which grows to hold the offer. Nothing is written until *Add to calendar*, and what did not fit is named, not only counted. The panel draws waiting, the offer, nothing left and allowance spent. Unavailable hides the button and keeps the capacity line. On a phone the panel sits above the list |
| `t_proposed` 3.3a | `show.t_proposed` | A proposal has **no checkbox**; the AI tag takes its place. *Accept* is on the row, and the menu has *Accept* and *Dismiss task*. Undated tasks gather under *No day* at the end. The status line sits between the list head and the first group and is replaced by the next change; a partly refused change is a warning line |
| `t_states` 3.3b | `show.t_states` | The two empties point at different things — nothing at all, versus everything done and hidden (naming *Show done*). Loading is the list skeleton; reading more is a spinner line at the foot |
| `p_none` 3.4a | `show.p_none` | From fold upward the detail pane shows *Nothing open* with one sentence before anyone is picked. A phone shows the list alone. After a change, one status line sits under the name: green when it worked, red when refused |
| `p_states` 3.4b | `show.p_states` | Each address book says why it is empty in its own terms. Loading and failure look the same for the book (in the list) and for correspondence (in a person's *Conversations*), a colleague's *Organization* card waits and fails as *Conversations* does, and each fails on its own |

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
- **A trusted link opening** (`hostTrusted`). A link to a host on the person's trusted list opens a new browser
  context and draws nothing in the application at all, so the only evidence of the path is that no question
  appeared; the meeting link of *Rollout status with Piotr* and the materials folder in *Trade show materials
  24.09* take it.
- **A rejected picture** (`photoError`). Set only by choosing a file that is neither a JPEG nor a PNG, or
  one over 1 MB; no control in the prototype can produce one.
- **Every answer a settings write can get but the saved one** (`settingsWrites`). The prototype saves
  every write unless the `settingsWrites` property says `refused`, `notStored`, `unanswered` or `moved`,
  so no preview shows a refusal under a setting, an account, a folder or a footer — § *The settings panel* lists
  what each answer says.
- **Settings this sign-in may not change** (`settingsAccess`, `connection`). The name held by the
  administrator is `settingsAccess` `nameManaged`, a sign-in that may only read is `readOnly`, and the
  sections shown rather than offered for want of a connection need the `connection` property pinned to
  `offline`.
- **Telemetry the server forwards nowhere, or has not answered about** (`telemetry`), and **notifications
  the browser blocks** (`notificationPermission`). Each is a property alone; the defaults draw telemetry
  forwarded and notifications not yet asked.
- **The automatic reply answering now, and one whose period ended.** The sample reply is off and its
  period lies ahead, so *Answering now, until …* and *The period ended …* are reached only by saving a
  period that contains or ends before the prototype's today, 27 August 2026, with the switch on.
- **Every tab state** (`tabsBarShown`, `noTabsOpen`, `convBarShown`, `closeAllAsk`). Two conditions at
  once: the user menu's *Tab mode* switch turned on and a width of at least 1180 px. The initial state
  is `classic`, so a preview opened at any width shows none of it.
- **No sign-in method offered** (`noMethods`). Three properties turned off together — `ssoAvailable` joined `oauthAvailable` and `basicAvailable`.
- **Outside an organization** (`inOrganization` turned off). The component property alone reaches it: no
  *Colleagues* segment, none of the organization's contacts under *Own*, no presence on the rail or in the user menu
  or the *More* sheet, no status menu, no colleague in the recipient suggestions, and no presence on a message row.
- **The reader shown as hidden.** Only the reader's own status menu, rail dot and menu row ever draw *Hidden*; every
  other member who chose it is drawn *Offline*, so no record in the sample draws the hidden dot on anybody else.
- **A contact photo refused** (`ctPhotoError`). Set only by choosing a file over 1 MB, one that is neither a JPEG nor
  a PNG, or one the browser could not read.
- **A contact's photo saved on its own, and one the server did not take.** The prototype's *Save* is live on any open
  dialog and nothing in it answers for a server, so neither half is drawn. In the client a correction whose only change
  is the photo is still a change: *Save changes* is flat until something in the form differs, and choosing or removing
  a photo is enough. A change to the photo the server would not take — a new one, a replacement, or a removal — is said
  where the dialog's other outcomes are, *The server did not store that change to the photo.*, and one nobody answered
  *The server did not answer, so it is not known whether that change to the photo was stored.*
- **An arrival with no subject, and the moment arrivals merge.** Every sample mail row carries a subject, so *No
  subject* is drawn by no preview; and the sample centre holds its merged row from the start, so the sixth arrival
  folding five separate rows into one is a rule the inventory states rather than a click that plays it.
- **The spam that folds, and what a verdict changes.** The two folded sample rows stand at the very end of the mail
  list, so no preview opened at the top shows one; the Spam folder's badges need that folder picked, the move into
  Spam needs a move, and the refused selection, the *Not spam* confirmation, the landing in the Inbox, both acts on
  the personal account, which does not move spam, and the read-only refusals each need a selection or a press first —
  § *Spam* under Mail states every one of them.
- **Most of the sender marks and every step's result.** The rows a preview opens on show four marks, but the
  details need a press, a certificate's detail a second one, and most results sit in threads further down the list or
  in another account's folders — § *The sender mark* under Mail names the thread that seeds each one. Known is seeded
  for a contact and an organization contact only: its sentences for one of the owner's own addresses and for a domain
  of the owner's own accounts have no thread, and are reached by giving a record that `known.source`. The anchored
  details opening above their mark instead of under it need a mark low in the window. *Add to contacts* needs a press
  to ask and a second to write, and its failed state is reached only with the `connection` property pinned to
  `offline`, which draws the *unavailable* reason; the other reason, *your contacts changed since this message was
  read*, is the service's answer to a write the book refused and has no sample. Verified beside failed authentication,
  on a sender nobody wrote down (*… Sender authentication failed, which a forward or a mailing list often causes.*),
  has no sample: `piotr`, which seeded it, is now a contact his signature makes trusted.
- **Every receipt mark and the composer's two requests.** The receipt marks are drawn in *Sent* alone, and the badge on
  an own message sits on a message a preview keeps folded under *Show N earlier messages* — § *The receipt mark* under
  Mail names the thread that seeds each outcome. The details need a press. *Sending options*, its checked items, and
  the line above the foot need the composer open and a press on its `more_vert`; a draft opening with a request already
  on is reached through the out-of-step draft row in Drafts. A read receipt asked of the reader sits on messages
  further down the list, its sending state lasts a moment after the press, its offline state needs the
  `connection` property pinned to `offline`, and *Not sent* has no sample — nothing in the prototype fails a send, so
  it is reached only with `mdnFailed` set for the thread by hand. A report it cannot word has no sample either. The reports that came back as mail sit below the first screen of the
  inbox, and a report unfolds only on a press of its line.
- **Sending later.** The *Send later* group, the picker, the scheduled composer, the confirmation that schedules and
  the toast after it all need the composer open, a press on its `more_vert`, and a time taken. A time already gone is
  reached only by typing one into the picker (today, before 10:47), and refusing a time at the press of *Schedule*
  in the confirmation has no sample, since every ready time and every time the picker accepts is still ahead.
- **A failure raised while the client is open** (`raiseFailure`). The three failure rows start in the sample
  centre, so their toasts are raised only by the prototype's `raise` property — `send`, `change` or `draft` —
  and the rule that a tab already following a change raises none (*One failure, one voice*) has no pending-change
  line in the prototype to follow.
- **The message source's four outcomes past the plain one** (`sourceCut`, `sourceFailed`, `sourceScreened`,
  `sourceOffline`). Each
  belongs to one sample message (`data/prototype/message-sources.js`), so a preview reaches them only by opening that
  message and pressing *More actions*, then *Show source*; the skeleton stands for under a second.
- **A connection status pinned** (the `connection` property). The prototype cycles online → delayed (channel) →
  delayed (mail server) → offline every 60 s on its own, so a preview opened and captured at once shows online;
  `connection` pins `connecting`, `online`, `delayed`, `delayedMailServer` or `offline`, and **connecting is reached
  only through it** — in the cycle it would last as long as a start-up does. `design/parity.json` pins `online` on
  every prototype screen, so a capture is never taken mid-cycle.
- **The writes refused while offline** (`writeRefused`). Drawn only while the status is offline, which the cycle reaches
  three minutes in and the `connection` property pinned to `offline` reaches at once; `design/parity.json` pins
  `online`, so no capture shows a refused control.
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
- **The list's pull** (`listPullShown`, `listPullSettled`). A finger dragged down a list at its top under a coarse
  pointer is the only way in, and a click is not a drag, so no preview shows the disc mid-pull, armed past its
  distance, or waiting while the refresh is under way — *Pulling the list to refresh* under § *What every touch
  composition holds to* states each one by number.
- **What back does.** A press of back is a history traversal rather than a click on anything drawn, so no capture
  reaches the state before or after one — *What back does* above states each step.
