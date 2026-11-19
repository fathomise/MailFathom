// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

/* A task is a commitment. `dir` says which way it runs: "owe" is something I owe, "wait" something I am waiting for
   from somebody else. `on` is the due day (a waiting task's is the day to follow up), `plan` the time it is planned
   into, separate from the due day. `anchor` is the context it lives in — a thread, a person, or an event — and
   `person` the person the commitment is with, which is what the person page reads. Today is Thursday 27 August. */
MailFathomDesign.data.tasks = [
  { id: "t1", t: "Send the 5% CPI cap counter-proposal", dir: "owe", on: "08-27", when: "today", est: "45 min", mins: 45, plan: { on: "08-27", time: "11:00", h: 11 },
    anchor: { kind: "thread", label: "Contract addendum - signatures", tid: "contoso" }, person: "anna", origin: "mail" },
  { id: "t2", t: "Reply to Marta about the calculation", dir: "owe", on: "08-27", when: "today", est: "20 min", mins: 20, plan: { on: "08-27", time: "15:00", h: 15 },
    anchor: { kind: "thread", label: "CPI calculation 2027", tid: "marta" }, person: "marta", origin: "mail" },
  { id: "t10", t: "Book the meeting room for the Fabrikam review", dir: "owe", on: "08-27", when: "today", est: "10 min", mins: 10,
    anchor: null, person: null },
  { id: "t8", t: "Review the addendum before the call", dir: "owe", on: "08-28", when: "tomorrow", est: "30 min", mins: 30, plan: { on: "08-28", time: "09:00", h: 9 },
    anchor: { kind: "event", label: "Call with Anna Kowalska", day: "28", time: "13:00" }, person: "anna" },
  { id: "t5", t: "Answer Adventure Works on weekend SLA", dir: "owe", on: "09-01", when: "01.09", est: "30 min", mins: 30,
    anchor: { kind: "thread", label: "Question about weekend SLA", tid: "klient3" }, person: "ewa", origin: "mail" },
  { id: "t3", t: "Confirm the Fabrikam delivery date", dir: "owe", on: "09-02", when: "02.09", est: "15 min", mins: 15,
    anchor: { kind: "thread", label: "Q4 delivery dates", tid: "fabrikam1" }, person: "tomasz", origin: "mail" },
  { id: "t4", t: "Prepare the IT contract list for audit", dir: "owe", on: "09-03", when: "03.09", est: "2 hrs", mins: 120,
    anchor: { kind: "thread", label: "Request for the contract register", tid: "audyt1" }, person: null, origin: "mail" },
  { id: "t6", t: "Close the 2027 budget after the Contoso decision", dir: "owe", on: "09-10", when: "10.09", est: "1 hr", mins: 60,
    anchor: { kind: "person", label: "Marta Nowak", pid: "marta" }, person: "marta" },
  { id: "t7", t: "Sign the Northwind NDA", dir: "owe", on: "09-12", when: "12.09", est: "10 min", mins: 10,
    anchor: { kind: "thread", label: "Draft NDA - Northwind", tid: "prawnik2" }, person: "jacek" },
  { id: "t9", t: "Draft the onboarding checklist for Q4 hires", dir: "owe", on: null, when: "", est: "1 hr", mins: 60,
    anchor: null, person: null },

  { id: "w2", t: "Delivery confirmation from Tomasz Bąk", dir: "wait", on: "08-27", when: "today", since: "sent Mon 24.08",
    anchor: { kind: "thread", label: "Q4 delivery dates", tid: "fabrikam1" }, person: "tomasz" },
  { id: "w1", t: "Signed addendum from Anna Kowalska", dir: "wait", on: "08-28", when: "tomorrow", since: "sent Tue 25.08",
    anchor: { kind: "thread", label: "Contract addendum - signatures", tid: "contoso" }, person: "anna" },
  { id: "w4", t: "Weekend SLA terms from Ewa Sikora", dir: "wait", on: "09-02", when: "02.09", since: "sent Wed 26.08",
    anchor: { kind: "thread", label: "Question about weekend SLA", tid: "klient3" }, person: "ewa" },
  { id: "w3", t: "Opinion on the indexation cap from Jacek Wrona", dir: "wait", on: "08-27", when: "today", since: "sent Mon 24.08", replied: "Jacek replied at 09:12",
    anchor: { kind: "thread", label: "Legal opinion - indexation cap", tid: "jacek" }, person: "jacek" },
];

/* What the AI proposed and nobody has answered: from mail enrichment and from the Agent. A proposal is never a task
   until it is accepted. */
MailFathomDesign.data.proposedTasks = [
  { id: "p1", t: "Send Piotr the rollout risks before the status call", on: "08-27", when: "today", est: "20 min", mins: 20, from: "From mail",
    anchor: { kind: "thread", label: "Re: rollout schedule", tid: "piotr" }, person: "piotr" },
  { id: "p3", t: "Ask Marta for the 2026 baseline figures", on: "08-31", when: "31.08", est: "10 min", mins: 10, from: "From the Agent",
    anchor: { kind: "person", label: "Marta Nowak", pid: "marta" }, person: "marta" },
  { id: "p2", t: "Book the auditor's visit", on: null, when: "", est: "15 min", mins: 15, from: "From mail",
    anchor: { kind: "thread", label: "Request for the contract register", tid: "audyt1" }, person: null },
];

// The title a task made from an open thread starts with, by thread.
MailFathomDesign.data.taskFromThread = {
  contoso: "Send Anna the signed addendum",
  marta: "Check Marta's CPI figures against the budget",
  piotr: "Send Piotr the rollout risks",
  fabrikam1: "Confirm the Fabrikam delivery date",
};

// The days a sent message can wait for its reply until, in the composer's sending options.
MailFathomDesign.data.composeFollowUp = [
  { on: "08-28", when: "tomorrow", label: "Remind me tomorrow", hint: "Fri 28.08" },
  { on: "08-31", when: "31.08", label: "Remind me on Monday", hint: "Mon 31.08" },
  { on: "09-03", when: "03.09", label: "Remind me in a week", hint: "Thu 03.09" },
];

// How full today is, after the count of open tasks.
MailFathomDesign.data.taskCapacity = { meetings: "2 meetings", advice: "It fits if you start with the CPI counter-proposal." };
