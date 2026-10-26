# Spam classification

<!-- describes: backend/src/Domain/Spam/**, backend/src/Application/Spam/**, backend/src/Application/Folders/IJunkMailFolderCatalog.cs, backend/src/Infrastructure/Persistence/Spam/**, backend/src/Infrastructure/Observability/DerivedWorkGateTelemetry.cs, backend/src/Infrastructure/Observability/SpamModelStageTelemetry.cs, backend/src/AI/SpamClassification/**, backend/src/Infrastructure/Mail/Mime/MimeKitEmailSpamHeaderReader.cs, backend/src/Host/Configuration/Spam/** -->

A mailbox that an assistant reads is a mailbox somebody else can write to. Mail written to deceive a reader is
indistinguishable from correspondence once it is a row in a timeline, and the receiving mail server has usually already
decided what it thought of it — in an `Authentication-Results` header, in a provider's `X-Spam-*` header, or by filing
the message in the junk folder. Spam classification is what keeps that decision rather than discarding it, and records
it as derived data beside the message.

Seven things ship here and are independent of each other: the classification record with the two stages that fill it,
mail being classified as it arrives, the three switches that let a verdict reach the mail server, the person's own
verdict, spam and the junk folder being kept from every agent's read, junk being kept out of everything this deployment
derives from mail, and the floor that gives every message in the junk folder or carrying `$Junk` at least a spam verdict.
The fifth and the seventh reach a mailbox with no classification at all.

## No agent reads a message judged spam

A spam message is written by somebody hostile, and an agent is a model acting on what it reads, so a phishing message's
instructions would otherwise reach a model acting for the mailbox's owner. **An agent never reads a message judged
spam.** Two things are withheld from every agent's read:

- **A message whose current verdict is `Spam`**, whichever stage reached it — the deterministic one, the model, or the
  person — and whichever folder holds it.
- **The junk folder's mail**, unless the person marked the message not spam, which is the person saying it is ordinary
  mail. A stage's own `NotSpam` does not lift it: the folder is where the provider or the person put the message, and
  only the person's verdict outranks that.

It holds on every surface through which an agent reads mail: `list_emails`, `search_emails`, `get_email_content` and the
conversation it expands, `ask_mail` and the retrieval it answers from, the contact correspondence an agent reads, a
conversation's state, `reply_to_email`, `forward_email`, a draft answering a message, an attachment link an MCP read
minted, and the service's own agents — the conversation agent, Discover, reply drafting, and the acts an answer
performs. Retrieval never ranks a passage of a withheld message into an agent's context, whether or not the index holds
one, because the vector and the full-text windows are narrowed by the same predicate as a listing. No argument of any
tool asks for any of it back.

The rule is read from the verdict when the read is made, so a message marked spam drops out of the very next read and
one marked not spam comes back in it, with nothing reindexed in between. A message asked for by its identifier is
answered exactly as one that does not exist, so a refusal does not tell an agent that something is there.

The person is not an agent. The client keeps showing spam — folded, badged, and read-only as the client draws it — and
every read the person makes through it reaches the junk folder as before. Rules and automation act on the verdict as
they always have, and an act an agent requests by an identifier — setting flags, moving, deleting — returns no message
content and is not withheld.

Which folder is the junk folder comes from configuration rather than from what a server advertised — the `Junk` special
use in the account's folder mapping, described in [IMAP synchronization](imap-synchronization.md). A deployment that
maps no junk folder withholds only what a verdict names. A folder withheld from AI with `VisibleToAI: false` stays out
of every agent's read whatever its mail's verdicts are, because the two decisions are held apart and a query is handed
both. That switch withholds the folder from AI and from nothing else, and the classification described on this page is
not AI: a deterministic verdict still reaches the folder's mail exactly as it reaches any other mirrored folder's.
[Mailbox queries](mailbox-queries.md) holds the rest of what narrows a read.

## What a classification records

One classification per stored email, replacing whatever was recorded for it. It is derived data of the same kind as an
embedding: computed locally, never mirrored to the mail server, and never a statement about where the message lives or
which flags it carries. Where a message lives is the server's, which is why acting on a verdict is a separate decision
behind switches of its own — described in [what an operator can let a verdict do](#what-an-operator-can-let-a-verdict-do).

| What it holds | Why |
| --- | --- |
| The verdict — undetermined, not spam, or spam | Undetermined is an ordinary answer: it says nothing was found either way |
| The stage that decided it — the deterministic stage below, a decision model, or the person | Which of them a verdict rests on is the first thing anybody questioning it needs. Every verdict a stage reaches is the deterministic stage's or the model's; the person's own verdict is the third kind a record carries |
| What kind of spam it is, when a stage named one, and the phishing detail beside it | A phishing message needs a warning and marketing needs filing; [what kind of spam it is](#what-kind-of-spam-it-is) holds what may be named and by whom |
| The score and the threshold it was judged against, when the message stated both | A score without its threshold cannot be read: the same number is spam under one configuration and ordinary mail under another |
| The signals the verdict rests on: the deterministic stage's in the order it produced them, then, where a model answered, the model's own judgement | An operator diagnosing a wrong verdict asks *which* authentication method failed and *what* the provider header said |
| The profile the verdict was reached under | A short digest of the terms the verdict was decided under. It is what makes *this message was already decided under the terms now in force* a question somebody can ask of a record that already exists |
| When it was evaluated | |

A signal is one fact: its kind, its name, what was observed, and where it came from — a header field, a folder
placement, or a decision model, named by the build that answered and the instruction revision it was asked under. The signals stay separable rather than being merged into one opaque number, which is what makes the record
answerable rather than merely present.

The record is bounded: at most 64 signals, and an observation longer than 512 characters is shortened. A record the
deterministic stage alone decided keeps the first 64 it produced. A record a model answered for always keeps the model's
judgement signals, after the deterministic ones, and drops the deterministic stage's tail to make room for them — so a
message carrying many headers cannot push out the judgement its verdict rests on.

### What kind of spam it is

A `Spam` verdict can carry one **spam subcategory**: `Phishing`, `BusinessEmailCompromise`, `MalwareLure`, `Scam`,
`Extortion`, or `Marketing`. **The spam subcategory is not a mail category.** It says what kind of spam a spam message
is, and nothing about mail that is not spam; sorting all mail by topic is a different question, and nothing here takes
that name.

The record refuses what no stage could have concluded:

- a subcategory on a verdict other than `Spam`;
- a subcategory with nobody to have named it. The deterministic stage names none, so outside the person's own verdict a
  subcategory needs a model judgement among the record's signals, whose provenance names the model build that answered
  and the revision of the instructions it was asked under;
- a verdict decided by the model with no model judgement behind it.

A spam verdict without a subcategory is whole: it is what the deterministic stage reaches on its own. The model can name
the subcategory of a verdict the deterministic stage reached, and that verdict stays the deterministic stage's, so the
record tells *the model decided this is spam* from *the mail server decided, and the model named the kind*. Which stage
named a subcategory follows from the one that decided the verdict — the person names the one on their own verdict and the
model every other — and both administrative readings print it.

Beside phishing the model also names what the message asks for — credentials, payment details, a one-time code,
something else, or nothing stated — and beside phishing and business email compromise the **kind** of party it pretends
to come from: a bank, a courier, an online service, a colleague, an executive, a supplier, another party, or nobody in
particular. It is the kind and never the name, because a decision model writes no text; where a person needs a name, the
message's own sender display name is in front of them. Only the model names either of the two.

**The person's verdict outranks every stage.** It is spam, with or without a subcategory, or not spam — and its not spam
is a statement the person made rather than a check that found nothing. Neither a reclassification nor a
[classification run](#classifying-the-mail-you-already-have) under any profile replaces it; a run counts it as skipped.

This build fills the record from the deterministic stage, from the [model stage](#the-model-stage) where an account
consults it, and from [the person's own verdict](#the-persons-own-verdict), so a subcategory this build records is the
model's or the one a person named.

**The profile covers the model's terms and no other operator setting.** The deterministic stage reads what a message
carried, and nothing an operator configures moves its answer. What it reads is part of the digest all the same, so a
build that teaches it a new fact names a new deterministic profile and the next run decides once more the mail the old
one decided. This build reads a message's junk keywords, which is why the first run after an upgrade to it re-decides
existing mail, and a message carrying `$Junk` or `$NotJunk` can change its verdict there once. The model stage's terms — whether it is consulted, whether
it names the subcategory of deterministic spam, and the threshold its probability is judged against — are what the
digest adds when the model is consulted, so a change to any of them re-decides mail on the next run, and a deployment
that does not consult it keeps the deterministic profile. Every record a stage writes names the profile it was decided
under; the person's own verdict names none, and a run skips it whatever its profile. The folders an account classifies
are deliberately outside it: adding a folder changes which mail is classified and not what any classification would have
concluded. A stage's record whose profile differs from the current one — or that names none — was decided under other
terms, and a [classification run](#classifying-the-mail-you-already-have) classifies it again rather than skipping it.

## The deterministic stage

It runs on every classified message, and it needs nothing deployed beside this service. It reads the stored message's
header block —
never its body, never its MIME tree, never an attachment — with the folder the message is in and the keywords it
carries, and records four kinds of fact.

**Sender authentication.** Every outcome each `Authentication-Results` header states, once per authenticating hop, with
the properties the hop wrote beside it. `ARC-Authentication-Results` is read too and recorded as its own kind: an ARC
outcome is a relay's signed claim about what *it* saw, which is a weaker statement than what this mailbox's own server
saw. A failure is recorded and decides nothing on its own — a message the receiving server chose to deliver despite a
DMARC failure is a message about which something is known, not a message this system overrules the server about.

This reading is deliberately not the one [sender authentication](sender-authentication.md) performs. Here every hop's
claim is collected as a signal to be weighed against the others; there exactly one server is believed and the rest are
ignored, which is what makes that verdict a statement about who sent the message rather than evidence about it.

**Provider spam headers.** `X-Spam-Flag`, `X-Spam-Status`, `X-Spam-Score`, and `X-Spam-Level`. The flag is read before
the status, so a message that disagrees with itself is answered by the one field with two accepted values. A score
becomes an assessment only where the same header carries the threshold it was judged against; a bare
`X-Spam-Score: 15.2` is recorded as a signal and produces no numbers, because nothing says what scale it is in.

**Junk folder placement.** A message in the account's junk folder is spam, and the placement outranks a header saying
otherwise: somebody's filter already acted on this message, and that action is more recent than the header.

**Junk keywords.** RFC 9051 § 2.3.2 gives a mailbox three standard keywords for this, and other mail clients set and
read them: `$Junk`, `$NotJunk`, and `$Phishing`. They are read from the keywords stored for the message, without regard
to case, and each one present is recorded as a signal of its own.

| The message carries | The verdict | Why |
| --- | --- | --- |
| `$NotJunk` alone | Not spam, over every other signal — the junk folder and a provider's header included | It is a person's correction, made in whichever client they use |
| `$Junk`, or `$Phishing`, without `$NotJunk` | Spam, with no subcategory | Another client, the person, or the receiving server's filter marked it; `$Phishing` is read as junk, as RFC 9051 asks, and naming the kind stays a model's |
| `$Junk` and `$NotJunk` together, both somebody else's | Neither: the verdict is reached as if no junk keyword were there | RFC 9051 reads the pair as no statement at all |

A keyword is read when the message is classified — on arrival, by a run reaching a record decided under other terms, or
by a run with `--rescore` — and setting one does not by itself classify the message again, for the reason
[reclassifying is explicit](#classifying-is-idempotent-and-reclassifying-is-explicit). A `$NotJunk` set on a message
already decided spam therefore turns its verdict on the next run that reaches it.

**No agent sets any of the three through MailFathom.** `set_mail_flags` and every other path an agent or an MCP caller
changes keywords through refuses an addition or a removal naming `$Junk`, `$NotJunk`, or `$Phishing`, and refuses a
replacement outright, since a replacement takes off whatever it does not name from what the server carries when it is
performed and could strip a mark the person set in another client. That is because an agent that has just read the mail would otherwise be steered into turning the verdict on the message
steering it — and with the verdict, what every agent may read. The person sets them through the client, as part of
[their own verdict](#the-persons-own-verdict), and another mail client sets them on the server; [ADR 0007](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
records the decision as its axis O.

A message that already carries `$Junk` when it is first stored is therefore spam on arrival wherever it sits, as a
server that marks junk without filing it can leave one in the inbox; [what an operator can let a verdict
do](#what-an-operator-can-let-a-verdict-do) decides at once whether it is filed.

A message whose headers cannot be parsed is classified from its folder and its keywords alone, which is a weaker
classification and an honest one. One unreadable message does not stop a run.

### What MailFathom did is never read back as evidence

A junk folder placement that a verdict of this deployment filed, and a `$Junk` or `$Phishing` that a verdict of this
deployment wrote, are not signals. Each act a verdict takes on a message is recorded beside the message, in every custody
phase, and the stage reads that record before it reads the message: otherwise the next run would find the message in the
junk folder because the last verdict put it there, and a verdict could never be undone by the terms that reached it
changing. A message an earlier build filed, before acts were recorded, is recognized from the relocation that verdict
asked for instead.

The record answers by kind of act rather than by who made the mark the message now carries. **Once a verdict has filed a
message, no junk folder placement of it counts again, and once a verdict has written `$Junk` or `$Phishing`, no `$Junk` or
`$Phishing` on it counts again** — whoever moves it back into junk or sets the keyword afterwards, and also where the
keyword was asked for and a folder that keeps no such keyword refused it. A `$NotJunk` is never one of them: beside a
`$Junk` this deployment wrote it is the person correcting it, and it decides as it would anywhere.

**Leaving the junk folder is not a correction.** Nothing re-decides a message because it moved, so a message moved out
of junk, in MailFathom or in another client, keeps its verdict until something classifies it again. Then what stands
depends on what the verdict rested on. The deployment's own filing and keywords are no signal either way, so a verdict
resting on what the message itself carries — a provider's spam header — stands again and turns only on `$NotJunk`. A
verdict resting on somebody else's placement or keyword — the receiving
server's filter, another client's `$Junk` — is decided again on what the message carries then, so moving it out of junk
or taking that keyword off turns it at that next classification. A first run after upgrading to this build is one such
classification.

None of this can take a message still in the junk folder, or still carrying `$Junk`, below spam: the deployment's own act
decides nothing, and [the floor](#junk-mail-is-never-below-spam) beneath every stage holds such a message at spam all
the same.

### Junk mail is never below spam

**Every message that sits in the account's junk folder, or that carries `$Junk`, carries at least a `Spam` verdict, in
whatever folder it is.** It may carry a subcategory as well, but never less. This is a floor beneath classification
rather than a widening of it, so it needs no classification switched on and no folder in the classified set — the Inbox
default stays as it is — and it reads where a message is and which keywords it carries, nothing of its content.

A message below the floor is given a `Spam` verdict with no subcategory, recorded as the deterministic stage with the
placement or the keyword as its signal and under the deterministic stage's own profile. A classification run under terms
that consult a model therefore reaches it again and may name a subcategory above the floor, while a run under the
deterministic terms counts it as decided. Where the account classifies, what was already derived from the message is
removed in the same transaction, as for any spam verdict; where it does not, nothing gates the cut, so nothing is.

| The message | What the floor does |
| --- | --- |
| In the junk folder, or carrying `$Junk` without `$NotJunk`, with no verdict or a verdict below spam | Raises it to `Spam`, whoever filed it or set the keyword — this deployment's own verdict included |
| Already `Spam`, with or without a subcategory | Nothing: the floor only raises, and never takes a subcategory away |
| Carrying the person's own verdict, `NotSpam` included | Nothing: the person's verdict outranks every stage. A message marked not spam can sit in the junk folder — on an account whose `MoveToJunkFolder` is off, while its move to the Inbox is on its way, when the server refused it, or when another client moved it back |
| Carrying `$NotJunk` alone among the marks somebody else set | Nothing: it is the person's correction from another client, and decides `NotSpam` as above |
| Carrying `$Junk` beside `$NotJunk` | The keyword counts as neither, as RFC 9051 reads the pair, so only the junk-folder placement raises it |

It is the one place this deployment's own act still counts. The rule above keeps a model's verdict from being promoted
into a deterministic one by its own filing or its own keyword, and it still does: an own act decides nothing in any
stage. What the floor narrows is that such a message can no longer fall below spam while it sits in the junk folder or
carries `$Junk` — a reclassification, a profile change, and the model stage all end at spam at least. Leaving the junk
folder does not lift the floor's verdict either, for the reason [above](#what-mailfathom-did-is-never-read-back-as-evidence):
it stays until something classifies the message again or the person marks it not spam.

**Only the verdict is raised.** The floor writes nothing to the mail server: whether a keyword is written, whether the
message is marked read, and whether it is filed stay with the account's switches, which act on a floor's verdict exactly
as on any other when a classification acts on it. It is not an AI function either, so a folder withheld from AI with
`VisibleToAI: false` is covered exactly like any other, and the junk folder is the one the account maps to the `Junk`
role, the one agent reads are withheld from.

**It is applied in two places.** A classification applies it after every stage, so a message classification reaches is
recorded at the floor or above with every signal it carried. Every account's synchronization run applies it too, as a
step after the folders and ahead of the classification run, the rules, and the cut, in every custody phase and whether or
not the account classifies — so a new arrival, a message another client moved into the junk folder, and a keyword
another client set are raised in the run that read them. Both places read the folder the message is in now rather than
the one it was synchronized from, so on a held or restoring account a message the person moved into the local junk folder
is held at spam by a classification run as well as by the step. A message classification still awaits is left to it until the
[wait](#waiting-is-bounded-and-running-out-of-patience-is-not-a-failure) a verdict is allowed runs out, and is raised by
the first synchronization run of its account after that, within the step's per-run bound — so a stalled or unqueued
classification leaves a message below the floor for the wait and up to one more run, or longer while a backlog above the
bound is drained. The step reads at most
`SpamClassification:RunBatchSize` × `SpamClassification:MaxRunBatchesPerPass` messages per run, in identity order, and
the run after an upgrade reaches the mail stored before the floor existed, so the rule holds for every message rather
than only for new ones. A message it raised is no candidate afterwards, so a mailbox already at the floor costs one
bounded query per run.

A second replica finding the work done is the ordinary case. The write is conditional on the record holding neither spam
nor the person's verdict when it is read, and a record written after that read conflicts and is read again, so two
replicas reaching one message leave one verdict. A run that raised anything logs how many messages it raised, and
nothing about any of them.

## The model stage

Where an account switches it on, a decision model is asked about a message once the deterministic stage has read it. It
is the only part of classification that sends anything out of the deployment, so it is off unless the account's record
says otherwise, and the deterministic stage runs the same whether it is on or not.
[ADR 0041](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md)
records the shape of the question and what it is shown.

### Two jobs

| The deterministic stage concluded | What the model is asked | What the record says |
| --- | --- | --- |
| Not spam, or undetermined | Whether the message is spam, and of which kind | The model's verdict, decided by the model. Spam when its spam probability is at or above the account's threshold, with the kind it named; not spam, with no kind, below it. The probability and the threshold are recorded as the assessment |
| Spam, the [floor](#junk-mail-is-never-below-spam)'s included | Only which kind — and only where the account also switched on naming the kind of deterministic spam, since junk is often most of a mailbox and each one is a call | The deterministic verdict, still decided by the deterministic stage, with the kind the model named beside it |

The question is one choice over seven options — not spam and the six kinds — and the provider reports a probability for
each. The spam probability is one minus the probability of *not spam*, and the kind is the most probable of the six. The
second job is offered the six kinds alone, so naming a kind can never overturn a verdict the mail server reached.

Beside phishing the model is asked what the message asks for, and beside phishing and business email compromise who it
pretends to come from, as [what kind of spam it is](#what-kind-of-spam-it-is) describes. Anything the record would refuse
is dropped rather than repaired: a kind below the threshold, a phishing request beside another kind, and an impersonated
party beside a kind that impersonates nobody.

The model is never asked about a message somebody else marked `$NotJunk`. That is the person's correction, and a model
judgement is not evidence against it. It is not asked either while the account's model stage is off, or about
deterministic spam while naming its kind is off.

### What the model is shown

- the sender's address and display name, and the `Reply-To` address where it names another mailbox;
- the subject;
- the plain-text body, cut to `SpamClassification:ModelStage:MaxBodyCharacters` (8 000 by default), with the model told
  where it was cut;
- for each followable link, the host it leads to, the host its words name, and whether the two differ — the same reading
  that makes the reading pane warn about a link — up to 32 of them, read from the first 100 000 characters of each of the
  body's representations rather than from the part of it that is sent, so a long body or a heavy stylesheet costs no link;
- the declared type and name of up to 16 attachments, a name cut to 128 characters, never their content;
- what the account's trusted receiving server established about the displayed author — authenticated, failed, or not
  established — as the message's [provenance](../operations/configuration-mail.md) records it, and whether this
  deployment trusts the authenticated sender.

Nothing else is: no other recipient, no other header, no HTML, no picture, no attachment's words, and nothing the
person decided. The `Authentication-Results` headers themselves are not shown: a message can carry any number of them
in its own hand, and only the one the account's trusted server wrote is believed, so the model is told the verdict read
from that one rather than every claim the message makes about itself.

The body's bound is in characters, and the decision model's own `MaxRequestCharacters` is measured on the request as it
is sent, where a character outside ASCII travels as a longer escape. So the input is cut further where that is what it
takes for the request to fit — the body first, since it is written last — and mail in another script is asked about
rather than refused as too large. The message is handed over inside an envelope that escapes its markup, under instructions that say its
text is data to judge rather than instructions to follow, so a message telling the classifier what to answer is one more
thing to judge it by. The answer is a choice among fixed options, and the model writes no text that is kept.

### Where it goes

To the decision model `Decisions:SpamClassification:Model` names, or `Decisions:DefaultModel` where that is unwritten —
never to a chat model — through the router and whichever provider it picks, which are two third parties. Every call goes
through the same decision client as every other decision function: inside a run's budget and the period's spend, with
its resilience and its telemetry, and reported as
[ADR 0022](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md)
asks. One message is one run.

What leaves crosses the `spam_classification_input`
[egress point](sensitive-content-scanning.md#the-guarded-egress-points), so an account whose posture scans for sensitive
content has the display name, the subject, the body, the hosts, the media types, and the file names redacted before they
are sent. The addresses are not: an address is what the stage judges a sender by, and ADR 0041 records why it crosses.

### When no answer comes back

The stage is whole or absent. A provider that cannot be reached, a call past its budget, a period already spent, a
request too large for the declared model, an answer outside the options, and a sensitive-content scanner that cannot
run each leave the deterministic result recorded exactly as it would have been, with no model signal and the
deterministic profile — so the next classification run asks again. Each is counted on
`mailfathom.spam.model_stage.consultations` by job and by outcome, as [telemetry](../operations/telemetry.md) lists.

A deployment that declares no decision model for spam classification asks nothing. A record switching the stage on is
refused when it is written there, naming `Decisions:Models`. A record that asked for it before the model was taken away
is classified deterministically: startup reads the served account records and logs a warning naming each such
account, and each message it would have asked about
is counted as `not_activated`. A message whose stored content cannot be rendered has nothing to
show a model and is recorded under the account's own terms, since reading it again reads the same bytes.

How many calls run at once is bounded per replica by `SpamClassification:ModelStage:MaxConcurrentCalls`; a call past
the bound waits for a slot rather than failing.

A model's spam verdict acts through the same switches as any other, and the same rule holds: a message a verdict filed
once and somebody moved back is [never filed again](#a-message-the-user-moved-back-is-never-filed-again), whichever
stage reaches spam the second time.

### Folders withheld from AI

The model stage is an AI function, so it never reads a folder withheld from AI — one mapped `VisibleToAI: false`, or
one mapped `Synchronize: false`, which keeps nothing AI may read. An account record that
switches the stage on while any folder it classifies — its `ScannedFolders`, or its inbox where it names none — is
withheld from AI is refused, naming the account and the folder, and so is a record that withholds such a folder while
the stage is on. The deterministic stage is not AI and keeps classifying that folder's mail either way.

## Each account decides this for the mail in it

Junk is a judgement about one mailbox, and every action below writes to that mailbox's mail server. So **the
posture is the account's**: whether the mail in it is classified at all, which of that account's folders are classified,
and what becomes of the result. An account may be left unclassified entirely, and no deployment setting requires
otherwise.

**It is the account rather than each person assigned it because the mail is one copy.** A mailbox two people read is
classified once, and a verdict that filed a message moved it on the server for both of them whatever either would have
asked for — so a deployment whose two people want a mailbox classified differently is describing two mailboxes. It also
means that changing one account's posture changes that mailbox and no other, including the other mailboxes of the same
person.

**What stays the deployment's is what costs it a resource**: how long a message may wait for a verdict before the index
moves on without it, how wide one pass of a classification run is, and how many model calls run at once and how much of a
body each one carries.

An account's posture is read from that account's own record, described in
[classifying this account's mail](../operations/configuration-mail.md#classifying-this-accounts-mail--spamclassification).
**A record and the deployment's `SpamClassification` section are never unioned**: switching classification off in a
record actually switches it off rather than falling back to whatever the file still says. Nothing carries the section's
posture into a record, and a user's record carries no such block at all — one naming it is refused as a property nothing
binds.

A folder resolves within that account's own mapping and nowhere else — both the folders its mail is classified over and
the folder its junk is filed into. A name only another account carries is answered exactly as one this deployment does
not serve, and is refused where filing is switched on.

This reaches every deployment, one served user or several. `SpamClassification:Enabled`,
`SpamClassification:ScannedFolders`, and the `SpamClassification:Actions` block name nobody's posture and are read by
no classification: a deployment that stated them and recorded nothing on its accounts classifies nothing. State them on
each account instead.

## What an operator can let a verdict do

Three switches, independent of each other and **all off by default**. With none on, a verdict is recorded and no
mailbox is written to, which is what makes classification safe to watch for a while before it is allowed to act.

**File junk in the junk folder.** The message is moved on the mail server, through the same durable mutation record and
the same write session that carries a change somebody made by hand — described in
[IMAP synchronization](imap-synchronization.md#every-change-is-written-down-before-it-is-issued). The local row changes
afterwards, because synchronization observed the move; nothing writes a folder locally ahead of the server. What an
operator agrees to by switching this on is that spam moves in every client they open, and that correcting a false
positive means dragging the message back in any one of them — there is no MailFathom-specific undo to learn, and the
next section says what happens when they do.

**Mark it read.** The remote `\Seen` flag is set. This is the one authored act that sets it: synchronization and content
retrieval still never do, so reading mail on the user's behalf goes on leaving the flag alone. A message the server
already reports read is not written to at all.

**Mark it junk with keywords.** `$Junk` is added, and `$Phishing` beside it where the verdict's spam subcategory is
phishing, so every client that understands RFC 9051's junk keywords shows the message as junk and warns about phishing.
It is a keyword addition carried by the same durable record as the other two acts, and the account's `WriteKeywords`
permission still governs it: an account that refuses keyword changes gets none, and the other switches go on acting.
`$Phishing` is never written without `$Junk`, because a client is asked to warn only where both are set, and **no verdict
ever writes `$NotJunk`** — RFC 9051 reads it as *definitely not junk*, which only the person can say, and a verdict that
found nothing is not that statement. A keyword the message already carries is not written again. A folder whose
`PERMANENTFLAGS` keeps neither `\*` nor the keyword refuses it, which ends that change as unsupported while filing and
`\Seen`, each a change of its own, still go ahead.

With the same switch on, **a message carrying `$Junk` and `$NotJunk` together has the `$Junk` taken off**, as RFC 9051
asks of a client that finds both, whatever the verdict and on a message moved back out of junk as well. Only a filing
withheld until its destination or its account is known again holds the removal back with it, because what the switches
ask is then performed whole once it is. A pair somebody else set is cleared whole, a `$Phishing` beside it included —
the pair cancelled that too, and left alone it would decide junk at the next classification — and on a spam verdict
that removal stands in place of the `$Junk` the verdict would otherwise owe, on that run and every later one. A `$Junk` this deployment wrote is taken off
alone, with a `$Phishing` it wrote beside it, because the `$NotJunk` next to it is the person correcting that verdict and
is what has to stand. It is asked for once per message, read from the record of what verdicts did, so a pair somebody
sets again after it was cleared is left to them on a mirrored, a held, and a restoring account alike.

With more than one on, **flags and keywords are set before the move**. On a server without RFC 6851 `MOVE` a relocation
gives the message a new UID, so a flag stored afterwards would be aimed at an occurrence the source folder no longer
holds.

Nothing else is ever done. No delete, no flag other than `\Seen`, no keyword added other than `$Junk` and `$Phishing`,
none taken off but the pair above and a `$Phishing` beside it, no folder created, nothing sent.
[ADR 0007](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md)
admits a spam verdict among the authors of a flag or keyword change and refuses the rest. A verdict's own acts need
nothing new from it; this feature reopened it only for axis O, which narrows who may write the three junk keywords.

**On an account whose mailbox MailFathom holds itself**, there is no server to write to, so every act is made to the
stored message as the classification commits, where the junk destination is a mapping the account mirrors: the message
is marked read, given its keywords, and filed into the account's local junk folder in that transaction, with an audit
entry for each where the account keeps one. Nothing is written down to converge later, but what the verdict did is: the
same transaction records each act beside the message, which is what lets a held message moved back out of junk be
recognized as one already filed, exactly as a mirrored one is. **An account being restored to its source commits the
same way and also records**: where the message still stands on the source, the same transaction opens the records the
run carries there, so a verdict reached during the restore reaches the mailbox instead of being undone by it, and the
answer names them beside the acts it applied. The restore itself carries every keyword a held message has wherever
the source folder keeps them, so a held mailbox goes back to its source with each verdict in the form other clients
read; a folder whose `PERMANENTFLAGS` keeps neither `\*` nor every keyword the message holds takes it back with none. **An unmirrored junk destination
resolves to nothing on a held account**, and so does one no local folder corresponds to, so where filing is on the
verdict ends as an unresolved destination and no act is made — the message stays unread where it is.

**What can decide on a held account whose source only relays mail** is stated rather than left to be discovered: the
headers the source delivered a message with, the folder it delivered it into, and the `$Junk` or `$NotJunk` the person
sets, through MailFathom's own keyword acts or in any client while the source still holds the message. A relay that runs
no filter, writes no `X-Spam-*` header, and files nothing leaves the person's own keywords as the whole of what decides,
and `mfctl spam` says so in its help.

### Where junk is filed, and why it need not be mirrored

The destination is whichever folder the account maps to the `Junk` role, or a folder named explicitly in the
configuration. **It does not have to be a folder MailFathom mirrors, and for most deployments it should not be**: the
point of filing spam is to be rid of it rather than to move it around inside the instance. Filing into an unmirrored
folder takes the local copy with it, under the account's own answer about mail it deletes; filing into a mirrored one
files identically and leaves the message stored, excluded from listing and search by the rule at the top of this page.
[IMAP synchronization](imap-synchronization.md#what-a-mapping-decides-beyond-where-the-folder-is) holds what a folder's
participation decides.

The folder is found the same way a rule's destination is, through the one resolution every author of a filing shares:
a mirrored folder is read from the binding its own run recorded, and an unmirrored one is resolved against what the
server advertises the first time a filing needs it. **Classification asks for no folder to be created.** Whether a
mapping creates its folder when the server advertises none is that mapping's own declaration in the synchronization
section, decided once for every author rather than by this feature.

An account that maps no destination at all fails startup naming that account, rather than leaving its spam unfiled with
nothing said about why.

### A message the user moved back is never filed again

Two rules keep filing from becoming an argument with the person whose mailbox it is.

A message already in the destination is not moved into it. And a message this feature has already asked to have filed,
which is not in the destination, is **left alone** — not filed again, not marked read again, and given no keyword, the
one removal of a `$Junk` and `$NotJunk` pair above being the only change still asked of it. Either
somebody moved it back out, which is exactly the correction a false positive is supposed to have, or the change is
written down and has not been carried out yet; asking again would argue with the first or duplicate the second. That is
read from the durable mutation record, and from the record of what the verdict did that every custody phase keeps,
rather than from the message, because those records are what survive the message moving.

The same holds for a keyword. A `$Junk` or `$Phishing` a verdict already wrote, which the message no longer carries, is
not written again: somebody removed it, and a client removing a junk keyword is exactly the kind of correction this rule
leaves alone. `$Phishing` can still follow a `$Junk` that is there, when a later verdict names the subcategory.

The two readings are deliberately not told apart. Doing so would mean treating a change that ran out of attempts as
licence to file afresh — and a message that sat there failing to move is precisely the one somebody is most likely to
have moved by hand.

The protection reaches as far as the mirror does. A junk folder MailFathom does not mirror is one it stops seeing into,
so a message moved back out of *that* folder arrives as new mail and is classified afresh — which is the trade an
unmirrored destination makes, and the reason a deployment that expects to correct verdicts often may prefer a mirrored
one.

### Every spam verdict is acted on

There is no score to act at. A spam verdict rests on the provider's own decision or on where the receiving server filed
the message, both taken with network context nothing after delivery has, and a provider's score is in a scale this
deployment knows nothing about — so with a switch on, every spam verdict reaches it. Switching the actions off is the
edit that stops the acting while the verdicts go on being recorded.

### Asking twice is one change

A change is written down under an identity made of the occurrence, the mutation, and what asked for it — and what asked
here is the **stage that decided the verdict**, together with the keywords for a keyword change, so asking for
`$Phishing` after `$Junk` is a change of its own rather than the first one again. Classifying the same message again
therefore asks for nothing new. A
rule and a classification are told apart on the record by their origin, so an operator reading a stuck change knows
whether to look at a rule they wrote or at this section.

Where a filing has nowhere to go — a mapping withdrawn while mail was being classified, a mirrored folder no run has
bound yet, a folder the server advertises none of, or a role two folders carry — **nothing is written down at all**,
including a `\Seen` or keyword change asked for beside it. The same holds for an account a reload stopped declaring, whose answer
about mail it files away is then nobody's to guess. Marking spam read while leaving it in the inbox takes the unread
marker off mail that is still there, which is worse than waiting; a later attempt performs the pair whole.

## The person's own verdict

The person marks a batch of messages **spam**, naming one subcategory for the whole batch or none, or **not spam**,
through `POST /api/client/mutations/spam` — [the client endpoint](../operations/client-endpoint.md#marking-mail-spam-or-not-spam)
holds the request, the batch bound, and the answers. What it records is a verdict decided by the person, which no stage
replaces: a reclassification, a [classification run](#classifying-the-mail-you-already-have) under any profile, and a
change of profile all leave it standing and count it as skipped. **Marking not spam is the one way a spam verdict is
lifted** — moving a message out of the junk folder is not a correction, as above. Marking a message again is the same
act: the verdict is replaced with the one just stated, so marking spam with another subcategory changes the subcategory
rather than adding a second verdict. Each mark asks for every keyword below rather than for what the server last reported
missing, so a mark undone before the first one reached the server is corrected by the second one's changes, which reach
it afterwards.

**The keywords follow the verdict into the mailbox**, so every other mail client reads the correction:

| The person says | Added | Taken off |
|---|---|---|
| Spam | `$Junk`, and `$Phishing` where the subcategory is phishing | `$NotJunk` |
| Not spam | `$NotJunk` | `$Junk` and `$Phishing` |

They are the person's own keyword acts, so the account's `RuleActions:WriteKeywords` governs them and
`SpamClassification:Actions:MarkWithJunkKeywords` does not: an account refusing keyword changes records the verdict and
writes none. Each is carried by the durable record every keyword change is, before any move, and a folder whose
`PERMANENTFLAGS` keeps neither `\*` nor the keyword ends that change as unsupported without undoing the verdict.

**Whether the act also moves the message is the account's `SpamClassification:Actions:MoveToJunkFolder`.** With it on,
spam is moved into the account's junk destination after its keywords and recorded as a filing — so, exactly as for a
verdict's filing, [a message moved back out is never filed again](#a-message-the-user-moved-back-is-never-filed-again) —
and not spam moves a message in the junk folder to the inbox. With it off, nothing moves. A destination that resolves to
nothing leaves the verdict and its keywords recorded and the message where it was, and the answer says it did not move.
A mark on mail of an account whose switch files junk asks for the `mailfathom.mail.move` grant beside
`mailfathom.mail.flags.write`, for the whole batch before any of it is written, and a mark's move passes the checks a
person's own move does — a junk folder withheld from them, or one across the edge of the drafts or the sent tree, is no
move, and the verdict stands where the message is. The switch is read once per account for a batch, so a switch reloaded
partway through one changes neither its grant nor its moves. The act asks for no confirmation of its own; a call is
taken as confirmed. `Actions:MarkAsRead` is never applied: the person
said what the message is, not that they have read it.

**Moving a message into the junk folder through the client is marking it spam**, in the same act and whatever the switch
says — with no subcategory, or the one the person already named. The mark is committed with the move or not at all, so
a move into junk that is not made — the message is already there, or the move is not allowed — marks nothing. Moving
it out changes no verdict; marking it not spam does. A move an agent or an MCP caller asks for is a move and nothing else.

A message marked spam loses its passages and attachment text in the same transaction, as
[a junk verdict does](#what-a-junk-verdict-removes); on an account whose classification is off nothing withholds it
afterwards, so the next sweep of derived work derives from it again. One marked not spam is admitted again: once it is outside a junk
folder, the next sweep of derived work selects it like any other admitted message. No verdict files it back into junk —
the switches act on spam verdicts alone, and a stage cannot replace the person's.

**In every custody phase.** On a mirrored account the keyword changes and the move are written down for the account's
next run, which the act brings forward. On a held account they commit to stored state in the same transaction as the
verdict, with an audit entry for each where the account keeps one, and a filing into the local junk folder is recorded
exactly as a mirrored one is. A restoring account commits the same way and opens the records the source is still owed
where the message stands there, and the answer names them.

## Junk is kept out of what a deployment derives from mail

The section above is about what a verdict may do to somebody's mailbox, and it is off by default. This one is about what
a verdict does here, and it is not a switch at all: wherever classification is on, junk is withheld from everything
MailFathom derives from a message — it is never cut into [passages](message-chunks.md), never embedded, its content
never reaches an embedding provider, and it is never offered to the [rule set](mail-rules.md).

Two costs are what that is for. Embedding unsolicited mail pays a provider, per message, to make it retrievable; and a
rule set is the user's automation, so mail somebody else chose to send is mail somebody else would otherwise be firing
it with. Both are spent before anybody reads a verdict, which is why the ordering matters rather than only the answer:
**classification is scheduled ahead of chunking, embedding, and rule evaluation**, and a message classification has not
decided about yet waits rather than being derived from ahead of the answer.

With classification off, nothing is gated. Chunking, embedding, and rule evaluation reach exactly the mail they reached
before any of this existed, and no folder is looked at to decide it.

**Off is per account here as everywhere else.** A walk over stored mail spans accounts, and the gate narrows it by the
accounts that classify — so an account with classification switched off has all of its mail admitted at once, its junk
folder included, while another account's mail in the same walk goes on waiting on its verdict. Its junk folder goes
with it because withholding it is an ordering behind a verdict rather than a rule of its own: nothing is
ever going to score that mail, so holding it back would hold it back forever.

**Which accounts classify, and which of their folders are junk or classified, is read from the database.** Each
account's record carries those answers in columns beside its settings document, written in the same transaction as the
document, so the gate asks one query per question rather than reading every account's settings. It asks them once per
unit of work — one synchronization run, one pass of a walk — and judges every message that unit reaches by that one
reading, so an account switched on halfway through a run is gated from the next run rather than from the next message.
[The account records](../architecture/stored-email-schema.md#what-every-account-is-asked-about-at-once) hold the columns.

### What is decided about one message

| Answer | When | What follows |
| --- | --- | --- |
| Withheld as junk | The message is in the account's junk folder, or its classification says spam | Nothing is cut, nothing is embedded, and it is not offered to the rule set |
| Awaiting classification | Classification covers the folder and has reached no verdict about this message yet | The same, until one of the answers below replaces it |
| Released after waiting | It has waited longer than `SpamClassification:ClassificationWait` | Derived from like any other message |
| Released as unclassifiable | Its content was never stored, because it exceeded the size limit, so no verdict is coming | Derived from like any other message |
| Admitted | Classification is off, or does not cover the folder, or the verdict is anything but spam | Derived from like any other message |

The junk folder is read before any verdict, so a mailbox whose own filter already took a message needs no classification
of its own for it to be withheld — the same fact the deterministic stage reads as a signal, acted on one step earlier.

### Waiting is bounded, and running out of patience is not a failure

A verdict that never arrives is the case this bound exists for: a classification nobody noticed had stalled, a queue deep enough
that a message waits hours for a worker, a job that spent every attempt and was dead-lettered, a run nobody asked for.
Without a bound each of those would hold mail out of the index indefinitely, and an instance withholding everything
publishes the same silence as one with nothing to do.

`SpamClassification:ClassificationWait` is how long a message may wait, measured from when it was stored, and it is
fifteen minutes unless a deployment says otherwise. Past it the message is released and derived from, verdict or no
verdict — the index staying current is worth more than the small chance that the message nobody classified was spam. A
message still inside the bound and a message the bound released are separate counts, because *waiting* and *given up on*
are different things for an operator to act on.

A classification that failed, timed out, or spent every attempt leaves the message exactly as it was: no verdict, and
therefore eligible on the same terms as one nothing has looked at yet. Nothing records a failure as a decision.

### Eligibility is derived, and there is no flag to clear

Nothing marks a message as withheld. What is read is where the message is now and what its classification currently
says, so the answer changes the moment either does and no record has to be found and cleared.

That is what makes correcting a false positive work without a MailFathom-specific undo. Mail the user drags out of junk
in any client arrives in the destination as a new occurrence — a new UID in a different folder — with no classification
of its own, so it is admitted like any newly stored message and cut, embedded, and offered to the rules from then on. A
move MailFathom itself made keeps the row and its verdict, which is right, because such a move files a message *into*
junk rather than out of it.

### What a junk verdict removes

A message decided spam after it was already cut is stripped of its passages, in the same transaction the verdict is
written in, and the vectors hanging off them go with them. Two ordinary sequences produce derived data before a verdict
exists — mail stored while classification was off and classified by a run afterwards, and mail the wait released and a
later run scored — and neither may leave the index holding spam.

The count of what was removed is reported. Nothing else is: which message, which passage, and what any of it said are
mail content.

### Where in-scope mail is cut and embedded instead

The cut is the last local step of the account's synchronization run, after this pass and after the rule pass, so a
message whose verdict this gate is still waiting for reaches that step and is passed over. Nothing else has to remember
it: the same run repeats on the account's next interval, and the moment the verdict admits the message or its wait runs
out, the rule pass evaluates it and the cut that follows in that same run cuts it. The
[embedding backfill](embedding-backfill.md) sweep is the second reader of the same condition and what reaches whatever
one run's batch budget did not. Such a message ends up cut and embedded exactly as it would have been on arrival, later
by at most an interval.

This is the one behavior change a deployment that switches classification on will notice.
[The arrival pipeline](../architecture/arrival-pipeline.md) draws where this gate sits among the stages it orders.

### What an operator can see

| Signal | What it answers |
| --- | --- |
| `mailfathom.spam.derived_work.admissions` | Each answer above under its own `mailfathom.spam.admission` tag: how much is withheld, how much is waiting, and how much was released without a verdict |
| `mailfathom.spam.derived_work.discarded` | Passages removed because a junk verdict arrived after they were cut |

The tag is one of five names of MailFathom's own, and both signals are counts. No message identity, subject, address, or
passage reaches either of them, or a log line about them.

## Classifying is idempotent, and reclassifying is explicit

Classification is keyed to the stored email, so repeating it either leaves the existing record alone or replaces it with
what the same inputs produce. Two callers asking together resolve to one record rather than to a history: a concurrent
write conflicts on the record's own optimistic-concurrency token and is retried from a fresh read.

Replacing an existing verdict is an explicit operation. Nothing sets it off — not a reload, not a configuration change,
and not a message being read again. The person's own verdict is the one thing it never replaces.

Content comes from the local content store, which already holds it, so no classification path opens an IMAP session and
none can affect a remote `\Seen` flag. A message stored without content — one that exceeded the size limit — is
reported as unclassifiable rather than fetched.

## Mail is classified as it arrives

A message is classified because it arrived, without an operator asking for anything. Synchronization asks for one as
soon as it has committed the message and its content, and the work then runs as an execution of the same durable queue
that carries every other background job: leased to one worker, bounded by a timeout, retried with a jittered backoff,
and dead-lettered once it has spent its attempts. A job that stopped is read and acted on through
[administering a deployment](../operations/admin-endpoint.md#reading-the-background-work-that-stopped-and-deciding-what-becomes-of-it),
under the job type `classify-stored-email-spam`. The job names the stored email rather than where the server holds it,
so a message the source has since expunged is still classified; only the action a verdict asks for needs the occurrence,
and is not recorded without one. A `classify-email-spam` job names the message by its occurrence instead; nothing
enqueues one any more, and it runs only work an earlier build queued, by finding the stored email that occurrence
belongs to and classifying it exactly as above, or ending as done when no stored email holds it.

**Why a job rather than another step of the synchronization run.** Rule evaluation is a step of that run and needs
nothing else, because evaluating a rule over committed local state has no transient failure and therefore nothing to
retry. A classification does: it reads the message out of the content store, which can be unreachable or slow, so one
message can fail for a reason the next attempt would not meet. The account run's only recovery is to defer the whole account, which cannot
express one message out of three hundred deserving another attempt — and per-message backoff is the whole of what the
queue is here for. Nothing about it is written onto the classification record: the attempt count, the next attempt, and
the terminal failure all belong to the queue's own row.

**What it costs a synchronization run is one insert per stored message.** The run does not wait for the verdict and does
not read what the queue answered. A queue already holding as much of this type as `Jobs:MaxQueueDepthPerType` allows
refuses the row rather than growing, and that refusal changes nothing about the message: it is stored, and the wait a
verdict is allowed releases it to derived work without one. A classification backlog therefore degrades a secondary
signal instead of stalling the pass that produced it, and it is watched where every other backlog is: the queue's depth
per job type saturates at exactly the bound enqueuing is refused at, which
[telemetry](../operations/telemetry.md#durable-background-work) states in full.

**A copy of this deployment's own outgoing mail is not asked for either**, and it is the one message whose scoring is
decided by where it came from rather than by what it holds. A message MailFathom
[filed](mail-delivery.md#the-copy-in-the-accounts-own-folders) into the account's own sent folder was composed
here and sent from here, so a verdict about whether somebody sent it unsolicited says nothing — and one calling it spam
would withhold everything derived from a message the user wrote and, where the operator switched filing on, move their
own send into their junk folder.

**Two messages are deliberately not asked for.** One whose folder the configured scope does not cover, and one stored
without its content — a message above the fetch size limit, or one waiting for storage headroom — because a message
whose payload is not stored is reported unclassifiable rather than fetched. The second becomes classifiable the moment a
later run stores its content, without the message being fetched a second time for classification's sake — unless it is a
copy this deployment filed, which the run completing it recognizes from the join on the row and leaves unasked for, the
same answer its first discovery gave. With
classification switched off, nothing is asked for at all: the switch and the scope are read before the queue is touched,
which is the same shape every other path here has when it is off.

Asking twice for one message is asking once. The queue compares the job type together with the message's own stored
identity — the identifier `mfctl spam classifications --email` takes — so a folder walked again, or a run resumed after
a crash, finds the classification already asked for rather than adding a second one. Where the message is stays readable
beside it, in the job's own payload, which names the account, the folder, and the remote numbers and nothing else.

The changes an operator's switches ask of the mailbox are applied to a verdict reached this way exactly as they are to
one a run reached; [what an operator can let a verdict do](#what-an-operator-can-let-a-verdict-do) is the whole of that
decision, and every switch is still off by default.
[The arrival pipeline](../architecture/arrival-pipeline.md) draws where this sits among the stages it orders.

## Classifying the mail you already have

The trigger above reaches mail as it arrives, so a **classification run** is what reaches the mail a deployment already
holds: everything stored before this existed, everything stored while the feature was off, and everything a stalled
job or a full queue left without a verdict. It is also what switching classification on or switching filing on is
applied to an existing mailbox with. `mfctl spam run --account <id>` asks for one, and
[administering a deployment](../operations/admin-endpoint.md#classifying-the-mail-you-already-have-and-reading-what-was-concluded)
is the command reference.

**It is a dry run unless `--apply` is given.** With filing switched on, a run over an inbox is the largest single thing
this feature does to somebody's mail, so the default works out every change and writes none of them down. The posture
is read last, after every refusal above it, so what a dry run reports is the decision the acting run would take rather
than a guess at it: a message already filed reports that, a destination that resolves to nothing reports that, and only
a message the switches genuinely reach is counted as one the run would act on. The verdicts are recorded under either
posture, because a classification is derived data rather than a change to a mailbox.

The run's terms — the folders, the posture, and whether it rescores — are fixed when it is asked for. A walk spans as
many account runs as its batch budget needs, so reading configuration again on each pass would let a folder added
halfway through be walked from wherever the run had got to, and a reader of the record could not say which mail the run
had covered. Editing configuration therefore changes the next run rather than this one.

**A message already decided under the run's profile is not classified again, and is still acted on.** Skipping the
classification is what makes a run over a mailbox that has already been classified cost one lookup per message instead
of a read of every message's content; skipping the action would make *switch filing on and apply it to what is there* a
run that files nothing, which is one of the two reasons the run exists. A record naming no profile, or a profile other
than the current one, was decided under terms the run cannot compare and is reached again. `--rescore` classifies
everything in scope afresh, which is the one form of the run that reads every message's content however recently the
message was decided.

The scope defaults to the folders classification is configured over, and is bounded by them: `--folder` narrows within
that scope and a folder outside it is refused, naming the configured scope as the thing to edit. A run over a folder
nobody classifies would read the whole of it and record nothing, because the classifier declines an occurrence outside
the scope message by message.

One run per account is outstanding at a time. Asking twice is asking once — the second request is answered with the run
already under way, on the terms it was asked for rather than the ones the second request carried, because a walk that
has scored half a mailbox as a dry run cannot become one that acts halfway through.

The run is carried by the account's synchronization run, one step before [mail
rules](mail-rules.md#running-the-rules-over-mail-you-already-have) are evaluated, and takes a bounded number of batches
per pass. That run already has per-account isolation, backoff, and a slot count that stops one account starving
another; classification needs every one of those and none of them differently. Each batch commits the position it
reached, so a restart resumes at the message nobody scored rather than at the beginning of the mailbox, and a request
that arrived seconds before a shutdown is still a request afterwards. A pass that fails does not defer the account's
next fetch: classification reaches no mail server for the mail it reads, so a failure here says nothing about the
mailbox.

**A second replica neither walks the run nor takes a lease of its own for it.** The request writes the run down on
whichever replica answered it and classifies nothing. The pass is a step of the account's synchronization run, and that
run happens only on the replica holding the account's
[lease](imap-synchronization.md#a-second-replica-supervises-nothing-for-an-account-the-first-one-holds), so one
outstanding run per account and one holder per account leave one walk, advanced under the account's lease rather than
a second one beside it. A hold that is lost cancels the pass with the rest of that run, and whichever replica takes the
account next resumes past the position the last committed batch reached.

A run ends in one of three ways. **Completed** is the walk reaching the end of its scope. **Superseded** is the profile
having moved while the run was outstanding — a run cannot finish under terms it did not start with, and half a mailbox
decided each way is worse than a run an operator asks for again. **Disabled** is classification having been switched
off under it. All three are reported, and the run is readable after it has ended: *it completed an hour ago* and *you
never asked* are different answers.

What a run reports is counts and nothing derived from a message: how many it scored, how many were already decided,
how many it could reach no verdict about, how many are junk, how many nothing decided either way, and how many the
switches reach. That last one is the whole of what an operator is deciding on when they read a dry run.

## What it costs when it is off

Nothing. Every switch is off by default, and the checks run in the order of what they cost: whether classification is
on at all is free, the scope and any existing record are one lookup each, and only then is a message's content read. An
occurrence outside the configured scope therefore costs no read of its mail.

The action switches are the same shape. With all three off, deciding what a verdict causes is one property read and nothing
else: no mailbox is looked at, **no write session is ever obtained**, and no account holds the write connection that
would carry a change.

So is the gate over derived work. Whether classification is on at all is the first thing it asks, and the answer being
no ends it: no folder is resolved, no query is narrowed, and every walk that would otherwise be narrowed is handed back
unchanged rather than wrapped in a predicate that admits everything.

The [floor](#junk-mail-is-never-below-spam) is the one part that runs with classification off, because it is not
classification: one bounded query per account run, which finds nothing once the mailbox is at the floor.

## Configuration

The `SpamClassification` section, in full, is in the
[AI configuration](../operations/configuration-ai.md#spamclassification), and the block each mailbox carries is in
[classifying this account's mail](../operations/configuration-mail.md#classifying-this-accounts-mail--spamclassification).
The section bounds what the process spends; the account's block is what decides:

- whether classification runs at all;
- which folder aliases are classified, defaulting to whichever alias that account maps to its inbox;
- whether the model stage is consulted, whether it names the kind of deterministic spam, and the threshold its
  probability is judged against;
- what a spam verdict does to the mailbox.

What the account's block does not decide is how wide one pass of a classification run is: how many messages a batch
commits, and how many batches one account run takes before it leaves the rest to the next. Both are the deployment's for every account,
because they bound what one pass of the process spends rather than what happens to anybody's mail, and neither is a
schedule — how often a pass happens is the account's own synchronization interval. So is how long a message may wait on
a verdict before derived work runs for it unclassified. So are the model stage's two bounds, how many calls run at
once and how much of a body one carries, which bound what the process spends on every account that consults it.

`Actions` holds the three switches and the folder junk is filed into; it is read per verdict, so switching filing on
reaches the next one without a restart. An account's own record carries `Actions` and the posture above it, and none of
the engine settings — a record reaching for one is refused by the strict binding that reads it.

An operator who asked for junk to be acted on with classification off is told at startup rather than given the quiet
answer, and so is one whose account does not map the folder its filing would go to. An unusable folder alias and a
bound outside its range each fail startup naming themselves.

## Reading what was concluded

`mfctl spam classifications --account <id>` reads the records back, newest first, in bounded keyset-paginated pages.
Narrowing to a message with `--email` answers *why is this message in junk*; narrowing to a verdict with `--verdict`
answers *what would this run file, before I let it*.

It is a reading of the classification records rather than a second copy of them. There is no per-run history table:
a classification is what is believed about a message now, so the run that reached it is recoverable from the instant
and the profile the record already names, and a row per run per message would duplicate the verdict in order to record
it twice. A change a verdict asked for is named and pointed at rather than described — what became of it is the
[mutation trail](imap-synchronization.md#an-account-can-keep-a-record-of-what-was-done-to-it-and-none-does-by-default)'s
own answer, with its own retention.

Each record also shows its spam subcategory, the stage that named it, and the phishing detail beside it, or that none was
named.

**The signals appear by name and never by value.** A name is an authentication method, a header field, a folder alias,
or the question a model answered; the observation beside it is text a mail server wrote and can carry a sending domain, which is
exactly the second copy of the mailbox a record read back over an administrative endpoint must not become.

### What the person's own mail client reads

The [client endpoint](../operations/client-endpoint.md#the-mail-list-route) publishes the verdict on every message it
serves — a list row in any folder, a message inside a conversation, a search result, and an opened message — as one
`spam` object read in the query each of them already makes. It carries the verdict, the stage that reached it, the
subcategory and the stage that named it, and the phishing detail, and nothing else: the score, the profile, and the
signals stay on the administrative reading above, because they are what a verdict was reached from rather than what a
person reading their mail acts on. A message no stage has reached carries `null`, which is not the same as `NotSpam`.

Every value is a closed set's member name, so no text a model wrote crosses to the client and the object needs no
egress guard. It reads the record that exists and derives nothing: a message whose record names no subcategory is
published with none rather than with one inferred. The MCP surface publishes none of it.

## Privacy

A classification is derived from mail content and inherits its classification, retention, and deletion constraints: the
record hangs off the stored email, whether or not a mail server still holds it, and is removed with it, so whatever erasure and retention already reach the
message reach the record too. A change a verdict asked for is the same kind of thing and follows the same rule: the
mutation record says where a person's mail was moved and holds a folder path, a UID, and the deciding stage — never
anything from the message — and it is removed with the email it describes. So is the record of what a verdict did to a
message, which holds which act it was and when, and nothing else.

Nothing about a message's content reaches a log line or a telemetry attribute from any path here — not a header value,
not an authentication detail, not a subject. What is safe to report is the occurrence identifier, the folder alias, the
outcome, and the verdict. The spam subcategory and the phishing detail are closed values rather than text and are still
a judgement about one person's message, so no log line or telemetry attribute carries them either. Neither does
anything the model stage sends or receives: its log lines and its counter name the job, how it ended, and the model's
alias, and never the message, the probability, or the kind.

What the model stage sends is the minimised reading [listed above](#what-the-model-is-shown), to the two third parties
named there, and only for an account whose record switched it on.
