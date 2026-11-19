// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Fathom: the button in the bottom-right corner of Start and Mail, and what it offers.
//
// `menu` is what pressing it unfolds: the space's own actions first (Mail's come from the screen: *New message* and,
// with a thread open, the reply), then the Agent.
// `itemActions` is what a press on anything inside the catch-up offers, by the item's kind, and `selection` what
// selected text offers anywhere Fathom reads it.
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
  },
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
  // Selected text, wherever Fathom reads a selection: the catch-up.
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
