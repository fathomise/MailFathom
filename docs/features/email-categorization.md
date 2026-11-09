# Email categorization

<!-- describes: backend/src/Application/Categories/**, backend/src/AI/EmailCategorization/**, backend/src/Infrastructure/Persistence/Categories/**, backend/src/Infrastructure/Observability/EmailCategorizationTelemetry.cs, backend/src/Domain/Categories/** -->

A mailbox is easier to read when a message arrives already saying what sort of mail it is. Categorization asks a
decision model about each arriving message, once, and records two things beside it: which one of seven default
categories it is, and which of the subjects the mailbox named for itself it is about. It is an opinion recorded as
derived data, and nothing about a message waits for it.

It is the account's own decision, off until the account's record switches it on, and it needs a decision model declared
under [`Decisions`](../operations/configuration-ai.md#decisions). [Sorting this account's mail into
categories](../operations/configuration-mail.md#sorting-this-accounts-mail-into-categories--categorization) holds the
keys.

## What a message is asked

One request per message, carrying every question at once:

- **One choice between the seven defaults**: `correspondence`, `subscriptions`, `finance`, `shopping`, `travel`,
  `notifications`, and `other`. The whole set is always offered, including a default the account switched off, because
  the record is what is hidden rather than the question: switching a default back on asks nothing again.
- **One yes or no per category the account named**, reading that category's description as the criterion. A probability
  of 0.5 or more is a match. At most twenty can be named on one account.

Each question carries a revision of its own, recorded with the answer, beside the build of the model that gave it. A
reworded question or a replaced model can therefore be told apart from the answers that came before it.

The model writes no text. It answers closed questions, so nothing it says can reach a reader as a sentence, and an answer
outside the options offered is unreadable rather than recorded.

## What the model is shown

The same minimised reading of a message [the spam model stage](spam-classification.md#the-model-stage) is shown: the
sender's address and display name, the `Reply-To` where it differs, the subject, the plain-text body cut to
`SpamClassification:ModelStage:MaxBodyCharacters`, each link's hosts, and each attachment's declared type and name. A
reply also carries the first 1 000 characters of the message that opened its conversation, because "thanks, that works
for us" says nothing about what it is about. The opening is left out where its own message may not be shown to a model.

All of that crosses the `email_categorization_input` [egress
point](sensitive-content-scanning.md#the-guarded-egress-points), so the account's scanning posture applies to everything
but the two addresses. **The sender's address and the `Reply-To` address are sent unredacted under every posture**, as
[ADR
0041](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md)
records for the spam stage. A scanner that posture switched on and that cannot be reached means nothing is sent. Every question tells the model the
message is data to judge and never an instruction to it.

**What the account wrote for itself crosses unguarded.** A category's description is the account's own authored text
rather than anybody's mail, so it is sent as written. The category's name is not sent at all: a question is identified by
a digest of the name, and the name stays in the deployment.

The model routes as [ADR
0041](https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md)
requires, to providers that keep nothing.

## Which mail is asked about

A stage of the account's run, after the spam verdict and before the [rule pass](mail-rules.md). It takes exactly the mail
that pass is about to evaluate, under the same batch bounds, so an arriving message reaches the rules having been asked
about. Two things follow:

- **Switching it on re-reads nothing.** Mail the rules have already evaluated is not arriving mail.
- **A message is asked about at most while it is arriving.** One that got no answer simply has no category once the
  rules have evaluated it.

Never asked about: anything with a spam verdict, anything in the junk folder, anything in a folder that is not visible to
AI, a message spam classification has not yet admitted, a tombstoned message, a delivery or read report, a message whose
content is not stored, and the copy MailFathom filed of the person's own outgoing message.

## When it fails

**It fails open.** No later stage and no settlement waits for a category, so a provider that is down leaves mail arriving
exactly as it would have. What is recorded for a failure is its reason alone, as a count, with nothing from the message.

| Outcome | What it did |
| --- | --- |
| `answered` | The categories are recorded |
| `not_activated` | No decision model serves categorization. Nothing was asked and the pass ended |
| `budget_exhausted` | The period allowance or the call's ceiling refused it. The pass ended |
| `provider_unavailable` | The provider could not be reached or refused the request. The pass ended |
| `scanner_unavailable` | The account's scanner could not read the input, so nothing was sent. The pass ended |
| `answer_unreadable` | The answer was outside what was offered. This message has no category, and the pass went on |
| `input_too_large` | The request exceeds what the model is declared to accept. This message has no category, and the pass went on |
| `content_unavailable` | The stored message could not be read. This message has no category, and the pass went on |

The first four failures say something about every message behind this one, so the pass stops rather than meeting them
once per message while it holds the account's run. A call is written to the same two spend ledgers a spam model call is.

## Several replicas, and custody

The account's run holds its lease while this stage runs, so one replica asks. A replica that loses the lease is cancelled
between messages, and what it committed stays. One whose call was already in flight commits nothing over the answer of
the replica that took the account: the store refuses a second set of categories for one message, and the losing replica
carries on.

**It behaves identically in every custody phase.** It reads stored content, writes local rows, reaches no mail server,
and needs no custodian, so `Mirrored`, `Held`, and `Restoring` accounts are all asked about in the same way, and the
stage does not read the phase.

Categories are derived from a message and are removed with it. They inherit its classification.
