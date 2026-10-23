// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The Today screen: what needs doing, what is on the calendar, the morning briefing, and the inbox digest.
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
