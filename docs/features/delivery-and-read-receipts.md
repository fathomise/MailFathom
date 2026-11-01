# Delivery and read receipts

<!-- describes: backend/src/Domain/Delivery/Receipts/**, backend/src/Application/Mail/Delivery/Receipts/**, backend/src/Infrastructure/Mail/Mime/MailReceiptReading.cs, backend/src/Infrastructure/Persistence/Delivery/ReceiptReportStore.cs, backend/src/Infrastructure/Persistence/Delivery/StoredOutgoingEmailReceipts.cs, backend/src/Host/Api/ClientReadReceiptEndpoint.cs, backend/src/Host/Api/ClientMailReceiptResponses.cs, backend/src/Mcp/Tools/Outgoing/*Receipt*.cs, backend/src/AI/Orchestration/AgentReceiptReadings.cs, backend/src/Infrastructure/Mail/Dkim/OrganizationSignatureVerifier.cs -->

A message the account sends may ask to be told what became of it: a delivery notification, which the receiving mail
servers write (RFC 3461 asks for it and RFC 3464 is the report), and a read receipt, which the recipient's mail app
writes (RFC 8098). [Mail delivery](mail-delivery.md#the-record-a-send-is-written-down-as-before-anything-is-sent)
describes how a send asks for either. This page is the other half: what MailFathom does with the reports that come back,
and what it does when a message that arrived asks the person for a read receipt.

**A report is information, not proof.** Anybody can write a delivery status notification or a read receipt, a receiving
server may send none, and most mail apps send no read receipt at all or send one only when their user agrees. So a
report changes what MailFathom tells the person about a message they sent, and nothing else: it never moves a send's
stage, never retries or withdraws anything, and the absence of one says nothing.

**Between two accounts of one organization, no report is needed.** Both ends are MailFathom, so this deployment sees the
delivery and the reading itself, and marks a colleague's message *delivered* and *read* whatever the composer asked for
— [below](#between-two-accounts-of-one-organization).

## What is recognized

A message is a report when its top-level body is `multipart/report` with a `report-type` of `delivery-status` or
`disposition-notification`. Nothing else qualifies, however its subject reads — an automatic reply that calls itself a
receipt is ordinary mail.

| Report | Read from | Outcomes recorded |
|---|---|---|
| Delivery status notification (RFC 3464) | The `message/delivery-status` part: `Original-Envelope-Id` and `Reporting-MTA` of the message, and per recipient `Final-Recipient`, `Original-Recipient`, `Action`, `Status`, and `Remote-MTA` | `delivered`, `relayed`, `expanded`, `failed`, `delayed` |
| Read receipt (RFC 8098) | The `message/disposition-notification` part: `Original-Message-ID`, `Final-Recipient`, `Original-Recipient`, and the disposition type of `Disposition` | `displayed`, `deleted`, `dispatched`, `processed` |

A report naming more than 100 recipients is read for its first 100. The status code kept beside a delivery outcome is
the RFC 3463 code the report gave, such as `5.1.1`, at most 16 characters; the server it names is kept to 255.

Recognition happens once, when the message arrives. A message stored before this feature existed, or re-derived since,
is not read again as a report.

## How a report finds the message it is about

A report is matched only against mail the same account sent, and in this order, the first that names a message
winning:

1. the envelope identifier the delivery report carries back — every send that asks for a delivery notification is
   transmitted with `ENVID` set to its own identifier, so this names the send exactly;
2. the `Original-Message-ID` a read receipt carries, held against the `Message-ID` the account's sends were written
   with;
3. the report's own `In-Reply-To`, then its `References` from the newest back, held against the same identifiers.

Within the matched send, a report's recipient is matched by its `Original-Recipient`, then by its `Final-Recipient`,
and — where the send had one recipient — to that recipient. A report naming nobody the send was addressed to records
nothing: an outcome is always about a real recipient of a real send.

A report that matches nothing is still a report. It is marked as one, with no message beside it, and it stays out of
every agent's sight like any other.

## Between two accounts of one organization

A message one account of an organization sends to another account of the same organization on this deployment is
marked *delivered* and then *read* for that recipient by MailFathom itself, with neither request switched on on the
composer. Its `source` is `organization` rather than `report`.

**What makes it this kind of message is the organization's signature**, never an address. The receiving account stores
a message whose author a signature of its organization proved a member
([ADR 0042](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md)),
and the signature names the accounts the author sends as. The send whose `Message-ID` the message carries, in one of
those accounts' outboxes, is the send that arrived. So unsigned mail, mail another organization signed, mail from or
into an account in no organization, and a message whose `Message-ID` no such send carries mark nothing, and a forged
message cannot make a sender believe their mail was read.

**Delivered** is recorded as soon as the receiving account stores the message, for each recipient of the send whose
address is that account's own address. A send to several colleagues is marked recipient by recipient, as each of their
accounts stores it; a recipient reached through an alias or a forward is another address and is not marked.

**Read** is recorded when a person the receiving account is assigned to opens the message in MailFathom's own client —
the act [ADR 0026](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md)
decides, which the client states on the flag change it submits. Nothing else records it: a rule, the spam verdict, an
MCP caller, *mark as read*, and another mail client each set `\Seen` without reporting a reading. A person who turned
[`markReadOnOpen`](../operations/client-endpoint.md#the-preferences-routes) off writes nothing when they open a message,
and so MailFathom reports no reading for them either; a read receipt the sender asked for still travels, as the next
paragraph says, and another client answering it can still tell them. MailFathom keeps no read state of its own beside the server flag, and this adds none:
what is recorded is the sender's outcome, not the reader's state. A message opened again keeps the first reading.

**The composer's requests stay available, and the organization's outcome takes precedence.** A colleague may also read
their mail in another client, so a delivery notification and a read receipt are still asked for when switched on and
still carried onto the wire. Where both arrive for one recipient, the outcome MailFathom saw replaces a delivery or read
outcome a report recorded earlier, and a report arriving afterwards changes nothing; where MailFathom has seen nothing
yet, a report is recorded exactly as for anybody else, so a reading done in another client still shows through its
receipt.

**The custody phase changes nothing.** The delivery hangs on the recipient's stored copy, which exists whether the
receiving account is `Mirrored`, `Held`, or `Restoring`, and the reading hangs on the opening, which every phase carries
the same way; neither reads the phase.

**Who sees it, and for how long.** Whether a colleague has read a message is personal data about that colleague, and
reporting it automatically is a form of monitoring between members of one organization. It is reported only inside one
organization, only to the account that sent the message, and only for the act the reader can turn off with
`markReadOnOpen`. Each outcome is erased with the send, with the receiving account's stored copy it was observed on, and
with either account. Mail stored, or opened, before this existed is not looked at again.

## What is recorded

Each matched outcome is a row per send, recipient, and kind — delivery or read — in
`outgoing_email_receipt_outcomes`, which holds the outcome, when the report says it was written, where it came from,
and for a delivery the status code and the server. **A later report replaces a `delayed` one and nothing else**: a final
delivery outcome, and every read outcome, stands as the first report gave it, so a duplicate or a forged second report
cannot rewrite what the person was already told. An outcome MailFathom saw itself between two accounts of one
organization replaces whatever a report said and is replaced by nothing. The rows go with the send and with the
account, exactly as the send's recipients do, and an organization's outcome also with the received copy it was observed
on.

The report message itself is stored like any mail and marked with what it said: its kind, the recipients and outcomes
it named, and the subject of the send it matched. That mark is what lets the client show a report as what it is rather
than as a message, and it is why no agent needs to read one.

## What a person sees

The client API serves both halves, and [the client endpoint](../operations/client-endpoint.md) lists the fields:

- **On a message the account sent**, a summary in the mail list — how many recipients, how many deliveries were
  asked for, delivered, delayed, and failed, how many reads were asked for and reported — and on the opened message
  each recipient with its delivery and read outcome, when each was reported, and where it came from. A message that
  asked for nothing still carries the summary once any outcome is recorded, which is how a colleague's delivery and
  reading show on mail sent with no request switched on.
- **On a report**, its kind, the recipients and outcomes it names, when it was written, and the subject of the send it
  is about, or no subject where it matched none.

## Answering a read receipt

A message that arrived may ask the person for a read receipt with `Disposition-Notification-To`. The opened message
says so: the address the receipt would go to, whether that address is another one than the message's `Return-Path`
and `From` — which RFC 8098 asks a mail app to point out, because it is how a receipt is redirected to somebody the
reader never wrote to — and when a receipt was already sent. Nothing is offered on a message held as spam, on a filed
copy of the account's own send, or on a message the account itself wrote.

**A receipt is sent only when the person asks for it**, through `POST /api/client/messages/{storedEmailId}/read-receipt`.
Opening a message, a rule, the `\Seen` flag, an agent, and an MCP caller never send one. The caller needs
`mailfathom.mail.read` beside `mailfathom.mail.send`, because a receipt answers mail it read: a send-only credential
learns nothing about a stored message from the route, not even whether it exists. It is refused — with a reason
the client reads — where the message asked for none, where one was already sent (with when), where the message is held
as spam, where it is the account's own, and where the account has no sending identity.

The receipt is an RFC 8098 report: `multipart/report; report-type=disposition-notification`, a short human-readable
part in the reader's language, and a disposition part carrying `Reporting-UA`, `Final-Recipient` naming the receiving
account, `Original-Message-ID`, and `Disposition: manual-action/MDN-sent-manually; displayed`. Its subject is the
original's with `Read:` in front, it threads under the message it answers, and it carries `Auto-Submitted: auto-replied`
so an automatic responder on the other side does not answer it in turn. It goes from the receiving account to the
requested address through the account's own outgoing pipeline — the same governance, audit, outbox, and delivery as any
message the account sends — and **once per message**: the first request claims the message before anything is
composed, a second is refused, and a claim whose send was refused is released so the person can try again.

**The envelope sender is the account's own address, not the null sender RFC 8098 recommends.** The RFC says an MDN
SHOULD go with an empty `MAIL FROM` so that a failure to deliver it raises no further report. The outbox transmits
every message with the account's address, and the submission servers an account sends through authenticate the account
and commonly refuse a null sender from it, so a receipt sent with one would fail where a receipt sent with the
account's address arrives. A bounce of a receipt therefore returns to the account like a bounce of any message it
sent, which is the cost taken knowingly.

## What agents see

A report is shown to no agent. Every reader that selects mail for an agent leaves reports out — the thread-state agent
and the count and newest arrival that tell it a conversation changed, the reply drafter, the Agent in the agent view,
enrichment, calendar extraction, contact relationships, body cleanup, the day layout, discovery, and the agent behind
`ask_mail` — so a report arriving in a conversation whose state is current leaves it current.

What an agent is given instead is what was recorded. A message the account sent that an agent reads — in a search's
extracts or a conversation read whole — carries a `receipts` element: whether a delivery notification and a read receipt
were asked for, and per recipient the delivery and read outcome, when each was reported, and its source. A message
that asked for nothing and got nothing carries none. The recipient addresses pass the sensitive-content guard like a
sender's. So *was my message delivered?* and *has she read it?* are answered from the same rows the client draws.

The MCP tools that return a sent message — `get_outgoing_email` in its outbox preview, `list_outgoing_emails`, `get_email_content`,
`list_emails`, and `search_emails` — carry the same reading as a `receipts` field;
[the MCP tools](mcp-tools.md) describe it.
