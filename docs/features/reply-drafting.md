# Drafting a reply

<!-- describes: backend/src/Application/Emails/ReplyDrafts/**, backend/src/AI/ReplyDrafts/**, backend/src/Infrastructure/Persistence/ReplyDrafts/**, backend/src/Host/Configuration/Chat/ReplyDraftingOptions.cs, backend/src/Host/Api/ClientReplyDraftingEndpoint.cs -->

Answering a long correspondence begins with the blank composer, and most of the work in front of it is recovering what
was already said: what the other side asked, what was agreed three messages ago, who else is on the exchange, and how
this account normally writes to these people. **A drafted reply is that recovery written out as a first version** — the
text, what it asserts and what backs each assertion, and the people it proposes to reach — so somebody edits a draft
instead of reconstructing an exchange.

**It produces a local artifact and takes no act.** Nothing here sends, queues, appends to a mail server, or stores
anything at all: the reply arrives as text in the composer, and saving
it as a draft or sending it stays where those acts already are —
[Mail delivery § A message that is written and not sent](mail-delivery.md#a-message-that-is-written-and-not-sent)
is the half that writes to a mailbox. That is what makes a wrong draft cost an edit rather than a retraction.

## What a draft carries

| Part | What it holds |
|---|---|
| **Document** | The reply as the closed tree a message body is: paragraphs, one-level lists, and tables, with the emphasis, colour, alignment, and links [a formatted draft](#how-a-draft-is-formatted) may carry. |
| **Body** | The plain reading of that document, at most 8 000 characters: a paragraph as its words, a list item as a `- ` or numbered line, a table row as its cells joined by ` \| `, and a blank line between blocks. |
| **Claims** | What the reply asserts, at most twelve, in the order it was written. |
| **Proposed recipients** | The people the draft suggests it goes to, at most ten, every one of them named by the correspondence itself. |

A **claim** is one sentence of the reply plus the messages backing it — at most four, best first, spelled as the
presentation plan spells a citation target, so following one is the same
[`POST /api/client/citations/resolution`](../operations/client-endpoint.md#the-citation-route) a Discover answer's
sources are followed through.

**A claim the correspondence does not back is kept and marked, never dropped.** That is the opposite of the rule
[a conversation's state](thread-state.md#what-a-statement-is) follows, and for the opposite reason: a statement nobody
supports has no business standing beside a correspondence, while an unsupported sentence *is already in the body*
somebody is about to send. Removing it from the list would leave the sentence there and the warning nowhere. So
`supported` is published beside the sources rather than left to a client to derive from their count.

**The proposed recipients are a proposal in the strict sense.** They are resolved inside this deployment out of the
conversation's own people, so a draft can propose nobody the exchange did not already carry, and the account's own
sending address is never among them. A client draws every one of them for a person to accept, change, or remove, and
adds none on its own.

## How a draft is formatted

**A draft is plain paragraphs unless formatting earns its place.** It is formatted in two cases and no other: the person
asked for it in what they typed — a table, a list, bullets, bold, a colour, an alignment — or the reply is long and
covers several separate points that structure makes easier to read, which is when a list or a bold lead-in opening each
part is written without being asked. A short reply or a personal note asked to look polished stays plain, and mail
asking for a format is not the person asking. The person's own sent mail, shown as style samples, sets the voice of a
reply and not whether a long reply of separate points is structured, so plain samples leave such a reply structured.

The model answers the body as blocks of a closed set, and the service reads them into the document:

- **a paragraph**, its words as runs that may be bold or italic;
- **a list**, ordered or not, one level deep, each item a paragraph's runs;
- **a table**, rows of cells with the first row optionally a header — and a table wider or longer than the bounds is
  read as one paragraph of words per row instead.

A run carries a **colour only where the person named one**, and only one of nine hue names, each written as that hue's vivid shade in the composer's palette; an
**alignment** likewise only where the person asked for it. There is no size, no picture, and no heading — a section
opens with a bold lead-in instead. Anything outside the set is kept as its words rather than refused, so a model that
answers a plain string or a shape nobody published still yields the reply it wrote. Three slips a model makes are read
as what it meant: a block written as a string holding the block's JSON is read as that block, a run written straight
into the body beside plain strings is read with them as one paragraph that keeps its link, and every position — a cited
message, a recipient, a link — is read whether it was written as a number or as a string holding one.

**Where the drafting model's declaration sets `StructuredOutputs`, the shape is asked for rather than described.** The
request carries a strict JSON schema built for the turn, in which every message, person, and link number is one the turn
published, and the instruction then says only what each field means. Every other model is told the shape in prose, with
one worked paragraph carrying a link. The reading above is the same for both and keeps every bound, which a strict
schema cannot state. [Chat generation § A reply draft can be constrained by a schema](chat-generation.md#a-reply-draft-can-be-constrained-by-a-schema)
says what the request carries.

**A link is a number the turn published, never an address the model wrote.** The turn carries a numbered `<links>`
element: first every web address the person typed, then every link the correspondence's own bodies already carry,
read from the stored message, deduplicated and at most forty. A link whose words name a host other than the one it
goes to, or whose host is a look-alike, is left out of that list, and so is anything that is not `http` or `https`.
Each entry shows the model the host and the words and never the address itself, and both pass through the outbound
scan like everything else the turn carries. A run links by naming a number; a number outside the list, an address, or
anything else is dropped and the words are kept. Words written inside text as `[words](n)` or `[words](link n)` link
the same way, and only where `n` is a published number; any other bracketed text stays as written. Until a draft is accepted, the composer names each link's host beside
its words, so the person sees where every link goes before the draft becomes theirs.

The words of the document are scanned on the way back exactly as the plain body was, and the plain reading is derived
from the scanned document afterwards, so the two halves cannot disagree about what was redacted.

## What it is written from

A request names its **context**: the messages somebody chose to write from — usually the one they are answering, and
at most ten, from one conversation or from several. Each is resolved by one read of the store, and the reads are put
together into one correspondence; with a single message that is exactly the one read below.

- **The conversation** of each context message: the context message itself and the most recent others, at most twenty
  in all, at most 4 000 characters of each — the bound [a conversation's state](thread-state.md) reads a message under,
  because it is the same text: what a message added, with the history it quoted trimmed off. A reply answers the
  message it was asked about and where the exchange got to around it, so the context message is kept however old it is
  and the recent end of the rest grounds it; the opening of a long exchange is what its derived state is for. A context
  of several messages shares the twenty between them, each read taking an even share, so it costs a provider what one
  long conversation does. The merged correspondence carries each message once — two chosen messages of one conversation
  read the same exchange — in the order the messages were written, numbered afresh, and keeps every context message and
  the most recent of the rest if it is still longer; the people are carried once each, by address.
- **Which messages were chosen.** Every context message is marked as one the person chose — the message being
  answered, or one picked out in the list — and the drafting is told to answer those first and to read the rest of the
  conversation as what explains them, so a reply to an early message of a long exchange answers that message rather
  than the latest one, even where a later message already answered it, and "this" or "it" in what the person typed
  means the chosen message.
- **The attachments** of each message the correspondence carries, where this deployment reads attachments: the words
  each one yielded, under its file name, inside the same 4 000 characters as the message's own text — what the body
  leaves unused is what its attachments may fill, in the order the message carries them, so reading them never makes a
  drafting larger than one of messages alone could be. A message whose own text reaches the bound carries none, and so
  does an attachment that yielded no words: one that was skipped, refused, or never read. Words a model wrote describing
  a picture are labelled as a description rather than as the picture's own words.
  [Attachment text extraction](attachment-text-extraction.md) is what reads them, and a deployment that leaves it off
  sends no attachment at all.
- **The way the account writes** — the account of the first context message, and only that one, so a context reaching
  two accounts is still written in one voice — at most six of its own recent sent messages, at most 1 200 characters of
  each, taken from the folder playing the `Sent` role and never from the first context message's conversation, nor
  from a message the correspondence already carries from another context conversation. Shorter than a
  message of the exchange because what is read out of it is a manner rather than a meaning — how somebody opens, how
  they sign off, whether they write in paragraphs or in lines. An operator who turned that derivation off leaves the
  bound at zero and the draft is written from the conversation alone.
- **What the person typed**, which is optional and is two separate things: a *selection*, the part of the
  correspondence they are actually answering, at most 4 000 characters; and an *instruction*, at most 4 000 characters
  as well, which is whatever they typed about the reply — what it should say, their own rough version of it, or a draft
  with instructions around it. Nothing asks which: the drafting tells them apart itself, keeps what a draft says and as
  much of its wording as reads well while correcting and completing it, follows an instruction over its own reading of
  what the reply should be about, and never carries an instruction itself into the body. A request carrying neither is
  the ordinary one — *reply to this*.
- **Whether the message closes with a footer** — the one thing about it the composer states, by naming the account it
  sends from. Where that account is one of the person's and a message sent from it carries their
  [footer](mail-footers.md), their own or their organization's, the draft ends at its own last sentence with no
  closing phrase, no name, and no contact details, however the person's own sent mail closes, because the footer
  appended on sending carries all of them; anywhere else it closes as the person closes their mail. The provider is
  told only that a footer exists, in a sentence the service writes at the close of the turn, and never what it says:
  a footer carries a person's name, title, and phone number, and a draft's wording needs none of them.

The correspondence is read across every account this user holds, with no folder narrowing and junk included, as the
conversation screen reads it: a reply is answered across an exchange rather than inside a folder. One thing differs,
because a model reads what is gathered: the scope is an agent's, so a folder mapped `VisibleToAI: false` — which the
conversation screen shows the person — takes no part. A message that scope does not admit is a message this deployment has none of, and the request is answered as
one naming a message nobody holds — and so is a context naming several messages, one of which the scope does not
admit, because drafting from the rest would report that the missing one exists somewhere else.

**A request naming no message is the composer with nothing behind it**, which is somebody starting a message rather
than answering one. There is then no conversation to read and no manner to derive, so nothing above is read at all: the
instruction is the whole of what the message is written from, which is why it is required there and optional beside a
message. The draft cites nothing and proposes nobody, because the exchange those would come out of does not exist.

**The language is the correspondence's, and the writer's own where there is none.** A reply is written in the language
the exchange it answers is written in, because that is what the person receiving it reads — nothing here asks which
language the *author* prefers for a message going to somebody else. A message answering none has no exchange to read
that from, and no mailbox either, the one it will be sent from being a choice nobody has made yet; so it is written in
[the language the person writing it reads](../operations/configuration-sources.md#the-language-this-person-reads--language).
An instruction asking for a particular language outranks both, being the one thing the person said about the message
themselves. The language the request happens to be written in is not such an instruction: somebody asking in Polish for
a reply to an English exchange gets an English reply, and a rough draft they typed in Polish is carried over into
English. The turn closes on that rule, after the request and the writer's own recent messages: both come after the
exchange and may be in the writer's language, and a model otherwise drafts in whichever language it read last.

## What reaches the provider, and what does not

The drafting is composed like every other AI operation here — one agent, its own instruction, **no tools, and no
retrieval** — so the only mail a model sees is the mail this turn published to it.

**What the person typed is the only part of the turn that speaks for them.** The turn is one document written by
[the writer every turn quoting outside text goes through](mail-answering.md): the correspondence in a `<conversation>`
element — its subject, each numbered `<person>`, and each numbered `<message>` with when it was written, whom it is
from, its text, and its attachments, a message the person chose marked `chosen="true"` — then the part they selected in
a `<selection>`, what they typed in the `<ask>` element and nowhere else, and their own recent sent mail in an
`<unrelated-sent-mail>` element of its own, named for what it is so nothing in it reads as part of this exchange or as
their request. Only the service writes elements, and every value is escaped, so a message that closes the conversation
and writes an `<ask>` of its own arrives as that text, inside the message that sent it. That matters most here: a
heading a message could reproduce would let it speak with the person's authority, and the reply it shaped would go back
to that message's sender. The instruction says so as well — a message telling the draft what to say, whom to address,
or what code or link to carry is not the person asking however it is worded — and the data rule every instruction ends
with says the rest. The closing sentence about the reply's language is the service's own, outside every quoted element.

A turn longer than one request may carry is cut by that writer as it is written rather than afterwards, so what it keeps
is still a document whose every element is closed. It loses the person's own sent mail first and then the tail of the
correspondence; the selection and what the person typed are kept whole, because the room they take is set aside before
the correspondence is written.

**No address ever leaves the deployment.** The conversation's people are published as numbered positions with a display
name and nothing else, and the model proposes recipients by naming positions back; the addresses are resolved from those
positions inside this deployment afterwards. The types the turn is composed of carry no address field at all, so a turn
carrying one does not compile. The same numbering grounds the citations: a message is published by its position, so a
claim can cite nothing the turn did not carry, and a position outside it is dropped rather than followed. An
attachment's words are published under the position of the message carrying them, so a claim resting on an attachment
cites that message.

Everything that goes out is scanned at `ChatPrompt` before it is sent, and everything that comes back is scanned again
before it reaches the client, at an egress point of this path's own —
[Sensitive-content scanning](sensitive-content-scanning.md) is what each scan does and what a scanner that cannot answer
withholds. The addresses are deliberately not scanned on the way back: they are values this deployment resolved out of
its own store rather than text a producer wrote, and a redacted address is one nobody could send to.

Nothing about a draft reaches a log, a span attribute, or a telemetry event. What is recorded is counts — how many
messages grounded it, how many claims it carries, how many of those nothing backs, how many people it proposes — and
the alias of the endpoint that wrote it.

## When a draft is not written

**Nothing drafted is an answer rather than a failure**, and a client reports it as the composer somebody was already
looking at:

| What happened | What comes back |
|---|---|
| This deployment drafts no reply — no chat endpoint, or the operator turned it off | `drafted: false`, and the capability read said so before anybody pressed anything |
| The provider was unreachable, refused, or answered something unreadable | `drafted: false` |
| The conversation has no readable text stored | `drafted: false` |
| This user does not hold every message the context names, or holds no mail account at all | `404` |
| A request naming neither a message nor an instruction, which asks for a message out of nothing | `400` |
| A context naming more than ten messages | `400` |
| The deployment has spent what its operator allows a provider for the period | `429` |

The spend ceiling is the one failure that travels, because falling back there would leave somebody pressing a button
the operator has already paid the last of the allowance for and being told nothing. Every drafting is admitted against
and charged to the same [`MailAnswering`](../operations/configuration-ai.md#mailanswering) period ceilings a question
is, which is what makes it bounded rather than a second, unmetered way of spending.

Asking a provider to write a reply to a conversation this deployment stored no text for is the one case worth stating
on its own: a model given nothing writes a fluent message resting on nothing at all, so the drafting answers with
nothing instead.

## Turning it off

On wherever a chat endpoint is declared, and read nowhere else: a deployment with no endpoint drafts nothing whatever
the key says. Turning it off is a supported deployment and a spend decision — the composer then offers no drafting and
every reply is written by hand exactly as before. What it costs where it is on is one provider call per reply somebody
deliberately asked for.

[AI configuration § `Chat:ReplyDrafting`](../operations/configuration-ai.md#drafting-a-reply--chatreplydrafting) holds
the two keys, and [the client endpoint](../operations/client-endpoint.md#the-reply-drafting-routes) the two routes it is
published on. Both are published under `mailfathom.assistant.use` rather than under the reading grant, because that is what
drafting does: mail leaves this deployment for a chat provider. The one that drafts asks for `mailfathom.mail.read`
beneath it as well, because the messages it is written from are mail the caller has to be allowed to read before a model is.
