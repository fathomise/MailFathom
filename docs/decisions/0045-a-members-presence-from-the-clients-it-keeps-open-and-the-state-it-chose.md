---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-31
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Work out a member's presence when it is read from a row each open client renews and the one state the member chose, hold no history of it, tell only the member's own clients that it changed while colleagues read it on their refresh, and let an expiry rather than a replica end it

<!-- describes: backend/src/Host/Api/ColleagueResponses.cs, backend/src/Domain/Presence/**, backend/src/Application/Presence/**, backend/src/Infrastructure/Persistence/Presence/**, frontend/src/Client.App/src/presence/** -->

## Context and Problem Statement

A member contact ([ADR 0044](0044-an-organizations-member-directory-as-a-projection-of-user-records.md)) says who a
colleague is, not whether they are around. Issue 2793 adds presence: one of six states — `Active`, `Away`, `Busy`,
`Do not disturb`, `Hidden`, `Offline` — shown to the other members of the organization on the member contact and in
every list that shows a member, with *Last seen* for a member who is `Offline`. Issue 2809 drew it: the dot on a
member's avatar, *Last seen* on the contact, and the status menu under the account icon with its *Duration* page.

Three of the states are worked out from the member's clients, and all six can be chosen. ADR 0044 left where
presence is stored and how it expires to this record. It has to answer:

- what "the client is open" is, when the replica holding a client's signal connection can die without anybody
  clearing anything, and when the state is read on a replica that holds none of that member's connections;
- how `Away` is measured, and how several devices combine;
- how a chosen state, its duration, and *Last seen* are held, and how the six resolve into the one a reader sees;
- how a change reaches the members looking at it;
- what is held at all, given that presence is monitoring of an employee.

## Decision Drivers

- **Several replicas.** No state a guarantee rests on lives in one process's memory, and a replica that dies stops
  counting as anybody's presence on its own ([ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md)).
- **The hub stays one-directional.** `ClientSignalHub` declares no method, so that everything a client says reaches
  the deployment over a route where its permission is judged and its request bounded
  ([ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md)).
- **The backplane keeps nothing.** ADR 0032 lets it carry client signals and forbids anything being read back from it.
- **Privacy by default.** Presence is a fact about an employee's working day. Only the current state and one timestamp
  are held: no history, no log line, no metric, and no trace naming a person.
- **Honest absence.** Closing every client reads as `Offline`, whatever the member chose, so nobody looks present who
  is not.
- **One design.** The rules match what issue 2809 drew, and leave a seam for issue 2798's calendar-derived `Busy`.

## Considered Options

For where "the client is open" lives:

- **A row per open client, renewed by the client over an HTTP route, and expired by its own clock.**
- **A row per signal connection, written by the hub and renewed by the replica holding the connection.**
- **Each replica's own memory, with the backplane spreading it.**
- **A key with a time-to-live in the RESP endpoint the backplane already uses.**

## Decision Outcome

Chosen option: **a row per open client, renewed by the client over an HTTP route, and expired by its own clock**,
because it is the only option in which nothing has to happen for a dead client or a dead replica to stop counting, it
keeps the hub method-free and the backplane stateless, and it needs nothing a deployment does not already run.

### The six states, and the one a reader sees

Presence is worked out when it is read, from three inputs: the member's open clients, the state they chose, and the
reader. Nothing stores the result.

1. **No open client — `Offline`.** Whatever the member chose. The choice is kept, and applies again when a client
   opens, unless its duration ran out meanwhile.
2. **A chosen state that has not run out — that state.** Any of the six can be chosen, `Offline` and `Hidden`
   included, so a member can look absent while working.
3. **Otherwise, the most present client — `Active` when any client is active, `Away` when every one is idle.**
   Across devices the most present wins: `Active`, then `Away`, then `Offline`.
4. **`Hidden` is seen by everybody else as `Offline`, and by the member as `Hidden`.**

Issue 2798's calendar-derived `Busy` and `Do not disturb` are a fourth input between steps 2 and 3, and nothing here
has to change for it.

**Choosing `Active` until changed is not a choice. It returns the member to automatic presence.** Issue 2809's status
menu has no *Reset* row. A state picked from it lasts until changed, so without this rule a member who once picked any
state could never get back to the presence their clients produce. `Active` is the one state the automatic rules
already produce for somebody at their keyboard, so picking it clears the choice rather than pinning `Active` on a
member who then walks away. With a duration, `Active` is a choice like the other five, and it holds `Active` through
idleness until the duration runs out.

### An open client is a row its client renews

Each running client mints an identifier for itself when it starts, as a version 7 UUID
([ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md)). While it is
open it renews a row keyed by its user and that identifier:

- **The route is `PUT /api/client/presence/clients/{client}`, carrying `active` or `idle`.** The user comes from the
  session and never from the request, so an identifier one person guesses names nothing of another's. The row records
  the user, the client, the activity, and an expiry. Nothing else is recorded: no address, user agent, device kind, or
  replica.
- **The client renews every 60 seconds, and a row expires 180 seconds after its last renewal.** The expiry is three
  intervals, so one late or lost renewal does not flicker a member to `Offline`. A browser throttles the timers of a
  hidden tab to about one a minute, which the interval survives. Both are constants rather than settings, because
  nothing an operator knows about their deployment changes how fast a person should be seen to leave.
- **A client that closes says so.** On `pagehide`, and on sign-out, it sends `DELETE` for its row as a `keepalive`
  request, so a closed window reads as `Offline` at once. That is the ordinary case. The expiry is what covers a
  crash, a lost network, a suspended phone, and a request that never arrived, and nothing may be built on the delete
  having happened.
- **Every read filters out expired rows, and they are deleted opportunistically.** A renewal deletes its own user's
  expired rows. At most once per expiry period, a replica deletes a bounded batch of expired rows across the
  deployment, as the ticket store already sweeps its own table. Nothing has to run for an expired row to stop
  counting.
- **A member holds at most sixteen open clients.** A renewal that would add a seventeenth is refused. That bounds the
  table and the per-member read, and no person has sixteen windows open.

**This is why a dying replica needs no handling.** No row belongs to a replica. A client's next renewal lands on
whichever replica the load balancer picks, and a client that cannot reach any of them stops renewing and expires. So
presence survives a replica dying for a client that is still open, and ends for a client that is gone, and neither
depends on anybody noticing.

### `Away` is measured by the client, from input

The client watches its own input: pointer, key, touch, and wheel events on its document. It counts itself idle once
ten minutes pass with none. It reports `idle` on its next renewal, and it renews at once when input returns, so
returning to the keyboard turns a member `Active` within a second rather than within a minute. A hidden window
receives no input, so a tab left in the background turns `Away` on the same rule, and nothing has to treat visibility
separately.

The server cannot measure this. It sees requests, and a client renewing, re-reading on its five-minute safety net, or
answering a signal makes requests whether a person is there or not. Only the client knows whether a person touched it.

### The chosen state, its duration, and *Last seen* are one row per member

`member_presence` holds one row per member, overwritten in place: the chosen state or none, the while it was chosen
for and the instant the choice ends or none, and the instant the member was last seen. It is not a property of the user record. ADR 0037's policy does not
govern it, because an administrator never sets somebody's presence (ADR 0044). It is a separate row because it is
written on every renewal, and the record is written by people. The row cascades with the user, so erasing a user
erases it.

- **A choice travels in one request with its duration**: `PUT /api/client/presence/choice`, carrying the state and
  `30m`, `1h`, `2h`, `today`, `week`, or nothing for *until changed*. The *Duration* page's *Done* sends both
  together, and a state picked from the status menu's list sends no duration, which ends any duration set before.
- **The deployment resolves the duration to an instant, in the member's own time zone.** `today` ends at the next
  local midnight, and `week` at the midnight that ends the local Sunday, ISO 8601's week. A member whose record states
  no zone is resolved in UTC. The deployment resolves it, rather than the client, so a member on two devices in two
  zones gets one answer, and it is the answer the member's contact shows colleagues as their local time.
- **The duration is kept beside the instant it resolved to.** The status menu names the while a member picked —
  *Busy · 1 hour* — and an instant cannot say which while produced it, so the row keeps both and the member's own
  reading answers both. Colleagues are told neither: they read the state alone.
- **A choice that runs out is not cleared by anybody.** A read compares the instant with the time of the read, so the
  member is back to automatic presence when it passes, whether or not any process is running.
- ***Last seen* is one timestamp, moved forward by every renewal.** It is never moved while the member's chosen state
  is `Hidden` or `Offline`, because a timestamp that kept moving would tell anybody watching it that the member is
  present after all. A reader sees *Last seen* for a member who is `Offline`, and never for one who chose `Hidden`,
  whose last-seen time would give away the same thing.

### Who reads it

- **Members of the same organization, and nobody else.** Presence and *Last seen* are added to `ColleagueResponse`, so
  every list and contact that already reads colleagues reads presence in the same query, under the same
  `ContactsRead` grant. A user in no organization has no colleagues. Every presence route answers them `404` and
  records nothing, so a deployment holds no presence for somebody nobody can see, and their client stops renewing.
- **The member reads their own**: `GET /api/client/presence` answers their state as they see it, `Hidden` included,
  with their choice and when it ends. That is what the dot on the rail avatar and the status menu draw.
- **MCP never reads it.** ADR 0044 already keeps every availability fact on the client endpoint alone, and this record
  adds no tool.
- **An administrator never reads it.** No administrative route or `mfctl` command answers it. A deployment's
  operator can read the two tables, as they can read everything else in the database, and the personal-data
  documentation says so.

### A change reaches the member's own clients, and colleagues read it on their refresh

A client signal kind, `presence.changed`, tells the member's own clients that how the other members see them may have
changed, and carries nothing at all: no member, no state, and no timestamp. The deployment publishes it to the member's
own user group alone, so the member's other devices re-read the member's own presence — a state chosen on the laptop
shows on the phone at once. It crosses replicas over the backplane exactly as every other signal addressed to one user
does.

**No other member is told.** Every client already re-reads what it draws every five minutes while its window is
visible (ADR 0032), and the colleague screens re-read their colleagues' data and presence on that refresh, every row the
reader holds included. A signal to every member of the organization would cost one send per member for each change —
over the hub, over the backplane, and in the per-replica folding every other kind of signal shares — and every client
would answer each one with requests, so in a large organization presence would crowd out mail signals and flood the
backplane. So one member's change costs one signal however large the organization, and reaches colleagues within five
minutes.

The signal carries no state for the reason it never did: what a reader may see — `Offline` for `Hidden`, *Last seen*
withheld — is decided in one place, the read.

It is published when a write changes what the other members see: a renewal that opens the member's first client or
moves them between `Active` and `Away`, a delete that closes their last client, a choice, and an automatic reply saved
so that the member starts or stops reading as out of office now. **A change that no write causes is not signalled.** A
client that crashed expires, and a duration runs out, and no request marks the moment. The member's own client re-reads
at the instant its choice ends, because it knows the instant, and every other reader catches up on its refresh, as it
does for every change.

### What is held, and what never is

| Held | Where | How long |
|---|---|---|
| An open client: the user, the client's own identifier, `active` or `idle`, an expiry | `presence_clients` | 180 seconds after the last renewal, then deleted opportunistically |
| The chosen state, the while it was chosen for, and when it ends | `member_presence` | until changed or run out, overwritten in place, erased with the user |
| When the member was last seen | `member_presence` | overwritten on each renewal, erased with the user |

- **No history.** No previous state, no earlier timestamp, and no record of when a choice was made is kept anywhere.
  Each row holds the present and is overwritten by it.
- **Nothing names a person.** No log line, metric dimension, trace attribute, or failure message carries a member's
  identifier together with their presence, their activity, or a renewal. A renewal is the most frequent request the
  client makes, so this is stated rather than assumed. The presence routes record no audit entry, because they change
  no mail and an entry per renewal would be exactly the history this record refuses. Counters may count renewals and
  open clients for the deployment as a whole.
- **The backplane carries no presence.** `presence.changed` names nobody and travels in the member's own user group,
  whose name carries their identifier the way every signal addressed to one user already does, under ADR 0032's
  confidentiality requirements, and stores nothing.

The personal-data documentation and `docs/features/contacts.md` state what is tracked, who sees it, and how long it
lasts, in the change that builds it.

### Breaks this records

None. Two tables arrive in an additive migration. The client endpoint gains three routes, a signal kind, and two fields
on `ColleagueResponse`. No configuration key, MCP tool, or deployment asset changes.

### Consequences

- Good, because a dead client and a dead replica both stop counting with nothing written, so presence is right in a
  deployment of any size without a lease, a sweep anybody depends on, or affinity.
- Good, because the hub stays method-free and the backplane stays a pipe, so neither ADR 0032 boundary moves.
- Good, because the visibility rules live in one read, so `Hidden`, *Last seen*, and the organization boundary cannot
  disagree between a list, a contact, and a signal.
- Good, because what is held is the present alone, and it is erased with the user.
- Good, because a change costs one signal to the member's own clients however large the organization, so presence
  never crowds out mail signals, fills the per-replica folding, or floods the backplane.
- Neutral, because a renewal a minute for each open client is new traffic: one small write per client per minute,
  against a surface that already re-reads whole screens every five minutes.
- Neutral, because `Active` until changed is the way back to automatic presence rather than a choice, which the
  status menu has no other way to express.
- Bad, because every change to a colleague's presence reaches a reader on their next refresh, up to five minutes
  later, rather than at once.
- Bad, because a client the operating system suspends without `pagehide` — a phone putting a web view to sleep —
  shows its member present for up to three minutes after they left.
- Bad, because the client's own measure of idleness is trusted: a client can report itself `active` forever. That is
  no more than choosing `Active` for a duration already allows, so nothing is protected by distrusting it.

## Validation

- Unit tests over the resolution: the six states, the precedence across devices, a chosen state and its duration
  running out, *Until changed*, `Active` until changed returning to automatic, closing every client reading as
  `Offline` over any choice, and what a reader other than the member sees for `Hidden`.
- Unit tests over *Last seen*: moved by a renewal, held still under a chosen `Hidden` or `Offline`, shown for
  `Offline`, and withheld for `Hidden`.
- Unit tests over expiry and the losing replica: a row past its expiry counts as no client, with no replica, sweep, or
  delete involved.
- Unit tests over the duration's resolution in a member's time zone, at a day's and a week's end, and in UTC where the
  record states none.
- Unit tests over the client's idleness at ten minutes, its immediate renewal when input returns, and its `keepalive`
  delete on `pagehide`.
- Unit tests over the signal's addressing: a change reaches the member's own user and no colleague, and a write that
  changes nothing the others see raises nothing.
- Unit tests over the client's refresh of a long directory: every colleague held is read again, past what one page
  holds.
- Review against this record for any log, metric, trace, or audit entry naming a person's presence.

## Pros and Cons of the Options

### A row per open client, renewed by the client over an HTTP route

- Good, because the row's own clock ends it, so nothing about a replica, a connection, or a process has to be noticed.
- Good, because the client is what knows both facts presence needs — that it is open, and whether a person is using
  it — and it says so over a route that is bounded, authorized, and ordinary.
- Good, because it works whether or not the signal connection stands: a client behind a proxy that refuses the
  WebSocket upgrade still counts as present.
- Bad, because it adds a periodic request per open client.

### A row per signal connection, renewed by the replica holding it

The hub writes a row in `OnConnectedAsync` and deletes it in `OnDisconnectedAsync`, and each replica renews the rows of
the connections it holds.

- Good, because it adds no request: the connection is already open.
- Bad, because it still needs activity from the client, and the hub has no method to carry it, so either the hub gains
  its first method or a route carries activity beside a connection it cannot name. The JavaScript client has no
  connection identifier when negotiation is skipped.
- Bad, because a replica renewing rows on behalf of connections is singleton-shaped work per replica, and a renewal
  loop that falls behind under load shows people leaving who did not.
- Bad, because a client whose WebSocket cannot stand is never present.

### Each replica's own memory, with the backplane spreading it

- Good, because it writes nothing to the database.
- Bad, because a replica that dies takes its members' presence with it and nobody else knows they are gone, which is
  exactly what issue 2793 refuses.
- Bad, because a replica that starts knows nothing until every other one has spoken, so the backplane would become
  something read back, which ADR 0032 forbids.

### A key with a time-to-live in the RESP endpoint

- Good, because expiry is native to the store.
- Bad, because the backplane is optional, so presence would need a second implementation for every deployment that
  runs none.
- Bad, because ADR 0032 holds that nothing reads back from the backplane, and turning it into a store of personal data
  would need a record of its own, with a retention, export, and erasure story the backplane does not have today.

## More Information

- Issue 2793 builds this record. Issue 2794 adds the status message and work mode beside it, issue 2798 the
  calendar-derived `Busy` and `Do not disturb`, and issue 2795 the phone-app marker, which is derived from sessions
  and not from this table.
- Revisit the five-minute delay if the people reading presence find it too long. A signal that reaches colleagues
  again would have to be addressed to the readers actually drawing the member rather than to the whole organization,
  so that its cost stays independent of the organization's size.
