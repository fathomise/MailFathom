// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Fathom: the button in the bottom-right corner of every space, and what it offers.
//
// `menu` is what pressing it unfolds, per space: the space's own actions first (in a thread, the two the thread's
// button always had), then *Smart*, then the Agent. `components` is what *Smart* finds under the pointer: an element
// carrying `data-smart="<id>"` is one component, named and given its own actions. `itemActions` is what a press on
// anything inside the catch-up offers, by the item's kind, and `selection` what selected text offers anywhere Fathom
// reads it.
//
// An action's `do` says what it leads to: `answer` writes a short answer in the Fathom card, with *Continue with the
// agent* under it; `agent` opens the Agent with the component as its context; `go` opens the space that owns it;
// `toast` acts in place and reports with *Undo*. A component's `answers` carries the line `answer` shows for one of its
// actions; anything without one gets the action's `line`, which names what the model would read.
MailFathomDesign.data.fathom = {
  menu: {
    start: [
      { id: "ask-day", icon: "forum", label: "Ask about your day", do: "agent", ctx: "your day" },
      { id: "plan-day", icon: "event_note", label: "Plan the rest of today", do: "agent", ctx: "planning today" },
    ],
    mailList: [
      { id: "ask-folder", icon: "forum", label: "Ask about this folder", do: "agent", ctx: "the Inbox" },
      { id: "sum-unread", icon: "summarize", label: "Summarize what is unread", do: "answer" },
    ],
    mailThread: [
      { id: "draft-reply", icon: "edit_note", label: "Draft a reply", do: "draft" },
      { id: "ask-thread", icon: "forum", label: "Ask", do: "agent" },
    ],
    cal: [
      { id: "ask-week", icon: "forum", label: "Ask about this week", do: "agent", ctx: "this week's calendar" },
      { id: "find-slot", icon: "manage_search", label: "Find a free slot", do: "answer" },
    ],
    tasks: [
      { id: "ask-tasks", icon: "forum", label: "Ask about your tasks", do: "agent", ctx: "your tasks" },
      { id: "slipped", icon: "history", label: "What slipped this week?", do: "answer" },
    ],
    people: [
      { id: "ask-person", icon: "forum", label: "Ask about this person", do: "agent" },
      { id: "waits", icon: "hourglass_top", label: "What do they wait for?", do: "answer" },
    ],
    agent: [
      { id: "new-conv", icon: "add_comment", label: "New conversation", do: "agentNew" },
    ],
  },
  menuAnswers: {
    "sum-unread": "Seven unread: two from Contoso about the addendum, one Fabrikam date question, and four newsletters that can wait.",
    "find-slot": "Free today 15:30-16:30 and tomorrow 09:00-11:00. Thursday afternoon is the longest gap this week.",
    "slipped": "Two tasks moved past their date: the Fabrikam confirmation (twice) and the audit list, which is due 3 September.",
    "waits": "Anna waits for the CPI cap counter-proposal, asked twice since 7 August.",
  },

  components: {
    "start.ask": { name: "Ask or find", what: "the question field", actions: [
      { id: "suggest", icon: "lightbulb", label: "Suggest a question", do: "answer", line: "Three questions your mail answers today: what Contoso changed since 2021, where the latest insurance policy is, and who approved the indexation budget." },
      { id: "recent", icon: "history", label: "Show my recent questions", do: "answer", line: "Last asked: “Where is the latest version of the insurance policy?” on 6 August." },
    ] },
    "start.counts": { name: "The day's counts", what: "counts read off your mail, calendar and tasks", actions: [
      { id: "summarize", icon: "summarize", label: "Summarize the new mail", do: "answer", line: "14 new: three need a reply today, five are FYI, six are newsletters and notifications." },
      { id: "only-me", icon: "filter_alt", label: "Show only what needs me", do: "go", go: "waiting" },
      { id: "draft-all", icon: "edit_note", label: "Draft replies to the three", do: "agent", ctx: "replies to the three threads waiting on you" },
      { id: "archive-rest", icon: "archive", label: "Archive what needs nothing", do: "toast", toast: "Six newsletters and notifications archived." },
    ] },
    "start.catchup": { name: "Catch-up", what: "while you were away", actions: [
      { id: "open", icon: "open_in_full", label: "Open it", do: "catchup" },
      { id: "decisions", icon: "rule", label: "Only the decisions left", do: "answer", line: "Three: the Contoso counter-proposal (due tomorrow), Fabrikam's Q4 date (02.09) and the audit register (03.09)." },
      { id: "done", icon: "done_all", label: "Done with it", do: "catchupDone" },
      { id: "export", icon: "ios_share", label: "Export", do: "catchupExport" },
    ] },
    "start.briefing": { name: "Morning briefing", what: "written from today's new mail", actions: [
      { id: "expand", icon: "unfold_more", label: "Expand it", do: "answer", line: "The Contoso addendum decision is due tomorrow. Anna asked twice and copied legal on 25 August; the draft on Start proposes a 5% cap. Fabrikam needs a Q4 date before phase three can start, and Tomasz offered 14 or 21 October. Audit wants the IT contract register by 3 September; only the Contoso addendum is missing from it." },
      { id: "shorter", icon: "compress", label: "Make it shorter", do: "answer", line: "Answer Contoso today. Confirm Fabrikam's date. The rest can wait." },
      { id: "followup", icon: "forum", label: "Ask a follow-up", do: "agent", ctx: "this morning's briefing" },
      { id: "why-order", icon: "help", label: "Why in this order?", do: "answer", line: "By deadline first, then by how many people are waiting. Contoso is due tomorrow and two people wait on it." },
      { id: "translate", icon: "translate", label: "Translate", do: "answer", line: "Decyzja w sprawie aneksu Contoso jest jutro, a kontrpropozycja z limitem CPI wciąż nie wyszła. Fabrikam czeka na potwierdzenie daty, audyt na listę umów IT. Reszta poczty może poczekać." },
    ] },
    "start.briefingOffer": { name: "Briefing offer", what: "the written briefing, off", actions: [
      { id: "preview", icon: "visibility", label: "Show me today's, once", do: "answer", line: "The Contoso addendum decision is due tomorrow. Fabrikam is waiting for a date and audit for the IT contract list. The rest of the mail can wait." },
      { id: "cost", icon: "payments", label: "What does it cost?", do: "answer", line: "About 6,000 tokens a morning, read from that day's new mail only; it counts against your monthly allowance." },
    ] },
    "start.headsUp": { name: "Heads up", what: "risks raised by rules", actions: [
      { id: "why", icon: "help", label: "Why is this a risk?", do: "answer" },
      { id: "sender", icon: "verified_user", label: "Check the sender", do: "answer", line: "fabrikam-billing.co was registered 9 days ago and has never written to you before. Fabrikam writes from fabrikam.com." },
      { id: "safe", icon: "check", label: "Mark as safe", do: "toast", toast: "Marked as safe. It will not be raised again." },
      { id: "open", icon: "open_in_new", label: "Open the message", do: "go", go: "thread" },
    ] },
    "start.since": { name: "Since you last looked", what: "what arrived since 08:12", actions: [
      { id: "summarize", icon: "summarize", label: "Summarize what arrived", do: "answer", line: "Since 08:12: Contoso asked again about the cap, Marta sent the September calculation, and Piotr moved the rollout call to 15:00. The rest is newsletters." },
      { id: "which", icon: "person_alert", label: "Which ones need me?", do: "go", go: "waiting" },
      { id: "archive", icon: "archive", label: "Archive the safe ones", do: "toast", toast: "Six messages that need nothing archived." },
      { id: "rules", icon: "rule", label: "How is this sorted?", do: "answer", line: "By rules, not a model: a question asked of you, a date in the thread, a sender you reply to, and lists you never open." },
    ] },
    "start.nextUp": { name: "Up next", what: "your next meeting", actions: [
      { id: "prep", icon: "auto_awesome", label: "Prepare me", do: "prep" },
      { id: "expect", icon: "psychology_alt", label: "What do they expect from me?", do: "answer", line: "Piotr wants a go or no-go on phase three; Tomasz will ask for the Q4 date you have not confirmed." },
      { id: "agenda", icon: "list_alt", label: "Draft an agenda", do: "agent", ctx: "an agenda for “Rollout status with Piotr”" },
      { id: "late", icon: "schedule_send", label: "Tell them I am 5 minutes late", do: "toast", toast: "Draft to Piotr and Tomasz ready in the composer." },
      { id: "move", icon: "event_repeat", label: "Find another time", do: "agent", ctx: "another time for “Rollout status with Piotr”" },
    ] },
    "start.needs": { name: "Needs you today", what: "raised by rules", actions: [
      { id: "summarize", icon: "summarize", label: "Summarize the thread", do: "answer" },
      { id: "draft", icon: "edit_note", label: "Draft a reply", do: "agent", ctx: "a reply" },
      { id: "remind", icon: "notifications", label: "Remind me at 16:00", do: "toast", toast: "Reminder set for 16:00." },
      { id: "delegate", icon: "forward_to_inbox", label: "Hand it to someone", do: "agent", ctx: "handing this over" },
      { id: "why", icon: "rule", label: "Why is it here?", do: "answer" },
    ] },
    "start.step": { name: "One step", what: "a step the agent proposes", actions: [
      { id: "why", icon: "help", label: "Why this step?", do: "answer", line: "Contoso is due tomorrow and you have no free block before the 15:00 call except this one." },
      { id: "another", icon: "alt_route", label: "Suggest another", do: "answer", line: "A 30-minute block tomorrow at 09:00 would also make the deadline, with less room to write the reply." },
      { id: "agent", icon: "forum", label: "Talk it through", do: "agent", ctx: "this step" },
    ] },
    "start.watching": { name: "Watching", what: "the questions you keep", actions: [
      { id: "moved", icon: "difference", label: "What moved since this morning?", do: "answer", line: "Two answers moved: Contoso's terms - the cap is now the only open term - and the office move, where facilities answered twice. The invoices and the Fabrikam sign-off read as they did at 08:12; the Q4 renewals question is paused." },
      { id: "refresh-all", icon: "refresh", label: "Refresh them all now", do: "toast", toast: "Four questions refreshed, each counted against your allowance. The Q4 renewals question stays paused." },
    ] },
    "start.watch": { name: "Watching", what: "a watched question", actions: [
      { id: "changed", icon: "difference", label: "What changed?", do: "answer", line: "One new message from Contoso on 25 August moves the answer: the cap is now the only open term." },
      { id: "refresh", icon: "refresh", label: "Refresh now", do: "toast", toast: "Refreshed. It counted against your allowance." },
    ] },
    "start.day": { name: "Your day", what: "calendar and tasks in time order", actions: [
      { id: "plan", icon: "event_note", label: "Plan the gaps", do: "agent", ctx: "the gaps in today" },
      { id: "focus", icon: "do_not_disturb_on", label: "Protect a focus block", do: "toast", toast: "13:45-14:30 blocked as focus." },
      { id: "move", icon: "event_repeat", label: "Move a task to tomorrow", do: "toast", toast: "“Approve invoice 08/2026” moved to tomorrow 09:00." },
    ] },
    "start.waiting": { name: "Waiting on you", what: "people you owe an answer", actions: [
      { id: "draft", icon: "edit_note", label: "Draft a reply", do: "agent", ctx: "a reply" },
      { id: "want", icon: "help", label: "What do they want?", do: "answer" },
      { id: "when", icon: "schedule_send", label: "Tell them when I will answer", do: "toast", toast: "A short holding reply is in the composer." },
      { id: "person", icon: "person", label: "Open in People", do: "go", go: "people" },
    ] },

    "mail.folders": { name: "Folders", what: "your mailboxes", actions: [
      { id: "unread", icon: "summarize", label: "Summarize unread here", do: "answer", line: "Seven unread in the Inbox: two need a reply, five can wait." },
      { id: "clean", icon: "cleaning_services", label: "Clean up what needs nothing", do: "toast", toast: "Fourteen newsletters archived." },
      { id: "rule", icon: "rule", label: "Make a rule from this", do: "agent", ctx: "a rule for this folder" },
    ] },
    "mail.row": { name: "Message", what: "a message in the list", actions: [
      { id: "summarize", icon: "summarize", label: "Summarize", do: "answer" },
      { id: "draft", icon: "edit_note", label: "Draft a reply", do: "agent", ctx: "a reply" },
      { id: "translate", icon: "translate", label: "Translate", do: "answer" },
      { id: "task", icon: "add_task", label: "Make it a task", do: "toast", toast: "Task added, due tomorrow." },
      { id: "remind", icon: "notifications", label: "Remind me", do: "toast", toast: "Reminder set for tomorrow 09:00." },
    ] },
    "mail.thread": { name: "Thread", what: "the open conversation", actions: [
      { id: "summarize", icon: "summarize", label: "Summarize the thread", do: "answer" },
      { id: "draft", icon: "edit_note", label: "Draft a reply", do: "draft" },
      { id: "dates", icon: "event", label: "Pull out dates and tasks", do: "answer", line: "Two dates: decision due 28 August, signature by 15 September. One task: send the counter-proposal." },
      { id: "translate", icon: "translate", label: "Translate", do: "answer" },
      { id: "ask", icon: "forum", label: "Ask about it", do: "agent" },
    ] },
    "mail.message": { name: "Message", what: "one message in the thread", actions: [
      { id: "summarize", icon: "summarize", label: "Summarize it", do: "answer" },
      { id: "explain", icon: "help", label: "Explain the terms", do: "answer", line: "CPI indexation ties the price to the consumer price index; a cap limits how far it can rise in a year." },
      { id: "translate", icon: "translate", label: "Translate", do: "answer" },
      { id: "reply", icon: "reply", label: "Reply to this one", do: "draft" },
    ] },

    "cal.event": { name: "Event", what: "a meeting or block", actions: [
      { id: "prep", icon: "auto_awesome", label: "Prepare me", do: "answer", line: "Piotr wants a go or no-go on phase three. Fabrikam still has no Q4 date from you. The updated plan moves the pilot by a week." },
      { id: "about", icon: "forum", label: "What is it about?", do: "answer", line: "From the thread “Re: rollout schedule”: two open questions and one changed attachment." },
      { id: "agenda", icon: "list_alt", label: "Draft an agenda", do: "agent", ctx: "an agenda" },
      { id: "move", icon: "event_repeat", label: "Find another time", do: "agent", ctx: "another time" },
      { id: "decline", icon: "event_busy", label: "Decline with a note", do: "toast", toast: "Declined with a note to the organiser." },
    ] },
    "cal.grid": { name: "Calendar", what: "the week", actions: [
      { id: "slot", icon: "manage_search", label: "Find a slot for…", do: "agent", ctx: "a slot this week" },
      { id: "focus", icon: "do_not_disturb_on", label: "Plan my focus time", do: "answer", line: "Three focus blocks fit: today 13:45, tomorrow 09:00 and Friday 14:00." },
      { id: "week", icon: "summarize", label: "Summarize the week", do: "answer", line: "Nine meetings, four of them about the rollout. Thursday is the heaviest day." },
    ] },

    "tasks.row": { name: "Task", what: "one task", actions: [
      { id: "steps", icon: "checklist", label: "Break it into steps", do: "answer", line: "1. Read Tomasz's two dates. 2. Check them against Piotr's plan. 3. Confirm 14 October to Fabrikam." },
      { id: "time", icon: "event_available", label: "Find time for it", do: "toast", toast: "Booked 16:30-16:45 today." },
      { id: "draft", icon: "edit_note", label: "Draft what it needs", do: "agent", ctx: "what this task needs" },
      { id: "why", icon: "help", label: "Why is it due?", do: "answer", line: "Tomasz asked for a date by 2 September; phase three cannot start without it." },
    ] },
    "tasks.list": { name: "Tasks", what: "your task list", actions: [
      { id: "prioritize", icon: "low_priority", label: "Put them in order", do: "answer", line: "Fabrikam first (it blocks phase three), then the invoice approval, then the audit list." },
      { id: "slipped", icon: "history", label: "What slipped?", do: "answer", line: "The Fabrikam confirmation moved twice." },
    ] },

    "people.row": { name: "Person", what: "a contact", actions: [
      { id: "history", icon: "history", label: "Summarize our history", do: "answer" },
      { id: "waits", icon: "hourglass_top", label: "What do they wait for?", do: "answer" },
      { id: "draft", icon: "edit_note", label: "Write to them", do: "agent", ctx: "a message" },
      { id: "next", icon: "event", label: "When do we meet next?", do: "answer", line: "Tomorrow at 10:00, the call about the 2027 terms." },
    ] },
    "people.page": { name: "Person's page", what: "everything you share with them", actions: [
      { id: "history", icon: "history", label: "Summarize our history", do: "answer" },
      { id: "files", icon: "folder_open", label: "What did we exchange?", do: "answer", line: "Four documents: the 2027 terms, two addendum drafts and the signed NDA." },
      { id: "draft", icon: "edit_note", label: "Write to them", do: "agent", ctx: "a message" },
    ] },

    "agent.message": { name: "Answer", what: "an answer in the conversation", actions: [
      { id: "shorter", icon: "compress", label: "Shorter", do: "answer", line: "In one line: answer Contoso with a 5% cap before tomorrow." },
      { id: "sources", icon: "fact_check", label: "Show the sources", do: "answer", line: "From six messages in “Contract addendum - signatures” and the attachment terms_2027.pdf." },
      { id: "translate", icon: "translate", label: "Translate", do: "answer" },
      { id: "task", icon: "add_task", label: "Make it a task", do: "toast", toast: "Task added from the answer." },
    ] },
  },

  // Inside the catch-up, by the kind of thing pressed.
  itemActions: {
    stat: [
      { id: "summarize", icon: "summarize", label: "Summarize", do: "answer" },
      { id: "more", icon: "unfold_more", label: "Break it down", do: "answer" },
      { id: "show", icon: "open_in_new", label: "Show these", do: "go" },
    ],
    thread: [
      { id: "summarize", icon: "summarize", label: "Summarize", do: "answer" },
      { id: "more", icon: "unfold_more", label: "Give me more", do: "answer" },
      { id: "translate", icon: "translate", label: "Translate", do: "answer" },
      { id: "draft", icon: "edit_note", label: "Draft a reply", do: "agent", ctx: "a reply" },
      { id: "open", icon: "open_in_new", label: "Open the thread", do: "go" },
    ],
    settled: [
      { id: "summarize", icon: "summarize", label: "How did it settle?", do: "answer" },
      { id: "open", icon: "open_in_new", label: "Open the thread", do: "go" },
    ],
    risk: [
      { id: "summarize", icon: "help", label: "Why is this a risk?", do: "answer" },
      { id: "more", icon: "verified_user", label: "What should I check?", do: "answer" },
      { id: "safe", icon: "check", label: "Mark as safe", do: "toast", toast: "Marked as safe. It will not be raised again." },
      { id: "open", icon: "open_in_new", label: "Open the message", do: "go" },
    ],
    person: [
      { id: "more", icon: "person_search", label: "Context", do: "answer" },
      { id: "summarize", icon: "summarize", label: "Summarize their mail", do: "answer" },
      { id: "draft", icon: "edit_note", label: "Write to them", do: "agent", ctx: "a message" },
      { id: "open", icon: "person", label: "Open in People", do: "go" },
    ],
    topic: [
      { id: "summarize", icon: "summarize", label: "Summarize", do: "answer" },
      { id: "decided", icon: "gavel", label: "What was decided?", do: "answer" },
      { id: "show", icon: "open_in_new", label: "Show the threads", do: "go" },
    ],
  },
  // Selected text, wherever Fathom reads a selection: the catch-up and Smart.
  selection: [
    { id: "explain", icon: "help", label: "Explain", do: "answer", line: "In plain words: " },
    { id: "summarize", icon: "summarize", label: "Summarize", do: "answer", line: "In short: " },
    { id: "translate", icon: "translate", label: "Translate", do: "answer", line: "Po polsku: " },
    { id: "more", icon: "unfold_more", label: "Give me more", do: "answer", line: "More on this: " },
    { id: "ask", icon: "forum", label: "Ask the agent", do: "agent" },
    { id: "copy", icon: "content_copy", label: "Copy", do: "toast", toast: "Copied." },
  ],
  // What an `answer` says when nothing more specific is written for it.
  fallback: {
    summarize: "Fathom reads the messages behind “{label}” and writes three lines about them.",
    more: "Fathom reads further into “{label}”: who said what, and what is still open.",
    translate: "Fathom translates “{label}” into the language you read in.",
    why: "Raised by a rule: a question asked of you and not answered for two days.",
    want: "They wait for an answer to the question they asked last.",
    history: "Fathom reads your shared threads and writes when you last spoke, about what, and what is open.",
    waits: "Fathom lists what this person asked you that has no answer yet.",
    decided: "Fathom reads the threads on this topic and lists what was agreed.",
    default: "Fathom answers from the mail behind “{label}”.",
  },
};
