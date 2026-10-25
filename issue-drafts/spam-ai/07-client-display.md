## Context

Part of #{{PARENT}}. The client shows nothing about a message's classification beyond the folder it sits in. Once #{{SURFACES}} publishes the verdict and spam subcategory, an opened spam message shows what kind of spam it is. The three dangerous subcategories (phishing, business email compromise, and malware lure) get a warning a person cannot miss, and the rest get a quiet label. Mail that is not spam shows nothing new.

This is not the place for mail categories. Topic categories for all mail (finance, work, and the like) are a separate future feature with a UI of their own. The spam label is drawn and worded so that it cannot be mistaken for one, and does not take the position on the message where such a category would naturally sit.

The design leads ([`AGENTS.md` § The design leads the client](https://github.com/fathomise/MailFathom/blob/main/AGENTS.md#the-design-leads-the-client-never-the-other-way-round)). `design/` is changed and checked first, at every composition it reaches and in both themes, and only then is the client built from it.

The tone matters as much as the warning:

- The copy says a subcategory is a judgement, and that a model made it.
- A warning names what the message asks for and whom it pretends to be, where the record says so. It never repeats the message's own claims as fact.

The link-deception notice `MessageLink` already shows is the nearest precedent for how a warning sits on a message.

## User stories

**As a person opening a message** I want to see at a glance that it is phishing and what it is after, **so that** I do not enter a password or pay an invoice it asks for.

**As a person opening marketing spam** I want a label rather than an alarm, **so that** warnings keep meaning something.

**As a person using the client in Polish** I want the subcategories and warnings in Polish, **so that** the warning is understood the moment it is read.

## Acceptance

- `design/` draws the opened message for each subcategory, a spam message with no subcategory, and a non-spam message, in both themes and at every composition it reaches, before any client code changes. The pull request names this as a design change.
- `design/state-inventory.md` lists every new state, and `design/parity.json` pairs the screens that move.
- Whether a list row carries a marker for the dangerous subcategories is decided in the design and built as drawn.
- The client draws the subcategory from the fields #{{SURFACES}} publishes, and draws nothing where the subcategory is absent.
- The warning does not rely on colour alone, is announced to assistive technology, and meets `frontend/src/AGENTS.md`'s accessibility obligations.
- Every string exists in `en.ts` and `pl.ts`, under keys that name spam rather than a bare category.
- Unit tests beside the source cover each subcategory's rendering and the absent case, and a browser spec covers the warning on an opened phishing message.

## Out of scope

- Disabling links or attachments in a flagged message. That is a separate decision about what the client refuses to do.
- Any setting to hide the label.
- Mail categories for all mail.

## Dependencies

Waits on #{{SURFACES}}. Runs beside #{{MODEL}}: until it lands, no record carries a subcategory, and the screen shows the absent state.

## Baseline

1d26b66df368199d215814baacb461e90f902748
