---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-23
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Give every mail account one folder model under an identifier MailFathom issues, keep the source binding as an attribute of a folder, and carry every act on a mailbox through one implementation per custody phase that one resolver chooses

<!-- describes: backend/src/Domain/Folders/**, backend/src/Application/Folders/**, backend/src/Application/Mail/Mutations/**, backend/src/Application/Mail/Delivery/Filing/**, backend/src/Application/Accounts/Custody/** -->

## Context and Problem Statement

[ADR 0034](0034-holding-a-mailbox-mailfathom-alone-keeps.md) let an account hold its own mailbox and chose, for messages, one identity for every account: a stored message is its own row, and the occurrence on the source server is an optional attribute of it (its axis A, option 2). It made no such choice for folders, and what was built gives each custody phase a folder model of its own:

- `mail_folders` holds the source server's folders keyed by their alias. Each row is a **binding** of an alias to a remote path at one resolution generation (`MailFolderResolution`), not a folder: every generation of a binding is kept so that an occurrence stays attributable to the remote folder it was read from.
- `local_mail_folders` holds a held account's own hierarchy keyed by a generated identifier (`LocalMailFolderId`). Its shape — identity, parent, name, role, the source alias it was created from — is already the shape of a general folder.
- A stored message points at both: a non-null `MailFolderId` naming a binding, which it keeps on a held account long after the source stopped holding it, and on a held account a `LocalMailFolderId` as well.

A mirrored account's folder, meanwhile, is a declaration in configuration rather than a row ([ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md), axes L to N): its identity at every surface is the alias it is declared under, and the alias deliberately never moves, so a folder renamed through MailFathom from `Projects` to `Clients` is still called `Projects` by every client and every tool.

Two consequences follow, and issue 2580 is the feature that removes both.

**The read side ignores a held account's folders.** The folder tree (`MailFolderDirectoryReader`), listing, search, `ask_mail`, and every other folder-scoped query filter on the binding's alias (`AccountScopedMailFolders`). A folder created on a held account appears only under the client's `/managed-folders`, and a message moved there is still listed, searched, and answered from in the folder it was synchronized into. The MCP tools name a folder by alias or by role, so they cannot name a folder a held account created at all, because it has no alias.

**The act side decides the phase in six places.** What a caller sees as one surface is dispatched by a `switch` on `MailAccountCustodyPhase` in `MailFolderEditor`, `MailboxChangeSubmission`, `MailboxDestinationResolver`, `MailboxMutationConverger`, `LocalMailFiler`, and `LocalMailFolderArrivals`. Each reads the phase through `ILocalMailFolderStore` and has to remember to branch on its own. `LocalMailFolderEditor` and `MirroredMailFolderEditor` share no contract — different identities, different outcome types — so `MailFolderEditor` translates between them, and names a folder by "an identity whose shape is not part of the contract": a local identifier on one account, an alias on the next.

ADR 0034 required that no caller branch on where a mailbox's truth lives, and the callers do not. Behind them, though, the choice is made wherever somebody remembered to make it, and the read side is the place nobody did. Issue 2577 asks for the shape that replaces both halves, so that the implementation issues beneath 2580 — 2578 for the folder model and the read side, 2579 for the acts — are written against one answer. This record is that answer. It produces no code.

## Decision Drivers

- **A held account's folders and moves appear everywhere mail is read**, exactly as a mirrored account's do. The defect is that a correct local commit is invisible, and the fix has to be one no future reader can forget.
- **The custody phase is decided in one place.** A new reader, a new act, or a new phase that silently ignores a held account is the failure being removed; a compiler error is the only reminder that cannot be skipped.
- **A folder's identity survives everything that happens to the folder**: a rename, a move in the hierarchy, a rename on the source by another client, a new resolution generation, a drain into `Held`, and a restore back to `Mirrored`.
- **A name a person or a model reads is not stale.** An identifier that still reads `Projects` after the folder was renamed `Clients` misleads both, and a model told to file into `Projects` will do so.
- **Configuration stays configuration.** An operator writes folder mappings, rules, spam destinations, and per-folder settings against names they chose; none of that may start requiring an identifier the deployment generated.
- **The two transaction shapes stay visible.** A mirrored folder act reaches the server before anything is written ([ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md)'s *never half an act*), and a held one commits in one transaction ([ADR 0034](0034-holding-a-mailbox-mailfathom-alone-keeps.md)'s option D1). A contract that hides the difference invites a held act to wait on IMAP or a mirrored one to write first.
- **A rolling upgrade runs two builds against one database**, so the schema change is additive and the older build keeps serving across it.

## Considered Options

The decision has four axes. A and B are the folder model and are ordered — B decides which folders carry the identity A picks. C and D are the acts and are written against A and B.

**A — what identifies a folder:**

1. An identifier MailFathom issues for every folder of every account; the alias stays as an attribute of the source binding and as configuration's name for the folder.
2. An immutable alias for every folder, generated for a folder MailFathom creates.
3. Both identities kept, behind a reference that is either — what `MailFolderEditor` does today.

**B — which folders are rows, and what a stored message names:**

1. Every account has folder rows — a mirrored account's maintained by synchronization as a projection of its source hierarchy, a held account's owned by MailFathom. The source binding is an optional attribute of a folder, and a stored message names exactly one folder.
2. Folder rows only for held accounts; a mirrored account keeps its bindings, and every reader unions the two by phase.
3. A database view over both tables that every reader queries.

**C — how an act on a mailbox reaches the copy that is the truth:**

1. One contract covering folder acts, message acts, and filing, implemented once per custody phase, with one resolver as the only reader of the phase.
2. One contract per act family, each with its own implementation per phase and its own resolver.
3. The switches stay, and the phase is read through one shared helper whose switch expressions the compiler checks for exhaustiveness.

**D — what is put behind that contract:**

1. The acts a caller requests, and nothing else; synchronization, the drain, the restore, and the converger stay specific to their mode.
2. Everything that touches a mailbox, background work included.

## Decision Outcome

Chosen options: **A1**, **B1**, **C1**, and **D1**.

### A folder is identified by an identifier MailFathom issues, for every account

Every folder of every account carries a `MailFolderId`, a version 7 UUID minted under [ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md), **for its whole life**. It is minted when the folder first exists in MailFathom — when synchronization first resolves a mirrored folder, when a held account creates one, when a source folder first delivers to a held account — and it survives every rename and move, a rename of the remote folder by another client, a new resolution generation, a drain into `Held`, and a restore back to `Mirrored`. Like every identifier ADR 0036 governs it is not a secret; whether a caller may read a folder is the account's authorization, never knowledge of the identifier.

The **alias stays, and keeps exactly two jobs**: it is the key a configuration declaration is written under — a folder mapping, a rule destination, a spam destination, a per-folder setting — and it is what a source binding is keyed by, together with its resolution generation. It is an attribute of a folder rather than its identity, present on every folder a declaration names and on every held folder a source folder created, and absent on a folder created on a held account. A declaration MailFathom writes for a folder a person created on a mirrored account still carries a generated alias, because configuration needs a key; nothing outside configuration and synchronization reads it.

Option A2 was refused for the reason the issue already gave and the drivers state: an alias that keeps its old name after a rename misleads both a person and a model, and ADR 0007 is right that the alias must not move, because the bindings and the checkpoints are keyed by it. An identifier that names nothing can be stable without lying. Option A3 is the state that produced the defect: a reference that is sometimes one thing and sometimes another is resolved correctly only by the code that remembered which.

### Every account has folder rows, and a stored message names exactly one of them

**One folder model serves every account** (option B1). A folder row carries its identifier, its account, its parent, its name, its role, its alias where it has one, and whether it is erased. On a mirrored account the rows are **a projection of the source hierarchy, maintained by synchronization**: a declaration that resolves gets a row, its name and parent follow the remote path the server reports, and a rename on the server — by MailFathom or by another client — updates the row rather than replacing it. On a held account the rows are **owned by MailFathom**, exactly as ADR 0034 describes its local hierarchy: the five protected roles, delete into the trash, the name validation, and the fixed `/` delimiter all stand, now applying to the folder rows of a held account.

**The source binding is an optional attribute of a folder.** A binding row — alias, resolution generation, remote path, hierarchy delimiter, and the synchronization checkpoint beside it — names the folder it binds, and a folder holds as many bindings as resolution generations have existed and as many as zero. A held folder whose source folder was drained keeps its binding exactly as long as anything on the source still needs finding; a folder created on a held account has none.

**A stored message names exactly one folder**, through one non-null reference to a folder row that replaces both `MailFolderId`-as-folder and `LocalMailFolderId`. The binding a message was read from becomes **part of its occurrence**: the binding reference, `UIDVALIDITY`, and `UID` are present together or absent together, which extends the check constraint ADR 0034 already placed on the latter two. On a mirrored account the message's folder is the folder its occurrence's binding binds; on a held account it is wherever the person or a rule put it, and the occurrence, while it stands, still says where the drain finds it on the source.

**Every reader filters on that one reference, and none of them reads the phase.** The folder tree, listing, search, `ask_mail`, threads, rules, spam, reply drafting, and every other folder-scoped query ask which folder a message is in and nothing else. A role resolves to the folder row playing it; an alias written in configuration resolves to the folder row carrying it. That is the whole of the fix for the read-side defect, and it is structural: a new query written against the model is correct for every custody phase without its author knowing custody exists.

**Placing an arrival stops depending on the phase.** A message synchronization stores goes into the folder its binding binds, and where that folder has been erased — which only a held account's own act can do — into the inbox, as ADR 0034 already rules. A binding that has no folder yet is given one in the same transaction: on a mirrored account the projection of its declaration, on a held account a folder named after the source folder, which is ADR 0034's correspondence rule. Whether an arrival is drained afterwards is the drain's question, not placement's.

**A per-folder setting is a property of the folder**, written in configuration under the folder's alias and read from the row that carries it. A folder with no alias is governed by the values a folder mapping that states none of them takes — visible to tools, embedded, classified for spam where the account classifies — because a folder a person made on their own held mailbox is ordinary mail of theirs. Setting those values per folder without an alias is not decided here.

Option B2 was refused because it is the defect generalized: every reader would union two sources by phase, and the one that forgot is the bug this record exists to close. Option B3 hides the union rather than removing it, keeps two writers with two identities beneath one name, and a view cannot carry the foreign key a stored message needs.

### Every act on a mailbox goes through one contract, implemented once per custody phase

**Acting on a mailbox has one port, `IMailboxCustodian`**, covering three families of act:

- **Folder acts**: describe which acts the account and each of its folders allow, then create, rename, move within the hierarchy, and delete.
- **Message acts**: set or clear flags and keywords, move, copy, and delete — with the preparation step that runs before the caller's transaction (resolving a destination, reading what a copy needs) separate from the commit that runs inside it.
- **Filing**: file a draft revision, give a draft up, and file a sent copy.

**It has one implementation per custody phase** (option C1): `MirroredMailboxCustodian`, `HeldMailboxCustodian`, and `RestoringMailboxCustodian`, each carrying the whole of what that phase does in one class and composing the collaborators that already do the work — the mirrored and local folder editors, the mutation records and the local state store, the copy appender and the local filer. Adding an act to the port is a compiler error until all three implement it. Each family takes and returns the same types in every phase: one folder identity, one outcome type per family, one refusal vocabulary.

**One resolver, `MailboxCustodianResolver`, is the only reader of the phase for acts**, and the only `switch` over `MailAccountCustodyPhase` outside the mode-specific background work. Reading the phase here means deciding something from it; the types that own the phase — the custody domain types, the store that persists them, and the administrative switch that changes them — hold and report it and decide no act, and stay as they are. Adding a phase is a compiler error in that one switch and a new implementation of the port. It reads the phase **inside the caller's transaction** for message acts and filing, which already commit in one, so a switch of custody cannot be judged differently by two acts in the same request. Nothing else reads the phase through `ILocalMailFolderStore`, which goes back to being the store of a held account's folders.

**The transaction shapes stay explicit in the contract, by what each family takes:**

- **Message acts and filing take the caller's transaction** in every phase. On a mirrored account the act opens its mutation record in it, and the converger carries the record to the server under ADR 0007; on a held account the act commits to stored state in it with its audit entry; on a restoring account it does both.
- **Folder acts take no transaction, and each implementation owns its own.** A mirrored folder act reaches the server first and writes the declaration and the folder row only once the server has answered, because a transaction held open across an IMAP round trip is exactly what ADR 0007's write session exists to avoid. A held folder act commits in one transaction, and asks the resolver again inside it — the one place the phase is read, now given that transaction — refusing with `AccountNotHeld` where the account no longer resolves to the held implementation since the act was dispatched. A mirrored act that races a switch to `Held` is bounded by one server round trip, and ends with a declaration a held account reads only when it is restored — the same window `MailFolderEditor` has today, named rather than closed.

**`Restoring` is a phase of its own, not a flag on the held implementation.** Its message acts commit locally, as a held account's do, and in the same transaction open the mutation record that carries the act to the source wherever the message holds an occurrence — ADR 0034's rule, unchanged. Its folder acts are refused with `AccountNotHeld` and its folder description allows none, because its local folders are still the truth while its source is being filled from them. Its filing is the mirrored path — appended to the source through the durable record ADR 0007 governs — exactly as today, since the source of a restoring account is not drained. Writing it as its own class is what keeps that combination readable in one place instead of as conditions in two.

Option C2 was refused because three resolvers are three readers of the phase, and two acts in one request could still be judged by two of them; nine implementation classes also put one phase's behaviour in three files a reviewer must assemble. Option C3 keeps exhaustiveness and loses the rest: a helper the switches call is still six places deciding, each with its own idea of which transaction to read in, and it gives a contributor adding an act nothing to implement.

### What is not behind the port

**Synchronization, the drain, the restore, and the converger stay specific to their mode** (option D1). Each exists because of a mode rather than independently of one: synchronization imports a mirrored account's remote changes and refuses to import a held account's; the drain empties a held account's source; the restore fills a restoring account's source; the converger carries a mirrored account's mutation records and only a held account's erasures. Putting them behind the port would give the held implementation a synchronizer that does nothing and the mirrored one a drain that does nothing, and the port would grow one empty method per background pass — option D2. They keep reading the phase, and they are the only code besides the resolver that decides anything from it.

### What ADR 0007, ADR 0008, and ADR 0034 keep, and what this record changes

All three records are `proposed`, so each carries a pointer to this one rather than being superseded, and none of their texts is rewritten.

- **ADR 0034** keeps everything it decided about holding a mailbox — the switch, the phases, the drain gate, local acts, the five protected roles, background source work, filing, export, and the operator's obligations. This record replaces two of its sentences. *A stored message also carries the local folder it is in, which on a mirrored account is the folder its occurrence is in and on a held account is MailFathom's own* becomes [a stored message names exactly one folder](#every-account-has-folder-rows-and-a-stored-message-names-exactly-one-of-them), for every account. *The choice of executor is made once, in the mutation layer, from the account's phase* becomes [one resolver for every act](#every-act-on-a-mailbox-goes-through-one-contract-implemented-once-per-custody-phase) — folder acts and filing as well as message acts, which is what the sentence promised and the code did not deliver.
- **ADR 0007** governs every command MailFathom issues against a source server unchanged, axes L to N included: which folder acts reach a mirrored account's server, for which requester, under which permission, and that a declaration is written only after the server acted. What changes is the identity a surface names such a folder by — its `MailFolderId` rather than its alias — and that the act writes the folder row's projection beside the declaration. The alias still never moves; it has stopped being what anybody outside configuration reads.
- **ADR 0008**'s first clause, that a copy is a second stored message, holds; its second clause was already replaced by ADR 0034. This record adds only that the message's folder is a reference of its own rather than a component of its occurrence.

### What breaks, and what each party does

The major version is `0`, and [ADR 0004](0004-versioning-and-release-policy.md) permits a minor to break any of the four public surfaces. Three of them break; the configuration schema does not.

- **The MCP tool contract.** The `folders` argument of `list_emails`, `search_emails`, and `ask_mail` takes a folder identifier or a role written as `role:<Role>`, and refuses an alias with the refusal an unknown folder already gets. Every result that reports `folderAlias` — a listed summary, a cited email, retrieved content, the `set_mail_flags` result, and each folder's freshness in `list_accounts` — reports the folder's `folderId` and its `folderPath` instead, and `list_accounts` lists each account's folders with identifier, path, and role, which is where a model learns the identifiers. **Caller's action**: an agent prompt, a saved tool call, or an integration that names a folder by alias names it by role, or reads its identifier from `list_accounts`. Nothing changes on the deployment.
- **The client endpoint.** The folder tree served at `/folders` and the folders served at `/managed-folders` name every folder by its `MailFolderId`, on every account in every phase, so a folder one returns is the folder the other acts on. The folder scope of the timeline and of search, the destination of a move, the folder fields of a message, a citation, a mutation, and a spam verdict, and the signals announcing a changed folder use it too. **Operator's action**: none beyond deploying the release, because the web client is served by the deployment it talks to; a desktop or Android client older than the release fails every folder call until it is updated, and the release notes say so.
- **The database schema**, expanded in one release and contracted in a later one. The first adds the folder rows of every mirrored account and the new reference on every stored message, backfills both — on a mirrored account a folder for each alias a binding names, on a held or restoring account its existing local folders under the identifiers they already carry, and each message's folder from its local folder where it has one and from its binding otherwise — keeps the old columns written by the new build, and carries what an older build writes into the old columns onto the new reference in the same statement, so two builds against one database never disagree about which folder a message is in. The later release removes the old columns and that carriage. Neither regenerates a baseline. **Operator's action**: apply the release's schema artifact as for every release, and plan for the expanding migration taking time in proportion to the stored mail, since it writes every stored message once; install the contracting release only over the expanding one, and only once no replica runs an older build — its changelog entry names the release it requires.

The administrative surface and `mfctl` accept a folder identifier wherever they accept an alias today, and keep accepting the alias, because an operator works from the configuration they wrote and a held account's own folders have no alias to name them by. That is two names for two audiences rather than a compatibility path. Audit entries keep naming folders by path, as ADR 0034 already requires.

### Consequences

- Good, because a held account's folders and moves are visible to every reader, and a reader written later is correct for every phase without knowing about custody.
- Good, because the phase is read for acts in exactly one place, and both a new act and a new phase are compiler errors until they are handled everywhere they must be.
- Good, because a folder keeps one identity across a drain and a restore, so a mailbox switched on and off again is the same mailbox to every client and every tool.
- Good, because each phase's behaviour reads in one class, and a reviewer reading what a held account does follows one file instead of six dispatchers.
- Neutral, because a mirrored account gains rows that are a projection of what it declares and the server reports, which synchronization now maintains beside the bindings; the bindings and their generations are unchanged.
- Neutral, because configuration still names folders by alias, so a folder with no alias is reachable from configuration only by the role it plays, as it is today.
- Bad, because the MCP tools, the client endpoint, and the schema all break in one release, and every integration that hard-coded an alias rewrites it.
- Bad, because the schema change spans two releases with carriage between the old columns and the new reference, which is machinery that exists only for the length of an upgrade.
- Bad, because the expanding migration writes every stored message once, which on a large deployment is a migration measured in minutes.

## Validation

- Owner review of this record before either implementation issue starts.
- Issue 2578 lands with a test that reproduces the read-side defect against the code before its change — a folder created and a message moved on a held account are absent from, respectively misplaced in, the folder tree and a folder-scoped listing — and passes after it.
- A test in `Boundaries.UnitTests` fails when a type references `MailAccountCustodyPhase` outside an allowed set the test names: the resolver; the mode-specific background work — synchronization, the drain, the restore, and the converger; and the types that own the phase rather than decide from it — the custody domain types, the store that persists them, the administrative switch and its endpoint, and the persistence of the account and its folders. Each of the three implementations of the port is outside that set, so a phase check written inside one of them fails the test rather than passing review.
- `PublicSurfaces.UnitTests` records the MCP tool contract and the HTTP API contract, so the argument, result, and endpoint changes appear there as an intended diff in the pull request that makes them.
- The integration suite covers a drain into `Held` and a restore back to `Mirrored` keeping every message in its folder under the same `MailFolderId`, and the expanding migration over a database carrying mirrored and held accounts.
- The existing unit tests of both folder editors and of the change submission pass against the new implementations, moved rather than rewritten where the behaviour is the same, since issue 2579 changes no observable behaviour.

## Pros and Cons of the Options

### A2 — an immutable alias for every folder

Generate an alias for every folder MailFathom creates, and make the alias the identity everywhere.

- Good, because the alias already is the identity on a mirrored account, so the read side would change least.
- Bad, because an alias reads as a name and does not follow one: after a rename it misleads a person in the client and a model in a tool call, and a model told to file into a name files into it.
- Bad, because a generated alias for a held folder is an identifier dressed as a name, which is A1 with the drawback of looking meaningful.

### A3 — both identities behind a reference that is either

Keep the alias on a mirrored account and the local identifier on a held one, and let every surface accept a text that is one or the other.

- Good, because it needs no migration.
- Bad, because every reader has to know which kind a reference is on which account, which is the defect itself, and a folder changes identity when its account changes phase.

### B2 — folder rows only on held accounts

- Good, because a mirrored account's storage is unchanged.
- Bad, because every reader unions bindings and local folders by phase, and a reader written later that does not is the next defect of this kind.

### B3 — a view over both tables

- Good, because readers query one relation.
- Bad, because it hides two writers with two identities under one name, and a stored message cannot hold a foreign key to a view.

### C2 — one contract per act family

- Good, because each contract is small and a caller depends on only the family it uses.
- Bad, because three resolvers are three readers of the phase, and one phase's behaviour is spread across three classes.

### C3 — exhaustive switches through one helper

- Good, because it is the smallest change and the compiler checks each switch.
- Bad, because the decision is still made in six places, each choosing its own transaction to read the phase in, and a new act has nothing to implement.

### D2 — background work behind the port as well

- Good, because the phase would be read in one place without exception.
- Bad, because each mode-specific pass would become an empty method on the other phases' implementations, and the port would describe what modes are rather than what a caller may ask.

## More Information

- Issue 2577 asks the questions this record answers; issue 2580 is the feature it belongs to; issue 2578 implements the folder model and the read side, and issue 2579 the port, in that order.
- [ADR 0034](0034-holding-a-mailbox-mailfathom-alone-keeps.md) holds the custody phases and the held account's folder rules this record keeps, and the two sentences it replaces; [ADR 0007](0007-remote-mailbox-mutation-boundary-and-write-session.md) holds the folder acts on a source server and the declaration they write; [ADR 0008](0008-copied-message-local-identity.md) holds the copy decision; [ADR 0036](0036-minting-every-identifier-as-a-version-7-uuid-and-never-treating-one-as-a-secret.md) governs how the folder identifier is minted; [ADR 0004](0004-versioning-and-release-policy.md) permits the breaks.
- [ADR 0040](0040-each-account-outbox-as-a-read-only-list-beside-its-folders.md) keeps this record's folder model unchanged: each account's outbox is presented beside its folder rows rather than as one, carries no `MailFolderId`, and is reached by no act, and the `Outbox` role leaves the roles a folder may play.
- Revisit when a per-folder setting is asked for on a folder with no alias, when a provider whose folders are labels over one store is asked for — one message in several folders is outside what "names exactly one folder" can say — or when a fourth custody phase is proposed.
