# Tasks read out of mail

<!-- describes: backend/src/AI/DayLayout/**, backend/src/Application/Tasks/MailDerivedTaskProposals.cs, backend/src/Application/Tasks/TodayLayout.cs, backend/src/Application/Tasks/OwnTasks.cs, backend/src/Application/Tasks/AwaitedReplies.cs, backend/src/Application/Tasks/PersonalTaskQuery.cs, backend/src/Domain/Tasks/**, backend/src/Application/Tasks/IDayLayoutPlanner.cs, backend/src/Application/Tasks/DayLayout*.cs, backend/src/Application/Emails/Enrichment/EmailTaskProposal.cs -->

A message that asks somebody to do something and a list of what they have to do are the same fact written twice, and
the second copy is the one they make by hand. A reply somebody is waiting for is the same fact seen from the other
side: nothing in a mailbox says that the answer has not come. This page describes what MailFathom does about both. It
**offers** a task when a message asks for one, it **keeps** each task as a commitment that says whose move it is, and
it **arranges** the tasks due today around what the day already holds when somebody asks it to.

None of that decides anything for the person. A proposal waits on the half of the list nobody has agreed to, and an
arrangement is drawn on a screen and forgotten unless the person acts on it — so mail can put something in front of a
reader and can never put it in their day. The one thing mail does change by itself is closing a wait the person kept,
because that is a fact the arrival of the reply settles rather than a decision: the person still reads it and may
reopen the task. What the list itself is, and what accepting or completing a task does, is
[the record](../architecture/stored-email-schema.md#what-a-person-owes) and
[the routes that serve it](../operations/client-endpoint.md#the-task-routes).

## A task is a commitment

A task is a line and, where somebody said so, a due day. What turns it into a commitment is a small set of optional
parts stated together, every one of which a task may leave out — a task is often written down before anybody knows how
long it will take or when it will be done.

| Part | What it says | Bounds |
|---|---|---|
| Direction | Whose move the task waits on: `Owed`, which the person does, or `Awaited`, which somebody else owes them. A task that states none is `Owed`, which is what every task was before the part existed | Two names, read exactly as written |
| Estimate | How long the person expects the work to take | Whole minutes, 5 to 720 |
| Plan | The time the person has set aside for it: an instant it starts and how long the block lasts | Whole minutes, 5 to 720 |
| Anchor | The conversation or the calendar event the task is about, as an identifier and the label it was drawn with — a thread's subject, an event's title | One kind at most, a label of at most 200 characters |
| Counterpart | Who the task is owed to, or who owes it: an address and the name it was written under | An address of at most 320 characters, a name of at most 200 |

**The due day and the plan are two facts, and neither implies the other.** The due day says when the thing is owed; the
plan says when the person intends to do it. A task due Thursday may be planned for Friday, a task may be planned and
carry no due day, and a task may carry either, both, or neither. A calendar draws a task in both places, which is why
reading a window of days matches a task that is due inside it *or* planned to start inside it. Planning never moves the
due day, and the reminder a task announces itself with is measured from the due day alone
([reminders](reminders.md)).

**The anchor and the counterpart are kept as written, not followed.** Both are plain values rather than references, so
a task keeps saying what and whom it was about after the thread, the event, or the mail that introduced the person is
gone. A client that wants to open the thread or the event follows the identifier and finds nothing where it was erased,
and the label is what the task is still drawn with.

**An anchor names only something the person can already open.** Writing a task onto a thread they may not read, or onto
an event that is not on their calendar, is refused as a task about something that does not exist, so a task is never
the way to learn that somebody else's conversation is there. A task that keeps the anchor it already holds is not asked
again, because the label is stored precisely so that a task outlives access to what it was about.

**A write states the whole commitment.** Revising a task restates every part, so a request that leaves out the plan
takes the plan off and one that leaves out the direction states the task owed. A task that has already been answered
refuses a change of direction, which the next section explains.

**The Agent reads whose move a task is and when it is planned, and nothing more of the commitment.** The `read_tasks`
tool answers each task with whether it is open or done, where it came from, its direction as *owed* or *waiting for*,
the day it is due, and where one is set the instant its plan starts and how long it lasts, so *what am I only waiting on*
and *what have I set time aside for* can be answered from the list. The estimate, the anchor, and the counterpart's
address are not shown to it, and the title is guarded as every line a person wrote about their correspondence is. The
tool only reads, and a task the Agent proposes is always an owed one with none of the parts above.

## A wait is closed by the reply it waits for

A task that is `Awaited` and anchored to a conversation closes itself when the answer arrives. The settlement of a batch
of arrivals — the step that admits mail to every read, described in
[the arrival pipeline](../architecture/arrival-pipeline.md#a-message-is-shown-once-it-has-settled) — hands the arrivals
it settled to the step that looks for the waits those arrivals answer. For each arrival in a conversation that holds an
open wait:

- **Who closes it.** A wait that names a counterpart is closed only by a message from that person. A wait that names
  nobody is closed by a message from anybody who is not the person themselves. Addresses are compared in their
  normalized form, so a reply written in another case still counts.
- **What never closes one.** A message the person sent, whether it comes back as a message from their own address or as
  the copy a send leaves in the sent folder, answers nothing. The step also sees only what the settlement
  itself reads as mail for the person's inbox — what settled as junk, in the trash, or tombstoned is not an answer — and
  only what this run settled, so a message stored already settled closes nothing.
  An account that configures no sending address leaves the person's own address unknown, so a wait naming a
  counterpart is still closed by that counterpart, while a wait naming nobody stays open there, because nothing tells
  a reply from the person's own message.
- **What closing does.** The task is marked completed and cites the reply — the message and the instant it arrived —
  as its answer. The person's open clients are told that the list moved, and the task is drawn as replied rather than
  as done by hand. Nothing else is written, and no notification of its own is raised.
- **Once, however many replicas settle it.** The close is one conditional write that holds only for a wait still open, so
  a wait the person completed by hand a moment earlier, or one a second replica closed first, is left as it stands and
  the person is told once.
- **Reopening clears the answer.** A wait the person reopens is open again with no reply cited, and the next reply in
  the conversation closes it afresh. The database holds this as a rule of its own: an answer is only ever recorded on
  a completed `Awaited` task, so the one state that cannot be stored is a reply cited by a task that is not a wait it
  closed. A reply cannot be turned into an owed task either: changing the direction of a wait that has been answered
  is refused with a conflict.

**A wait with no anchor is not closed by settlement.** Nothing in a free-standing wait says which conversation would
answer it, so mail cannot decide that it has been; the person completes it, or anchors it to a thread, which then
closes it like any other. Closing such waits from the sender alone is tracked on issue #3274 rather than guessed at
here.

**A failure here never undoes the settlement.** What follows a settled batch runs after the batch has committed, and a
settled message is never read again, so a failure is not retried. Each follow-up — this one and the automatic reply —
runs in isolation, so one failing does not stop the other, and a failure is counted and logged rather than raised: the
wait that stays open is the visible consequence, and the person completes it by hand.
[Telemetry](../operations/telemetry.md) names the counter, `mailfathom.mail.settlement.follow_up_failures`, tagged
`awaited_replies` for this step.

## A message proposes, and never commits

Reading a message for what it asks of its reader happens inside
[message enrichment](message-enrichment.md#the-same-reading-offers-a-task), in the call that derives the marks and in
no call of its own. What that reading produces is at most three lines per message — a title, and the day it is due on
where the message named one — and each of them is written onto the list of every person the mailbox is assigned to,
under the origin that says nobody has agreed to it.

**A proposal is always something the person would owe.** What mail or the Agent proposes is `Owed`, with no estimate,
plan, anchor, or counterpart: a message asks its reader to do something, and which conversation it belongs to, or who
is waiting for it, is the person's to state when they take it on. A wait is something a person writes down, never
something mail offers.

**What separates a proposal from a task is who put it there**, which is why it is a column on the row rather than a
second table: accepting one is a single state change on a row that already exists, so the identity a client is holding
stays the identity it was holding, and nothing about the task has to be carried across from one place to another.

**Turning enrichment on is the whole of what turns this on.** There is no key of its own, no second call, and no
ceiling beside the one a derivation is already admitted against —
[`Chat:Enrichment`](../operations/configuration-ai.md#message-enrichment--chatenrichment) is the switch, and a
deployment that derives nothing proposes nothing.

**The title is the reader's to fix.** It is written as the model phrased it, collapsed to single spaces and cut to what
the list holds, in the language the mailbox is read in — the same language the marks are written in, and for the same
reason. A proposal somebody accepts is a line they have read; one they dismiss costs them a glance.

## Arranging a day is asked for, once, and applies nothing

The other half runs when a person presses the control that asks for it, and at no other time. Nothing schedules it,
no pass reaches it, and opening a screen does not run it.

What it is decided from is four things and nothing else: the window the client states, the tasks that person owes by
the end of it and has set no time aside for, what they are already committed to during it, and how long a placement may be. The tasks are read
through the same reading their own list is drawn with, and the commitments through the calendar's own reading, so a
day is arranged out of exactly what their screens would have shown — and there is no way here to name somebody else's
list or somebody else's calendar.

| What is read | Which of it |
|---|---|
| Tasks | The person's own, on the half of the list they committed to, `Owed` rather than `Awaited`, not yet completed, due on or before that day, and not already planned inside the window, soonest due first, at most 20 |
| Commitments | The events on their own calendar during the window — not the dates their mail proposed — and the tasks the person has already planned to start inside it, at most 50 together |
| The day | The two instants the client stated, at most 48 hours apart, because nothing here reads a recorded zone to decide which hours a day covers |

**Only what the person owes and has not planned is offered.** A task somebody else owes them is not theirs to place in a
day, so an `Awaited` task is never offered. A task already planned to start inside the window is a block the person set
aside themselves, so it is as fixed as an event: it joins the commitments the placements are made around and is not
placed a second time. A plan that starts before the window is a block of another day, whatever hour it runs into. A task
with no due day is left where it is, so an arrangement never quietly decides that something nobody dated is due now.
The estimate a task carries is handed to the planner as a hint for how long to set aside, where one was stated, and a
placement is still free to differ from it within the 5-minute to 8-hour range.

**What comes back is an offer.** Each placement names a task by identity, when it is suggested to begin, and how long
it is suggested to take; beside them stands the list of tasks that do not realistically fit the day, so somebody is
shown what was left out rather than told about it afterwards. A placement falls inside the window that was asked about
and lasts between 5 minutes and 8 hours, a task stands in at most one of the two lists, and an arrangement naming a
task nobody asked about is dropped rather than drawn.

**Where the model declares `StructuredOutputs`, the shape is asked for rather than described.** The request carries a
strict schema in which every task number is one of the tasks the turn listed and both lists are always present, empty
where nothing belongs in one. The reading is the same either way: a length outside its range is brought within it, and
a placement outside the day is dropped. [Chat generation § An answer in one document can be constrained by a schema](chat-generation.md#an-answer-in-one-document-can-be-constrained-by-a-schema) says what the request carries.

**Nothing is written by asking.** No task is rescheduled, no due day is rewritten, no proposal is accepted, and no
event reaches a calendar — each of those is an act of the person's own through the route that owns it, and laying a
day out twice leaves the same two lists it started with.

**A day with nothing owed is answered without a call at all**, because the only arrangement of nothing is two empty
lists and asking a provider for it would spend a person's allowance on a question with one answer.

## Why a model arranges it

Fitting work into gaps is arithmetic, and how long something takes is not. Nothing here records a duration, an effort,
or a priority, so *what realistically fits before the 3 pm meeting* is a judgement read out of the lines somebody wrote
— which is what the agent is for, and what a sort by due date could not produce. The bound it is given is the one the
code can state: a placement's window, the day's edges, and which tasks exist.

The agent is composed the way every agent in this product is — one instruction carried as its own, an empty tool set,
one name, and the registered instruction envelope around it — and it opens no chat call of its own. The tool set is
empty for the reason enrichment's is: the day is put to it in the turn, so an agent that could look something up would
be an agent able to reach a list it was not asked about.

**What leaves the deployment is the day.** The window, the lines of the tasks owed by the end of it with their due days and estimates, and the titles and
times of what is already committed during it, planned tasks included. No message, no address, no identity of anything, and nothing about a task
beyond that — the answer names tasks by identity because the identities were never sent, they were
numbered. Every line is scanned first by whatever
[sensitive-content scanning](sensitive-content-scanning.md) the deployment switched on, under the posture of the person
whose day it is.

**It is admitted against and charged to the same period ceilings a question is**, and it runs behind the same provider
bulkhead every other outbound call does. [`Chat:DayLayout`](../operations/configuration-ai.md#arranging-a-day--chatdaylayout)
is what routes it to a model of its own; a deployment that declares no chat endpoint arranges no day, and the client is
told so before it offers the control.

## When no arrangement is offered

| Withheld because | What it means |
|---|---|
| **Not activated** | The deployment declared no model to arrange a day with. Nothing is read to say so — not the list, not the calendar. |
| **Allowance exhausted** | The answering period has no admission left for another call. |
| **Provider unavailable** | The model failed, timed out, was unreachable, or its credential would not resolve — and where a fallback was declared behind it, that model could not answer either. |

A provider that *answered* with something unreadable is not a withholding: the call was made and paid for, and asking
again buys the same answer, so the day is answered with an arrangement of nothing rather than with a failure nobody can
act on. A day the arrangement genuinely had nothing to place is that same answer, which is why a client draws both the
same way. What the two routes make of each of these is
[the day-layout routes](../operations/client-endpoint.md#the-day-layout-routes).

## Privacy

- **A proposal is derived personal data and inherits the message's classification whole.** What is stored for one read
  out of mail is the title, the day, and the identity of the message it was read out of — never a subject, a body, or
  an address. A proposal carries no anchor and no counterpart: those are stated by the person, or by the screen that
  made the task for them.
- **A commitment's anchor and counterpart are personal data the person chose to keep.** The anchor's label is a copy of
  a subject or an event title and the counterpart is an address and a name, so they are classified as sensitive like the
  mail they were taken from, are bounded as the table above says, and go when the task is erased. The reply that
  closed a wait is cited by identity and instant only, never copied. None of them reaches a log or a span, and the
  address is the one thing a task answers with that names a person outside the deployment.
- **A task outlives the mail it cites**, deliberately: the citation is a plain value rather than an association, so
  erasing the message leaves the task standing and its citation resolving to nothing. Somebody still owes the thing.
  [The record](../architecture/stored-email-schema.md#what-a-person-owes) holds that
  reasoning, the absence of a retention bound, and what a data-subject erasure reaches instead.
- **Nothing about a day reaches a log or a span.** What an arrangement reports about itself is the endpoint that
  answered, how many tasks it was given, how many it placed, and the reason one was withheld. Never a title, never a
  time, never whose day it was.
- **An arrangement is never stored.** It exists for the length of the response, which is also why nothing has to be
  erased when somebody changes their mind.

## What is not here

- **Accepting anything automatically.** No pass, no schedule, and no arrangement moves a task onto the committed half
  of a list. A person does that, or it does not happen.
- **Chasing a task nobody asked to be reminded about.** A due day on its own is a column, and no pass reads it. What is watched is a [reminder](reminders.md) somebody put on that due day, which is the same mechanism a calendar event announces itself through.
- **Writing an arrangement onto a calendar.** A placement is a suggestion about a day, not an event — putting one on a
  calendar is [a calendar act](calendar-events.md) somebody takes.
- **Any external task protocol.** The list is native to this deployment in the strong sense: nothing synchronizes, and
  nothing is exported to a task manager.
- **Re-reading a message for tasks.** A settled derivation is settled, including after the instruction or the model
  behind it changes.
