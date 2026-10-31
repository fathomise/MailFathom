# Design Authoring Instructions

These instructions apply under `design/` in addition to the repository root instructions. [`README.md`](README.md) says
what the directory holds and what a change to it owes, and `$read-design` is how it is read; nothing here restates
either.

## Skills

**A new screen or a new direction is drawn with the taste skills vendored under `.agents/skills/`, never from a
session's own defaults. A change that extends what is already drawn follows the artboards and opens none of them.**

- Open with `$read-design`. For a new screen or a new direction, then the taste skills: `design-taste-frontend` by
  default, `redesign-existing-projects` when reworking a screen that exists, `high-end-visual-design` for the detail of
  a surface, and the image-generation ones to explore a direction before anything is drawn. One skill is enough; open a
  second only for what the first left undecided.
- The design already drawn outranks a skill. A skill settles what the artboards have not decided, and its advice on
  frameworks, libraries, and builds is not followed: an artboard takes no dependency.
- Draw only what the client can ship under `frontend/src/AGENTS.md`.
- These skills are for `design/` only. The client's own code is built from the design and opens none of them; the design
  half a client pull request carries is a design change, and is drawn with them like any other.

## Drawn for a finger at three sizes

**A screen is drawn for the phone, the fold and the tablet as deliberately as for the desktop, never narrowed from it.**
`state-inventory.md` § *What every touch composition holds to* is the bar all three meet — the target size, the phone's
bar, the bottom navigation, what stands in the *More* sheet, one-line prompts, no key names, segmented controls, no
repeated actions, the smallest text — and a rule for small screens is added to that table rather than here.

- A value drawn under `touch` meets that table rather than repeating the desktop's. The 44 px target matters most: the
  client lifts every control under a coarse pointer to it, so a design drawing less is a difference the parity loop
  reports on every pair rather than a choice the client follows.
- A change that adds or reshapes a screen is opened at all three touch compositions, in both themes, before it is
  finished — the four sizes are `frontend/AGENTS.md`'s — and the desktop composition is checked to be unchanged
  unless the change is about it.

## Nothing only under a hover

**A screen says nothing that only a mouse can read.** A finger has no hover, so a name, a reason, or a fact drawn only
for a pointer resting on something is missing from every phone, fold and tablet the design is drawn for.

- A control drawn as its symbol alone writes its name as its `title`, which `design.js` draws as the name bubble for a
  mouse, a key and a finger's hold alike — `state-inventory.md`, *A control's name*. An artboard draws no tooltip of
  its own and no hover-only label beside it.
- A control that cannot act says why on a tap, and is marked `aria-disabled="true"` so the bubble answers that tap.
- Something inside a row whose own hold opens a menu carries `data-tip-hover`, and a fact it said reaches a finger
  another way: drawn again under that menu's header (`ctxMenuDetail`, passed to `pressFor`), or carried by what the
  row already draws — `state-inventory.md`, *A row's hidden facts*. One held finger opens one surface — never a name
  and a menu, or a name and a swipe, at once.
- An action no glyph names keeps its words where the strip has room for them, and *Select all* keeps them everywhere.

## Fast, and working is enough

**The design is authored fast, and an artboard's code only has to work.** Nothing here ships, so what counts is how
soon a screen can be looked at and judged, and the code is the means to that rather than a deliverable. Working means
the page boots, every state the change draws is reachable by the press the inventory names, and the screens beside it
still work.

- **Not owed:** the stacks' rules for structure, naming, and logic, deduplication, error and edge-case handling, unit
  tests, or tidying on the way past — and the interaction mechanics the client is held to under
  `frontend/src/AGENTS.md`: keyboard paths, focus placement and return, focus traps, ARIA roles and semantics, and
  resetting state on every path that closes a surface. The design says what a screen shows and what a press reveals;
  how a keyboard or a screen reader reaches it is the client's to build.
- **Still owed:** the screen itself — layout, type, colour, copy, and every state the change adds — the touch and hover
  rules above, the split into template, styles, logic, and data, and everything `README.md` asks of a change.
- **The balance:** could the next session find what to change within a minute and change it without breaking the
  screen beside it? If so the code is good enough; if not, tidy as far as that and no further.

**A review reports only what breaks this bar**: a screen or state the issue asks for that is missing or drawn wrong, a
state that cannot be reached, a page that does not boot or a screen beside it the change broke, a fact only a hover
shows or a touch target under the inventory's size, sample data that is not made up, a missing licensing header, and
`state-inventory.md` or `parity.json` out of step with the artboard. Nothing else under `design/` is a finding — not
how its code is structured, not the mechanics listed above, and not polish such as a neighbouring colour token.
