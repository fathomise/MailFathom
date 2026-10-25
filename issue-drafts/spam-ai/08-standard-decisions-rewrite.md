## Blocked

**The target implementation does not exist yet.** This issue waits on two things outside the project:

1. OpenAI publishes the Decisions API as a documented, stable contract.
2. OpenRouter implements that contract.

Until both hold there is nothing to move onto, so the issue carries the `blocked` label and `Status: Blocked`, and sits in `Queue: Parked`. Re-check both conditions when either provider announces a change to its Decisions endpoint.

## Context

#{{ABSTRACTION}} gives MailFathom its own decision-client abstraction with a single adapter for OpenRouter's pre-release Decisions endpoint. No SDK in the stack supports a Decisions API: `OpenAI` 2.14.0, `Microsoft.Extensions.AI` 10.11.0, and `Microsoft.Agents.AI` 1.26.0 carry nothing for it (#{{DECISION}} holds the table), and openai-dotnet has an open request for it (openai/openai-dotnet#1434).

That adapter is a **deliberate and bounded departure from the OpenAI wire protocol**. ADR 0011 makes the single OpenAI-compatible client construction the whole of MailFathom's provider reach and refuses a second wire protocol, and ADR 0030 relies on that refusal. The ADR written for #{{DECISION}} records the exception and its limits:

- it covers decisions only;
- it is temporary, lasting until OpenAI publishes a standard Decisions contract and OpenRouter implements it.

This issue is the exit from that exception. Once both conditions hold:

- the OpenRouter-specific wire code is replaced by the standard contract, reached through the existing single client construction;
- where openai-dotnet or Microsoft.Extensions.AI ship a decision client or abstraction, MailFathom's adapter, and its own abstraction where theirs covers it, gives way to theirs;
- the exception recorded against ADR 0011 is closed, so the provider reach is one construction again.

## User stories

**As the owner** I want the custom Decisions adapter retired as soon as a standard exists, **so that** the exception to ADR 0011 lasts no longer than the gap it was made for.

**As a maintainer** I want decisions to travel through the same client construction as chat and embeddings, **so that** there is one wire protocol to keep working again.

## Acceptance

- Decisions are reached over the standard OpenAI Decisions contract through the single OpenAI-compatible client construction, against OpenRouter and against any other endpoint that serves the standard.
- The OpenRouter-specific request and response code from #{{ABSTRACTION}} is removed.
- Where an official .NET decision client or Microsoft.Extensions.AI abstraction exists, MailFathom uses it. The decision-client abstraction from #{{ABSTRACTION}} is either replaced by it or reduced to a thin adapter over it, and the pull request says which and why.
- The `Decisions` configuration keeps its shape (models, default model, per-function references), or the pull request records each break with the operator's action.
- The evaluation scenario of the spam model stage runs unchanged and its figures are compared before and after the move.
- A new ADR closes the exception recorded for #{{DECISION}} and states that ADR 0011's single client construction holds again without it.

## Dependencies

Follows #{{ABSTRACTION}}, which writes the adapter this issue retires. It is outside #{{PARENT}} on purpose: that feature is done without it, and this issue is about the provider reach rather than about spam.

## Baseline

1d26b66df368199d215814baacb461e90f902748
