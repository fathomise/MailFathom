# Automatic reply

<!-- describes: backend/src/Domain/AutomaticReplies/**, backend/src/Application/AutomaticReplies/**, backend/src/Infrastructure/Persistence/AutomaticReplies/**, backend/src/Infrastructure/Mail/Mime/MimeKitArrivedMailEnvelopeReader.cs, backend/src/Host/Api/ClientAutomaticReplyEndpoint.cs -->

A person who is away sets an automatic reply, and mail arriving on the accounts they chose is answered with it until
the period ends or they switch it off. Nothing an operator configures turns it on or off: reading one takes
`mailfathom.profile.read`, and saving one takes `mailfathom.profile.write` together with `mailfathom.mail.send`, since
what a save arranges is mail sent as the person's accounts — [Permissions](../operations/permissions.md#the-published-set)
holds both. It is set over [the automatic reply routes](../operations/client-endpoint.md#the-automatic-reply-routes) of
the client endpoint, and no screen of the client offers it yet.

## What a reply is

| Part | What it decides |
| --- | --- |
| The text for senders outside the organization | What every sender is answered with, unless the next row applies. Required |
| The text for senders inside the organization | What a colleague is answered with instead. Optional, and only for a person who is a member of an organization |
| The accounts | Which of the person's own accounts answer, at most 64 |
| The period | When answering starts and when it stops. The end is exclusive, and both are instants rather than days |
| Switched on | Whether it answers at all, so a person back early ends it without rewriting it |

Each text is plain text of at most 8000 characters. A reply is the whole of what a person states: saving one replaces
the one before it, and **a save starts it over**, so a sender answered under the replaced reply is answered once more
under the new one.

**A colleague is recognized by proof, never by the address.** A message is answered with the inside text only where it
carries a signature [made with a key of the receiving account's organization](sender-authentication.md#a-colleague-in-the-receiving-accounts-organization)
that verified, which is the trust MailFathom grants an organization member, and only where the answer goes back to
the `From` address that signature proved. A `From` claiming a colleague's address is something anybody can write, and
the inside text is usually the one that says more, so a forged colleague is answered with the outside text like any
other sender — and so is a colleague's message whose `Reply-To` names another address, because the signature proves
nothing about that one.

## Which accounts may answer

**Only the person's own accounts, and never a shared mailbox.** An account that is not assigned to them is refused
when the reply is saved, and so is an account also assigned to somebody else: one of the people reading a shared
mailbox being away says nothing about whether it is answered, so nobody is away on its behalf. The check is made again
whenever mail arrives, so an account shared after the save stops answering without anybody rewriting the reply.

## Which mail is answered

An arrival is answered when its account's one assigned user has a reply switched on whose period contains the
instant it settles, and when it settled where the person reads new mail: in the inbox, or filed out of it by a
[rule](mail-rules.md), and not as junk, in the trash, or already gone from the server. **Only mail the mailbox received
since the period started is answered**, by the date its own server recorded on the last `Received` hop: a mailbox's
first synchronization, or one after its folders were renumbered, stores years of history that settles during the
period like anything new, and none of it is answered — nor is a message carrying no such date. Mail that did arrive
during the period is answered even if it was already read on another client, since being read elsewhere says nothing
about whether the person is there.

**Nothing that would start a loop is answered**, which is what [RFC 3834](https://www.rfc-editor.org/rfc/rfc3834)
asks of an automatic responder. An arrival is left unanswered when:

- it carries `Auto-Submitted` with any keyword but `no` — another responder, or a program;
- it is a mailing-list posting, saying so with `List-Id`, `List-Post`, or `List-Unsubscribe`;
- it carries `Precedence: bulk`, `list`, or `junk`;
- its return path is null (`Return-Path: <>`), which is a bounce or another delivery notification;
- its answer would go to an address nobody reads: one beginning `owner-` or ending `-request`, which are a list's own
  addresses, or a `noreply`, `do-not-reply`, `mailer-daemon`, or `postmaster` mailbox, however its separators are
  written;
- its answer would go to the account's own address;
- classification judged it junk.

The answer goes to the first `Reply-To` mailbox where the message names one, and to the first `From` mailbox
otherwise. Neither header is authenticated for this purpose, so a forged message can direct one reply carrying the
outside text to an address of its choosing — once per address per reply, by the rule below — exactly as a forged
`From` alone could; the inside text never travels that way.

**Each sender is answered once per reply.** The first arrival from an address is answered and every later one is not,
however many arrive during the period; the address is compared as normalized, so case does not make a second sender.
That is a row in PostgreSQL rather than a memory of the process, so two replicas settling one sender's mail at once
send one reply between them — the replica whose claim affected no row writes nothing. A save mints a new revision of
the reply and removes the answers recorded under the old one in the same statement, which is what starting over means.
[Stored email schema](../architecture/stored-email-schema.md#what-a-person-answers-while-they-are-away) holds both
tables.

## What is sent

The reply is an ordinary outgoing message queued on the account's own [outbox](mail-delivery.md), sent from the
account's own address and under every bound and governor any other send meets. It carries:

- `Auto-Submitted: auto-replied`, so a responder on the other side does not answer it in turn;
- the subject of the message it answers behind `Auto:`, which is not added a second time;
- `In-Reply-To` and `References`, so it lands in the conversation it answers;
- the text chosen for the sender, as plain text.

Its idempotency identity is the stored email it answers, so a retried attempt queues the same message rather than a
second one. A send the outbox refuses — a governor, a size, an account that cannot send — leaves that arrival
unanswered and is not retried; the sender's claim was taken first, so the sender is not answered later in the period
either. Answering is not durable beyond that: it runs once, when the message settles, and an arrival a failure skipped
is not looked at again.

## Custody phases

The answer is the same in all three [custody phases](held-mailboxes.md), because nothing in it reads the phase:

| Phase | What happens |
| --- | --- |
| Mirrored | Mail synchronized from the source is answered when it settles, and the reply leaves through the account's outbox |
| Held | Mail the source still delivers is synchronized and settled exactly as while mirrored, and is answered the same way. What the person files locally is stored already settled and is never answered |
| Restoring | Mail arriving on the source is answered as while mirrored. What the restore appends back to the source is stored already settled and is never answered, so a restore answers nobody a second time |

## Privacy

The texts are the person's own words and the answer rows name their correspondents, so neither reaches a log, a metric,
a trace, or an error message; a failure names the account alias at most. Both go with the user's record when it is
erased, and the answer rows are kept for as long as one reply runs: every save deletes the ones it outlived.
