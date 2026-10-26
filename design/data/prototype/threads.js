// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The inbox, newest first. `meta` is the sender's address, when the newest message arrived, and how long the thread is;
// `state` is what the thread has settled so far, and `body` the newest message as plain or Markdown paragraphs.
MailFathomDesign.data.threads = [
  {
    id: "contoso",
    from: "Anna Kowalska",
    org: "Contoso",
    time: "09:14",
    subject: "Contract addendum - signatures",
    ai: "Needs a decision by Friday · SLA change",
    meta: "Anna Kowalska <a.kowalska@contoso.example> · today 09:14 · thread: 6 messages",
    state: [
      { label: "Agreed", value: "SLA 2 h · price +8% from 2027" },
      { label: "Open question", value: "Upper cap on CPI indexation" },
      { label: "Commitment", value: "Decision on our side by 28.08" },
      {
        label: "Version difference",
        value: "The revised addendum of 25.08 no longer carries the 5% indexation cap that the first draft of 18.08 still had.",
      },
    ],
    body: [
      {
        md: `Hello, here is **addendum no. 3** in the version for signature. Compared with the 2021 master agreement, the service level and the fee change.

## What changes

| Parameter | Today | After addendum |
| --- | --- | --- |
| Response time (critical) | 4 h | **2 h** |
| Annual fee | €236,000 | €254,880 |
| Availability report | quarterly | monthly |
| CPI indexation cap | none | none |

> § 3. The rules for indexing the fee to CPI remain unchanged. The parties *do not set* an upper cap on indexation.

### To do on your side
1. Approve the addendum text by **28 August**
2. Name the signatory (first name, surname, position)
3. Return a scan or sign electronically in \`eSign\`

After 28 August we send the document for signature as it stands. Full text: [SLA addendum.pdf](https://contoso.example/addendum-3).`,
        hl: true,
      },
    ],
    attachments: [
      { type: "PDF", name: "SLA addendum.pdf", size: "248 kB" },
      { type: "PDF", name: "Master agreement.pdf", size: "1.2 MB" },
    ],
  },
  {
    id: "finanse",
    from: "Finance",
    org: "",
    time: "08:02",
    subject: "Invoice 08/2026",
    ai: "Payment due: 29.08",
    meta: "invoices@company.example · today 08:02 · thread: 1 message",
    state: [
      { label: "Agreed", value: "€1,452 net" },
      { label: "Deadline", value: "Payment by 29.08" },
      { label: "Commitment", value: "No action on our side" },
    ],
    body: [
      { text: "Attached is invoice 08/2026 for August. Amount due: €1,452.00 net, €1,786.00 gross." },
      {
        text: "Line items: application hosting for August and 250 GB of backups. Bank details unchanged; please quote the invoice number in the transfer reference.",
      },
      { text: "Payment due 29.08.2026. The invoice does not require a signature." },
    ],
    attachments: [{ type: "PDF", name: "INV_08_2026.pdf", size: "96 kB" }],
  },
  {
    id: "piotr",
    from: "Piotr Zieliński",
    org: "",
    time: "yest.",
    subject: "Re: rollout schedule",
    ai: "Confirmed phase two",
    meta: "p.zielinski@company.example · yesterday 16:41 · thread: 5 messages",
    state: [
      { label: "Agreed", value: "Phase two confirmed" },
      { label: "Open question", value: "UAT test dates" },
      { label: "Commitment", value: "Piotr will prepare the test plan" },
      {
        label: "Version difference",
        value: "The corrected schedule of 21.08 closes phase two at 41 of 58 tasks instead of the earlier “all tasks done”.",
      },
    ],
    body: [
      {
        text: "Confirming that phase two is closed - the Exchange integration is ready for acceptance, we closed 41 of 58 tasks.",
      },
      {
        text: "Two tasks remain blocked on the Graph API side, but they do not affect acceptance. No critical bugs open.",
      },
      {
        text: "The UAT window is still to be agreed. I suggest 15-19 September so we finish before the October training. I rate the schedule risk as medium.",
      },
    ],
    attachments: [],
  },
  {
    id: "marta",
    from: "Marta Nowak",
    org: "Finance",
    time: "yest.",
    subject: "CPI calculation 2027",
    ai: "Commitment: reply by 27.08",
    meta: "m.nowak@company.example · yesterday 11:20 · thread: 4 messages",
    state: [
      { label: "Agreed", value: "CPI forecast 4.1%" },
      { label: "Open question", value: "Do we propose a 5% cap" },
      { label: "Commitment", value: "Reply by 27.08" },
    ],
    body: [
      {
        md: `# CPI calculation 2027

I ran the new terms through the budget. Base: a flat fee of **€238,000** net per year.

| Scenario | CPI | 8% increase | Indexation | Total |
| --- | ---: | ---: | ---: | ---: |
| Base | 4.1% | €19,040 | €10,542 | **€267,582** |
| Pessimistic | 9.0% | €19,040 | €22,179 | €279,219 |
| With a 5% cap | 5.0% | €19,040 | €12,852 | €267,892 |

Year on year that is **+€29,582** in the base scenario.

## Recommendation
- negotiate an upper indexation cap of **5% per year**
- in the worst case that saves about €11.6k
- ~~dropping the monthly report~~ - Contoso will not budge on that

Formula used in the sheet:

\`\`\`
total = flat_fee * 1.08 * (1 + min(CPI; cap))
\`\`\`

Answer needed by **27 August**.`,
      },
    ],
    attachments: [{ type: "XLSX", name: "CPI_2027.xlsx", size: "34 kB" }],
  },
  {
    id: "jacek",
    from: "Jacek Wrona",
    org: "",
    time: "24.08",
    subject: "Legal opinion - indexation cap",
    ai: "",
    meta: "j.wrona@lawoffice.example · 24.08.2026 · thread: 2 messages",
    state: [
      { label: "Agreed", value: "A cap is contractually permissible" },
      { label: "Open question", value: "None" },
      { label: "Commitment", value: "None" },
    ],
    body: [
      {
        md: `## Legal opinion - indexation cap

Introducing an upper cap on indexation is **permissible** under freedom of contract and requires no change to the other provisions of the master agreement.

### Elements of the clause
1. reference index (CPI, statistics office, year on year)
2. the moment the index is read
3. the maximum increase per year

> Indexation of the fee may not exceed **5% per year**, regardless of the CPI figure published by the statistics office.

I rate the litigation risk as *low*. An uncapped clause with CPI above 8% could, however, materially shift the contractual balance.`,
      },
      {
        text: "The clause should name the reference index, the moment it is read and the maximum increase per year. Proposed wording: indexation may not exceed 5% per calendar year, regardless of the CPI figure published by the statistics office.",
      },
      {
        text: "I rate the litigation risk as low. An uncapped clause with CPI above 8% could, however, materially shift the contractual balance.",
      },
    ],
    attachments: [],
  },
  {
    id: "anna2",
    from: "Anna Kowalska",
    org: "Contoso",
    time: "08:47",
    subject: "Re: proposed terms for 2027",
    ai: "Waiting on our reply",
    meta: "a.kowalska@contoso.example · today 08:47 · thread: 3 messages",
    state: [
      { label: "Agreed", value: "Three options on the table" },
      { label: "Open question", value: "Do we accept option B" },
      { label: "Commitment", value: "Reply by 28.08" },
    ],
    body: [
      {
        text: "A reminder about our proposal of 26 August. We prepared three options for 2027 - unchanged, recommended, and extended to weekends.",
      },
      {
        text: "The recommended option shortens the response time to two hours, adds a dedicated technical account manager for two days a month and a monthly availability report. Annual price: €257,040 net.",
      },
      {
        text: "If you need a further calculation or a comparison with the 2026 settlement, I can prepare it by the end of the week. The proposal is binding on us until 28 August.",
      },
    ],
    attachments: [{ type: "XLSX", name: "Calculation_2027.xlsx", size: "42 kB" }],
  },
  {
    id: "hr1",
    from: "HR Department",
    org: "",
    time: "07:55",
    subject: "Satisfaction survey - due 05.09",
    ai: "Due: 05.09",
    meta: "hr@company.example · today 07:55 · thread: 1 message",
    state: [
      { label: "Agreed", value: "Anonymous survey, 7 minutes" },
      { label: "Open question", value: "None" },
      { label: "Commitment", value: "Complete by 05.09" },
    ],
    body: [
      {
        text: "Please fill in the annual satisfaction survey. It takes about seven minutes and is fully anonymous - an external platform collects the answers and we only see aggregate results.",
      },
      {
        text: "The survey covers six areas plus two open questions. Last year 78% of the team completed it; that is what the training budget and flexible start hours came from.",
      },
      { text: "Deadline: 5 September. We will discuss the results at the September all-hands." },
    ],
    attachments: [],
  },
  {
    id: "north1",
    from: "Robert Lis",
    org: "Northwind",
    time: "07:12",
    subject: "Quote for the reporting module",
    ai: "Needs a decision · quote valid 30 days",
    meta: "r.lis@northwind.example · today 07:12 · thread: 2 messages",
    state: [
      { label: "Agreed", value: "€24,000 net, 6-week rollout" },
      { label: "Open question", value: "Scope of custom reports" },
      { label: "Commitment", value: "Decision by 30.09" },
    ],
    body: [
      {
        md: `# Quote - reporting module

## Scope
- 12 standard reports (sales, SLA, licence usage)
- custom report builder
- export to \`XLSX\` and \`PDF\`
- scheduled delivery by e-mail

## Pricing

| Item | Net amount |
| --- | ---: |
| Annual licence | €14,000 |
| Implementation | €8,400 |
| Training (2 days) | €1,600 |
| **Total** | **€24,000** |

Maintenance: *€1,200 per month* from the second month after acceptance.

## Schedule
1. Data source configuration - 2 weeks
2. Report build - 2 weeks
3. Testing and training - 2 weeks

---

Quote valid until **30 September**. Details: [northwind.example/reports](https://northwind.example/reports).`,
      },
    ],
    attachments: [{ type: "PDF", name: "Quote_reporting.pdf", size: "310 kB" }],
  },
  {
    id: "bank1",
    from: "Business Bank",
    org: "",
    time: "06:58",
    subject: "Transfer confirmation €18,400",
    ai: "",
    meta: "noreply@bank.example · today 06:58 · thread: 1 message",
    state: [
      { label: "Agreed", value: "Transfer completed 01.09" },
      { label: "Open question", value: "None" },
      { label: "Commitment", value: "None" },
    ],
    body: [
      {
        text: "A transfer of €18,400.00 has been completed. Beneficiary: Contoso Ltd, account DE61 1090 1014 0000 0712 19.",
      },
      {
        text: "Reference: addendum - August 2026 settlement. Booking date: 1 September 2026, 06:58. Operation reference: TRN-8841-220916.",
      },
      { text: "This message only confirms the operation and needs no reply." },
    ],
    attachments: [{ type: "PDF", name: "Confirmation.pdf", size: "58 kB" }],
  },
  // A report that came back as mail. `report.kind` is `read`, a read receipt (RFC 8098, `report-type=disposition-notification`),
  // or `delivery`, a delivery status notification (RFC 3461, `report-type=delivery-status`), which lists each recipient it
  // covers with the outcome it reports. `of` is the thread of the account's own message it reports on, null where it
  // names none this account holds, and `at` is when it arrived. Its row and its head are drawn from `report` rather than
  // from `subject`, which the reader's mail app or a mail server wrote.
  {
    id: "mdnMarta",
    from: "Marta Nowak",
    org: "",
    time: "25.08",
    subject: "Read: CPI calculation 2027",
    ai: "",
    meta: "m.nowak@company.example · 25.08.2026 17:40 · thread: 1 message",
    report: { kind: "read", reader: "Marta Nowak", at: "25.08 17:40", of: "marta" },
    state: [],
    body: [
      { text: "This is a read receipt for the message you sent to m.nowak@company.example, subject \"CPI calculation 2027\"." },
      { text: "It says only that the message was displayed on the recipient's screen. It does not say that the message was read or understood." },
      { text: "Reporting-UA: mail.company.example · Final-Recipient: rfc822; m.nowak@company.example · Disposition: manual-action/MDN-sent-manually; displayed" },
    ],
  },
  {
    id: "mdnStray",
    from: "Karol Wiśniewski",
    org: "Harbor Works",
    time: "08:15",
    subject: "Read: Supplier visit 03.09",
    ai: "",
    meta: "k.wisniewski@harborworks.example · today 08:15 · thread: 1 message",
    report: { kind: "read", reader: "Karol Wiśniewski", at: "08:15", of: null },
    state: [],
    body: [
      { text: "This is a read receipt for the message you sent to k.wisniewski@harborworks.example, subject \"Supplier visit 03.09\"." },
      { text: "It says only that the message was displayed on the recipient's screen. It does not say that the message was read or understood." },
      { text: "Reporting-UA: harborworks.example · Final-Recipient: rfc822; k.wisniewski@harborworks.example · Disposition: manual-action/MDN-sent-manually; displayed" },
    ],
  },
  {
    id: "dsnNorth",
    from: "Mail Delivery System",
    org: "",
    time: "25.08",
    subject: "Undelivered Mail Returned to Sender",
    ai: "",
    meta: "MAILER-DAEMON@mail.company.example · 25.08.2026 10:15 · thread: 1 message",
    report: { kind: "delivery", at: "25.08 10:15", of: "north1", to: [{ addr: "r.lis@northwind.example", outcome: "failed" }] },
    state: [],
    body: [
      { text: "This is the mail system at host mail.company.example. Your message could not be delivered to one or more of its recipients." },
      { text: "<r.lis@northwind.example>: host mx2.northwind.example said: 550 5.1.1 The mailbox does not exist." },
      { text: "Reporting-MTA: dns; mail.company.example · Final-Recipient: rfc822; r.lis@northwind.example · Action: failed · Status: 5.1.1" },
    ],
  },
  {
    id: "dsnFabrikam",
    from: "Mail Delivery System",
    org: "",
    time: "26.08",
    subject: "Delivery Status Notification (Delay)",
    ai: "",
    meta: "MAILER-DAEMON@mail.company.example · 26.08.2026 14:40 · thread: 1 message",
    report: { kind: "delivery", at: "26.08 14:40", of: "fabrikam1", to: [{ addr: "t.bak@fabrikam.example", outcome: "delayed" }] },
    state: [],
    body: [
      { text: "This is the mail system at host mail.company.example. Your message has not been delivered yet to one of its recipients. It will be tried again until 28.08 14:10. You do not have to send it again." },
      { text: "<t.bak@fabrikam.example>: connection to relay.fabrikam.example timed out." },
      { text: "Reporting-MTA: dns; mail.company.example · Final-Recipient: rfc822; t.bak@fabrikam.example · Action: delayed · Status: 4.4.1" },
    ],
  },
  {
    id: "dsnContoso",
    from: "Mail Delivery System",
    org: "",
    time: "26.08",
    subject: "Delivery Status Notification (Success)",
    ai: "",
    meta: "postmaster@mail.lawoffice.example · 26.08.2026 16:22 · thread: 1 message",
    report: { kind: "delivery", at: "26.08 16:22", of: "contoso", to: [{ addr: "j.wrona@lawoffice.example", outcome: "delivered" }] },
    state: [],
    body: [
      { text: "This is the mail system at host mail.lawoffice.example. Your message was delivered to the recipient's mailbox, as you asked to be told." },
      { text: "Reporting-MTA: dns; mail.lawoffice.example · Final-Recipient: rfc822; j.wrona@lawoffice.example · Action: delivered · Status: 2.0.0" },
    ],
  },
  {
    id: "dsnAudit",
    from: "Mail Delivery System",
    org: "",
    time: "24.08",
    subject: "Delivery Status Notification (Failure)",
    ai: "",
    meta: "MAILER-DAEMON@mx.company.example · 24.08.2026 09:13 · thread: 1 message",
    report: {
      kind: "delivery", at: "24.08 09:13", of: "audyt1",
      to: [{ addr: "audit@company.example", outcome: "delivered" }, { addr: "o.krawiec@company.example", outcome: "failed" }],
    },
    state: [],
    body: [
      { text: "This is the mail system at host mx.company.example. Your message could not be delivered to one of its recipients. It was delivered to the others." },
      { text: "<o.krawiec@company.example>: 552 5.2.2 The mailbox is full." },
      { text: "<audit@company.example>: delivered to the mailbox." },
      { text: "Reporting-MTA: dns; mx.company.example · Final-Recipient: rfc822; o.krawiec@company.example · Action: failed · Status: 5.2.2 · Final-Recipient: rfc822; audit@company.example · Action: delivered · Status: 2.0.0" },
    ],
  },
  {
    id: "dsnStray",
    from: "Mail Delivery System",
    org: "",
    time: "06:41",
    subject: "Undelivered Mail Returned to Sender",
    ai: "",
    meta: "MAILER-DAEMON@mail.company.example · today 06:41 · thread: 1 message",
    report: { kind: "delivery", at: "06:41", of: null, to: [{ addr: "orders@old-supplier.example", outcome: "failed" }] },
    state: [],
    body: [
      { text: "This is the mail system at host mail.company.example. Your message could not be delivered to one or more of its recipients." },
      { text: "<orders@old-supplier.example>: host old-supplier.example said: 550 5.1.2 The domain does not accept mail." },
      { text: "Reporting-MTA: dns; mail.company.example · Final-Recipient: rfc822; orders@old-supplier.example · Action: failed · Status: 5.1.2" },
    ],
  },
  {
    id: "fabrikam1",
    from: "Tomasz Bąk",
    org: "Fabrikam",
    time: "yest.",
    subject: "Q4 delivery dates",
    ai: "Date change · November",
    meta: "t.bak@fabrikam.example · yesterday 17:05 · thread: 7 messages",
    state: [
      { label: "Agreed", value: "Delivery moved to 12.11" },
      { label: "Open question", value: "Do we accept the delay" },
      { label: "Commitment", value: "Confirm by 02.09" },
    ],
    body: [
      {
        text: "Because of a line stoppage at the component manufacturer, delivery of order ORD-2026-0914 moves from 28 October to 12 November, window 08:00-14:00.",
      },
      {
        text: "Scope unchanged: four pallets, 1,240 kg. Component production finishes on 2 September, picking and quality control by 5 November.",
      },
      {
        text: "Please confirm the new window by 2 September. Without a reply we will book the fallback date of 19 November. All other commercial terms stay as they are.",
      },
    ],
    attachments: [],
  },
  {
    id: "audyt1",
    from: "Internal Audit",
    org: "",
    time: "yest.",
    subject: "Request for the contract register",
    ai: "Needs a decision · data by 03.09",
    meta: "audit@company.example · yesterday 16:12 · thread: 3 messages",
    state: [
      { label: "Agreed", value: "Scope: IT contracts 2024-2026" },
      { label: "Open question", value: "Who prepares the list" },
      { label: "Commitment", value: "List due 03.09" },
    ],
    body: [
      {
        md: `## Request for the IT contract register 2024-2026

As part of the periodic review, please send a **complete list of IT contracts** including addenda.

### Columns required in the list
- [ ] counterparty and tax ID
- [ ] signature date and term
- [ ] annual value, net
- [ ] notice period
- [ ] personal data processing (yes/no)
- [x] file format agreed: \`XLSX\`

| Stage | Date |
| --- | --- |
| List submitted | 03.09 |
| Audit follow-up questions | 08.09 |
| Review closed | 15.09 |

> The request needs no formal reply - just send the file to the audit address, copying the board office.`,
      },
    ],
    attachments: [],
  },
  {
    id: "prawnik2",
    from: "Wrona Law Office",
    org: "",
    time: "yest.",
    subject: "Draft NDA - Northwind",
    ai: "For signature",
    meta: "j.wrona@lawoffice.example · yesterday 15:40 · thread: 2 messages",
    state: [
      { label: "Agreed", value: "3-year NDA, €50k penalty" },
      { label: "Open question", value: "Will the other side accept the lower penalty" },
      { label: "Commitment", value: "Signature by 05.09" },
    ],
    body: [
      {
        text: "Here is the second draft of the NDA with Northwind. I shortened the term from five to three years after the end of the engagement.",
      },
      {
        text: "The key change is in clause 5: I propose lowering the contractual penalty from €100k to €50k per breach. The carve-outs in clause 7 stay unchanged.",
      },
      {
        text: "The document is ready for electronic signature until 5 September. If the other side rejects the reduction, I recommend the variant with a €150k aggregate cap.",
      },
    ],
    attachments: [{ type: "DOCX", name: "NDA_Northwind.docx", size: "72 kB" }],
  },
  {
    id: "travel1",
    acc: "acc2",
    from: "Travel Desk",
    org: "",
    time: "yest.",
    subject: "Tickets Kraków-Berlin 12.09",
    ai: "Check-in from: 11.09",
    meta: "bookings@travel.example · yesterday 14:02 · thread: 1 message",
    state: [
      { label: "Agreed", value: "Flight 12.09, 07:40, PNR 7QX4MB" },
      { label: "Open question", value: "None" },
      { label: "Commitment", value: "Online check-in 11.09" },
    ],
    body: [
      {
        md: `## Booking confirmed - \`7QX4MB\`

| | Departure | Arrival |
| --- | --- | --- |
| City | Kraków (KRK) | Berlin (BER) |
| Date | 12.09 | 12.09 |
| Time | **07:40** | 09:05 |

Direct flight, flying time *1 h 25 min*. Seat **14C**, aisle.

### Flex fare
- 23 kg checked baggage
- free changes up to 24 h before departure
- 90% refund of the ticket price

---

Online check-in: **11.09 from 07:40**, closes 2 h before departure - [travel.example/checkin/7QX4MB](https://click.travelmail.example/r/7QX4MB).`,
      },
    ],
    attachments: [{ type: "PDF", name: "Tickets.pdf", size: "120 kB" }],
  },
  {
    id: "marketing1",
    from: "Marketing",
    org: "",
    time: "yest.",
    subject: "Trade show materials 24.09",
    ai: "Deadline: 10.09",
    meta: "marketing@company.example · yesterday 12:30 · thread: 5 messages",
    state: [
      { label: "Agreed", value: "Stand C18, 18 m²" },
      { label: "Open question", value: "Who runs the demo" },
      { label: "Commitment", value: "Materials by 10.09" },
    ],
    body: [
      {
        md: `# Trade show Poznań, 24-26.09

Stand **C18**, 18 m². Coordination: Piotr Zieliński, print: Nord agency.

## Material status
- [x] stand build design
- [x] roll-up 1 - print-ready file
- [ ] roll-up 2 - no English version
- [ ] product leaflet \`PL\` / \`EN\`
- [ ] **demo presenter** - not assigned

| Item | Owner | Due |
| --- | --- | --- |
| Print-ready files | Marketing | 10.09, 12:00 |
| Print pickup | Nord | 18.09 |
| Stand assembly | Piotr Z. | 23.09 |

> After 10 September the printer adds a **35% surcharge** for rush turnaround.`,
      },
    ],
    attachments: [{ type: "PPTX", name: "Tradeshow_2026.pptx", size: "4.1 MB" }],
  },
  {
    id: "klient3",
    from: "Ewa Sikora",
    org: "Adventure Works",
    time: "yest.",
    subject: "Question about weekend SLA",
    ai: "Needs a reply",
    meta: "e.sikora@adventure.example · yesterday 10:15 · thread: 4 messages",
    state: [
      { label: "Agreed", value: "Today: support Mon-Fri 8:00-18:00" },
      { label: "Open question", value: "Do we offer Saturdays, and at what price" },
      { label: "Commitment", value: "Reply by 01.09" },
    ],
    body: [
      {
        md: `Coming back to **weekend support** - from October we are starting Saturday cover for three of our clients.

## What we need

| Scope | Today | Target |
| --- | --- | --- |
| Days | Mon-Fri | Mon-**Sat** |
| Hours | 8:00-18:00 | 8:00-16:00 (Sat) |
| Response (critical) | 2 h | 4 h |
| Channel | portal | portal + on-call phone |

1. Does the current contract cover Saturdays?
2. If not - what is the price range for the add-on?
3. How soon could we start?

> We take the budget decision next week, so even a ballpark figure helps.`,
      },
    ],
    attachments: [],
  },
  {
    id: "devops1",
    from: "Nordwind CI",
    org: "",
    time: "06:20",
    subject: "Nightly report - build #2418",
    ai: "Build green · 2 warnings",
    meta: "ci@nordwind.example · today 06:20 · thread: 1 message",
    state: [
      { label: "Agreed", value: "Build green, 2 warnings" },
      { label: "Open question", value: "Do we ship to production" },
      { label: "Commitment", value: "Decision by 09:00" },
    ],
    body: [
      {
        md: `# Nightly report - build **#2418**

Branch \`release/2.4\` · commit \`8f31c0d\` · duration 14 min 22 s

| Stage | Status | Time |
| --- | --- | ---: |
| Lint | OK | 41 s |
| Unit tests | OK (1,284) | 3 min 08 s |
| E2E tests | OK (96) | 8 min 51 s |
| Security scan | **2 warnings** | 1 min 42 s |

## Warnings
1. \`libxml2 2.9.14\` - *medium* severity, fixed in 2.9.15
2. Unused API key in \`config/staging.yml\`

> Neither warning blocks the release. Team recommendation: **ship it**, take the libxml fix into the next patch.

### Pre-deploy checklist
- [x] database migrations tested on a copy
- [x] rollback plan written up
- [ ] maintenance window confirmed with the client
- [ ] note for support

Deploy command:

\`\`\`
mf deploy --env prod --build 2418 --window 22:00-23:00
\`\`\`

---

Full log: [ci.nordwind.example/2418](https://ci.nordwind.example/2418). Decision needed by **09:00**.`,
      },
    ],
    attachments: [{ type: "PDF", name: "Report_2418.pdf", size: "88 kB" }],
  },
  {
    id: "rekrutacja1",
    from: "Anna Bielska",
    org: "HR",
    time: "yest.",
    subject: "Hiring summary - Frontend",
    ai: "3 candidates awaiting a decision",
    meta: "a.bielska@company.example · yesterday 13:50 · thread: 6 messages",
    state: [
      { label: "Agreed", value: "Three finalists" },
      { label: "Open question", value: "Who goes to the final round" },
      { label: "Commitment", value: "Decision by 04.09" },
    ],
    body: [
      {
        md: `## Hiring: Frontend Engineer

**Three candidates** are left after the second round. Scores below, on a scale of 1 to 5, with weights in brackets.

| Candidate | Code (40%) | Systems (30%) | Team (30%) | Total |
| --- | ---: | ---: | ---: | ---: |
| K. Adamiak | 5 | 4 | 4 | **4.4** |
| M. Rutkowski | 4 | 5 | 3 | 4.0 |
| J. Sowa | 4 | 3 | 5 | 4.0 |

### Interviewer notes
- **K. Adamiak** - best practical task, expectations 12% above the band
- **M. Rutkowski** - strong architecture, weaker in pairing; available immediately
- **J. Sowa** - excellent team fit, *needs support* on testing

> Band for this role: €3,600-4,300 per month, contract. Going above the top of the band needs board approval.

### Next steps
1. Pick two people for the final round by **4 September**
2. Final interviews 8-10 September
3. Offer by 15 September

- [x] references checked (all three)
- [ ] decision on the band for K. Adamiak
- [ ] book a room for the finals

Notes and recordings: [hiring/frontend-2026](https://company.example/hiring/frontend-2026).`,
      },
    ],
    attachments: [{ type: "XLSX", name: "Candidate_scores.xlsx", size: "28 kB" }],
  },
  {
    id: "handover1",
    acc: "acc2",
    from: "Olga Mazur",
    org: "",
    time: "21.08",
    subject: "Handover - supplier contracts",
    ai: "Two renewals to take over",
    meta: "o.mazur@harborworks.example · 21.08.2026 · thread: 1 message",
    state: [
      { label: "Agreed", value: "You take over the supplier contracts" },
      { label: "Open question", value: "Who signs the Halvard renewal" },
      { label: "Commitment", value: "Renewals by 30.09" },
    ],
    body: [
      {
        text: "As agreed, the supplier contracts are yours from Monday. The folder with all of them is shared with you.",
      },
      {
        text: "Two renewals are due by 30 September: Halvard Logistics and the cleaning service. Halvard asked for a two-year term this time.",
      },
    ],
    attachments: [],
  },
  {
    id: "selfNote1",
    acc: "acc2",
    from: "Karolina Kowalska",
    org: "",
    time: "20.08",
    subject: "Note to self - passport renewal",
    ai: "A reminder you sent yourself",
    meta: "k.kowalska@nordwind.example · 20.08.2026 · thread: 1 message",
    state: [{ label: "Commitment", value: "Book the passport appointment by 15.09" }],
    body: [
      {
        text: "Book the passport appointment before 15 September - the old one runs out in November. Bring the photos from the drawer.",
      },
    ],
    attachments: [],
  },

  // Spam. `spam` is the verdict a message arrived with: `judge` is who reached it (`model`, `server`, or `person`),
  // `kind` its spam subcategory where one was named, and `request` and `party` what a phishing message asks for and whom
  // a phishing or business email compromise message is written to look like, where the record names one. Both are the
  // closed values the deployment publishes rather than words the message wrote, so a warning never repeats its claims. `box: "spam"` puts a thread in an account's Spam folder; every
  // other spam thread sits in the folder the rest of the list stands for. `acc` names the account where it is not the
  // work one, which is what decides whether *Spam* and *Not spam* move it.
  // Spam carries no AI reading (`ai`, `state`), because nothing that reads mail for the person reads spam.
  {
    id: "spamPayroll",
    from: "Payroll Team",
    org: "",
    time: "yest.",
    subject: "Action required: confirm your salary account today",
    ai: "",
    meta: "payroll@nordwlnd-hr.example · yesterday 17:42 · thread: 1 message",
    state: [],
    spam: { judge: "model", kind: "phishing", request: "Credentials", party: "Colleague" },
    body: [
      { text: "Our records show that your salary account could not be verified for the September payroll run." },
      { text: "To avoid a delay in your payment, sign in through the secure payroll portal below and confirm your bank details before 18:00 today." },
      { md: "[Confirm my salary account](https://nordwlnd-hr.example/verify)" },
    ],
  },
  {
    id: "spamChairs",
    acc: "acc2",
    from: "Brightline Deals",
    org: "",
    time: "yest.",
    subject: "Up to 60% off office chairs this weekend",
    ai: "",
    meta: "offers@brightline-deals.example · yesterday 11:05 · thread: 1 message",
    state: [],
    spam: { judge: "model", kind: "marketing" },
    body: [
      { text: "Our biggest seating sale of the year starts now. Ergonomic chairs from 89 EUR, while stock lasts." },
      { text: "Free delivery on orders over 150 EUR. Unsubscribe at any time from the link in our footer." },
    ],
  },
  {
    id: "spamDesk",
    box: "spam",
    from: "Nordwind IT Service Desk",
    org: "",
    time: "08:31",
    subject: "Your mailbox password expires today",
    ai: "",
    meta: "it-desk@nordwind-support.example · today 08:31 · thread: 1 message",
    state: [],
    unread: true,
    spam: { judge: "model", kind: "phishing", request: "Credentials", party: "OnlineService" },
    body: [
      { text: "Your mailbox password expires today. Mail sent to you after 23:59 will be held until it is renewed." },
      { text: "Keep your current password by signing in below. It takes less than a minute." },
      { md: "[Keep my current password](https://nordwind-support.example/renew)" },
    ],
  },
  {
    id: "spamSupplier",
    box: "spam",
    from: "Halvard Logistics",
    org: "",
    time: "07:58",
    subject: "Updated bank details for our invoices",
    ai: "",
    meta: "accounts@halvard-logistlcs.example · today 07:58 · thread: 1 message",
    state: [],
    unread: true,
    spam: { judge: "model", kind: "bec", party: "Supplier" },
    body: [
      { text: "Hello, following an audit we have moved our banking. Please pay the open invoices and every future one into the new account below." },
      { text: "Please do not call our office about this, the line is being moved as well. Reply here once the change is made." },
    ],
  },
  {
    id: "spamParcel",
    box: "spam",
    from: "Parcel Notice",
    org: "",
    time: "yest.",
    subject: "Your parcel could not be delivered - open the attached form",
    ai: "",
    meta: "notice@parcel-track.example · yesterday 19:20 · thread: 1 message",
    state: [],
    spam: { judge: "person", kind: "malware" },
    body: [
      { text: "We tried to deliver your parcel today but nobody was home." },
      { text: "Open the attached delivery form, fill it in, and we deliver again tomorrow." },
    ],
    attachments: [{ type: "ZIP", name: "delivery_form.zip", size: "212 kB" }],
  },
  {
    id: "spamEstate",
    acc: "acc2",
    box: "spam",
    from: "Estate of R. Marlowe",
    org: "",
    time: "yest.",
    subject: "Unclaimed funds held in your name",
    ai: "",
    meta: "executor@marlowe-estate.example · yesterday 14:12 · thread: 1 message",
    state: [],
    spam: { judge: "model", kind: "scam" },
    body: [
      { text: "I write as executor of an estate that names a beneficiary with your surname. The funds have been unclaimed for three years." },
      { text: "Call me on the number below to arrange the release. A small handling fee applies." },
    ],
  },
  {
    id: "spamThreat",
    box: "spam",
    from: "No Reply",
    org: "",
    time: "24.08",
    subject: "Pay within 48 hours",
    ai: "",
    meta: "noreply@mail-relay.example · 24.08 22:40 · thread: 1 message",
    state: [],
    spam: { judge: "model", kind: "extortion" },
    body: [
      { text: "I have access to your accounts. Unless the payment below reaches me within 48 hours, everyone in your address book hears from me." },
    ],
  },
  {
    id: "spamSignals",
    acc: "acc2",
    box: "spam",
    from: "Weekly Signals",
    org: "",
    time: "24.08",
    subject: "Your picks for this week are in",
    ai: "",
    meta: "picks@weekly-signals.example · 24.08 06:00 · thread: 1 message",
    state: [],
    spam: { judge: "server" },
    body: [
      { text: "Five assets our model expects to double before the month is out. Members get the full list first." },
    ],
  },
];

// The messages before the newest one, for the threads whose history is written out. `html` names the original in
// mail-bodies.js; every other thread gets a short history derived from its state.
MailFathomDesign.data.earlierMessages = {
  /* Thread for testing entry "from the middle": the list opens message 3 of 5. */
  piotr: [
    {
      from: "Piotr Zieliński",
      when: "24.08, 09:40",
      paras: [
        {
          text: "Hello, here is the status after the first week of phase two. The Exchange integration works on the test environment; calendar mappings are what is left.",
        },
        { text: "26 of 58 tasks closed. I see no risk to the date so far." },
      ],
    },
    {
      from: "Karolina Kowalska",
      when: "25.08, 11:15",
      paras: [
        {
          text: "Thank you. Let me know when the calendar mappings are done - that decides when we can invite users to test.",
        },
      ],
    },
    {
      from: "Piotr Zieliński",
      when: "26.08, 14:05",
      paras: [
        {
          text: "Calendar mappings are done. Along the way we found a time-zone problem with recurring meetings - the fix ships on Wednesday.",
        },
        {
          text: "Two tasks are blocked on the Graph API side. Unblocking depends on Microsoft; we have a priority B ticket open.",
        },
        {
          text: "I will prepare the UAT test plan after the phase is accepted. I need a list of five test users from you.",
        },
      ],
      attachments: [{ type: "PDF", name: "Status_etap2.pdf", size: "118 kB" }],
      html: "rolloutSchedule",
    },
    {
      from: "Karolina Kowalska",
      when: "27.08, 08:20",
      paras: [
        {
          text: "You will have the list of test users on Thursday. Does the time-zone problem need a separate acceptance?",
        },
      ],
    },
  ],
  contoso: [
    {
      from: "Anna Kowalska",
      when: "21.08, 10:02",
      paras: [
        {
          text: "Hello, here is an outline of the changes to the 2021 master agreement. The key areas are service level and fees - details in the formal version we will prepare this week.",
        },
      ],
    },
    {
      from: "Karolina Kowalska",
      when: "21.08, 15:38",
      paras: [
        {
          text: "Thank you. Please send a version with changes marked against the current contract - otherwise it is hard for us to judge the cost impact.",
        },
      ],
    },
    {
      from: "Anna Kowalska",
      when: "22.08, 09:11",
      paras: [
        {
          text: "Attached is the comparison version. The changes affect clause 4 (response time) and the price schedule.",
        },
      ],
      attachments: [{ type: "PDF", name: "Addendum_comparison.pdf", size: "204 kB" }],
      html: "contosoComparison",
    },
    {
      from: "Marta Nowak",
      when: "25.08, 12:24",
      paras: [
        {
          text: "I calculated the impact of the 8% increase at a 4.1% CPI forecast - about €19k more per year. Worth asking for an upper indexation cap.",
        },
      ],
      attachments: [{ type: "XLSX", name: "CPI_2027.xlsx", size: "34 kB" }],
      html: "cpiCalculation",
    },
    {
      from: "Karolina Kowalska",
      when: "26.08, 08:30",
      paras: [
        {
          text: "Are the indexation rules negotiable? We would accept the 2 h SLA on condition that an upper CPI cap is introduced.",
        },
      ],
    },
  ],
};

// Which original in mail-bodies.js each thread's newest message was.
MailFathomDesign.data.threadMailBodies = {
  contoso: "contosoComparison",
  finanse: "hostingInvoice",
  piotr: "rolloutSchedule",
  marta: "cpiCalculation",
  jacek: "legalOpinion",
  anna2: "proposedTerms",
  hr1: "satisfactionSurvey",
  north1: "reportingQuote",
  bank1: "transferConfirmation",
  fabrikam1: "deliveryDateChange",
  audyt1: "auditRequest",
  prawnik2: "ndaDraft",
  travel1: "travelTickets",
  marketing1: "tradeShowMaterials",
  klient3: "weekendSla",
};

MailFathomDesign.data.threadStateSources = { contoso: [5, 4, 5, 3] };

/* Reply-To and CC appear only where they make sense. */
MailFathomDesign.data.replyTo = { contoso: "Frida Iversen <frida.iversen@contoso.example>", audyt1: "Jacek Wrona <j.wrona@company.example>" };
MailFathomDesign.data.cc = { contoso: ["Marta Nowak"], marta: ["Jacek Wrona"], klient3: ["Anna Kowalska"] };

/* The title a model writes for each conversation, from the whole of it rather than from its first subject line, so the
   list can be read by conversation. Spam never gets one, and a conversation missing here — `jacek`, `bank1` — has not
   been given one yet and is listed under its subject. */
MailFathomDesign.data.conversationTitles = {
  contoso: "Contoso SLA addendum: sign by 28.08, CPI cap still open",
  finanse: "August hosting invoice, €1,452 net due 29.08",
  piotr: "Phase two rollout: UAT test users due Thursday",
  marta: "2027 CPI indexation: our reply due 27.08",
  anna2: "Contoso 2027 terms waiting on our counter-proposal",
  hr1: "Staff satisfaction survey closes 05.09",
  north1: "Northwind reporting module quote, valid 30 days",
  fabrikam1: "Fabrikam Q4 deliveries slip to November",
  audyt1: "Audit asks for the contract register by 03.09",
  prawnik2: "Northwind NDA draft ready to sign",
  travel1: "Kraków-Berlin trip on 12.09, check-in from 11.09",
  marketing1: "Trade show materials for 24.09, files by 10.09",
  klient3: "Adventure asks about weekend SLA cover",
  devops1: "Nightly build #2418 green with 2 warnings",
  rekrutacja1: "Frontend hiring: 3 candidates awaiting our decision",
};
