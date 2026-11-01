---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-11-01
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep the ciphertext the server delivered, open a message in flight wherever a key addressed to it is held, let the deployment's ring make it ordinary mail for the accounts the key names and a device key make it the client's alone, and encrypt only to keys configuration or a verified signature supplied

<!-- describes: backend/src/Infrastructure/Mail/Mime/**, backend/src/Application/Emails/Extraction/**, backend/src/Application/EmailContent/Rendering/**, backend/src/Application/Emails/GetEmailContent/**, backend/src/Application/Mail/Delivery/**, backend/src/Infrastructure/Persistence/Emails/StoredEmailSignerKeyWriter.cs, backend/src/Infrastructure/Persistence/Entities/StoredEmailSignerKeyEntity.cs, frontend/src/Client.Backend/src/** -->

## Context and Problem Statement

What MailFathom does with an encrypted message today is notice it. `MimeContentClassification.BodyIsEncrypted`
recognises a body that arrived inside a cryptographic envelope, `ExtractedEmailText.EncryptedBody` stops extraction,
`StoredEmailEntity.IsEncrypted` marks the message, and `get_email_content` answers
`EmailBodyAvailability.EncryptedNotReadableLocally`. MailFathom holds no message key, so such a message is permanently
unreadable to its own owner, and nothing MailFathom sends can be encrypted.

Issue 75 closes that in both directions and is written against one table: where the key is decides what each reader
sees. The key can be in the deployment's configuration, on the person's own device, or nowhere. Twelve children build
against that table, and each would otherwise answer for itself what a model may see, what a search index may hold, what
the client may send back, and what removing a key undoes. Issue 1587 is this record, and every other child waits on it.

Two choices were settled before this record and it writes down their reasoning rather than reopening them: the content
store always holds the ciphertext, and outbound recipient keys come from configuration and from verified incoming
signatures with no network discovery. The questions it answers are:

- what the storage invariant is, and what it does and does not protect;
- what each of the three key locations gives which reader;
- what MCP, search, embeddings, and the models reach;
- what the opt-in is, and whose mail a key in the deployment's configuration may open;
- what removing a key undoes;
- whether headers protected inside an envelope reach the stored metadata;
- how the capability meets access, export, and erasure;
- what a device-held key costs the client, and which of [ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md)'s
  guarantees the device reproduces;
- where outbound key material comes from, and what happens when some is missing.

## Decision Drivers

- **A database copy is the threat the stored bytes answer, and the only one.** A dump, a replica, a restored volume, or
  an object store bucket ([ADR 0017](0017-object-storage-content-backend-consistency-and-object-identity.md)) is how mail
  leaks from a deployment at rest. Anything the running service can read, someone holding the running service can read,
  and no scheme that decrypts unattended changes that.
- **A key in configuration is an operator's decision about somebody's correspondence.** MailFathom serves several users
  from one deployment ([ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md)), and a key that opens
  whatever is addressed to it opens it in every mailbox where it arrives. Whose mail a key reads has to be stated where
  the key is configured, not inferred from where a message happens to sit.
- **One consent, written once.** The act of placing private key material in front of the service is already a decision
  to let it read. A second switch beside it is a second spelling of the same decision, and two spellings disagree.
- **A reader that is allowed to see a message should not notice how it arrived.** Once the deployment can read a message,
  a list showing a placeholder subject, a search that cannot find it, or an answer that cannot cite it is a defect, not
  a protection.
- **A reader that is not allowed to see it must never see it.** A device key exists for somebody who will not put their
  key on a server. Anything the device decrypts that reaches the service in the clear, a log, telemetry, an index, or a
  model defeats the one reason that row exists.
- **Two implementations of one judgement will disagree** ([ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md)).
  The device row forces a second reduction of mail into a document tree. It is accepted only where the disagreement is
  proven absent rather than hoped absent.
- **Encryption that silently degrades is worse than none.** A sender who asked for an encrypted message and got a clear
  one believes something false about what they sent.
- **Every replica must answer the same.** Several replicas read one database
  ([ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md)), so whether a message opens cannot
  depend on which replica read it or on what that replica read before.

## Considered Options

**A — what the content store holds for an encrypted message:**

- **A1.** The bytes the server delivered, always; decryption happens in the memory of each read that needs it.
- **A2.** The decrypted message, written back once a key opens it.
- **A3.** Both, the decrypted copy sealed under the data-encryption ring of [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md).

**B — what the derived rows hold for a message the deployment's ring opens:**

- **B1.** Exactly what they hold for cleartext mail, written in the clear.
- **B2.** Nothing body-derived: the message is readable in flight and absent from every index.
- **B3.** Derived rows sealed under the data-encryption ring.

**C — whose mail a key in the deployment's ring may open:**

- **C1.** Any message addressed to it, in any mailbox the deployment holds.
- **C2.** Messages addressed to it in the mail accounts the ring entry names.
- **C3.** C1 or C2 behind a per-account switch that turns reading encrypted mail on.

**D — what removing a key from the ring does to rows already derived under it:**

- **D1.** Nothing; future reads stop, derived rows stay, and the operator is told so where the ring is documented.
- **D2.** Derived rows built under the removed key are deleted by a pass that finds them.

**E — where headers protected inside an envelope go:**

- **E1.** Only ever returned in flight, beside the body.
- **E2.** Into the metadata columns, all of them, for a message the ring opens.
- **E3.** The protected `Subject` into the metadata columns for a message the ring opens; every addressing and identity
  header stays the outer one, and the rest are returned in flight.

**F — what a message only a device key opens costs the client:**

- **F1.** The client reduces the message itself into the same `MailDocument` the service composes, under the same bounds,
  proven against one adversarial corpus both stacks read.
- **F2.** The client draws the decrypted message's own HTML in a sandboxed frame.
- **F3.** The client draws the decrypted plain text alone.

**G — where outbound recipient keys come from:**

- **G1.** Configuration, and the key or certificate carried by an incoming signature that verified.
- **G2.** G1 plus network discovery: WKD, DANE, key servers, LDAP.
- **G3.** G1 plus any key or certificate any incoming message carried.

## Decision Outcome

Chosen options: **A1**, **B1**, **C2**, **D1**, **E3**, **F1**, and **G1**.

### The stored bytes are the bytes that arrived

`EmailMessageContentEntity.RawMime` — or the object it points at, whichever backend
[ADR 0017](0017-object-storage-content-backend-consistency-and-object-identity.md) wrote the payload to — holds the
byte-exact message the server delivered, in every case and whatever key anybody holds. **Nothing ever writes a
decrypted RFC 822 message back to it**, nor to any other table, object, file, cache, or archive. Decryption happens in
the memory of the read that needs it: a body read, an extraction, an attachment download, a re-derivation. Plaintext is
not cached between reads, on the same replica or across them, so every read decrypts again and two replicas reading one
message answer alike. `Sha256Hash` keeps describing the stored bytes, so the integrity checks ADR 0017 relies on are
untouched.

**What that protects is somebody holding the raw content alone**: the raw-MIME table, a bucket, a mailbox export (whose
messages are the stored bytes exactly), and the client's source route (which serves them). Each of those carries the
ciphertext, as the sender sealed it.

**What it does not protect is somebody holding the database.** For a message the ring opens, the derived rows are
written in the clear (below), so a copy of the whole database is a copy of that correspondence's readable text. The
invariant is a promise about one table and one store, and nothing on any page may describe it as a promise about the
deployment. The derived rows inherit the source message's classification, retention, access, export, and erasure
obligations whole, which they already do for cleartext mail.

### What is opened, and how far it may expand

**Only the message's own envelope is opened.** That is an encrypted part that is the message's root, or the sole content
of a `multipart/signed` that is the root. What comes out of it is reduced on its own, as the message, and is never
joined with clear parts beside the envelope. An encrypted part sitting next to clear content is an attachment. It is not
decrypted into the body, so captured ciphertext wrapped between clear siblings cannot be turned into an exfiltration
gadget (EFAIL's direct exfiltration). The same rule holds on the device.

**A ciphertext that could have been altered is not trusted to stay inert.** OpenPGP data is opened only under integrity
protection — a modification detection code or AEAD — and only when that check passes; otherwise the message takes the
unkeyed path. S/MIME's `EnvelopedData` carries no integrity protection, and most S/MIME mail in use is sent that way, so
it is opened anyway. But remote pictures are never offered for content that came out of an envelope without integrity
protection. A ciphertext someone malleated into a picture address therefore stays an address nobody fetches.

**Decryption is bounded before it expands, as it streams.**

- The plaintext is held to `MailSynchronization:MaxRawMimeBytes`, the ceiling the stored message was held to. Decompression
  inside an OpenPGP message counts against that same ceiling.
- An envelope inside an envelope is opened one level deep and no further.
- The decrypted message passes through the MIME structure limits the outer message already passed, because its structure
  is something the outer pass never saw.

A message that reaches a limit is not readable here. The read publishes that as a state of its own, distinct from *no
key is held*, so an operator can tell a ceiling from a missing key. The device builder applies the same three bounds.

### Where the key is decides what each reader sees

| Where the key is | What the service reads | What the client reads | What MCP, search, embeddings, and every model reach |
|---|---|---|---|
| The deployment's ring, in an entry naming the message's account | The whole message, in flight | Everything the service read | Everything, exactly as for cleartext mail |
| The person's own device only | Nothing | The whole message, decrypted and reduced on the device | The envelope and the state, never a body |
| Nowhere | Nothing | The envelope and the state | The envelope and the state, never a body |

**A message the ring opens is an ordinary message on every surface.** It is extracted, chunked, indexed, embedded,
searched, rendered, summarised, answered from, classified, and scanned for sensitive content exactly like cleartext
mail, and its attachments are read and downloaded the same way, decrypted per download. Nothing tells a reader it is
different except two things. One is the marker saying it arrived protected: `IsEncrypted` keeps describing what arrived
rather than what is readable, and the availability a read publishes is what says whether a body could be read. The
other is the rule below that its content leaves MailFathom only sealed. A signature inside the envelope is verified once
the envelope is open, exactly as an outer one is.

**That includes every model the deployment calls.** Ring-opened text goes wherever the deployment already sends
cleartext mail: the embedding provider, the chat provider, the spam stage, image description. An operator who places a
key in the ring and points the deployment at a hosted provider has decided that provider may read that correspondence,
and the operator-facing page states it in those words.

**A message only a device key opens is readable in the client alone.** The service serves it as the bytes it arrived as
to the person signed in, and the device decrypts and reduces it in memory. Nothing a device key opened is sent to the
service in the clear, written to device storage, indexed, chunked, embedded, logged, or reported to telemetry —
[ADR 0028](0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md)'s rule that the device
keeps no mail holds for plaintext as it does for everything else. A reply or a draft that quotes such a message is
sealed on the device before it leaves (below), so what reaches the service is ciphertext. No model, no search, and no answer reaches such a message's body, and the surfaces that offer one say why rather than
offering an empty result.

**A reader whose reads are redacted does not get the device row.** Where a sensitive-content scanner covers any account
a reader is assigned, the source route already refuses to serve raw source, because a raw source cannot be redacted. The
device path rests on serving exactly those bytes, so whichever route issue 1594 serves them through refuses that reader
on the same terms, and a message only a device key opens is shown to them as a message no key opens. That is the right outcome rather than a gap: a posture that redacts every read of a
person's mail cannot be kept by a device that reads it unredacted.

**A message no key opens shows its envelope and nothing else**: its headers, its recipients, its attachment structure as
far as the outer MIME shows it, and a plain explanation of what would change that. It is never an empty body.

### Content decrypted from a protected message leaves only sealed

*An ordinary message on every surface* stops at one place, and it is the only visible difference from cleartext mail
this record allows beyond the protected marker. **Content decrypted from a protected message never leaves MailFathom in
the clear, whichever row opened it.** The sender chose encryption, and a quotation, a forward, or an attached original
that dropped it would be the silent degradation the drivers refuse. So:

- **An answer, a forward, or a draft that carries such content is sent encrypted, or it does not carry it.** A recipient
  with no key makes that send refuse, exactly as an encrypted send refuses (below), and the person may remove the
  quotation instead. This holds for an answer the service composes from a ring-opened message, for one composed in
  the client, and for one an agent drafted. Drafting and sending stay separate acts
  ([ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md)): a person who sends a quotation back to whoever sent
  the ciphertext has chosen to.
- **Nothing automatic quotes or forwards it.** An automatic reply, a rule, or any other pass that sends on nobody's
  explicit act never carries decrypted content. That is what keeps the ring from becoming a reply oracle, in which
  someone mails captured ciphertext into an account and reads what comes back.
- **A draft that carries such content, or that is to be sent encrypted, is stored and filed only as ciphertext sealed
  to the author's own key.** A draft here is a row, a stored message, and a copy in the author's drafts folder on their
  mail server. The mail server never saw the plaintext of what arrived, and a draft filed in the clear would hand it
  over. Where the deployment's ring holds a key for the sending account, the service seals the draft. Where the key is
  on the device, the device seals it before the drafts routes see it. Reopening a draft, including the outbox handing a
  refused send back to its draft, decrypts it in flight with that same key. A ring-path draft for an account the ring
  holds no key for cannot be sealed. It is refused rather than saved in the clear, so sending encrypted through the
  service needs a ring key for the sending account.

### Holding a key is the whole opt-in, and the key says whose mail it opens

There is no per-account switch that turns reading encrypted mail on (C3 is refused). Placing private key material in the
ring is the operator's explicit decision to let MailFathom read the correspondence it opens, and holding a key on a
device is the person's decision to read it there.

**Each ring entry names the mail accounts it opens mail for**, by the account's email address, which is unique across
the deployment and is the identity an operator already writes. Within those accounts matching stays automatic, as issue
75 requires: the recipient identifiers in the message's own envelope — the key ID or fingerprint in each OpenPGP
public-key-encrypted session key packet, the issuer-and-serial or subject key identifier in each CMS `RecipientInfo` —
select the entry, and a message no entry for its account is addressed to takes the unkeyed path without a decryption
attempt. An entry naming no account opens nothing, and startup reports it.

C1 is refused because it makes the ring a decryption oracle between the users of one deployment. Ciphertext addressed to
one person's key decrypts wherever it arrives, so anybody holding an account on the deployment could mail a captured
message to themselves and read it. The account list is not a second consent: it is the scope of the one consent, stated
where the key is, and a key that serves one account names one address. This adds one field to what issue 1589's ring
entry carries.

### Removing a key stops future reads and retracts nothing

Removing an entry from the ring stops every read that would have needed it from then on: a body read, a download, and
the next extraction of the message all take the unkeyed path. **It does not retract a chunk, a search document, a
vector, an attachment reading, or anything else already derived while the key was held** (D2 is refused). Those rows
are readable text that existed while the deployment was allowed to read it, and deleting them would be an erasure
presented as a configuration change: incomplete wherever the text has already left — a model's answer, an export, a
person's own reading — and invisible as an act of erasure to the accountability this project keeps for erasure.

So the operator is told rather than surprised. The page that documents the ring states, where the ring is configured,
that removing a key is not an erasure, that what was derived under it stays readable and searchable until the messages
are erased, and that erasing the messages is the way to retract it — the cascade from the stored email reaches every
derived row. Issue 1591 writes that page's paragraph and builds the reverse case: a key *added* to the ring makes the
existing extraction backfill re-derive the stored messages it now opens, without a re-synchronization.

### Protected headers: the subject becomes the stored subject, and nothing that names a person does

Header protection (RFC 9788) and the older practice of an inner `message/rfc822` both put the real headers inside the
envelope and leave a placeholder outside — a subject of `[...]` most often. For a message the ring opens, E3 decides:

- **The inner `Subject` is written to the stored subject**, by the same extraction that writes the derived rows, and
  re-derived with them, so adding or removing a key moves it the way it moves everything else. A list, a search, and a
  thread then read what the sender wrote, which is what *an ordinary message on every surface* means for the field a
  person reads first.
- **Every addressing and identity header keeps its outer value**: `From`, `Sender`, `Reply-To`, `To`, `Cc`, `Date`,
  `Message-ID`, `In-Reply-To`, and `References`. Sender authentication
  ([ADR 0043](0043-how-a-sender-is-verified-and-trusted-and-contacts-as-the-one-trusted-sender-list.md)) judged the
  outer `From`, threading and delivery read the outer identifiers, and adopting an inner value that disagrees would let
  whoever composed the envelope's contents choose the sender MailFathom shows. Where an inner value disagrees with the
  outer one, the read reports the disagreement rather than adopting either silently.
- **Everything else protected inside the envelope is returned in flight**, beside the body, to a reader allowed to see
  the body, and is stored nowhere.

For a message only a device key opens, every protected header — the subject included — exists on the device alone and
is never sent back. For a message no key opens, the outer headers are all there is.

### Access, export, and erasure

- **Access.** A readable form of a message exists only while a key is held. While the ring holds it, every read surface
  serves it, and the derived rows — which are personal data in the clear — are part of what an access request reaches,
  exactly as for cleartext mail. For a device-held key, the deployment holds the ciphertext and the envelope and nothing
  more: it never processed the plaintext and cannot produce it, and the readable form exists on that person's device,
  under their control, for as long as the message is open.
- **Export.** The mailbox export carries the stored bytes exactly, so an encrypted message is exported as the ciphertext
  it arrived as. That is what makes the archive importable anywhere and keeps it faithful, and it means the export
  writes no plaintext copy at rest for the time an archive is kept. A person who wants a readable copy reads it with the
  key they or their operator hold; no decrypted export is offered.
- **Erasure.** Erasing a message erases its stored bytes and, through the existing cascade from the stored email, every
  derived row, whatever key opened it and whether or not a key is still held. Erasure never needs a key. Removing a key
  is not erasure (above).

### What a device-held key costs the client

`Client.Backend` holds no MIME parser and no cryptography today. The device row gives it both: an OpenPGP and CMS
implementation to decrypt with, and a MIME parser to read what comes out. Each is a new client dependency that issue
1596 brings with its licence review under [ADR 0016](0016-third-party-licence-obligations-per-artifact.md); the key
itself is kept where issue 1595 decides per head, in the store
[ADR 0023](0023-where-the-client-keeps-the-credential-it-signs-in-with.md) and
[ADR 0027](0027-an-android-head-built-every-night-and-supported-by-nothing.md) already chose for the session credential.

**The device builds the closed document tree itself (F1).** A message only the device opens has no service-composed
`MailDocument` and never will, so the reduction is built a second time in `Client.Backend`, emitting the same
`MailDocument` the service composes, under the same `MailDocumentBounds`, and handed to the pane through **the same
validating parser** a served document goes through. That last point is what keeps ADR 0024's trust boundary where it
is: the pane does not trust the device's builder any more than it trusts the service.

Against the five properties ADR 0024 lists:

| ADR 0024 property | How the device-side builder reproduces it |
|---|---|
| No script, embedded object, form, or event handler executes | Unchanged: the builder emits the same closed contract, which has nowhere to express one, and the pane draws it through the same components |
| The client never turns a message value back into markup | Unchanged and still enforced by the lint rule: the builder parses HTML with a parser that builds no document — never `DOMParser`, `innerHTML`, or a `srcdoc` — and the rule gains no exception |
| Message style cannot escape the pane | Unchanged: the builder emits the same opaque colour, alignment, and emphasis values and nothing else |
| No remote resource is fetched before a person asks | Reproduced: the builder removes every remote address while it builds and counts the removals as the service does; asking for remote pictures rebuilds that one message with addresses kept, per message, remembered nowhere |
| A link cannot navigate the application | Reproduced: the builder applies the same `http`, `https`, `mailto` allow-list, computes the same `MailLinkDeception` verdict, finds the same addresses written as words, and the client opens links through the same shell operation |

Two things the service offers are not offered on the device: the full-markup surface, which rests on a service pass that
removes every executable construct and is not reproduced, and every AI-backed action on the message. Both are absent
for such a message rather than present and broken.

**It is proven against one adversarial corpus that both stacks read.** Each case is a message and the `MailDocument` it
must reduce to. The service suite reduces every case and compares; the client unit suite reduces every case and
compares; a case added for one builder is a case for both because there is one corpus rather than two copies. A
difference between the two builders is a failing test in whichever suite runs, not a reading a reviewer has to notice.
The browser suite adds what only a browser can show: opening a device-decrypted message issues no request but to the
page's own origin, and no request the client makes carries the plaintext. Where the corpus lives is issue 1596's choice;
that it is one is this record's.

### Outbound: keys come from configuration and from verified signatures, and a missing key refuses the send

Recipient key material comes from two places and no others (G1): **public keys and certificates an operator configures**,
each naming the addresses it is held for, and **the key or certificate carried by an incoming signature whose
verification outcome was `Verified`**, which `stored_email_signer_keys` already keeps for this purpose. A signature that
did not verify contributes nothing usable, so a forged signature never becomes a key MailFathom would encrypt to (G3 is
refused). An expired or revoked key is not used.

**A key learned from a signature is filed under the address the key itself names, and never under the message's
`From`.** For S/MIME that is a `rfc822Name` in the certificate's subject alternative names, and for OpenPGP the address
in a self-certified user ID. `Verified` proves the signature was made with that key. It does not prove the key belongs
to the displayed author, because a valid key for one address can sign a message claiming any `From`. Filing it under
the `From` would let anybody holding a valid key make MailFathom encrypt somebody else's mail to them, which is the
planted key G3 was refused for. A key that names no address is learned for none.

**No network discovery is performed** (G2 is refused): no WKD, no DANE, no key server, no LDAP directory, and no fetch of
a certificate an incoming message points at. Each of those turns composing a message into a request to a third party
saying who the sender is writing to, and that is a decision of its own rather than a detail of this one.

**A recipient whose key is unknown makes an encrypted send refuse, and it never falls back to the clear.** Every
recipient counts, including a blind-copied one, and the refusal names the recipients that cannot be reached before
anything is sent. A message the person asked to encrypt is sealed once, at submission, and what the outbox keeps and
retries is the sealed message. The draft behind it is sealed to the author's own key (above), so nothing stores the
plaintext the person wrote. The message is also encrypted to the sending account's own key, which the draft rule makes
present on the service path. The sent copy therefore reads on the same terms as the reply that comes back.

**A blind-copied recipient gets a copy sealed to them alone.** A sealed message names every recipient it was sealed
to — a key ID in each OpenPGP session-key packet, an issuer and serial or a subject key identifier in each CMS
`RecipientInfo` — and every visible recipient can read those identifiers. One message sealed to everybody would tell
the `To` and `Cc` recipients who was blind-copied, which is exactly what a blind copy exists to hide. So the visible
message is sealed to the visible recipients and the sender, and each blind-copied recipient receives a separately
sealed copy.

Sealing happens where the key is. Where the deployment's ring holds the sending account's keys, the service signs and
encrypts. Where the key is on the device, the device seals the message before it leaves, using recipient public keys it
reads from the service — which are not secret — so the service receives ciphertext it cannot read and is a courier, as
it is on the way in. Issues 1597, 1598, and 1599 build the three halves.

### What reaches a log, a metric, or a trace

Neither stack writes key material, a passphrase, plaintext, or a recipient identifier read from an envelope — a key ID,
a fingerprint, an issuer and serial — to a log, a metric, or a trace attribute. The recipient identifiers name who a
message was sealed to, so they are personal data, not diagnostics. What may be recorded is MailFathom's own: the ring
entry's configured identifier, which is the operator's label for a key in the way
[ADR 0005](0005-data-encryption-key-ring-and-provisioning.md)'s `KeyId` is, and the outcome of a read.

### Breaks this records

None. Every surface keeps its current contract until a child changes it, and a deployment that configures no key reads
exactly as it does today. The ring is a new configuration root that is absent on every existing deployment, which is a
valid configuration.

### Consequences

- Good, because the stored bytes stay what the sender sealed, so the raw content table, a bucket, an export, and the
  source route each disclose ciphertext only, and every integrity check over the stored bytes is untouched.
- Good, because a message the ring opens is simply mail: search finds it, an answer cites it, a list shows its subject,
  and nothing about it needs a second code path beyond the in-flight decryption.
- Good, because the consent is one act stated in one place, and its scope is stated in the same place, so a key cannot
  read a mailbox nobody meant it to.
- Good, because the device row is a real alternative for somebody who will not put a key on a server, at a cost that is
  stated: no model, no search, no full-markup view.
- Good, because a second reduction is held equal to the first by a test rather than by care, and the pane's own trust
  boundary does not move.
- Good, because an encrypted send never degrades to a clear one.
- Neutral, because derived rows in the clear mean the database as a whole protects a ring-opened message no better than
  a cleartext one. That is the deliberate trade B1 makes, and the operator-facing page says it in those words.
- Neutral, because every read decrypts again. That is CPU spent per read in exchange for no plaintext held anywhere
  between reads. The concurrency limits every read already runs under bound it, and so do the plaintext, decompression,
  nesting, and structure limits above, which hold memory to what a stored message could already occupy.
- Bad, because sending encrypted through the service needs a ring key for the sending account even when every
  recipient's key is known, since that key is what seals the draft. Without one, the draft would reach the mail server
  in the clear.
- Bad, because S/MIME without integrity protection is opened, so its malleability is answered by withholding remote
  pictures and by reducing the envelope on its own, not by the cipher.
- Bad, because removing a key is not a retraction, and an operator who expects it to be will be wrong. The page states
  it where the ring is configured, which is the most this record can do short of making removal an erasure.
- Bad, because the client takes a MIME parser, an OpenPGP implementation, and a CMS implementation, which are a supply
  chain and a patch obligation it did not have, and a second reduction of mail that has to be kept equal to the first
  for as long as both exist.
- Bad, because a reader under a redacting sensitive-content posture cannot use a device key at all, and somebody who
  wants both has to choose.
- Bad, because an operator has to write the accounts each key serves, which is one line per key that issue 75's own
  wording did not ask for.

## Validation

- **The storage invariant is held by an integration-level assertion**, which is issue 75's own acceptance: after every
  keyed read — a body read, an extraction, a download, a re-derivation — the stored bytes and their digest are what was
  stored. A unit test cannot hold it, because the property is about what reaches the store.
- **The ring's scope is held by unit tests on the matcher**: a message addressed to a key whose entry names a different
  account takes the unkeyed path without a decryption attempt, and an entry naming no account opens nothing.
- **The device row is held at the network boundary**: the client's tests assert that no request carries plaintext from a
  device-opened message, and the browser suite asserts that opening one issues no request but to the page's own origin.
- **The second reduction is held by the shared corpus**, run by both suites, so the two builders cannot drift without a
  failing test.
- **The outbound refusal is held by unit tests** for a recipient with no key, a mixed recipient list, a blind-copied
  recipient with no key, an expired key, a key from a signature that did not verify, and a test that no network request
  is made to find a key.
- **Recipient key provenance is held by unit tests**: a key from a verified signature whose own address differs from the
  message's `From` is filed under the key's address and is never offered for the `From`.
- **Blind copies are held by unit tests**: the visible message's recipient identifiers name no blind-copied recipient,
  and each blind-copied recipient's copy names that recipient alone.
- **Decrypted content leaving only sealed is held by unit tests**: an answer, forward, or draft that quotes a decrypted
  message is sealed, or refuses, for both rows. An automatic reply never carries decrypted content. A saved draft of
  such a message is ciphertext in this deployment and in the drafts folder.
- **What is opened is held by the adversarial corpus**: captured ciphertext between clear siblings is an attachment and
  never joins the body, an OpenPGP message without integrity protection or with a failing check is not opened, and an
  integrity-unprotected S/MIME message offers no remote pictures.
- **The bounds are held by unit tests** for a plaintext over the ceiling, a compressed payload that expands past it, an
  envelope nested two levels deep, and an inner message over the structure limits, each publishing the limit state
  rather than a body.
- **No secret reaches telemetry**, held by tests in both stacks over the log and trace output of a keyed read and a
  sealed send.
- **By review** of each child of issue 75 against the table and the sections above. A child whose change makes a reader
  see more than its row allows, or makes a ring-opened message visibly different from cleartext mail beyond the
  protected marker and the rule that its content leaves only sealed, does not match this record.

## Pros and Cons of the Options

### A1 — the bytes the server delivered, always

- Good, because a raw-content copy, an export, and the source route disclose ciphertext only.
- Good, because the stored bytes and their digest stay true, so ADR 0017's verification and every re-derivation keep
  reading what arrived.
- Good, because adding a key, removing one, or moving a key from the ring to a device changes nothing that is stored.
- Bad, because every read decrypts again.

### A2 — the decrypted message written back

- Good, because every later read is a cleartext read.
- Bad, because the end-to-end protection the sender applied is removed from the one store that is meant to be the
  message as received, permanently and with no record that it happened.
- Bad, because the stored digest stops describing what arrived, and moving a key off the server afterwards protects
  nothing.

### A3 — both, the decrypted copy sealed under the data-encryption ring

- Good, because reads are cheaper and the copy is sealed at rest.
- Bad, because it is a second copy of every encrypted message, sealed under MailFathom's own key rather than the
  correspondent's, so the protection a database copy gets depends on a different secret than the sender chose.
- Bad, because the derived rows are in the clear anyway under B1, so the sealed copy protects nothing the derived rows
  do not already disclose.

### B1 — derived rows as for cleartext mail

- Good, because a ring-opened message is searchable, answerable, and summarised like the rest of the mailbox, which is
  why an operator configures a key at all.
- Bad, because a database copy discloses the readable text of that correspondence.

### B2 — nothing body-derived

- Good, because a database copy discloses nothing of the body.
- Bad, because the message is a hole in search and in every answer, which is the state issue 75 exists to end, and the
  reader is told the message is readable while every tool behaves as if it were not.

### B3 — derived rows sealed under the data-encryption ring

- Good, because a database copy discloses neither the raw content nor the derived text.
- Bad, because a full-text index and a vector index cannot be searched over sealed values, so sealing them is B2 with
  extra steps.
- Bad, because issue 75 declines it explicitly, and it would be a change to every derived table for one class of
  message.

### C1 — any message addressed to the key, anywhere

- Good, because it is the shortest configuration.
- Bad, because it makes the ring a decryption oracle between users of one deployment.

### C2 — messages addressed to the key in the accounts the entry names

- Good, because whose mail a key reads is stated where the key is, and the oracle is closed.
- Good, because matching stays automatic within that scope.
- Bad, because it is one more line per key.

### C3 — a per-account switch

- Bad, because it is a second spelling of the consent the key already is, and it can disagree with the key.
- Bad, because it still needs C2's scope, or it does not close the oracle.

### D1 — removal stops future reads and retracts nothing

- Good, because removing a key is a configuration change and behaves like one.
- Good, because erasure stays one act with one accountable path.
- Bad, because an operator can mistake it for a retraction, which the documentation has to prevent.

### D2 — removal deletes the rows derived under the key

- Good, because the message closes again in the index.
- Bad, because it is an erasure performed by a configuration edit, incomplete wherever the text has already gone, and
  it needs every derived row to record which key produced it.
- Bad, because restoring the key would have to rebuild everything, so an operator fixing a typo in the ring would
  trigger a mass deletion and a mass re-derivation.

### E1 — protected headers in flight only

- Good, because nothing inside the envelope ever reaches a column.
- Bad, because a ring-opened message protected by RFC 9788 lists, sorts, and searches as `[...]`, which contradicts
  *an ordinary message on every surface* at the field a person reads first.

### E2 — every protected header into the metadata columns

- Good, because the stored message looks exactly as the sender composed it.
- Bad, because the inner `From` is whatever the envelope's contents claim, so adopting it lets a sender bypass the
  sender verification that judged the outer one, and threading would follow identifiers no server saw.

### E3 — the subject in, the identities out

- Good, because the field that is a placeholder outside and meaningful inside is the one that moves, and every field
  that names a person or links a thread stays what the transport and sender authentication saw.
- Neutral, because a disagreement between an inner and an outer identity has to be reported, which is one more state a
  read publishes.

### F1 — the device reduces the message itself, proven against one corpus

- Good, because the pane draws the same contract through the same parser, so ADR 0024's guarantees hold for these
  messages too.
- Bad, because it is a second implementation of the reduction, with its own dependencies, kept equal only by the corpus.

### F2 — a sandboxed frame over the decrypted HTML

- Good, because it is the least client code.
- Bad, because ADR 0024 rests its markup surfaces on a service pass removing every executable construct, and the device
  has no such pass, so the frame would be drawing untreated mail.
- Bad, because a frame fetches remote resources whatever its sandbox flags, so the message would report itself read.

### F3 — the plain text alone

- Good, because it needs no reduction at all.
- Bad, because a heavily designed message becomes unreadable, the device row becomes the worst way to read mail in the
  product, and the plain text still needs the link and deception handling F1 builds.

### G1 — configuration and verified signatures

- Good, because every key MailFathom encrypts to came from an operator, or from a verified signature that filed it
  under the address the key itself names.
- Good, because composing a message makes no request to anybody.
- Bad, because a correspondent who has never written signed and is not configured cannot be written to encrypted.

### G2 — network discovery as well

- Good, because more recipients become reachable.
- Bad, because each lookup tells a third party who is being written to, at the moment of writing.

### G3 — any key any message carried

- Good, because more recipients become reachable.
- Bad, because a forged message would plant a key MailFathom then encrypts to, which turns encryption into a way to
  deliver a message to an attacker.

## More Information

- Issue 75 is the parent. Its children build this record: 1589 the ring and its matcher, 1590 in-flight decryption,
  1591 what adding and removing a key does to stored mail, 1592 the published state, 1593 the screens, 1594 serving
  unreadable mail as its bytes, 1595 device-held keys, 1596 device-side decryption and reduction, 1597 recipient keys,
  1598 outbound sealing, and 1599 the compose control. Signature verification, issue 1588, has shipped.
- The standards are S/MIME 4.0 (RFC 8551), PGP/MIME (RFC 3156), the OpenPGP crypto refresh (RFC 9580), and header
  protection (RFC 9788).
- [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) is the shape the ring follows and the ring it is not.
  The data-encryption ring is MailFathom's own sealing key; this one is somebody's correspondence identity, provisioned
  and rotated by a different person on a different schedule.
- [ADR 0024](0024-rendering-mail-in-the-client-as-a-closed-document-tree.md) is the contract the device builder
  reproduces, and [ADR 0028](0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md) is the
  rule the device row keeps.
- Revisit this decision if a deployment needs a database copy to protect ring-opened mail as well as the raw content
  does, which is a decision about sealing indexes rather than about keys; if a second builder proves impossible to keep
  equal to the first; or if network key discovery is wanted, which needs its own record.
