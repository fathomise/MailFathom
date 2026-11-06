---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-11-05
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Widen the composer's closed tree by typed values rather than by markup, read a formatted paste from an inert document into that tree and nothing else, send a picture as a related part the composer holds by name, and let the drafting agent answer in blocks that mirror the tree

<!-- describes: frontend/src/Client.App/src/composer/**, frontend/src/Client.App/src/messageBody/**, frontend/eslint.config.ts, backend/src/Application/EmailContent/Rendering/Document/**, backend/src/Infrastructure/Mail/Mime/Rendering/**, backend/src/Infrastructure/Mail/Mime/Composition/**, backend/src/AI/ReplyDrafts/** -->

## Context and Problem Statement

The composer writes a closed tree rather than markup. `frontend/src/Client.App/src/composer/writtenText.ts` reads the
editable region node by node into fifteen element names — bold, italic, underline, strikethrough, two lists, a quote, a
link, and the paragraphs and breaks an engine writes — renders that tree back as React elements, and writes the HTML
part by escaping every value it puts in one. `WrittenMessage.tsx` edits the region through `document.execCommand`, and
pastes everything as `text/plain`. Two rules make that safe, and both are stated only in those files' comments: **no
markup is parsed anywhere on the composer's path**, and **what it writes is a closed set of elements**.
[ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) states the first rule for a message arriving
and enforces it with a lint rule under `frontend/src/` that refuses every call turning a string into nodes,
`DOMParser.parseFromString` among them.

[#2846](https://github.com/fathomise/MailFathom/issues/2846) widens what a person can write: pictures, pasted links,
colour, size, alignment, tables, formatted paste, spreadsheet paste, and a drafting agent that formats when asked. Every
child of it except the link paste and the design moves one of the two rules, so this record answers, once, the eight
questions [#2847](https://github.com/fathomise/MailFathom/issues/2847) asks before any of it is built:

1. how a formatted paste reads the clipboard's `text/html`, and how that stands against ADR 0024;
2. what the closed set gains — colour, size, alignment, tables, pictures — and within which bounds;
3. how the HTML part expresses it;
4. how a picture travels, what bounds it, and where an unsaved composition keeps it;
5. whether editing stays on `execCommand`;
6. whether the reduced document gains a text size every reading surface draws;
7. which clipboard flavour wins, what of a spreadsheet's styling is kept, and what happens past the bounds;
8. what form the drafting agent answers formatting in.

What the service already carries decides much of the shape. The reduced document
(`backend/src/Application/EmailContent/Rendering/Document/`) has a run's foreground colour as an opaque
`MailDocumentColour`, a block's `MailBlockAlignment`, tables whose rows carry `IsHeader` and whose cells carry
alignment, background, and spans, heading levels, and inline pictures resolved from `cid:` parts into bounded `data:`
sources — `MailDocumentBounds` caps one at 2 MiB and a document's at 4 MiB. It has no text size. On the way out, the
footer work ([#2841](https://github.com/fathomise/MailFathom/issues/2841)) already composes a `multipart/related`
beside the markup: `MimeKitMailFooterComposer` mints a content identifier per image for the message, rewrites the
markup's `cid:` references to it, and reuses a `multipart/related` it finds already there. `MailFooterContent` takes
PNG, JPEG, and GIF — the three every mail client draws — and judges an image by its signature rather than by the type an
uploader declared. And `messageBody/senderColour.ts` already decides that the reading surfaces draw a sender's colour as
one of the client's own two tokens rather than as the value.

## Decision Drivers

- **The safety statement stays the shape of the tree.** What reaches the editor, the session store, a saved draft, and
  the HTML part is the composer's own closed set; nothing pasted reaches the live document unread, nothing pasted is
  fetched, and no sanitizer is added.
- **One rule per question across both stacks.** A size the reduction reads, a size the paste reads, and a size the
  composer writes are one scale; a reopened draft and a received message are drawn by the same rule.
- **Use what each engine already does correctly** — its HTML parser, its CSS parser, its colour parser — rather than
  writing a second one, provided each is used where it cannot run, fetch, or reach the live document.
- **Both heads, three engines.** The web head runs in a browser; the desktop head in WebView2 on Windows and WebKitGTK on
  Linux. A property that holds on one engine and not another is a property that does not hold.
- **Reuse the outgoing path that exists.** The footer's `multipart/related` and `cid:` minting are the shape a pasted
  picture takes, not a second one beside it.
- **Mail content stays mail content.** Pasted words and pictures are personal data: none of it reaches a log, an error
  message, or a telemetry event, and none of it is kept where it outlives the tab.

## Considered Options

**Question 1 — reading a formatted paste**

- **P1** — Parse `text/html` into an inert document with `DOMParser`, and read that document node by node into the
  closed tree, exactly as `writtenIn` reads the editable region.
- **P2** — Let the engine paste natively into the editable region, then read the region back and unwrap whatever falls
  outside the set.
- **P3** — Send the clipboard's HTML to the service and let its sanitizer and reduction answer with a document.
- **P4** — A sanitizer package (`dompurify`) or the browser's `Sanitizer` API, and insert what it leaves.
- **P5** — Keep pasting plain text only.

**Question 2 — colour**

- **K1** — The tree carries any opaque colour, as `MailDocumentColour` does; the picker writes only colours that read on both themes.
- **K2** — The tree carries only colours the picker could write; a paste snaps to the nearest one.
- **K3** — A paste carries no colour at all.

**Question 5 — the editing model**

- **E1** — `execCommand` for what it does inside the set, and structural operations as pure functions over the closed
  tree written back through `insertHTML` of the composer's own serialization.
- **E2** — An editing model and undo history of the composer's own, with `execCommand` retired.
- **E3** — Direct DOM manipulation for the operations `execCommand` lacks, beside its own undo stack.

**Question 6 — text size in the reduced document**

- **Z1** — The document carries a run's size on the closed scale, and every reading surface draws it as the client's
  own type token.
- **Z2** — The document carries it and only the composer reads it back; the pane ignores it.
- **Z3** — Size lives only in the composer's tree and is lost when a draft reopens.

**Question 4 — where an unsaved composition keeps its pictures**

- **S1** — The session store, under a key of their own beside the composition, with the store's own refusal deciding
  what survives a reload.
- **S2** — IndexedDB or the Cache API.
- **S3** — Memory only.
- **S4** — Save a draft to the deployment whenever a picture is pasted.

**Question 8 — the drafting agent's form**

- **A1** — Structured blocks in the JSON answer, mirroring the closed tree, read at the AI boundary.
- **A2** — A Markdown subset in the body, parsed on the service.

## Decision Outcome

Chosen: **P1, K1, E1, Z1, S1, and A1**, with the bounds, the scale, the colour band, and the flavour order below. How the
record relates to ADR 0024 is stated first, because every other answer rests on it.

### How this stands against ADR 0024: an extension, with one recorded exception and one new restriction

This record **extends** ADR 0024 to the direction it did not cover. ADR 0024 made the closed tree the safety statement
for a message arriving; this one makes the composer's own tree the safety statement for a message going out, by the same
reasoning — what a hostile input can put on the screen is bounded by what the tree can express, not by what a filter
remembered to strip.

**It records one exception to ADR 0024's lint row, and narrows it to one file.** `DOMParser.parseFromString` becomes
writable in exactly one named module, `frontend/src/Client.App/src/composer/clipboardReading.ts`, and nowhere else under
`frontend/src/` — stated in `frontend/eslint.config.ts` as the markup rule minus that one selector on that one file,
the shape the `srcdoc` exception already takes for `MessageMarkupFrame.tsx`. The reason it is an exception and not a
contradiction: ADR 0024 refuses the parse because a parsed string becomes nodes on a screen. Here the parse produces a
document **with no browsing context, which is never rendered, never adopted, and never inserted** — no node of it is
passed to `appendChild`, `importNode`, `adoptNode`, a `Range`, or `execCommand`. The module's only output is a
`WrittenNode` tree, and only that tree reaches the live document.

**It adds one restriction ADR 0024's list does not carry.** `execCommand('insertHTML', …)` is a markup parse into the
live document, so it is made unwritable everywhere under `frontend/src/` except the one editing module that hands it
`htmlOf` output — the composer's own escaped serialization of its closed tree, and never a string from anywhere else.

### Question 1: a formatted paste is an inert parse read into the closed tree

`clipboardReading.ts` parses `text/html` with `DOMParser`, then walks the resulting document with the same element map
`writtenIn` uses, widened as Question 2 widens it. An element outside the set is unwrapped to what it holds; a link
`followable` refuses keeps its words and loses the link; a picture whose source is a remote address, a `file:` path, or
a `cid:` reference is dropped and never fetched; a picture whose source is a `data:` URI of an accepted type is decoded
by hand (`atob`, no request) and becomes a picture the composer holds, under Question 4's bounds; a `blob:` source is
accepted only when it is an object URL in this composer's own map, and then resolves to the picture it names — which is
what copying or cutting a picture inside the message puts on the clipboard — and any other `blob:`, another tab's
included, is dropped; a heading becomes a
paragraph whose runs are bold at the size its level maps to (Question 6). Nothing is re-serialized from the inert
document and nothing of it survives the walk.

Reading presentation uses the engine's own parsers and nothing hand-written:

- **Inline declarations** are read through `element.style` on the inert document's elements, which is the engine's CSS
  parser over a declaration block — no selector matching, no layout, no fetch.
- **A spreadsheet's class rules** — Excel writes most of its cell styling as `.xl65 { … }` in a `<style>` element — are
  read by handing that element's text to a constructed `CSSStyleSheet` with `replaceSync`, which refuses `@import` and
  is never adopted by any document, and then applying only rules whose selector is a single class, a single element, or
  an element with a single class, in sheet order and beneath the inline declarations.
- **Presentational attributes** — `align`, `bgcolor`, `<font color>` and `<font size>` — are read as the engine's own
  editing commands spell them, because a spreadsheet's flavour and an engine's own output both use them.
- **A colour in any CSS notation** is resolved to three channels by the engine's colour parser — a detached 2D canvas
  context's `fillStyle`, which normalizes a keyword, a function, or a system colour to `#rrggbb` and fetches nothing —
  and a value that comes back with transparency is dropped.

Only these properties are read, everything else being ignored: `color`, `background-color` (and the colour in a
`background` shorthand), `font-weight`, `font-style`, `text-decoration-line`, `text-align`, and `font-size`.

**A paste is not the only way foreign content arrives, so the rule is held on every insertion rather than on one
event.** A drop — `insertFromDrop` — and any other `beforeinput` whose data transfer carries `text/html` or files has
its native insertion prevented and goes through the same flavour order (Question 7) and the same `clipboardReading.ts`
as a paste. Otherwise a selection dragged from a web page or another mail client would be inserted by the engine
natively, fetching its pictures and applying its style element to the page — the outcome P2 is refused for. An insertion
carrying neither, such as typing, stays the engine's.

**What has to be proven for *runs nothing and fetches nothing* to hold.** The HTML standard disables scripting for a
document `DOMParser` creates, and such a document has no browsing context to fetch a resource for; that is the claim,
and it is load-bearing only once a test has watched it hold on the engines the heads run on. A fixture clipboard
document carrying `<script>`, an inline event handler, a `javascript:` link, `<svg onload>`, `<img src>` and `srcset`,
`<picture><source>`, `<video poster>`, `<object data>`, `<embed>`, `<iframe src>`, `<link rel=stylesheet>`,
`rel=preload`, `rel=prefetch` and `rel=dns-prefetch`, `<base href>`, `<meta http-equiv=refresh>`, `@import` and
`url()` in a `<style>` element and in a `style` attribute, and a `background` attribute is read through
`clipboardReading.ts`, and the suite asserts that no request leaves the page for any host, that no script ran (a global
the fixture's script and handlers would set stays unset), and that the tree it returns holds none of those constructs.
That runs in the browser suite on Chromium and in the desktop suite in WebKitGTK, each against the built client. WebView2
is Blink, so the Chromium run answers for its engine; what it does not answer for is the clipboard flavours Windows
presents, which is checked by hand once, on WebView2, before
[#2855](https://github.com/fathomise/MailFathom/issues/2855) closes — beside the Outlook, Gmail, and Apple Mail check
#2846 already requires.

### Question 2: the closed set gains typed values, not free style

The tree stays DOM-shaped, as `writtenText.ts` argues it must, and gains:

| Addition | Carried as | Who writes it |
|---|---|---|
| A run's colour | `span` with an opaque `#rrggbb` colour | The picker writes a colour that reads on both themes; a paste or a reopened draft may carry any opaque colour |
| A run's size | `span` with a step of the size scale below | The size control, a paste, a reopened draft |
| A block's alignment | `start`, `center`, `end`, or `justify` on `p`, `div`, `li`, `th`, and `td` — `MailBlockAlignment` without `Inherited`, which is the absence of a value | The alignment controls, a paste, a reopened draft |
| A table | `table`, `tr`, `th`, `td`; a header row is a row of `th`; a cell carries alignment, an opaque background, and a column span and a row span | The table controls, a paste, a reopened draft |
| A picture | `img` naming a picture the composer holds (Question 4), with its pixel width and height | A paste, a reopened draft |

**Colour (K1).** The tree carries any opaque colour, as `MailDocumentColour` does, so a reopened draft written in
another client and a pasted range keep the colours they had rather than being snapped to something nobody chose. The
picker offers *Automatic*, which removes the colour, a palette, and a colour of the person's own, and **everything it
writes is neither too dark nor too light**: at least 3:1 against the light theme's panel and against the dark theme's
alike, which bounds its relative luminance between the two values that contrast gives. A colour that reads on both
panels is drawn in the editor as the value it is sent with, in either theme, so no second spelling of a colour exists
for the dark one to read back. The bound is a rule rather than a list: the palette is that band sampled across hues
and shades, and its values are the design's and the client's to choose inside it. A colour of the person's own is
picked anywhere in hue and saturation and only inside the band in lightness, and one typed as a value outside it is
moved to the band's nearer edge with the same hue, and said to have been. Every colour the picker offers is named, so
none is conveyed by colour alone, and a grid that writes no names says them to a screen reader and in the name a
pointer or a held finger reads.

3:1 rather than the 4.5:1 body text is held to is what a band readable on both themes can reach: no luminance is 4.5:1
against a white page and a dark panel at once. A sent message is overwhelmingly read on a white page, where every
colour the picker writes is 3:1 or more, which is the bar for the emphasis a coloured run is. A grey in the band is
light enough that `senderColourRole` reads it as `receding`, which is what somebody greying a line means and what the
reading pane already draws for one. A paste drops two colours that are the source's defaults rather than a choice:
a foreground in the range `senderColour.ts` describes senders writing body text in — near-grey and dark, at or under
its saturation bound of 0.25 and under its lightness bound of 0.35, which Excel's `color: black` and `windowtext` on
every cell both are — and a background as light as the page (every channel at `#f8` or above). A pasted or reopened
colour outside the band is the source's choice and is kept and drawn as it is; what is sent is the value.

**Size.** A closed scale of four marked steps and an unmarked normal, named for what they are rather than in pixels:

| Step | HTML part writes | `<font size>` read as | CSS keyword read as | Any length read as |
|---|---|---|---|---|
| `small` | `12px` | 1, 2 | `xx-small`, `x-small`, `small` | below 12.5px |
| normal (unmarked) | nothing | 3 | `medium` | 12.5px up to 17px |
| `large` | `18px` | 4 | `large` | 17px up to 21px |
| `larger` | `24px` | 5 | `x-large` | 21px up to 28px |
| `largest` | `32px` | 6, 7 | `xx-large`, `xxx-large` | 28px and above |

A length in points is read at four thirds of a pixel each, and a relative length against 16px, because neither a paste
reader nor the reduction knows the parent the sender meant. A heading level maps to a step: level 1 to `largest`, 2 to
`larger`, 3 to `large`, and 4 to 6 to normal, each bold. The table is the one statement of the scale; the reduction
([#2850](https://github.com/fathomise/MailFathom/issues/2850)) and the paste reader (#2855) each implement it and each
test it against these rows.

**Tables.** At most 200 rows and 20 columns, a span no wider or taller than the table, and **no table inside a table**:
a nested table in a paste or a reopened draft is unwrapped to its cells' contents, cell by cell, as `draftWords.ts`
already does for a whole table today. A cell holds what a paragraph holds — runs, colour, size, and line breaks — and
neither a list, a quote, a table, nor a picture.

**Pictures.** An `img` in the tree names a picture by a name the composer minted — a version 7 UUID, under
[ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md), which also
satisfies `MailFooterContent.IsImageName` — and never carries an address. In the editor it is drawn from an object URL
the composer issued for the bytes it holds; reading the region back, an `img` whose source is not an object URL in the
composer's own map is dropped, so neither a remote address nor a `blob:` the composer did not issue can become one. PNG,
JPEG, and GIF only, judged by the octets' signature in the client as well as on the service — a picture drawn and then
refused at send is a refusal arriving late.

`writtenTextIn` refuses a stored tree holding any value outside these: a colour not in `#rrggbb`, a step not on the
scale, an alignment not in the four, a table past a bound or nested, a picture whose name is not one the composer
mints. **A well-formed name the kept pictures do not hold is not a refusal**: that `img` is dropped from the tree and the
rest is read, because it is exactly what a store that refused the pictures leaves behind (Question 4), and refusing the
tree for it would take the words, recipients, and subject down with the picture.

### Question 3: the HTML part writes inline styles from the closed values alone

Mail clients drop style sheets, so `htmlOf` writes every presentation as an inline `style` attribute, and every value in
one comes from the closed set rather than from text anybody wrote: `<span style="color:#b42318">`,
`<span style="font-size:18px">`, `<p style="text-align:center">`. Start and end are written as `left` and `right`,
because the composer writes no `dir` and the Word engine Outlook renders with does not read `start` and `end`; a
composer that one day writes a direction maps them through it.

A table is written as a bordered grid every common client draws:
`<table style="border-collapse:collapse">`, and each cell
`style="border:1px solid #d0d5dd;padding:4px 8px;vertical-align:top"` plus its background and always a `text-align`
— the cell's own alignment, or `left` where it carries none, because most clients centre a `th` by default and the
composer draws a header cell start-aligned like the cells under it — with `th` for a header cell and `colspan` and
`rowspan` attributes where a span is not one. Per-cell border styling a spreadsheet had
is not kept; every table the composer sends has the one border. `plainTextOf` writes a row per line with its cells
separated, as [#2853](https://github.com/fathomise/MailFathom/issues/2853) states.

A picture is written as `<img src="cid:<name>" width="<w>" height="<h>" style="max-width:100%;height:auto">`, with the
width the picture is drawn at, at most 600 pixels and the height scaled to it — Outlook honours the attribute and
ignores `max-width`, and the others honour both. It contributes nothing to the plain-text alternative.

**`htmlOf` therefore takes how a picture's source is written, and nothing else varies with it.** For the HTML part a
picture's source is `cid:<name>`; for the markup Question 5 hands to `insertHTML`, it is the object URL the composer
issued for that name, which is the one source `writtenIn` reads back as a picture. It is one serializer with the
picture source as its only parameter, so the editor's markup and the HTML part cannot differ in anything but that.

### Question 4: a picture is a related part the composer holds by name, kept in the session store

**Out.** A picture travels as a part of a `multipart/related` beside the HTML part, referenced from it by `cid:`, never
as a `data:` URI inside it: `OutgoingEmailBounds.MaxBodyCharacters` bounds the HTML part, and several clients refuse a
`data:` picture. The client sends each picture beside the HTML part with its name, and the HTML part references it as
`cid:<name>`. **The service mints the content identifier, exactly as the footer composer mints one per image, and
rewrites the reference** — so no string a client chose becomes a header, and a composer's pictures and a footer's
images share one `multipart/related` without a collision, the footer composer already reusing one it finds there.
[#2849](https://github.com/fathomise/MailFathom/issues/2849) builds it on `MimeKitAuthoredEmailComposer` and on that
existing shape rather than beside it.

**Bounds.**

| Bound | Value | Why that value |
|---|---|---|
| One picture | 2 MiB | `MailDocumentBounds.MaximumInlineImageOctets`, so a picture the send accepts is drawn when its draft reopens |
| All of one composition's pictures together | 2.5 MiB | With a footer's largest — five images of 256 KiB — the sent copy stays under the 4 MiB `MaximumInlineImageOctetsPerDocument`, so the person's own sent message draws everything it carries |
| Pictures in one composition | 16 | Far under the reduction's 64, and a person pasting a seventeenth is told rather than surprised |
| Types | PNG, JPEG, GIF | The three every mail client draws, as for a footer; SVG stays excluded for the reason `MailInlineImages` gives |

**The service holds every row, and the client mirrors it.** The draft and send routes refuse a composition past any of
them — by count before a picture is decoded, then by each picture's octets and by their sum — so no caller of those
routes reaches the 4 MiB reasoning above by not being the composer. The whole message is still held to
`MaxMessageBytes`. The composer refuses a picture beyond one of these when it is pasted, as #2848 draws it, so a person
learns at the paste rather than at the send, and the composition is left as it was. A reopened draft reads a picture back through the reduced
document, whose `data:` source `draftWords.ts` decodes into a picture the composer holds under a name of its own.

**Keeping (S1).** The session store keeps the tree with each picture's name, and the pictures' octets under a key of
their own beside it, base64-encoded. Two keys rather than one, because a store that refuses the pictures must not take
the words, recipients, and subject down with them — the reasoning `keptComposition.ts` already gives for refusing a
record it cannot read whole. The write is attempted and its refusal caught: the platform's own quota decides what
survives a reload, rather than a number guessed here. **A refused pictures write removes the earlier pictures value**,
so octets kept for an older tree are never read against a newer one; a reload then reads the tree without the pictures
it names, which Question 2 drops rather than refuses. Where the store refused, the pictures live in memory only and the
composer says that a reload would lose them, which is a state #2848 draws. **A picture that leaves the tree keeps its
octets and its object URL until the composition ends** — it is sent, saved, discarded, or its composer closes — rather
than being revoked at once, so a picture cut and pasted elsewhere in the message, or brought back by Ctrl+Z, is still
the composer's to resolve. Only the pictures the tree names are kept in the store, counted against the bounds, or sent.

### Question 5: `execCommand` stays for what it does inside the set, and structural operations are functions over the tree

**E1.** `execCommand` keeps every operation it performs inside the closed set: the four emphases, the lists, the quote,
the link, clearing, and now colour (`foreColor` with a value the picker wrote), size (`fontSize` with 2, 4, 5, or 6), and
alignment (`justifyLeft`, `justifyCenter`, `justifyRight`, `justifyFull`). It still composes with elements
(`styleWithCSS` off), so it writes `<font color>`, `<font size>`, and `text-align` on a block, and **that is the
engine's spelling, not the tree's**: `writtenIn` reads each into the typed value, the tree never holds a `font` element,
and what the composer renders back after a reload or sends is its own spelling. A value an engine writes that is not on
the closed set — a computed colour a paste context left behind, a size off the scale — is dropped, never approximated.

**Inserting a table, editing one, inserting a picture, and every paste** are pure functions over the closed tree: the
affected part of the region is read into the tree, the function returns the new tree, and the part is replaced through
`execCommand('insertHTML', false, htmlOf(part))` — with each picture's source written as the object URL the composer
issued for it (Question 3) — and the caret placed afterwards. That keeps each operation one step
on the engine's own undo stack, which is what makes one Ctrl+Z undo a paste or a row added, and it means every
structural operation is a unit-testable function with no DOM in it. Adding a row selects the table and replaces it
whole; Tab in the last cell is the same operation.

`insertHTML` is still a parse into the live document, so it is held to the narrowest form: one editing module, an
argument `htmlOf` produced with the editor's picture source and nothing else, and a lint restriction everywhere else (§ *How this stands against ADR
0024*). Whatever the engine does to that markup on the way in is read back by the same strict `writtenIn`, so an engine
that adds a styled wrapper adds nothing to the tree.

### Question 6: the document carries a size, and every reading surface draws it as the client's own token

**Z1.** `MailInlineRun` gains a size on the scale above, under the document's own versioning rule, and #2850 maps what
mail writes onto it. Every reading surface draws a step as a type token of the client's own, relative to the pane's body
size, so a draft reopened in the composer and a received message in the pane are drawn by one rule. That is not the
choice `senderColour.ts` made for colour, and the difference is the reason: a colour is a sender's decoration and
legible by luck against a reader's theme, while a size says what is a headline and what is small print, which is meaning
in the way bold is. The scale being closed is what keeps it safe to draw — nothing renders below `small`, so text a
sender shrank to hide is drawn at a size somebody can read, and nothing renders above `largest`.

### Question 7: the flavour order, what a spreadsheet keeps, and what happens past the bounds

A paste is decided in this order, and the first that applies is the paste:

1. **Ctrl+Shift+V** pastes `text/plain` as text, exactly as every paste does today: no link, no table.
2. **`text/plain` that is exactly one address `followable` accepts** makes a link, as
   [#2851](https://github.com/fathomise/MailFathom/issues/2851) states — over the selection, or showing the address.
3. **`text/html`** is read by Question 1's rule, and is the paste when it reads to a tree with anything in it.
4. **Picture files** on the clipboard, where `text/html` is absent or read to an empty tree in step 3, are pasted as
   pictures. Excel and Word put a picture of the copied range beside their HTML, and that picture is the same content
   drawn again, so HTML that reads to something wins. A browser's *Copy image* puts the picture beside a `text/html` of
   one remote `<img>`, which step 3 drops to nothing, so there the picture is the paste.
5. **`text/plain` whose every non-empty line splits on tabs into the same two or more cells** becomes a table.
6. **`text/plain`** is pasted as text.

**What a spreadsheet range keeps** — from Excel's HTML flavour, LibreOffice Calc's, and Google Sheets', and from
tab-separated text where that is all there is: the grid; the composer's own border on every cell (the sheet's border
styling is not kept, Question 3); a header row where the sheet marks one — a row of `th` or in a `thead`, or a first row
whose every non-empty cell is bold above a second row whose cells are not; a cell's horizontal alignment as the sheet
states it, which is how a number keeps its right edge; bold, italic, underline, and strikethrough; a run's colour and a
cell's fill under Question 2's two dropped defaults; and a size by the scale. Empty trailing rows and columns are
trimmed. Number formats, fonts, column widths, borders per cell, merged-cell styling beyond the span, and anything
conditional are not kept.

**Past a bound, a paste is refused whole and said so**, as #2848 draws it, rather than cut: a table of 201 rows arriving
as 200 is a figure silently missing from somebody's message. Ctrl+Shift+V stays the way to paste it as words, which
`writtenIn`'s own bounds cut as they always have.

### Question 8: the drafting agent answers in blocks that mirror the tree

**A1.** The drafting agent's JSON answer gains a list of blocks in place of a plain `body` string, each block one of
`paragraph`, `list` (ordered or not, one level, items of runs), or `table` (a header flag and rows of cells of runs). A
run is text with bold, italic, a link, and — only where the person named one — a colour given as the **name of one
of the palette's hues** from Question 2, never a value, read as that hue's vivid shade; a block takes an alignment only where the person named one. The agent writes no
size and no picture.

**A link names its target by position, never by address.** Model output is text a prompt injection in the mail being
answered can steer, so a link whose address the model wrote could point words such as *the signed contract* anywhere
an attacker chose. The turn therefore publishes the addresses it may link to — each followable address in the person's
instruction and each `MailDocumentLink` target in the correspondence the turn carries — as a numbered list, and a run
cites one by its number, exactly as `ReplyDraftInstructions` already has the model cite a recipient and a claim's
messages by position. `ReplyDraftReading.cs` resolves the number against the list this deployment composed and drops a
link naming none, keeping its words; an address the model typed is never a target. That makes an address from
outside the exchange impossible rather than unlikely. What it cannot stop is the model linking words to an address an
attacker's own message carried, so the drafting block shows each link's host beside its words before the draft is
accepted, as #2848 draws it. A host alone cannot expose a deceptive target, so a `MailDocumentLink` whose
`IsWorthWarningAbout` is true — its displayed host differs from its target's, or its host has an `AsciiHost` because
its Unicode form may imitate another — is never published to the turn. No number resolves to it, and words the model
links to it stay words. Such a link is shown with both hosts and its warning in the reading pane, and a reply has no
reason to carry it.

**The model is shown an entry, never the address behind it.** A target is often a credential in its own right — a
password-reset link, a sign-in link, a signed download, an unsubscribe token — and the turn today carries only guarded
text. So each entry the turn publishes carries only the target's host and the words it was linked from, both passed
through the egress guard as fields of `GuardedDraftingTurn`, like every other text the turn hands the model. The full
address stays in the unguarded list the deployment composed, which is what `ReplyDraftReading.cs` resolves the cited
number against, so a link the draft carries is the one the correspondence held and its query string never leaves the
deployment.

**A heading is not something the agent writes**: a section opens with a short bold lead-in run, which is the structure
#2856 asks for and the one form both a plain-text reader and a mail client read the same way.

`ReplyDraftReading.cs` reads the blocks at the AI boundary into the reduced document's own types, holding them to the
closed set and to Question 2's bounds and dropping anything outside to its words, and the drafting response carries that
`MailDocument` beside the plain text — the type `Client.Backend` already parses at its trust boundary and
`draftWords.ts` already reads into the composer's tree, so the client gains no second reader. **The plain text is the
service's reading of the same blocks**, never a second thing the model writes beside them, so the two halves of a draft
cannot say different things; `claims` are their own sentences and are unaffected. A plain draft is paragraphs of plain
runs and reaches the composer exactly as it does today.

A2 is refused because it puts a parser over model output — text a prompt injection in somebody's mail can steer — in
the place a schema check would do, and because the repository pins no Markdown parser to do it with: one written here is
a parser on hostile input, and one taken from a package is a dependency, a licence review, and a grammar far wider than
the subset it would be used for.

### Consequences

- Good, because the safety of every child of #2846 rests on one statement a reviewer can check: only the composer's own
  tree reaches the editor, the store, the draft, and the HTML part.
- Good, because the paste reads with the engine's own HTML, CSS, and colour parsers, so nothing here is a second
  implementation of a grammar that disagrees with the first.
- Good, because the one parse ADR 0024 refused is admitted in one named file, and the parse ADR 0024 did not list —
  `insertHTML` — is confined the same way, so the lint rule still says where markup is parsed.
- Good, because a picture rides the footer's existing `multipart/related` and content-identifier minting, and a draft
  reopens with its pictures through the reduction that already inlines them.
- Good, because a size and a received message's size are one scale on both stacks, and the drafting agent's form is the
  document type the client already parses.
- Neutral, because the scale's mapping is implemented twice — once in the reduction and once in the paste reader — and
  held together by the table above and a test on each side rather than by shared code, the two stacks sharing none.
- Neutral, because a pasted picture from a file keeps whatever metadata its octets carry, as an attachment does today;
  a screenshot, which the clipboard hands over as a fresh PNG, carries none. Re-encoding every picture through a canvas
  would strip it and is not done here.
- Neutral, because the reading pane starts drawing a sender's size, which changes how existing mail looks; the closed
  scale bounds that change to four steps.
- Bad, because *runs nothing and fetches nothing* now rests partly on the engine's handling of a `DOMParser` document,
  a constructed style sheet, and a detached canvas rather than on the shape of a contract alone — which is why
  § *Validation* proves it on two engines and by hand on the third.
- Bad, because `insertHTML` keeps the editing on a deprecated command and on each engine's own reading of the markup it
  is handed; an engine that rewrites a table on the way in is a defect the round-trip test finds, not one the design
  prevents.
- Bad, because pictures kept across a reload depend on a quota this client does not choose, so a large composition can
  survive a reload without its pictures — said on the screen, never silent.

## Validation

- **The lint rule** carries the exception and the restriction by name: `DOMParser.parseFromString` (and
  `createContextualFragment`, which stays refused) is writable only in `composer/clipboardReading.ts`, and the string
  `'insertHTML'` only in the one editing module. Both land with #2855 and the first structural operation, whichever
  comes first, and a second file needing either is a change to this record.
- **The unit suite proves the tree and the reader as attacks rather than as examples.** `clipboardReading.ts` over the
  fixture document of Question 1 returns no script, handler, style sheet, remote address, or `file:` and `cid:` picture;
  every row of the size table maps as stated; a colour with transparency, a body-text foreground, and a page-light
  background are dropped; a table one row or one column past a bound is refused, and a nested table is unwrapped to its
  cells' contents; a `blob:` source in the composer's own map resolves to its picture and any other is dropped;
  `writtenTextIn` refuses a
  stored tree holding each value outside the set, and reads a tree whose picture the kept pictures do not hold with that
  picture dropped and its words, recipients, and subject intact; every structural operation is a pure function tested
  without a DOM; and `htmlOf` writes the same tree with a picture as `cid:<name>` for the HTML part and as its object
  URL for the editor, identical in everything else.
- **The browser suite** reads the fixture clipboard document against the built client on Chromium and asserts no
  request left the page and no script ran, once pasted and once dropped onto the region; it pastes a copied image whose
  clipboard carries a remote `<img>` beside the picture, and asserts the picture arrives; it pastes a formatted block and a spreadsheet range from recorded clipboard
  fixtures with made-up data; and it proves each structural operation round-trips — the tree written through
  `insertHTML` and read back by `writtenIn` is the tree that was written.
- **The desktop suite** repeats the fixture read, a spreadsheet paste, and a pasted picture in WebKitGTK, the shell's own
  WebView.
- **By hand, once each**: the clipboard flavours WebView2 presents on Windows, before #2855 closes; and a message with a
  table, a picture, a colour, and a size read in Outlook, Gmail, and Apple Mail, before #2846 closes.
- **On the service**, #2849's refusals — one per row of Question 4's bounds table, each one past it, and the count
  refused before anything is decoded — and its round trip, #2850's mapping, and #2856's reading and evaluation cases are each
  tested in the change that adds them, against the values recorded here — #2856's including a link citing a position
  the turn did not publish, a link written as an address, and a correspondence link whose `IsWorthWarningAbout` is
  true, each read as words, and a published entry that carries the target's guarded host and words and never the
  address itself.

## Pros and Cons of the Options

### P1 — an inert parse read into the closed tree

- Good, because only the closed tree reaches the live document, and the reader is the same walk `writtenIn` already is.
- Good, because the engine's own parser reads the markup, so nothing here disagrees with how the source would render.
- Bad, because it admits a `DOMParser` call ADR 0024 refused, and rests part of its safety on the engine — answered by
  the one-file exception and the two-engine test.

### P2 — the engine pastes natively, and the region is read back

- Good, because it needs no parse of ours and no lint exception.
- Bad, because the sender's markup is in the live document for as long as it takes to read it back: its pictures are
  fetched, a style element applies to the page, and what the person sees between the paste and the reading is
  somebody else's markup. That is the exact property #2846 forbids.

### P3 — the service sanitizes and reduces the paste

- Good, because the reduction already reads HTML into a closed document, and the client would parse nothing.
- Bad, because a paste becomes a round trip to the deployment, so pasting fails without a connection and waits on one
  with it; and the clipboard — which may hold something the person never meant to send anywhere — leaves the device on
  a paste rather than on a send.

### P4 — a sanitizer, and insert what it leaves

- Bad, because ADR 0024's reasoning holds unchanged: a filter's safety is what it remembered to strip, `dompurify` is a
  dependency bought to do worse what the closed walk does by construction, and the `Sanitizer` API is not available on
  WebKitGTK.

### P5 — plain text only

- Good, because nothing moves.
- Bad, because formatted and spreadsheet paste are what #2846 exists to give.

### K1 — any opaque colour, the picker held to a band readable on both themes

- Good, because a reopened draft and a pasted range keep the colours they had, and the tree matches the document's own
  `MailDocumentColour`.
- Bad, because a paste can carry a colour illegible on the page — narrowed by the two dropped defaults, and left as the
  source's choice beyond them.

### K2 — picker colours only, a paste snapped to the nearest

- Good, because every colour sent passes the contrast bar.
- Bad, because a corporate colour becomes the nearest swatch, which is a colour nobody chose, and a reopened draft from
  another client changes colour on a save.

### K3 — a paste carries no colour

- Bad, because a spreadsheet's red negatives and its filled header lose what they were saying.

### E1 — `execCommand` inside the set, structural operations as functions written back through `insertHTML`

- Good, because the engine's undo stack covers every operation, and every structural operation is a pure, testable
  function.
- Bad, because it depends on a deprecated command and on each engine's reading of `insertHTML`, proven by round-trip
  tests rather than guaranteed.

### E2 — an editing model and undo history of the composer's own

- Good, because the composer would own every keystroke and every undo, on every engine alike.
- Bad, because it is a document editor — selection, composition input, caret movement, IME, undo — written and tested
  here, which is the cost `WrittenMessage.tsx` already argues against. It is the upgrade path if an engine's
  `insertHTML` proves unusable for a structural operation.

### E3 — direct DOM manipulation beside `execCommand`

- Bad, because a change the engine did not make is not on its undo stack, so Ctrl+Z after a table edit undoes whatever
  came before it, or corrupts the region.

### Z1 — the document carries a size and every surface draws it

- Good, because a draft and a received message follow one rule, and the closed scale keeps hidden-small and shouting
  text inside four steps.
- Bad, because existing mail starts drawing differently in the pane.

### Z2 — the document carries it and only the composer reads it

- Good, because the pane changes nothing.
- Bad, because one member of the document would mean different things on two surfaces, which is the disagreement #2850's
  reader story asks not to have.

### Z3 — size only in the composer

- Bad, because saving a draft undoes its sizes.

### S1 — the session store, pictures under their own key

- Good, because it is the store a composition is already kept in, dies with the tab, and is emptied by signing out —
  the bound ADR 0028 allows a composition and nothing more.
- Bad, because its quota is the platform's, so large compositions keep their words across a reload and not their
  pictures.

### S2 — IndexedDB or the Cache API

- Good, because the quota is far larger.
- Bad, because both are origin-wide and outlive the tab, so a picture somebody was writing stays on a machine two people
  share after the tab closes or crashes — the exact reason `keptComposition.ts` chose the session store.

### S3 — memory only

- Bad, because a reload, which a single-page application makes a cold start, loses every picture with nothing to try.

### S4 — save a draft on every paste

- Bad, because saving reaches the person's mail server with every revision, which `keptComposition.ts` keeps an act
  somebody asks for.

### A1 — structured blocks

- Good, because a schema check reads the answer, the client already parses the resulting document, and the plain text is
  derived from the same blocks.
- Bad, because a block list costs more output tokens than prose, and the evaluation cases #2856 adds are what show the
  model keeps to it.

### A2 — a Markdown subset parsed on the service

- Good, because models write Markdown fluently.
- Bad, because it puts a parser over steerable model output where a schema check would do, and needs a parser this
  repository does not pin.

## More Information

- [ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) is the record this one extends.
- [ADR 0028](0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md) bounds what the session
  store may keep, which is why pictures stay there and nowhere longer-lived.
- [ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md) is how a picture's
  name is minted.
- The children that build on this record: [#2848](https://github.com/fathomise/MailFathom/issues/2848) draws it,
  [#2849](https://github.com/fathomise/MailFathom/issues/2849) sends and keeps pictures,
  [#2850](https://github.com/fathomise/MailFathom/issues/2850) carries the size,
  [#2851](https://github.com/fathomise/MailFathom/issues/2851) pastes a link,
  [#2852](https://github.com/fathomise/MailFathom/issues/2852) adds colour, size, and alignment,
  [#2853](https://github.com/fathomise/MailFathom/issues/2853) adds tables,
  [#2854](https://github.com/fathomise/MailFathom/issues/2854) pastes pictures,
  [#2855](https://github.com/fathomise/MailFathom/issues/2855) pastes formatted content and spreadsheets, and
  [#2856](https://github.com/fathomise/MailFathom/issues/2856) lets the agent format.
- Revisit Question 5 if a structural operation cannot round-trip through `insertHTML` on an engine a head runs on; E2 is
  the replacement, and it would be a record of its own.
