# Mail rules

<!-- describes: backend/src/Application/Rules/**, backend/src/Infrastructure/Rules/**, backend/src/Infrastructure/Persistence/Rules/**, backend/src/Host/Configuration/Rules/** -->

A mail rule selects mail and changes it. It belongs to one mail account, and it is a name, a condition, the occasions
that run it, what a match leads to, whether a match ends the pass, and whether it runs at all. This page documents both
halves: every fact a condition can read, every function and operator available to it, the limits it is read and run
under, how an account's rules are stored and changed, the order they are evaluated in, and every change a matching rule
can ask for.

An account's rules are rows the deployment keeps for that account in PostgreSQL, and nothing in configuration declares
one. [ADR 0048](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0048-keeping-a-mail-accounts-rules-in-postgresql-and-changing-them-at-runtime.md)
records that decision — the identity, the version check, the archive, and the permissions below are all its — and
[ADR 0010](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0010-rule-authoring-in-configuration-and-ncalc-conditions.md)
the half it left standing: a condition is one expression rather than a nested structure of predicates. No MCP tool reads
or changes a rule.

## An account's rules

An account holds an ordered list of rules, at most 200 of them, enabled or not. What `mfctl rules show <name> --account
<id>` prints for one of them is every part a rule has:

```text
Rule       supplier-invoices (0193a6f2-6c1e-7b4a-9d2f-5e8c1a3b7d40)
State      enabled, position 0
Rule set   3f9a1c0e7b52, version 7
Condition  senderDomain == 'supplier.test' and attachmentCount > 0
Runs on    Arrival
A match    relocate → invoices, set-seen → read; ends the pass
```

**The identifier is what a rule is.** It is generated when the rule is created and never changes, so a schedule, a
history record, and a request a rule opened against the mail server all hold on to it rather than to the name.

**The name is what a person reads.** It is 1 to 128 characters with no control character, and it is unique within the
account without regard to case, so `Invoices` and `invoices` cannot be two rules of one account. Renaming a rule changes
nothing about which mail it reaches: its revision, its schedule, and its history stay where they were.

**The position is the order a pass evaluates the rules in**, counted from `0` and always dense. Removing a rule moves
every rule below it up by one, and moving one rewrites the positions between where it was and where it went.

**Whether a rule is enabled decides whether it runs.** A disabled rule is kept, read, and reported, and no pass of any
kind evaluates it: not an arrival, not a schedule, not a run somebody asked for. Disabling a rule changes the set's
revision exactly as removing it would, and enabling it again unchanged returns the set to the revision it had before.

`Runs on` is the rule's triggers, and its schedule where it declares one: [the section below](#which-triggers-run-a-rule)
states what each means. `A match` is the changes a match asks for in the order they were declared, and whether a match
ends the pass; [what a matching rule does](#what-a-matching-rule-does) states each of them.

**A rule reaches its own account's mail and no other.** There is no filter naming accounts, because there is nothing for
one to select between: a rule that should act on two mailboxes is two rules, one in each, and each is validated against
the folders and the permissions of the account that holds it. The [`account`](#the-facts-a-condition-can-read) fact is
still there, and always answers the account the rule belongs to.

**Reading an account's rules** is `mfctl rules list --account <id>`, which prints the whole set in order with the
revision and the version it stands at, and `mfctl rules show <name> --account <id>` for one rule. Both read
`GET /rules?account=<id>` on the administrative endpoint, under `mailfathom.admin.read` over that account; [reading the
rules, running them, and finding out what they
did](../operations/admin-endpoint.md#reading-the-rules-running-them-and-finding-out-what-they-did) is where the commands
and the response are documented. No command or route writes a rule; [how a change is
written](#how-a-change-is-written) is what a write does.

**An account's rules go with the account.** Erasing the account erases its rules and every archived version of them,
because the rows cascade from the account's own. Erasing the person who wrote a rule does not: a rule is a standing
decision about the mailbox's mail, not something sent in anybody's name, so it stays for everybody still assigned the
mailbox, and the archive stops saying who wrote it.

**A configuration still declaring `MailRules:Rules` binds and applies none of them.** The key stays recognized so a
deployment carrying it still starts, and the start logs one warning with the number of rules the section declares —
never what they say — and the command an operator imports them with. [Mail
configuration](../operations/configuration-mail.md#mailrules) lists the keys the section still holds: the bounds a
condition is read and run under, and how much a pass evaluates.

## How a change is written

A change to an account's rules is one of five writes, and each is a whole transaction over the account's set:

| Write | What it does |
| --- | --- |
| Create | Adds a rule at the end of the set, **disabled**, under a new identifier |
| Replace | Changes what one rule declares, keeping its identifier, its position, and whether it runs |
| Enable or disable | Switches one rule on or off, and nothing else about it |
| Move | Puts one rule at another position; a position outside the set is refused |
| Remove | Deletes one rule, moves the rules below it up, and withdraws its schedule |

**Every rule starts disabled.** A rule acting on real mail is worth seeing before it runs, so a create never commits an
enabled rule and enabling it is a write of its own — [previewing a rule](#previewing-a-rule) is
how it is seen first.

**A write states the version it read, and a stale one is refused.** The set carries a version that every committed
write moves by one. A write takes the set's lock, compares the version it was given against the one stored, and refuses
when they differ, changing nothing; the caller reads the set again and decides again. Two writes are never merged: two
edits that each validate — one person moving a rule above another while somebody else makes that other rule end the
pass — can produce a set neither of them wrote. The lock is the set's alone and serializes writes to one account's rules
across every replica; a pass reading the set takes none.

**The whole set is validated as it would stand afterwards.** A write builds the candidate — every rule, in order — and
judges all of it, so a write is refused for a defect it would leave anywhere in the set rather than only in the rule it
names. [What a rule set is checked for](#what-a-rule-set-is-checked-for) lists every check, and every defect is reported
together. **A refused write commits nothing**: no rule row changes, the version stays where it was, and nothing is
archived.

**Every committed write is archived.** It appends one row naming the account, the version it produced, the revision it
produced and the one it replaced, who wrote it, which surface it came through, when it committed, and every rule of the
set as it then stood, disabled ones and names included. That row is what a past run's revision is resolved against —
the newest archived version carrying that revision committed at or before the run started — and the record of who
changed what. It is pruned with the rule history under `MailRules:HistoryRetention`, except that the account's newest
row is never pruned and neither is one a retained execution or run still names.

These writes are use cases with no surface over them: no `mfctl` command, administrative route, or client route
calls one, so an account's rules cannot be changed from outside the process.

## A held rule set

**A stored set that no longer compiles runs none of its rules.** A set valid when it committed can stop compiling later
— the condition bounds are configuration, and a narrower `MaxConditionLength` or nesting depth makes a condition that
was within them a condition that is not. A pass that cannot compile every enabled rule of an account runs none of that
account's rules rather than the ones that still compile, because an ordered, stop-when-matched set with one rule taken
out is a set nobody wrote, and it can file mail a rule above it was written to keep.

While a set is held:

- **Arriving mail stays in the queue.** Nothing is recorded as evaluated, so the mail that arrived while the set was held
  is what the repaired set evaluates first, rather than mail it never sees.
- **A run somebody asked for stays outstanding.** One that had not started walking yet walks once the set compiles again,
  under the rules the repair left; one already part-way through ends as superseded, as it would after any change to
  the rules, and asking again runs it under the repaired set.
- **Every reading says so.** `mfctl rules list` and `mfctl rules show` print that no pass runs the rules and which rules
  no longer compile, by identifier, and `GET /rules` answers `held` with those identifiers. The reason is never quoted
  with the condition.

A disabled rule never holds a set, because no pass would compile it. A write repairs a held set the way it changes any
other: replacing or disabling the rules that no longer compile, or removing them.

## Previewing a rule

A preview evaluates one rule over the mail an account already stores and reports what it would match, **without changing
anything**: it asks the mail server for nothing, opens no change, records no evaluation and no history, and commits
nothing at all. It previews either a rule a person is still writing, validated exactly as a write would validate it
first, or a stored rule by its identifier — disabled or not, which is what a rule created disabled is for.

It reads the mail a pass would read, in the order a whole-mailbox run walks it, and evaluates the rule alone, as though
it were the account's only rule. Each matching message is reported with the changes the rule would ask for there. A page
evaluates at most 1,000 messages and reports at most 100 matches, and says where the next page continues, so previewing
a rule over a large mailbox is a sequence of bounded requests rather than one walk of all of it.

What a preview reports names messages, so it needs `mailfathom.admin.audit.read` over the account, the same grant the
history needs. Like the writes, it is a use case no surface publishes.

## Which triggers run a rule

`Triggers` is the list of automatic occasions a rule takes part in. It governs automatic firing and nothing else, so it
decides *when* a rule is reached rather than which mail it matches once it is.

| Triggers | What the rule takes part in |
| --- | --- |
| `Arrival` | Every message the account's synchronization run commits |
| `Schedule` | The occasions the rule's own schedule names, each of them a walk of the whole mailbox |
| none | No automatic occasion at all: nothing fires the rule, and a whole-mailbox run is what applies it |

**`Arrival` is a message the account's synchronization run has just committed.** It is named after the moment rather
than after the transport, because `Push` already names a folder's
[synchronization mode](imap-synchronization.md#choosing-the-mode-per-folder) and a rule is unaffected by which one its
account uses: mail a polled run commits reaches this trigger exactly as mail a watched one commits does.

**A rule that names no trigger runs on no arriving message.** That is a statement rather than an omission: the rule is
stored, validated, and reported, and a whole-mailbox run is what applies it. So a rule meant to file mail as it arrives
declares `Arrival` — there is no occasion a rule joins without naming it.

**Such a rule is one you run rather than one that runs**, which is what housekeeping wants — file everything
older than a quarter, delete what a mailing list left behind — where firing on each arriving message is either useless
or exactly what the user is afraid of. [A whole-mailbox
run](#running-the-rules-over-mail-you-already-have) is what applies it, and [a
schedule](#running-a-rule-on-a-schedule) is how the same rule gets applied without anybody asking each time.

**A rule a trigger does not reach is not evaluated and records no outcome**, exactly as a disabled rule is not: it did not decline to match, it was not one of that pass's rules. It follows that such a rule cannot end
a pass either, whatever `StopWhenMatched` says.

**A run somebody asks for is never a member of the list**, and it applies the whole rule set including the manual-only
rules. Somebody asking for a run is the request itself, so a rule declining to run because it had not agreed to be asked
would be surprising in the one place surprise is least affordable. `Schedule` is a member of the list for the opposite
reason: nobody is asking, so the rule has to have said in advance that it wanted to be run. No run selects rules by name
either: what a run applies is the account's enabled rules, narrowed only by what started the run.

**A write refuses a trigger that names nothing and the same trigger declared twice**, naming the rule, because the
value is a set. Neither is dropped: a list whose only entry was mistyped would otherwise arrive as an empty one, which
would silently turn an automatic rule into a manual one — and a rule that never fires is indistinguishable from a rule
nothing matched.

### Running a rule on a schedule

A rule declaring the `Schedule` trigger names its own occasions in its schedule, and each occasion is a walk of the
whole mailbox rather than anything about one message:

```text
Rule       archive-old-newsletters (0193a6f2-7d0a-7c51-8e3b-1f4a6c9d2e80)
Condition  contains(subject, 'newsletter') and ageInDays > 90
Runs on    Schedule (daily:03:00:Europe/Warsaw)
A match    relocate → archive
```

`mfctl` prints a schedule in its canonical form, so `Daily at 03:00 Europe/Warsaw` reads back as
`daily:03:00:Europe/Warsaw`.

**The syntax is MailFathom's own, and it accepts exactly two forms.** It is deliberately not cron: a user writing
when their own mailbox is tidied needs an interval or a time of day, and the four fields cron would add are ones nothing
here would honour to the minute anyway.

| Written | What it means |
| --- | --- |
| `Every <interval>` | An occasion every interval, where the interval is `hh:mm:ss` or `d.hh:mm:ss` — `Every 06:00:00`, `Every 7.00:00:00` |
| `Daily at <HH:mm>` | One occasion a day at that time of day, read in UTC |
| `Daily at <HH:mm> <zone>` | The same, read in a named time zone — `Daily at 03:00 Europe/Warsaw` |

- **The keyword is read case-insensitively and repeated spaces are ignored**, so `daily at 03:00` and `Daily at 03:00`
  are one schedule and the rule set's revision does not move between them.
- **An interval is at least one minute and at most 365 days.** A shorter one would ask for a walk of the whole mailbox
  more often than the queue is polled; a longer one is a date rather than a recurrence.
- **A time of day is written as `HH:mm` on a 24-hour clock**, zero-padded — `03:00`, not `3:00`.
- **The zone is a time-zone identifier the host recognizes**, so a deployment writing one gets that zone's wall clock
  including its daylight-saving changes. Leaving it out means UTC, which is what a user reads off the declaration
  without knowing where the container runs.
- **Anything else is not a schedule a rule can carry** — a cron expression, an unpadded time, a zone nothing knows. A schedule silently dropped would leave a rule that never runs,
  which is indistinguishable from a rule nothing matched.

**Occasions are anchored rather than counted from the last run.** `Every 06:00:00` means midnight, 06:00, 12:00, and
18:00 UTC whatever time the instance started, so restarting the process does not shift a schedule and two replicas
reading one account's rules agree on when its occasions are.

**A local time daylight saving skips happens when the gap ends, and one it passes through twice happens once.** A rule
at `Daily at 02:30 Europe/Warsaw` runs at the instant the clock reaches 03:00 on the spring-forward day, and on the
autumn day it runs at the first 02:30 rather than at both.

**An occasion that passed while nothing was running is skipped rather than replayed.** Whatever a process being down,
a queue being full, or a previous run still walking cost, the dispatch takes the most recent occasion and steps over the
ones before it — a mailbox does not want six walks because the host was off for a day, and each of those walks would
apply the same rules to the same mail. What the skip leaves is a count:
`mailfathom.jobs.schedule.skipped_occurrences`, broken down by why they were passed over, beside
`mailfathom.jobs.schedule.dispatches` for the decisions themselves. [Telemetry](../operations/telemetry.md#durable-background-work)
lists both.

**One run per schedule at a time.** An occasion arriving while that schedule's previous run is still in the queue or
still being walked is answered with the run already under way rather than starting a second one, and it counts as
skipped. It follows that a schedule shorter than the walk it asks for does not pile up; it runs as often as the mailbox
allows.

**A scheduled walk reaches the rules that declared a schedule, and no others.** That is the one place it differs from a
whole-mailbox run a user asks for, which applies the entire rule set including the rules declaring no trigger at all.
Both walk the same mail, are bounded by the same two settings, run as a step of the account's synchronization run, and
are bound to the rule-set revision they started under.

**A user's request replaces an outstanding scheduled run** rather than being answered with it, because the request
reaches every rule and the scheduled run reaches only some of them. A schedule's occasion arriving while any run is
outstanding stands down, whichever started it.

**A scheduled run is distinguishable from the other two.** The run says what started it — `mfctl rules run-status`
prints `under way, started by ScheduledRun` — and every outcome it records carries the trigger `ScheduledRun`, apart
from `Arrival` for the walk of arriving mail and `RequestedRun` for the one somebody asked for. [Finding out what a rule
did](#finding-out-what-a-rule-did) is where those records are read.

**Each scheduled rule is one schedule of its own account**, named `mail-rules:<account>:<rule identifier>`, so a mailbox
behind on its walk does not delay another's and renaming a rule keeps the schedule it had. Removing the rule, disabling
it, or taking its schedule away withdraws the schedule. Two rules declaring different schedules for one account are two schedules, and either occasion walks the
mailbox for every scheduled rule that account has — the walk is per mailbox rather than per rule, so a rule whose
occasion has not come round yet is still reached by one that has. Where that matters, give the rules the same schedule.

### No trigger and disabled say different things

The two sound alike and are not:

| | No trigger | Disabled |
| --- | --- | --- |
| In the set a pass compiles and runs | Yes | No |
| Validated by every write | Yes | Yes |
| Moves the revision | Only as its other parts do | Disabling it does, exactly as removing it would |
| Run when mail arrives | No | No |
| Run by a whole-mailbox run | Yes | No |
| What it is for | A rule a person applies deliberately | A rule not yet enabled, or taken out of service without deleting it |

## What a matching rule does

A rule declares what a match leads to as a list of changes, each named as the mutation it is requested through — the
name `mfctl rules show` and `GET /rules` report it by:

| Change | What it carries | What a match asks for |
| --- | --- | --- |
| `relocate` | a folder alias or role | The message leaves the folder it matched in and is filed into the named one |
| `copy` | a folder alias or role | A second copy of the message is placed in the named folder, and the one that matched stays where it is |
| `delete` | nothing | The message is removed from the folder it matched in |
| `set-seen` | read or unread | The message's remote `\Seen` flag is set, or cleared |
| `set-flagged` | flagged or not | The message's remote `\Flagged` flag is set, or cleared — the star a mail client draws |
| `add-keywords` | a list of keywords | Each named keyword is put on the message, beside the ones it already carries |
| `remove-keywords` | a list of keywords | Each named keyword is taken off the message, leaving the ones the rule did not name |
| `set-keywords` | a list of keywords, possibly empty | The message ends up carrying exactly the named keywords and no others |

**A change a rule does not declare is one it does not ask for**, which is why `set-seen` carries a state rather than
being a switch: a rule declaring `set-seen` to unread asks for mail to be marked *unread*, and a rule declaring no
`set-seen` does not touch the flag at all. `set-flagged` reads the same way. The distinction is sharpest on
`set-keywords`: an empty list is a rule asking for every keyword to be cleared, which is the one thing the other two
keyword changes cannot say however many keywords they name, while declaring none of the three is a rule that does not
touch keywords at all.

**A rule that declares no action is not a defect.** It selects mail and changes nothing, which is what a rule ending the
pass with `StopWhenMatched` does to keep the mail it names away from the rules below it.

A destination is a **folder alias** — one an account states under `Folders` in its own declaration — and never a path on the server. What that alias is bound to is resolved when the change is written down, so a rule goes on
working across a server that renames the folder underneath it, and a rule may name any folder the account maps —
including one it deliberately does not mirror, which is how mail is filed somewhere MailFathom keeps no copy of.
[Folder aliases and discovery](imap-synchronization.md#folder-aliases-and-discovery) states what a binding is
and when it moves.

A destination may instead name the **role** the folder plays, written `role:<role>` — `role:Junk`, `role:Archive`, and
the rest. That is what lets the rules of several accounts read alike when their folders were named differently, since the role is
asked of the account the rule belongs to. Anything without the `role:` prefix is an alias, so an alias spelled `Junk`
still means that alias. A write refuses a role no folder of the account carries, exactly as it refuses an alias the
account maps nothing for.
[What a role says, beside how a folder is found](imap-synchronization.md#what-a-role-says-beside-how-a-folder-is-found)
states what a role is and why an account has at most one folder per role.

### What a keyword may be

A keyword is a label a mail server stores on a message beside its flags, and mail clients show them as tags or labels.
The text is yours to choose, and the convention worth following is the one clients already use — a leading `$`, as in
`$Todo`, `$Invoice`, `$Waiting` — but nothing here requires it.

What IMAP requires is that a keyword be a single unbroken token, so a keyword may not contain a space, a control
character, any of `( ) { % * " \ ]`, or anything above US-ASCII — `café` is refused for that last reason and `cafe` is
not. One that does is refused, because a keyword that cannot be sent is a rule that cannot do what
it says. A keyword is at most 64 characters and
a rule names at most 64 of them in one key, which are the same bounds MailFathom reads a message's keywords under.
Keywords are compared without regard to case, so naming both `$todo` and `$Todo` is naming one keyword — and the
spelling you wrote is the one sent to the server.

`add-keywords` and `remove-keywords` naming no keyword are refused, because a list naming nothing asks the server for
nothing and is a mistyped list far more often than an intent. An empty `set-keywords` is the one that means something.

**A condition reads keywords as well as writing them.** [`keywords`](#the-facts-a-condition-can-read) is a fact, so a
rule can select on a label an earlier run put on a message, under the same case-insensitive comparison.

What it reads is what the server last reported, never what a rule asked for. A keyword a rule asks for in this pass is
a change written down: the account's next run carries it to the server, and the fact carries it once
[reconciliation](imap-synchronization.md#reconciling-against-the-server) has read the message's flags back. So a rule
below one that adds `$Invoice` does not see `$Invoice` on that message in the same pass, which is deliberate — a fact
that answered from a change nobody had performed yet would report labels the mailbox does not carry.

**A server may refuse to keep a keyword**, and that is the one refusal a write cannot report. A folder
tells MailFathom which flags it keeps permanently when it is opened, so a server that keeps no arbitrary keyword — some
do not — is discovered as the change is issued, and `add-keywords` and `set-keywords` then fail with
`MailboxMutationUnsupported` naming the account and the folder alias rather than being accepted and forgotten.
`remove-keywords` is never refused for that reason: taking a keyword off a message that carries one is meaningful
whatever the folder keeps.

### Which combinations a rule may declare

At most one action decides where the matched occurrence ends up — a relocation, a copy, or a deletion — and a deletion
admits nothing beside it. Beyond that, a rule may declare any mixture of flag and keyword changes, with one exception:
`set-keywords` states the whole set, so nothing else about the same message's keywords may be declared beside it.

| Combination | Verdict | Why |
| --- | --- | --- |
| `relocate` alone | permitted | |
| `copy` alone | permitted | |
| `delete` alone | permitted | |
| `set-seen` alone | permitted | |
| `set-flagged` alone | permitted | |
| `set-seen` and `set-flagged` | permitted | Two different flags, so neither decides anything about the other |
| `add-keywords` and `remove-keywords` | permitted | The removal is issued first, so a keyword named by both ends up on the message |
| `set-keywords` alone, including `[]` | permitted | An empty list is what clears every keyword |
| `relocate` and `set-seen` | permitted | The flag is written first, on the occurrence the condition matched |
| `copy` and `set-seen` | permitted | The same: the flag is written before the copy is placed |
| `relocate`, `set-flagged`, and `add-keywords` | permitted | Every flag and keyword change is written before the message is filed |
| `relocate` and `copy` | refused | Two fates for one occurrence; whichever ran second would act on a message no longer where the rule matched it |
| `relocate` and `delete` | refused | The same, and the deletion would undo the filing |
| `copy` and `delete` | refused | The same |
| `delete` and `set-seen` | refused | A flag written on a message being removed is a flag nobody will ever read |
| `delete` and `add-keywords` | refused | The same |
| `set-keywords` and `add-keywords` | refused | Two answers about one set of keywords; which one held would come down to which ran second |
| `set-keywords` and `remove-keywords` | refused | The same |

**A refused combination is refused where it is written.** The write fails naming the rule and which change could not be
honored beside which, rather than a run resolving it against a mailbox — no resolution invented at run time would
be the one the operator meant, and a rule that resolved differently depending on what a server answered would not be a
rule.

### The order they are applied in

The actions of one rule are applied in **MailFathom's order rather than the order they were written in**, so that every
permitted combination acts on the occurrence the condition matched, and so that the answer is the same on every run and
on every replica:

1. `set-seen`
2. `set-flagged`
3. `set-keywords`
4. `remove-keywords`
5. `add-keywords`
6. `copy`
7. `relocate`
8. `delete`

The same order governs the actions of two rules that both match one email, which no single rule's block could order on
its own.

Only two positions in that list decide anything observable. Every flag and keyword change comes before `copy`,
`relocate`, and `delete`, so all of them act on the occurrence the condition matched rather than on one that has already
been filed elsewhere; and `remove-keywords` comes before `add-keywords`, so a keyword one rule takes off and another puts
on ends up on the message. The rest is fixed for determinism — each of those actions writes a different flag, and the
one pair that would contradict another is refused before it is applied.

### How a change reaches the mail server

**A rule pass issues no IMAP command a rule asked for.** Each action a match asks for is written down as a
[durable mutation record](imap-synchronization.md#every-change-is-written-down-before-it-is-issued) inside the same
transaction that records the evaluation, and the account's own convergence pass — the first thing every account run does
— is what issues the IMAP commands. The one server call a pass makes for itself is
[finding a folder the account maps and does not mirror](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is),
made before the batch's transaction is opened and only where a rule files into such a folder. Three things follow, and
each is the point of the arrangement:

- **A pass that fails costs no mail server work.** Nothing the evaluation itself decided is carried remotely, so a local
  failure never defers the account's fetching.
- **A change survives a restart.** A record written and not yet carried is picked up by the next run, and
  [a change nobody finished finishes by itself](imap-synchronization.md#a-change-nobody-finished-finishes-by-itself)
  states every way one can be left and what becomes of it.
- **Asking twice asks once.** The record's identity is the email occurrence, the mutation, and who asked — and for a
  rule, *who asked* is the rule's identifier together with the revision of the rule set that matched. So a whole-mailbox run
  over mail a rule has already acted on issues nothing, while an edit to the rule set is a new revision and therefore a
  fresh request. A user who moved the message back by hand is not overruled by the rule that filed it. A held account
  opens no record to match against, so there a repeat is harmless only where the stored state already matches: a flag
  already set, a message already in the folder, and a message already in the trash each leave nothing to do. A person is
  not protected there the same way — a message moved out of the folder a rule filed it into is filed there again by the
  next whole-mailbox run.

**A change MailFathom made does not come back as something to act on.** A rule filing a message would otherwise meet it
in its new folder, match again, and file it again for as long as the folder is watched;
[a change MailFathom made is not a change to react to](imap-synchronization.md#a-change-mailfathom-made-is-not-a-change-to-react-to)
states how the record answers that exactly, without a cycle counter or a rate limit.

**What a deletion does is the account's decision**, taken from `AuthoredDeleteEmailDisposition` for the local copy and
`AuthoredDeleteServerDisposition` for the server at the moment the request is written, exactly as a deletion somebody
authored through a tool is.
[What becomes of a message MailFathom deleted itself](imap-synchronization.md#what-becomes-of-a-message-mailfathom-deleted-itself)
states the three local answers, and [a delete that only flags the message](imap-synchronization.md#a-delete-that-only-flags-the-message)
the server's other one. A rule that moves mail always expunges its source.

### What an account permits a rule to do

Each account states, under `RuleActions` in its own declaration, which changes a rule
may make to its mailbox:

| Key | Default | What it permits |
| --- | --- | --- |
| `Move` | `true` | A rule may file this account's mail into another of its folders |
| `Copy` | `true` | A rule may place a copy of this account's mail in another of its folders |
| `Delete` | `false` | A rule may remove this account's mail |
| `MarkAsRead` | `true` | A rule may set or clear this account's `\Seen` flag |
| `MarkAsFlagged` | `true` | A rule may set or clear this account's `\Flagged` flag |
| `WriteKeywords` | `true` | A rule may add, remove, or replace the keywords of this account's mail |

**Deletion is opt-in and everything else is opt-out**, because deletion is the one action whose result cannot be
undone by editing a rule afterwards.

`WriteKeywords` is one switch for all three keyword actions rather than three, because permitting an addition while
refusing a removal would leave mail accumulating labels nothing is allowed to take off again.

**A write leaving a rule that declares an action its account does not permit is refused**, naming the rule and the
action, and that holds for a disabled rule too. It is refused rather than skipped, because a rule that silently did less
than it declares would be indistinguishable from a rule that never matched. The remedy is to permit the action in the
account's `RuleActions` or to change the rule.

**Withdrawing a permission takes effect at the next pass, not at the next write to the rules.** The account's settings
and its rules are written independently, so narrowing what an account permits leaves a rule set nobody edited in force;
the permission is therefore read again when each change is written down, and an action it now refuses is
[a failed action](#when-a-change-cannot-be-made) rather than one more deletion. The rule itself stays as stored, and the
next write to the account's rules is refused until it is changed, which is the message that says what to edit.

### When a change cannot be made

Validation settles what a rule may declare, so what is left is what only the mailbox in front of the pass can cause.
Each is recorded against the rule that asked, and the actions beside it are still carried:

| Reason | What happened |
| --- | --- |
| `DestinationFolderUnresolved` | The destination names a folder the account mirrors and no run of that folder has bound it yet |
| `DestinationFolderUnmapped` | No mapping of the account answers to the name — one was withdrawn between the rule set being read and the change being written |
| `DestinationFolderNotAdvertised` | The mapping is there and the server holds no folder for it: the folder was deleted or renamed, the path was never right, or one the mapping asked to have created could not be |
| `DestinationFolderAmbiguous` | The mapping names a role that two advertised folders carry, so which one was meant is yours to state |
| `AccountNoLongerConfigured` | The account stopped being served between the rule set being read and the change being written |
| `ActionNoLongerPermitted` | The account has stopped permitting this action since the rule set that declares it was read |
| `EmailNotOnMailServer` | The stored email carries no occurrence, because no mail server holds it any longer, so there is nowhere a change could be carried to |
| `EmailNoLongerStored` | The account's mailbox is one MailFathom holds itself, and the email was erased between the pass reading it and the change being made |
| `EmailContentNotStored` | The account's mailbox is one MailFathom holds itself, and the action is a copy, but the message's own content is not stored — and a copy is a second stored message, which there is nothing to make out of |
| `LocalDestinationFolderMissing` | The account's mailbox is one MailFathom holds itself, and no local folder corresponds to the destination: its source folder has delivered nothing locally, or its local folder was deleted into the trash. No run will supply one, so the rule's destination is what to correct |

Nothing is written down in any of these cases: filing into whichever folder looked closest to the name is precisely
what a stale destination must not do. The account run reports how many changes it asked for, how many it withheld because
another matching rule had already settled the same message, and how many named something that no longer resolves,
together with the identifiers of the rules involved. Counts and rule identifiers only — no rule's name, condition, or
folder, and nothing derived from a message, reaches a log line, a metric, or a span.

On a held account every action is made to the stored email as the pass commits, in the transaction that records
the rule's decision, rather than written down for a server: a move files into the local folder the destination's role
or source folder corresponds to, and a delete moves the message into the local trash. A copy writes a **second stored
message** into that local folder — its own row, its own content, carrying the copied message's flags and keywords —
which is what [a copy becomes locally](imap-synchronization.md#what-a-message-mailfathom-copied-becomes-locally) on
any account. It answers the same way a move into the same place does: a destination no local folder corresponds to is
`LocalDestinationFolderMissing`, and copying a message into the local folder it is already in writes nothing and counts
as done. A delete meeting a message already
in the trash does nothing: erasing it is a person's act, taken with the window a person can withdraw it in, and never a
rule's. The rule's history records each such action as requested with no mutation identifier, because the change was
committed rather than recorded: there is no mutation behind it and no mutation trail to follow, so a requested action
without one is done.

## The facts a condition can read

A condition reaches these twenty-six names and nothing else. Each carries one shape of value, which is what the
comparison, operator, and function checks below are made against.

| Fact | Type | What it holds |
| --- | --- | --- |
| `account` | text | The configured alias of the account the email belongs to |
| `folder` | text | The configured alias of the folder the email occurrence is in |
| `folderRole` | text | The [special-use role](imap-synchronization.md#what-a-role-says-beside-how-a-folder-is-found) that folder plays — `Inbox`, `Junk`, `Archive` and the rest; absent when its mapping names none |
| `subject` | text | The subject line; absent when the email carries none |
| `senderAddress` | text | The sender's address in its comparison form; absent when the email names no sender |
| `senderDomain` | text | The part of the sender's address after the at sign; absent when there is no sender |
| `recipientAddresses` | text set | The addresses the email was sent to and copied to, in their comparison form |
| `recipientDomains` | text set | The distinct domains of every recipient address |
| `authorAuthentication` | text | What was established about the author the email displays — `authenticated`, `failed`, or `notEstablished`. Reached by the receiving server, or by local DKIM verification where that server wrote nothing; a rule reads the conclusion and not which of the two produced it |
| `senderTrust` | text | Whether this deployment recognizes that author — `trusted`, `known`, or `unknown` |
| `receivedAt` | timestamp | When the last receiving hop recorded the email; absent when no hop recorded one |
| `sentAt` | timestamp | When the sender's client stamped the email; absent when it carries no such header |
| `ageInDays` | number | Days since the email was received; absent when nothing recorded that |
| `sizeInBytes` | number | The size of the whole email as the server reported it |
| `attachmentCount` | number | How many attachments the email carries |
| `attachmentTotalBytes` | number | The size of every attachment added together |
| `isEncrypted` | boolean | Whether the email's body is encrypted |
| `carriesUnverifiedSignature` | boolean | Whether a signature part is present anywhere in the message; it says nothing about whether the message's own signature verified |
| `isSeen` | boolean | Whether the server reports the email as read |
| `isAnswered` | boolean | Whether the server reports the email as answered |
| `isFlagged` | boolean | Whether the server reports the email as flagged |
| `isDraft` | boolean | Whether the server reports the email as a draft |
| `keywords` | text set | The keywords the server reports the email as carrying; empty when it carries none |
| `hasExtractedContent` | boolean | Whether text has been extracted from the email's body |
| `bodyText` | text | The text extracted from the email's body, after quoted history and signatures were removed; absent while no extraction has run for it |
| `machineAuthorship` | text | How much the email's own text reads as machine written — `likely`, `possible`, `unlikely`, or `notAssessed` |

Names are case-sensitive. `senderDomain` is a fact and `SenderDomain` is not, so the surface documented here and the
surface accepted are the same one.

**Every fact resolves only if the condition names it, and once per email however many rules name it.** Twenty-four of
the twenty-six come from metadata a pass already holds, so they cost nothing to read. `folderRole` is read from
configuration, which costs no read either. `bodyText` is the exception: it reads
stored content, which is why a rule set naming it nowhere pays for no read at all, and one naming it in five conditions
pays for one. The boolean operators short-circuit, so a condition whose first half already decides it never resolves
what its second half would have named.

**An absent fact answers rather than failing.** A message with no subject compares unequal to every subject, matches no
`contains`, and reports `isNull(subject)` as true. Guarding a comparison is a choice about what the rule should mean,
never a requirement.

**A timestamp is always in UTC.** A date literal is written `#yyyy/MM/dd#` and carries no offset of its own, so
`receivedAt >= #2026/01/01#` means the start of that day in UTC.

**Text comparison ignores case and is ordinal.** `senderDomain == 'Supplier.TEST'` matches `supplier.test`, and it does
so identically on every instance whatever locale its host is set to. That is what makes `keywords` symmetrical with the
keyword actions: a rule that put `$Todo` on a message is selected by a later rule naming `contains(keywords, '$todo')`.

**Three of the facts are verdicts this deployment stored when the email was extracted**, and reading one re-evaluates
nothing: no DNS is resolved, no header is re-read, and no text is re-assessed. So a condition names what was concluded
at the time rather than what the same policy would conclude now. The one part read as it stands is whether a contact
holds the sender's address: `senderTrust` asks the account's contact books when the rule is evaluated, exactly as
every other read of the message does ([sender authentication](sender-authentication.md#the-books-are-read-when-the-mail-is-read)).
Mail stored before a verdict existed reads as the value that says nothing was established — `notEstablished`, `unknown`, and `notAssessed` — until
[`mfctl mailbox rederive`](imap-synchronization.md#bringing-stored-mail-up-to-a-later-release) fills it in.
[Sender authentication](sender-authentication.md) and [machine authorship](machine-authorship.md) state what each value
means and what it is worth.

**`senderTrust` says little on its own and is written beside `authorAuthentication`.** `unknown` is the ordinary state
of legitimate mail from a correspondent nobody has named, so a rule acting on it alone acts on most of a mailbox; what
carries a real statement is `authorAuthentication == 'failed'`, which is a receiving server reporting that the
displayed author did not satisfy their own domain's published policy.

**`known` is a value of its own, and `senderTrust == 'trusted'` does not match it.** It says the address the email
displays is a contact, one of the owner's addresses, or at one of the owner's domains, while nothing established that
its domain sent the email — a `From` header anybody can write, which a relaxed account (the default; see
[`Strict`](../operations/configuration-mail.md#one-account--a-mailbox-in-a-users-record)) still marks. A rule meant to file `known` mail with
`trusted` mail names both: `senderTrust == 'trusted' or senderTrust == 'known'`.

**`machineAuthorship` publishes the band and not the number behind it.** The likelihood a reader is shown is a
heuristic comparable only within one weighting, so a rule written against a threshold would change meaning the next
time that weighting moved. `likely` is not an accusation and warrants no action on its own — a rule acting on it is
filing mail, not judging anybody.

## Operators

| Operator | Written | Takes | Produces |
| --- | --- | --- | --- |
| Conjunction | `and`, `&&` | two booleans | boolean |
| Disjunction | `or`, `\|\|` | two booleans | boolean |
| Negation | `not`, `!` | a boolean | boolean |
| Equality | `==`, `!=` | two values of one type, other than a text set | boolean |
| Ordering | `<`, `<=`, `>`, `>=` | two numbers, or two timestamps | boolean |
| Membership | `in`, `not in` | text, a number, or a timestamp, then a parenthesized list of two or more of its type | boolean |
| Arithmetic | `+`, `-`, `*`, `/`, `%` | two numbers | number |
| Sign | `-`, `+` | a number | number |
| Choice | `? :` | a boolean, then two values of one type | that type |

Grouping is by parentheses and precedence is the conventional one, so
`a == 1 or b == 2 and c == 3` reads as `a == 1 or (b == 2 and c == 3)`. Write the parentheses.

`in` needs a list of at least two values — `folder in ('inbox', 'archive')`. A single parenthesized value is not a list,
and `folder in ('inbox')` is refused with a message saying so; write `folder == 'inbox'`.

A text set — `recipientAddresses`, `recipientDomains` — is neither compared nor tested with `in`, because a set has no
single value to compare. `contains` is how a set is asked about a member, and both refusals say so.

**Everything else the expression language offers is refused when the condition is compiled**, including the bitwise and
shift operators, `**`, the null-coalescing `??`, the pattern-matching `like`, and the factorial `!` suffix. The last two
are refused for cost rather than tidiness: a factorial turns a short expression into arbitrarily large arithmetic, and a
pattern match takes an authored pattern and runs it against mail content.

## Functions

| Function | Takes | Produces |
| --- | --- | --- |
| `contains(searched, term)` | text or a text set, then text | boolean |
| `startsWith(text, prefix)` | two texts | boolean |
| `endsWith(text, suffix)` | two texts | boolean |
| `isNull(value)` | any value | boolean |
| `isNullOrEmpty(text)` | text | boolean |
| `if(condition, whenTrue, whenFalse)` | a boolean, then two values of one type | that type |
| `in(value, first, second, …)` | text, a number, or a timestamp, then one or more of its type | boolean |

`contains` is the one function that reads two shapes: over text it looks for a substring, and over a text set it looks
for a member equal to the term. Both are literal searches rather than pattern matches, which is what keeps the cost of a
text condition the length of the two strings and nothing else.

**No other function is available**, and naming one is refused when the condition is compiled rather than at evaluation.
The expression language ships a mathematical library — `Sqrt`, `Max`, `Round` and the rest — and none of it is part of
this surface.

## Limits

| Limit | Default | What it bounds |
| --- | --- | --- |
| `MaxConditionLength` | 1000 characters | How long one condition may be written |
| `MaxConditionNestingDepth` | 16 levels | How deeply the parsed condition may nest |
| `ConditionEvaluationTimeout` | 1 second | How long one condition may take, including resolving the facts it names; at most 30 seconds |
| Rules per account | 200, fixed | How many rules one account keeps, enabled or not |

The first two are checked when a condition is compiled, so a write leaving a condition over either is refused with the
rule named, and a stored condition over a bound narrowed since it committed [holds its set](#a-held-rule-set). The
depth limit is checked twice there — once over the written text before it reaches a parser, because a parser descends
into a nesting before there is anything to inspect, and again over the parsed condition, which is what catches depth
reached through operators rather than brackets.

The timeout is checked while a condition runs, because nothing readable in the text says how long a read of stored
content will take. It has a ceiling of its own for the reason the two written limits do: a timeout an operator meant as
milliseconds and wrote as minutes would stop bounding anything, per email.

A length limit and a depth limit are both needed: length alone would admit a short expression nested past what anyone
can follow, and depth alone would admit a flat expression of ten thousand terms.

## What a rule set is checked for

Every write judges the whole set as it would stand afterwards, and every condition in it is compiled — a disabled
rule's included, so enabling a rule is never the moment its defects are discovered. Compiling one checks four things:

- **Syntax.** The condition parses, and a failure reports the position it failed at.
- **The names.** Every identifier is one of the twenty-six facts and every call is one of the seven functions.
- **The types.** Every comparison, operator, and argument holds between shapes that could match. `subject == 1`,
  `sizeInBytes > 'large'`, `recipientDomains == 'example.test'`, and `contains(subject, 3)` are each refused here.
- **The result.** The condition produces a boolean. A condition producing text or a number is refused rather than read
  as truthy, so what a rule means never depends on a coercion nobody wrote down.

The set itself is checked beside those four: it keeps at most 200 rules, no two of them share a name without regard to
case, and no identifier appears twice. So is each rule's own shape: a name of 1 to 128 characters with no control
character, triggers that each name something and none twice, and a schedule exactly when the rule declares the
`Schedule` trigger — a rule declaring the trigger carries one, and a rule carrying one declares the trigger.

So are its changes, against the rule itself and against its account:

- **The destinations.** Each names a folder alias or a `role:<role>`, and each names a folder the account maps — a rule
  filing into a folder no mapping declares has nowhere to file. Mirroring is not asked about: a mapped folder the
  account does not mirror is resolved when the first change files into it.
- **The combination.** The changes are ones MailFathom applies together, per [the table above](#which-combinations-a-rule-may-declare).
- **The permissions.** Every change is one the account's `RuleActions` permits a rule to take.

Every defect in every rule is reported together, so a set with three mistakes is fixed in one write rather than three.
Each names the rule by its identifier and says what is wrong, and none quotes the rule's name, a folder, or a keyword,
because each of those is text a person typed. A condition that does not compile is reported the way the compiler read
it — the position it stopped at, and the one fact or function name it could not read — because that is what says what
to correct. A refusal goes back only to whoever asked for the write, and none is logged.

## Order, and stopping

Rules are evaluated in position order, skipping the disabled ones and the ones the walk's trigger does not reach. That
order is the whole of the contract — nothing sorts, groups, or reorders a set — so two rules that both match one email
produce the same outcome on every run and on every replica.

`StopWhenMatched` on a rule that matches ends the pass, and the rules below it are not reached. It defaults to `false`.
A rule that does not match never stops a pass whatever it declares.

**Position order also settles what two matching rules do to one email.** Their actions are gathered in that order, and
an action the ones already gathered leave no room for is **withheld** — judged by exactly the table one rule's own
actions are judged by, so two rules naming incompatible fates settle the way one rule declaring both would have been
refused. Two rules filing one email into different folders therefore file it into the folder the rule placed first
names; the same two rules the other way round file it the other way; and a rule deleting an email leaves nothing for a
later rule to file. Whatever survives is then applied in the fixed order above, so a flag a later rule asked for is
still written before an earlier rule moves the message.

A withheld action is reported with the name of the rule that asked for it, because a rule that appears not to have fired
is otherwise indistinguishable from one that never matched. `StopWhenMatched` is the way to say which rule owns a
message rather than leaving it to be settled this way.

## When rules run

**Rules are evaluated as a step of the account's synchronization run, and there is no schedule of their own.** That run
already has everything a rule pass needs — one account at a time, a slot count that stops one account starving another,
a jittered backoff when something is wrong, and a shutdown that lets work in flight finish — so evaluation joins it
instead of adding a second thing to configure and watch.

**A pass reads its account's rules once, when it starts, and holds them to the end.** A write committed while a pass is
running reaches the next pass and never the one running, so no pass applies half of one set and half of another. The
compiled conditions are kept in the process by the set's version, so a pass compiles an account's rules only after a
write changed them, and a write on another replica is noticed by the next pass there because it reads a version it has
not seen.

The step comes after every folder has synchronized and committed, after the classification pass, and outside the
synchronization transaction entirely. Only one thing runs after it, and it runs after it because of this pass: the cut
that gives a message its passages, which waits until the rules have finished so that nothing derived describes a folder
a rule was about to move the message out of. Waiting for the rules is not by itself enough for that, because a rule
declares a move rather than performing one and the account's *next* run is what carries it to the mail server — so the
cut also passes over a message whose relocation is still converging, and cuts it once it is in the folder it ended up
in. [The arrival pipeline](../architecture/arrival-pipeline.md) draws the whole order. Two consequences of this step's own position follow that are worth stating rather than inferring. A rule
can only ever see mail the run before it has already stored, so a provider redelivering a message or a synchronization
retry cannot produce a different processing boundary than a clean run. And nothing an MCP tool does waits on a rule:
reads are served from what is already stored, and a pass neither blocks one nor is blocked by one.

**A pass reads nothing out of a folder the account does not mirror.** Such a folder keeps whatever it stored before its
synchronization was switched off, and neither the arrival queue nor a whole-mailbox run walks those rows: their flags
are whatever they were on the day the switch was flipped and nothing will ever correct them, so a rule reading one
would act on a mailbox MailFathom stopped observing. The exclusion is applied where the candidates are read rather than
after they come back, which is what keeps such a message from sitting at the head of the arrival queue forever.

**Both passes reach only the folders the account's configuration mirrors.** A rule is evaluated against mail in a folder
a mapping names and leaves synchronized, and against no other mail. Two kinds of stored mail are outside that, and both
are mail this deployment keeps: a folder whose `Synchronize` was switched off keeps what it had already stored, and a
folder whose mapping was removed keeps it as well. Neither is evaluated again, by an arrival pass or by a whole-mailbox
run, because nothing refreshes either — a rule acting on such a message would flag, file, or delete mail against a
mailbox state nobody here is still reading.
[What a mapping decides beyond where the folder is](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is)
states what each of the two is and what makes both unreachable everywhere else too.

**A copy of this deployment's own outgoing mail is not arriving mail.** A message MailFathom
[filed](mail-delivery.md#the-copy-in-the-accounts-own-folders) into the account's own sent folder comes back
through the next synchronization like anything else, and it is recognized as this system's own and joined to the send
it is a copy of. Neither pass ever offers it to a rule: a rule conditioned on mail arriving would otherwise fire on
what the user just sent, and a rule that files or deletes would act on the copy of a message the record above it still
governs. The exclusion is a column on the row, applied where the candidates are read and repeated in the queue's own
partial index, so such a message leaves the queue rather than sitting at the head of it — and it is never stamped as
evaluated, because it was not.

**Junk is not offered to the rule set at all.** Where spam classification is switched on, a message in the account's
junk folder and a message a verdict called spam are left out of both passes, so a rule set is never fired by mail
somebody else chose to send. A message no verdict has been reached about is left out too and stays a candidate, so the
pass that evaluates it is the one after classification has decided or after the message's wait has run out — which is
the same ordering everything derived from mail follows here, and it is applied where the candidates are read rather than
after they come back.
[Junk is kept out of what a deployment derives from mail](spam-classification.md#junk-is-kept-out-of-what-a-deployment-derives-from-mail)
holds the whole of it, including the bound on the wait and what a failed classification does. With classification off,
which is the default, nothing here is narrowed.

**A rule declaring `Arrival` applies to mail that arrives after the rule exists.** Each message is evaluated once, and
the record of that
evaluation is what takes it out of the queue the next pass reads — so editing a rule changes what happens to the mail
that arrives from then on and does nothing to the mail already in the mailbox. Mail an instance stored before it had
rule evaluation at all is recorded as evaluated when the schema is applied, for the same reason: an upgrade must not
hand a first rule set an entire mailbox's history as though all of it had just arrived. Re-running the rules over mail
already stored is the whole-mailbox run below, and it is always something somebody asks for.

**A pass is bounded, and what it leaves behind is the next run's.** It reads, evaluates, and commits `EvaluationBatchSize`
messages at a time and takes at most `MaxEvaluationBatchesPerPass` batches, so an account whose mail has never been
evaluated drains over as many runs as its size needs instead of holding up the run that fetches its mail. Each batch
commits its evaluations together with the position they account for, so a restart resumes at the message nobody read
rather than replaying a batch or stepping over one.

**A message whose body text has not been extracted yet is skipped and stays eligible.** It applies only where the rules
that walk actually runs for the account name `bodyText`, which is decided per walk — so a rule only a whole-mailbox run
applies holds nothing up on arrival, and a rule declaring `Arrival` does: such a message is left in the queue and
evaluated once its text has been
derived, rather than evaluated against a fact that would answer absent and then never reconsidered. Mail whose payload
local storage has not had headroom for is waited on the same way, because a later run fetches it as soon as the ceiling
permits. A message whose content will never yield text is evaluated now with the fact absent, because waiting for text
that is not coming would stall the queue behind it. Two messages are that: one above the size limit, which every later
run refuses for the same reason, and one whose stored payload a reader has already failed to read, which every later
run would fail to read the same way.

**A rule that cannot answer for one message costs that message's rule and nothing else.** It is recorded as a failed
rule with a reason, the rules below it still run, the remaining messages of the batch are still evaluated, and the
message is recorded as evaluated. The account is not put into backoff for it either: nothing a rule decided is carried
remotely, so fetching the account's mail less often would answer a local problem by slowing remote work that had nothing
to do with it. The next section lists the two reasons.

**Nothing scans on a timer of its own.** The account run recurs already, so anything a scan would find on arrival is
found by the `Arrival` trigger; a rule whose condition only becomes true with the passage of time — mail older than some
age — is applied by a whole-mailbox run, which the user asks for or a rule's own
[schedule](#running-a-rule-on-a-schedule) asks for on its behalf. A schedule adds no loop and no second worker either:
its occasions are dispatched as ordinary jobs by the queue's worker, under the same capacity bounds as every other job,
and the walk still happens as a step of the account's synchronization run.

## Running the rules over mail you already have

Editing a rule is only useful if the rules can be applied to mail that arrived before the edit, and that is what a
**whole-mailbox run** is: a walk over everything stored for the folders one account mirrors, evaluating each message
again under the rules now in force.

- **One per account, and asking twice is asking once.** A request that finds a run already outstanding is answered with
  that run rather than starting a second walk of the same mailbox. The one exception is a run [a schedule
  started](#running-a-rule-on-a-schedule), which a request replaces rather than is answered with, because a request
  reaches rules a scheduled walk does not.
- **It runs where every other pass runs.** The request records that the run is wanted and nothing more; the work happens
  as a step of the account's synchronization run, bounded by the same two settings. Whoever asked is not what keeps it
  alive, so closing a terminal does not cancel a walk of a mailbox.
- **A run you ask for applies the whole rule set.** Every enabled rule of the account is run, including the ones that
  declare no automatic trigger, and nothing selects a subset of them for one run. A run a
  schedule started is the one narrower case: it applies the rules that declared a schedule, which is what they opted
  into.
- **It is bound to one rule set.** The revision in force when the run starts walking is recorded with it, so a write
  cannot change what the run is doing halfway through. If the rules do change while a run is outstanding, the run ends
  as **superseded** and says so, because finishing under a different set would apply two rule sets to one mailbox.
  Asking again re-runs under the rules now in force.
- **It is resumable and cancellable.** Its position is committed with each batch, so a restart or a shutdown costs at
  most the batch that was in flight, and the run finishes over as many account runs as it needs.

**`mfctl rules run --account <id>` is what asks for one**, and `mfctl rules run-status --account <id>` is where it is
watched from. Both are documented under [reading the rules, running them, and finding out what they
did](../operations/admin-endpoint.md#reading-the-rules-running-them-and-finding-out-what-they-did). Neither waits for
the walk, because the run is carried by the account's synchronization runs rather than by whoever asked.

**A rule's own [schedule](#running-a-rule-on-a-schedule) asks for one of these walks without anybody typing the
command.** Such a run is the same walk under the same bounds and is watched the same way; it differs in reaching only
the rules that declared a schedule, in saying `ScheduledRun` where a requested run says `RequestedRun`, and in giving
way to a request rather than answering it.

## Finding out what a rule did

A rule that concluded something about a message leaves a record of it: the rule's identifier and the name it then
had, the revision it ran under, whether the
condition matched, the facts it read, and each change it asked for with what became of that change. `mfctl rules
history --account <id>` reads it, narrowed to a rule with `--rule` or to a message with `--email`. That is the answer
to both "what is this rule doing" and "why is this message here", and it is the only place either is answered — a rule
publishes nothing about itself through MCP, and a run reports counts rather than which message went where.

Three distinctions the record exists to keep:

- **Reached and answered no** is recorded; **never reached** leaves nothing at all. A rule below one that ends the pass
  is the second case, and reading it as the first is what makes a rule placed below the wrong one look like a condition
  that is never true.
- **Could not answer** is neither of those. It is recorded as failed with its [reason](#when-a-condition-cannot-answer)
  rather than folded into a non-match.
- **A change that was refused** is distinguishable from one **another rule had already settled**, and from a rule that
  simply asked for nothing. The refusal carries its classification, and the change that gave way names the rule that
  declared it.

A change that named its folder by role is recorded against the **folder it resolved to**, so the history names the alias
mail was actually filed into rather than the word the rule was written with. One that never resolved a destination —
a role this account maps no folder with, an alias nothing has bound — names no folder at all, and its
[refusal](#when-a-change-cannot-be-made) is what says why.

**The facts are recorded by name and never by value.** That a condition read `senderDomain` is kept; what the domain
was is not, and neither is a subject, a matched span, or any other value the message supplied. The revision recorded
beside them is what the expression is retrievable from — the account's [archive](#how-a-change-is-written) keeps what
each revision declared for as long as a record names it — so the reasoning is reconstructible without the record
becoming a second copy of the mailbox. What a change did to the mail server is likewise not restated here: the record points at
the mutation it opened, and [what happened to that
mutation](imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default) is the
mutation's own trail to answer.

The record inherits the obligations of the mail it names. Erasing a message erases what every rule concluded about it,
and `MailRules:HistoryRetention` bounds how long the rest is kept — thirty days by default, and a window of zero keeps
it until the mail itself goes.

## When a condition cannot answer

Validation catches an unknown fact and a comparison that could never hold, so neither reaches an evaluation. What is
left is what only the email in front of the condition can cause, and each is recorded as a rule that **failed** with a
reason:

| Reason | What happened |
| --- | --- |
| `EvaluationFaulted` | The expression raised a failure while it was being evaluated for this email |
| `EvaluationTimedOut` | The evaluation, including resolving the facts it named, outlasted `ConditionEvaluationTimeout` |

A failed rule is never read as a match and never as a non-match. It did not match, so it never stops a pass, and the
pass carries on to the rules below it — one unlucky email must not stop a rule set that works from being applied.

## The revision a pass runs under

An account's rule set is identified by a digest over its enabled rules, taken in position order, rendered as twelve
lowercase hexadecimal characters. The identity is derived rather than declared, so it cannot be left stale by a write
that forgot to bump it, and every replica reading the same rows names the same revision.

The revision is not the version. The version counts writes, moves by one with every committed write, and is what a write
is checked against; the revision names what the set means, and is what a run is bound to and a history record carries.
So a rename moves the version and leaves the revision alone.

What moves the revision and what does not:

- Changing an enabled rule's condition, its changes, whether it ends the pass, its triggers, or its schedule moves it.
- Enabling, disabling, adding an enabled rule, or removing one moves it. A rule created disabled leaves it alone, and
  enabling a rule unchanged after disabling it returns the set to the revision it had before.
- Moving an enabled rule to another position moves it, because the order is part of what a set means.
- Renaming a rule leaves it alone, because the identifier stands in for the name: a name is what a person reads, and it
  changes nothing about which mail the rule reaches.
- Every write to a disabled rule leaves it alone.

The identity carries none of the authored text and no ordering. A record naming a revision therefore holds nothing
personal that a condition contributed, and a record that has to say which of two revisions came first carries its own
timestamp rather than inferring one. A revision means something only beside its account: two accounts holding the same
text under different identifiers have different revisions.

The revision is also half of what makes a change idempotent, so moving it has a consequence worth stating: a rule whose
changes were edited asks afresh for mail it has already acted on, while an unchanged rule re-evaluated under the same
revision asks for nothing it has already asked for.

## Worked conditions

A single sender:

```text
senderAddress == 'billing@supplier.test'
```

Anything from a domain, in one of two folders:

```text
senderDomain == 'supplier.test' and folder in ('inbox', 'archive')
```

Large mail with attachments that nobody has read:

```text
attachmentCount > 0 and attachmentTotalBytes > 10000000 and not isSeen
```

A domain, an attachment, and an age together:

```text
senderDomain == 'supplier.test'
  and attachmentCount > 0
  and ageInDays > 30
```

Something in the subject or, failing that, in the extracted body:

```text
contains(subject, 'invoice') or contains(bodyText, 'invoice')
```

Received this year, unless it is a draft:

```text
receivedAt >= #2026/01/01# and not isDraft
```

Mail addressed to a particular domain that carries no readable text yet:

```text
contains(recipientDomains, 'example.test') and not hasExtractedContent
```

Size measured in a unit a user thinks in:

```text
sizeInBytes / 1048576 > 25
```

Mail a rule of your own already labelled, which is what makes a keyword worth writing:

```text
contains(keywords, '$Invoice') and not isSeen
```

Mail whose displayed author failed their own domain's published policy:

```text
authorAuthentication == 'failed'
```

Mail from nobody this deployment recognizes, whose author nothing established either:

```text
senderTrust == 'unknown' and authorAuthentication != 'authenticated'
```

Bulk-generated mail from outside, filed without asking a model anything:

```text
machineAuthorship == 'likely' and senderTrust == 'unknown'
```

## Worked rules

Each rule below is written the way `mfctl rules show` reports it — its condition, what runs it, and what a match does —
and each belongs to the account it is stored on.

Filing supplier invoices and marking them read, so that the rules below never see them:

```text
Rule       supplier-invoices
Condition  senderDomain == 'supplier.test' and attachmentCount > 0
Runs on    Arrival
A match    relocate → invoices, set-seen → read; ends the pass
```

Keeping a copy of everything a regulator sends, without disturbing where the message itself sits or whether it has been
read:

```text
Rule       regulator-copies
Condition  senderDomain == 'regulator.example'
Runs on    Arrival
A match    copy → compliance
```

Putting automated notices back in front of somebody by clearing the flag, and nothing else:

```text
Rule       unread-alerts
Condition  contains(subject, 'alert') and isSeen
Runs on    Arrival
A match    set-seen → unread
```

Marking mail from a handful of people for attention, leaving it exactly where it arrived:

```text
Rule       star-the-board
Condition  senderDomain == 'board.example'
Runs on    Arrival
A match    set-flagged
```

Labelling mail so it can be found by tag in any mail client, without moving it out of the inbox, with `$Invoice` and
`$Todo` as the keywords:

```text
Rule       label-invoices
Condition  contains(subject, 'invoice') and attachmentCount > 0
Runs on    Arrival
A match    add-keywords
```

Taking a label off once the thing it stood for is over, by stating the whole set — `$Invoice` and `$Done` — rather than
adding to it:

```text
Rule       settle-invoices
Condition  contains(subject, 'payment received') and ageInDays > 1
Runs on    nothing automatically; 'mfctl rules run' applies it
A match    set-keywords
```

Deleting mail nobody needs — which the account has to permit under `RuleActions:Delete`, and which a write refuses if
it does not:

```text
Rule       drop-build-notifications
Condition  senderAddress == 'builds@ci.example' and ageInDays > 7
Runs on    Arrival
A match    delete
```

Quarterly housekeeping nothing fires by itself, which the user applies by asking for a whole-mailbox run:

```text
Rule       retire-old-newsletters
Condition  contains(subject, 'newsletter') and ageInDays > 90
Runs on    nothing automatically; 'mfctl rules run' applies it
A match    relocate → archive
```

The same housekeeping without anybody asking, run once a night in the user's own time zone:

```text
Rule       retire-old-newsletters-nightly
Condition  contains(subject, 'newsletter') and ageInDays > 90
Runs on    Schedule (daily:03:00:Europe/Warsaw)
A match    relocate → archive
```

Selecting mail and changing nothing, which is what a rule owning a message ahead of the rules below it looks like:

```text
Rule       leave-family-mail-alone
Condition  senderDomain == 'family.example'
Runs on    Arrival
A match    nothing; ends the pass
```
