## Context

Part of #{{PARENT}}. The model stage in #{{MODEL}} has to ask a model whether a message is spam and, when it is, which spam subcategory from the parent's table it belongs to. The intended mechanism is a **Decisions API**: an endpoint that answers a closed set of choices with a probability for each option, rather than with free text. OpenAI and OpenRouter both expose one, and both are pre-release. What is known about either so far comes from secondhand reports rather than from the providers' own references, so nothing about request shape, model names, pricing, or data terms is assumed here. This issue produces that knowledge, and #{{MODEL}} waits on it.

### What the SDKs support today

Checked on 2026-10-24 by inspecting the published NuGet packages for any `Decision` type, member, or endpoint string. The pinned versions were checked, and so was the newest release of each:

| Package | Pinned | Newest | Decisions API |
|---|---|---|---|
| `OpenAI` (openai-dotnet) | 2.14.0 | 2.14.0 | None: no client, no model type, no `/decisions` route. The SDK's own tracker has an open feature request for it (openai/openai-dotnet#1434), which reports that other OpenAI SDKs already ship `client.decisions` |
| `Microsoft.Extensions.AI.Abstractions` / `.OpenAI` | 10.10.1 | 10.11.0 | None. The abstraction is `IChatClient` and `IEmbeddingGenerator`, and a decision is neither |
| `Microsoft.Agents.AI` / `.OpenAI` (Agent Framework) | 1.24.0 | 1.26.0 | None. The only "decision" in it is the tool-approval decision, which is unrelated |

So no SDK in this stack can make a Decisions API call. The other consequence is what such a call bypasses. Everything this deployment wraps around a model call hangs on `IChatClient`:

- the budget (`BudgetedChatClient`);
- retries and failure classification (`ResilientChatClient`, `ProviderCallFailureClassification`);
- run cost reporting under ADR 0022;
- the evaluation suite's answer cache, keyed on chat messages and `ChatOptions`.

### The owner's direction

Settled before the ADR, so the ADR records it rather than re-opening it:

- **OpenRouter only, for now.** The stage reaches the Decisions API through OpenRouter alone. OpenAI's own endpoint is not integrated until there is a reason to, and the chat-completion fallback is not built.
- **MailFathom writes its own decision-client abstraction**, because none exists. It is shaped so that it plugs into Microsoft.Extensions.AI and the Agent Framework the way `IChatClient` does: a provider-neutral interface, delegating middleware, a builder, and dependency-injection registration. The budget, retries, failure classification, cost reporting, telemetry, and evaluation cache can then wrap it as they wrap a chat client today. When Microsoft.Extensions.AI or openai-dotnet ships an official abstraction, the OpenRouter adapter is what gets replaced, and nothing above it moves.

- **Decision models are configured apart from chat models.** A decision model is a different kind of model, so the default is not `Chat:MainModel`. A `Decisions` section declares decision models, a `Decisions:DefaultModel`, and a model reference per function, mirroring how `Chat` routes each capability. Spam classification is the first function.

#{{ABSTRACTION}} builds that abstraction, the `Decisions` configuration, and the OpenRouter adapter beneath them.

OpenRouter's endpoint is reported at `/api/alpha/decisions` with a request shape of its own (a `state` plus `questions`). That rests on a single secondhand source and is unverified. Verifying it against OpenRouter's own documentation is the first thing this issue does. The status of OpenAI's own endpoint is reported inconsistently, from limited preview through public beta to generally available, and it is out of scope for now in any case.

A closed answer is worth having for two reasons specific to this stage. A message is adversarial text, and a model that can only pick an option cannot be talked into writing something else. A probability per option is also a score that a threshold can judge, which is the shape a classification already records in `SpamAssessment`.

The questions that need an answer before #{{ABSTRACTION}} and #{{MODEL}} are written:

1. **Fitness.** Do the API's question types cover what the stage needs: a spam verdict, and on spam one subcategory out of six; for phishing, what is being asked for (credentials, payment details, one-time code); and for phishing and business email compromise, whom the message impersonates (which may need free text the API does not allow)?
2. **OpenRouter's contract.** What is the endpoint's real request and response shape, its release status and stability commitment, how failures and rate limits are reported, and which models serve it? How does the decision-client abstraction map onto it without leaking OpenRouter's vocabulary upward? ADR 0011 already governs reaching a provider outside the OpenAI wire protocol, so the ADR states how this adapter sits under it.
3. **Where it runs.** Can a self-hosted or local model serve the same contract? If not, enabling the stage sends every classified message to a third party, which is a different privacy posture from the scanner it replaces, since the scanner ran inside the deployment.
4. **What leaves the deployment.** Headers, body text, link targets, attachment names. Redaction weakens phishing detection the same way it weakened the scanner, so what is sent unredacted, and under which new egress point in the [sensitive-content register](https://github.com/fathomise/MailFathom/blob/main/docs/features/sensitive-content-scanning.md#the-guarded-egress-points)?
5. **Provider terms.** OpenRouter's retention, training use, data-processing terms, and region, and those of the providers it routes a decision to. A router is a second party between this deployment and the model, so both sets of terms apply, and the ADR states which routing options (for example, provider restrictions or no-retention routing) the stage requires.
6. **Cost and reporting.** Cost per classified message at the bound #{{MODEL}} will set, and how a call is reported under ADR 0022.

## User stories

**As the owner** I want OpenRouter's Decisions API verified against the stage's needs before code depends on it, **so that** an alpha API does not shape a contract it cannot keep.

**As the operator of a deployment** I want a written decision on what the model stage sends and to whom, **so that** I know what enabling it means for my users' mail.

## Acceptance

- An ADR under `docs/decisions/`, approved by the owner, answers each of the six questions above.
- The ADR names the egress point the stage's input crosses and states what is sent unredacted.
- The ADR records the owner's direction above:
  - OpenRouter only, for now;
  - MailFathom's own decision-client abstraction, shaped for Microsoft.Extensions.AI and the Agent Framework;
  - no chat-completion fallback;
  - decision models configured in their own section, with a default model and a per-function reference.
- The ADR states what an operator configures to reach OpenRouter's Decisions API, and what the stage does in a deployment that configures none.
- The ADR states how the budget, the retry and failure classification, the cost report, and the evaluation cache reach a decision call through that abstraction.
- The SDK table above is re-checked against the versions pinned when the ADR is written.
- Anything the decision adds to `THIRD_PARTY_LICENSES.md` (a provider service or terms) is recorded.

## Dependencies

None. #{{ABSTRACTION}} and #{{MODEL}} wait on this.

## Baseline

1d26b66df368199d215814baacb461e90f902748
