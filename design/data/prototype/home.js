// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Start: the reader's day - what is next, what needs them, one step each, and how the day runs - gathered from
// Mail, Calendar, Tasks and People. Start stores none of it; every item opens where it lives. The day is
// Thursday 27 August, and it is 13:40.
MailFathomDesign.data.home = {
  // Derived by rules rather than written by a model: `rule` names the rule that raised the item, and is what the
  // reader is shown as the reason it is here.
  todayItems: [
    { id: "n1", title: "CPI cap counter-proposal", due: "tomorrow", why: "Contoso has been waiting on our reply for 2 days.", rule: "waiting on your reply", src: "Contract addendum - signatures", tid: "contoso", urgent: true },
    { id: "n2", title: "Fabrikam date confirmation", due: "02.09", why: "Fabrikam asked you to confirm a date and nobody has answered.", rule: "asked of you", src: "Q4 delivery dates", tid: "fabrikam1", urgent: false },
    { id: "n3", title: "IT contract list for audit", due: "03.09", why: "Audit set 03.09 as the date for the register with addenda.", rule: "date in the thread", src: "Contract register request", tid: "audyt1", urgent: false },
  ],
  // The counters above the list are counts, not prose: nothing reads the mail to produce them.
  todayCounts: "14 new messages · 2 meetings today · 1 deadline tomorrow",
  // The written briefing is off until the reader turns it on.
  briefingOffer: "A short written briefing each morning, from today's new mail. It reads that mail with the model and counts against your allowance.",
  agenda: [
    { when: "today", t: "Rollout status with Piotr", kind: "meeting" },
    { when: "tomorrow", t: "Decision: Contoso addendum", kind: "deadline" },
    { when: "tomorrow", t: "Call with Anna Kowalska", kind: "meeting" },
    { when: "29.08", t: "Payment of invoice 08/2026", kind: "deadline" },
  ],
  briefing: "The Contoso addendum decision is due tomorrow - the CPI cap counter-proposal still has not gone out. Fabrikam is waiting for a date confirmation and audit for the IT contract list. The rest of the mail can wait.",
  briefingStats: [
    { n: "14", label: "new messages" },
    { n: "3", label: "need a decision" },
    { n: "2", label: "meetings today" },
    { n: "1", label: "deadline tomorrow" },
  ],
  digest: [
    { n: 5, label: "Clients", note: "Contoso, Fabrikam, Adventure Works", tid: "contoso" },
    { n: 4, label: "Internal", note: "controlling, delivery, HR", tid: "marta" },
    { n: 3, label: "Invoices and payments", note: "two to approve", tid: "faktura1" },
    { n: 2, label: "FYI", note: "nothing needs a reply", tid: "hr1" },
  ],
};

// The counts under the greeting. Each one opens the space it counts, filtered to what it counted.
MailFathomDesign.data.home.dayCounts = [
  { key: "mail", icon: "mail", n: 14, label: "new messages" },
  { key: "cal", icon: "event", n: 2, label: "meetings today" },
  { key: "tasks", icon: "task_alt", n: 2, label: "due today" },
  { key: "waiting", icon: "hourglass_top", n: 3, label: "waiting on you" },
];

// The next meeting. What stands by default is what rules read off the calendar and the thread behind it; the
// prepared briefing is the model's, written only when the reader asks for it.
MailFathomDesign.data.home.nextUp = {
  title: "Rollout status with Piotr",
  when: "15:00-15:30", inText: "in 1 h 20 min", where: "Teams",
  people: [{ initials: "PZ", name: "Piotr Zieliński" }, { initials: "TB", name: "Tomasz Bąk" }],
  tid: "piotr", thread: "Re: rollout schedule",
  facts: ["2 open questions in the thread", "Fabrikam's date is still unconfirmed", "1 attachment changed since Monday"],
  prep: [
    "Phase two is confirmed for 12 November; Piotr wants a go or no-go on phase three.",
    "Fabrikam still has no date from us - Tomasz asked yesterday, and phase three depends on it.",
    "The updated plan (rollout_v3.xlsx) moves the pilot by one week; nothing else changed.",
  ],
  prepSources: "from 6 messages and 1 attachment",
};

// The day as it runs, from the calendar and the tasks. `state` is past, now, next or later; a `proposed` item is a
// step below (`step` is its index) that has not been taken yet; taking it books the slot.
MailFathomDesign.data.home.dayPlan = [
  { time: "10:00", end: "", title: "Reply to Marta - calculation", kind: "task", state: "past", note: "done 10:12", go: "tasks" },
  { time: "13:40", end: "", title: "Now", kind: "now", state: "now" },
  { time: "13:45", end: "14:30", title: "Focus: Contoso counter-proposal", kind: "focus", state: "later", note: "proposed below", proposed: true, step: 0, go: "cal" },
  { time: "15:00", end: "15:30", title: "Rollout status with Piotr", kind: "meeting", state: "next", note: "Teams · 2 people", go: "cal" },
  { time: "16:30", end: "", title: "Confirm the Q4 date to Fabrikam", kind: "task", state: "later", note: "due today", go: "tasks" },
  { time: "17:00", end: "", title: "Approve invoice 08/2026", kind: "task", state: "later", note: "due today", go: "tasks" },
];

// Who is waiting on the reader, longest first. Rule-derived: a question asked of them and not yet answered.
MailFathomDesign.data.home.waiting = [
  { initials: "AK", name: "Anna Kowalska", org: "Contoso", what: "the counter-proposal", since: "2 days", tid: "contoso" },
  { initials: "TB", name: "Tomasz Bąk", org: "Fabrikam", what: "a Q4 date", since: "yesterday", tid: "fabrikam1" },
  { initials: "MN", name: "Marta Nowak", org: "Finance", what: "a look at the calculation", since: "today", tid: "marta" },
];

// One step each, drawn with the Agent's proposal blocks and taken with one press that can be undone.
// One step each, as the Agent proposes them. `alts` are the alternatives a field offers here; a step without them
// takes the Agent's own (`fieldAlternatives`).
MailFathomDesign.data.home.steps = [
  { type: "event", phase: "pending", title: "Focus: Contoso counter-proposal", when: "today 13:45-14:30", duration: "45 min", attendees: "just you", from: "Contract addendum - signatures", done: "Blocked in the calendar",
    alts: {
      When: [
        { v: "today 15:30-16:15", why: "right after the rollout call" },
        { v: "today 16:30-17:15", why: "the rest of the afternoon is free" },
        { v: "tomorrow 09:00-09:45", why: "first free block tomorrow" },
      ],
      Duration: [
        { v: "30 min", why: "enough to mark up the addendum" },
        { v: "1 h 30 min", why: "room to write the reply too" },
      ],
    } },
  { type: "task", phase: "pending", title: "Confirm the Q4 date to Fabrikam", due: "today 16:30", from: "Q4 delivery dates", reminder: "16:00", done: "Task added",
    alts: {
      Due: [
        { v: "today 15:00", why: "before the call with Piotr" },
        { v: "tomorrow 10:00", why: "Fabrikam answers in the morning" },
      ],
      Reminder: [
        { v: "14:30", why: "half an hour ahead" },
        { v: "none", why: "" },
      ],
    } },
  { type: "draft", phase: "pending", meta: "opens in the composer", to: "Anna Kowalska", subject: "Re: Proposed terms 2027",
    paras: ["Thank you for the proposal. We can accept the 2-hour response time, but not indexation without a cap.", "We propose CPI indexation capped at 5% a year, and would like to settle it before 28 August."] },
];

// Since the reader last looked, read off the mail by rules and counted rather than written: what arrived, sorted into
// what needs them (the items below), what they can read when they like, and what needs nothing and can go in one press.
MailFathomDesign.data.home.sinceLast = {
  at: "08:12",
  arrived: 14,
  groups: [
    { key: "needs", icon: "person_alert", n: 3, label: "need you", note: "below, in Needs you today" },
    { key: "fyi", icon: "visibility", n: 5, label: "to read when you like", note: "Marta, Piotr, HR" },
    { key: "leave", icon: "inventory_2", n: 6, label: "need nothing", note: "newsletters and notifications" },
  ],
};

// Heads up: what could hurt if it went unnoticed, raised by rules from the messages themselves. `level` is `danger`
// (money, credentials, a sender pretending to be someone) or `warn` (a deadline about to pass). Each one says why it
// was raised and what to check, and goes away when it is marked safe or dealt with.
MailFathomDesign.data.home.headsUp = [
  { id: "h-bank", level: "danger", icon: "gpp_maybe", title: "An invoice asks to pay a new bank account",
    why: "It comes from fabrikam-billing.co, not fabrikam.com, and the account number differs from the last three invoices.",
    check: "Confirm by phone with Tomasz Bąk before anything is paid.", tid: "fabrikam1", rule: "payment details changed" },
  { id: "h-contoso", level: "warn", icon: "schedule", title: "The Contoso decision is due tomorrow",
    why: "Anna Kowalska asked twice; without an answer the 2027 terms renew at Contoso's proposal.",
    check: "The draft below proposes a 5% cap.", tid: "contoso", rule: "deadline without a reply" },
];
