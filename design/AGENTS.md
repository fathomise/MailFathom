# Design Authoring Instructions

These instructions apply under `design/` in addition to the repository root instructions. [`README.md`](README.md) says
what the directory holds and what a change to it owes, and `$read-design` is how it is read; nothing here restates
either.

## Skills

**A design change is drawn with the taste skills vendored under `.agents/skills/`, never from a session's own
defaults.**

- Open with `$read-design`, then the taste skills: `design-taste-frontend` by default, `redesign-existing-projects` when
  reworking a screen that exists, `high-end-visual-design` for the detail of a surface, and the image-generation ones to
  explore a direction before anything is drawn.
- The design already drawn outranks a skill. A skill settles what the artboards have not decided, and its advice on
  frameworks, libraries, and builds is not followed: an artboard takes no dependency.
- Draw only what the client can ship under `frontend/src/AGENTS.md`.
- These skills are for `design/` only. A client task builds from the design and opens none of them.

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

## Fast, in balance

**The design is authored fast.** Nothing here ships, so what counts is how soon a screen can be looked at and judged,
and an artboard's code is the means to that rather than a deliverable.

- **Not owed:** the stacks' rules for structure, naming, and logic, deduplication, error and edge-case handling, unit
  tests, or tidying on the way past.
- **Still owed:** the screen itself at the full bar, a page that boots with every inventoried state reachable under the
  inventory's name, the split into template, styles, logic, and data, and everything `README.md` asks of a change.
- **The balance:** could the next session find what to change within a minute and change it without breaking the
  screen beside it? If so the code is good enough; if not, tidy as far as that and no further.

A review reads a change here against this bar: what the page shows and what it owes is a finding, how its code is
structured is not.
