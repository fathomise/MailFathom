---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-11-05
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Keep a mail account's rules as rows of that account in PostgreSQL, address each by a generated identifier, commit every change against the set version it read, archive every committed set as the revision a run resolves against, and reach rules only over the administrative endpoint and the client API

<!-- describes: backend/src/Application/Rules/*.cs, backend/src/Application/Rules/Evaluation/MailRuleScheduleSource.cs, backend/src/Application/Rules/History/MailRuleHistoryRetention.cs, backend/src/Host/Configuration/Rules/**, backend/src/Host/Api/MailRuleEndpoints.cs -->

## Context and Problem Statement

[ADR 0010](0010-rule-authoring-in-configuration-and-ncalc-conditions.md) decided two things together: that a rule set is a section of the deployment's configuration, and that a rule's condition is one NCalc expression. It refused a rules table outright and foreclosed runtime rule management, and it named the condition that would reopen the question: a user who genuinely needs to change a rule without changing their configuration, or a rule authored by somebody who is not the operator of the instance. [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md) then made one deployment serve several users, moved mail accounts, users, and credentials out of configuration into PostgreSQL, and kept rules where ADR 0010 put them, settling only what a rule naming an account reaches.

Both reopening conditions now hold. A deployment serves people who are not its operator, a mailbox is shared by everybody it is assigned to, and the client is to offer a person the rules of their mailbox to change on its Settings screen (issue 2997). On 2026-11-04 the owner reversed the storage half of ADR 0010: **a rule belongs to one mail account and is kept only in PostgreSQL**, changed while the deployment runs from `mfctl` through the administrative endpoint and from the client API by a person the account is assigned to. On 2026-11-05 the owner added that **no MCP tool reads or changes a rule**. Issue 771 carries the feature and issue 761 asks for this record, so that the storage, the identity, the concurrency, the revision, the permissions, and the upgrade are settled once, before issues 762, 763, 2996, and 764 build on them.

The condition half of ADR 0010 stands unchanged and is not reopened here: NCalc as the one condition language, the closed fact set and function surface, the totality classification, the action safety model, and the bounds issue 454 built.

## Decision Drivers

- **One mailbox is one copy.** ADR 0014's driver applies to rules as it applies to folders and flags: a rule that files mail moves it for everybody who reads the mailbox, so a rule is a property of the mailbox rather than of whoever wrote it.
- **An erased account takes everything about it with it.** A rule names folders and carries text its author typed, so it is data of the account, reached by the account's erasure and export without a list to remember.
- **One source, never two.** ADR 0010 rejected a configuration seed beside a live database copy because the two diverge on the first edit; that argument holds against every shape that keeps any part of a rule in configuration.
- **Several writers, several replicas.** A person in the client, a second person assigned the same mailbox, and an administrator in `mfctl` can edit one account's rules at once, on different replicas ([ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md)). A lost update on an ordered list silently changes which rule runs first.
- **An identity every surface addresses alike.** A position moves when a rule above is removed, and a name moves when a person renames a rule; neither can be what an API, a schedule, or a history row holds on to.
- **A past run stays explainable.** Issue 458's history names the revision an execution ran under, and an execution's idempotency identity contains it. Once a rule can be edited or removed at runtime, what a revision *said* has to outlive the edit, because no configuration repository keeps it any more.
- **Changing a rule is a capability of its own.** Reading mail, moving mail, and deciding what happens to mail that has not arrived yet are different acts, and [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) publishes a name wherever an operator would plausibly grant two acts apart.
- **Nothing new on the logging and error paths.** A condition can contain an address and a folder path can contain a name, so neither reaches a log, a metric, or a generic error, which is already the rule for the configured form.

## Considered Options

Where a rule lives:

1. **Three tables keyed by the account**: one row per account's rule set carrying its version, one row per rule, and one append-only row per committed write.
2. **One JSONB document per account** holding the whole ordered set, with a version column beside it.
3. **A writable configuration layer** writing rules into the owner's settings document, which issue 771 carried before it was rewritten.
4. **Configuration as a seed and the database as the live copy**, which ADR 0010 already weighed and refused.
5. **Keep ADR 0010's configuration section.**

How a write is protected against a concurrent one:

1. **Every write states the set version it read**, takes the set's row lock, and is refused when the version moved.
2. **A version per rule**, so that two writes to different rules both commit.
3. **Last writer wins.**

## Decision Outcome

Chosen: **three tables keyed by the account, every write committed against the set version it read under the set's lock, every committed write archived as one append-only row, and rules reached only over the administrative endpoint and the client API.**

The tables win because each of the three things a rule needs — the set's version and lock, a rule's own identity and position, and a durable record of every committed set — is a row with a shape of its own, and folding them into one document would make the version, the identity, and the uniqueness of a name each a property MailFathom checks by hand rather than one PostgreSQL enforces. A whole-set version wins over a version per rule because a rule set is ordered and stop-when-matched: two edits to two different rules are still two edits to what the set means, and merging them is exactly the silent change of meaning the drivers forbid.

### A rule is the account's own, and everybody assigned the account shares it

A rule belongs to exactly one mail account and is stored under it. **It names no account**, because the account it belongs to is its whole scope. That retires the `Accounts` selector, the reading of an empty selector as *every account*, and the check that a rule names only accounts its author may reach. A rule meant for two mailboxes is two rules, one in each.

Every user the account is assigned to reads and changes the same rules, and that is ADR 0014's trade-off for a shared mailbox stated once more rather than a new one: the mail is one copy, so a rule filing it files it for all of them, and per-user rules over one copy would disagree with each other and with the server about where a message is. An operator who wants two people to handle a mailbox differently is describing two mailboxes.

**A rule is not its author's, the way a recurring send is.** ADR 0014 keeps a recurring send on its author and removes it when they are erased, because it is a message sent in that person's name. A rule sends nothing in anybody's name; it is a standing decision about what happens to the mailbox's mail, like the folder mapping beside it, so erasing the person who wrote it leaves it in place for everybody still assigned the mailbox. Who wrote each version is kept in the archive below, not on the rule.

### Three tables, each deleted with the account

- **`mail_rule_sets`** — one row per account that has ever had a rule written, keyed by the account with a cascading foreign key onto `mailbox_accounts`. It carries the set's **version**, a 64-bit counter every committed write moves by one, and it is the row a write locks. An account with no row has version `0` and an empty set; the first write creates the row inside its own transaction.
- **`mail_rules`** — one row per rule: its identifier, its account (cascading onto the set row), its **position**, its **name**, whether it is **enabled**, the **condition text**, the **actions**, whether a match **stops the pass**, its **triggers**, and its **schedule**. A name is unique within its account, compared without regard to case, and a position is unique within its account; both are constraints rather than checks.
- **`mail_rule_set_revisions`** — one append-only row per committed write: its own identifier, its account (cascading onto the set row), the **version** the write produced, the **revision** it produced, the revision it replaced, the **actor** who wrote it, the **surface** it came through (`Administration` or `Client`), when it committed, and the **canonical declarations of the whole set** as it stood after the write, disabled rules and names included. It is never updated except by the erasure rule below.

All three go when the account goes, which is ADR 0014's account cascade doing its ordinary work: there is nothing for the erasure seam to remember, and no rule can outlive the mailbox it was about.

**A rule's destination folder is held by the folder's identifier**, the one [ADR 0039](0039-one-folder-model-and-one-implementation-per-custody-phase.md) issues, rather than by an alias or a path. A stored rule is no longer configuration, so ADR 0039's reason for keeping configuration on names an operator chose does not reach it; a folder a person created on a held account carries no alias at all; and an identifier survives the rename a path does not. A surface accepts and shows the folder a person can read, and resolves it to the identifier when the write is validated. An action whose folder has since been deleted fails when it runs, exactly as an action that cannot be carried out fails today.

### A generated identifier is the identity, the name is what a person reads, and the position orders

- **The identifier** is a version 7 UUID minted when the rule is created, under [ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md). It is what every surface addresses a rule by, in a route, in a command argument, and in a request body, and it never changes. It is not a secret and is not treated as one.
- **The name** is unique within the account and is what a person reads and an administrator types to find a rule. A surface may look a rule up by its name to resolve its identifier, but nothing holds on to a name: renaming a rule is an ordinary write that changes the name and nothing else.
- **The position** is the rule's place in the order a pass evaluates rules in, a dense sequence from `0`. Every write that adds, removes, or moves a rule rewrites the positions of the set it commits, so the stored order is always the order a pass reads.

**A schedule is composed from the identifier.** ADR 0014 composes a scheduled walk's identity as `mail-rules:{accountId}:{ruleName}`, which is `job_schedules`' key and the root of the job idempotency key. It becomes **`mail-rules:{accountId}:{ruleId}`**, both generated identifiers, so renaming a rule neither orphans its schedule nor starts a second one. The schedule source declares a schedule for each enabled rule that carries one, and a rule removed, disabled, or stripped of its schedule leaves the declaration on the next reconciliation as any withdrawn schedule does today.

### Every write states the version it read, takes the set's lock, and validates the whole candidate

Every write — create, edit, rename, enable, disable, move, remove, and an import's replacement of the whole set — states **the set version it read** and commits in one transaction that:

1. ensures the set row exists and locks it with `SELECT … FOR UPDATE`;
2. refuses the write when the stored version is not the stated one, answering with the current version and changing nothing;
3. builds the **candidate set**: the whole ordered set as it would stand after the write;
4. validates the candidate as a whole, under the rules below;
5. writes the rule rows and the positions, moves the version by one, derives the revision, and appends the archive row.

**A stale version is refused rather than merged.** The refusal is a failure with a stable code under [ADR 0003](0003-first-party-exception-hierarchy-and-stable-error-codes.md), a `409` on both HTTP surfaces, and the caller re-reads the set and decides again. A merge was the alternative and is refused because two edits that each validate can produce a set nobody wrote: one person moving a rule above another while somebody else marks that other rule stop-when-matched is two reasonable edits and one silently changed outcome. **A refused candidate never commits**: a write whose candidate fails validation changes no row, moves no version, and appends no archive row.

The lock is the set row's alone. It serializes the writes to one account's rules and nothing else; it is never held across a network call, and a pass reading the set takes no lock at all.

### What a candidate is validated against, and the bounds a set is held to

One validation serves every surface, so `mfctl` and the client cannot accept different rules. It is the NCalc compilation and type check ADR 0010 and issue 454 built, under the configured condition bounds, and the declaration rules that exist today, with three changes: there is no account scope to judge, a destination is checked to be a folder of this account, and each action is checked against the account's `RuleActions`.

**`RuleActions` stays the administrator's ceiling.** It is part of the account's record, written only with the administrative permission that writes the account, so a person can write a rule that deletes mail only on an account whose administrator permitted deletion. A write asking for an action the account does not permit is refused. An administrator narrowing `RuleActions` later is not checked against the stored rules: an action the account no longer permits is refused when it runs and recorded as refused, which is what the rule pass already does with a narrowed permission.

The bounds a set is held to:

- **At most 200 rules per account**, enabled or not. A fixed bound rather than a setting, because nothing about a deployment makes a larger set safer to evaluate on every arrival, and because a bound an operator lowers below a stored set would leave sets nobody may write without first deleting rules.
- **A name of 1 to 128 characters**, with no control characters, unique within the account without regard to case — the bound an account's display name is held to.
- **A condition within the configured `MailRules:MaxConditionLength` and `MailRules:MaxConditionNestingDepth`**, evaluated within `MailRules:ConditionEvaluationTimeout` — issue 454's bounds, unchanged.
- **Each action at most once, each trigger at most once, and a schedule present exactly when the `Schedule` trigger is**, as the declaration rules already require.

Together these bound one set's canonical declarations to roughly a quarter of a megabyte at the default condition length, which is what bounds an archive row and a write request. Each HTTP write route refuses a body over 1 MiB before reading it.

**A stored set that no longer compiles is held, not run in part.** The condition bounds are configuration and a release can withdraw a fact, so a set valid when it committed can stop compiling later. A pass that cannot compile every enabled rule of an account runs none of that account's rules, records the run as refused naming the rules that failed and why — never their condition text — and every surface reading the set reports it held until a write repairs it. Running the rules that still compile was the alternative and is refused: an ordered, stop-when-matched set with one rule removed is a set nobody wrote, and it can file mail a rule above it was written to stop.

### A rule is created disabled, and can be previewed before it is enabled

Every create commits the rule disabled, an import's included, and enabling it is a write of its own. A preview evaluates a candidate rule, or a stored disabled one, over the account's stored mail and reports what it would match without committing anything or recording an execution; issue 762 builds it. This keeps ADR 0010's safety model — a rule acting on real mail is worth seeing before it runs — now that the review a configuration diff gave is gone.

### The revision is a digest of one account's enabled rules

`MailRuleSetRevision` stays a twelve-character prefix of a SHA-256 digest, and it stays derived rather than declared, for ADR 0010's reason. What it is taken over changes:

- **One account's set**, never the deployment's. A revision is meaningful only beside the account it belongs to, and every record naming one names its account as well.
- **No account field**, because a rule has none.
- **The identifier in place of the name.** Renaming a rule changes what a person reads and nothing about which mail is selected or what happens to it, so it does not move the revision, and an edited rule asks the mailbox afresh while a renamed one does not. Every execution record names the rule's identifier beside the name it carried, so a history row still says which rule it was after a rename.
- **The destination folder's identifier**, so renaming a folder does not move the revision either.
- **Everything else as today**, in position order: the condition text, the actions, whether a match stops the pass, the triggers, and the schedule.

**A disabled rule stays outside the digest**, as it does in configuration today. Disabling a rule changes which mail is acted on exactly as removing it would, so it moves the revision exactly as removal does; re-enabling an unchanged rule returns the set to a revision it has had before, and that is correct rather than a collision, because the same enabled rules in the same order are the same set and an execution recorded under that revision is still a record of what that set did.

The set's **version** and its **revision** are therefore two different things and both are kept. The version counts writes and is what concurrency compares; the revision names what the set means and is what a run is bound to. A rename moves the version and leaves the revision alone.

### The archive is both the revision a run resolves against and the record of who changed what

One append-only row per committed write serves two readers. Issue 458's history resolves a past execution's revision to **the newest archive row of that account carrying that revision committed at or before the run started**, which is the set as it stood when the run read it. A person or an administrator reading the account's changes reads the same rows as a list: what the set became, what it replaced, who wrote it, through which surface, and when.

**Retention follows the history's.** An archive row is pruned once it is older than `MailRules:HistoryRetention`, is not the account's newest row, and no retained execution or evaluation run resolves to it. The pass that already erases expired history prunes it, under the same per-pass bound. So what the current set says is never pruned, what a retained run ran under is never pruned, and the record of who changed what is kept as long as the history beside it is — 30 days by default. Keeping the change record longer was weighed and declined: it is authored text pointing at mail, and storage limitation is the default this repository designs for.

**The actor is reached by the actor's erasure.** The actor is a plain user identifier with no foreign key, because a row of a mailbox's record must not be deleted by a cascade from a person. Erasing a user clears the actor of every archive row naming them, in the same erasure that removes their drafts and recurring sends, and leaves the row, its surface, and its time: the mailbox keeps the record that its rules changed, and stops saying who changed them. That is the treatment ADR 0014 left to the erasure seam for an actor in a shared account's trail, stated here for this table.

### A pass reads one account's set, and every replica sees a commit at its next pass

There is no process-wide current rule set. `IMailRuleSetSource.Current` goes, and a pass reads **one account's set** from PostgreSQL when it starts — the enabled rules in position order, their revision, and the version — and holds it for its whole duration, so an edit committed mid-pass reaches the next pass and never the one running, as ADR 0010 required of a reload. Compiled conditions are cached in the process by account and revision, under a bound on how many sets the cache holds.

**Every replica sees a commit at its next pass without a backplane**, because there is nothing to invalidate. The pass reads the database, which is the only source, and the cache is keyed by a revision rather than by *current*: a commit produces a new revision, the next pass on any replica reads it and misses the cache, and an entry for a superseded revision is simply never asked for again. [ADR 0032](0032-reaching-a-client-from-any-replica-over-websockets-and-a-resp-backplane.md)'s backplane carries signals to clients and is optional; a rule pass needs none of it, and a deployment without one runs rules correctly. [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md)'s leases keep one account's run from happening twice at once, and the read needs nothing more than the transaction it is taken in.

### Who reads and who writes a rule

**On the client surface, two permissions of the mail domain**, published by the change that first serves them under ADR 0012's rule that a name is published when the capability exists:

| Permission | What it covers |
| --- | --- |
| `mailfathom.mail.rules.read` | Reading the rules of an account assigned to the caller, the set's version and state, and the record of its changes. |
| `mailfathom.mail.rules.write` | Creating, editing, renaming, enabling, disabling, moving, and removing a rule of an account assigned to the caller. |

Neither implies the other or anything else, and nothing implies either. A preview reports which messages a rule would match, so it asks for `mailfathom.mail.rules.read` *and* `mailfathom.mail.read`. The account is resolved through the caller-scoped catalog, so an account not assigned to the caller is refused exactly as an account the deployment does not serve.

**The seeded `User` role carries both**, through the `mailfathom.mail.*` pattern it already lists, so no migration writes them in and every holder of `User` gains them on the upgrade that publishes them. That is deliberate: a person assigned a mailbox already moves and deletes its mail by hand under that role, `RuleActions` bounds what a rule may do to it whoever writes the rule, and a rule is created disabled. The changelog names both permissions as reaching `User`, as ADR 0012 requires, and an operator who wants people to read their mail without writing rules defines a role listing names. A domain of its own — `mailfathom.rules.*` — was the alternative and is declined: a rule is a decision about mail, and a domain `User`'s patterns do not reach would leave the Settings screen's rules refused to every person on every deployment until a root edited the role, which is the trap issue 2449 removed.

**On the administrative surface, the administrative names it already has.** Reading an account's rules is `mailfathom.admin.read` over that account; every write is `mailfathom.admin.configuration.write` over that account, the name that already writes the account's record and its `RuleActions`; a preview, like the history, is `mailfathom.admin.audit.read`; and running rules over an account stays `mailfathom.admin.operate`. Each is checked over the account the operation names, so an organization's administrator reaches the rules of that organization's accounts and no others. An operation that is both a person's and an administrator's is two operations, as ADR 0014 states.

**No MCP tool reads or changes a rule.** A rule is a standing instruction over mail that has not arrived yet: it outlives the conversation that would have written it, it acts without anybody present, and it acts on mail nobody has read. An agent able to write one could be told to by a message it was reading — a prompt injection that ends with the session on most tools ends with a persistent rule here, filing or deleting every later message from a sender the injection chose. Reading is excluded with writing, because what a person filters for, and where, is exactly what an agent assembling such an instruction would want to know, and because a read tool beside no write tool invites the write tool next. So rules are read and written only over `AdminEndpoint` and the client API, where a person or an administrator acts directly, and MCP's tool contract gains nothing. A deployment switch and a tool annotation were the alternative and are refused: a switch defaults somewhere, and either default is a decision this record would be leaving to every operator.

**A settings policy holds no rules.** [ADR 0037](0037-a-settings-policy-at-the-deployment-and-at-each-organization.md)'s policy supplies and forces values of settings a record has. A rule set is not a value of a setting but an ordered collection with its own identity, version, and archive, and a rule held by a policy would apply to every account the policy reaches — the rule spanning accounts that issue 771 refuses. `RuleActions` is an account setting like any other, so whatever ADR 0037 decides about account settings reaches it, and this record decides nothing more about it.

### Rule rows and the archive are sensitive, and condition text is not sealed

A condition can carry an address or a subject a person typed, and a destination names a folder whose name can carry a name, so `mail_rules` and `mail_rule_set_revisions` are classified sensitive with the mail they select. They are reached by the account's erasure through the cascade above and carried by the account's export — the rules as they stand and the archive rows still retained. No condition text, folder name, or rule name reaches a log line, a metric, a trace attribute, or a generic error; a failure names the rule's identifier and the defect.

**Condition text is not sealed at rest.** The mail a condition is evaluated against — subjects, addresses, bodies — is stored beside it unsealed, protected by the database's own access control as everything else is, so sealing the shorter, less sensitive text that selects it would protect nothing an attacker reading the database lacks. And [ADR 0005](0005-data-encryption-key-ring-and-provisioning.md) lets a deployment that seals nothing run with no key ring; sealing conditions would make the ring mandatory for every deployment with a single rule, or need an unsealed fallback that is the same as not sealing.

### Removing a rule removes its row

Removing a rule deletes its row, rewrites the positions below it, and withdraws its schedule on the next reconciliation. Nothing keeps a tombstone. The archive is the only place that keeps what the rule was, for as long as the retention above keeps the row, and the executions already recorded keep naming the revision and the rule's identifier they ran under.

### The upgrade: the section's rules stop being read, and startup says so

**`MailRules:Rules` is no longer bound.** Nothing reads it, no rule it declares is applied, and its keys stop being validated, so a deployment that still carries the section starts. The section's strict binding keeps recognizing the key for exactly that purpose, because a binder that refuses a name it does not know would otherwise read the retired key as a misspelling and fail the start this paragraph promises. At startup it logs one warning naming how many rules the section declares, that rules are now stored per mail account and none of these is applied, and the `mfctl` command issue 764 adds to import them. The warning names no rule and no condition, because both are authored text.

**The section's limits stay configuration**, because they belong to the deployment rather than to a mailbox, which is ADR 0014's line between the two: `MaxConditionLength`, `MaxConditionNestingDepth`, `ConditionEvaluationTimeout`, `EvaluationBatchSize`, `MaxEvaluationBatchesPerPass`, and `HistoryRetention` keep their keys, their defaults, and their meaning.

Issues 762 and 764 ship in the same release, so no release stops reading configured rules without offering the import. An imported rule is a create like any other: it commits disabled, under the importing administrator as its actor, and the operator enables it once they have seen it.

**Executions recorded before the upgrade keep the revisions they named.** Those digests were taken over the configured form and no archive row carries them, so the history reports such a revision as not archived rather than resolving it to a set it never was, and such an execution names its rule by the name it carried and by no identifier.

### What this breaks, surface by surface

- **Configuration schema — breaking.** `MailRules:Rules` and every key beneath it, `Accounts` and `Enabled` among them, are no longer read. The operator's action is to import each account's rules with the command issue 764 adds, review them, and enable them; until then, no configured rule runs.
- **Database schema — additive.** Three tables, each keyed onto the account and deleted with it, and a nullable column on a rule execution naming the rule's identifier, empty on every execution recorded before the upgrade. Nothing existing is rewritten, so the release is deployable over the previous release's data.
- **MCP tool contract — unchanged.** No rule tool exists and none is added.
- **Deployment contract — unchanged.**
- **The administrative HTTP API — changed.** It is not one of ADR 0004's four surfaces and is recorded as they are, because `mfctl` reads it: the deployment-wide read of the loaded rules becomes a read of one account's rules, and the write routes are new.
- **Permissions — two published**, `mailfathom.mail.rules.read` and `mailfathom.mail.rules.write`, reaching the seeded `User` role through its pattern.

### Consequences

- Good, because a rule is the mailbox's own data: a person changes it from the client without a restart, every person assigned the mailbox sees the same set, and deleting the account deletes its rules with nothing to remember.
- Good, because the version, the identity, the uniqueness of a name, and the order are each a column and a constraint, so `mfctl` and the client cannot settle any of them differently.
- Good, because a stale write is refused rather than merged, so no committed set is one nobody wrote.
- Good, because a past run stays explainable after any edit or removal, without the configuration repository ADR 0010 relied on and that most operators never kept.
- Good, because renaming a rule or a folder no longer moves the revision or orphans a schedule.
- Good, because a pass on any replica sees a commit at its next start with no reload signal, no cache invalidation, and no backplane.
- Neutral, because a write that loses a race is refused and has to be made again on a fresh read; for a list a person edits by hand that is rare, and for an import it is the caller's retry.
- Neutral, because the configured condition bounds still apply to stored rules, so lowering one can hold a stored set until it is repaired, which is reported rather than silent.
- Bad, because an instance is no longer described by its configuration. Two deployments provisioned identically can run different rules, and reproducing one means its database — the cost ADR 0010 named, now accepted.
- Bad, because the record of who changed a rule is kept only as long as the history, 30 days by default; somebody who wants a longer trail keeps a longer history window, which keeps more personal data with it.
- Bad, because every person holding the seeded `User` role can change the rules of every mailbox assigned to them from the upgrade on, and an operator who wants otherwise has to define a role.
- Bad, because every configured rule stops running at the upgrade until an operator imports and enables it, which is the break the changelog names.
- Bad, because executions recorded before the upgrade name revisions nothing can resolve.

## Validation

- Unit tests over the write path require a write stating a stale version to be refused with no row changed, a candidate failing validation to commit nothing, and every committed write to move the version by one and append exactly one archive row.
- Unit tests over the revision require a rename of a rule or of its destination folder to leave the revision unchanged, any change to an enabled rule's condition, actions, stop, triggers, schedule, or position to move it, disabling a rule to move it exactly as removing it does, and two accounts holding identical text under different identifiers to have different revisions.
- Unit tests require a pass to read one account's set, to finish against the set it began with when a write commits mid-pass, and to run none of an account's rules when one enabled rule no longer compiles.
- Unit tests over the schedule source require the composed identity to name the rule's identifier, and a renamed rule to keep its schedule.
- Unit tests over the permissions require each client route to ask for its name over an account assigned to the caller, a preview to ask for both names it needs, and the seeded `User` role's pattern to reach both.
- The boundary and contract tests require the MCP tool catalog to publish no tool reading or changing a rule.
- A unit test requires the startup warning for a carried `MailRules:Rules` section to state the count and the import and to carry no rule name or condition.
- The model test and the erasure tests require all three tables to be deleted with the account, and a user's erasure to clear the actor of every archive row naming them while keeping the row.
- Code review rejects a configuration source that contributes a rule, a write that merges a stale candidate, a rule addressed by its position or held by its name, and a log, metric, or error that carries a condition, a rule name, or a folder name.

## Pros and Cons of the Options

### Three tables keyed by the account

- Good, because the version and its lock, the rule and its identity, and the archive each get the row shape their reader needs, and PostgreSQL enforces name and position uniqueness.
- Good, because a pass reads the enabled rules in order with one indexed query, and a history resolves a revision with another.
- Neutral, because a write rewrites positions, which for at most 200 rows is a small, bounded statement.
- Bad, because three tables are three mappings, three configurations, and one migration more than a document would need.

### One JSONB document per account

- Good, because a write replaces one value, and the whole set is read and written as the unit it is.
- Bad, because a rule's identity, a name's uniqueness, and the order become properties MailFathom checks inside a document rather than constraints the database holds, and a schedule or a history row pointing at one rule points into a blob.
- Bad, because the archive is still a second table, so the document saves one table and loses every constraint.

### A writable configuration layer in the owner's settings document

- Good, because rules would stay configuration-shaped and reuse the existing validation path.
- Bad, because a rule would belong to an owner rather than to a mailbox, so a shared mailbox's rules would be one assigned person's and an erased person would take a mailbox's rules with them.
- Bad, because it keeps a configuration layer as a second place a rule can come from, which is the merge issue 771 refuses.

### Configuration as a seed, the database as the live copy

- Bad, for the reason ADR 0010 gave and that still holds: the file and the table diverge on the first runtime edit, and every later question about which wins has to be answered forever.

### Keep ADR 0010's configuration section

- Good, because nothing is built.
- Bad, because a person cannot change their mailbox's rules, a shared mailbox's rules are the operator's file, and the Settings screen has nothing to edit — the conditions ADR 0010 named for revisiting it.

### A version per rule

- Good, because two people editing two different rules at once both succeed.
- Bad, because the set is ordered and stop-when-matched, so two edits to two rules are two edits to one meaning, and accepting both commits a set neither person wrote.

### Last writer wins

- Bad, because an edit made against a list that changed underneath it silently undoes somebody else's.

## More Information

- [ADR 0010](0010-rule-authoring-in-configuration-and-ncalc-conditions.md)'s storage decision is superseded by this record, and its condition-language decision stands; ADR 0010 says so. [ADR 0014](0014-single-tenant-multi-user-ownership-on-the-mail-account.md)'s paragraphs on where a rule stays, on the composed schedule identity, and on what stays configuration are amended to this shape in the same change, and so is [ADR 0039](0039-one-folder-model-and-one-implementation-per-custody-phase.md)'s statement that a rule destination is written against an alias.
- [ADR 0012](0012-authorization-model-named-permissions-and-where-they-are-enforced.md) is the vocabulary the two permissions are allocated in and the rule that publishes a name only with the capability it governs. [ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md) is how a rule's identifier is minted. [ADR 0003](0003-first-party-exception-hierarchy-and-stable-error-codes.md) is the failure a stale write is refused with. [ADR 0004](0004-versioning-and-release-policy.md) is why the configuration break is permitted and why it is written down.
- Issue 771 is the feature. Issue 762 builds the tables, the write path, evaluation from the stored set, the revision, the archive and its retention, and the preview, and updates the pages that today say rules are configuration: `docs/features/mail-rules.md`, `docs/operations/configuration-mail.md`, `docs/operations/configuration-reference.md`, `docs/operations/configuration-sources.md`, `docs/operations/admin-endpoint.md`, and `docs/architecture/stored-email-schema.md`. Issue 763 builds the `mfctl` commands and issue 2996 the client API, each documenting its own surface. Issue 764 builds export and import and ships in the same release as issue 762. Issue 2997 builds the Settings screen on issue 2996.
- Revisit when a single account genuinely needs more than 200 rules, when a deployment needs the change record kept longer than its history, or when a rule has to reach mail on more than one account — each is a decision this record took deliberately rather than a gap it left.
