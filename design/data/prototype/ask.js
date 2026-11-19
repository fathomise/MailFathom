// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What Start's *Ask or find* answers with: one plan per kind of question, each an answer, its evidence, and the
// messages it came from.
MailFathomDesign.data.plans = {
  contract: {
    confidence: "high confidence",
    gap: "1 gap: 2022 addendum missing",
    meta: "41 messages read",
    blocks: ["answer", "timeline", "facts"],
    parts: [
      { text: "The terms changed in three stages: the master agreement of April 2021", cite: "1" },
      { text: ", CPI indexation with no upper cap introduced in February 2023", cite: "3" },
      { text: ", the SLA cut from 4 h to 2 h in the November 2025 addendum", cite: "5" },
      { text: ". The 2027 proposal raises the price by 8% and keeps indexation uncapped", cite: "7" },
    ],
    timeline: [
      { date: "12.04.2021", title: "Master agreement", detail: "€1,200/mo · SLA 4 h", cite: "1" },
      { date: "03.02.2023", title: "CPI indexation", detail: "no upper cap", cite: "3" },
      { date: "18.11.2025", title: "SLA addendum", detail: "4 h → 2 h · +8%", cite: "5" },
      { date: "26.08.2026", title: "2027 proposal", detail: "decision due 28.08", cite: "7" },
    ],
    facts: { cols: ["Version", "Price / mo", "SLA", "Indexation"], rows: [
      [{ v: "Agreement 2021", cite: "1" }, { v: "€1,200", cite: "1" }, { v: "4 h", cite: "1" }, { v: "none" }],
      [{ v: "Addendum 2023", cite: "3" }, { v: "€1,261", cite: "1" }, { v: "4 h", cite: "1" }, { v: "CPI, uncapped", cite: "3" }],
      [{ v: "Addendum 2025", cite: "5" }, { v: "€1,452", cite: "5" }, { v: "2 h", cite: "5" }, { v: "CPI, uncapped", cite: "3" }],
      [{ v: "Proposal 2027", cite: "7" }, { v: "€1,568", cite: "7" }, { v: "2 h", cite: "5" }, { v: "CPI, uncapped", cite: "3" }],
    ] },
    evidence: [
      { n: "1", title: "Master agreement.pdf", srcType: "attachment", snippet: "“Monthly remuneration is €1,200 net…”", meta: "12.04.2021 · p. 3",
        pre: "§2.4 The scope of services covers maintenance of the production environment and support on business days.",
        quote: "§2.5 Monthly remuneration is €1,200 net. Response time for a critical incident is 4 hours.",
        post: "§2.6 The agreement is concluded for an indefinite term with three months' notice.",
        supports: "base price €1,200 · SLA 4 h", mail: "“Master agreement - version for signature”, 12.04.2021" },
      { n: "3", title: "Re: indexation from 2023", srcType: "mail", snippet: "“…indexed to CPI, with no upper cap.”", meta: "03.02.2023 · A. Kowalska",
        pre: "Hello, coming back to the indexation of rates for next year.",
        quote: "As agreed, remuneration will be indexed to the CPI published by the statistics office, with no upper cap.",
        post: "Please confirm and we will prepare the addendum for signature.",
        supports: "CPI indexation · no upper cap", mail: "“Re: indexation from 2023”, 03.02.2023" },
      { n: "5", title: "SLA addendum.pdf", srcType: "attachment", snippet: "“Response time is shortened to 2 hours…”", meta: "18.11.2025 · p. 2",
        pre: "§3.1 The parties confirm the scope of services under the master agreement of 12 April 2021.",
        quote: "§3.2 Response time for a critical incident is shortened from 4 hours to 2 hours on business days. In return for the higher service level, remuneration increases by 8%.",
        post: "§3.3 All other provisions, including CPI indexation, remain unchanged.",
        supports: "SLA 2 h · +8% price · indexation unchanged", mail: "“Contract addendum - signatures”, 18.11.2025" },
      { n: "7", title: "2027 proposal - Contoso", srcType: "mail", snippet: "“…an 8% rate increase from 1 January 2027.”", meta: "26.08.2026 · A. Kowalska",
        pre: "Here is our proposal of terms for 2027, together with the calculation.",
        quote: "We propose an 8% rate increase from 1 January 2027 while keeping the 2-hour response time. CPI indexation remains uncapped.",
        post: "Please decide by 28 August so the addendum can be signed before year end.",
        supports: "price €1,568 · SLA 2 h · CPI uncapped", mail: "“Proposed terms 2027”, 26.08.2026" },
    ],
  },
  files: {
    confidence: "high confidence",
    gap: "",
    meta: "6 files found",
    blocks: ["answer", "gallery"],
    parts: [
      { text: "The current policy runs to 31.12.2026 and arrived attached to the broker's email of 14.12.2025", cite: "2" },
      { text: ". Older versions sit in the archive-2019 account", cite: "4" },
    ],
    gallery: [
      { type: "PDF", name: "Policy_2026.pdf", meta: "14.12.2025 · 640 kB · current", cite: "2" },
      { type: "PDF", name: "Policy_2025.pdf", meta: "09.12.2024 · 612 kB", cite: "4" },
      { type: "DOCX", name: "Coverage_scope.docx", meta: "14.12.2025 · 88 kB", cite: "2" },
      { type: "PNG", name: "Signature_scan.png", meta: "16.12.2025 · 1.4 MB", cite: "2" },
    ],
    evidence: [
      { n: "2", title: "Policy_2026.pdf", srcType: "attachment", snippet: "“Period of insurance: 01.01.2026 - 31.12.2026”", meta: "14.12.2025 · p. 1",
        pre: "Policyholder: Northgate Ltd. Subject of insurance: professional indemnity.",
        quote: "Period of insurance: 01.01.2026 - 31.12.2026. Limit of indemnity: €2,000,000 per claim and in the aggregate.",
        post: "Premium payable in two instalments, the first due 15.01.2026.",
        supports: "valid to 31.12.2026 · €2m limit", mail: "“Policy for 2026 - for approval”, 14.12.2025" },
      { n: "4", title: "Policy_2025.pdf", srcType: "attachment", snippet: "“Period of insurance: 01.01.2025 - 31.12.2025”", meta: "09.12.2024 · archive-2019",
        pre: "Previous version of the document, archive-2019 account.",
        quote: "Period of insurance: 01.01.2025 - 31.12.2025. Limit of indemnity: €1,500,000.",
        post: "Kept for comparison of indemnity limits.",
        supports: "previous limit €1.5m", mail: "“Policy 2025”, 09.12.2024" },
    ],
  },
  people: {
    confidence: "moderate confidence",
    gap: "no email confirmation from the board",
    meta: "18 messages, 3 people",
    blocks: ["answer", "people"],
    parts: [
      { text: "The 2023 indexation budget was approved by Marta Nowak in Finance, after Jacek Wrona's legal opinion", cite: "6" },
      { text: ". Anna Kowalska was Contoso's counterpart throughout the correspondence", cite: "3" },
    ],
    people: [
      { initials: "MN", name: "Marta Nowak", role: "Finance · approved the 2023 budget", last: "yesterday", cite: "6" },
      { initials: "JW", name: "Jacek Wrona", role: "Lawyer · opinion on the indexation cap", last: "24.08", cite: "6" },
      { initials: "AK", name: "Anna Kowalska", role: "Contoso · account manager", last: "today", cite: "3" },
    ],
    evidence: [
      { n: "6", title: "Re: 2023 indexation budget", srcType: "mail", snippet: "“I approve the calculation, we go with CPI.”", meta: "07.02.2023 · M. Nowak",
        pre: "Thread: 2023 indexation budget, participants: Marta Nowak, Jacek Wrona, Chris Kasprowicz.",
        quote: "I approve the calculation, we go with CPI indexation. I am leaving the legal caveat about a cap to the next renegotiation.",
        post: "Please pass the decision on to Contoso.",
        supports: "budget approval · cap knowingly omitted", mail: "“Re: 2023 indexation budget”, 07.02.2023" },
      { n: "3", title: "Re: indexation from 2023", srcType: "mail", snippet: "“…indexed to CPI, with no upper cap.”", meta: "03.02.2023 · A. Kowalska",
        pre: "Hello, coming back to the indexation of rates for next year.",
        quote: "As agreed, remuneration will be indexed to the CPI published by the statistics office, with no upper cap.",
        post: "Please confirm and we will prepare the addendum for signature.",
        supports: "Anna Kowalska's role in the agreement", mail: "“Re: indexation from 2023”, 03.02.2023" },
    ],
  },
};

// The messages a result was built from: Start lists them under the answer, and they open as a filtered Mail list.
Object.assign(MailFathomDesign.data.plans.contract, {
  sources: ["contoso", "finanse", "marta", "jacek"],
});

Object.assign(MailFathomDesign.data.plans.files, {
  sources: ["north1", "finanse"],
});

Object.assign(MailFathomDesign.data.plans.people, {
  sources: ["marta", "jacek", "contoso"],
});

// The plans behind the watched questions. A plan names the widgets its answer is drawn with (`blocks`), and each is
// there because the question asks for it: what is open in a thread is a thread state, a list of invoices a fact table,
// a date that moved a timeline, an owner the people. A question the answer alone settles takes no widget at all.
Object.assign(MailFathomDesign.data.plans, {
  open: {
    confidence: "high confidence", gap: "", meta: "9 messages read", blocks: ["answer", "state"],
    parts: [
      { text: "Two things are still open: the counter-proposal on the CPI cap, which Anna Kowalska has asked for twice", cite: "1" },
      { text: ", and the signature page of the addendum, which legal has not returned", cite: "2" },
      { text: ". The 2-hour response time is already agreed", cite: "1" },
    ],
    state: [
      { kind: "Open question", text: "CPI cap: our counter-proposal has not gone out", cite: "1" },
      { kind: "Open question", text: "The addendum's signature page is still with legal", cite: "2" },
      { kind: "Agreed", text: "The 2-hour response time stays", cite: "1" },
      { kind: "Commitment", text: "You promised an answer before 28 August", cite: "1" },
    ],
    evidence: [
      { n: "1", title: "Re: proposed terms for 2027", srcType: "mail", snippet: "“…could you send the counter-proposal on the cap before the 28th?”", meta: "25.08.2026 · A. Kowalska",
        pre: "Thank you for confirming the 2-hour response time, we will keep it in the addendum.",
        quote: "Could you send the counter-proposal on the cap before the 28th? This is the second time I am asking, and legal on our side needs two days.",
        post: "Otherwise the 2027 terms renew as proposed.",
        supports: "cap still open · response time agreed · answer promised by 28.08", mail: "“Re: proposed terms for 2027”, 25.08.2026”" },
      { n: "2", title: "Legal opinion - indexation cap", srcType: "mail", snippet: "“…I will return the signature page once the cap is settled.”", meta: "24.08.2026 · J. Wrona",
        pre: "I have read the addendum and the proposed terms.",
        quote: "The cap should be written into the addendum itself. I will return the signature page once the cap is settled.",
        post: "A 5% cap is defensible against the last three years of CPI.",
        supports: "signature page held back until the cap is settled", mail: "“Legal opinion - indexation cap”, 24.08.2026”" },
    ],
    sources: ["contoso", "jacek"],
  },
  invoices: {
    confidence: "high confidence", gap: "1 invoice has new bank details", meta: "12 messages, 5 invoices", blocks: ["answer", "facts"],
    parts: [
      { text: "Two August invoices are unpaid: 08/2026 from Fabrikam, due 29.08", cite: "1" },
      { text: ", and the Adventure Works licence renewal, due 05.09", cite: "2" },
      { text: ". The other three were paid in the run of 21 August", cite: "3" },
    ],
    facts: { cols: ["Invoice", "From", "Amount", "Due", "Status"], rows: [
      [{ v: "08/2026", cite: "1" }, { v: "Fabrikam" }, { v: "€4,200" }, { v: "29.08" }, { v: "unpaid · new account", cite: "1" }],
      [{ v: "AW-2291", cite: "2" }, { v: "Adventure Works" }, { v: "€1,850" }, { v: "05.09" }, { v: "unpaid", cite: "2" }],
      [{ v: "07/2026" }, { v: "Adventure Works" }, { v: "€1,850" }, { v: "14.08" }, { v: "paid", cite: "3" }],
      [{ v: "N-118" }, { v: "Northwind" }, { v: "€960" }, { v: "20.08" }, { v: "paid", cite: "3" }],
      [{ v: "HR-08" }, { v: "Benefit provider" }, { v: "€540" }, { v: "10.08" }, { v: "paid", cite: "3" }],
    ] },
    evidence: [
      { n: "1", title: "Invoice 08/2026", srcType: "mail", snippet: "“Payment due: 29.08, to the account given below.”", meta: "27.08.2026 · Finance",
        pre: "Please find attached invoice 08/2026 for the August services.",
        quote: "Payment due: 29.08, to the account given below. Please note our bank details have changed.",
        post: "Thank you for the prompt payment.",
        supports: "08/2026 unpaid · due 29.08 · account changed", mail: "“Invoice 08/2026”, 27.08.2026”" },
      { n: "2", title: "Licence renewal - Adventure Works", srcType: "mail", snippet: "“The renewal invoice AW-2291 is due on 5 September.”", meta: "20.08.2026 · E. Sikora",
        pre: "Your licences run out at the end of September.",
        quote: "The renewal invoice AW-2291 is due on 5 September. The offer stands until then.",
        post: "Let us know if the number of seats changes.",
        supports: "AW-2291 unpaid · due 05.09", mail: "“Licence renewal - Adventure Works”, 20.08.2026”" },
      { n: "3", title: "Payment run 21.08 - confirmation", srcType: "mail", snippet: "“Three invoices went out today: 07/2026, N-118 and HR-08.”", meta: "21.08.2026 · M. Nowak",
        pre: "The August payment run is done.",
        quote: "Three invoices went out today: 07/2026, N-118 and HR-08. Nothing else was approved in time.",
        post: "08/2026 waits for your approval.",
        supports: "three August invoices paid on 21.08", mail: "“Payment run 21.08 - confirmation”, 21.08.2026”" },
    ],
    sources: ["faktura1", "finanse", "marta"],
  },
  owners: {
    confidence: "moderate confidence", gap: "nobody is named for Adventure Works", meta: "11 messages, 3 people", blocks: ["answer", "people"],
    parts: [
      { text: "Marta Nowak owns the Contoso and Fabrikam renewals", cite: "1" },
      { text: ", and Jacek Wrona reviews the terms before anything is signed", cite: "2" },
      { text: ". Nobody has been named for Adventure Works yet", cite: "1" },
    ],
    people: [
      { initials: "MN", name: "Marta Nowak", role: "Finance · owns Contoso and Fabrikam", last: "today", cite: "1" },
      { initials: "JW", name: "Jacek Wrona", role: "Lawyer · reviews the terms", last: "24.08", cite: "2" },
      { initials: "TB", name: "Tomasz Bąk", role: "Fabrikam · their side of the renewal", last: "yesterday", cite: "1" },
    ],
    evidence: [
      { n: "1", title: "Q4 renewals - who takes what", srcType: "mail", snippet: "“I take Contoso and Fabrikam. Adventure Works is still open.”", meta: "18.08.2026 · M. Nowak",
        pre: "Three renewals fall in the fourth quarter.",
        quote: "I take Contoso and Fabrikam. Adventure Works is still open, somebody from delivery should pick it up.",
        post: "Tomasz Bąk is the contact on Fabrikam's side.",
        supports: "Marta owns two renewals · Adventure Works unowned", mail: "“Q4 renewals - who takes what”, 18.08.2026”" },
      { n: "2", title: "Legal opinion - indexation cap", srcType: "mail", snippet: "“Send me each renewal before it is signed.”", meta: "24.08.2026 · J. Wrona",
        pre: "I have read the addendum and the proposed terms.",
        quote: "Send me each renewal before it is signed, the indexation clause differs between the three.",
        post: "A 5% cap is defensible against the last three years of CPI.",
        supports: "legal reviews every renewal", mail: "“Legal opinion - indexation cap”, 24.08.2026”" },
    ],
    sources: ["marta", "jacek", "fabrikam1"],
  },
  pilot: {
    confidence: "moderate confidence", gap: "depends on Fabrikam's Q4 date", meta: "14 messages read", blocks: ["answer", "timeline"],
    parts: [
      { text: "Sign-off is planned for 12 November", cite: "1" },
      { text: ", one week later than first agreed, because the updated plan moves the pilot", cite: "2" },
      { text: ". It holds only if Fabrikam confirms the Q4 delivery date", cite: "3" },
    ],
    timeline: [
      { date: "21.08.2026", title: "Phase two closed", detail: "41 of 58 tasks", cite: "1" },
      { date: "15-19.09.2026", title: "UAT window", detail: "proposed by Piotr", cite: "1" },
      { date: "05.11.2026", title: "Pilot ends", detail: "moved by one week", cite: "2" },
      { date: "12.11.2026", title: "Sign-off", detail: "needs Fabrikam's date", cite: "3" },
    ],
    evidence: [
      { n: "1", title: "Re: rollout schedule", srcType: "mail", snippet: "“I suggest 15-19 September so we finish before the October training.”", meta: "26.08.2026 · P. Zieliński",
        pre: "Confirming that phase two is closed, we closed 41 of 58 tasks.",
        quote: "The UAT window is still to be agreed. I suggest 15-19 September so we finish before the October training, with sign-off on 12 November.",
        post: "I rate the schedule risk as medium.",
        supports: "phase two closed · UAT proposed · sign-off 12.11", mail: "“Re: rollout schedule”, 26.08.2026”" },
      { n: "2", title: "rollout_v3.xlsx", srcType: "attachment", snippet: "“Pilot: 22.10 - 05.11 (was 15.10 - 29.10)”", meta: "26.08.2026 · sheet Plan",
        pre: "Version 3 of the rollout plan.",
        quote: "Pilot: 22.10 - 05.11 (was 15.10 - 29.10). Sign-off follows one week after the pilot ends.",
        post: "Nothing else changed against version 2.",
        supports: "pilot moved by one week", mail: "“Re: rollout schedule”, 26.08.2026”" },
      { n: "3", title: "Q4 delivery dates", srcType: "mail", snippet: "“Without a date from you we cannot book the pilot team.”", meta: "26.08.2026 · T. Bąk",
        pre: "We are planning our fourth quarter.",
        quote: "Without a date from you we cannot book the pilot team. Please confirm the Q4 delivery date this week.",
        post: "Phase three starts once the date is in.",
        supports: "sign-off depends on the Q4 date", mail: "“Q4 delivery dates”, 26.08.2026”" },
    ],
    sources: ["piotr", "fabrikam1"],
  },
  move: {
    confidence: "high confidence", gap: "", meta: "6 messages read", blocks: ["answer"],
    parts: [
      { text: "Yes. Facilities confirmed the weekend of 17 October, and packing crates arrive the week before", cite: "1" },
      { text: ". HR still asks every team to pick its desks on the floor plan", cite: "2" },
    ],
    evidence: [
      { n: "1", title: "Re: office move - dates", srcType: "mail", snippet: "“The move is confirmed for the weekend of 17 October.”", meta: "26.08.2026 · Facilities",
        pre: "Thank you for your patience.",
        quote: "The move is confirmed for the weekend of 17 October. Packing crates will be delivered the week before.",
        post: "Lifts are booked for Saturday morning.",
        supports: "move date confirmed · crates the week before", mail: "“Re: office move - dates”, 26.08.2026”" },
      { n: "2", title: "Office move - pick your desks", srcType: "mail", snippet: "“Please mark your team's desks on the floor plan by 10 September.”", meta: "25.08.2026 · HR Department",
        pre: "The new floor plan is attached.",
        quote: "Please mark your team's desks on the floor plan by 10 September.",
        post: "Teams that do not choose are seated by HR.",
        supports: "desk choice still owed", mail: "“Office move - pick your desks”, 25.08.2026”" },
    ],
    sources: ["hr1"],
  },
});

// Questions kept on Start. Each refreshes when new mail matches it; `changed` says whether that moved
// its answer since it was last opened, and `paused` stands for a refresh the allowance did not cover. `plan` is the
// answer the question opens.
MailFathomDesign.data.watchedQuestions = [
  { id: "w1", plan: "open", q: "What is still open with Contoso before 28 August?", state: "changed", note: "1 new message from Anna Kowalska since Tuesday", answer: "Two things: the CPI cap counter-proposal and the signature page. Anna asked for both again on Tuesday." },
  { id: "w2", plan: "invoices", q: "Which invoices from August are unpaid?", state: "same", note: "no change since yesterday", answer: "Two: invoice 08/2026 from Fabrikam, waiting on your approval, and the Adventure Works licence renewal." },
  { id: "w3", plan: "owners", q: "Who owns the Q4 renewals?", state: "paused", note: "paused until 1 September - the allowance is spent", answer: "Marta Nowak for Contoso and Fabrikam; nobody is named for Adventure Works yet." },
  { id: "w4", plan: "pilot", q: "When does the Fabrikam pilot sign-off land?", state: "same", note: "no change since Monday", answer: "Planned for 12 November, once Fabrikam confirms the Q4 delivery date." },
  { id: "w5", plan: "move", q: "Has anyone answered about the office move in October?", state: "changed", note: "2 new replies in the facilities thread", answer: "Yes. Facilities confirmed the weekend of 17 October, and packing crates arrive the week before." },
];

// The questions Start offers as chips under its field.
MailFathomDesign.data.suggestedQuestions = [
  { text: "How have the Contoso contract terms changed since 2021?", why: "Contoso sent new terms yesterday" },
  { text: "Where is the latest version of the insurance policy?", why: "3 versions arrived as attachments this year" },
  { text: "Who approved the indexation budget in 2023?", why: "the August calculation cites that approval" },
];
