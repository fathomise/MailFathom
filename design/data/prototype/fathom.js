// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Fathom: the button in the bottom-right corner of Start and Mail, and what it offers.
//
// `menu` is what pressing it unfolds: the space's own actions first (Mail's come from the screen: *New message* and,
// with a thread open, the reply), then the Agent.
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
