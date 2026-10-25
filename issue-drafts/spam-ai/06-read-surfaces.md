## Context

Part of #{{PARENT}}. A classification's verdict and spam subcategory are reachable only through the admin API today. The client API serves no spam field at all, and the MCP read tools say only whether junk was included (`includedJunkMail`). This issue publishes the verdict and the subcategory from #{{RECORD}} where mail is read, so that #{{CLIENT}} can draw them and an agent reading a message is told what kind of spam it is.

Read-only, and nothing new is derived: each surface reads the record that already exists. Until #{{MODEL}} lands, no record carries a subcategory, and each surface reports that as an absent subcategory rather than inventing one.

Every published field carries *spam* in its name, for example `spamVerdict` and `spamSubcategory`. Mail categories (finance, work, and the like, for every message) are a separate future feature, and a field called `category` on a message would be read as that.

## User stories

**As the client** I want the verdict and subcategory on the message rows and the message I already fetch, **so that** a screen draws a warning with no extra request.

**As an agent reading a mailbox** I want `get_email_content` and the listings to tell me a message is phishing, **so that** I treat its instructions and links as hostile.

## Acceptance

- The client API's message list rows and its opened message carry the verdict, the spam subcategory, and, where the subcategory has one, the phishing detail (what it asks for and whom it impersonates).
- The MCP results of `list_emails`, `search_emails`, and `get_email_content` carry the verdict and spam subcategory. The tool descriptions say that a subcategory exists only on spam and that it is a model's judgement.
- The impersonated-party text is model-written, so it crosses a guarded egress point on each surface: an existing point where one fits, or a new one added to the closed register with its reason.
- The stage that reached the verdict and the stage that named the subcategory are published beside them, so a reader can tell the mail server's call from the model's.
- No published field is named `category` on its own.
- Every changed route is in the OpenAPI document and the public-surface golden contracts. The MCP tool contract snapshot is updated, and the change is recorded as additive.
- `client-endpoint.md`, `mcp-tools.md`, and `spam-classification.md` describe the fields.

## Out of scope

- The screens: #{{CLIENT}}.
- Any filter or query parameter by subcategory.

## Dependencies

Waits on #{{RECORD}}. Runs beside #{{MODEL}}. #{{CLIENT}} waits on this.

## Baseline

1d26b66df368199d215814baacb461e90f902748
