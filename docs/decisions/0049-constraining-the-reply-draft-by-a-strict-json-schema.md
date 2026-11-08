---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-11-08
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Ask a model for a reply draft under a strict JSON schema built for the turn, where the model's declaration says its server honours one, and keep the prose shape and the forgiving reading for every other model

<!-- describes: backend/src/AI/ReplyDrafts/ReplyDraftSchema.cs, backend/src/AI/ProviderAdapters/ChatGenerationParameterMapping.cs -->

## Context and Problem Statement

The reply-drafting agent answers in a document the client renders: a body of blocks that mirror the composer's closed tree ([ADR 0047](0047-what-the-composer-may-write-beyond-its-closed-set-and-how-it-reads-a-formatted-paste.md), Question 8), claims citing the turn's message numbers, and recipients naming its person numbers. Until now every agent here described its JSON in prose and no request asked a provider for structured output.

Reading `openai/gpt-6-luna`'s drafts showed that most reply-draft failures were the shape of the answer rather than what it said: numbers written as strings, which lost the whole draft; a table or a list written as a string holding JSON; a link written as Markdown inside a string; a numbered list written as paragraphs opening with `1.`; and a recipient numbered past the people the turn published. Issue 3066 made the reading forgive the shapes the model writes, and issue 3067 asks whether the request should constrain them instead.

[ADR 0041](0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md) records the owner's refusal of structured output for spam classification. That refusal was about where a judgement comes from: a schema constrains the shape of the text, and a message arguing for its own classification is still being read by a writer. A reply draft is no judgement of that kind. Its shape is a document the client draws, and a shape error is a draft lost.

## Decision Drivers

- **A shape a provider cannot produce is better than a shape a reader has to repair.** A number outside the conversation, a string where a number belongs, and a block written as a string holding JSON should be impossible rather than forgiven.
- **Not every OpenAI-compatible server honours `json_schema`.** One refuses the request, and another accepts it and ignores the schema. Nothing at startup can tell which, so the operator states it.
- **A router must not quietly drop the schema.** OpenRouter chooses an upstream provider per request, and a provider that ignores the parameter answers in whatever shape it likes.
- **The bounds stay the reader's.** Strict mode drops `maxItems`, `maxLength`, and similar keywords, so a schema cannot carry a draft's bounds.
- **One run measures both paths.** The evaluation declares the same key the deployment does, and its answer cache keys the response format, so an answer written under the schema is never replayed for a request without it.

## Considered Options

- **S1: A strict schema per turn, opt-in per model declaration.** Built for the numbers the turn published, sent through the provider-neutral response format, with the prose shape kept for a model that does not declare it.
- **S2: The same schema for every model, always on.**
- **S3: No schema. Keep describing the shape in prose and make the reader more forgiving.**

## Decision Outcome

Chosen option: **S1**.

- **A key on the chat model declaration, `StructuredOutputs`, off by default.** Where it is off, the request is exactly what it was before. Where it is on, an operation that names a schema sends it as a strict one. Only the reply-drafting agent names a schema today. The evaluation's model declaration reads the same key.
- **Built per turn, through `ChatResponseFormat.ForJsonSchema`.** The schema follows `ReplyDraftDocument`. The body is an array whose items are a string or one of the paragraph, list, and table blocks. A run is a string or a run object carrying its optional link. Every message, person, and link number is an `enum` of the numbers the turn published. A member the turn published nothing for is left out rather than given an empty `enum`. Every object requires every member and admits no other, and a member that does not always apply is nullable.
- **Strict, and required through OpenRouter.** The request sets the client library's `strict` option. A request reaching OpenRouter also carries `provider.require_parameters: true`, merged into whatever routing block the operator declared, so the router never chooses a provider that would drop the schema.
- **The instruction says only what the schema cannot.** Where the schema is sent, the prose description of the shape gives way to what each field means. Where it is not, the prose description stays, with one worked paragraph carrying a linked run. It also says that a block is a JSON value and never a string holding JSON, that a link is never Markdown, that a list is a list block, and that recipients are person numbers and never message numbers.
- **The reader stays forgiving and keeps every bound.** `ReplyDraftReading` reads numbers written as strings and the block shapes a model writes without the schema, and it enforces every bound the schema cannot state.

### Consequences

- Good, because the shape errors that lost whole drafts cannot be produced by a provider honouring the schema.
- Good, because a deployment whose server does not honour a schema changes nothing.
- Good, because a fallback model is asked according to its own declaration, so a chain mixing the two works.
- Bad, because the claim that a server honours the schema is the operator's, and only a request can test it. A server that refuses the schema fails the drafting the way any refused request does, and the person keeps the composer.
- Neutral, because the configuration schema gains a key. That is a change to a public surface below `1.0.0`, and the operator's action is none unless they turn it on.

## Pros and Cons of the Options

### S2 — always on

- Good, because there is no key to explain.
- Bad, because a server that refuses `json_schema` would fail every drafting, and one that ignores it would answer under an instruction that no longer describes the shape.

### S3 — prose and a forgiving reader

- Good, because it asks nothing of the provider.
- Bad, because every new shape a model invents is another repair in the reader, found only after a draft was lost.

## More Information

- Issue 3067 carries the change and issue 3066 the forgiving reading.
- [ADR 0041](0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md) is unaffected. Spam classification still asks no model for structured output.
- OpenAI documents strict structured outputs and the keywords strict mode drops at <https://platform.openai.com/docs/guides/structured-outputs>. OpenRouter documents `provider.require_parameters` at <https://openrouter.ai/docs/features/provider-routing>.
