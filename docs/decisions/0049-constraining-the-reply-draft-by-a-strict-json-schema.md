---
status: proposed
contact: Krzysztof Kasprowicz
date: 2026-11-08
deciders: Krzysztof Kasprowicz
consulted:
informed:
---

# Ask every agent answering in one JSON document for a strict schema built for its turn, where the model's declaration says its server honours one, read every such answer through one forgiving policy, and refuse nothing a schema admits

<!-- describes: backend/src/AI/*/*Schema.cs, backend/src/AI/Orchestration/StrictJsonSchema.cs, backend/src/AI/Orchestration/AgentJsonAnswer.cs, backend/src/AI/ProviderAdapters/ChatGenerationParameterMapping.cs -->

## Context and Problem Statement

The reply-drafting agent answers in a document the client renders: a body of blocks that mirror the composer's closed tree ([ADR 0047](0047-what-the-composer-may-write-beyond-its-closed-set-and-how-it-reads-a-formatted-paste.md), Question 8), claims citing the turn's message numbers, and recipients naming its person numbers. Until now every agent here described its JSON in prose and no request asked a provider for structured output.

Reading `openai/gpt-6-luna`'s drafts showed that most reply-draft failures were the shape of the answer rather than what it said: numbers written as strings, which lost the whole draft; a table or a list written as a string holding JSON; a link written as Markdown inside a string; a numbered list written as paragraphs opening with `1.`; and a recipient numbered past the people the turn published. Issue 3066 made the reading forgive the shapes the model writes, and issue 3067 asks whether the request should constrain them instead.

Nine agents answer with one JSON document a reader parses, and the reply-drafting agent was only the first to be constrained. The same run showed the same class of failure losing answers elsewhere: in three of five thread-state failures the model wrote `"keepTopic": true` beside `"title"` instead of inside it, the reader ignored the misplaced key, the title was left naming no topic, and the whole answer was withheld as unreadable. Issue 3079 asks for every such agent to be constrained the same way, without the schema becoming a new source of lost answers. The readers also ignored a member they did not know wherever it was written, which for a key written at the wrong level silently changed what the document meant.

[ADR 0041](0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md) records the owner's refusal of structured output for spam classification. That refusal was about where a judgement comes from: a schema constrains the shape of the text, and a message arguing for its own classification is still being read by a writer. A reply draft is no judgement of that kind. Its shape is a document the client draws, and a shape error is a draft lost. The same holds for a thread state, an enrichment, a contact card, a Discover plan or result, a calendar extraction, a day layout, and a search phrase: each is a document a reader takes apart, never a verdict on the mail it describes.

## Decision Drivers

- **A shape a provider cannot produce is better than a shape a reader has to repair.** A number outside the conversation, a string where a number belongs, and a block written as a string holding JSON should be impossible rather than forgiven.
- **Not every OpenAI-compatible server honours `json_schema`.** One refuses the request, and another accepts it and ignores the schema. Nothing at startup can tell which, so the operator states it.
- **A router must not quietly drop the schema.** OpenRouter chooses an upstream provider per request, and a provider that ignores the parameter answers in whatever shape it likes.
- **The bounds stay the reader's.** Strict mode drops `maxItems`, `maxLength`, and similar keywords, so a schema cannot carry a draft's bounds.
- **A schema must not turn a recoverable answer into a refusal.** A schema stricter than its reader, a reader stricter than its schema, or a schema a provider refuses would each lose answers the prose path keeps.
- **One policy for every reader.** A member a model invented, a member written at the wrong level, and a member written as `null` should mean the same thing whichever agent wrote it.
- **One run measures both paths.** The evaluation declares the same key the deployment does, and its answer cache keys the response format, so an answer written under the schema is never replayed for a request without it.

## Considered Options

- **S1: A strict schema per turn, opt-in per model declaration.** Built for the numbers the turn published, sent through the provider-neutral response format, with the prose shape kept for a model that does not declare it.
- **S2: The same schema for every model, always on.**
- **S3: No schema. Keep describing the shape in prose and make the reader more forgiving.**

## Decision Outcome

Chosen option: **S1** for the request, and **R1** for the reading.

- **A key on the chat model declaration, `StructuredOutputs`, off by default.** Where it is off, the request is exactly what it was before. Where it is on, every agent answering in one JSON document sends its schema as a strict one: reply drafts, thread states, enrichment, contact relationships, Discover planning and composition, calendar-event extraction, the day layout, and the search phrase. No agent has a key of its own. The evaluation's model declaration reads the same key.
- **Built per turn, through `StrictJsonSchema`.** Each agent's schema follows its document and is written for strict mode: every object requires every member and admits no other, a member that does not always apply is nullable rather than optional, and the root is an object. Where the turn fixes the valid values — a message, person, link, passage, source, or task the turn published — they are an `enum` of exactly those. A provider admits at most a thousand enumerated values across one schema, so a per-turn `enum` past 250 values, or one with none, falls back to a plain integer the reader bounds. A reply draft leaves out an array the turn published nothing for, as it did before. The reply draft's body is an array whose items are a string or one of the paragraph, list, and table blocks, and a run is a string or a run object carrying its optional link.
- **Nothing the schema admits is refused by the reader.** Strict mode cannot state a length, a count, a numeric range, or a pattern, so every schema admits documents a reader's bounds exclude. For each such bound the reader clips, cuts, or drops the one item and keeps the answer wherever the meaning survives, and refuses the answer only where the meaning would be lost; each schema's class says which bound is which. A thread-state title is the worked example: a line that only runs long is cut, a line carrying markup, a link, or a line break is refused, and, where the turn showed a topic, a title naming no new topic keeps the one shown rather than being refused.
- **One reading policy, in `AgentJsonAnswer.Read`, for every agent.** A member no level of the document declares is ignored. A member the document declares only inside one nested object, written instead on the object holding it, is read inside that object, provided the answer wrote the nested object and gave it no value of its own for the member — the thread-state `keepTopic` beside `title` is read inside it. A member several nested objects declare, or that the items of an array declare, belongs nowhere in particular and is ignored rather than guessed into one. A member written as `null` is read as one left out, and an object whose every member is `null` reads as the empty answer it stands for: no title, no mark, no card.
- **Strict, and required through OpenRouter.** The request sets the client library's `strict` option. A request reaching OpenRouter also carries `provider.require_parameters: true`, merged into whatever routing block the operator declared, so the router never chooses a provider that would drop the schema.
- **The instruction says only what the schema cannot.** Each agent's instructions carry both variants, chosen by the model's declaration. Where the schema is sent, the prose description of the shape gives way to what each field means and when it is `null`. Where it is not, the prose description stays. The reply draft's prose variant keeps one worked paragraph carrying a linked run, and says that a block is a JSON value and never a string holding JSON, that a link is never Markdown, that a list is a list block, and that recipients are person numbers and never message numbers.
- **The readers stay forgiving and keep every bound.** One reader serves both variants, so an answer that reads without the schema reads with it. `ReplyDraftReading` still reads numbers written as strings and the block shapes a model writes without the schema, and every reader enforces every bound its schema cannot state.
- **The evaluation cache keys the response format.** The schema travels on the request options the cache key reads, and each agent names its own, so an answer written under one agent's schema is never replayed for a request without it or for another agent's.

### Consequences

- Good, because the shape errors that lost whole answers — a number as a string, a number outside the turn, a field at the wrong level, a field nobody asked for — cannot be produced by a provider honouring the schema.
- Good, because the prose path is more forgiving than it was: a misplaced field is read where it belongs, and a `null` reads as an omission.
- Good, because a deployment whose server does not honour a schema changes nothing.
- Good, because a fallback model is asked according to its own declaration, so a chain mixing the two works.
- Bad, because the claim that a server honours the schema is the operator's, and only a request can test it, and the key now reaches every agent on that model. A server that refuses the schema fails each of those calls the way any refused request does: a drafting leaves the person the composer, and a derivation is withheld and asked again.
- Neutral, because the configuration key covers more than it did. That is a change in what a public key does below `1.0.0`, and the operator's action is none unless they turn it on.

## Pros and Cons of the Reading Policies

- **R1: Ignore an unknown member, read a misplaced one where it unambiguously belongs, read `null` as left out.** Good, because no answer is lost to a field the model invented or misplaced. Bad, because a member that belongs to several places is still ignored, which is why a schema is the stronger answer to it.
- **R2: Refuse an answer carrying a member the reader does not know**. Good, because it notices a model answering something else. Bad, because it loses the whole answer for a field nothing would have read.
- **R3: Refuse an answer carrying a member at the wrong level, naming the member.** Good, because it is explicit. Bad, because it withholds an answer whose meaning the reader could recover.

## Pros and Cons of the Options

### S2 — always on

- Good, because there is no key to explain.
- Bad, because a server that refuses `json_schema` would fail every drafting, and one that ignores it would answer under an instruction that no longer describes the shape.

### S3 — prose and a forgiving reader

- Good, because it asks nothing of the provider.
- Bad, because every new shape a model invents is another repair in the reader, found only after a draft was lost.

## More Information

- Issue 3067 carries the reply-draft change and issue 3066 its forgiving reading. Issue 3079 carries every other agent and the shared reading policy.
- [ADR 0041](0041-asking-a-model-a-closed-question-through-openrouters-decisions-api.md) is unaffected. Spam classification still asks no model for structured output, and the mail-answering agent and the Agent answer in text and through tools rather than in one document.
- OpenAI documents the limits a strict schema stays within — ten levels of nesting, five thousand properties, a thousand enumerated values, and the keywords allowed — on the same page.
- OpenAI documents strict structured outputs and the keywords strict mode drops at <https://platform.openai.com/docs/guides/structured-outputs>. OpenRouter documents `provider.require_parameters` at <https://openrouter.ai/docs/features/provider-routing>.
