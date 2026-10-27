---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-27
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Verify a sender by what each check proves about the author, trust an authenticated author only where somebody wrote them down as a contact, believe a known provider's server by default, and draw a weaker `Known` level of its own on every account that is not strict

<!-- describes: backend/src/Domain/Emails/Authentication/**, backend/src/Domain/Emails/Signatures/EmailSignature.cs, backend/src/Domain/Contacts/ContactOrigin.cs, backend/src/Infrastructure/Mail/Dkim/**, backend/src/Host/Configuration/Mail/Readers/ConfiguredSenderTrustPolicyReader.cs, frontend/src/Client.App/src/senderMark/** -->

## Context and Problem Statement

Every received message carries three answers about its sender. The first says whether an identity authenticated and
whether the author the message displays authenticated (`SenderAuthentication`). The second says whether the message's
own S/MIME or OpenPGP signature verified, and whether it names that author (`EmailSignature`, issue 1588). The third
says whether this deployment recognizes the author (`SenderTrust`). The client combines them into one mark on every row
(issue 2694, issue 2695). Nothing states in one place what each check proves, about whom, and what it rests on. The
mechanism is spread over issue 1588, issue 2694, issue 2695, issue 2697, issue 2728, and ADR 0042.

The trusted-sender list behind the third answer cannot be used:

- The configured half, `TrustedSenders` on each mail account, is written by an operator in configuration.
- The stored half takes a second list that nothing fills. `ConfiguredSenderTrustPolicyReader` passes it empty, under a
  comment that still points at issue 760.
- Issue 760, which would have made the list editable, was closed as not planned, because nothing in the client asked a
  person to act on a sender. The sender mark brings that moment back. This record is the decision issue 760's closing
  comment asked for.

The rule the list is held against also fails on real mail. It trusts an author whose domain any authenticated identity
aligns with, SPF included. It ignores the domain's own DMARC alignment mode. It believes only a receiving server an
account configured by hand, and most providers make that impossible. Microsoft 365 writes no authserv-id at all.
Yahoo changes it from one message to the next.

The owner settled the direction in issue 2698 before this record. The record states that direction, describes the
whole mechanism as it is and as it changes, and answers the questions issue 2698 left open. Issue 2693 is the parent.
Issues 2778, 2779, 2782, 2776, 2780, 2781, and 2783 implement it.

### The mechanism at the baseline

| Part | Where | What it does today |
|---|---|---|
| Trusted authority | `TrustedAuthenticationAuthority`, `SenderAuthenticationReading` | An account names one authserv-id. Only headers carrying it are read, and of those the topmost. An account naming none believes no header |
| Local verification | `LocalSenderAuthenticationReading`, `DkimLocalSenderVerifier` | Where no trusted header exists, MailFathom verifies up to eight DKIM signatures against keys from DNS. It never reports SPF or DMARC. `MailSynchronization:VerifyDkimLocally` turns it off |
| Author conclusion | `SenderAuthentication.EstablishAuthor` | `Failed` from a server's `dmarc=fail` alone. `Authenticated` from a server's `dmarc=pass`, or from any DKIM or SPF identity equal to the `From` domain, beneath it, or above it. Otherwise `NotEstablished`. It does not record which route held |
| Message signature | `EmailSignature` | `Verified` when content, authority, validity, and revocation passed. `SignerMatchesAuthor` when the certificate or key names the `From` address |
| Organization signature | ADR 0042 | A signature under the receiving account's organization's key proves the author a member who sent the message to that account |
| Trust | `SenderTrustPolicy` | Asks in order: organization member, the owner's own address, the owner's own domain where `TrustOwnAccountDomains` is set, the configured list, the stored list. Every step after the first is held against `AuthenticatedAuthorDomain` |
| When | Extraction | Trust is decided once and stored with `SenderTrustPolicyRevision`. Nothing reads the revision back. `mfctl mailbox rederive` re-judges stored mail |
| Contacts | `backend/src/Domain/Contacts/` | `Asserted` (somebody wrote it down) or `Collected` (collection recorded it). One user's book, or one account's collected book. No trust |
| Client mark | `senderMarkOf` | The first that holds: signature mismatch, trusted, verified by the author, authentication failed, neutral |

## Decision Drivers

- A forged `From` must never read as *trusted*, under any provider and any setting. What it may read as is stated, and
  named so that it is never mistaken for trust.
- A person's contacts must be recognized on the mail they actually receive, or nobody keeps the book.
- Trust must work on the providers people actually use, with nothing configured per domain and usually nothing configured
  per account.
- Each mark must mean exactly what the check behind it proves, so *Trusted*, *Verified*, *Known*, and a warning mean the
  same thing to a reader as to the service.
- A model must never be able to plant trust. An operator must be able to tell who made a sender trusted.
- One list, written where people already write down who they know, rather than a second list beside the contact book.
- No public suffix list, and no SPF or DMARC policy evaluation MailFathom cannot complete after delivery.

## Considered Options

- **Contacts as the one trusted-sender list, held against an author authenticated by DMARC or by aligned DKIM, with a
  verified author signature standing in for both.**
- Keep `TrustedSenders`, and fill the stored half from an editor in the client and on MCP (the shape issue 760 had).
- Contacts as the list, matched on the `From` address with no authentication required.
- Contacts as the list, believing only a receiving server's `dmarc=pass`.

## Decision Outcome

Chosen option: **contacts as the one trusted-sender list, under an authenticated author**. It is the only option that
recognizes correspondents on every provider without making the `From` header evidence. The sections below are the
decision. Each answers one question issue 2698 asked.

### What each mechanism proves

| Mechanism | What it proves | About which identity | What it rests on | Mark it can lead to |
|---|---|---|---|---|
| Trusted authority | Which `Authentication-Results` header is the receiving server's own | None by itself | The server stripping forged copies of its own header, and the account or the known-provider list naming it | None by itself |
| SPF (server only) | The connecting address may send for the envelope sender's domain | The transport, never the author | The server's view of the connection, which no longer exists after delivery | None. It never makes an author trusted or known |
| DKIM (server or local) | A key the signing domain publishes signed these bytes | The signing domain `d=` | DNS control of `<selector>._domainkey.<d>` | Trusted, only where aligned with the `From` domain |
| DMARC `pass` (server only) | An aligned identity passed for the displayed domain under its published policy | The domain of the first `From` mailbox, as `header.from` names it | The server's full evaluation, SPF included | Trusted |
| The `From` address alone (relaxed account) | Nothing. It is what the message claims | The first `From` mailbox | Nothing but a person or an administrator having written the address down | Known, never Trusted |
| DMARC `fail` (server only) | The displayed domain's checks did not hold under its published policy | The displayed domain | The same | Authentication failed. Known only through the `p=none` exception below |
| The domain's DMARC record (read locally) | The domain's alignment mode and its policy | The `From` domain | DNS control of `_dmarc.<domain>` or a parent | Nothing by itself. It decides whether DKIM is aligned and whether the `p=none` exception applies. It never makes a local failure |
| S/MIME, OpenPGP | The content is unchanged and the signer is the identity the trusted certificate or key names | The author's exact address, where `SignerMatchesAuthor` holds | The configured trust anchors and keys of issue 1588 and issue 2697 | Verified, or Trusted where a source knows the address. Signature mismatch on a content failure |
| Organization signature | The author is a member who sent this message to this account | The author's exact address and the recipient | The organization's key (ADR 0042) | Trusted as an organization member |

### What needs DKIM alignment or DMARC, and what does not

| Trust source | Needs an aligned DKIM signature or a server's DMARC `pass` | Why |
|---|---|---|
| Organization signature | Neither | The signature proves the exact address and the recipient, which is more than either proves about a domain |
| A verified author signature, for an address a source below knows | Neither | `Verified` with `SignerMatchesAuthor` proves the exact address. A forward or a list that broke the domain's checks leaves it intact, so it holds even after a server's `dmarc=fail` |
| Organization contact | One of the two, or a verified author signature | A contact is an address somebody wrote down. Only authentication or a signature ties the message to it |
| The owner's own address | The same | It moves to the contact rule. See below |
| The owner's own domain, where the account opted in | The same | It moves to the contact rule. See below |
| Personal contact | The same | As for an organization contact |
| SPF alone | Never sufficient, for any source | It authenticates whoever handed the message over. A shared relay or provider satisfies it for everybody using it |

**The owner's own addresses and domains move to the contact rule.** They stop accepting an author aligned only through
SPF. Keeping two rules would mean a reader has to know which source matched to know what the mark is worth. The case
SPF-only alignment served is a domain that signs nothing, and the `Known` level is where that case now goes.

### What establishes the author, and the record of how

`SenderAuthentication` keeps its three author outcomes and gains a record of the route that established an
`Authenticated` author: **`DmarcPass`**, **`AlignedDkim`**, or **`AlignedSpf`**. `AuthenticatedAuthorDomain` is still
the domain of the first `From` mailbox and never the signing name. Every trust source that needs an authenticated
author accepts `DmarcPass` and `AlignedDkim` only. `AlignedSpf` still reads as authenticated in the sender details, and
the details say SPF alone established it. Where several routes hold, the record names the first of `DmarcPass`,
`AlignedDkim`, `AlignedSpf`.

**DKIM alignment** is the relation the code already draws: the signing name equals the `From` domain, lies beneath it,
or lies above it, at a label boundary. Two siblings under one parent are never aligned. The domain's DMARC record
narrows this and never widens it:

- `adkim=s` aligns only a signing name equal to the `From` domain.
- `adkim=r`, or no `adkim` tag, keeps the relation.
- A domain that publishes no record keeps the relation. The aligned signature is enough there.
- Where the record could not be read, DKIM does not align. The author may still be `AlignedSpf` on the server path, and
  stays `NotEstablished` on the local path.

**A server's DMARC result is held to the domain it names.** The trusted header may carry several `dmarc=` results. Each
names the domain it evaluated in its `header.from` property.

- A `pass` counts only where its `header.from` names the domain of the first `From` mailbox, in the comparison form the
  domain already has. A `pass` naming another domain, or naming none, is read as `NotReported`. Then the DKIM route
  decides, under the domain's record.
- A `fail` counts where its `header.from` names the domain of any `From` mailbox, or names none. A refusal is not
  discarded over a missing property, because discarding it would turn a warning into silence. A `fail` naming a domain
  the `From` field does not carry is read as `NotReported`.
- `none`, `temperror`, and `permerror` are held to `header.from` the same way as a `pass`.
- The first result that counts is used. One header carrying both a counting `pass` and a counting `fail` is read as
  `Fail`.

| Server-reported outcome | Author conclusion | Organization signature, author signature | Contact, own address, own domain | `Known` (relaxed account) |
|---|---|---|---|---|
| `Pass` (about the first `From` domain) | `Authenticated`, `DmarcPass` | Unaffected | Trusted where the address matches | Never needed |
| `Fail` | `Failed` | Unaffected. A verified author signature still makes a known address Trusted | Not trusted by authentication | Only where the domain's policy reads `none` |
| `NoPolicyPublished` | DKIM aligns by the relation, with no lookup. The server already found no record | Unaffected | Trusted where DKIM aligns | Where nothing aligned |
| `PermanentError` | As `NoPolicyPublished`. An unusable record is no record, as RFC 9989 treats it | Unaffected | The same | The same |
| `TemporaryError` | As `NotReported`. MailFathom reads the record itself | Unaffected | Trusted where DKIM aligns under the record read | Where nothing aligned |
| `NotReported` | MailFathom reads the record itself to decide DKIM alignment | Unaffected | The same | The same |

### Reading the domain's DMARC record locally

MailFathom reads a DMARC record to learn two things: the alignment mode, and, for `Known` alone, the policy. It
evaluates no DMARC policy and computes no DMARC result. The record is read on both paths. On the server path, it is
read where the server reported no DMARC result that counts or only a temporary error, and, on a relaxed account, on
every `fail` that counts, whether or not a source knows the sender yet. The policy is stored with the verdict, so a
later contact write can re-judge the message exactly as extraction would have. On the local path, it is read wherever a verified signature could
align.

**The record is found by RFC 9989's DNS tree walk.** That is DMARC's current definition (it replaces RFC 7489) and it
uses no public suffix list:

1. Query `_dmarc.<From domain>`. Discard every TXT record not starting with `v=DMARC1`. Where more than one remains,
   discard all of them.
2. Where exactly one remains, it is the record.
3. Otherwise walk up. A name of eight or more labels is shortened to seven. Then remove one label at a time and query
   each parent, down to and including the single-label name. Apply the same discard rule at each name.
4. The walk stops at the first name holding exactly one valid record, at a record carrying `psd=n` or `psd=y`, or when
   no labels remain. It makes at most eight queries for one domain.

`NXDOMAIN` and an empty answer mean *no record here, keep walking*. A timeout, `SERVFAIL`, or any other error ends the
walk without an answer, and the record counts as *could not be read*.

**Tags read.** `adkim` (default `r`), and for `Known` the policy that applies to the `From` domain:

- `p`, where the record was found at the `From` domain.
- Where it was found above it: `sp` if present, otherwise `p`. `np` as well, where present, because MailFathom does not
  ask whether the `From` domain exists.

The `p=none` exception holds only where every tag that could apply reads `none`. Nothing else in the record is read.
RFC 9989's organizational domain is not used for alignment, because the owner settled that alignment stays the
relation. **What that gives up is stated and accepted.** Two siblings under one organization, such as
`news.example.test` signing for `@shop.example.test`, are aligned under DMARC's relaxed mode and not here. A sender in
that position is recognized through `Known`, a signature, or DMARC `pass` from a trusted server.

**A local evaluation never records a DMARC failure or an author failure, under any policy, `p=reject` included.** A
locally evaluated message is `Authenticated` with `AlignedDkim`, or `NotEstablished`, and `Dmarc` stays `NotReported`.
Three facts make a local failure wrong far more often than right:

- MailFathom sees no SPF after delivery, so a message aligned only through SPF is legitimate and looks exactly like one
  aligned through nothing.
- The provider may have rewritten the delivered copy, with Safe Links, a banner, or a footer, which breaks a signature
  that verified at the server.
- A receiving server that enforces DMARC never delivers a `p=reject` failure, and files a `p=quarantine` one as spam. A
  message in the mailbox that does not align locally therefore almost always passed at the server, through SPF, or was
  altered after delivery.

**What follows is stated plainly.** On an account with no server verdict, MailFathom draws no warning for a forged
domain. The receiving server already enforced the domain's policy before delivery, and MailFathom cannot repeat that
check. The *authentication failed* warning comes only from a server's verdict, through the account's own
`TrustedAuthenticationServiceIdentifier` or the known-provider list below. A *signature mismatch* warning comes from a
signature that does not match its content, on any account. A DKIM signature checked locally against a key that
resolved and rejected still records the identity as failed, as it does today. It says nothing about the author.

**Lookups are bounded, cached, and switched off together.**

- They go through the resolver and the bounded cache the DKIM keys already use, keyed by the queried name. An answer is
  held for its record's time-to-live, an absence for a shorter time, and an error not at all.
- Every query has its own deadline. The queries share the per-message budget the key lookups already spend.
- `MailSynchronization:VerifyDkimLocally` keeps its key and governs every DNS lookup sender authentication makes. With
  it off, no record is read. DKIM then aligns only where the server reported `pass`, `none`, or `permerror`.
- A query names `_dmarc.` and a domain the message displays, or one of its parents. Like a key lookup, it tells that
  domain's nameserver only that somebody received mail claiming to be from it. It happens when the message is stored,
  never when it is read.

### Believing a known provider's receiving server by default

A deployment-wide list in the database maps the IMAP host an account synchronizes from to the authserv-id that
provider's receiving server writes. An account whose `TrustedAuthenticationServiceIdentifier` is unset believes the
server its IMAP host's entry names. An account that sets one keeps its own, always. The server's header then wins over
local verification exactly as a configured authority does, because the server also saw SPF.

**An entry is safe only where all of these hold.**

1. **The receiving server removes or renames every incoming `Authentication-Results` header carrying its own
   authserv-id**, as RFC 8601 requires of it. Otherwise anybody can forge `dmarc=pass` under it.
2. **The authserv-id never changes.** It is the same across messages, senders, and time. A provider that varies it,
   as Yahoo does, cannot have an entry. A provider that writes none, as Microsoft 365 does, cannot either.
3. **Every message reaching the mailbox from outside passes through that server.** A message the account holder adds
   with IMAP `APPEND` does not, and could carry a forged header. That is the holder writing into their own mailbox, and
   it is accepted.

**The verification an entry needs before it is seeded or added** is a test anybody can repeat:

- Send a message from a server outside the provider to a mailbox it hosts. Give it an `Authentication-Results` header
  carrying the provider's authserv-id and claiming `dkim=pass` and `dmarc=pass` for a `From` domain that fails both.
- In the delivered copy, read through the IMAP host the entry names: the forged header must be gone or renamed, and the
  topmost header must be the provider's own, reporting the real results.
- Read the authserv-id of at least three messages from different senders delivered on different days. It must be
  identical.

`mfctl` states those three requirements when an entry is added and asks for confirmation before writing it.

**The seed holds one entry**, `imap.gmail.com` to `mx.google.com`, which covers Gmail and Google Workspace. A provider
joins the seed only through a later migration, after the same verification. An operator adds anything else to their
own deployment.

**Matching** is on the exact IMAP host the account connects to, compared without regard to case and without a trailing
dot. There is no wildcard and no suffix match. The port is not part of it. A mailbox reached through another host, such
as a gateway or a self-hosted proxy, has no entry and is configured by hand.

**Who may change it.** Adding and removing an entry is `mailfathom.admin.configuration.write` held at deployment
scope only, because an entry decides which header every account on that host believes, across every organization.
Reading the list is `mailfathom.admin.read`, at deployment scope as well. An organization-scoped administrator can do
neither.

**Changes are audited.** Each entry records who added it (the user and the credential) and when. A removal is written
to the list's own append-only change record, with the entry, the act, who, and when, because a removed entry leaves
nothing else to read. A change reaches the next extraction on every replica within the bound a grant change already
has, and needs no restart. Like a newly configured authority, it judges new mail only. `mfctl mailbox rederive`
re-reads mail already stored, because the header is part of each message.

### Contacts as the one trusted-sender list

**`TrustedSenders` and the matcher's stored half are removed.** That removes `TrustedSenderOptions`, the configured
entries, their validation, and the empty stored list `ConfiguredSenderTrustPolicyReader` passes under its comment
pointing at issue 760. The reference to issue 760 goes with that code, which settles it.

**Only written-down contacts count.** Those are a person's own `Asserted` contacts and their organization's contacts. A
`Collected` contact never makes a sender trusted or known, because collection writes down whoever wrote to the mailbox,
forgers included.

**Addresses are compared without regard to case**, in the local part and the domain, in the comparison form the
contact book already uses: the whole address upper-cased, with the book's handling of an internationalized domain. The
owner's own addresses are compared the same way. The subject is always the first mailbox of `From`.

**Trust is per address, and no domain-wide trust survives beside `TrustOwnAccountDomains`.** A contact carries
addresses, never a domain. A configured domain entry, with or without `IncludeSubdomains`, has no replacement. The case
it served is covered three other ways: an organization's colleagues are covered by the organization signature, the
owner's own company domain by `TrustOwnAccountDomains`, and anybody else by their addresses. `TrustOwnAccountDomains`
keeps the consequence it states today: on a shared provider's domain it recognizes every user of that provider.

**Whose personal contacts count.** The asserted books of every user the receiving account is assigned to, as they read
when the message is judged. A mailbox two people share is judged by both books. The verdict names the contact that
recognized the author. The client says *your contact* when the reader's own book holds that contact. Otherwise it says
*a contact of another user of this mailbox*, and shows nothing from that record: not its name, its note, or which user
holds it.

**What that discloses is accepted.** A reader of a shared mailbox learns that some other user of it wrote a given
address down. With two users, that says whose book it is. On a relaxed account the mark answers it for any message
showing the address. This is accepted because the verdict is stored once per message, not once per reader, so a
shared mailbox's marks are necessarily shared. The alternative, judging each message by its reader's book alone,
would need a verdict per reader on every message. The users of a shared mailbox already read every message in it and
every mark on them. They still cannot read, list, or name another user's book, which is what `docs/features/contacts.md`
promises.

### The order

Highest first. Each step is asked only where the one above did not recognize the author:

1. **Organization member** (ADR 0042). Needs no authenticated author, and is never `Known`.
2. **Organization contact.**
3. **The owner's own address.**
4. **The owner's own domain**, where the account set `TrustOwnAccountDomains`.
5. **Personal contact.**

Steps 2 to 5 recognize an author established by `DmarcPass` or `AlignedDkim`, or an address a verified author signature
established. A sender matching several steps is reported under the highest one. Within those steps, authentication is
asked first, and the verdict records **which established the author: authentication, or the author's signature**. The
signature is recorded only where authentication did not hold, so the record names what the mark actually rested on.

`SenderTrustSource` loses `ConfiguredTrustedSender` (`2`) and `StoredTrustedSender` (`3`), whose numbers are never
reused. It gains `OrganizationContact` (`6`) and `PersonalContact` (`7`). A verdict reached by a contact stores a
reference to that contact, as an organization member's verdict stores its organization. Unlike an organization's
reference, it is never cleared on its own. Every act that removes a contact resets every verdict referencing it to
the not-evaluated verdict, in that act's own transaction, so no `Trusted` or `Known` mark is left with nothing to name.
There are three such acts: erasing the contact, erasing the user whose book holds it (the book goes by cascade), and
deleting the organization whose book holds it. The reset runs before the cascade, so the data-subject erasure is never
blocked by a verdict and never leaves one dangling. The re-judgment below then restores whatever another source still
grants.

### Organization contacts

**An organization contact is a contact the organization holds**: a third book beside a user's own and an account's
collected one. It always has the `Asserted` origin. It has the same parts and the same limits as a person's contact. An
address belongs to at most one contact within it.

- **Who writes it.** The administrative endpoint and `mfctl contact … --organization <id>`, under
  `mailfathom.admin.operate` to record, amend, and promote into it, and `mailfathom.admin.erase` to erase. Either is held
  at the organization's scope or the deployment's. Reading and exporting it follow the user's book, under
  `mailfathom.admin.audit.read`. **Neither the client endpoint nor MCP writes it.** What every member's mailbox trusts is
  an administrative act, and an agent never performs it.
- **Who reads it.** Every member of the organization reads it beside their own book on every surface that reads the
  book, labelled as the organization's and read-only there. Display reads keep a person's own book first: their name for
  somebody hides the organization's record carrying the same address, then the organization's book, then each account's
  collected one. Trust uses the order above instead.
- **Where it recognizes.** On every receiving account that belongs to the organization, whoever the account is assigned
  to. That is the same organization ADR 0042 verifies against.
- **How it relates to membership.** Membership proves a member's exact address through the organization's own
  signature and needs no authentication. An organization contact names anybody, usually somebody outside, and needs the
  authentication rule or a signature. A member's address may also be held as an organization contact. Membership is
  asked first, so such a sender is reported as a member.
- **Lifecycle.** Deleting the organization deletes its book. A user who leaves stops reading it. Mail arriving on an
  account that leaves the organization is no longer judged against it. Organization contacts follow the book's erasure
  and export paths, and nothing about them is logged.

### Who may make a contact count, and from where

**Every asserted address records who put it there**: the kind of writer, the user, the credential, and when. The kind
follows from the surface:

| Surface | Writer kind | Makes a sender Trusted or Known |
|---|---|---|
| The client endpoint | Person | Yes |
| The administrative endpoint and `mfctl` | Administrator | Yes |
| MCP (`create_contact`, `update_contact`, `promote_contact`) | Agent | **Never, under any grant** |
| Any address recorded before the writer was recorded | Unrecorded | **Never**, until a person or an administrator states it again |

**An address an agent wrote never grants trust.** This is the answer to the question issue 760 was closed over. A model
reads mail an attacker wrote, and a message telling it to add an address to the contacts must not make that address's
next message trusted. No extra permission could separate a legitimate agent write from an injected one. So the agent
keeps `mailfathom.contacts.write` for what contacts are otherwise for, such as addressing mail and naming people. The
tools' descriptions say that what they record does not make anybody trusted. A promotion an agent performs records the
promoted addresses as the agent's. Erasing a contact removes trust, the safe direction, so `delete_contact` stays
unrestricted.

**Addresses already in a book when the writer is first recorded get the `Unrecorded` kind.** No stored contact records
which surface wrote it, and some were written over MCP. Backfilling them as a person's would let every agent-written
address grant trust on the first day, so the migration fails closed: an `Unrecorded` address grants neither `Trusted`
nor `Known`.

An amendment that keeps an address keeps the writer that address has. An address added or moved takes the writer of the
write that added it. **The person makes an agent's or an unrecorded address count by stating it themselves**, either
with the sender details' act below or by adding the address again from the client. An administrator does the same
through `mfctl`.

**The sender details popup gains *Add to contacts*.** It is offered on a received message whose first `From` mailbox is
not one of the owner's own addresses, and is not already held as a trust-granting address (one a person or an
administrator wrote) in the reader's own book or their organization's book. It records that address in the reader's
own book with the person as writer: as a new contact named by the displayed name, or, where a contact of theirs already
holds the address with the `Agent` or `Unrecorded` kind, by recording it again under the person. It goes through the client's existing contact routes and their `mailfathom.contacts.write`. Before it writes, the popup says what follows: mail from that address is drawn as trusted
when it authenticates, and as known whenever it shows that address, unless the account is strict. It is offered on a
warning too, because the person may be writing down the real correspondent a forgery impersonated. Their authenticated
mail is then trusted. What becomes of the forgery the act was pressed on follows the ordinary rules, and the popup's
sentence says so. On a strict account, or where the domain's policy is `quarantine` or `reject`, it stays a warning.
On a relaxed account under `p=none` it becomes `Known`, like any other message showing that address, because a
monitoring-only domain is exactly the case relaxed mode accepts. No exception is made for the message the act was
pressed on: a verdict that depended on which button was pressed where could not be re-judged.
It is never offered on Sent or Drafts, where no mark is drawn.

**Audit** is that record together with the verdict's contact reference. From a trusted message an operator reaches the
contact, the address, and who wrote it, by which surface and credential. No model ever appears there as the writer of
trust, because no agent-written address grants it. Nothing about it reaches a log, a metric, or a failure message: the
contact's and the verdict's identifiers do, and no address or domain does.

### A contact that changes, and mail already stored

**A contact write re-judges the stored mail it affects.** Adding an address, removing one, moving one between
contacts, promoting a collected contact, deleting or erasing a contact, writing an organization contact, erasing a
user, and deleting an organization each enqueue one job per address the act removed or changed, in the durable job
store (ADR 0009), which issue 2783 builds. The job is keyed by the book, the address, and an identifier minted for
that act. ADR 0009 keeps a key unique across terminal rows too, so a key of the book and the address alone would let
the first write's finished job swallow every later one, and an amendment would revoke nothing. Each write therefore
enqueues a job of its own. That is safe because re-judging a message twice from the same state writes the same
verdict. Jobs for one book and address run one at a time, in the order they were enqueued, so a job that read the
state before a later write is always followed by that write's own job, and the last verdict written is judged from
the last state. It re-judges two sets of stored messages:

- every message whose first `From` mailbox is that address, on every account the book reaches now: the accounts
  assigned to the book's user, or every account of the organization;
- every message whose verdict references the contact written, wherever it sits, including on an account the book no
  longer reaches after an unassignment or a move between organizations.

The second set is what makes revocation complete. Amending a contact revokes every verdict it granted, not only those
on the accounts its book reaches today. An act that removed the contact has already reset those verdicts in its own
transaction. The job then re-judges the first set, on the accounts the book reached when the act ran, where the book
itself is gone. No stored mark outlives the contact that explained it.

- A re-judgment recomputes the trust verdict from what the message already stores: the authentication verdict and the
  route that established the author, the signature verification, the organization verdict, and for `Known` the policy
  read at extraction. It applies the sources as they stand when the job runs.
- It resolves no DNS, re-reads no MIME, re-verifies no signature, and writes nothing but the trust verdict.
- It re-runs nothing that already acted on the message's arrival: rules, spam classification, notifications, and
  enrichment keep what they did.
- Issue 2776 stores the first `From` mailbox's comparison form with the verdict where nothing yet holds it, so the job
  selects by an index.

This replaces *changes reach new mail only* for contact writes. A person who adds a sender from a message expects that
message's mark to change. Changes to configuration keep today's rule: the owner's addresses, the owner's domains,
`TrustOwnAccountDomains`, `Strict`, the trusted authority, and the known-provider list judge new mail, and
`mfctl mailbox rederive` re-judges stored mail. So do assignment and membership changes.

**The policy revision names what configuration said, and contacts are looked up.**
`SenderTrustPolicyRevision` keeps digesting the configured statements: own addresses, own domains, `Strict`,
and the receiving organization. Contacts are not digested. They are looked up per message by address, through an
indexed, bounded query. What recognized a sender is named by the verdict's source and contact reference instead.

**Rows already carrying a retired source.** The schema is append-only, so a migration rewrites every stored verdict
whose source is `ConfiguredTrustedSender` or `StoredTrustedSender` to the not-evaluated verdict: `Unknown`, no source,
no revision. The list that granted it no longer exists, and a trusted mark nothing can explain or revoke is the wrong
thing to keep. Nothing ever wrote `StoredTrustedSender`. An operator who re-creates the addresses as contacts brings the
marks back through re-judgment for mail extracted after issue 2778 records the route. Older mail needs
`mfctl mailbox rederive`, because a verdict with no recorded route never establishes an author for a contact.

### Relaxed and strict accounts, and the `Known` level

`SenderTrustLevel` gains **`Known`** beside `Unknown` and `Trusted`. **Every mail account is relaxed by default**, and
a relaxed account produces `Known`. Each account gets a `Strict` option, `false` by default. An account that sets it
to `true` is strict: it never produces `Known`, every failure stays a failure, and every verdict is what it would be
without this section. The option is part of the policy revision. Issue 2781 builds it.

**Why relaxed is the default.** On a strict account, the marks are empty for most real correspondents. Their domains
rarely authenticate in a way MailFathom can see: most providers write no usable authserv-id, many small senders sign
with a name that does not align, and providers rewrite delivered mail. A contact book that marks almost nobody is a
list nobody keeps.

- On a relaxed account, a message no route made `Trusted` is `Known` where its first `From` mailbox matches an
  organization contact, the owner's own address, the owner's own domain (where opted in), or a personal contact. The
  sources are asked in the same order, compared the same way, and include only addresses a person or an administrator
  wrote. The verdict names the source and the contact.
- That covers a message whose author was `NotEstablished`, or established only by `AlignedSpf`.
- **A DMARC `fail` the server reported stays a failure, with one exception that exists only on a relaxed account.**
  Where every policy tag that applies to the `From` domain reads `none`, the `fail` is read as an alignment that did not
  hold, and the message can be `Known`. Under `quarantine` or `reject`, or where the record could not be read, it is
  never `Known`, and the warning stands. The recorded authentication still says DMARC failed.
- The organization signature is never `Known`, on any account.
- **One difference between accounts is accepted.** A local evaluation never fails a message, and a server's verdict
  can. So the same message can be `Known` on an account verifying locally and failed on one believing its server.
- Rules read `Known` as a value of `senderTrust` of its own. A condition testing `Trusted` does not match it. A model
  is never told a `Known` sender is trusted: the `sender-trusted` attribute stays the mark of `Trusted` alone.

Relaxed mode trades a real check for a header anybody can write. On a relaxed account, a forged `From` showing a
contact's address is drawn as `Known`. That is why `Known` is a level of its own that is never drawn or reported as
`Trusted`, why an account that must show only verified marks sets `Strict`, and why the configuration page says so
where the option is described.

### How the client draws it

The marks, the first that holds deciding:

1. **Signature mismatch.**
2. **Trusted.**
3. **Verified by the author.**
4. **Known.**
5. **Authentication failed.**
6. **Neutral.**

`Known` ranks above *authentication failed* because it is reached under a failure only where the domain itself
publishes a monitoring-only policy.

**`Known` must never read as `Trusted`.**

- It has its own symbol and word, *Known*, in the neutral tone rather than the trusted one, with no check mark or
  shield.
- Its details name the source and say that nothing confirmed the domain sent the message, for example: *{address} is
  in your contacts, but nothing confirmed that {domain} sent this message.* Under the `p=none` exception they add that
  the domain's checks failed and that the domain publishes a monitoring-only policy.
- The authentication steps show what did not hold.

A trusted sender's details name the source: organization member, organization contact, the owner's address or domain,
or a contact (theirs or a co-assignee's). They say when the author's own signature, rather than the domain's checks,
established the author. Issue 2780 draws the contact sources, the signature case, and *Add to contacts* in the design
first. Issue 2781 draws `Known`. Both move the state inventory and the parity pairing with them.

### Breaks this records

Each is named in its implementing pull request with the operator's action, for the release changelog.

| Surface | Break | Operator's action |
|---|---|---|
| Configuration | `TrustedSenders` and its `Domain`, `Address`, and `IncludeSubdomains` are removed from every mail account (issue 2776) | Re-create each address entry as a contact in the owner's book (`mfctl contact create`) or as an organization contact. Replace a domain entry with `TrustOwnAccountDomains`, an organization, or addresses. Then run `mfctl mailbox rederive` for mail stored before issue 2778 |
| Behaviour | The owner's own addresses and domains no longer trust an author aligned only through SPF. A server's DMARC `pass` counts only for the domain it names. DKIM alignment honours `adkim=s`. With `VerifyDkimLocally` off, server-reported DKIM no longer aligns without a DMARC result. Every account is relaxed and draws `Known` by default | Set `Strict` to `true` on an account that must show only verified marks |
| Database | Verdicts carrying the two retired sources become not evaluated. New columns record the author route, the contact reference, and the policy read for `Known`. A new column records each asserted address's writer, and every address already stored is backfilled as `Unrecorded`, which grants nothing | Have each person, or an administrator, state again every contact address that should grant trust: the person through *Add to contacts* or the client, the administrator through `mfctl` |
| Client endpoint | `trustSource` loses `ConfiguredTrustedSender` and `StoredTrustedSender`, gains `OrganizationContact` and `PersonalContact`, and `deploymentTrust` gains `Known` (issue 2776, issue 2781) | A client built against the old values updates with the release |
| MCP tools | `deploymentTrust` gains `Known` on every read tool (issue 2781). The contact tools' descriptions state that agent-written addresses grant no trust | An agent prompt branching on `deploymentTrust` handles `Known` |
| Mail rules | `senderTrust` gains `Known` | A rule meant to file `Known` mail with `Trusted` names both |

### Consequences

- Good, because a person makes a correspondent trusted where they read the correspondent's mail, on every provider,
  with nothing configured.
- Good, because *Trusted* means one thing whatever source matched: an authenticated author, or an author's own verified
  signature, that a person or an administrator wrote down.
- Good, because no model can plant trust, and every trusted mark leads back to who wrote the address.
- Good, because Gmail and Google Workspace mailboxes get the server's full verdict, SPF and DMARC failures included,
  with nothing configured.
- Neutral, because the known-provider list is only as good as the verification behind each entry. That is why adding
  one states the test and asks for confirmation.
- Bad, because domain-wide trust outside `TrustOwnAccountDomains` is gone, and an operator who relied on it rewrites it
  as addresses.
- Bad, because sibling subdomains of one organization are not aligned, and a sender signing under a sibling needs
  `Known`, a signature, or a trusted server.
- Bad, because every account is relaxed by default, so by default a forgery showing a contact's address is drawn as
  `Known`.
- Bad, because the same message can read differently on an account believing its server and on one verifying locally.
- Bad, because an account with no server verdict gets no forged-domain warning from MailFathom. It relies on its
  receiving server having enforced the domain's policy before delivery.

## Validation

- Owner review of this record before any implementing issue starts.
- Issues 2778, 2779, 2782, 2776, 2780, and 2781 are corrected against it, issue 2783 is opened from it, before work starts, and each pull request
  names the section it implements.
- Unit tests in each implementing issue cover every row of the tables above: each DMARC outcome against each source, the
  `header.from` rule, `adkim`, the walk's stop conditions and errors, no local failure under any policy, the writer kinds, the order, the signature
  exception, re-judgment, and `Known` on a relaxed and on a strict account.
- `docs/features/sender-authentication.md`, `docs/features/contacts.md`, `docs/operations/configuration-mail.md`, and the
  administrative and client endpoint pages describe the result as each part lands.

## Pros and Cons of the Options

### Contacts as the one list, under an authenticated author

- Good, because the list is the contact book people already keep, written from the client, `mfctl`, or the popup.
- Good, because aligned DKIM, read under the domain's own record, works on mail verified locally, where most providers
  leave it.
- Neutral, because it needs a DNS lookup of `_dmarc` beside each key lookup, bounded and cached the same way.
- Bad, because a domain that signs nothing never makes its senders trusted. `Known` is the answer there.

### Keep `TrustedSenders`, and fill the stored half from an editor

- Good, because the matcher already takes both halves.
- Bad, because it is a second list of people beside the contact book, kept in step by hand.
- Bad, because it keeps domain entries and SPF-only alignment, and answers nothing about an agent writing to it.

### Contacts matched on the `From` address with no authentication

- Good, because every correspondent is recognized, whatever their domain does.
- Bad, because `From` is written by whoever sends the message, so every forgery of a contact is drawn as trusted. It
  survives here only as the `Known` level, under a name that does not say trusted and that a strict account never draws.

### Contacts believing only a server's `dmarc=pass`

- Good, because DMARC is the check designed for exactly this question.
- Bad, because MailFathom sees a usable server header on few providers. Without the known-provider list and local DKIM,
  almost nobody would ever be trusted.

## More Information

- Issue 2698 is this record's issue and issue 2693 the parent. Issue 2778 builds the author route and the record walk.
  Issue 2779 builds organization contacts. Issue 2782 builds the known-provider list. Issue 2776 builds the contact
  sources, writer kinds, and re-judgment. Issue 2780 builds the sender details in the design and the client. Issue 2781
  builds `Known`. Issue 2783 re-judges the stored mail a contact change affects.
- [ADR 0009](0009-durable-job-store-and-execution-identity.md) holds the re-judgment job. [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md)
  is the permission model the writers sit in. [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md)
  is why a mailbox may be assigned to several users. [ADR 0042](0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md)
  is the organization signature, unchanged here.
- RFC 6376 (DKIM), RFC 7208 (SPF), RFC 8601 (`Authentication-Results`), and RFC 9989 (DMARC, its DNS tree walk, and
  `psd`), which replaces RFC 7489.
- Revisit this record if a provider without a stable authserv-id starts writing one, if the owner wants DMARC's
  organizational domain for alignment, or if a client contacts screen arrives and should be the second place a person
  confirms an agent's address.
