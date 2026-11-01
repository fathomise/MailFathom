---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-11-01
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep the ciphertext the server delivered, open a message in flight wherever the deployment's ring holds a key addressed to it for the account it arrived in, make it ordinary mail there, and encrypt only to keys configuration or a verified signature supplied

<!-- describes: backend/src/Infrastructure/Mail/Mime/**, backend/src/Application/Emails/Extraction/**, backend/src/Application/EmailContent/Rendering/**, backend/src/Application/Emails/GetEmailContent/**, backend/src/Application/Mail/Delivery/**, backend/src/Infrastructure/Persistence/Emails/StoredEmailSignerKeyWriter.cs, backend/src/Infrastructure/Persistence/Entities/StoredEmailSignerKeyEntity.cs -->

## Context and Problem Statement

What MailFathom does with an encrypted message today is notice it. `MimeContentClassification.BodyIsEncrypted`
recognises a body that arrived inside a cryptographic envelope, `ExtractedEmailText.EncryptedBody` stops extraction,
`StoredEmailEntity.IsEncrypted` marks the message, and `get_email_content` answers
`EmailBodyAvailability.EncryptedNotReadableLocally`. MailFathom holds no message key, so such a message is permanently
unreadable to its own owner, and nothing MailFathom sends can be encrypted.

Issue 75 closes that in both directions, and where the key is decides what each reader sees. Its children build against
this record, and each would otherwise answer for itself what a model may see, what a search index may hold, and what
removing a key undoes. Issue 1587 is this record, and every other child waits on it.

**A key held on the person's own device is outside this record.** It was taken out of issue 75 as too much to settle
together with the service-side work: a second reduction of mail on the device, key storage on every head, and sealing
outside the service each carry a decision of their own. Issue 2937 is where that is decided, if it is taken up. Here a
key is either in the deployment's ring or nowhere MailFathom can reach.

Two choices were settled before this record and it writes down their reasoning rather than reopening them: the content
store always holds the ciphertext, and outbound recipient keys come from configuration and from verified incoming
signatures with no network discovery. The questions it answers are:

- what the storage invariant is, and what it does and does not protect;
- what a message the ring opens gives each reader, and what a message no key opens gives;
- what MCP, search, embeddings, and the models reach;
- what the opt-in is, and whose mail a key in the deployment's ring may open;
- what removing a key undoes;
- whether headers protected inside an envelope reach the stored metadata;
- how the capability meets access, export, and erasure;
- where outbound key material comes from, who may encrypt to it, when a message is encrypted or signed, and what
  happens when a key is missing.

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

**F — where outbound recipient keys come from:**

- **F1.** Configuration, and the key or certificate carried by an incoming signature that verified.
- **F2.** F1 plus network discovery: WKD, DANE, key servers, LDAP.
- **F3.** F1 plus any key or certificate any incoming message carried.

**G — who may encrypt to a recipient key:**

- **G1.** Only sends from the mail account the key was configured for or learned in.
- **G2.** Every account on the deployment.
- **G3.** The members of the organization that holds the account.

**H — when a message is encrypted, and when it is signed:**

- **H1.** Each is decided by itself, with its own key. Encryption is on by default only for an answer to mail that
  arrived encrypted. Signing is on for every message where the person switched it on for everything, and otherwise
  per message.
- **H2.** Encrypt automatically whenever every recipient has a key, and sign whatever is encrypted.
- **H3.** Nothing by default: the person chooses both on every message.

## Decision Outcome

Chosen options: **A1**, **B1**, **C2**, **D1**, **E3**, **F1**, **G1**, and **H1**.

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
gadget (EFAIL's direct exfiltration).

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
key is held*, so an operator can tell a ceiling from a missing key.

### Where the key is decides what each reader sees

| Where the key is | What the service reads | What the client reads | What MCP, search, embeddings, and every model reach |
|---|---|---|---|
| The deployment's ring, in an entry naming the message's account | The whole message, in flight | Everything the service read | Everything, exactly as for cleartext mail |
| No ring entry for the message's account holds it | Nothing | The envelope and the state | The envelope and the state, never a body |

**A message the ring opens is an ordinary message on every surface.** It is extracted, chunked, indexed, embedded,
searched, rendered, summarised, answered from, classified, and scanned for sensitive content exactly like cleartext
mail, and its attachments are read and downloaded the same way, decrypted per download. Nothing tells a reader it is
different except two things. One is the marker saying it arrived protected: `IsEncrypted` keeps describing what arrived
rather than what is readable, and the availability a read publishes is what says whether a body could be read. The
other is the rule below that its content goes into mail MailFathom sends or files only sealed. A signature inside the
envelope is verified once the envelope is open, exactly as an outer one is.

**That includes every model the deployment calls.** Ring-opened text goes wherever the deployment already sends
cleartext mail: the embedding provider, the chat provider, the spam stage, image description. An operator who places a
key in the ring and points the deployment at a hosted provider has decided that provider may read that correspondence,
and the operator-facing page states it in those words.

**A message no key opens shows its envelope and nothing else**: its headers, its recipients, its attachment structure as
far as the outer MIME shows it, and a plain explanation of what would change that. It is never an empty body. That
includes a message whose key the person holds somewhere MailFathom cannot reach, such as their own device.

### Content decrypted from a protected message goes into mail only sealed

*An ordinary message on every surface* stops at one place. That place is the only visible difference from cleartext
mail this record allows beyond the protected marker. **Content decrypted from a protected message never goes in the
clear into a message MailFathom sends or files.** That covers a message sent to anybody and a draft filed on the
author's mail server. It does not cover what the table above lets a reader reach. An MCP caller and every model the
deployment calls are still handed ring-opened text exactly as they are handed cleartext mail. The rule is about mail
leaving the envelope the sender put it in, not about who may read it here. The sender chose encryption, and a
quotation, a forward, or an attached original that dropped it would be the silent degradation the drivers refuse. So:

- **An answer, a forward, or a draft that carries such content is sent encrypted, or it does not carry it.** A recipient
  with no key makes that send refuse, exactly as an encrypted send refuses (below), and the person may remove the
  quotation instead. This holds for an answer composed in the client and for one an agent drafted. Drafting and
  sending stay separate acts ([ADR 0013](0013-what-a-caller-must-do-before-mail-leaves.md)): a person who sends a
  quotation back to whoever sent the ciphertext has chosen to.
- **Nothing automatic quotes or forwards it.** An automatic reply, a rule, or any other pass that sends on nobody's
  explicit act never carries decrypted content. That is what keeps the ring from becoming a reply oracle, in which
  someone mails captured ciphertext into an account and reads what comes back.
- **A draft that carries such content, or that is to be sent encrypted, is stored and filed only as ciphertext sealed
  to the author's own key.** A draft here is a row, a stored message, and a copy in the author's drafts folder on their
  mail server. The mail server never saw the plaintext of what arrived, and a draft filed in the clear would hand it
  over. The service seals the draft with the ring's key for the sending account. Reopening a draft, including the
  outbox handing a refused send back to its draft, decrypts it in flight with that same key. A draft for an account the
  ring holds no key for cannot be sealed. It is refused rather than saved in the clear, so sending encrypted needs a
  ring key for the sending account.
- **A subject decrypted under E3 is decrypted content under this rule**, even though it is stored as the message's
  subject. A sealed message or a sealed draft carries a subject derived from it — `Re:` and all — only inside protected
  headers (RFC 9788), behind a placeholder outer `Subject`. A send that is not sealed and concerns such a message, such
  as a read receipt, takes its subject from the outer `Subject` the message arrived with and never from the stored one.
  The mail servers on both sides then see what they saw on the way in and nothing more.

### Holding a key is the whole opt-in, and the key says whose mail it opens

There is no per-account switch that turns reading encrypted mail on (C3 is refused). Placing private key material in the
ring is the operator's explicit decision to let MailFathom read the correspondence it opens.

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

### Removing a key stops future reads and retracts nothing by itself

Removing an entry from the ring stops every read that would have needed it from then on: a body read, a download, and
the next extraction of the message all take the unkeyed path. **The removal itself does not retract a chunk, a search
document, a vector, an attachment reading, the stored subject, or anything else already derived while the key was
held** (D2 is refused). Those rows are readable text that existed while the deployment was allowed to read it.
Deleting them on removal would be an erasure presented as a configuration change. It would be incomplete wherever the
text has already left — a model's answer, an export, a person's own reading — and invisible to the accountability this
project keeps for erasure.

**Derived rows are what the current configuration produces from the stored bytes whenever they are rebuilt, and a key
is part of that configuration.** So the next re-derivation of such a message, for whatever reason it runs, takes the
unkeyed path like every other read. It rewrites that message's derived rows and its stored subject to what an unkeyed
message has: the envelope and nothing body-derived. That is the same rule that already makes a rebuild apply a changed
sensitive-content posture. A re-derivation that kept rows derived under a key it no longer holds would need every
derived row to record which key produced it, and would leave those rows stuck forever behind any later posture change.
So removal retracts nothing by itself and gives no promise of retention either. It leaves the derived text in place
until the message is erased or next re-derived.

So the operator is told rather than surprised. The page that documents the ring states, where the ring is configured,
that removing a key is not an erasure. It states that what was derived under the key stays readable and searchable
until the messages are erased or next re-derived, and that erasing the messages is the way to retract it on purpose —
the cascade from the stored email reaches every derived row. Issue 1591 writes that page's paragraph and builds the
reverse case: a key *added* to the ring makes the existing extraction backfill re-derive the stored messages it now
opens, without a re-synchronization.

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

For a message no key opens, the outer headers are all there is.

### Access, export, and erasure

- **Access.** A readable form of a message exists only while a key is held. While the ring holds it, every read surface
  serves it, and the derived rows — which are personal data in the clear — are part of what an access request reaches,
  exactly as for cleartext mail. For a message no key opens, the deployment holds the ciphertext and the envelope and
  nothing more, and that is what an access request reaches.
- **Export.** The mailbox export carries the stored bytes exactly, so an encrypted message is exported as the ciphertext
  it arrived as. That is what makes the archive importable anywhere and keeps it faithful, and it means the export
  writes no plaintext copy at rest for the time an archive is kept. A person who wants a readable copy reads it with the
  key they or their operator hold; no decrypted export is offered.
- **Erasure.** Erasing a message erases its stored bytes and, through the existing cascade from the stored email, every
  derived row, whatever key opened it and whether or not a key is still held. Erasure never needs a key. Removing a key
  is not erasure (above).

### Outbound: keys come from configuration and from verified signatures, and a missing key refuses the send

Recipient key material comes from two places and no others (F1): **public keys and certificates configured on a mail
account**, each naming the addresses it is held for, and **the key or certificate carried by an incoming signature whose
verification outcome was `Verified`**, which `stored_email_signer_keys` already keeps for this purpose. A signature that
did not verify contributes nothing usable, so a forged signature never becomes a key MailFathom would encrypt to (F3 is
refused). An expired or revoked key is not used.

**Whether a key is revoked is decided from what MailFathom already holds, and sending makes no request to find out.**
A revocation check at send time — OCSP, a CRL, a key server — is a request to a third party naming the recipient at the
moment of writing, which is exactly what refusing network discovery refuses. So the sources are these:

- For OpenPGP, the revocation signatures the key's own transferable form carries, as configured or as learned.
- For an S/MIME certificate learned from a signature, the revocation outcome recorded when that signature was verified
  at receipt. Each later verified signature from the same certificate refreshes that outcome.
- For a configured S/MIME certificate, its validity period. An operator who learns of a revocation removes the entry.

A certificate revoked after the last signature anybody received from it is therefore used until it expires, or until a
newer signature from it shows the revocation. That is the price of composing without telling anybody who is being
written to, and it is recorded as a consequence rather than left for issues 1597 and 1598 to discover.

**A key learned from a signature is filed under the address the key itself names, and never under the message's
`From`.** For S/MIME that is a `rfc822Name` in the certificate's subject alternative names, and for OpenPGP the address
in a self-certified user ID. `Verified` proves the signature was made with that key. It does not prove the key belongs
to the displayed author, because a valid key for one address can sign a message claiming any `From`. Filing it under
the `From` would let anybody holding a valid key make MailFathom encrypt somebody else's mail to them, which is the
planted key F3 was refused for. A key that names no address is learned for none.

**Every recipient key belongs to one mail account, and only a send from that account encrypts to it (G1).**

- **A configured recipient key is part of the mail account's own record**, beside the account's other declarations
  and written the same way — `mfctl account add --from-file` or the administrative endpoint. It names the addresses it
  is held for, and only a send from that account encrypts to it. It is not deployment configuration. Whom an account
  writes to encrypted is a fact about that account's correspondence, and a deployment-wide list would tell every
  account on it who each one corresponds with.
- **A learned key belongs to the account whose stored message carried the verified signature.** It is derived from
  that message, so it inherits the message's access, as every derived row does. Only a send from that account encrypts
  to it, and the people assigned to the account are the ones who send from it. It is never offered to another account,
  even one the same person holds, and never to another user or organization on the deployment who writes to the same
  address. It is erased with the message, through the cascade that already removes `stored_email_signer_keys` with its
  email.
- **Learning keys from signatures is an account setting, on by default.** While it is off, no learned key is offered
  for that account's sends, and turning it back on offers the keys already held again. The switch decides what may be
  learned. It is not a second consent to read, which holding a key in the ring stays the only one of, and it changes
  nothing about verifying a signature, which runs whatever the setting says. Like every new per-account setting, it is
  held by the service and set through its API, and the client's Settings screen does not draw it yet.
- **A learned key is used for what the key itself allows.** A certificate whose key usage permits only signing, or an
  OpenPGP key with no encryption-capable subkey, verifies signatures and is learned for nothing.

Where both kinds are held for one address on one account, the configured key is used. It is a deliberate statement
about that account, and a learned one is a fact observed in one of its messages.

What [issue 1588](https://github.com/fathomise/MailFathom/issues/1588) verifies decides how much learning can add. An
S/MIME signature verifies against a certificate authority, so its certificate is often a key nobody configured, and
that is the case learning exists for. An OpenPGP signature verifies only against a key in
`MailSynchronization:SignatureVerification:TrustedOpenPgpKeys`. A key learned from one is therefore always a key the
deployment already trusts for verification, and learning moves it onto that account as a recipient key.

**No network discovery is performed** (F2 is refused): no WKD, no DANE, no key server, no LDAP directory, and no fetch of
a certificate an incoming message points at. Each of those turns composing a message into a request to a third party
saying who the sender is writing to, and that is a decision of its own rather than a detail of this one.

**A recipient whose key is unknown makes an encrypted send refuse, and it never falls back to the clear.** Every
recipient counts, including a blind-copied one, and the refusal names the recipients that cannot be reached before
anything is sent. A message the person asked to encrypt is sealed once, at submission, and what the outbox keeps and
retries is the sealed message. The draft behind it is sealed to the author's own key (above), so nothing stores the
plaintext the person wrote. The message is also encrypted to the sending account's own key in the ring. The sent copy
therefore reads on the same terms as the reply that comes back.

**A blind-copied recipient gets a copy sealed to them alone.** A sealed message names every recipient it was sealed
to — a key ID in each OpenPGP session-key packet, an issuer and serial or a subject key identifier in each CMS
`RecipientInfo` — and every visible recipient can read those identifiers. One message sealed to everybody would tell
the `To` and `Cc` recipients who was blind-copied, which is exactly what a blind copy exists to hide. So the visible
message is sealed to the visible recipients and the sender, and each blind-copied recipient receives a separately
sealed copy.

**Sealing happens in the service, with the ring's key for the sending account.** The service seals the draft to that
key, and seals the message to the recipients and to that key when it is sent. A sending account the ring holds no
encryption key for cannot send encrypted: its draft could not be sealed to anybody its author can read it back with,
and the send refuses with that reason before anything leaves. Issues 1597, 1598, and 1599 build the recipient keys, the
sealing, and the compose control.

### Signing and encrypting are two acts with two keys

Throughout this record, *sealing* means encrypting. Signing is a separate act. It uses a different key, it is chosen
separately, and neither ever implies the other.

- **Each key does what its usage allows.** Encryption decrypts what arrives and seals drafts, sent copies, and
  outgoing messages. Signing signs. The key is an OpenPGP encryption subkey or an S/MIME certificate whose usage permits
  key encipherment for the first, and an OpenPGP signing key or a certificate permitting digital signatures for the
  second. A ring entry may hold either or both, and one that holds both is still used for each act by that act's own
  rule.
- **A signature uses the sending account's signing key in the ring, and the service signs.** A signature never needs a
  recipient's key, so a signed message that is not encrypted refuses for nobody.

**When a message is encrypted (H1):**

| The message | Encryption |
|---|---|
| An answer to, or a forward of, a message that arrived encrypted | On by default. It cannot be turned off while the message carries decrypted content (above); it can be turned off once the quotation is removed |
| A new message, every recipient has a key | Offered and off: the composer shows it can be turned on |
| Any message with a recipient who has no key | Unavailable, with the reason against that recipient |
| An automatic reply, a rule, or any other send nobody authored in the moment | Never encrypted, and it never carries decrypted content |

Encryption is not switched on just because every recipient has a key (H2 is refused). A key learned from a signature can
be one its owner no longer uses, or does not hold where they read mail. A message encrypted on the strength of it
would arrive unreadable to the person it was written for, and nobody would have chosen that.

**When a message is signed (H1):**

- **Signing everything is a per-person setting, off by default.** While it is on, every message the person sends is
  signed, from every account the ring holds a signing key for. The setting is held by the service and set through its
  API, and the client's Settings screen does not draw it yet.
- **While it is off, signing is chosen per message.** The composer offers it beside encryption, and an answer to signed
  or encrypted mail does not switch it on by itself.
- **An account with no signing key cannot sign.** The per-message choice is absent for it. With the setting on, a
  message from that account goes unsigned, and the composer says so before it is sent. Signing is never the reason a
  send refuses, because an unsigned message claims nothing false.

**What MailFathom adds to an outgoing message goes in before the message is signed and encrypted.** A message is
footed, screened, and given the organization's signature when it is queued. The footer is appended and the outgoing
screening reads the message first. The S/MIME or OpenPGP signature then covers the message with its footer, and the
encryption seals that signed message.
[ADR 0042](0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md)'s organization signature
is applied last, to the message as it is transmitted, because that is the form a receiving deployment verifies before
it opens anything.

**Both choices are made wherever a message is composed**: in the composer, and in the draft an agent prepares in the
agent view. The two surfaces offer the same two choices with the same defaults. Drafting and sending stay separate
acts there, as ADR 0013 requires. Where on each surface the choices sit is the design's to draw.

### Rotation: a new key joins the list, and the old one stays

Every place a key is held here is a list, never a single slot: the ring's entries for an account, and an account's
configured recipient keys. Rotating a key means adding the new one beside the old one.

- **Reading never needs to know which key is current.** A message is opened by the key its own envelope names, so mail
  sealed to the old key keeps opening with the old key, and mail sealed to the new one opens with the new one. The
  envelope keeps the two sets of mail apart without anybody stating which key is active, which is why the ring has no
  `ActiveKeyId` of the kind [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) needs.
- **Sealing and signing use the newest valid key.** Where an account holds several keys with the usage an act needs,
  the act takes the newest one that is neither expired nor revoked: by creation time for OpenPGP, and by the start of
  the validity period for S/MIME. That covers sealing a draft or a sent copy to the author, and signing. After a
  rotation, new mail therefore goes out under the new key without anything else changing.
- **Choosing a recipient key follows the same rule.** A configured key wins over a learned one for the same address
  on the same account. Among keys of one kind, the newest valid one is used. Expired and revoked keys are passed
  over, never treated as the last resort.
- **Retiring the old key is a separate act, taken when nothing that matters is sealed only to it.** Removing it is the
  removal described above: mail sealed only to it stops opening, and its derived rows go at their next re-derivation.
  Keeping an old key costs nothing but its place in the list, so the safe default after a rotation is to keep it.

### What reaches a log, a metric, or a trace

Nothing writes key material, a passphrase, plaintext, or a recipient identifier read from an envelope — a key ID, a
fingerprint, an issuer and serial — to a log, a metric, or a trace attribute. The recipient identifiers name who a
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
- Good, because an encrypted send never degrades to a clear one.
- Neutral, because derived rows in the clear mean the database as a whole protects a ring-opened message no better than
  a cleartext one. That is the deliberate trade B1 makes, and the operator-facing page says it in those words.
- Neutral, because every read decrypts again. That is CPU spent per read in exchange for no plaintext held anywhere
  between reads. The concurrency limits every read already runs under bound it, and so do the plaintext, decompression,
  nesting, and structure limits above, which hold memory to what a stored message could already occupy.
- Bad, because somebody who will not put their key on a server cannot read their encrypted mail in MailFathom at all.
  Issue 2937 is where a key on the person's own device is decided.
- Bad, because sending encrypted needs a ring key for the sending account, even when every recipient's key is known. It
  is what seals the draft. Without it, the draft would reach the mail server in the clear.
- Bad, because a recipient key is the account's alone. A key learned in one mailbox is not offered to a colleague or to
  the same person's other account, and making a correspondent reachable from several accounts means configuring the
  key on each of them.
- Bad, because nothing is encrypted automatically on the strength of a known key, so a person who wants a new message
  encrypted turns it on.
- Bad, because a recipient certificate revoked after the last signature received from it is still encrypted to until
  it expires or a newer signature reveals the revocation, since sending makes no revocation request.
- Bad, because S/MIME without integrity protection is opened, so its malleability is answered by withholding remote
  pictures and by reducing the envelope on its own, not by the cipher.
- Bad, because removing a key is not a retraction. The derived text goes only when the message is erased or next
  re-derived, and an operator who expects either more or less will be wrong. The page states it where the ring is
  configured, which is the most this record can do short of making removal an erasure.
- Bad, because an operator has to write the accounts each key serves, which is one line per key that issue 75's own
  wording did not ask for.

## Validation

- **The storage invariant is held by an integration-level assertion**, which is issue 75's own acceptance: after every
  keyed read — a body read, an extraction, a download, a re-derivation — the stored bytes and their digest are what was
  stored. A unit test cannot hold it, because the property is about what reaches the store.
- **The ring's scope is held by unit tests on the matcher**: a message addressed to a key whose entry names a different
  account takes the unkeyed path without a decryption attempt, and an entry naming no account opens nothing.
- **The outbound refusal is held by unit tests** for a recipient with no key, a mixed recipient list, a blind-copied
  recipient with no key, an expired key, a key from a signature that did not verify, a sending account with no ring
  key, and a test that no network request is made to find a key.
- **Send-time revocation is held by unit tests**: an OpenPGP key carrying a revocation signature and a learned S/MIME
  certificate recorded as revoked at its latest verification are both refused, and a send makes no request to any host.
- **Recipient key provenance is held by unit tests**: a key from a verified signature whose own address differs from the
  message's `From` is filed under the key's address and is never offered for the `From`. A learned or configured key is
  offered for sends from its own account and refused for every other account, including another account of the same
  person. A configured key wins over a learned one for the same address. An account with learning switched off is
  offered no learned key. A signing-only certificate is learned for nothing.
- **Rotation is held by unit tests**: with an old and a new key held for one account, mail sealed to each opens with
  its own key. A new draft, a sent copy, and a signature use the new key. An expired newest key is passed over for the
  next valid one.
- **The order of what is added is held by unit tests**: the footer is inside the signed and sealed content, and the
  organization signature covers the transmitted form.
- **Signing and encrypting are held apart by unit tests**: a signed message that is not encrypted refuses for no
  recipient. An encryption key never signs, and a signing key never decrypts or seals. The sign-everything setting
  signs every message from an account with a signing key and leaves one from an account without one unsigned. An
  answer to encrypted mail starts encrypted and does not start signed.
- **Blind copies are held by unit tests**: the visible message's recipient identifiers name no blind-copied recipient,
  and each blind-copied recipient's copy names that recipient alone.
- **Decrypted content leaving only sealed is held by unit tests**: an answer, forward, or draft that quotes a decrypted
  message is sealed, or refuses. An automatic reply never carries decrypted content. A saved draft of such a message is
  ciphertext in this deployment and in the drafts folder. A sealed answer to a message whose subject was protected
  carries the derived subject only in its protected headers, and a read receipt for it takes the outer subject the
  message arrived with.
- **Removal is held by unit tests on the extraction path**: re-deriving a message whose key was removed writes the rows
  and the stored subject an unkeyed message has.
- **What is opened is held by an adversarial corpus**: captured ciphertext between clear siblings is an attachment and
  never joins the body, an OpenPGP message without integrity protection or with a failing check is not opened, and an
  integrity-unprotected S/MIME message offers no remote pictures.
- **The bounds are held by unit tests** for a plaintext over the ceiling, a compressed payload that expands past it, an
  envelope nested two levels deep, and an inner message over the structure limits, each publishing the limit state
  rather than a body.
- **No secret reaches telemetry**, held by tests over the log and trace output of a keyed read and a sealed send.
- **By review** of each child of issue 75 against the table and the sections above. A child whose change makes a reader
  see more than its row allows, or makes a ring-opened message visibly different from cleartext mail beyond the
  protected marker and the rule that its content goes into mail MailFathom sends or files only sealed, does not match
  this record.

## Pros and Cons of the Options

### A1 — the bytes the server delivered, always

- Good, because a raw-content copy, an export, and the source route disclose ciphertext only.
- Good, because the stored bytes and their digest stay true, so ADR 0017's verification and every re-derivation keep
  reading what arrived.
- Good, because adding a key or removing one changes nothing that is stored.
- Bad, because every read decrypts again.

### A2 — the decrypted message written back

- Good, because every later read is a cleartext read.
- Bad, because the end-to-end protection the sender applied is removed from the one store that is meant to be the
  message as received, permanently and with no record that it happened.
- Bad, because the stored digest stops describing what arrived, and removing the key afterwards protects nothing.

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

### D1 — removal stops future reads and retracts nothing by itself

- Good, because removing a key is a configuration change and behaves like one.
- Good, because erasure stays one act with one accountable path.
- Good, because a re-derivation keeps meaning one thing, namely what the current configuration produces from the stored
  bytes.
- Bad, because an operator can mistake it for a retraction, or for a promise of retention, and the documentation has to
  prevent both.

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

### F1 — configuration and verified signatures

- Good, because every key MailFathom encrypts to came from an operator, or from a verified signature that filed it
  under the address the key itself names.
- Good, because composing a message makes no request to anybody.
- Bad, because a correspondent who has never written signed and is not configured cannot be written to encrypted.

### F2 — network discovery as well

- Good, because more recipients become reachable.
- Bad, because each lookup tells a third party who is being written to, at the moment of writing.

### F3 — any key any message carried

- Good, because more recipients become reachable.
- Bad, because a forged message would plant a key MailFathom then encrypts to, which turns encryption into a way to
  deliver a message to an attacker.

### G1 — every key for its own mail account alone

- Good, because no key tells anybody outside the account who that account corresponds with.
- Good, because a learned key is erased with its message, with no lifecycle of its own, and a configured one goes with
  the account's record.
- Bad, because reaching one correspondent from several accounts means configuring the key on each.

### G2 — every key for every account

- Good, because the most recipients are reachable.
- Bad, because every key, configured or learned, tells every user on the deployment who corresponds with whom.

### G3 — every key for the organization

- Good, because colleagues reach each other's correspondents.
- Bad, because it pools what one mailbox observed across an organization's members without anybody choosing to.

### H1 — encryption and signing decided separately, with narrow defaults

- Good, because the one default that is on, encrypting an answer to encrypted mail, keeps a thread protected without
  anybody remembering to.
- Good, because a person who wants every message signed sets it once.
- Bad, because a new message to somebody whose key is known goes unencrypted unless the person turns it on.

### H2 — encrypt whenever every recipient has a key, and sign what is encrypted

- Good, because the most mail is protected with no act by anybody.
- Bad, because a learned key can be one its owner cannot read with, so the message arrives unreadable without anybody
  having chosen that.
- Bad, because it ties signing to encryption, which are separate acts with separate keys.

### H3 — nothing by default

- Good, because nothing happens that the person did not choose.
- Bad, because an answer to encrypted mail is one forgotten click away from leaving the thread unencrypted, and the
  sealing rule would refuse it whenever it quotes.

## More Information

- Issue 75 is the parent. Its children build this record: 1589 the ring and its matcher, 1590 in-flight decryption,
  1591 what adding and removing a key does to stored mail, 1592 the published state, 1593 the screens, 1597 recipient
  keys, 1598 outbound sealing, and 1599 the compose control. Signature verification, issue 1588, has shipped.
- Issue 2937 is the decision about a key held on the person's own device, outside issue 75.
- The standards are S/MIME 4.0 (RFC 8551), PGP/MIME (RFC 3156), the OpenPGP crypto refresh (RFC 9580), and header
  protection (RFC 9788).
- [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) is the shape the ring follows and the ring it is not.
  The data-encryption ring is MailFathom's own sealing key; this one is somebody's correspondence identity, provisioned
  and rotated by a different person on a different schedule.
- Revisit this decision if a deployment needs a database copy to protect ring-opened mail as well as the raw content
  does, which is a decision about sealing indexes rather than about keys; or if network key discovery is wanted, which
  needs its own record.
