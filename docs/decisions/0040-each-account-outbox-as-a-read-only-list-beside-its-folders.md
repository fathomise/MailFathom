---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-24
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Present each mail account's outbox as a read-only list beside its folders rather than as a folder, name every entry by the identifier its send already answers with, keep a refused send there until somebody decides about it, and retire the operator-mapped outbox role

<!-- describes: backend/src/Domain/Delivery/**, backend/src/Domain/Folders/MailFolderSpecialUse.cs, backend/src/Application/Mail/Delivery/Tracking/**, backend/src/Application/Mail/Delivery/Operations/**, backend/src/Application/Mail/Delivery/Filing/**, backend/src/Host/Api/ClientOutboxEndpoints.cs, backend/src/Mcp/Tools/GetOutgoingEmailTool.cs -->

## Context and Problem Statement

Somebody who sends from MailFathom has nowhere to see what has been handed to the outbox and has not left yet, or what the mail server refused. The outbox exists: every send is an `OutgoingEmailRecord` moving through `OutgoingEmailStage`, the client reads one account's records at `/outbox` and one record at `/outbox/{outgoingEmailId}`, and an MCP caller reads back and withdraws its own sends with `get_outgoing_email` and `cancel_outgoing_email`. Nothing presents it as mail. Issue 2619 is the feature that does: each mail account shows an *Outbox* (*Nadawcza* in Polish) holding what is waiting or being transmitted and what failed, read-only on every surface. Issue 2614 asks for the shape before it is built, because three things already in place do not fit it as they stand.

- [ADR 0039](0039-one-folder-model-and-one-implementation-per-custody-phase.md) makes every folder a row under a `MailFolderId` that holds stored messages and that one custodian per custody phase acts on. An outbox entry is an outgoing record, not a stored message, and it is in no custodian's hands.
- `MailFolderSpecialUse.Outbox` already exists and means something else. [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) made it a label an operator puts on a real IMAP folder by path, into which outgoing-mail filing appends a `\Draft` copy of each send waiting for a later instant and withdraws it when the send leaves. No server advertises such a role — RFC 6154 has no `\Outbox` — and every act a mail client performs on those copies is meaningless: deleting one cancels nothing. The owner has decided it is retired, so that each account has one outbox and it is MailFathom's own.
- The client's outbox page deliberately names no recipient and no subject, and neither outbox reading reads the message. A folder somebody opens needs both, and a preview.

This record settles the seven questions issue 2614 lists. It produces no code: issue 2616 builds it on the client API and retires the role, issue 2617 on MCP, and issue 2618 in the client, against the design issue 2615 draws.

## Decision Drivers

- **A failure has a place.** A send the server refused stays where the person looks until somebody decides about it, rather than ending at a *queued* toast or vanishing once it is terminal.
- **Read-only by construction.** No mailbox act — flags, keywords, move, copy, delete, a folder act, or the outbox as a destination — may half-happen on an entry. A refusal that every act and every custodian has to remember is the failure mode ADR 0039 removed for custody, and it must not come back for the outbox.
- **ADR 0039 stays true.** A folder is a place stored messages are in, and a stored message names exactly one folder. Nothing here may make a reader written against that model wrong.
- **One identity per entry, at every surface.** The identifier a send already answers with is what a caller holds, so it is what the outbox names an entry by.
- **What the outbox shows is correspondence.** Subjects, recipients, and text are the person's mail, so reading them is a reading grant as well as a sending one, and the administrative surface still sees none of it.
- **No tool transmits mail** ([ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md)), so nothing on MCP sends a refused message again.
- **A rolling upgrade runs two builds against one database**, so every schema change is additive.

## Considered Options

**A — how the outbox enters the folder model:**

1. A folder row per account with a `MailFolderId` and a role of its own that holds no stored messages.
2. A presentation beside the folder rows: the outbox belongs to the account, and an entry is named by its `OutgoingEmailId`.
3. Each outgoing message materialized as a stored message in a local folder MailFathom keeps per account.

**B — what it holds, and what takes an entry out:**

1. Every send that has not left, including one the server refused, until it is sent, withdrawn, or somebody dismisses the refusal.
2. Only sends that have not reached a terminal stage, so a refusal leaves at once.
3. Refused sends for a fixed time after the refusal, then nothing.

**C — what a row and a preview carry, and under which grant:**

1. Recipients, subject, and text under `mailfathom.mail.send` alone.
2. Recipients, subject, and text under `mailfathom.mail.read` and `mailfathom.mail.send` together; `mailfathom.mail.send` alone keeps what it reaches today.
3. Status only, as today, under `mailfathom.mail.send`.

**D — how an act on the outbox is refused:**

1. Structurally: the outbox has no `MailFolderId` and an entry has no stored-message identifier, so no act can name either.
2. A dedicated refusal code every act checks for.

**E — the MCP shape:**

1. `role:Outbox` on `list_emails`, `search_emails`, `ask_mail`, and `get_email_content`.
2. A listing tool of its own, `list_outgoing_emails`, beside `get_outgoing_email`.

**F — the retired role:**

1. The virtual outbox reuses `Outbox` as its role on the wire.
2. `Outbox` stops being a folder role anywhere; the outbox is named as part of an account.

## Decision Outcome

Chosen options: **A2**, **B1**, **C2**, **D1**, **E2**, and **F2**.

### The outbox is beside an account's folders, and an entry is named by its outgoing email identifier

**Each mail account has one outbox, and it is not a folder row** (option A2). It is the account's not-yet-sent mail, read from the outgoing records, and it carries no `MailFolderId`: the folder model of ADR 0039 is unchanged, and so is its invariant that a stored message names exactly one folder. A surface names the outbox by the account it belongs to — the folder tree and `list_accounts` carry it as a member of the account, and the client's routes read it under `/outbox?account=` as they already do — and names **an entry by its `OutgoingEmailId`**, which is the identifier `send_email`, `reply_to_email`, `forward_email`, and the client's send already answer with. One identity therefore follows a send from the call that queued it through the outbox to the decision taken about it.

Option A1 was refused because a folder that can never hold a stored message is a folder every act, every destination resolution, every custodian, and every reader would have to treat as an exception — the read-only rule would live wherever somebody remembered it, which is the defect ADR 0039 removed. It would also be the one folder row on a mirrored account that is not a projection of the source hierarchy. Option A3 was refused because it duplicates every outgoing message into stored mail that is then read, cut, embedded, and searchable while it is still unsent, makes each one a target every act must refuse, and leaves a second copy beside the sent copy filing already appends.

### It holds every send that has not left, and a refusal stays until somebody decides about it

**The outbox lists a send at `Recorded`, at `TransmissionBegun`, and at `Refused`**, newest first, paged and bounded as the outbox page already is (option B1). `Recorded` covers a send about to be claimed, one waiting out a backoff, and one held for the time it was written to leave at. `TransmissionBegun` covers a send being transmitted now and one whose outcome nobody knows, and the row says which by whether an attempt still holds it. `Sent` takes a send out, and so does `Cancelled`. A refused send stays.

**A refused send leaves when somebody decides about it**, and there are two decisions. Offering it again puts it back at `Recorded`, where it is still in the outbox. **Dismissing it** takes it out: a new decision that records, on the outgoing record, when the refusal was dismissed and by which user, and changes nothing else — the stage stays `Refused`, the record stays in the administrative outbox, and its recipients keep what each server said, because a refusal may follow a partial acceptance and calling it withdrawn would be untrue. The same decision is offered on a send at `TransmissionBegun` that no attempt holds, which nothing moves on its own either: dismissing it is the person accepting that nobody will learn whether it arrived. Offering a dismissed send again — which only an operator can, from the administrative surface — clears the dismissal. Records already refused when the release is installed appear in the outbox like any other, since they are refusals nobody was ever told about.

Issue 2613's *taken back to be edited* is not decided here. When it lands, reopening a refused send as a draft dismisses it in the same transaction, so the message is in exactly one of the two places.

Option B2 was refused because it is the defect: a refusal reaches a terminal stage and disappears from the only place somebody looks. Option B3 lets a refusal vanish on a timer nobody chose, which is the same defect delayed.

### A row and a preview carry the person's own mail, read under both grants

**A row carries** the entry's `OutgoingEmailId`, its account, its stage, whether an attempt holds it, the recipients named in `To` — falling back to `Cc` and then `Bcc` where `To` is empty — with how many more there are, the subject, when it was recorded, when it was written to leave at where a time was named, how many attempts it has taken, and the failure code it stopped on with that failure stated in words.

**A preview carries** everything a row does, plus every recipient with its header and what the server said about each, the address it is sent as, the message itself — rendered from the stored MIME through the same closed document tree [ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) decides for stored mail, and on MCP as the text `get_email_content` returns for a stored message, under the same bounds — and each attachment by name, media type, and size. Serving an attachment's bytes from the outbox is not decided here.

**The subject is kept on the outgoing record** when the record is written, because a page of the outbox must not load each message's MIME to name it. It is personal data under the same classification, retention, export, and erasure as the record and the MIME beside it, and it is never logged. A record written without one — before the column, or by an older build during a rolling upgrade — has its subject read from its stored MIME's headers when it is listed. That cost is bounded to such records, and they leave the outbox as they are sent, withdrawn, or dismissed.

**The full outbox is read under `mailfathom.mail.read` and `mailfathom.mail.send` together** (option C2): the listing, the preview, and the count, over every send of an account the caller's user is assigned, whoever asked for it — the person, an agent, or a rule. The outbox is the not-yet-sent half of the Sent folder: a caller entitled to read that folder and to send as the account learns nothing it could not, and [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) already makes a send the account's rather than its author's. **A caller holding `mailfathom.mail.send` alone reaches what it reaches today**: on MCP, its own sends by identifier, status only, under the principal stamp [mail delivery](../features/mail-delivery.md#reading-a-send-back-and-withdrawing-one-that-is-still-waiting) describes; on the client, the decisions below. A send-only credential is a real shape — a notifier, an agent that writes and never reads — and handing it the correspondence of everybody sending from the account would be a widening nobody asked for.

That replaces, on the client endpoint, the rule `ClientOutboxEndpoints` and `UserOutbox` state today: a page of the outbox names no recipient and no subject because it would otherwise be an export of who this user writes to. The export the rule guarded against is now answered by what the outbox holds and who may read it — only what has not left, only an assigned account, only under the reading grant beside the sending one — rather than by blinding a list the person cannot use. **The administrative outbox and `mfctl outbox` keep the rule unchanged**: an operator sees every account's outbox and is not entitled to anybody's correspondence.

Option C1 was refused for the send-only credential above. Option C3 is the state the feature exists to replace.

### Every mailbox act is refused because none can name the outbox

**No act needs a check, because no act can address the outbox** (option D1). Flags, keywords, move, copy, and delete name a stored message, and an entry is not one: an `OutgoingEmailId` handed where an email identifier is expected is refused exactly as an unknown message already is. A move or copy names its destination by `MailFolderId`, and the outbox has none, so it is refused exactly as an unknown destination already is (`destination-not-found` on the client API); the same holds for every folder act — create inside it, rename, move, delete — and, since the role is retired below, for anything that names a folder by role. That is true on the client API and on MCP alike, where no tool moves mail and `set_mail_flags` names a stored message, and it holds without any custodian, any resolver, or any act knowing the outbox exists. A new act written later inherits it.

**The outbox's own decisions are offered from it on the client**, under `mailfathom.mail.send`, each on the conditions the store already states and nowhere else:

- **withdraw** a send at `Recorded` that no attempt holds — today's cancellation;
- **offer again** a refused send, with the refusal restated, and a send at `TransmissionBegun` that no attempt holds — today's re-queue;
- **dismiss** a refused send, or one at `TransmissionBegun` that no attempt holds — the new decision above, on a route of its own beside the other two, for the reason they are two routes: opposite decisions are never one field apart.

**On MCP only withdrawal is offered, and only for a caller's own sends**, as `cancel_outgoing_email` already is. Offering a send again transmits mail, which ADR 0013 keeps out of every tool. Dismissing a refusal hides a failure from the person, which is not an agent's to do.

Option D2 was refused because it is option A1's cost paid without A1: a refusal every act has to remember to raise.

### MCP lists the outbox with a tool of its own

**`list_outgoing_emails` lists one account's outbox** (option E2): the account named as every other tool names one, an optional stage among the three the outbox holds, a bounded page, and a continuation cursor, answered with the rows above. It is read-only, idempotent, and closed-world, in the `Sending` category, under `mailfathom.mail.send`, and it answers only where the caller also holds `mailfathom.mail.read`. **`get_outgoing_email` becomes the preview** where the caller holds both grants, over any send of an assigned account; with the sending grant alone it answers as it does today. Both descriptions say the outbox is read-only and name `cancel_outgoing_email` as the one decision a tool can take.

**`list_emails`, `search_emails`, and `ask_mail` do not reach the outbox.** Their `folders` argument refuses `role:Outbox` with `MailboxQueryFilterInvalid`, whose message names `list_outgoing_emails`, and `get_email_content` refuses an `OutgoingEmailId` as the unknown email it is. Option E1 was refused because an outgoing record is not a stored message: it has no email identifier, no thread, no embedding, and no search document, it is read under another grant, and a listing that mixed the two would answer with rows a caller could not pass to any tool that takes an email. Once a send leaves, its sent copy — where the account files one — is a stored message in the Sent folder, which every reading tool already reaches.

### The operator-mapped outbox role is retired, and nothing takes its name

**`MailFolderSpecialUse.Outbox` is removed** (option F2). No folder plays the role, no surface names it as a role, and the outbox is a member of the account rather than a role a folder could play — so `role:Outbox` names nothing, a folder listed by `list_accounts` or served at `/folders` never carries it, and the client's handling of an `Outbox` folder goes with it. Option F1 was refused because a role is what a folder plays, and keeping one that no folder may play leaves every reader of the role to remember the exception.

**The `held` filing is removed with it.** `OutgoingMailFiling.Held` and the withdrawal that follows it leave `OutgoingMailFiling` and `OutgoingMailFilingPass`, so nothing appends a copy of a waiting send to a mailbox or takes one back out. The draft and sent filings are unchanged.

**What was stored under the role is cleared by the release's migration, and nothing is regenerated.** The migration deletes every outgoing filing row named `held` — each describes a copy in a folder nothing will ever act on again, and keeping it serves no purpose — clears the role from every folder row and every stored folder setting that names it, and adds the subject and the dismissal to the outgoing record as nullable columns. The folder an operator mapped stays an ordinary folder, and every `\Draft` copy already in it stays an ordinary message, deletable by hand.

**A configuration naming the role is refused where it binds**, at startup, with a message naming the mapping to remove and saying that the folder and its copies are left where they are.

### What tells a client it moved

**The count is two numbers on the account**: `waiting`, the sends at `Recorded` or `TransmissionBegun`, and `refused`, the refused sends nobody has dismissed. The folder tree at `/folders` and each account in `list_accounts` carry it, and only for a caller holding both grants; without them the member is absent rather than zero, so a credential that may not read the outbox is not told its size. A client shows the outbox while either number is above zero, which is what lets issue 2615 draw it as a mark on the account's Sent folder and hide it otherwise.

**A signal of its own, `outbox.changed`, tells an open client that one account's outbox changed**, carrying the account's identifier and nothing else. It is published whenever what the outbox holds or the stage of an entry in it moves: a send recorded, an attempt beginning to transmit, a send delivered, refused, withdrawn, offered again, or dismissed. Under [ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md) a signal is a hint and the client's own re-read of the count and the open page is the guarantee, so a missed one costs freshness rather than correctness. MCP has no push, so a caller reads the count from `list_accounts`.

### What breaks, and what each party does

The major version is `0`, and [ADR 0004](0004-versioning-and-release-policy.md) permits a minor to break any of the four public surfaces. All four change here.

- **The configuration schema.** A folder mapping with `DeclaredRole: Outbox` is refused at startup. **Operator's action**: remove that mapping on the release already running and let the deployment roll before installing this one, so no replica of either build maps the role while the migration clears it; the folder it named stays, holding whatever copies were already filed, which can be deleted by hand.
- **The MCP tool contract.** `Outbox` leaves the roles the `folders` argument and a listed folder's role take, `list_outgoing_emails` is added, `get_outgoing_email` gains the preview where the caller holds both grants, and each account in `list_accounts` gains its outbox count. **Caller's action**: a prompt, saved call, or integration that named `role:Outbox` reads the outbox with `list_outgoing_emails` instead.
- **The client endpoint.** The `/outbox` page lists only what the outbox holds, carries the row above, and requires both grants; one send read by identity carries the preview; `/outbox/dismissal` is added; each account in the folder tree carries its count; and `outbox.changed` joins the signal kinds. **Operator's action**: none beyond deploying, since the web client is served by the deployment it talks to; a desktop or Android client older than the release reads the outbox wrongly until it is updated, and the release notes say so.
- **The database schema.** One additive migration, as above. **Operator's action**: apply the release's schema artifact as for every release, after the configuration step.

### Consequences

- Good, because a refused send has a place the person looks, and leaves it only by somebody's decision.
- Good, because the outbox is read-only structurally: no act can name it, so no act, custodian, or later reader can forget to refuse.
- Good, because ADR 0039's folder model is untouched, and every reader written against it stays correct.
- Good, because one identifier follows a send from the call that queued it to the decision about it, on every surface.
- Good, because a deployment has one outbox per account, MailFathom's own, instead of a mirror on somebody's server that every mail client can act on to no effect.
- Neutral, because the outgoing record gains two columns, and the subject is a new copy of mail metadata that inherits the record's classification and erasure.
- Neutral, because the outbox is reached by a route and a tool of its own rather than by the ones that read folders, which a client and an agent must know about — the folder tree and `list_accounts` are where each learns it is there.
- Bad, because all four public surfaces change in one release, and the configuration step has to be taken on the previous release first.
- Bad, because a deployment that relied on the `\Draft` mirror to see waiting mail in another mail client loses that view, and the copies already filed are left for the operator to delete.

## Validation

- Owner review of this record before issue 2616 or issue 2617 starts.
- Issue 2616 lands with unit tests for each stage the outbox lists and each it does not, for dismissal on each stage it is offered at and refused at, for the count, for the subject read from the MIME of a record that carries none, and for the startup refusal of a mapping that names the role.
- `PublicSurfaces.UnitTests` records the MCP tool contract and the HTTP API contract, so the removed role, the new tool, the new fields, the new route, and the new signal kind appear there as an intended diff in the pull requests that make them.
- A test proves that a move, a copy, and a flag change naming an `OutgoingEmailId` are refused as unknown, and that no destination can resolve to the outbox — on the client API in issue 2616 and on MCP in issue 2617 — so the structural refusal is asserted rather than assumed.
- The migration is reviewed as SQL under `$add-migration`, and the integration suite covers it over a database carrying `held` filing rows and a folder row playing the role.

## Pros and Cons of the Options

### A1 — a folder row per account that holds no messages

- Good, because every surface would name the outbox by a `MailFolderId` like any other folder.
- Bad, because it is a folder that every act and every reader must treat as an exception, which puts the read-only rule back where somebody has to remember it.
- Bad, because it is the one folder row on a mirrored account that is not a projection of the source.

### A3 — outgoing messages materialized as stored messages

- Good, because every existing reader would show them without change.
- Bad, because unsent mail would be stored, cut, embedded, and searchable, every act would have to refuse it message by message, and it would sit beside the sent copy as a second one.

### B2 — terminal sends leave at once

- Good, because the outbox drains with no decision from anybody.
- Bad, because a refusal disappears from the one place the person looks, which is what the feature exists to stop.

### B3 — a refusal shown for a fixed time

- Good, because the outbox drains without a new decision.
- Bad, because a refusal nobody saw still vanishes, only later.

### C1 — the full outbox under the sending grant alone

- Good, because it is one grant, as the client's routes already take.
- Bad, because a send-only credential would read the correspondence of everybody sending from the account.

### C3 — status only

- Good, because nothing changes.
- Bad, because a list of sends naming no recipient and no subject cannot be used as a folder.

### D2 — a dedicated refusal every act checks

- Good, because the refusal would name the outbox explicitly.
- Bad, because every act and every custodian would carry the check, and a new act that missed it would half-happen on an entry.

### E1 — `role:Outbox` on the reading tools

- Good, because an agent would learn no new tool.
- Bad, because the rows are not stored messages, carry no email identifier any other tool takes, are read under another grant, and have no search document or embedding for `search_emails` and `ask_mail` to draw on.

### F1 — `Outbox` kept as the virtual outbox's role

- Good, because the name already exists on every surface.
- Bad, because a role is what a folder plays, and a role no folder may play is an exception every reader of the role has to know.

## More Information

- Issue 2614 asks the questions this record answers; issue 2619 is the feature it belongs to; issue 2616 implements it on the client API and retires the role, issue 2617 on MCP, issue 2618 in the client against issue 2615's design, and issue 2613 reports a refused send durably and leads to its entry here.
- [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) decided the outbox role and its `\Draft` mirror as part of filing an outgoing message; this record removes both and leaves the draft and sent filings, and every other command it governs, unchanged.
- [ADR 0039](0039-one-folder-model-and-one-implementation-per-custody-phase.md) holds the folder model this record leaves untouched; [ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md) is why nothing on MCP offers a send again; [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) is why a send is the account's; [ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) governs how the preview draws the message; [ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md) governs the signal; [ADR 0004](0004-versioning-and-release-policy.md) permits the breaks.
- Revisit when serving an attachment from the outbox is asked for, when a client needs an outbox's history of sent and withdrawn sends rather than only what has not left, or when a deployment needs waiting mail visible in a mail client other than MailFathom's.
