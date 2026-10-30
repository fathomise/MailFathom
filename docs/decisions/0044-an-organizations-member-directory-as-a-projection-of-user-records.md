---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-30
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Show every person in an organization to the other members as a read-only contact derived from their user record when it is read, never copied into a contact row, read after a person's own book and before the organization's, and recognized by the trust lookup beside the organization's contacts

<!-- describes: backend/src/Application/Contacts/ContactBookScope.cs, backend/src/Application/Contacts/ContactBookHolder.cs, backend/src/Application/Contacts/IContactDirectory.cs, backend/src/Application/Emails/Trust/ISenderTrustBooks.cs, backend/src/Infrastructure/Persistence/Contacts/** -->

## Context and Problem Statement

A member of an organization cannot see who the other members are. A contact today is a name, one to 32 addresses with
a preferred one, a note, an origin, and two timestamps, filed in one of three books: a user's own, an organization's
(issue 2779), or a mail account's collected one (`ContactBookHolder`). Nothing connects a contact to a user of this
deployment. The user record holds what a directory would show — the display name, the portrait, the time zone, the
organization, and through the mail accounts assigned to the user, the addresses they use — but no member reads
another's.

Issue 2785 delivers the member directory: every member of an organization appears to the other members as an
organization contact, with their title, phone numbers, local time, working hours, manager, presence, status, work
mode, a phone-app marker, and an out-of-office marker. Its children build those parts one at a time. This record
decides the shape they all build on, before any of them starts:

- whether a member's contact is a row kept in step with the user record, or derived from it when it is read;
- where it sits among the books a person reads, and among the sources [ADR 0043](0043-how-a-sender-is-verified-and-trusted-and-contacts-as-the-one-trusted-sender-list.md)
  asks when it judges a sender;
- which addresses are a member's;
- who writes each field the directory shows;
- what the other members see, which is a decision about an employee's personal data;
- who appears in it at all, and that nobody writes it through a contact surface.

The owner settled the first two in issue 2788 before this record: a projection, never a copy, read after the user's
own book and before the organization's contacts and the collected books. This record states that direction with its
reasons and answers the rest. It extends ADR 0043, which is unchanged except where *Trust* below says otherwise.

## Decision Drivers

- One source of truth for a person. A colleague's name, addresses, and portrait already live on their user record and
  change there. A second copy is a synchronization problem the day it is written.
- Leaving and erasure must take the directory entry with them by construction, not by a write somebody has to
  remember. A former colleague who is still listed is a privacy defect, not a stale cache.
- A person's own word about somebody still wins on their own screen, as it does over every other book today.
- The directory is monitoring of employees as well as a phone book. Every field it shows to colleagues has to be one
  they need to reach somebody, and it shows nothing to anybody outside the organization.
- Trust must not get weaker. An address the directory names grants what an address an administrator wrote down
  grants, under the same authentication rule, and no more.
- Every read stays bounded and answered from an index, including the per-message lookup the trust step makes.

## Considered Options

- **A projection: a member's contact is derived from their user record and their assignments whenever a book is read,
  and has no row.**
- A copy: a contact row in the organization's book per member, written when the user is created and kept in step by
  synchronization code.
- A materialized view or a derived table, rebuilt from the user records on a schedule or by trigger.
- No directory in the contact book: a separate *People in my organization* surface beside it.

## Decision Outcome

Chosen option: **a projection**. It is the only option in which a member's departure, erasure, or change of name
needs no write to keep the directory right, and in which nothing can disagree with the user record because nothing
holds a second copy of it. The sections below are the decision.

### A projection, never a copy

**A member contact has no row.** It is read from the member's user record, the row beside it, and the mail accounts
assigned to them, at the moment a book is read, inside the same query that reads the contact rows. Nothing writes it,
so nothing synchronizes it. A change of name, portrait, title, or account reaches the next read on every replica,
because every replica reads the same rows.

**The directory is a fourth kind of book.** `ContactBookHolder` gains the member directory of one organization, keyed
`members:<organization>` under the same rule that keeps the other three key namespaces apart. A member contact is
identified within it by the member's user identifier. The contact API tells the book in every answer, so a client never
confuses a member's identifier with a contact's, and a lookup by identity names the book it means.

**Leaving and erasure need nothing written for the directory.** A member who leaves the organization, whose user
record is erased, or whose type stops being `User` is absent from the next read, because the query no longer finds
them. The re-judgment *Trust* below asks for is the one write those acts gain, and it is about verdicts rather than
about the directory.

**A person may still promote a member contact into their own book.** That writes a copy into the reader's own book,
exactly as promoting a collected contact does, with the reader as writer. The copy is the reader's own, keeps what it
held when it was made, and hides the member contact on the reader's screens by the precedence below. That staleness is
the reader's choice and the reader's alone.

### Who appears in it

- **Members of an organization alone.** A user in no organization has no member contact and reads no directory. Nothing
  that connects a contact to a user exists outside an organization.
- **People alone.** The user gains a type, `User` by default and `ServiceAccount` otherwise. A user whose type is
  `ServiceAccount` has no member contact, is never suggested as a recipient, and has no portrait served to colleagues.
- **The other members.** A reader's directory holds every member of their organization except themselves: their own
  record is their profile, not somebody they correspond with.
- **A member with no address still appears**, with their name and whatever else their record carries, because the
  directory is a list of colleagues before it is a list of addresses.

### Which addresses are a member's

**A member's addresses are the addresses of the mail accounts assigned to them, except a shared mailbox.** A mailbox
assigned to more than one user — the definition issue 2792 exposes — is never one person's address, so it is never one
member's. It can still be written down as an organization contact, under its own name.

- **One address names at most one member.** Where two members each hold an unshared account with the same address, the
  address names neither of them in the directory's address lookup, exactly as a shared mailbox does. Their contacts
  still appear, each without that address.
- **Addresses follow the assignments.** Mail accounts are created, assigned, and unassigned through the administrative
  endpoint and `mfctl` alone, under `mailfathom.admin.configuration.write`, and a mailbox is reached with its own
  credentials. That is why the directory's addresses are administrator-written for the purposes of ADR 0043, and why no
  person or agent can add an address to their own directory entry.
- **The preferred address is the member's choice among them**, stored on their record: the first address when the
  user is created, the member's choice afterwards, and the first remaining one when the account holding it is removed.
- **A functional mailbox assigned to one person is that person's address.** Assignment is the only signal this
  deployment has. An operator who wants `sales@` kept out of somebody's entry assigns it to a second user as well, a
  `ServiceAccount` serving that mailbox being the ordinary way.
- **Addresses are compared** in the contact book's comparison form, which ADR 0043 states, in the directory as in every
  other book.

### Where it sits

**For display, the order is: the reader's own book, the member directory, the organization's contacts, then the
collected books.** `ContactBookScope` reads the member directory after the user's own book and before the
organization's, and every read that applies its precedence — the listing, the identity reads, the name match, the
lookup by address, the recipient suggestions, and the photo resolution of issue 2804 — applies it unchanged.

- **The reader's own book first**, as today, because what a person wrote down about somebody is what they want to see.
- **The directory before the organization's contacts**, because a member's record is the authoritative description of
  that colleague, kept current by the colleague and their administrator. An organization contact naming the same
  address is a second, hand-kept description, usually written before the directory existed, and it would otherwise
  hide the live record from every member.
- **Both before the collected books**, because a mailbox collecting a colleague's address knows less than either.

The organization's book and the directory are read only by a member of that organization. The directory, like the
organization's book, is never written through a user's scope.

### Trust

**The lookup by address that ADR 0043's trust step makes reads the member directory as well as the contacts.** Its
order gains one step, directly after the organization signature:

1. **Organization member**, by the organization's signature (ADR 0042). Unchanged.
2. **Organization directory**: the first `From` mailbox is the address of a member of the receiving account's
   organization. New.
3. **Organization contact.**
4. **The owner's own address.**
5. **The owner's own domain**, where the account set `TrustOwnAccountDomains`.
6. **Personal contact.**

- **It counts exactly as an administrator-written address counts.** It makes a sender `Trusted` where the author was
  established by `DmarcPass` or `AlignedDkim`, or by a verified author signature naming that address, and `Known` on a
  relaxed account where nothing established it — under every rule ADR 0043 states for steps 2 to 5, including the
  `p=none` exception. It never stands in for the organization signature: a member who sends through an account the
  signature does not cover is recognized here, under authentication, and nowhere above.
- **Where it recognizes**: on every receiving account that belongs to the organization, the same reach ADR 0043 gives
  the organization's book. The whole directory is read there, without leaving out the users the receiving account is
  assigned to; a sender matching several steps is reported under the highest, as ADR 0043 already says.
- **The verdict names the member.** `SenderTrustSource` gains `OrganizationDirectory`, numbered after the values ADR
  0043 left in use. A verdict it reached stores a reference to the member's user, as a contact's verdict stores the
  contact. The sender details name the colleague: everybody who reads a mailbox of the organization is a member of it
  (ADR 0014), so nobody outside the organization reads the name this way.
- **The lookup is bounded.** It is one query per batch of verdicts, over the batch's addresses, answered from an index
  on the accounts' comparison form and an index on the user's organization, inside the transaction and under the
  address lock ADR 0043 takes for every book. It returns at most one member per address.
- **Every change to who the directory names for an address re-judges the stored mail it affects**, through the same
  job, keyed and locked the same way as a contact write in ADR 0043. Those acts are: assigning or unassigning an
  account, changing an account's address, an account becoming shared or unshared, moving an account or a user into or
  out of an organization, changing a user's type, and erasing a user or an account. Each takes the address lock for
  every address whose answer it changes and enqueues one job per such address. An act that removes a user resets
  every verdict referencing them to the not-evaluated verdict in its own transaction, before the cascade, as an act
  removing a contact does. This replaces, for this step alone, ADR 0043's sentence that assignment and membership
  changes judge new mail only: no stored `Trusted` or `Known` mark outlives the membership that explained it.

### Who writes each field

The directory shows fields of three kinds, and the kind decides the writer. Where a field lives in the user's record,
[ADR 0037](0037-a-settings-policy-at-the-deployment-and-at-each-organization.md) still decides whether a policy can
default it, force it, or restrict who edits it.

| Field | Where it lives | Written by | Under ADR 0037 |
|---|---|---|---|
| Name | the user's `DisplayName` | the member, under `mailfathom.profile.write`, and an administrator | an identity property, as today |
| Portrait | the user's record | the member | an identity property, as today |
| Addresses | the mail accounts assigned | an administrator, by assigning | not a record property |
| Preferred address | the user's record, new | the member | an identity property |
| Phone numbers | the user's record, new, in the shape of issue 2786 | the member and an administrator | an identity property, so a policy can make them the administrator's alone |
| Time zone | the user's `TimeZone` | the member, and an administrator | an ordinary property, as today |
| Working hours | the user's record, new (issue 2791) | the member, and an administrator | an ordinary property: an organization states a default and may force one |
| Organization | the organization's name | an administrator | not a record property |
| Title | a column on the user's row, new | an administrator alone | governed by no policy |
| Manager | a column on the user's row, new (issue 2790) | an administrator alone | governed by no policy |
| Type | a column on the user's row, new | an administrator alone | governed by no policy |
| Presence, status message, work mode | outside the record (issues 2793, 2794) | the member alone, or derived | governed by no policy |
| Phone-app marker | derived from the member's sessions (issue 2795) | nobody | — |
| Out of office | derived from the member's automatic reply (issue 2797) | the member, by setting the reply | — |

- **Title, manager, and type are columns, written through the administrative endpoint and `mfctl` alone**, under
  `mailfathom.admin.configuration.write` at the organization's scope or the deployment's. They say where a person sits
  in the organization and whether they are a person at all, which is the organization's statement about them, like
  membership itself, and never something a person writes about themselves. They are columns because the directory reads
  them across every member in one query, which is the same reason ADR 0037 makes the endpoint switches columns. A
  policy states nothing about them: a default title or a forced manager could only be wrong for every record but one.
- **Presence, the status message, and the work mode are the member's alone.** An administrator never sets them,
  because a status somebody else set is a statement made in that person's name. Where they are stored, and how
  presence expires, is the presence record issue 2793 opens.

### What the other members see

A member contact shows other members of the same organization the fields of the table above, and nothing else:

- name, the addresses with the preferred one, portrait, organization, title, and phone numbers;
- local time and its offset from the reader's, read from the time zone, and nothing where the member states none;
- working hours in the member's time and the reader's;
- the manager chain upward and the direct reports;
- presence, and *Last seen* for an `Offline` member — never for a `Hidden` one, whom everybody else sees as `Offline`;
- the status message and the work mode while they last;
- whether the member uses the phone app, as yes or no, never the day it rests on, and never for a `Hidden` member;
- that the member is out of office and until when, never the text of their reply.

**What it never shows:** roles and grants, the endpoint switches, the language and every client preference, sessions
and credentials beyond the phone-app marker, the mail accounts as accounts, the shared mailboxes a member reads, any
history of presence, and any field to anybody outside the organization. The administrative surfaces read users as they
do today; this record adds nothing to what an administrator reads.

**MCP reads the directory as a contact book, and only as one.** An agent acting for a member reads member contacts
through the contact tools exactly as it reads the organization's contacts: name, addresses, preferred address,
organization, title, and phone numbers, which is what addressing mail and naming people needs. Presence, *Last seen*,
the status message, the work mode, working hours, the manager chain, the phone-app marker, and out of office reach the
client endpoint alone. They are monitoring of an employee kept for a colleague deciding when and how to reach somebody,
no agent task needs them today, and a model reading mail an attacker wrote is the last reader to hand someone's
whereabouts to. Revisit when a tool needs one, field by field.

**Nothing about a member is logged.** No log line, metric dimension, or failure message carries a member's name,
address, title, phone number, presence, status, or any other field of their entry. Identifiers may appear, as for
contacts.

**Why this is proportionate.** Every field shown is one a colleague needs to reach the person or to know when not to
expect them; every field is shown inside the organization alone; and every field the member writes, they can see and
change. `docs/features/contacts.md` states what the directory shows and to whom, in the change that builds each part.

### Read-only, on every surface

**The client endpoint refuses every write to a member contact and to an organization contact**: creating one in either
book, amending, adding or removing an address, and erasing. Each is refused with a stable error code that says the
contact is read-only and whose it is, rather than answered as a contact the reader does not hold, so the client can
explain the refusal. The client draws no editing control for either. MCP's contact writes reach the reader's own book
alone, as today, so a member contact's identity is not one they can address. Promotion into the reader's own book is
the one act both books admit, and it writes a copy.

A member corrects their own entry by editing their own profile, and an administrator corrects it by editing the user.
That is the whole of how the directory is written.

### Breaks this records

Each is named in its implementing pull request with the operator's action, for the release changelog.

| Surface | Break | Operator's action |
|---|---|---|
| Client endpoint | Contact answers name their book, and a member's contact carries the member's user identifier. A write to an organization contact is refused as read-only, where it was answered as a contact the reader does not hold. `trustSource` gains `OrganizationDirectory` (issue 2789) | A client built against the old answers updates with the release |
| MCP tools | The contact read tools return member contacts beside the reader's own and the organization's | An agent prompt that treats every contact as one the person wrote down tells the books apart |
| Database | The user's row gains the type, the title, and the manager. Verdicts gain a reference to a member and the new source. Nothing is backfilled but the type, `User` for every existing user | Mark every technical user `ServiceAccount` through `mfctl`, so that it leaves the directory |
| Behaviour | Assignment, membership, type, and account changes re-judge the stored mail the directory step affects | None |

### Consequences

- Good, because nothing can disagree with the user record: there is one copy of a person.
- Good, because leaving the organization, erasure, and becoming a service account remove a member from every
  colleague's directory on the next read, with no cleanup job and nothing to forget.
- Good, because a colleague who mails from an account outside the organization signature is recognized under
  authentication with nothing written down for them.
- Good, because every field shown has one writer, stated, and a member can see everything their colleagues see about
  them.
- Neutral, because every read of the contact book now also reads the user, assignment, and account rows of one
  organization. It is bounded by the page and answered from indexes, and an organization's member count is small
  beside its collected books.
- Neutral, because a copy a reader promoted goes stale. That is the reader's own book and their choice.
- Bad, because assignment, membership, type, and account changes now enqueue re-judgment jobs, where ADR 0043 had them
  judge new mail only.
- Bad, because a functional mailbox assigned to one person reads as that person's address until an operator assigns it
  to a second user.
- Bad, because an address two members both hold unshared names neither of them, which an operator resolves by sharing
  the mailbox or removing the duplicate.

## Validation

- Owner review of this record before issue 2789 or any other child of issue 2785 that reads the directory starts.
- Issues 2789, 2790, 2791, 2793, 2794, 2795, 2797, and 2804 are checked against it before work starts, and each pull
  request names the section it implements.
- Unit tests in issue 2789 cover the projection's membership rules — organization, type, the reader left out — the
  address rules including the shared mailbox and the duplicate, the preferred address, the precedence, the refusals,
  and departure with nothing written. The trust tests cover the new step against every row ADR 0043's tables state for
  steps 2 to 5, and the re-judgment each act above enqueues.
- `docs/features/contacts.md`, `docs/features/sender-authentication.md`, `docs/operations/client-endpoint.md`, and
  `docs/operations/admin-endpoint.md` describe the result as each part lands.

## Pros and Cons of the Options

### A projection, derived when read

- Good, because there is nothing to synchronize and nothing to clean up.
- Good, because what a colleague changes on their own record is what everybody sees on the next read.
- Neutral, because the contact reads become a union of contact rows and a query over users, which every read path has
  to carry once.
- Bad, because a member contact has no row of its own to hang anything on, so a field that is the reader's about a
  colleague — a note, a nickname — has to be a promoted copy in the reader's own book.

### A contact row per member, kept in step

- Good, because every contact read stays a read of one table.
- Bad, because every write to a user, an assignment, an account, or a membership has to write the copy in the same
  transaction, and every path that forgets one leaves a second truth.
- Bad, because leaving and erasure become writes somebody has to remember, and a missed one leaves a former colleague
  listed, which is a disclosure rather than a stale value.

### A materialized view or derived table

- Good, because reads stay one table and the rebuild is the database's job.
- Bad, because a refresh on a schedule is a window in which a departed member is still shown, and a refresh by trigger
  is the synchronization code of a copy written in SQL instead of C#.
- Bad, because it reintroduces a second store of the same personal data with its own retention.

### A separate surface beside the contact book

- Good, because no contact read changes.
- Bad, because a colleague's address would resolve to nobody in the composer, the avatar, the sender mark, and every
  other place that asks the contact book who somebody is, unless each of them learns a second lookup.
- Bad, because the precedence between a person's own word and the directory would have to be stated a second time.

### A per-user switch hiding somebody from the directory, rather than a type

Considered for keeping technical accounts out. Rejected because the question is whether a user is a person, which a
type answers once for every surface — the directory, recipient suggestions, the automatic reply's audience, and the
portrait — while a switch answers it for the directory alone and leaves the rest to ask again.

## More Information

- Issue 2788 is this record's issue and issue 2785 the parent. Issue 2789 builds the directory, the type, the title,
  the phone numbers, the preferred address, the refusals, and the trust step. Issue 2790 builds the manager, issue 2791
  working hours, issue 2793 presence with a record of its own, issue 2794 the status message and work mode, issue 2795
  the phone-app marker, issue 2797 out of office, issue 2792 the shared flag, and issue 2804 the photos. Issue 2809 draws
  every screen.
- [ADR 0043](0043-how-a-sender-is-verified-and-trusted-and-contacts-as-the-one-trusted-sender-list.md) is the trust
  model this record adds a step to, and the organization's book it reads beside.
  [ADR 0042](0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md) is the organization
  signature, unchanged. [ADR 0037](0037-a-settings-policy-at-the-deployment-and-at-each-organization.md) is the policy
  every field in the user's record is read through. [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md)
  is why one mailbox may be assigned to several users. [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md)
  is the permission model the writers sit in. [ADR 0009](0009-durable-job-store-and-execution-identity.md) holds the
  re-judgment job.
- Revisit this record if a field is wanted that is the reader's about a colleague rather than a copy, if an agent task
  needs a member's availability, or if an organization grows large enough that reading its members with every page of
  the contact book shows up in measurements.
