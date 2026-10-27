// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

/* CONTACT AVATARS — PLACEHOLDERS.
   The images below are generated for the prototype and embedded as data URIs (so the demo also works opened
   from a file / in a new tab, with no dependency on ./avatars/, where the same PNG files live).
   In the real app a contact photo comes from the server together with the contact (photo/avatarUrl field in the
   address-book response: LDAP thumbnailPhoto, vCard PHOTO, Exchange/Graph /users/{id}/photo, MailFathom's own API),
   is cached locally and refreshed on contact sync. Map key = full sender name.
   No entry = contact without a photo - the UI must work without an avatar (system senders, mailing lists, unknown addresses). */
MailFathomDesign.data.avatars = {
  "Anna Kowalska": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23c9d6e8%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%235b7ba6%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%235b7ba6%22%2F%3E%3C%2Fsvg%3E",
  "Marta Nowak": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23e8d9c9%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%23a67f5b%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%23a67f5b%22%2F%3E%3C%2Fsvg%3E",
  "Piotr Zieliński": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23cfe0d3%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%235f8a6c%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%235f8a6c%22%2F%3E%3C%2Fsvg%3E",
  "Jacek Wrona": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23dcd6e8%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%237a6ba6%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%237a6ba6%22%2F%3E%3C%2Fsvg%3E",
  "Robert Lis": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23e8d2d2%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%23a66b6b%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%23a66b6b%22%2F%3E%3C%2Fsvg%3E",
  "Ewa Sikora": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23d3e2e8%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%235b8ea6%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%235b8ea6%22%2F%3E%3C%2Fsvg%3E",
  "Tomasz Bąk": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23e4e0cd%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%23958b57%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%23958b57%22%2F%3E%3C%2Fsvg%3E",
  "Karolina Kowalska": "data:image/svg+xml,%3Csvg%20xmlns%3D%22http%3A%2F%2Fwww.w3.org%2F2000%2Fsvg%22%20viewBox%3D%220%200%2072%2072%22%3E%3Crect%20width%3D%2272%22%20height%3D%2272%22%20fill%3D%22%23cdd8f0%22%2F%3E%3Ccircle%20cx%3D%2236%22%20cy%3D%2229%22%20r%3D%2211.2%22%20fill%3D%22%234f66a8%22%2F%3E%3Cpath%20d%3D%22M14.4%2072c0-13.3%209.7-20.2%2021.6-20.2S57.6%2058.7%2057.6%2072z%22%20fill%3D%22%234f66a8%22%2F%3E%3C%2Fsvg%3E",
};

MailFathomDesign.data.contactInsights = {
  anna: { reply: "≈ 3 h", peak: "mornings 8-11", style: "Specifics, numbers, short paragraphs", next: "Send the 5% CPI cap counter-proposal and confirm you accept the 2 h SLA.", wait: "waiting on us for 2 days" },
  marta: { reply: "≈ 1 h", peak: "midday", style: "Numbers and spreadsheets, no generalities", next: "Confirm the negotiating line so she can close the 2027 budget.", wait: "waiting on us for 1 day" },
  piotr: { reply: "≈ 5 h", peak: "afternoons", style: "Bullet points and dates", next: "Ask for a firm UAT date - it is the only open item.", wait: "" },
  jacek: { reply: "≈ 1 day", peak: "mornings", style: "Formal, with clause references", next: "Ask for ready wording of the indexation cap to paste into the addendum.", wait: "" },
  tomasz: { reply: "≈ 4 h", peak: "mornings", style: "To the point, with dates", next: "Confirm or challenge the 12.11 date - deadline 02.09.", wait: "waiting on us for 1 day" },
  ewa: { reply: "≈ 2 h", peak: "mornings", style: "Direct questions, expects a clear answer", next: "Answer whether weekend support is possible and on what terms.", wait: "waiting on us for 1 day" },
};

/* Every contact the address book answers with, in three kinds:
   - own: written down by the reader, editable: a name, an address, a photo, and `phones`, each with a label (Mobile,
     Work, Home, Other) - no company and no role, which are a member's and come from their profile;
   - organization: kept by the reader's organization in its own book (`organization` names it), read-only to members;
   - member: a member of the reader's organization, mapped from their profile rather than written down - read-only,
     with what connects the contact to a user: `presence` (active, away, busy, dnd, offline, hidden - a hidden member is
     drawn offline to everybody else), `lastSeen`, `phoneApp`, `status` (a message of at most 280 characters, with when
     it clears), `workMode`, `ooo` (the day an automatic reply ends - never its text), `clock` (their local time and its
     offset from the reader's, absent where the member states no zone), `hours` (in their time, converted where the
     zone is known), and `manager` (the id of the member they report to). `mails` lists every address, one preferred. */
MailFathomDesign.data.contacts = [
  { id: "anna", kind: "own", name: "Anna Kowalska", mail: "a.kowalska@contoso.example",
    phones: [{ label: "Mobile", number: "+48 601 555 102" }, { label: "Work", number: "+48 22 555 41 00" }],
    last: "today 09:14", owed: "Addendum decision by 28.08",
    note: "Leads the addendum renegotiation. Usually replies the same day; expects our decision before the end of the week.",
    threads: [{ id: "contoso", label: "Contract addendum - signatures", meta: "6 messages" }, { id: "anna2", label: "Re: proposed terms for 2027", meta: "3 messages" }],
    docs: [{ type: "PDF", name: "SLA addendum.pdf", size: "248 kB" }, { type: "XLSX", name: "Calculation_2027.xlsx", size: "42 kB" }] },
  { id: "jacek", kind: "own", name: "Jacek Wrona", mail: "j.wrona@lawoffice.example",
    phones: [],
    last: "24.08.2026", owed: "None",
    note: "Confirmed that an indexation cap is permissible. Preparing the Northwind NDA.",
    threads: [{ id: "jacek", label: "Legal opinion - indexation cap", meta: "2 messages" }, { id: "prawnik2", label: "Draft NDA - Northwind", meta: "2 messages" }],
    docs: [{ type: "DOCX", name: "NDA_Northwind.docx", size: "72 kB" }] },
  { id: "tomasz", kind: "own", name: "Tomasz Bąk", mail: "t.bak@fabrikam.example",
    phones: [{ label: "Work", number: "+48 61 555 20 30" }],
    last: "yesterday 17:05", owed: "Our confirmation by 02.09",
    note: "Reported a 15-day delivery slip caused by a stoppage at the manufacturer.",
    threads: [{ id: "fabrikam1", label: "Q4 delivery dates", meta: "7 messages" }],
    docs: [] },
  { id: "ewa", kind: "own", name: "Ewa Sikora", mail: "e.sikora@adventure.example",
    phones: [],
    last: "yesterday 10:15", owed: "Weekend SLA answer by 01.09",
    note: "Asking about extending support to weekends - the current contract covers business days.",
    threads: [{ id: "klient3", label: "Question about weekend SLA", meta: "4 messages" }],
    docs: [] },
  { id: "travel", kind: "organization", organization: "Nordwind", name: "Globe Travel desk", mail: "desk@globetravel.example",
    phones: [{ label: "Work", number: "+48 22 555 77 00" }],
    last: "no correspondence yet", owed: "-",
    note: "Books business travel for everybody at Nordwind. Kept in the organization's address book.",
    threads: [], docs: [] },
  { id: "facility", kind: "organization", organization: "Nordwind", name: "Lena Wójcik", mail: "l.wojcik@officepark.example",
    phones: [{ label: "Mobile", number: "+48 602 555 318" }],
    last: "12.08.2026", owed: "-",
    note: "Runs the building Nordwind rents. Kept in the organization's address book.",
    threads: [], docs: [] },

  { id: "zofia", kind: "member", name: "Zofia Krawczyk", role: "Managing Director", department: "Management", org: "Nordwind",
    mail: "z.krawczyk@nordwind.example", mails: [{ mail: "z.krawczyk@nordwind.example", preferred: true }],
    phones: [{ label: "Work", number: "+1 212 555 0148" }],
    presence: "offline", lastSeen: "yesterday 23:10", workMode: { mode: "Remote", scope: "until changed" },
    clock: { time: "04:41", offset: "6 h behind you" },
    hours: { own: "Mon–Fri 9:00–17:00", yours: "15:00–23:00 your time", outside: true },
    last: "21.08.2026", owed: "-", note: "Runs Nordwind from the New York office this quarter.",
    threads: [], docs: [] },
  { id: "adam", kind: "member", name: "Adam Lewandowski", role: "Finance Director", department: "Finance", org: "Nordwind", manager: "zofia",
    mail: "a.lewandowski@nordwind.example", mails: [{ mail: "a.lewandowski@nordwind.example", preferred: true }],
    phones: [{ label: "Work", number: "+48 22 555 01 10" }],
    presence: "away", workMode: { mode: "Office", scope: "today" },
    clock: { time: "10:41", offset: "same time as you" },
    hours: { own: "Mon–Fri 8:00–16:00", outside: false },
    last: "yesterday 14:02", owed: "-", note: "Signs off the 2027 budget.",
    threads: [], docs: [] },
  { id: "marta", kind: "member", name: "Marta Nowak", role: "Controller", department: "Finance", org: "Nordwind", manager: "adam",
    mail: "m.nowak@nordwind.example", mails: [{ mail: "m.nowak@nordwind.example", preferred: true }, { mail: "m.nowak@company.example" }],
    phones: [{ label: "Work", number: "+48 22 555 01 14" }, { label: "Mobile", number: "+48 601 555 014" }],
    presence: "busy", phoneApp: true, status: { text: "Closing the 2027 budget - quickest by phone", until: "clears Friday" },
    workMode: { mode: "Office", scope: "today" },
    clock: { time: "10:41", offset: "same time as you" },
    hours: { own: "Mon–Fri 8:00–16:00", outside: false },
    last: "yesterday 11:20", owed: "Our reply by 27.08",
    note: "Calculated the impact of the increase on annual cost. Waiting on a decision to update the 2027 budget.",
    threads: [{ id: "marta", label: "CPI calculation 2027", meta: "4 messages" }],
    docs: [{ type: "XLSX", name: "CPI_2027.xlsx", size: "34 kB" }] },
  { id: "piotr", kind: "member", name: "Piotr Zieliński", role: "Head of Delivery", department: "Delivery", org: "Nordwind", manager: "zofia",
    mail: "p.zielinski@nordwind.example", mails: [{ mail: "p.zielinski@nordwind.example", preferred: true }, { mail: "p.zielinski@company.example" }],
    phones: [{ label: "Mobile", number: "+48 603 555 207" }],
    presence: "offline", lastSeen: "yesterday 18:20", phoneApp: true, ooo: "04.09",
    clock: { time: "10:41", offset: "same time as you" },
    hours: { own: "Mon–Fri 9:00–17:00", outside: false },
    last: "yesterday 16:41", owed: "Piotr: UAT test plan",
    note: "Confirmed phase two of the schedule. The UAT window is still unset.",
    threads: [{ id: "piotr", label: "Re: rollout schedule", meta: "9 messages" }],
    docs: [] },
  { id: "oliver", kind: "member", name: "Oliver Grant", role: "Delivery Engineer", department: "Delivery", org: "Nordwind", manager: "piotr",
    mail: "o.grant@nordwind.example", mails: [{ mail: "o.grant@nordwind.example", preferred: true }],
    phones: [{ label: "Work", number: "+44 20 7946 0321" }],
    presence: "active", phoneApp: true, workMode: { mode: "Remote", scope: "today" },
    clock: { time: "09:41", offset: "1 h behind you" },
    hours: { own: "Mon–Fri 9:00–17:30", yours: "10:00–18:30 your time", outside: false },
    last: "26.08.2026", owed: "-", note: "Runs the UAT environment from London.",
    threads: [], docs: [] },
  { id: "hanna", kind: "member", name: "Hanna Mazur", role: "QA Lead", department: "Delivery", org: "Nordwind", manager: "piotr",
    mail: "h.mazur@nordwind.example", mails: [{ mail: "h.mazur@nordwind.example", preferred: true }],
    phones: [],
    presence: "dnd", status: { text: "Release testing - mentions only", until: "clears at 14:00" },
    hours: { own: "Mon–Thu 7:00–15:00", outside: false },
    last: "25.08.2026", owed: "-", note: "Owns the UAT test plan.",
    threads: [], docs: [] },
  { id: "igor", kind: "member", name: "Igor Pawlak", role: "Systems Engineer", department: "Delivery", org: "Nordwind", manager: "piotr",
    mail: "i.pawlak@nordwind.example", mails: [{ mail: "i.pawlak@nordwind.example", preferred: true }],
    phones: [],
    presence: "hidden", lastSeen: "today 08:02",
    clock: { time: "10:41", offset: "same time as you" },
    hours: { own: "Mon–Fri 8:00–16:00", outside: false },
    last: "20.08.2026", owed: "-", note: "Looks after the build servers.",
    threads: [], docs: [] },
];

// The organization the signed-in person is a member of. Without one, nothing that connects a contact to a user is drawn.
MailFathomDesign.data.organization = { name: "Nordwind" };
