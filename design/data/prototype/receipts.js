// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What came back for a message the account sent, keyed by thread: the reports for the thread's newest own message.
// `asked` is what that message requested - `dsn`, a delivery status notification (RFC 3461), and `mdn`, a read receipt
// (RFC 8098). Per recipient, `dsn` is the outcome a delivery status notification reported - `delivered`, `failed` or
// `delayed`, null where none came back - with `dsnAt` when it arrived, `by` the server that reported it and `status` the
// status code a failure or a delay gives; `read` is when a read receipt reported the message displayed, null where none
// came back. `internal` marks a colleague's account of the same organization on this deployment, whose delivery and
// opening MailFathom records itself, with no request and no report. Every name, address and server is made up.
MailFathomDesign.data.receipts = {
  contoso: {
    asked: { dsn: true, mdn: true },
    to: [
      { name: "Anna Kowalska", addr: "a.kowalska@contoso.example", dsn: "delivered", dsnAt: "26.08 16:21", by: "mx1.contoso.example", read: "26.08 16:48" },
      { name: "Marek Dąbrowski", addr: "m.dabrowski@contoso.example", dsn: "delivered", dsnAt: "26.08 16:21", by: "mx1.contoso.example", read: "27.08 08:05" },
      { name: "Jacek Wrona", addr: "j.wrona@lawoffice.example", dsn: "delivered", dsnAt: "26.08 16:22", by: "mail.lawoffice.example", read: null },
    ],
  },
  piotr: {
    asked: { dsn: false, mdn: false },
    to: [
      { name: "Piotr Zieliński", addr: "p.zielinski@company.example", internal: true, dsn: "delivered", dsnAt: "26.08 11:02", read: "26.08 11:40" },
    ],
  },
  marta: {
    asked: { dsn: false, mdn: true },
    to: [
      { name: "Marta Nowak", addr: "m.nowak@company.example", dsn: null, read: "25.08 17:40" },
    ],
  },
  north1: {
    asked: { dsn: true, mdn: false },
    to: [
      { name: "Robert Lis", addr: "r.lis@northwind.example", dsn: "failed", dsnAt: "25.08 10:15", by: "mx2.northwind.example",
        status: "5.1.1" },
    ],
  },
  audyt1: {
    asked: { dsn: true, mdn: true },
    to: [
      { name: "Internal Audit", addr: "audit@company.example", dsn: "delivered", dsnAt: "24.08 09:12", by: "mx.company.example", read: "24.08 09:30" },
      { name: "Ola Krawiec", addr: "o.krawiec@company.example", dsn: "failed", dsnAt: "24.08 09:13", by: "mx.company.example",
        status: "5.2.2" },
    ],
  },
  fabrikam1: {
    asked: { dsn: true, mdn: false },
    to: [
      { name: "Tomasz Bąk", addr: "t.bak@fabrikam.example", dsn: "delayed", dsnAt: "26.08 14:40", by: "relay.fabrikam.example",
        status: "4.4.1" },
    ],
  },
  klient3: {
    asked: { dsn: true, mdn: true },
    to: [
      { name: "Ewa Sikora", addr: "e.sikora@adventure.example", dsn: null, read: null },
    ],
  },
};

// A received message that asks for a read receipt (`Disposition-Notification-To`), keyed by thread: the request on the
// thread's newest received message. `to` is the address the receipt would go to, and `sentAt` when this account sent
// one, absent while it has not. A spam message's request is never offered, so `spamPayroll` draws nothing.
MailFathomDesign.data.receiptRequests = {
  jacek: { to: "j.wrona@lawoffice.example" },
  anna2: { to: "a.kowalska@contoso.example", sentAt: "29.08 08:52" },
  marketing1: { to: "campaign-tracking@mailer.example" },
  spamPayroll: { to: "payroll@nordwlnd-hr.example" },
};
