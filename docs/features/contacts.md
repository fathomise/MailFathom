# Contacts

<!-- describes: backend/src/Domain/Contacts/**, backend/src/Application/Contacts/**, backend/src/Infrastructure/Persistence/Contacts/**, backend/src/Infrastructure/Persistence/Entities/ContactEntity.cs, backend/src/Infrastructure/Persistence/Entities/ContactAddressEntity.cs, backend/src/Infrastructure/Persistence/Entities/ContactPhoneNumberEntity.cs, backend/src/Host/Api/Contact*.cs, backend/src/Host/Api/OrganizationContactEndpoints.cs, backend/src/Host/Api/ClientContactEndpoints.cs, backend/src/Host/Api/ClientContactPhotoEndpoint.cs, backend/src/Host/Api/ClientPersonPhotoEndpoint.cs, backend/src/Host/Api/PersonPhotoResponse.cs, backend/src/Host/Api/ClientContactAmendmentRequest.cs, backend/src/Host/Api/ClientContactCorrespondenceEndpoint.cs, backend/src/Host/Api/ClientColleagueEndpoints.cs, backend/src/Host/Api/ColleagueResponses.cs, backend/src/Host/Api/ClientPresenceEndpoints.cs, backend/src/Domain/Presence/**, backend/src/Application/Presence/**, backend/src/Infrastructure/Persistence/Presence/**, backend/src/Host/Configuration/UserSettings/MemberDirectory.cs, backend/src/Infrastructure/Persistence/Users/*OrganizationMember*.cs, backend/src/Cli/Commands/Contacts/**, backend/src/Cli/Administration/Contacts/**, backend/src/Mcp/Tools/Contacts/**, backend/src/Host/Configuration/Mail/ContactCollection*.cs, backend/src/Host/Configuration/Mail/Readers/ConfiguredContactCollectionSettingsReader.cs, backend/src/Infrastructure/Mail/Mime/MailAutomationReading.cs -->

MailFathom holds a contact book of its own: people, the addresses they use, and what a user recorded about them, in
the same PostgreSQL database the mail is in. This page describes the record and the rules every writer of it obeys —
what identifies a person, when two addresses are the same address, who may change what, and what erasing somebody
removes.

**Three surfaces reach the book.** `mfctl contact` maintains it over the deployment's administrative endpoint;
[administering a deployment](../operations/admin-endpoint.md#administering-the-contact-book) holds the command group in
full. An agent reads and writes it over the MCP endpoint, under two grants of its own; [MCP tools §
`list_contacts`](mcp-tools.md#list_contacts) holds those six tools and what each of them answers. A person reads and
writes their own over the client endpoint, under the same two grants the agent holds; [the client endpoint § The contact
routes](../operations/client-endpoint.md#the-contact-routes) holds those seven routes, the three [that hold a contact's
photo](../operations/client-endpoint.md#the-contact-photo-routes), and the two the administrative
surface keeps to itself. A fourth writer is the
deployment itself: [§ Collecting contacts from arriving mail](#collecting-contacts-from-arriving-mail) describes what an
account records on its own, and it is off until a user switches it on, so an instance nobody has written to and nobody
switched collection on for holds no contacts at all.

**One reader is not a surface at all.** A message may be addressed by naming somebody in the book rather than by writing
an address, and the send resolves that name against the book on its way to the outgoing record; [mail delivery §
Addressing a message by naming a contact](mail-delivery.md#addressing-a-message-by-naming-a-contact) holds how a name
becomes an address, which name refuses to, and what the record then keeps. Nothing about that writes the book: addressing
somebody is not a fact about them, so no contact is created, amended, or promoted by being written to.

## Three books, and a read of them

**What a person wrote down belongs to them; what an administrator wrote down for an organization belongs to the
organization; what a mailbox picked up belongs to the mailbox.** Those are three books, with three holders:

| Book | Holder | What is in it |
| --- | --- | --- |
| A user's own | The user | Everything anybody wrote down for them — asserted contacts |
| An organization's | The organization | Everything an administrator wrote down for every member to read — asserted contacts |
| A mail account's collected | The mail account | Everything collection picked up out of the mail that arrived on that account — every collected contact |

A contact record names exactly one of the three, the record's origin agrees with which it named — asserted for a user's
or an organization's, collected for a mailbox's — and the database refuses a row where they disagree rather than leaving
every reader to reason about it. So a collected record filed under a user or an organization, or an asserted one filed
under a mailbox, is not a state this system has.

The split is what a shared mailbox costs and what it buys. Two people assigned one account are two users and one
mailbox: the account collects a correspondent **once**, both of them read that one record, and neither of them holds a
copy the other's edit would have to chase. And when one of them leaves, what goes with them is the book they wrote —
the mailbox's stays for whoever is still assigned it. [Single-tenant multi-user
ownership](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md)
is the decision the second half of that follows from.

**A user reads all of theirs, as one book.** Every read a user makes — a listing, a lookup by address, a lookup by
name, an MCP tool, addressing a message by naming a contact — covers their own book, the book of the organization they
are a member of, and the collected book of every mail account they are assigned. That set is resolved from who the
caller was admitted to act for and which organization they are a member of, asked before the books are read, exactly as
the mail beside it is, and no request of theirs can name another user's or another organization's. A user who is a
member of no organization reads no organization's book. Every contact a read serves states the organization holding it
where one does, as `organization` on the administrative and client routes and on every contact an MCP tool returns, so a
reader tells the organization's contacts apart from their own: both are asserted, so the origin cannot.

**The order between those books is fixed and stated, because two of them may hold one address.** It is the user's own
book first, then their organization's, then the accounts by the ordinal order of their identifiers, and **a record is
hidden when a book earlier in that order holds one of its addresses**. A member who writes down somebody the
organization already recorded reads their own record of them, and every other member goes on reading the
organization's. Two more things follow. A contact the user wrote down hides a collected one carrying
the same address — which is the point of writing somebody down: their name and their note are what the user sees, and it
hides it for that user alone, because the other users of the mailbox never saw their record. And an address two of their
accounts both collected is answered once, from the first of the two in that order — the same answer on every read, and
the same answer for two users of one mailbox, because the order is the accounts' own rather than the order either user's
record happened to declare them in. The database applies it in the query rather than a reader applying it afterwards, so
a page never serves one person twice, and it is applied as the page is read rather than to a page already served — so a
page still holds the size asked for while the walk has more to serve, and the cursor rather than the count is what says
whether it does.

**Resolving an address takes the same order over the address rather than over the record**, which is the one place the
two differ. A hidden record may hold an address no earlier book holds — a mailbox that collected somebody under two of
their addresses, where the user wrote only one of them down — and a caller resolving that second address already has it
in hand, so answering that nobody uses it would deny an address this deployment holds and the reader is assigned. The
address they share still answers from the record a listing shows.

**Which book an act writes is never the same question as which books it reads.** A caller's writes go into their own
book: recording a person, amending one, promoting one. Collection writes into the book of the account it is
synchronizing and into no other. Erasure is the exception and deliberately so — see [§ Erasing and exporting a
person](#erasing-and-exporting-a-person) — and even erasure reached through a user's books never reaches the
organization's: a member reads the organization's contacts and writes none of them. On the client endpoint an
amendment, an erasure, or a photo write a member asks for on one of them is refused `409` under the code `51017`
(`OrganizationContactReadOnly`), and over the MCP endpoint the same refusal arrives as that code; a promotion of one
writes a copy into the member's own book — its name, addresses, preferred address, note, and phone numbers — and
leaves the organization's record where it is for every other member.

Each book keys onto its holder and is erased with it, by the schema rather than by a statement somebody remembers to
write. Erasing a user takes their own book, deleting an organization takes the organization's book, and erasing a mail
account takes the book it collected, beside that account's folders and mail; `contact_addresses` keys onto `contacts`
through `(ContactId, BookHolderId)` in every case, so the cascade reaches an address through the person holding it. A
collected book therefore goes when its account does, which is when the last user assigned it is erased.

## The organization's book

An organization's book is what its administrators record for every member at once — a supplier, a firm of accountants,
the people every member's mailbox should recognize. It holds the same record every other book holds, under the same
limits, and an address belongs to at most one contact of it. Every contact in it is asserted, because an administrator
writing a person down is somebody writing a person down.

**Only the administrative surface writes it.** It is reached beneath the organization's own route on the administrative
endpoint and with `mfctl contact … --organization <id>`; [administering a
deployment](../operations/admin-endpoint.md#administering-the-contact-book) holds the routes and the commands. Recording,
amending, and promoting into it need `mailfathom.admin.operate`, erasing from it `mailfathom.admin.erase`, and reading or
exporting it `mailfathom.admin.audit.read`, each held at the organization's scope or the deployment's. An administrator
whose scope is another organization is answered as if the deployment held no such organization, so a refusal does not
tell them another organization exists. The client endpoint and the MCP tools read it beside the caller's own book and
write none of it: `mailfathom.contacts.write` is what a member's client and an agent hold, and neither of them writes what
every member's mailbox recognizes.

**Promoting into it names the mailbox the record is copied out of.** An organization reads no collected book of its
own, so a promotion into it names the mail account that collected the person, under the same scope rule a collected book
is read under everywhere else.

**Every write and the deletion of the organization serialize on the addresses they touch.** Each single-contact write —
into any of the three books — and each deletion of an organization takes a transaction-scoped advisory lock keyed by
each address it adds, keeps, or removes, in one sorted order so two of them never wait on each other. Two writes that
reach one address therefore run one after the other rather than interleaving, whichever books they write, and so does
the deletion of an organization against a write into its book. Erasing everything one account collected takes no such
lock, because it is a sweep over a mailbox's whole book rather than a write about one person.

**MailFathom's own work finds the organization's contact by address.** It is one indexed seek on the book and the
address's comparison form, so it ignores case like every other match in the book, and it answers with the contact's
identity alone rather than anything the contact says. Nothing but MailFathom's own identity may ask it.

## The organization's member directory

Every person of an organization is shown to the other members as a **member contact**, without anybody writing one
down. The directory is not a fourth book: no row holds it. It is composed from the users' own rows and records each
time it is read, so a user joining an organization, leaving it, being erased, gaining or losing a mailbox, or changing
their record shows in their colleagues' directory on the next read with nothing written anywhere else.
[ADR 0044](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0044-an-organizations-member-directory-as-a-projection-of-user-records.md)
is the decision.

| What a member contact shows | Where it comes from |
| --- | --- |
| The name | The user's label |
| The addresses | Every mail account in the same organization assigned to that user and to nobody else, in the ordinal order of the accounts' identifiers. An account assigned to two or more users is a shared mailbox and is nobody's address here |
| The preferred address | `PreferredAddress` in the user's record while it is one of those addresses, otherwise the first of them; none where there is no address |
| The phone numbers | `PhoneNumbers` in the user's record, under the labels and rules a contact's phone numbers obey |
| The job title | The title an administrator wrote on the user's row |
| The organization's name, the portrait, the time zone | The organization's row, and `Portrait` and `TimeZone` in the user's record |
| The working hours | `WorkingHours` in the user's record as the settings policies governing them resolve it, so an organization's default reaches every member stating none; the client shows them in the member's time and in the reader's, and *Outside working hours* while it is outside them where the member is. [When this person works](../operations/configuration-sources.md#when-this-person-works--workinghours) holds the rules |

**Who is in it.** Every user of the organization whose type is `User`. A user an administrator marked `ServiceAccount`
— a technical account somebody signs a robot in as — is never shown, and the reader is never their own colleague. A
user whose record does not bind is still shown, from what their row holds; one whose label no contact name could carry
is left out. The directory reads at most 1,000 members of one organization per read; an organization larger than that
needs the read to move into the database, which is the upgrade the ceiling names.

**Where it sits in the order.** The reader's own book comes first, then the directory, then the organization's book,
then the collected books. A member the reader wrote down is shown from the reader's own record and not from the
directory. A listing of the reader's own book or of what their mailboxes collected, as the client endpoint reads it,
leaves out an organization contact and a collected contact holding a colleague's address, because the colleague answers
for it; the MCP tools and the administrative surface read the books without the directory and are unchanged.

**It is read-only by construction.** There is no route that changes a member contact: what it shows is what the user's
record and an administrator state. A reader keeps a colleague differently by promoting them, which writes a copy of
their name, addresses, preferred address, and phone numbers into the reader's own book; a colleague with no address
cannot be copied, because a contact needs one. The client endpoint serves the directory beneath `/contacts/colleagues`
under `mailfathom.contacts.read`, and the promotion under `mailfathom.contacts.write`; [the client endpoint § The
contact routes](../operations/client-endpoint.md#the-contact-routes) holds the routes. A user's job title and type are an
administrator's to write, through `mfctl user set-job-title` and `mfctl user set-type`.

**Where a colleague sits is read from one manager per person.** An administrator names, with `mfctl user set-manager`,
the member of the same organization a user reports to, and that one upward edge is the only fact stored: a colleague's
direct reports are every member naming them, derived on each read and never written. A colleague's page shows the chain
of managers from the top of the organization down to them, then their direct reports by name, each leading to that
member's own page. The reader stands in the structure like anybody else and leads nowhere, being nobody's colleague to
themselves, and a manager the directory does not list — a service account — ends the chain above it rather than leaving a
gap. The deployment refuses a manager who is the user, who is not a person of the user's organization, or who already
reports to the user directly or through others, and each refusal names its rule. A manager who leaves the organization,
or is erased, stops being anybody's manager there, and a user who moves to another organization leaves their own manager
behind. The structure is read beneath `/contacts/colleagues/{userId}/structure` under `mailfathom.contacts.read`, for the
client alone: no MCP tool reads it.

### A colleague's presence

A member contact also says whether the colleague is around: one of six states, worked out when it is read from the
clients the colleague keeps open and the state they chose, and never stored.
[ADR 0045](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0045-a-members-presence-from-the-clients-it-keeps-open-and-the-state-it-chose.md)
is the decision.

| State | When a colleague reads as it |
| --- | --- |
| `Active` | A client of theirs is open and a person used it in the last ten minutes |
| `Away` | Every open client of theirs has had no input for ten minutes |
| `Busy`, `Do not disturb` | They chose it, and a client of theirs is open |
| `Offline` | No client of theirs is open, whatever they chose; or they chose `Offline` or `Hidden` |

- **An open client is a row its client renews.** A running client renews it every minute, saying whether a person is
  using it, and removes it when it closes or signs out. A row stops counting three minutes after its last renewal, so
  a client that crashed, lost its network, or was last renewed by a replica that has since died stops counting with
  nothing written for it. Across several devices the most present one answers: `Active`, then `Away`. A member keeps
  at most sixteen clients open.
- **A chosen state lasts until changed or for a while.** The member chooses any of the six from the status menu, until
  changed or for 30 minutes, an hour, two hours, the rest of today, or the rest of this week. Today and this week end
  at midnight in the member's own time zone, the week at the end of Sunday, and in UTC where their record states none.
  Choosing `Active` until changed returns them to the presence their clients produce. A choice is kept while every
  client is closed and applies again when one opens, unless it ran out meanwhile.
- **`Hidden` is the member's own word for `Offline`.** They see `Hidden`; everybody else sees `Offline`, with no
  *Last seen*.
- ***Last seen* is one timestamp.** Every renewal moves it, except while the member's chosen state is `Hidden` or
  `Offline`. A colleague who reads as `Offline` shows it, and one who chose `Hidden` never does.

**What is held, and who sees it.** Presence is monitoring of an employee, so only the present is held: one row per open
client — the member, the client's own identifier, whether it is in use, and when the row expires — and one row per
member holding the chosen state, the while it was chosen for, when it ends, and the last-seen time. Each row is overwritten by the next write and
nothing keeps what it said before, so no history of anybody's presence exists. Both go with the user when they are
erased. Members of the same organization see a colleague's presence and nobody else: no MCP tool, administrative route,
or `mfctl` command reads it. A user in no organization is recorded nothing. No log line, metric, trace, or audit entry
names a person's presence or a renewal. A change another member could see is announced over the signal channel by
naming the member and nothing about them, and a client re-reads that colleague; a client that crashed or a duration
that ran out reaches colleagues on their client's own re-read, within five minutes. [The client endpoint § The
presence routes](../operations/client-endpoint.md#the-presence-routes) holds the routes.

### A colleague who is out of office

A member contact says the colleague is out of office, and until when, while the colleague's
[automatic reply](automatic-reply.md) is switched on and in its period: the end of that period and nothing else. The
reply's text, the accounts it answers on, and when it started never reach a colleague, so what a member wrote to the
people they correspond with is not shown to the people they work with. It is read from the reply on every read of a
colleague and stored nowhere else, so a reply switched off or past its end stops showing on the next read. No signal
announces either change: an open client learns of it when it reads the directory again. Like presence, it
reaches members of the same organization alone and no MCP tool reads it. The client marks the colleague in the
directory and on their page, and warns when a message is addressed to them.

## A contact is a person, not an address

One person uses a work address, a personal one, and an old one they still receive on. A book keyed on the address could
not say those were the same person, which is the whole reason to have a book rather than a list — so the record is the
person and the addresses hang off them.

| Part | What it holds |
| --- | --- |
| Identity | MailFathom's own UUID, minted when the contact is recorded and never derived from an address |
| Name | What the user wrote, kept in their casing, at most 256 characters and carrying no character that renders as nothing |
| Addresses | At least one and at most 32, each at most 320 characters |
| Preferred address | Which of them to use when something addresses the person without naming which |
| Note | Optional free text, at most 4000 characters, in which line breaks and tabs are kept and every other character that renders as nothing is refused |
| Phone numbers | None to 16, in the user's order, each a number and a label — see [§ Phone numbers](#phone-numbers) |
| Origin | How the contact came to be in the book — see [§ Two origins](#two-origins-and-who-may-write-which) |
| Recorded, amended | When the contact entered the book, and when it was last changed |
| Photo | Optional: the stored file a user gave a person they wrote down in their own book — see [§ A contact's photo](#a-contacts-photo) |

The identity is the book's own and is never an address, because an address is a thing a person has rather than a thing
they are: they give one up, gain another, and stay the same person. It is also the only part of a contact that may
appear in a log line, a metric, or a failure message, beside the origin, which is MailFathom's own classification of how
a record arrived rather than anything the person supplied. Everything else is personal data about a third party.

Which address is preferred is the user's choice rather than an ordering accident. Nothing picks one, and a record
naming a preferred address the contact does not hold is refused rather than repaired.

### Phone numbers

A person somebody wrote down may carry phone numbers, at most 16 of them, each a **number** and a **label** saying
which of their phones it reaches: `Mobile`, `Work`, `Home`, or `Other`. A label is matched without regard to case and
anything else is refused naming the four.

The number is kept as it was typed, trimmed of surrounding whitespace and otherwise unchanged: at most 32 characters,
drawn from the digits, `+`, space, `-`, `(`, `)`, and `.`, with at least one digit. Nothing normalizes it to E.164,
because nothing here compares, deduplicates, or dials one — it is read back to the person who wrote it and offered as a
link — and a normalization would need a numbering plan and a region to guess with. That becomes worth having when
numbers are matched against each other or exported as a vCard.

The order is the user's, and **the first number is the one to call by default**; there is no separate preferred
field to disagree with it. A refusal names the rule a number broke — its form, its label, or a seventeenth number —
and never the number.

**Only an asserted contact holds numbers.** Collection reads headers and never infers a number about a third party from
a signature, so a collected contact holds none and the book refuses a collected record that states one. Promotion
therefore copies no numbers, and the promoted record gains them when the user adds them. The numbers are rows of their
own beside the addresses, keyed on the contact and its position, and they cascade with the contact when it is erased.

A name and a note are published into listings of the other contacts, into an answer an agent reads, and into an export,
so both refuse the characters that carry no glyph of their own: the control characters, the line and paragraph
separators, and the formatting characters, among which the bidirectional overrides are what would let one name render as
text it does not contain. A note keeps line breaks and tabs, because a note is written to be read as somebody wrote it.
The zero-width joiner and non-joiner are admitted in both, because Persian, Arabic, and the Indic scripts write them
inside ordinary words and refusing them would refuse names people actually have. Text that is not well-formed at all —
a surrogate with no partner, which is no character and has no category — is refused for a different reason: the first
thing to reject it otherwise would be the encoder writing the row, so a value somebody typed would come back as an
encoding failure rather than as a value that was not accepted.

## A contact's photo

**A user may give a photo to a person they wrote down in their own book, and to nobody else.** It is the same kind of
thing as the portrait a user is drawn by, and it is stored the same way: a stored file the contact's record links to,
held wherever [`ContentStorage:Backend`](../operations/configuration-runtime.md#contentstorage) places content, owned
by the user whose book holds the contact. A collected contact belongs to a mailbox rather than to a user, and an
organization's to the organization, so neither has a user to own the file and neither takes a photo; the record refuses
one, and so does the database, which holds a photo link only on a row of a user's own book. Nothing is ever fetched
from outside to supply one — not from a sender's domain, not from a public avatar service, not from the mail — so a
contact has a photo exactly when the user chose a file for them.

**A photo is at most 1 MB and is a JPEG or a PNG**, judged by the signature its octets open with, and it is served back
exactly as it was supplied. A replacement writes the new file, links it, and only then removes the one it displaced, so
the record never links to a file that is gone; a step that fails leaves the previous photo in force, or at worst a file
nothing links to. **The sweep that removes a stored file nothing links to reads a contact's link as well as a user
record's**, so a contact's photo is never taken for an unlinked file, and one left behind by a failed step is removed
once it is an hour old. An amendment of the record keeps the photo; the photo changes only through its own routes,
[the contact photo routes](../operations/client-endpoint.md#the-contact-photo-routes), which are the client endpoint's.

A contact read over the client endpoint or the administrative endpoint names its photo by the stored file's identifier,
`photo`, which changes whenever the photo does and carries nothing about the picture. The tools over the book publish
no photo: an agent has nothing to do with a picture somebody chose for a person.

## Where a person's photo is drawn, and who sees it

**A person is drawn by a photo wherever the client draws them**: on a row of the mail list, at the head of an opened
message, on a row of the address book and on the person's own page, in the
organization's structure, on a person a recipient field suggests, and in the people and the participants an AI answer
names. Two photos exist to draw, and
they are found by address in the order [ADR 0044](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0044-an-organizations-member-directory-as-a-projection-of-user-records.md)
sets for naming somebody: the photo the reader gave a person in their own book, and otherwise the portrait a member of
the reader's organization holds in their own record. **The first book holding the address decides, with or without a
photo**, so a person the reader wrote down without one is drawn by their initials even where a colleague of that
address has a portrait — the reader's own record of somebody is never overridden by another. A contact the
organization's book or a mailbox's collected book holds carries no photo and hides no portrait. Anybody else is drawn
by their initials.

**Who sees a photo is who may read the book it comes from.** It takes `mailfathom.contacts.read`, the reader's own book
holds a photo only they gave, and a portrait is served only to another member of the same organization — never across
one, and never for a service account, which the member directory does not hold. The reader's own portrait is not
resolved for their own mail: it is drawn where the client draws the signed-in person. Nothing is fetched from outside to
supply a photo, here as anywhere else.

**A list names each sender's photo in the answer that draws it**, as `senderPhoto` on every row of the mail list, the
search results, and a conversation, and on every message — so drawing a page asks for no photo row by row, and a
replaced photo is shown on the next read because the reference names the stored file. An AI answer names people by
address alone, so the client resolves the ones a block names in one request,
[`POST /api/client/contacts/photos`](../operations/client-endpoint.md#the-person-photo-route), when the block is drawn.

## When two addresses are the same address

Two addresses name the same mailbox when their comparison forms are equal, and the comparison form is the whole address
upper-cased. `Anna@Example.test`, `anna@example.test`, and `ANNA@EXAMPLE.TEST` are one address everywhere in the book:
in a lookup, in the uniqueness rule below, and in what a record is stored as.

RFC 5321 makes the local part case-sensitive and almost no mail provider honours that. A rule that split those three
would store one person as two records for a distinction their own mail server does not make, and every surface over the
book would then disagree with itself about who a message is from. The cost is stated rather than hidden: a server that
genuinely does distinguish them is served one contact where it has two mailboxes.

The written form is kept beside the comparison form, so what a reader is shown is the address as somebody wrote it. Only
the comparison form is matched, grouped, or indexed on. It is the same rule and the same value the mail beside it is
matched by, defined once in the domain rather than per writer.

**One address belongs to one contact, within one book.** Adding an address a different contact in the same book
already holds is refused with an answer naming which contact holds it, so the caller can look at that person rather than
guess. The rule is a unique index in PostgreSQL rather than a check before the write: two callers claiming one address at
once both read that nobody holds it, and only the database closes that window. Losing that race is a conflict the write
retries from a fresh read, and the second caller is then told who holds the address.

It stops at the book's edge deliberately, and there are two edges it stops at. Two users who correspond with the same
person each hold their own record of them, with their own name for them and their own note, because a rule across the
table would make what one user may write down depend on what another already had — and would let one of them find out
that the other corresponds with somebody by being refused. And a user's own record and a record one of their mailboxes
collected may both hold one address: they are two books, the one the user wrote is the one their reads answer with, and
refusing the second would mean a person could not write somebody down because a mailbox they share had already met
them.

Inside one record, two spellings of one address are merged rather than refused. They name the same mailbox of the same
person, and refusing would ask a user to resolve a difference nothing else makes.

## Two origins, and who may write which

A contact is either **asserted** — somebody wrote this person down — or **collected**, an address that appeared in mail
that arrived. The origin is not a flag beside the record: it says which kind of book holds it — a user's or an
organization's for an asserted one, a mailbox's for a collected one — and the database refuses a row whose origin and
holder disagree. A user reads all of their books at once, so searching for somebody never requires knowing which half
they are in. What the difference decides is who may change the record without anybody asking:

- A writer amends the contacts of its own origin and no others, and an amendment writes into the writer's own book
  alone. Collection never touches what a user wrote down, and a user amending a record their mailbox collected is
  refused by that record's origin rather than having a mailbox's record rewritten under everybody else assigned it. The
  refusal names the origin rather than reporting a contact nobody holds, because what the caller has to be told is that
  promotion is the act that makes such a record theirs.
- **Promotion** is the one crossing between the books, and it **copies**: it writes the caller's own record of the
  collected person, under an identity of its own, and leaves the mailbox's where it is for whoever else is assigned that
  account. What the caller gains is a record they may then amend like any other, and which their own reads answer with
  in place of the collected one. It names its writer for the same reason an amendment does, so collection is refused the
  promotion of the record it just created rather than being able to award itself the authority that comes with it.
  Nothing turns an asserted contact back into a collected one, because nothing can unsay that somebody wrote a person
  down. Promoting a contact the caller has already asserted is answered as such rather than written again.
- Origin is recorded when the contact is created and is never changed by an amendment.

Every surface a caller reaches the book through writes as **asserted**, into the caller's own book, because each is
somebody writing a person down: `mfctl` is the user at a terminal, an agent over MCP is acting for them, and the client
is the person themselves. What follows is that none of them amends a collected record — the call is refused by that
record's origin — and what each does instead is promote it. All three reach that act: `mfctl contact promote`, the
`promote_contact` tool, and `POST /api/client/contacts/{contactId}/promotion`, under the same writing grant each
surface already holds. A promotion reachable from only one of them would leave an amendment permanently refused on the
others for every record collection produced.

Erasure is deliberately outside that rule. It is the data-subject path, and somebody asking to be removed from a contact
book is not answered with which half of the book they happen to be in.

## Contacts are the trusted-sender list

**A sender is trusted because somebody wrote them down.** There is no list of trusted senders beside the book: when a
message arrives, the address its `From` displays is looked up in the books the receiving account reaches — its
organization's, and the own book of every user it is assigned to — and a contact holding it there makes the sender
trusted. A mailbox's collected book is never consulted, because collection writes down whoever wrote to the mailbox,
forgers included. The comparison is the address's comparison form, so `Anna@Example.test` and `anna@example.test` are
one sender. What else the message has to prove before a contact counts — an author tied to the address by DMARC, an
aligned DKIM signature, or the author's own verified signature, never SPF alone — and in which order a contact ranks
against the owner's own addresses and an organization's signature is [sender authentication § Whether the author is
one this deployment recognizes](sender-authentication.md#whether-the-author-is-one-this-deployment-recognizes).

**Every address records who wrote it, and only a person or an administrator grants trust.** Each address row carries
the surface its write came through, the user, the credential the write was admitted by, and when:

| Writer | Written through | Grants trust |
|---|---|---|
| `Person` | The client endpoint | Yes |
| `Administrator` | The administrative endpoint and `mfctl` | Yes |
| `Agent` | The MCP tools | No |
| `Unrecorded` | Collection, and every address written before writers were recorded | No |

An agent may write somebody down for a user, and that record is read like any other; what it never does is make that
person's mail trusted, so an instruction an agent followed from a message cannot vouch for the message's own sender. The
MCP tools that write say so in their descriptions. An amendment keeps the writer each kept address already has, unless
the person restates it as below, and gives an address it adds its own writer, whoever amends. So restating a record through MCP does not take away trust a person
granted, and cannot add any — and a person saving the record in the client, which states every address it holds, does
not vouch for an address an agent added only because it was there. A promotion records whoever promoted.

An address an agent wrote, or one recorded as `Unrecorded`, therefore grants nothing until a person or an administrator
writes it down themselves. A person does that by naming the address in the `restatedAddresses` of [the client's
amendment](../operations/client-endpoint.md#the-contact-routes), which records them as its writer although the contact
keeps it — the sender details' *Add to contacts* does exactly that for the address a message displays — or by
recording the contact afresh. Amending a contact while keeping the address without restating it leaves its writer as
it was, and no other surface restates: the MCP tools and the administrative routes never do.

**A trusted verdict names the contact that granted it, and removing that contact takes the trust away.** Erasing a
contact, erasing the user whose book holds it, and deleting the organization whose book holds it each reset every
verdict naming one of its contacts to the not-evaluated verdict, in the same transaction and before the rows go — so an
erasure is never blocked by a verdict and no trusted mark is left naming somebody nobody holds. Each of those acts, every
contact write, and every verdict take one lock per address first, so a verdict reads the books wholly before a write or
wholly after it.

## Collecting contacts from arriving mail

An account can record the people it corresponds with as its mail is synchronized. It is **off unless a user switched
it on, and switched on per account**, because what it produces is derived personal data about people who never dealt
with MailFathom: an instance nobody asked never accumulates one, and a deployment reading a work mailbox and a personal
one decides separately for each. [Configuration §
`ContactCollection`](../operations/configuration-mail.md#contact-collection), which is a block of the account's own
declaration in its user's record, holds the keys and their bounds.

Collection runs inside the synchronization pass that stored the message, after the transaction that stored it committed.
**What it writes goes into the collected book of the account it is synchronizing, and into no other** — it never asks
who that account's users are, and it never reads a user's own book. That is what makes one mailbox's collection one
mailbox's: a correspondent is recorded once however many users are assigned the account, every one of them reads that
one record, and nothing a user wrote down can stop the mailbox from recording somebody it corresponds with.
It owns no worker, no timer, and no queue, and it reaches the mail server for nothing at all: the headers it reads were
already read to store the message, so what one message costs is a bounded number of indexed reads and, rarely, one
insert.

**The folder decides which header is read, and nothing else is ever read.**

| Folder | What it contributes |
| --- | --- |
| The folder mapped as `Sent` | The primary recipients — the `To` header. The user writing to somebody. |
| `Drafts`, `Junk`, `Trash` | Nothing. A draft is unsent, and the other two say the opposite of what a book is for. |
| Every other folder | The author — the `From` header. Somebody writing to the user. |

`Cc` and `Bcc` are the copied recipients of somebody else's thread; `Sender` and `Reply-To` name where a message was
submitted from and where a reply is to go rather than who the correspondent is. None of the four is read.

**The two directions are held to different evidence.** An address that wrote to the user is recorded once it has
written `MinimumMessagesFromSender` messages to that account — two by default, because one message from a stranger is
not correspondence. An address the user wrote to is recorded on first sight, because the user having addressed
somebody is exactly the evidence a count of their messages stands in for. The count is answered from the mail the
account has already stored, on the same indexed sender column a mailbox query uses, and it stops counting at the
threshold rather than counting the whole mailbox. Nothing is written down to answer it, so collection derives no record
of its own beside the contacts it produces.

**A message the user sent to more than 16 primary recipients contributes none of them.** A letter is addressed to the
few people it concerns and an announcement to everybody, and the count tells them apart without reading a word of
either. Past the bound the message contributes nothing rather than its first few recipients, because a truncation would
record whoever the sender happened to write first.

Four things are never collected, and none of them can be switched off:

- **A message a machine sent.** A mailing list stamps `List-Id`, `List-Post`, or `List-Unsubscribe` on what it
  distributes (RFC 2919 and RFC 2369); an automatic responder states `Auto-Submitted` with any keyword but `no`
  (RFC 3834); and `Precedence: bulk`, `list`, or `junk` is the oldest way of saying a message went to many rather than
  to one. Each is a claim the sender made in a header defined for that purpose rather than something inferred, which
  matters most for a mailing list: a list posting carries the real address of the person who wrote it, and no rule about
  mailbox names could tell one from ordinary correspondence.
- **A role mailbox**, by the names RFC 2142 reserves — `postmaster`, `abuse`, `info`, `support`, `sales`, and the rest —
  together with the `no-reply` family and the `-request`, `-bounces`, `-user`, `-admin`, `-subscribe`, and
  `-unsubscribe` list-administration suffixes.
- **The account's own mailboxes**, derived from the user name of every account declared beside it, so a user writing
  from one of their mailboxes to another is not recorded as a correspondent of themselves. The set stops there:
  somebody this deployment also serves, on an account this one is not declared beside, is an ordinary correspondent of
  the account they wrote to.
- **An address the account's own collected book already holds.** That is where a second sighting of a correspondent
  stops, and the record itself is never read: what a collected record says about somebody is what the message that first
  produced it said, and rewriting it on every later message would make the book a log of the last envelope. What a user
  wrote down is in another book and is neither read nor consulted — a mailbox two people share cannot stop collecting
  because one of them happened to write the sender down, and the precedence a read applies is what hides the collected
  record from that one person and from nobody else.

On top of those a user writes their own exclusions per account, each naming either a domain — optionally reaching the
names beneath it — or a pattern over the whole address, where `*` stands for any run of characters and `?` for exactly
one. An entry that names both, or neither, or a pattern whose only characters are the two wildcards and the at-sign —
which takes every address or none of them, `*@*` being the shape that reaches a user — is refused at startup rather than
silently excluding nobody.

**One folder run records at most `MaxContactsPerRun` contacts**, 50 by default. The bound is per folder rather than per
account, exactly as the run's content budget is, so an account whose configuration maps several folders may reach it
once for each of them in a single synchronization cycle. What that paces is the first synchronization of a mailbox
holding years of mail, where every
message is new and the book would otherwise gain thousands of people in one pass before anybody had seen one of them. A
run that reaches the bound stops recording and leaves the rest for the next run, which reads the same senders again and
finds the evidence they need still standing. Zero records nobody while leaving collection on, which is a way to see what
a policy would do without writing anything.

**A collected record is named by what the message offered** — the display name the header wrote, or else the address
itself where the header wrote nothing usable. A sender's spelling of somebody's name is exactly what a collected claim
is: weaker than a name the user wrote down, and replaced by one the moment they promote the record.

What collection reports is one measurement per address considered, tagged with which of six conclusions was reached, so
a book filling too fast, a policy excluding everything, and a run repeatedly stopping at its bound are readable apart
from each other. No address, name, folder, or message identity reaches an instrument; [telemetry §
`mailfathom.contacts.collection.decisions`](../operations/telemetry.md#contact-collection) holds the counter.

**A user who changes their mind takes the whole of it back, one account at a time.** A collected book is one mail
account's, so `mfctl contact delete-collected --account <id>` empties that account's and leaves both what another
account collected and every record anybody entered. It is an act over a mailbox rather than over one person's records:
the book it empties is read by every user assigned that account. It cannot be undone — switching collection on again
rebuilds the book from the mail that arrives afterwards rather than restoring what went. Switching it off is a separate
act in configuration, and one worth making, per account like the switch itself: with it still on, the book fills again
from the next message.

## Amending a contact states the whole record

An amendment carries the record the user wants the contact to have — the name, every address, which one is preferred,
and the note — rather than the difference from the one held. Adding an address, dropping one, choosing a different
default, and correcting a name are then one operation whose result is checked against the invariants above, instead of
four that could each leave a contact without an address or with a default it does not hold.

An address the amended record no longer names is removed, which is also what frees it for another contact in the same
book to claim.

**The phone numbers are the one part an amendment may leave unstated.** An amendment carrying no list keeps the numbers
the contact holds, and one carrying an empty list clears them, so a surface that knows nothing about numbers — an
agent's tool, or `mfctl contact update` and the address commands — corrects a name or an address without erasing them.
A list that is stated replaces the held one whole, in the order given.

An amendment writes into the caller's own book alone. A record one of their mailboxes collected is therefore refused by
its origin, naming the record as collected, and what the caller does instead is promote it — which leaves them a record
of their own to amend and leaves the mailbox's for everybody else assigned the account. A contact outside the books the
caller reads at all is a contact they do not hold, and is answered as such.

Two amendments of one contact are last-writer-wins, which is what stating the whole record means: the later one is the
record, exactly as it would be a second later. What is not left to that is an amendment racing an **erasure** — the row
carries PostgreSQL's own version token, as
[ADR 0001](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md)
requires of every record written in place, so an amendment of a contact somebody erased meanwhile writes nothing and is
answered as a contact the book does not hold rather than putting the person back.

## Reading the book

Three lookups and one listing:

- **By identity**, which is what every other part of the system names a person by.
- **By address**, which answers "who is this from" and is served from the unique index rather than from a scan. The index
  leads with the book, so the lookup seeks into each of the books the reader holds rather than scanning the table. Each
  of those books can answer at most once, which is the uniqueness rule above, and the order between them settles which
  of two answers the reader is given.
- **By the whole name**, which answers "who did they mean" and is what addressing a message to somebody reads. It matches
  the name's comparison form exactly rather than looking for text inside it, so it is served from the listing index the
  book and the name's key lead, and it answers with the one contact the reader's books carry the name in or with how
  many carry it. A namesake in a book they do not read is not one of them, and a collected record a contact of their own
  hides is not one either. More than one is not
  a result to choose from: nothing here ranks people, and [mail delivery §
  Addressing a message by naming a contact](mail-delivery.md#addressing-a-message-by-naming-a-contact) is where refusing
  that is argued. The count is exact and comes from the database, and the addresses of the people a shared name matched
  are never read, so a name a hundred collected contacts happen to share costs one number rather than a hundred records.
  A name resolving to one person answers with that person and with the count that decided it read together, so the answer
  can never be one of two people a namesake written down meanwhile made ambiguous.
- **A page of the books**, bounded and continued by a keyset cursor. The order is the name's comparison form and then
  the identity, across the books the page is of, which makes it total: two people with one name are still served in a
  fixed order, so a walk serves every contact exactly once. A record a book earlier in the reader's order hides is not
  served at all, and the hiding is applied as the page is read, so a page holds the size asked for until the walk runs out. A page carries 50 contacts unless the caller asks for fewer, and never more than 200.
  A listing may be narrowed to one origin, which is the question "what did my instance pick up" and its inverse, and to
  a **search**.

The name lookup and the identity lookup each also answer a **set**, up to a page of the book's worth in one read, because
one message may name many people and a query per person would let its recipients decide what addressing it costs. What
comes back is a match for every name asked about — reporting nobody where nobody carries it — and a contact for each of
the identities the book holds, with nothing standing in for the ones it does not. Both are the same answers the
single-value lookups give, which is why nothing above changes for a caller with one name in hand.

A search is text a contact has to carry somewhere in its name or in one of its addresses, matched on the same comparison
forms everything else here is matched on — so the search text is upper-cased once and compared against the stored name
key and the stored address key, and casing is not a thing a caller has to get right. It is a containment match rather
than a pattern: a wildcard character a caller wrote is text to look for, and there is no syntax to learn. The text is
bounded at 320 characters, the longest address the book can hold, and is refused for the characters a name is refused
for, because a value that renders as nothing would select on something nobody can see.

It is the one read here no index answers, and that is deliberate rather than pending. A book is a few hundred people at
the scale this system is written for, the pages are bounded whatever narrows them, and an index over "text anywhere in a
value" is a different structure from the ones that make the lookups above exact. Where a caller has an address, the
address lookup is the answer and is served from the unique index; a search is for the case where they have a fragment of
a name.

A search never widens what a walk serves: the cursor is the position in the book's own order and is not bound to the
filters, so continuing a walk with a different search or a different origin is defined rather than refused.

The order is taken on a comparison form stored beside the name rather than on the name itself, and the column holding it
is pinned to PostgreSQL's `C` collation, so the order is the ordinal one MailFathom derived the form to produce rather
than whichever collation the database was created with. The index it is served from leads with the book and ends with
the identity, which is what lets each of a reader's books be reached by a seek rather than the table scanned, and
what makes the walk over them terminate.

## What mail already holds about a contact

A contact is a name and a set of addresses, and those addresses are already in the mail this deployment stores — as a
sender, as a recipient, and on the files that arrived. So opening one person answers a second question beside the
record: which exchanges have they been part of, and what have they sent. That is a read over the mail index rather than
anything the book holds, and it is served by
[`GET /api/client/contacts/{contactId}/correspondence`](../operations/client-endpoint.md#the-contact-correlation-route)
on the client endpoint. No MCP tool publishes it.

**Nothing is stored and nothing is derived durably.** Both lists are computed while the request is served, from the
mail index and the [attachment index](attachment-text-extraction.md), so the book grows no second copy of a
correspondence and no row here can fall behind the mailbox it describes. A contact erased takes nothing extra with it,
because there was nothing extra.

**The conversations are the ones naming one of the person's addresses** — the sender, `To`, or `Cc`, matched on the
same comparison forms everything else here is matched on. Each is reported at the most recent message in it that names
them rather than at the conversation's own latest message: an exchange that has run on without somebody is not a recent
exchange with them.

**The documents are the ones the person sent**, which means the message the file arrived on was written from one of
their own addresses. A file somebody else attached to a conversation this person was copied on belongs to that other
person, and reporting it here would tell a reader the contact sent them something they never sent. They are read from
the attachment index rather than from the messages, so a deployment whose attachment reading is off never wrote that
index and this list stays empty on it.

**Both are bounded rather than paged**: at most ten of each, out of the messages received in the last year. The two
bounds below that differ, because the two lists are cut differently. The conversations are the distinct ones among the
most recent two hundred messages naming the person, so somebody who wrote more than that many inside one exchange is
reported at that exchange. The documents are simply the ten most recent files on mail they sent inside the window, with
no scan bound of their own — so a file they sent stays reachable however much later mail has named them since. What an
opened contact draws is a card rather than a mailbox, so the rest of an exchange is read by opening the conversation and
everything a person ever sent by searching for their address.

**Only mail the caller may already read takes part.** The correlation runs under the same scope every other mail read
composes — the accounts the signed-in user is assigned, the folders a mapping admits, junk left out — so a message
withheld from this caller is outside the query rather than removed from its answer, and neither list ever carries an
entry marked withheld. Where a deployment scans for sensitive content, the subjects and the file names are scanned
before the answer leaves, under the point [sensitive-content scanning](sensitive-content-scanning.md) names
`client_contact_correspondence`.

**Where a chat endpoint is declared, that correlation is also read into a few sentences.**
[Where a correspondence with one person stands](contact-relationship.md) is the note an opened contact is headed by —
what the exchange amounts to, one thing worth doing next, and a handful of observations, each citing the conversation
or the document behind it. It is a route of its own and a grant of its own, because it costs a provider call on top of
the correlation, and a deployment that declared no endpoint or declined it draws the contact page described above and
nothing more.

## Erasing and exporting a person

A contact book is the most concentrated personal data this system holds: not mail that arrived about somebody, but an
assembled record about identified third parties. Both data-subject paths therefore exist from the first commit rather
than as a later addition, and both are proven by test rather than described.

**Erasing a contact removes them and everything derived from them.** The schema's own foreign keys are what guarantee no
address and no phone number outlives its person, rather than a second statement somebody remembers to write; the erasure takes those rows
first inside the same transaction so it can answer with what it removed — the contact and how many addresses went with
it — and a user asking for one gets an answer rather than a call that returned without complaint. Erasing somebody the books do not hold is a completed erasure, not
a failure: the state the user asked for is the state the books are in. **A photo goes with its contact**: the stored
file is deleted in the same transaction as the record, and its octets are released from the object store once that
transaction commits, exactly as a message's content is.

**It reaches every book the caller reads except an organization's, not only their own.** That is the one act over the
books that does, and it is what a person asking to be taken out of a contact book is owed: neither asserted nor collected
gates it, and somebody making that request is not answered with which half of which book they happen to be in. So
**erasing a collected contact takes it out for every user assigned that mailbox**, because it was one record rather than
a copy each — which is the honest reading of the request and the cost of the mailbox holding one record for everybody.
The organization's book is the one a member reads and does not reach: its contacts are erased only by an administrator,
through the administrative surface or `mfctl contact delete --organization`, under `mailfathom.admin.erase`, and a
member's erasure of one of them is refused under `51017` and removes nothing — see [§ The organization's
book](#the-organizations-book).

**Erasing a user erases their own book.** It is the cascade rather than an erasure of its own, and it runs in two
hops: the asserted half of `contacts` keys onto the user record and `contact_addresses` keys onto `contacts` through
`(ContactId, BookHolderId)`. So a data-subject request against the user discharges the people they wrote down and every
address beneath them along with the mail, and every photo they gave one of them, which is a stored file of theirs and
goes with their other stored files. What it leaves is what their mailboxes collected, which belongs to those
mailboxes and to whoever is still assigned them; a mailbox nobody is left assigned is erased with them, and its
collected book goes with it.

**Exporting a contact produces everything held about them** as of the instant it was taken: the name, every address,
which is preferred, every phone number with its label in the user's order, the note, the origin, both timestamps, and the photo where one is held — its kind and its octets,
encoded as Base64, so the document carries the picture itself rather than an identifier nobody outside the deployment
can resolve. What a user reads is left to the surface that asks for
it, so no surface can choose which parts of a person to hand back.

Both are commands rather than seams something else is expected to reach: `mfctl contact delete` erases and
`mfctl contact export` writes the document, because a data-subject path nothing invokes is one that will not work on the
day somebody asks for it. The erasure asks before it runs and answers with what went — the identity and how many
addresses — and never with the person.

Erasure is also a tool, `delete_contact`, and a route on the client endpoint, `DELETE /api/client/contacts/{contactId}`;
both answer the same counts. Export is neither: the document is the answer to a request a person made of whoever holds
the book, and the surface that produces it is the one that names the user it is reaching the book of.
An agent or a client that needs what an export holds reads the contact, which is the same record without the framing of
a data-subject reply.

## What the book is held under

Nothing about a contact is held under weaker terms than a message is. It is the same database, the same access, and the
same storage protection as the mail beside it, with no field-level encryption on either — what protects both is the
deployment's own protection of its database. There is no retention window: a contact is held until somebody erases it,
which is what distinguishes an assembled record from mail that arrived and ages.

Nothing here is logged. No log line, metric dimension, trace attribute, or failure message carries a name, an address,
a phone number, or a note; the contact's identifier is what a failure names, and it is the one part of the record that is not personal
data.
