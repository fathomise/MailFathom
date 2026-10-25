## Context

Part of #{{PARENT}}. A classification records a verdict, the stage that reached it, signals, an optional assessment, and a profile. It cannot say what kind of spam a message is. This issue gives the record that capacity and a model stage to attribute it to. Filling it is #{{MODEL}}'s job; nothing here calls a model.

**This is a subcategory of the `Spam` verdict, not a mail category.** Mail categories (finance, work, and the like, for every message whatever its verdict) are a separate feature planned for later. Nothing here may take that name: no type, column, API field, or configuration key is called *category* on its own. Every one carries *spam* in its name, for example `SpamSubcategory` and `spam_subcategory`.

Proposed shape, for the implementer to refine:

- **`SpamSubcategory`**: the six values in #{{PARENT}}'s table, `Phishing`, `BusinessEmailCompromise`, `MalwareLure`, `Scam`, `Extortion`, and `Marketing`. It is a published identity (database, admin API, client API, MCP), so it is modelled with `$closed-enumeration` rather than as a bare C# enum if that skill's test says so.
- **Invariants on `SpamClassification`**:
  - a subcategory only on a `Spam` verdict, and never on `NotSpam` or `Undetermined`;
  - a subcategory only from a stage that can name one. The deterministic stage names none;
  - a `Spam` verdict without a subcategory stays valid, since it is what the deterministic stage reaches on its own.
- **Phishing detail**, carried only by `Phishing` and `BusinessEmailCompromise`:
  - what the message asks for, as a closed value (credentials, payment details, one-time code, other);
  - whom it impersonates, as bounded text. The impersonated name is model output about a message, so it is untrusted, classified like the message, and never logged.
- **`SpamClassificationStage.Model`**, and a signal kind for the model's judgement whose provenance names the model and the instruction revision, in the same way a corpus revision used to name the scanner's rules.
- **Two kinds of model contribution**, which the record must tell apart:
  - the model decided the verdict, on mail the deterministic stage left `NotSpam` or `Undetermined`;
  - the model only named the subcategory of a verdict the deterministic stage reached (see #{{MODEL}}).

  The verdict's deciding stage stays `Deterministic` in the second case.
- **Profile digest**: gains whether the model stage is consulted, whether it names subcategories for deterministic spam, and the threshold its probability is judged by. A change to any of them re-decides mail on the next run.
- **Assessment**: the model's probability as `Score` and the configured threshold as `Threshold`. This reuses the existing type, and keeps "a number is never recorded without its scale" true.

## User stories

**As a person reading my mail** I want the system to remember what kind of spam a message is, **so that** every screen that shows it says the same thing.

**As an operator diagnosing a wrong verdict** I want the record to say which stage reached the verdict and which named the subcategory, under which model and instruction revision, **so that** I can tell a model's call from what the mail server decided.

## Acceptance

- `SpamSubcategory` and the phishing detail exist in `Domain`, with unit tests proving every invariant above, including that a subcategory on a verdict other than `Spam` is refused.
- The model stage, its signal kind, and the distinction between deciding a verdict and only naming a subcategory exist, and the profile digest covers the model terms, with pinned digests in tests.
- An additive migration adds the columns, and reading back a record written before it yields no subcategory.
- The impersonated-party text has a stated maximum length and is refused or shortened at the domain boundary, consistent with how `SpamSignal` treats an observation.
- The admin API's classification history and `mfctl spam` show the subcategory and the phishing detail, with their contract and golden tests updated.
- No identifier, column, field, or key introduced here is named *category* without *spam*.
- `spam-classification.md` and the stored-schema documentation describe the new fields, and say that the spam subcategory is not a mail category.
- No log line or telemetry event carries the impersonated-party text, and a test proves it for the classifier's log events.

## Out of scope

- Calling a model: #{{MODEL}}.
- The client API and MCP results: #{{SURFACES}}.
- Mail categories for all mail.

## Dependencies

Waits on #{{REMOVE}}, which reshapes the same stage enumeration, profile digest, and `Decide`. #{{MODEL}} and #{{SURFACES}} wait on this.

## Baseline

1d26b66df368199d215814baacb461e90f902748
