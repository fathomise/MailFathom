# Organization signing keys

<!-- describes: backend/src/Application/Access/Organizations/SigningKeys/**, backend/src/Infrastructure/Security/SigningKeys/**, backend/src/Infrastructure/Persistence/Users/*OrganizationSigningKey*.cs, backend/src/Infrastructure/Persistence/Users/PersistedOrganizations.cs, backend/src/Infrastructure/Observability/*OrganizationSigningKey*.cs, backend/src/Host/Api/OrganizationSigningKey*.cs, backend/src/Host/Hosting/Workers/OrganizationSigningKeyRenewalWorker.cs, backend/src/Host/Configuration/Access/OrganizationSigningOptions.cs, backend/src/Cli/Commands/Organizations/*SigningKey*.cs, backend/src/Cli/Administration/Organizations/OrganizationSigningKeys.cs, backend/src/Application/Mail/Delivery/Signing/**, backend/src/Infrastructure/Mail/Dkim/OrganizationMailSigner.cs, backend/src/Infrastructure/Persistence/Users/PersistedAccountSigningKeys.cs -->

Every [organization](admin-endpoint.md#organizations) holds an Ed25519 signing key: the key its accounts' mail is
signed with, so that a receiving account of the same organization can tell a colleague's message from somebody writing
their name. **Every message an organization's account sends is signed with it** — *What is signed, and when* below —
**and a receiving account of the same organization verifies an arriving signature against these keys.**
[Sender authentication](../features/sender-authentication.md#a-colleague-in-the-receiving-accounts-organization) states
what a verified signature establishes and which key states it accepts.
[ADR 0042](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0042-signing-an-organizations-mail-and-trusting-a-sender-its-signature-proves.md)
records how a signature is written and what it must hold before it grants trust; this page is about the keys themselves:
where they come from, what they sign, how long one is used, how one is withdrawn, and what an operator owes them.

Nobody creates a key. The deployment mints one for every organization on its own, rotates it on a schedule, and keeps
the public half of every key it ever minted. What an operator decides is the rotation interval, whether to rotate or
revoke one by hand, and — before any of it can happen — whether the deployment has a key ring to seal the keys under.

## What a key is

| Part | What it holds |
| --- | --- |
| Identifier | A version 7 UUID minted with the key. It is the selector a signature names, so it is not a secret and is what every command and route names the key by |
| Public half | The 32-byte Ed25519 public key, stored in the clear and kept for as long as the organization exists |
| Private half | The 32-byte private key, sealed under [the data-encryption key ring](configuration-runtime.md#dataencryption) with the purpose `organization-signing-key` and the key's own identifier as its subject, so a sealed value moved onto another key's row does not open. It is deleted the moment the key stops being active |
| Created, retired, revoked | When the key was minted, when a newer one replaced it, and when an operator revoked it |

A key is in exactly one of three states. **Active** is the one key an organization signs with, and the database holds
at most one active key per organization under a unique index rather than trusting every replica to agree. **Retired**
is a key a rotation replaced: its private half is gone, and its public half stays so mail signed under it while it was
active is still verified. **Revoked** is a key an operator withdrew: nothing it signed is trusted from then on,
whenever that mail claims to have been written. A verdict already stored stays until [stored mail is
re-derived](../features/sender-authentication.md#re-deriving-what-is-already-stored), which then leaves none the
revoked key reached.

The organization's name is never stored on a key. A key reaches its organization through the store, and a listing names
the organization by the display name it holds now. Removing an organization removes its keys with it.

## Where a key comes from

**An organization recorded on a deployment with a key ring is recorded with its key**, in the same transaction, so
there is no moment at which the organization exists and holds nothing to sign with.

**Every other organization is given one by an hourly pass.** Each replica runs a pass an hour after it starts and every
hour after that, looks for organizations with no active key or with an active key older than the rotation interval, and
gives each a fresh one — **at most a hundred per pass**, taken in identifier order, so a deployment with more due at once
works through them a hundred an hour on one replica rather than within one hour. A replica whose pass falls at a
different point in the hour takes the next hundred still due; replicas started together run their passes together, read
the same hundred, and share it, so more replicas hurry a backlog only as far as their passes are spread across the
hour. That covers an
organization recorded before the deployment had a key ring, one recorded before the release that introduced these keys,
and one whose active key was revoked while no replacement could be sealed.

The pass takes [a lease](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) per organization, so two replicas never renew one
organization at once, and the unique index stands behind the lease: whichever of two writers lost — two replicas, or
the pass and an administrator rotating by hand — reads back the key the winner minted instead of minting a second.

## What is signed, and when

**Every message sent from a mail account that belongs to an organization is signed with that organization's active
key**, whoever its recipients are. A message is signed once, when the outbox first stores it as a send — a composed
message, a reply, each occasion of a recurring send, and a draft promoted to a send alike — and the stored, signed bytes
are what every submission attempt sends and what is filed into the sent folder, so the sent copy is byte for byte the
message the recipients received, whether the account is mirrored, held, or restoring. A draft that is only saved is
never signed: it is not sent. A send asked for again with the same identity keeps the bytes it stored the first time.

The organization is the one the sending account belongs to when the message is stored, and the key is that
organization's active key at that moment, so a scheduled send is signed when it is queued rather than when it leaves.
**An account in no organization signs nothing.**

The signature is one header added above the message, `MailFathom-Organization-Signature`, carrying a DKIM signature
value (RFC 6376) made with Ed25519-SHA256 and relaxed canonicalization. `d=` is the domain of the message's author, in
its ASCII form, and
`s=` is the key's identifier; it signs `From`, `Reply-To`, `To`, `Cc`, `Subject`, `Date`, `Message-ID`, `In-Reply-To`,
and `References`, each listed twice so a second instance inserted later breaks it, and carries no expiry and no body
length. Nothing already in the message is changed or removed, and MailFathom writes no `DKIM-Signature` of its own:
every server ignores a header it does not know, so recipients outside the organization judge the message exactly as
before, and a provider that signs the message after MailFathom submits it signs beside it. Nothing identifying the
account or the organization is written beyond what the signature carries. Every message is sent
[seven-bit](../features/mail-delivery.md), which is what keeps the signature intact past a relay that would re-encode
an eight-bit body.

**A send is never refused for want of a signature.** Where the organization holds no active key — no key ring, a mint
that has not run yet, or a sealed private half the ring can no longer open — the message leaves unsigned and
[`mailfathom.organization.mail.unsigned`](telemetry.md#organizations-left-without-a-signing-key) counts it. So does a
message composed within a few hundred bytes of
[`MailDelivery:MaxMessageBytes`](configuration-mail.md#maildelivery), which the signature header would carry past the
bound: the bound is the operator's ceiling on what is stored and sent, and the signature never stretches it.

## Rotation

An active key is replaced once it is older than
[`OrganizationSigning:KeyRotationInterval`](configuration-runtime.md#organizationsigning), a year by default. The pass
that finds it mints the new key, retires the old one, and deletes the old private half in one transaction, so a crash
between the steps leaves either the old key active or the new one, never both and never neither. The interval is held
between one day and a hundred years; the ceiling is how a deployment that wants no scheduled rotation says so.

A rotation is due when the key's age passes the interval and happens on a later hourly pass, so a key lives up to an
hour past the interval — longer when more than a hundred organizations fall due together, since each pass replaces a
hundred, and after a replica starts, since its first pass waits an hour. Nothing is published anywhere: the keys are read from the store they are written to,
so a new key is the organization's from the moment it commits.

To rotate ahead of schedule — after a key ring change, or because an operator wants a fresh key now — run:

```console
mfctl organization signing-key rotate --organization <id>
```

It answers with the new active key and the one it retired. A rotation needs `mailfathom.admin.credentials.write` over
the whole deployment, because a key belongs to every member of the organization at once rather than to any one scope
inside it, and it is recorded as an audit entry in the log naming the organization, both keys, who asked, and when.

## Revocation

Revoke a key when its private half may have been exposed:

```console
mfctl organization signing-key list --organization <id>
mfctl organization signing-key revoke --organization <id> --key <id>
```

The command asks before it acts, because a revocation is not undone: a revoked key stays revoked for good. `--yes`
answers the question for a script.

**Revoking the active key replaces it in the same transaction.** The private half is deleted, a fresh active key is
minted, and the command names it, so the organization is never left without a key for longer than the act takes.
**A revocation never waits on the key ring**: where no replacement can be sealed — no ring is configured, or its active
key cannot be resolved — the key is revoked all the same, the command says that no replacement was minted, and the
hourly pass mints one once it can. Revoking a retired key only marks it, since it has no private half left to delete,
and mints nothing. Revoking a key already revoked is refused and changes nothing, and so is a key the organization does
not hold.

Revocation takes the same grant as rotation and is audited the same way. The listing takes `mailfathom.admin.read` at
any scope covering the organization, and reports each key's identifier, state, and dates — never key material, public
or private. It is read a page at a time, oldest key first, and `mfctl organization signing-key list` follows the pages
to the end, so every key the organization ever held can be found and revoked however many rotations it has been
through.

## A deployment with no key ring

The private half of a key is sealed under the key ring, so **a deployment that configures no `DataEncryption` mints no
signing key**. It is still a supported deployment: organizations are recorded exactly as before, they simply hold
nothing to sign with.

- Recording an organization records it without a key.
- Its accounts' mail leaves unsigned, and each message is counted in
  [`mailfathom.organization.mail.unsigned`](telemetry.md#organizations-left-without-a-signing-key).
- The hourly pass finds such organizations, a hundred at most per pass, mints nothing, logs one warning naming how
  many it could not serve, and counts each in
  [`mailfathom.organization.signing_key.unminted`](telemetry.md#organizations-left-without-a-signing-key).
- `mfctl organization signing-key rotate` is refused with `409`, naming `DataEncryption` as what to configure.
- Revoking an active key still revokes it — withdrawing trust never waits on a key ring — and the command says that no
  replacement could be minted.

Configuring a key ring is the whole fix. The passes that follow mint every missing key, at least a hundred an hour, and nothing
about the organizations has to change.

## Rotating the key ring under signing keys

A signing key's private half is sealed under the ring key that was active when the signing key was minted, so the
existing rule for retiring a ring key applies to it as it does to every sealed value: **keep the old ring key
configured** until nothing sealed under it remains. Every signing key minted after `ActiveKeyId` moves is sealed under
the new ring key, and retiring or revoking a signing key deletes its sealed half outright, so the signing keys move off
an old ring key on their own as they rotate. To move them sooner, rotate each organization's key by hand once the new
ring key is active. A ring key removed while an active signing key is still sealed under it does not stop mail: that
organization's messages leave unsigned and are counted in `mailfathom.organization.mail.unsigned` until its key is
rotated. [Secret rotation](secret-rotation.md) holds the rest of what a ring rotation owes.
