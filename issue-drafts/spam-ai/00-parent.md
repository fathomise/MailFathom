## Context

Spam classification runs two stages today ([spam classification](https://github.com/fathomise/MailFathom/blob/main/docs/features/spam-classification.md)). The deterministic stage reads what the receiving server already concluded: `Authentication-Results`, the provider's `X-Spam-*` headers, and whether the message sits in the junk folder. The scanner stage sends the whole message to an Apache SpamAssassin daemon deployed beside the service.

The scanner stage goes. Scoring mail with a rule corpus is the mail server's job, and MailFathom is not a mail server: a scanner sidecar is a second mail-filtering deployment that every operator either maintains or switches off. The deterministic stage stays exactly as it is, because reading what the receiving server established is MailFathom's job.

A model stage takes the scanner's place, with a different purpose. A verdict says only *spam or not*. A model stage also answers *what kind of spam*, and the kind is what a reader acts on: a phishing message needs a warning, while marketing spam just needs filing. **A subcategory exists only on a message whose verdict is spam.** Mail that is not spam carries none.

| Spam subcategory | What it covers |
|---|---|
| Phishing | Harvesting credentials, payment details, or one-time codes, usually by impersonating a bank, courier, or service |
| Business email compromise | Impersonating a colleague, an executive, or a supplier, typically to redirect a payment, often with no link at all |
| Malware lure | A pretext for opening an attachment or a download. The model judges the pretext only; the file itself is not scanned |
| Scam | Advance-fee, lottery, inheritance, investment, and callback scams (a phone number instead of a link) |
| Extortion | Blackmail and sextortion demanding payment |
| Marketing | Unsolicited commercial mail, unwanted but harmless |

**These are not mail categories.** Sorting all mail by topic (finance, work, and the like) is a separate feature planned for later. It applies to every message whatever its verdict, and it is a different question from what kind of spam a message is. So nothing here uses the bare word *category*, and no type, column, field, or setting introduced here is named in a way that would claim it. The word stays free for that feature, and the two can never be read as one.

## User stories

**As the operator of a deployment** I want spam classification to need no scanner sidecar, **so that** I do not run and update a second mail filter next to my mail server.

**As a person reading my mail** I want a spam message to say what kind of spam it is, and to warn me plainly when it is phishing, **so that** I can tell a dangerous message from a merely unwanted one.

**As an agent reading a mailbox** I want a message's spam subcategory beside its content, **so that** I do not treat a phishing message as ordinary correspondence.

## Acceptance

- Every child below is closed.
- No scanner port, scanner adapter, scanner configuration key, scanner startup gate, or scanner deployment asset remains anywhere in the repository.
- The deterministic stage behaves as it did before, and its spam verdict still stands over anything a later stage says.
- A classification with a `Spam` verdict can carry one subcategory from the table above, a classification with any other verdict carries none, and the model stage is the only thing that writes one.
- The subcategory reaches the client API, the MCP read results, and the opened message on screen.
- The model is asked through MailFathom's own decision-client abstraction, shaped for Microsoft.Extensions.AI and the Agent Framework, with OpenRouter as its only adapter for now, because no SDK in the stack supports a Decisions API. Decision models are configured in their own `Decisions` section, with a default model and a per-function reference, never borrowing `Chat:MainModel`.
- Nothing introduced here is named *category* on its own, so the future mail-category feature does not collide with it.
- Every break to the configuration, deployment, database, and API contracts is recorded against the surface it breaks, with what the operator has to do.
- The model stage carries an evaluation scenario measuring each subcategory, and the false-positive rate on legitimate mail is reported as its own figure.

## Delivery order

A partial order rather than a schedule. **Wave** is the earliest point a child can start: everything in one wave may be taken at the same time. It is not a deadline, and **Waits on** is the real constraint, the wave being derived from it. **Runs beside** names the children this one has no order against, which may therefore be taken at the same moment even from another wave. A number from outside this parent is a dependency on other work rather than on a child here.

| Wave | Child | Waits on | Runs beside |
|---|---|---|---|
| 1 | #{{REMOVE}} | — | #{{DECISION}}, #{{ABSTRACTION}} |
| 1 | #{{DECISION}} | — | #{{REMOVE}}, #{{RECORD}}, #{{SURFACES}}, #{{CLIENT}} |
| 2 | #{{RECORD}} | #{{REMOVE}} | #{{DECISION}}, #{{ABSTRACTION}} |
| 2 | #{{ABSTRACTION}} | #{{DECISION}} | #{{REMOVE}}, #{{RECORD}}, #{{SURFACES}}, #{{CLIENT}} |
| 3 | #{{MODEL}} | #{{RECORD}}, #{{ABSTRACTION}}, #{{DECISION}} | #{{SURFACES}}, #{{CLIENT}} |
| 3 | #{{SURFACES}} | #{{RECORD}} | #{{DECISION}}, #{{ABSTRACTION}}, #{{MODEL}} |
| 4 | #{{CLIENT}} | #{{SURFACES}} | #{{DECISION}}, #{{ABSTRACTION}}, #{{MODEL}} |

Two lines run side by side and meet at #{{MODEL}}:

- **The record line**, #{{REMOVE}} → #{{RECORD}}. Both reshape `SpamClassificationStage`, `SpamClassificationProfile`, and `EmailSpamClassifier.Decide`, so the record changes once in a known order rather than in two pull requests that conflict.
- **The provider line**, #{{DECISION}} → #{{ABSTRACTION}}. The owner verifies OpenRouter's Decisions API, and then the decision-client abstraction and its OpenRouter adapter are built against what was verified. This line waits on that verification, which is why both of its issues are queued `Needs decision` or behind one.

After #{{RECORD}} lands, the display path #{{SURFACES}} → #{{CLIENT}} has no order against the provider line or the model stage. It shows whatever subcategory a record carries, and until #{{MODEL}} lands no record carries one. The feature is done when #{{MODEL}} and #{{CLIENT}} are both closed.

## Out of scope

- Mail categories (finance, work, and the like) for all mail. They are a separate future feature, described above.
- Moving or deleting mail by subcategory. The existing actions keep acting on the verdict alone.
- Scanning attachments for malware. *Malware lure* judges the pretext, not the file.
- Moving the decision client onto a standard OpenAI Decisions contract once one exists: #{{STANDARD}}, outside this parent, parked and blocked until OpenAI publishes the standard and OpenRouter implements it.
- Learning from a person's "not spam" correction. That is a feedback loop with its own privacy design.

## Dependencies

Follows #1996, which made the classification posture the mail account's. Nothing outside this parent blocks it.

## Baseline

1d26b66df368199d215814baacb461e90f902748
