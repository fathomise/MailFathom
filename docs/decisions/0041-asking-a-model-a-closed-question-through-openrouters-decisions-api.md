---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-10-25
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Ask a model a closed question through OpenRouter's Decisions API behind MailFathom's own decision client, take it as a bounded exception to ADR 0011, and send the spam model stage only what it reads, routed to providers that keep nothing

<!-- describes: backend/src/AI/ProviderAdapters/** -->

## Context and Problem Statement

The spam model stage in issue 2672 asks a model two things about a message: whether it is spam, and when it is, which of six kinds. Issue 2667 holds the six kinds. The intended mechanism is a **Decisions API**: an endpoint that answers a closed set of options with a probability for each, rather than with text. The answer cannot be talked into anything outside the options. The probability is a score a threshold can judge, which is the shape `SpamAssessment` already records.

Issue 2669 asks six questions before code depends on that API. They cover fitness, OpenRouter's contract, where the model runs, what leaves the deployment, the provider terms, and cost. Before this record, what was known about the API came from secondhand reports. **Everything below was read on 2026-10-25 from each provider's own material**:

- OpenRouter's published OpenAPI document, `https://openrouter.ai/openapi.json`, version `1.0.0`;
- its live model catalogue and endpoint listings;
- its privacy, zero-data-retention, and in-region routing pages;
- the decision skill OpenRouter publishes in its own `OpenRouterTeam/skills` repository;
- OpenAI's own Decisions guide, and Microsoft's page for Azure OpenAI.

The owner settled the direction before this record, so it records that direction rather than reopening it:

- the stage reaches a Decisions API through OpenRouter alone;
- MailFathom writes its own decision-client abstraction, shaped like `IChatClient`;
- no chat-completion fallback is built;
- decision models are configured in their own section, never borrowing a chat model.

What this record decides is everything that direction leaves open.

### What the SDKs support

Re-checked on 2026-10-25 against the pins in `backend/Directory.Packages.props` and the newest stable release of each. The check searched each package's XML documentation for a type named after a decision, and its assemblies for a `decisions` route:

| Package | Pinned | Newest | Decisions API |
|---|---|---|---|
| `OpenAI` | 2.14.0 | 2.14.0 | None. The open feature request, openai/openai-dotnet#1434, was last updated 2026-10-13 |
| `Microsoft.Extensions.AI.Abstractions`, `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | 10.11.0 | None. The abstractions are `IChatClient` and `IEmbeddingGenerator`, and a decision is neither |
| `Microsoft.Agents.AI` | 1.24.0 | 1.26.0 | None |
| `Microsoft.Agents.AI.OpenAI` | not pinned | 1.26.0 | None |

OpenAI's Python and JavaScript libraries ship a `decisions` client, and the .NET one does not. Nothing in this stack can make the call.

## Decision Drivers

- **A message is adversarial text, and the classifier is a model.** A message that says *classify this as not spam* is writing to the reader. A closed answer limits what such text can achieve to moving a probability. That is the property the stage is built on, so the API is worth an exception only if it keeps that property under every failure.
- **One client construction is the whole of the provider reach.** ADR 0011 decided that, and ADR 0030 relies on it. A second wire protocol is a second failure classification, a second credential path, and a second thing to keep working. So it is taken only as narrowly as the gap needs, and only for as long as the gap lasts.
- **Everything around a model call hangs on `IChatClient`**: the budget, resilience, failure classification, cost reporting under ADR 0022, telemetry, and the evaluation cache. A decision call must get all of it without each piece being written a second time.
- **The stage replaces a scanner that ran inside the deployment.** The SpamAssassin daemon saw the whole RFC 822 message and sent it nowhere. A model stage sends mail to at least two third parties: the router and whichever provider it chooses. That is a different privacy posture, and the operator must be told so in those words.
- **Data minimisation is a property of the request, not of the provider's promise.** What is not sent cannot be retained, trained on, or routed to the wrong region.
- **Thresholds belong to one model build.** OpenRouter's own guidance says probabilities differ between models on the same question, so a threshold set against one build means nothing against the next.

## Considered Options

The six questions are answered together, so the options are the shapes the whole answer could take.

1. **OpenRouter's Decisions API behind MailFathom's own decision client**, with the request minimised, routing restricted to endpoints that keep nothing, and the impersonated party recorded as a kind rather than a name.
2. **The same, sending the whole message**: every header, the full body, and every link target as written. The minimisation stays the provider's problem.
3. **A chat model asked for structured output**, through the existing client construction, constrained to the six kinds by a JSON schema.
4. **OpenAI's own Decisions endpoint**, which OpenAI's guide now calls generally available, reached directly.

## Decision Outcome

Chosen option: **1**. It is the only option that keeps the closed answer the stage exists for while bounding what leaves the deployment. Option 2 keeps the closed answer but sends more than the questions read, which costs privacy and, by OpenRouter's own account, accuracy. Option 3 gives up the property the stage is built on. Option 4 is outside the owner's direction, and the section *The exception to ADR 0011, and its exit* below explains why it does not yet close the exception either.

### 1. Fitness: the questions fit, and the impersonated name does not

OpenRouter's contract offers four question types. `noul` is yes or no, and answers with the probability of yes. `choice` picks one option and gives a probability for each. `score` places an answer on ordered levels. `box` locates a region in an image. The stage's needs map onto the first two:

| What the stage asks | Question | Why that type |
|---|---|---|
| Is it spam, and which kind | **One `choice`** over seven options: `not_spam` and the six kinds | The seven are mutually exclusive, and OpenRouter's guidance is that one label from a written rubric is one `choice`. Two questions, a `noul` for spam and a `choice` for the kind, are evaluated independently and can contradict each other. The guidance says so too: P(yes) from one question and P(not yes) from another need not sum to 1 |
| Which kind, when the deterministic stage already decided spam | **One `choice`** over the six kinds alone | The verdict is not the model's to give, so `not_spam` is not offered. No answer can then contradict the deterministic verdict |
| For phishing, what is being asked for | **One `choice`** over `credentials`, `payment_details`, `one_time_code`, `other`, and `nothing_stated` | It is the closed value issue 2670 records. It is asked in the same request and read only when the kind is phishing, because questions in one request are answered independently and in parallel |
| For phishing and business email compromise, who is impersonated | **One `choice`** over the *kind* of party, not its name. Issue 2672 owns the option list | See below |

The spam probability is read off the first question as **1 − P(`not_spam`)**. One distribution decides both the verdict and the kind, so they cannot disagree. Code compares that probability against the account's threshold, and the kind is the most probable of the six. This is what `SpamAssessment` records as score and threshold. The `probabilities` member is optional in OpenRouter's schema, but this function cannot be read without it. **An answer is unreadable unless its probabilities name exactly the options offered, each a finite number within [0, 1].** A missing `probabilities` member, a missing `not_spam`, an option that was never offered, and a value outside that range are all unreadable. None of them is read with a default, because a missing `not_spam` read as zero would make every such answer a spam verdict.

**A decision model writes no text, so the impersonated party's name is out of reach.** OpenRouter's limits page puts it plainly: decision models are *not trained to produce text or values*. Issue 2670 proposes the name as bounded text, and that text cannot come from this API. Two other ways to get it were rejected:

- **A chat call to write the name** is the chat fallback the owner refused. Its output would also be model-written text about a real person or organisation, stored beside their mail.
- **A choice among names code extracted**, such as the sender's display name or link hosts, needs an extractor that does not exist. It would also only reach names that code can already see.

So the record carries the impersonated party's **kind**, from a closed set. Where a person needs a name, the client already shows them the one the message presents, the sender's display name. The model never repeats it. Issue 2670's shape changes accordingly, and the change makes it smaller and safer.

### 2. OpenRouter's contract, and how the abstraction maps onto it without its vocabulary

**The contract is `POST https://openrouter.ai/api/alpha/decisions`**, authenticated with an OpenRouter key as a bearer token. The secondhand report the issue quoted was right about the path and the `state` plus `questions` shape. It missed the `box` type and the routing block.

- **The request** has three required members:
  - `model`;
  - `state`, which is a string, an object, or an array;
  - `questions`, an object keyed by the caller's own identifiers.

  A `choice` question's `criteria` maps each option's name to its description, and a `score` question's `criteria` is an ordered array. The request may also carry `provider`, `session_id`, `user`, and `trace`.
- **The response** carries `id`, `model`, `provider`, `answers`, and `usage`.
  - `model` is the exact dated build that answered.
  - `provider` is the upstream that served the request.
  - `answers` is an object keyed like the questions.
  - `usage` holds `input_tokens`, `output_tokens`, and optionally `cost`.

  Every answer carries `type`. OpenRouter's guidance is to treat a missing key or an unexpected `type` as an error, never as a default.
- **Failures are statuses**:
  - `400`, `404`, and `413` refuse a request;
  - `401` and `403` reject the credential;
  - `402` means the account has no credits;
  - `429` limits the rate;
  - `500`, `502`, `503`, `524`, and `529` are server, upstream, edge-timeout, and overload failures.

  `ProviderCallFailureClassification` already sorts every one of them by status, so the adapter raises them through the same classification and nothing new is written:
  - `401` and `403` are *credential rejected*;
  - `429` is *rate limited*;
  - every status of `500` and above is a transient *transport fault*, `524` and `529` included;
  - the rest are *request refused*.

  `402` is the case to name, because it is not retried. An exhausted OpenRouter balance stops the stage until somebody tops it up, and the stage leaves classification to the deterministic stage meanwhile.
- **Release status: alpha, with no stability commitment anywhere.** The path is `/api/alpha/`. OpenRouter's own SDKs file it under *Alpha feature endpoints*. The OpenAPI document states no deprecation policy for it. The Terms of Service, last updated 31 August 2026, have no clause about pre-release features at all. The shape can therefore change without notice. That is the reason everything OpenRouter-specific stays in one adapter, and why the adapter refuses an answer it cannot read rather than guessing at it.
- **Which models serve it.** On 2026-10-25 the catalogue returned 30 entries whose output modality is `decisions`. Some are free variants, and some are aliases whose identifier begins with `~`. Every paid entry charges between $0.01 and $0.15 per million input tokens, and none charges for output. **This record chooses no model.** The operator declares one, and issue 2672 measures candidates through the evaluation suite. Two rules follow from the catalogue:
  - **A decision model is declared by a dated build or a versioned identifier, never by an alias.** An alias moves to a new build, and the threshold calibrated against the old build moves with it, without anybody being told. A `Decisions:Models:<n>:Model` value beginning with `~` is refused at startup.
  - **The build that answered is recorded**, from the response's `model` member, in the signal provenance issue 2670 adds. The configured name is not what is recorded. When a classification is questioned later, the build is what explains it.

**The abstraction speaks neither provider's words.** Issue 2671 builds it. This record fixes the vocabulary rule, taken from comparing the two contracts that exist:

| Concept | OpenRouter's word | OpenAI's word | MailFathom's |
|---|---|---|---|
| The evidence | `state`: a string, an object, or an array | `input`: a string or messages | The input as `ChatMessage` and `AIContent`, so the turn-document envelope is reused unchanged |
| Yes or no | `noul` | `predicate` | A yes-or-no question |
| One of several | `choice`, with `criteria` as a map | `choice`, with `choices` as an array of value and description | A choice, with options as an ordered list of value and description |
| Ordered levels | `score`, with `criteria` as an array | `score`, with `levels` | A scale |
| Identifying a question | The key in an object | A `name` in an array | A question identifier the caller assigns |
| A question with no answer | A missing key | A `refusal` answer | An unanswered question. It is part of the answer type from the start, because both contracts can produce one |

`box` has no use here and is not modelled. The OpenRouter adapter serialises the input's text into `state` as one object with named fields, which is OpenRouter's stated preference. It sends no image: the stage has no question an image answers, and it is one more thing that would leave. OpenRouter's request and response types never cross the adapter, so issue 2675 can replace the adapter alone.

### 3. Where it runs: at a third party, and the operator is told so

**No deployment-local model serves this contract today.** Several entries in the catalogue are open-weight, and one family is published under Apache-2.0. Self-hosting one of them, however, yields whatever surface the operator's inference server offers, typically chat completions or raw logits. It does not yield `/api/alpha/decisions`, which is the contract of OpenRouter's router. The adapter's address is the operator's own setting, so a server speaking this contract inside the deployment would be reached with no code change. That route stays open, but no such server is known, and this project ships and tests none.

So **switching the model stage on sends each classified message's minimised input to OpenRouter, and from there to one upstream provider**. That is two third parties where the scanner involved none. The stage is off unless an account turns it on, which issue 2672 already provides. The configuration reference says in plain words what turning it on sends and to whom. A deployment whose obligations forbid third-party processing of mail leaves it off. It loses nothing it had before, because the deterministic stage is unchanged.

### 4. What leaves the deployment, and the egress point it crosses

The input crosses a new egress point, **`spam_classification_input`**, which joins the closed register in [sensitive-content scanning](../features/sensitive-content-scanning.md#the-guarded-egress-points). It is a point of its own, not a reuse of `chat_prompt`. What crosses it is chosen by a classifier about a message nobody asked a question about, and an operator reading redaction rates needs to see the two apart.

It is guarded like every other point. **The account's scanning posture applies in full, and there is no exemption for this point.** What is sent unredacted is therefore what that posture leaves unredacted, together with the two addresses named below the table. With both scanners off, which is the default, everything in the table below is sent as written:

| Sent | Not sent |
|---|---|
| The sender's address and display name, the `Reply-To` address when it differs, and the subject | Every other header, including the `To` and `Cc` lists, which are other people's personal data, and the `Received` chain, whose authentication the deterministic stage already read |
| The message's plain-text body, cut at the bound issue 2672 sets | The HTML representation, and the stored RFC 822 bytes |
| For each link: the host of its target, the host its visible text names when it names one, and whether the two differ, as `MailLinkReader` computes | The link target's path and query, which often carry a token identifying the recipient |
| For each attachment: its declared content type and file name | Its content and its extracted text |
| The facts the deterministic stage established: each sender-authentication outcome, and whether the sender is one this deployment trusts | The deterministic stage's own verdict, which the second job does not ask the model to judge |

**The sender's address and the `Reply-To` address cross unredacted under every posture.** The guard never sits above an address. The sensitive-content page draws its line between a routing identity and free text, so the display name in front of an address is guarded and the address is not. That page justifies the line by who may reach the deployment, and that reasoning does not hold for a third-party destination. The addresses are still sent, because a lookalike sender and a `Reply-To` that differs from it are among the strongest signs of phishing and business email compromise. Whether that is acceptable is the operator's to weigh when switching the stage on, and the configuration reference says so beside the switch.

Minimisation costs some detection power, and that is the trade the issue asks to be stated. A phishing page's path, a pixel's query string, or the recipient list of a business email compromise would each tell the model something. What the table keeps is what a person deciding the same question would read first. **Redaction costs more**: with the personal-data scanner on, a callback scam's phone number, a business email compromise's named executive, and a lookalike sender's display name are replaced before the model sees them. Detection of those kinds will be measurably worse. That is the operator's trade to make, through a posture they already control. It is documented beside the stage's switch, not overridden for this point. Issue 2672's evaluation reports per-kind figures, and that is where the cost becomes a number.

No subject, body, address, host, file name, or answer probability reaches a log line or a telemetry attribute. The provider's `cost` member is not reported either, for the reason section 6 gives.

### 5. Provider terms: two parties, and the routing the stage requires

- **OpenRouter itself** keeps no prompt or response unless the account opts in. Its data-collection page names the two opt-ins: private input and output logging, and OpenRouter's own use of inputs and outputs in exchange for a discount. It keeps metadata such as token counts and latency. It samples prompts for anonymous categorisation through a model with a zero-retention policy. It offers a data processing agreement with Standard Contractual Clauses. The register's existing OpenRouter row already requires both opt-ins to stay off, and that condition holds here unchanged.
- **The upstream provider** is chosen per request, and its own retention and training policy is the one that applies. **So every decision request carries `"provider": { "zdr": true, "data_collection": "deny" }`.** The adapter writes this block, and no configuration key can turn it off.
  - `zdr` restricts routing to endpoints OpenRouter lists as retaining nothing. OpenRouter treats it as an OR with the account's own settings, so the request can only tighten them.
  - `data_collection: deny` refuses providers that may train.

  A model with no qualifying endpoint fails the request rather than falling through to one that keeps data. On 2026-10-25 that excluded six of the 27 entries that are not aliases. OpenAI's own decision model was among them, because its only endpoint is not on OpenRouter's zero-retention list. The other 21 each had at least one qualifying endpoint.
- **Nothing links one request to another at the router.** `session_id`, `user`, and `trace` are never sent. A decision has no conversation to keep on one provider, and a per-user value would let the router join a person's mail across calls.
- **Region.** Routing is global by default. OpenRouter's in-region routing needs a Business or Enterprise plan and a regional host. On 2026-10-25 the European host listed **no** decision model, and the United States host listed four. The address is the operator's setting, so a deployment can point at the European host once it serves one. Until then, a deployment bound to keep mail processing in Europe cannot switch the stage on, and the configuration reference says so.

Nothing reaches `THIRD_PARTY_LICENSES.md` in this change. The register records only what the repository uses today, and no code calls this endpoint yet. Issue 2671 introduces that call, so it adds the row: OpenRouter's Decisions API as a hosted service, with the terms above and the forced routing block as its operating conditions. Nothing is added to *Trademark and brand use* either. OpenRouter and OpenAI are the only owners named here, and both are already named throughout the repository.

### 6. Cost, and how a call is reported

**Cost per classified message is the input it carries times the input price.** Output is free on every decision model in the catalogue. The input is the minimised table above, plus the questions and their criteria, which issue 2672 writes. A worked figure uses an illustrative body bound of 8,000 characters. That is about 2,000 tokens of English or 2,700 of Polish, plus roughly 600 for the facts and questions, so about 2,600 to 3,300 input tokens per message:

| Input price | Per message | Per 100,000 messages |
|---|---|---|
| $0.042 per million tokens, the median paid entry | about $0.00011 to $0.00014 | about $11 to $14 |
| $0.15 per million tokens, the dearest paid entry | about $0.0004 to $0.0005 | about $39 to $50 |

The second job, naming the kind of mail the deterministic stage already called spam, costs the same per message. Junk is often most of a mailbox, which is why issue 2672 gives that job its own switch.

**Reporting follows ADR 0022, which reports consumption and never money.**

- One message's classification is one run. It is charged to the same period ledger every derivation charges, and bounded by a run ledger of its own, exactly as enrichment is today.
- What a run reports is the calls it made and the tokens the provider counted, from `usage.input_tokens` and `usage.output_tokens`.
- OpenRouter's `usage.cost` is a dollar figure. MailFathom neither converts it nor displays it, because ADR 0022 holds no price and reports no money. It survives only in the raw representation the adapter keeps.

### How the rest of the pipeline reaches a decision call

The decision client is composed like a chat client: a `DelegatingDecisionClient` base, a builder, and middleware in the same order. Each concern is written once:

| Concern | How it reaches a decision |
|---|---|
| The budget | A budget middleware over the same two ledgers `BudgetedChatClient` writes. It is composed outside resilience, so a call the deployment's own ceiling refused never touches the provider's health or circuit |
| Retries and failure classification | A resilience middleware running under the same `IOutboundOperationRunner` provider budget, keyed by the decision model's alias. It classifies through the existing `ProviderCallFailureClassification`. A decision has no side effect, so retrying a transient failure is safe. **There is no fallback model.** A threshold is calibrated against one build, so a second model's probability judged against it would be a score nobody calibrated. A failure the retries do not cure leaves the deterministic result where it was |
| Health | Recorded per decision model alias through the same health recorder |
| Cost reporting | Section 6 |
| Telemetry | A span and metrics naming the alias, the build that answered, the provider, the token counts, the latency, and the outcome. No input, no question text, and no probability |
| The evaluation cache | A caching middleware over the same `IDistributedCache` the evaluation reporting configuration hands a scenario, through `IEvaluationResponseCacheProvider.GetCacheAsync`. It is keyed on a canonical serialisation of the input, the questions, the options, and the model. A filed answer is read back exactly as a chat answer is, and a changed question asks again. Production composes no cache |

### What an operator configures, and what happens when they configure nothing

- **To reach OpenRouter's Decisions API**, the operator declares a `Decisions:Models:<n>` block. It takes the endpoint keys of a `Chat:Models` block, with the same validation:
  - an alias;
  - the dated model identifier;
  - the address `https://openrouter.ai`, or a regional host, under which the adapter calls `/api/alpha/decisions`;
  - exactly one of `ApiKey`, holding an OpenRouter key, and `Unauthenticated`;
  - a request timeout and the request-character bound.

  It takes none of the chat block's other keys. A decision has no generation parameters, so `Api`, `MaxOutputTokens`, `Temperature`, `TopP`, and `ReasoningEffort` do not apply. `StickySessions` and `ExtraHeaders` are refused, because a session or a static header would link requests at the router. `AdditionalProperties` is refused as well, because the adapter owns every request member: `provider`, `session_id`, `user`, and `trace` included. That is what keeps section 5's routing block impossible to loosen from configuration.

  The operator also states `Decisions:DefaultModel` and, optionally, `Decisions:SpamClassification:Model`. Each is an `Alias` alone, with no `Fallback`, for the reason the resilience row above gives. An alias that names a chat model is refused at startup, and so is a model value beginning with `~`.
- **With no decision model declared**, nothing calls the API. An account record switching the model stage on is **refused at the write**, and the refusal names `Decisions:Models`. That is how an account asking for a personal-data scanner the deployment cannot reach is refused today. A record that asked for the stage before its model was removed classifies through the deterministic stage alone, and startup logs a warning naming the key. The stage never fails closed: a missing model, an unreachable provider, an exhausted balance, and an unreadable answer all leave the deterministic result exactly where it was.

### The exception to ADR 0011, and its exit

ADR 0011 makes one OpenAI-compatible client construction the whole of MailFathom's provider reach, and refuses a second wire protocol. **The OpenRouter Decisions adapter is a second wire protocol, taken knowingly.** It is bounded three ways:

- **What it is:** a request and response shape that is not the OpenAI wire protocol, reached at an address `OpenAiCompatibleClientFactory` does not build. It reuses the provider-endpoint credential and reach declarations, but none of that factory's code.
- **Its scope:** decision calls only. No chat or embedding request takes it, and no other provider is reached through it.
- **Its exit condition:** OpenAI publishes a standard Decisions contract, and OpenRouter implements it. **The exit issue is #2675.**

On 2026-10-25 the first half had arrived and the second had not. OpenAI's own guide calls `POST /v1/decisions` generally available. Microsoft documents the same shape for Azure OpenAI at `/openai/v1/decisions`. OpenRouter's endpoint is not that contract. The path differs, the evidence is `state` rather than `input`, questions are an object rather than an array, the yes-or-no type is `noul` rather than `predicate`, and OpenRouter emits no `refusal` answer. So #2675 waits on OpenRouter alone now, and its body can say so.

**ADR 0011 and ADR 0030 otherwise stand unchanged.** The refusal of a second construction for chat and embeddings, the Bedrock embeddings refusal, the Vertex credential arrangement, and ADR 0030's refusal of a second vector space all hold as written. This exception is narrower than ADR 0011's decision, and it ends. ADR 0011 is still `proposed`, so it could take a pointer to this record. This record does not add one, because that is the owner's call.

### Consequences

- Good, because the stage gets a closed answer and a probability per option. Injected text can move a number, but it cannot write a verdict outside the set.
- Good, because the request carries only what the questions read. Two third parties then hold less of a message than the scanner had in hand. Neither retains a prompt or a response, and OpenRouter keeps request metadata, as section 5 states.
- Good, because the budget, resilience, classification, health, telemetry, and evaluation cache are each written once and composed. When an official abstraction arrives, it replaces one adapter.
- Good, because the vocabulary was chosen against both contracts that exist. When issue 2675 moves to OpenAI's shape, the unanswered-question case and the question identifiers are already in the abstraction.
- Neutral, because the European in-region host serves no decision model today. A Europe-bound deployment keeps the stage off until it does, and loses nothing it had.
- Bad, because an alpha contract with no stability commitment now sits under a feature. A change at OpenRouter breaks classification of the second job and of undetermined mail until the adapter follows. The deterministic stage carries on meanwhile, and nothing else does.
- Bad, because enabling the stage is a third-party transfer of mail. No configuration makes it otherwise, and this record cannot make that decision for an operator.
- Bad, because the sender's address and the `Reply-To` address reach both third parties under every posture, the personal-data scanner included.
- Bad, because the minimised input and any redaction both cost detection power. Phishing that hides in a link path, and business email compromise whose tell is a recipient list, are the cases most exposed.
- Bad, because the impersonated party's name is not recorded. A person sees the kind of party a message pretends to be, and the name only as the message itself presents it.

## Validation

- Issue 2671's adapter tests drive the contract recorded in section 2 against a fake handler. They cover a well-formed answer, a missing key, an unexpected `type`, a missing `probabilities` member, a distribution missing an offered option or naming one never offered, a probability outside [0, 1], and each failure status in the classification above. A test asserts that every request carries the routing block of section 5, and that it carries no `session_id`, `user`, or `trace`.
- Startup refuses a decision model whose value begins with `~`, and a decision alias naming a chat model. Unit tests cover both.
- `spam_classification_input` is in the closed egress register, in code and on the sensitive-content page. A test proves the guard runs on it under each posture.
- Issue 2672's evaluation reports precision and recall per kind, and the false-positive rate on legitimate mail. That is the measured cost of the minimisation in section 4, and the evidence a threshold is set from.
- A type from the OpenRouter adapter appearing outside it is a violation of this record. So is a second decision adapter, or a decision call reaching an address the operator did not declare.
- Revisit when OpenRouter serves OpenAI's contract, which is #2675. Revisit too when a deployment-local server speaks a decisions contract, when the European host serves a decision model, or when the evaluation shows minimisation costing a kind more than its false-positive rate can bear.

## Pros and Cons of the Options

### 1. OpenRouter's Decisions API, minimised and restricted

- Good, because the answer is closed and scored, which is the property the stage needs from a model reading adversarial text.
- Good, because the catalogue offers many decision models behind one contract. A model can be changed with a configuration value and a re-run of the evaluation.
- Good, because the routing block makes no-retention and no-training a property of every request, not an account setting somebody can forget.
- Neutral, because the second wire protocol is real, bounded, and has an exit with a named issue.
- Bad, because the contract is alpha and can change without notice.
- Bad, because the impersonated party's name is given up.

### 2. The same, sending the whole message

- Good, because the model sees everything, including the link path and the recipient list.
- Bad, because every header and every token in every link reaches two third parties for a question that reads a fraction of them. That is the opposite of data minimisation.
- Bad, because OpenRouter's own limits page reports that accuracy drops as unrelated content grows, so the extra material is not even clearly a gain.

### 3. A chat model with structured output

- Good, because it needs no second wire protocol. Every chat model the deployment already declares could serve it.
- Good, because it could write the impersonated name.
- Bad, because a schema constrains the shape of text, not where the model's judgement comes from. The model still writes, and a message that argues for its own classification is still being read by a writer.
- Bad, because there is no per-option probability to put a threshold on, only a number the model chose to write.
- Bad, because the owner refused it.

### 4. OpenAI's own Decisions endpoint, reached directly

- Good, because it is the contract the exit is waiting for, and OpenAI calls it generally available.
- Bad, because it is outside the owner's direction for now.
- Bad, because it is still a second wire protocol, since no .NET SDK in the stack calls it. It would add a second adapter while leaving the exception open.
- Bad, because it offers one model, and that model has no zero-retention endpoint at OpenRouter. Reached directly, it would need its own data-processing review.

## More Information

- Issue 2669 asks the six questions. Issue 2667 is the parent feature. Issue 2670 records the kind, and changes as section 1 says. Issue 2671 builds the abstraction and the adapter, and adds the register row. Issue 2672 builds the stage and sets the input bound. #2675 is the exit.
- [ADR 0011](0011-reaching-a-provider-outside-the-openai-wire-protocol.md) is the decision this record makes an exception to. [ADR 0030](0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md) relies on it and is unaffected.
- [ADR 0022](0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md) decides what a run reports. [ADR 0031](0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md) decides when the period ledger is written.
- OpenRouter's material, read on 2026-10-25:
  - the OpenAPI document at <https://openrouter.ai/openapi.json>, path `/api/alpha/decisions`;
  - the model listing at <https://openrouter.ai/api/v1/models?output_modalities=decisions>, the per-model endpoint listings, and the zero-retention endpoint list at <https://openrouter.ai/api/v1/endpoints/zdr>;
  - the pages <https://openrouter.ai/docs/guides/privacy/data-collection>, <https://openrouter.ai/docs/guides/privacy/provider-logging>, <https://openrouter.ai/docs/guides/features/zdr>, and <https://openrouter.ai/docs/guides/features/in-region-routing>;
  - the Terms of Service and Privacy Policy, both last updated 31 August 2026;
  - the decision skill and its limits page in <https://github.com/OpenRouterTeam/skills/tree/main/skills/openrouter-decisions>.
- OpenAI's guide at <https://developers.openai.com/api/docs/guides/decisions>, and Microsoft's page at <https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/decisions>, dated 2026-10-21.
