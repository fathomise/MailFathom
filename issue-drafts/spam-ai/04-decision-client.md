## Context

Part of #{{PARENT}}. **Waits on #{{DECISION}}**, which verifies OpenRouter's Decisions API and records the owner's direction in an ADR.

No SDK in this stack can call a Decisions API today. `OpenAI` 2.14.0, `Microsoft.Extensions.AI` 10.11.0, and `Microsoft.Agents.AI` 1.26.0 carry no decision client, no decision types, and no route; #{{DECISION}} holds the table. So MailFathom writes the abstraction itself, with one adapter beneath it for now: **OpenRouter**. OpenAI's own endpoint is not integrated, and no chat-completion fallback is built.

The abstraction is shaped so that it plugs into Microsoft.Extensions.AI and the Agent Framework the way `IChatClient` does. There are two reasons:

- everything this deployment wraps around a model call can wrap a decision the same way: the budget, resilience and failure classification, cost reporting under ADR 0022, telemetry, and the evaluation cache;
- when Microsoft.Extensions.AI or openai-dotnet ships an official abstraction, the OpenRouter adapter is what gets replaced or wrapped, and the model stage in #{{MODEL}} does not move.

**This adapter is the exception to ADR 0011** that #{{DECISION}}'s ADR records: a second wire protocol, for decisions only, until OpenAI publishes a standard Decisions contract and OpenRouter implements it. Everything OpenRouter-specific therefore stays inside the one adapter, so that #{{STANDARD}}, the parked rewrite onto the standard, replaces that adapter and touches nothing above it.

This issue knows nothing about spam. It delivers a general way to ask a model a closed question; #{{MODEL}} is its first caller.

Proposed shape, for the implementer to refine against what #{{DECISION}} verified:

- **`IDecisionClient`**, mirroring `IChatClient`:
  - one asynchronous call that takes the input as Microsoft.Extensions.AI `ChatMessage` / `AIContent`, so text and data contents and the existing turn-document envelope are reused, together with the questions and a `DecisionOptions`;
  - `GetService(Type, object?)`;
  - `IDisposable`.
- **Questions and answers** in Microsoft.Extensions.AI's idiom:
  - the question kinds the API offers (a choice among named options, yes/no, a score), named neutrally rather than after OpenRouter's wire vocabulary;
  - an answer per question, carrying the chosen value and a probability per option;
  - `UsageDetails`, `ModelId`, `RawRepresentation`, and `AdditionalProperties`.
- **`DecisionClientMetadata`**: provider name, endpoint, and default model, like `ChatClientMetadata`.
- **Composition**: a `DelegatingDecisionClient` base, a `DecisionClientBuilder` with `Use(...)`, and a dependency-injection registration in the style of `AddChatClient`, so middleware composes in the same order a chat pipeline does.
- **Agent Framework**: a decision client can be offered to an agent as an `AIFunction`, so an agent can ask a closed question as one of its tools.
- **Configuration, as its own section beside `Chat`**. A decision model is a different kind of model from a chat model, so `Chat:MainModel` cannot be its default and no chat model can be named here. The section mirrors how `Chat` routes capabilities to models today ([`configuration-ai.md` § Which model a capability runs on](https://github.com/fathomise/MailFathom/blob/main/docs/operations/configuration-ai.md#which-model-a-capability-runs-on--chatmainmodel-and-the-references-beside-it)):
  - `Decisions:Models:<n>`: the declared decision models, each under its own alias, with its address, credential, model name, request timeout, and request bounds, in the same shape and through the same validation as a `Chat:Models` block. For now the only kind of endpoint accepted is OpenRouter's decisions endpoint;
  - `Decisions:DefaultModel` with `Alias` and `Fallback`: the model every decision function runs on unless it names its own. It may be left unwritten where exactly one decision model is declared;
  - `Decisions:<Function>:Model` with `Alias` and `Fallback`: a per-function reference, resolved through the same chain as a chat capability's (its model, its fallback, then `Decisions:DefaultModel`). What a chat capability is to `Chat`, a function is here. The first function is spam classification, `Decisions:SpamClassification:Model`, which #{{MODEL}} adds;
  - an alias naming a `Chat:Models` entry, or a decision alias named from `Chat`, is refused at startup and the refusal names the key;
  - a run reports the decision model that answered, including a fallback, exactly as a chat run does.
- **Placement**: the neutral types live in their own namespace in `AI`, with no MailFathom domain types in them, so they can be extracted, upstreamed, or swapped for an official abstraction. The OpenRouter adapter lives beside the existing provider adapters, and no OpenRouter type crosses above it. `Domain` and `Application` never see either: #{{MODEL}} reaches it through an `Application` port.

## User stories

**As a maintainer** I want a decision client shaped like `IChatClient`, **so that** budget, retries, cost reporting, telemetry, and the evaluation cache apply to it without being written a second time.

**As the owner** I want the OpenRouter-specific code confined to one adapter, **so that** moving to an official abstraction later replaces that adapter and nothing else.

**As the operator of a deployment** I want decision models declared once, with a default and a per-function override, just as chat models are, **so that** I can route each decision to the model that suits it, and credentials, timeouts, and failure reporting behave the same way.

## Acceptance

- `IDecisionClient`, its request, answer, and options types, `DecisionClientMetadata`, `DelegatingDecisionClient`, and `DecisionClientBuilder` exist in the `AI` project. They use Microsoft.Extensions.AI types wherever one fits: `ChatMessage`, `AIContent`, `UsageDetails`, and `AdditionalPropertiesDictionary`.
- A middleware for each of these exists and is proven by a unit test:
  - the budget;
  - resilience with the existing failure classification, telling caller cancellation, timeout, authentication failure, and transient failure apart;
  - cost reporting under ADR 0022;
  - telemetry that carries no input content.
- An evaluation-cache middleware keys an answer on the input, the questions, the options, and the model, so `backend/tests/Evaluations` reads a filed answer back exactly as it does for chat. An `EvaluationStoreTests`-style case proves that a changed question asks the model again.
- A test proves a decision client exposed as an `AIFunction` is callable by an Agent Framework agent.
- The OpenRouter adapter speaks the contract #{{DECISION}} verified:
  - it reuses the existing provider-endpoint credential and reach declarations;
  - its outbound HTTP client is registered with the lifetime and bounds `backend/src/AGENTS.md` requires;
  - it has explicit request and response size limits;
  - it maps every failure to the existing classification.
- No OpenRouter request or response type, route, or field name appears outside the adapter, so #{{STANDARD}} can replace the adapter alone. The adapter's own documentation comment names the ADR 0011 exception and #{{STANDARD}}.
- Unit tests drive the adapter against a fake HTTP handler, and cover a well-formed answer, an unknown option, a missing probability, and each failure class.
- One provider-contract test under `backend/tests/IntegrationTests/ProviderAdapters` reaches OpenRouter only when the existing provider-contract switch is on, following `backend/tests/AGENTS.md`.
- `Decisions:Models`, `Decisions:DefaultModel`, and the per-function reference resolution exist and are validated at startup. Unit tests cover:
  - the chain;
  - the single-model default;
  - a fallback naming its own model;
  - an alias that names a chat model;
  - an undeclared alias.

  The new keys are in the public-surface configuration-key contract.
- `docs/operations/configuration-ai.md` describes the `Decisions` section next to `Chat`. `THIRD_PARTY_LICENSES.md` records OpenRouter as a service if #{{DECISION}} says so, and any package this needs is pinned and recorded.

## Out of scope

- OpenAI's own Decisions endpoint, and a chat-completion fallback.
- Anything about spam: #{{MODEL}}.

## Dependencies

Waits on #{{DECISION}}. Queue `Needs decision` names #{{DECISION}} as the decision it waits on. #{{MODEL}} waits on this.

## Baseline

1d26b66df368199d215814baacb461e90f902748
