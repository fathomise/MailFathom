// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

MailFathomDesign.data.notifications = [
  { id: "n1", kind: "mail", unread: true, mins: 3, title: "Anna Kowalska", body: "Re: Contract addendum - CPI cap counter-proposal", src: "Mail · Contoso", go: { screen: "mail", thread: "contoso" } },
  { id: "n3", kind: "mail", unread: true, mins: 12, merged: true, count: 8, account: "Fabrikam", title: "8 new messages", body: "More than five arrived unread, so they are gathered here.", src: "Mail · Fabrikam", go: { screen: "mail" } },
  { id: "n2", kind: "cal", unread: true, mins: 26, title: "Meeting in 30 minutes", body: "Contoso addendum review · Nordwind room and Teams.", src: "Calendar", go: { screen: "cal" } },
  { id: "n4", kind: "task", unread: false, mins: 150, title: "Task: IT contract list", body: "Audit is waiting for the list with notice periods.", src: "Tasks", go: { screen: "tasks" } },
  { id: "n5", kind: "mail", unread: false, mins: 320, title: "Marek Wiśniewski", body: "Re: IT contract audit - two files attached", src: "Mail · Audit", go: { screen: "mail", thread: "audyt" } },
  { id: "n6", kind: "system", unread: false, mins: 700, title: "Sync finished", body: "3 accounts, 12 new threads, 1 account needs to sign in again.", src: "System", go: null },
  { id: "n7", kind: "cal", unread: false, mins: 1580, title: "Meeting invitation", body: "Nordwind - SLA negotiations, Tuesday 10:00.", src: "Calendar", go: { screen: "cal" } },
];

MailFathomDesign.data.incomingNotifications = [
  { kind: "mail", title: "Tomasz Zieliński", body: "Delivery 12.11 - confirmation needed", src: "Mail · Fabrikam", go: { screen: "mail", thread: "fabrikam" } },
  { kind: "cal", title: "Meeting in 10 minutes", body: "Contoso - call about the CPI cap.", src: "Calendar", go: { screen: "cal" } },
];

/* Demo refresh — one click plays a small, realistic inbox event: a mail lands, an existing
   thread gets a reply, and one message is withdrawn. Only the affected tiles animate; the
   list itself is never reloaded. */
MailFathomDesign.data.demoBatches = [
  {
    mail: { from: "Marta Wilk", org: "Nordwind", time: "now", subject: "Framework agreement - annex for signature",
      ai: "Awaiting our comments · deadline 15.09",
      meta: "Marta Wilk <m.wilk@nordwind.example> · today, now · thread: 1 message",
      state: [ { label: "Agreed", value: "Annex scope: support hours" }, { label: "Open question", value: "Penalties for exceeding response time" }, { label: "Deadline", value: "Comments by 15.09" } ],
      body: [ { text: "Attached is the annex to the framework agreement in the version after our call. The change concerns support hours (8-18 instead of 9-17) and the reporting procedure." },
        { text: "Please send comments by 15 September - after that date we send the document for signature." } ],
      attachments: [ { type: "PDF", name: "Annex_framework_agreement.pdf", size: "184 kB" } ] },
    edit: { id: "finanse", patch: { time: "now", subject: "Invoice 08/2026 - correction", ai: "Corrected amount €1,388 · new due date 05.09",
      meta: "invoices@company.example · today, now · thread: 2 messages" } },
    editNotif: { title: "Accounts payable", body: "Invoice 08/2026 - correction", thread: "finanse" },
    fallbackRemove: "jacek",
    removeNote: "The sender withdrew a message - the thread has left the list",
  },
  {
    mail: { from: "Tomasz Zieliński", org: "Fabrikam", time: "now", subject: "Delivery 12.11 - confirmation needed",
      ai: "Reply expected today · date confirmation",
      meta: "t.zielinski@fabrikam.example · today, now · thread: 1 message",
      state: [ { label: "Agreed", value: "Delivery window 12.11, morning" }, { label: "Open question", value: "Who receives on our side" }, { label: "Commitment", value: "Confirmation today" } ],
      body: [ { text: "We confirm the delivery on 12.11 in the morning window. We need the name of the person receiving and a contact number for the driver." },
        { text: "Without confirmation today the slot goes to the next order in the queue." } ],
      attachments: [] },
    edit: { id: "marta", patch: { time: "now", subject: "Re: CPI calculation 2027 - cap accepted", ai: "Finance accepts a 5% cap · ready for the annex",
      meta: "m.nowak@company.example · today, now · thread: 5 messages" } },
    editNotif: { title: "Marta Nowak", body: "Re: CPI calculation 2027 - cap accepted", thread: "marta" },
    fallbackRemove: "jacek",
    removeNote: "The sender withdrew a message - the thread has left the list",
  },
  {
    mail: { from: "Karolina Bąk", org: "Contoso", time: "now", subject: "Signatory for the addendum - details",
      ai: "Data complete · nothing to do on our side",
      meta: "k.bak@contoso.example · today, now · thread: 1 message",
      state: [ { label: "Agreed", value: "Signatory: Anna Kowalska, board member" }, { label: "Open question", value: "None" }, { label: "Commitment", value: "eSign link tomorrow" } ],
      body: [ { text: "Passing on the signatory details for addendum no. 3: Anna Kowalska, board member, acting under the entry in the register." },
        { text: "The eSign link goes out tomorrow morning - nothing more is needed from your side." } ],
      attachments: [] },
    edit: { id: "contoso", patch: { time: "now", subject: "Contract addendum - CPI cap agreed", ai: "Cap 5% accepted · ready for signature",
      meta: "Anna Kowalska <a.kowalska@contoso.example> · today, now · thread: 7 messages" } },
    editNotif: { title: "Anna Kowalska", body: "Contract addendum - CPI cap agreed", thread: "contoso" },
    fallbackRemove: "jacek",
    removeNote: "The sender withdrew a message - the thread has left the list",
  },
];
