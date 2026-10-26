---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-26
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Sign every message an organization's account sends with an Ed25519 key the organization holds, write the signature in a header of MailFathom's own, and trust a sender only where the signature, its key, its author, and its recipient all belong to the receiving account's organization

<!-- describes: backend/src/Domain/Emails/Authentication/SenderTrust*.cs, backend/src/Application/Mail/Delivery/MailOutbox.cs, backend/src/Infrastructure/Mail/Dkim/** -->

## Context and Problem Statement

MailFathom is a client of real mail servers, so a message between two accounts of one organization crosses servers MailFathom does not control. One account may sit on a hosted provider and the other on the organization's own server. Nothing a receiving account sees today proves such a message came from another account of its organization:

- `From` is written by whoever sends the message.
- A provider's DKIM signature proves a domain, not a MailFathom organization.
- `SenderTrustSource.OwnAccountDomain` recognizes every author on a shared provider's domain alike.

Issue 2714 adds the proof: every message an organization's account sends is signed with a key that organization holds, and a receiving account of the same organization verifies the signature locally and trusts its sender. The signer and the verifier are the same deployment, so no key is published in DNS or anywhere else.

The owner settled six things before this record, and it does not reopen them:

- One signing key per organization, created automatically and never by an operator.
- The key rotates automatically. A retired key's private half is destroyed and its public half kept, so mail signed earlier still verifies. A key can also be revoked by hand.
- The organization's name is never written into a message. A key reaches its organization through the store, and the client shows the organization's current display name.
- Every message sent from an account in an organization is signed, whatever its recipients.
- An account in no organization takes no part: it signs nothing, and its mail is never verified against an organization key.
- A sender verified this way is trusted (`SenderTrustLevel.Trusted`) as a member of the organization. Mail already stored is not re-evaluated.

This record answers what issue 2715 left open: the header, the algorithm, the signed headers, the selector, sealing, rotation, revocation, what a valid signature must hold before it grants trust, the model, the lifecycle, and the custody phases.

### What the pinned MimeKit does

A spike on 2026-10-26 ran MimeKit `4.18.1` and BouncyCastle.Cryptography `2.7.0`, the versions `backend/Directory.Packages.props` pins, through `DkimSigner` and `DkimVerifier` with a key locator that reads a key from memory. It signed `from:to:cc:subject:date:message-id:in-reply-to:references` with `relaxed/relaxed`, serialized the message, parsed it back, and verified it.

| Check | Ed25519-SHA256 | RSA-SHA256 (2048) |
|---|---|---|
| `DkimSigner` signs, `DkimVerifier` verifies; the verifier enables the algorithm by default | yes | yes |
| `DkimVerifier` given a header under another name | refused: `ArgumentException`, *The signature parameter MUST be a DKIM-Signature header* | the same |
| `DkimVerifier` given a `DKIM-Signature` header built from that header's value | verifies | verifies |
| A relay adds `Received`, its own `DKIM-Signature`, and a header of its own | still verifies | still verifies |
| `Subject` changed after signing | fails | fails |
| A footer appended to the body after signing | fails | fails |
| Signed with no `To`, then a `To` added | fails: `h=` still lists `to`, so an absent header is signed as absent | fails |

Three more facts came out of it:

- MimeKit writes `t=` and writes neither `x=` nor `l=` unless asked.
- With the default query method it writes no `q=` tag, and the locator is still asked with `methods=dns/txt`, `domain` set to `d=`, and the selector.
- Relaxed header canonicalization lowercases the header's name and signs it, so the name the verifier sees has to be the name the signer wrote. The rename at write time has to be reversed exactly at read time.

## Decision Drivers

- A colleague's impersonation must never read as the colleague, however many servers the real message crossed.
- Nothing outside the deployment may be asked, told, or relied on: no DNS record, no published key, no third party's verdict.
- Recipients outside the organization must judge the message exactly as they did before it was signed.
- A key, once leaked, needs an answer, and the answer must not depend on a timestamp the forger writes.
- Several replicas run this code, and a rolling upgrade runs two builds at once.
- Keep MimeKit's DKIM implementation rather than writing a canonicalizer and a signature format of our own.

## Considered Options

For the header:

1. **`DKIM-Signature`**, with a selector published nowhere.
2. **A header of MailFathom's own, `MailFathom-Organization-Signature`**, carrying a DKIM signature value, renamed to `DKIM-Signature` in memory for verification only.
3. **An S/MIME or OpenPGP signature** made with the organization's key.

For the algorithm: **Ed25519-SHA256** (RFC 8463), or **RSA-SHA256** at 2048 bits.

For where the rotation interval is set: **the settings policy of ADR 0037**, or **deployment configuration**.

## Decision Outcome

Chosen: **option 2, a DKIM signature value in `MailFathom-Organization-Signature`, made with Ed25519-SHA256**, and the rotation interval in **deployment configuration**.

Option 2 keeps everything DKIM does well: a canonicalization that survives what relays do to whitespace and header case, a body hash, a signed header list, and an implementation already pinned and already used here. It refuses the one thing DKIM's own header would do: tell every receiving server there is a signature it must try to verify.

### The header

**The signature is written as `MailFathom-Organization-Signature`, never as `DKIM-Signature`.** A `DKIM-Signature` header is an instruction to every receiving server. It names a domain in `d=` and asks the server to fetch `s=._domainkey.d=` from DNS.

Here that lookup always finds nothing, so every server records a `permerror` or a `fail`. Some spam filters score that against the message, which breaks the third decision driver. On a shared provider, `d=` would also be a claim made on that provider's behalf with a key the provider never published.

The deployment's own local DKIM verifier would be affected too:

- It would spend a DNS query on every arrival.
- It would leak the selector to the resolver.
- Its signature slots, `LocalSenderAuthenticationReading.MaximumVerifiedSignaturesPerMessage`, would fill with a signature it can never check.

A header of MailFathom's own costs none of that. Every server and every filter ignores a header it does not know.

The name has no `X-` prefix, following RFC 6648. It names the organization because the signature is the organization's. A later signature of another kind would take a name of its own rather than share this one.

**Signing produces a `DKIM-Signature`, which is renamed before the message is stored.** Verification takes the header's value, builds a `DKIM-Signature` header from it in memory, and hands that to `DkimVerifier`. The message itself is never modified. The spike shows the round trip verifies, and that the direct path is refused.

The rename is the one place this design leaves the beaten track of DKIM, so it stays inside the adapter that owns MimeKit, beside `backend/src/Infrastructure/Mail/Dkim/`.

### The algorithm

**Ed25519-SHA256.** The pinned MimeKit signs and verifies it, as the spike shows. RFC 8463's usual objection — receivers that do not support it — does not apply, because the only verifier is this deployment.

What Ed25519 gives:

- a 32-byte public key and a 64-byte signature, against a 256-byte key and a 344-character signature for RSA-2048;
- deterministic signatures, with no random padding to get wrong;
- no key-size parameter for a later reader to weaken.

A key is generated from BouncyCastle's `SecureRandom`, which is the cryptographically secure generator the root `AGENTS.md` asks for.

### The tags

| Tag | Value | Why |
|---|---|---|
| `v` | `1` | DKIM's only version. |
| `a` | `ed25519-sha256` | Above. |
| `c` | `relaxed/relaxed` | Relays refold headers and rewrite whitespace; `simple` breaks on either. |
| `d` | the domain of the sending account's address | The format requires it and MimeKit parses it. It grants nothing: the verifier never reads it, locates the key by `s=` alone, and checks the signed `From` instead. |
| `s` | the key's identifier | Below. |
| `h` | `from:to:cc:subject:date:message-id:in-reply-to:references` | Below. |
| `t` | written by MimeKit | Harmless and never read: a timestamp the signer writes proves nothing about when a forger wrote it. |
| `x` | **never** | An expiring signature would stop mail already delivered from verifying, which is exactly what keeping a retired key's public half exists to prevent. |
| `l` | **never** | A body-length limit lets anybody append to a signed body and keep the signature. |
| `i`, `q`, `z` | never | Nothing reads them. |

**Every listed header is signed whether or not the message carries it.** A header absent at signing is signed as absent, so adding it later fails verification. That is what stops a blind copy from being turned into a message that names its recipient in `To`, and the spike confirms it.

`Bcc` is not listed: the submitted message carries none. `Reply-To` is not listed either. It is the one header a receiving client acts on that a forger would want to change, but changing it does not change who the message is from — and verifying the author is all this signature is for.

**The selector is the key's identifier**, a version 7 UUID under ADR 0036. That ADR is what makes it safe to write into every message: an identifier is never a secret. It names one key without naming the organization. A recipient outside the organization learns only that two messages were signed with the same key, which adds little to what their shared `From` domain already says.

### The key, its sealing, and its rotation

**Each organization holds keys in a store of its own.** A key has:

- its identifier;
- the organization it belongs to;
- its public half;
- its private half, sealed, and absent once the key is retired or revoked;
- when it was created, retired, and revoked.

**At most one key per organization is active, and PostgreSQL enforces it** with a partial unique index on the organization over active keys. Two replicas creating or rotating at once therefore leave exactly one active key, and the one that lost reads it back. The organization's name is never stored on a key: a key reaches the name through the organization it belongs to.

**The private half is sealed under the data-encryption key ring of ADR 0005**, with a new `DataEncryptionPurpose` named `organization-signing-key` and the key's identifier as the subject. The ADR 0005 binding is what stops a sealed signing key from being opened as anything else, or moved onto another key's row. The public half is stored unsealed, because nothing about it is secret.

**A deployment without a key ring cannot seal, so it mints no key.** ADR 0005 makes an absent ring valid until something seals a value. Here, nothing minted means nothing is sealed, and organizations still work:

- An organization is still created.
- Its messages go out unsigned.
- The background pass below logs and counts that it could not mint.

Refusing to create an organization, or refusing to send, over a missing ring would turn a feature the operator never configured into an outage.

**A key is minted with the organization, in the same transaction**, and a background pass mints one for any organization that has none — every organization that exists when the feature arrives, and any minted while the ring was missing.

**Rotation is the same background pass.** It runs through `IWorkLeaseRunner` with one leased scope per organization, as `backend/src/AGENTS.md` requires of work that must not run twice. An active key older than the interval is rotated in one transaction: the new key is created, and the old one is retired and its private half deleted.

Rotation is not left to the first send after the interval. An organization that stops sending would otherwise keep its active key forever, and a leaked key would go on verifying forever with it.

**The interval is deployment configuration, `OrganizationSigning:KeyRotationInterval`, defaulting to 365 days and refused below one day.** ADR 0037's settings policy is not the place:

- That policy governs a user's record and a mail account. A key belongs to neither.
- Putting the interval there would open a third governed section for one value.
- Whether a leaked key may live a month or a year is the operator's security posture, not a preference somebody in an organization holds.

**A retired key's public half is kept for as long as its organization exists.** One key a year is a few dozen bytes per organization per year, and there is no point after which mail signed with the key stops being worth verifying.

**Revocation is by hand, through `mfctl`, and is audited.** Revoking the active key deletes its private half and mints a new active key in the same transaction, so the organization keeps signing. Revoking a retired key only marks it.

**A revoked key confirms nothing for any message verified after the revocation**, whenever the message says it was signed. The other answer — revoked only for messages signed after the revocation — would rest on `t=` or `Date`, and the forger writes both. A leaked key would sign backdated mail that verifies forever.

What a revocation cannot reach is a verdict already stored. Mail already stored is not re-evaluated, so a revocation changes the verdict of every later arrival and of nothing that arrived before it.

### Signing

**A message is signed once, where its MIME is first stored as a send:** in `MailOutbox.EnqueueAsync`, after the outgoing screen and before the content is placed. Signing goes through a port in `Application`, implemented by a MimeKit adapter in `Infrastructure`.

Every send reaches the outbox through that one method:

- a composed message (`AuthoredMailSubmission`);
- a response (`AuthoredResponseSubmission`);
- an occasion of a recurring send (`RecurringSendOccurrenceHandler`);
- a promoted draft (`MailDraftPromotion`), which enqueues the draft's own MIME without composing it again.

The composer would miss that last path, which is why signing does not sit there.

The bytes stored there are what every later step uses:

- every submission attempt sends them;
- every filing into the sent folder appends them;
- the outbox reads them.

This corrects issue 2717's *immediately before submission over SMTP*. Signing at submission would mean either rewriting the stored MIME on every attempt, or sending bytes nothing stored.

A request that already has a record keeps the bytes it stored, as `EnqueueAsync` already guarantees. Its repeated signature is computed and discarded. A draft is never signed: it is not sent, and the copy in the drafts folder is not the message that leaves.

Every occasion of a recurring send is composed on its own, with its own `Date` and `Message-ID`, so it is signed on its own. A scheduled send is signed when it is enqueued, not when it leaves. If the key rotates in between, the message still verifies; if the key is revoked in between, it does not, which is what revocation is for.

**The organization is the sending account's organization at enqueue time.** An account in no organization signs nothing.

**A send is never refused for want of a signature.** An organization with no active key — no ring, or a mint that has not run yet — sends unsigned, and a counter says so. A signature is evidence for the recipient, not a condition of delivery.

**Nothing else is added to the message.** The header carries a domain the message already names and an identifier that is not secret. The organization's name and identifier are never written, as the owner decided.

### Verifying, and what a valid signature must hold to grant trust

Verification runs at extraction, beside the local DKIM verification, for a receiving account that belongs to an organization. It reads at most `LocalSenderAuthenticationReading.MaximumVerifiedSignaturesPerMessage` headers of this name, because their number is written by whoever sent the message. It locates keys among **the receiving account's own organization's keys and nowhere else**, so a selector naming any other key answers *no key*, and nothing is asked of DNS.

**Trust is granted only where all four hold:**

1. **The signature verifies** against a key of the receiving account's organization that is active or retired, and never revoked.
2. **The signed `From` names exactly one mailbox, and its address is the address of a mail account in that organization**, read at verification time. An author who has left the organization is no longer recognized as a member of it, whatever key once signed for them.
3. **The receiving account's address is in the signed `To` or `Cc`**,
4. **or the message is a blind copy the deployment itself sent to it.** The deployment's `OutgoingEmailRecord` whose `Message-ID` is the signed `Message-ID`, sent through the account whose address is the signed `From`, lists the receiving address as a `Bcc` recipient.

Conditions 3 and 4 are what refuse a replay. A validly signed message forwarded or resent, byte for byte, to somebody it never named still verifies cryptographically. It is not trusted, because it was not sent to them.

The cost of that rule is stated rather than hidden: a message reaching a member through a mailing list, an alias, or a redirect names the list or the alias rather than the member, so it is not trusted by this source. It is judged by the other sources as it is today.

Condition 4 needs the outgoing record to carry, indexed, the `Message-ID` of the MIME it was enqueued with. Today only a filing records one. Issue 2718, which is what reads it, adds that column with an additive migration and fills it when `MailOutbox.EnqueueAsync` opens the record, so the verifying half does not wait on the signing half. A record whose MIME carried no `Message-ID` cannot satisfy condition 4. A record deleted by retention or erasure before the blind copy arrives grants nothing, which fails closed.

**A signature that fails, names no key of the organization, or names a revoked key contributes nothing.** It is never a warning on its own. Relays and transport rules legitimately alter messages — a disclaimer footer breaks the body hash, as the spike shows — and DKIM's own rule (RFC 6376 § 6.1) is that a signature that fails is treated as absent. An attacker gains nothing from writing one: the message is judged by every other source exactly as it would have been.

A receiving account in no organization never runs this verification.

### The model

**A new `SenderTrustSource` member, `OrganizationMember = 4`**, appended under the enum rule in `backend/AGENTS.md`.

**It is a trust fact, not a `SenderAuthenticationMethod`.** That enum answers which mechanism established the author's *domain*, and feeds `SenderAuthentication.AuthenticatedAuthorDomain`. An organization signature proves something else and something stronger: that a key this deployment holds for the receiving account's organization signed for this address. Folding it into domain authentication would show it in the popup's *Sender authentication* steps, which issue 2719 deliberately leaves unchanged. It would also have the policy reach it only through a domain, which is the shared-provider weakness this feature exists to remove.

**It ranks first.** `SenderTrustPolicy.Evaluate` takes the verified organization membership as an input of its own. Where it holds, the verdict is `Trusted` from `OrganizationMember` without the policy consulting the domain lists.

Two consequences follow:

- **It does not require `AuthenticatedAuthorDomain`.** A colleague on a self-hosted server that publishes no DKIM key is still proven, because the signature proves the address, which is more than a domain check ever did.
- **Ranking first is also what the reader needs.** A colleague on the organization's own domain would otherwise match `OwnAccountDomain` first, and the popup would name a domain where it could name the organization.

The other sources keep their order beneath it.

**The verdict stores the organization that recognized the sender**, as a nullable reference beside the stored trust source. The client shows that organization's *current* display name, as the owner decided. A receiving account that later moves to another organization does not relabel mail already stored, and an organization deleted since leaves the reason without a name.

That organization is also part of the receiving account's policy revision, so a verdict says which organization's keys it was reached under.

### Lifecycle

**Deleting an organization deletes its keys.** `OrganizationAdministration.DeleteAsync` already refuses to delete an organization that still has members or mail accounts, so by then nothing signs with its keys and no receiving account verifies against them. Verdicts already stored keep their source. Their reference to the organization is cleared with it, and the reason shows without a name.

**An account that moves between organizations, or leaves every organization, changes both halves from the move onward:**

- It signs with its new organization's key, or not at all.
- Its arrivals are verified against its new organization's keys, or not at all.
- Its former colleagues stop recognizing it by condition 2.
- A message it sent before the move but composed while it was a member still carries the old signature, and is judged at extraction like any other.

### Custody phases

**Signing is identical in `Mirrored`, `Held`, and `Restoring`.** The outbox signs and stores the MIME the same way in every phase, and the copy filed in the sent folder is the stored, signed bytes in each. A held account files it locally, and a mirrored or restoring one appends it to the source.

**Verification is identical in every phase.** It happens at extraction, and every phase extracts arrivals: ADR 0034 has a held source keep delivering new mail, which is extracted as an arrival before the drain takes it. A restore appends what MailFathom already holds back to the source and re-extracts nothing, so it re-evaluates nothing — consistent with mail already stored not being re-evaluated.

### Several replicas and a rolling upgrade

- **One active key per organization** is a database constraint, not a check before a write.
- **Rotation runs once per organization**, under a leased scope.
- **Signing reads the active key inside its own short transaction**, and holds nothing across the SMTP submission that follows.
- **During a rolling upgrade**, a replica of the older build composes unsigned mail and verifies nothing. Its arrivals are judged as they are today and never re-evaluated. That window is the cost of the upgrade, and nothing about it is unsafe.

### Consequences

- Good, because a colleague's message is proven per address rather than per domain. A shared provider's other customers, and a forged `From`, gain nothing.
- Good, because no recipient outside the organization sees anything it acts on. Their servers ignore the header, and the message is judged as it was before.
- Good, because a leaked key has an answer that does not depend on a timestamp the forger writes, and an idle organization's key still rotates.
- Good, because the cryptography is MimeKit's and the key handling is BouncyCastle's, both already pinned. No package is added.
- Neutral, because the header tells every recipient the sender runs MailFathom and that two messages share a key. The second adds little to what a shared `From` domain already says.
- Neutral, because a message reaching a member through a list, an alias, or a redirect is not trusted by this source. That is the replay refusal working as intended, and it falls back to today's judgement rather than to a warning.
- Neutral, because a deployment with organizations and no key ring signs nothing until it configures one, and says so on a counter rather than refusing to start.
- Bad, because verifying a signature under a name `DkimVerifier` refuses depends on rebuilding the header in memory. A MimeKit release that changed how the signature header itself is canonicalized would break it. The adapter's unit tests sign and verify through the rename, so a pin move that breaks it fails the suite rather than production.
- Bad, because a transport rule that appends a disclaimer breaks every signature that crosses it. An organization whose server does that gets no proof for its own mail until the rule is changed. No `l=` is the deliberate price.

## Validation

- **Issue 2716** tests the store's one-active-key constraint against a second replica losing the creation race, rotation and revocation against `TimeProvider`, and that no key material reaches a log, a metric, an endpoint, or a command's output.
- **Issue 2717** tests:
  - that a message from an organization's account carries exactly one `MailFathom-Organization-Signature` and no `DKIM-Signature` of MailFathom's;
  - that the signature verifies against the organization's public key through the rename;
  - that a promoted draft is signed like a composed message, and a draft that is only stored is not;
  - that an account in no organization signs nothing;
  - that the sent copy is byte-identical to the stored MIME in each custody phase.
- **Issue 2718** tests each of the four conditions failing alone, a key of another organization, a revoked key, a retired key, a message altered after signing (not trusted, no warning), and a receiving account in no organization.
- Review holds a change to the header name, the tag set, the algorithm, or the trust conditions against this record. Changing any of them is a new ADR.

## Pros and Cons of the Options

### The header: `DKIM-Signature`

- Good, because `DkimVerifier` takes it directly, with no rename.
- Bad, because every receiving server tries to verify it, finds no key, and records a failure some filters score against the message.
- Bad, because `d=` on a shared provider's domain makes a claim on that provider's behalf.
- Bad, because the deployment's own local DKIM verifier would query DNS for it on every arrival and spend a signature slot on it.

### The header: `MailFathom-Organization-Signature` carrying a DKIM value

- Good, because nothing outside the deployment reads it, and recipients' servers judge the message as before.
- Good, because it keeps DKIM's canonicalization and MimeKit's implementation.
- Bad, because verification rebuilds a `DKIM-Signature` header in memory, which `DkimVerifier` does not offer itself.

### The header: an S/MIME or OpenPGP signature

- Good, because the formats exist to sign a message's author.
- Bad, because both wrap the body in a `multipart/signed` structure every recipient's client renders. An outsider sees an attachment or a signature badge for a key nothing they hold can check, which breaks the third decision driver outright.
- Bad, because the existing S/MIME and OpenPGP verification (issue 1588) would read it as an author signature it cannot trust, and report it as *not verifiable here*.

### The algorithm: Ed25519-SHA256

- Good, because its keys and signatures are small and deterministic, and it has no size parameter to weaken.
- Neutral, because RFC 8463's interoperability gap does not apply when the only verifier is this deployment.

### The algorithm: RSA-SHA256 at 2048 bits

- Good, because every DKIM implementation supports it.
- Bad, because nothing here needs that, and it costs an eight-times-larger key and signature, plus a size a later change could lower.

### The rotation interval: the settings policy of ADR 0037

- Good, because an organization could set its own.
- Bad, because the policy governs users' records and mail accounts. A key is neither, and the interval is the operator's security posture rather than a person's setting.

### The rotation interval: deployment configuration

- Good, because it is one value, validated at start, next to the key ring it depends on.
- Neutral, because every organization in a deployment rotates on the same interval.

## More Information

- Issue 2714 is the parent. Issues 2716 (the keys), 2717 (signing), 2718 (verifying), 2719 (the design), and 2720 (the client) implement this record.
- [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) seals the private half. [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) bounds an account to one organization. [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) leases the rotation. [ADR 0034](0034-holding-a-mailbox-mailfathom-alone-keeps.md) and [ADR 0039](0039-one-folder-model-and-one-implementation-per-custody-phase.md) define the custody phases. [ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md) makes the selector safe to publish. [ADR 0037](0037-a-settings-policy-at-the-deployment-and-at-each-organization.md) is the policy the interval stays out of.
- RFC 6376 (DKIM), RFC 8463 (Ed25519 for DKIM), and RFC 6648 (deprecating the `X-` prefix).
- Revisit this record if MimeKit publishes a verifier that takes a signature under another header name — the in-memory rename then goes — or if the deployment ever needs to prove an organization's mail to a verifier it does not run, which would make publishing a key the question again.
