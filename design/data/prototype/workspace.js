// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Who is signed in, and the build the About screen names.
MailFathomDesign.data.me = { name: "Karolina Kowalska", address: "k.kowalska@company.example" };
MailFathomDesign.data.app = { version: "2.4.2", build: "build 20260907" };
// The footer MailFathom appends to what the signed-in person sends, per account, line by line as it went out: `strong`
// is the name line, `small` the fine print. A message of their own that carries `footer: true` (threads.js) names it,
// as does the "Still open" reply the prototype writes into every thread with no written-out history, and the simplified
// view folds it to one line; one without the flag was sent before the footer existed and is whole. The composer draws the
// sending account's footer under what is being written, and an account missing here — the shared Board — has none.
MailFathomDesign.data.footers = {
  acc1: [
    { text: "Karolina Kowalska", strong: true },
    { text: "Head of Procurement · Nordwind" },
    { text: "+48 22 555 0143 · k.kowalska@nordwind.example" },
    { text: "Nordwind S.A. · 12 Harbour Street, 00-950 Warsaw · nordwind.example" },
    { text: "This message and its attachments are confidential and meant only for the people it is addressed to. If it reached you by mistake, tell the sender and delete it.", small: true },
  ],
  acc2: [
    { text: "Karolina Kowalska", strong: true },
    { text: "+48 600 555 012" },
  ],
};
// The domains whose links open at once, without asking first: the person's own list, which the service keeps per
// person. A domain covers its subdomains too, so files.nordwind.example opens and northwind-billing.example asks.
// No screen edits it yet.
MailFathomDesign.data.trustedLinkDomains = ["nordwind.example"];

// The mail accounts in the folder column and the Move dialog: `counts` are the unread badges on the standard
// folders, and `moveFolders` the folders Move offers in that account. `movesSpam` is the account's own setting for
// whether marking spam or not spam also moves the message, off unless an account turns it on.
MailFathomDesign.data.accounts = [
  { key: "acc1", label: "Nordwind · work", address: "k.kowalska@nordwind.example", dot: "var(--accent)", counts: { Inbox: "8", Drafts: "2", Spam: "3" }, movesSpam: true,
    moveFolders: [
      { icon: "inbox", label: "Inbox" }, { icon: "archive", label: "Archive" }, { icon: "report", label: "Spam" },
      { icon: "folder", label: "Projects" }, { icon: "folder", label: "Contracts" }, { icon: "folder", label: "Archive 2026" },
    ] },
  { key: "acc2", label: "Personal mail", address: "kowalska.k@mail.example", dot: "#4d9aa8", counts: { Inbox: "4" }, movesSpam: false,
    moveFolders: [
      { icon: "inbox", label: "Inbox" }, { icon: "archive", label: "Archive" }, { icon: "report", label: "Spam" },
      { icon: "folder", label: "Clients" }, { icon: "folder", label: "To decide" },
    ] },
  // A shared mailbox: several people read and send from it, so wherever an account is drawn or chosen it carries the
  // shared mark beside its name.
  { key: "acc3", label: "Board", address: "board@nordwind.example", dot: "#b0873a", shared: true, counts: { Inbox: "3" }, movesSpam: false,
    moveFolders: [
      { icon: "inbox", label: "Inbox" }, { icon: "archive", label: "Archive" }, { icon: "report", label: "Spam" },
      { icon: "folder", label: "Board minutes" },
    ] },
];

// What has not left each account yet: its Outbox, drawn as a mark on that account's Sent row while it holds anything,
// newest first. `stage` is what the row says: waiting (handed over, no attempt yet), scheduled (written to leave at
// `leaveAt`), retrying (an attempt failed before the body went out; the next one is at `retryAt`), sending (an attempt
// holds it now), unknown (the body went out and no answer came back, so nobody knows whether it arrived) or refused
// (the server would not take it, `reason` in words). `rcpt` is every recipient with its header and, where the server
// refused it, what it said; a send names a recipient by address alone, so the address is what is drawn. An account missing here has nothing waiting, so its Sent row carries no mark.
MailFathomDesign.data.outbox = {
  acc1: [
    { id: "ob3", stage: "waiting", at: "10:44", attempts: 0,
      rcpt: [{ hdr: "To", name: "Tomasz Bąk", mail: "t.bak@fabrikam.example" }],
      subject: "Re: UAT testing date",
      body: ["Tomasz,", "12.11 works for us. Please confirm the test accounts by 02.09 so audit gets the list in time.", "Thanks,\nKarolina"] },
    { id: "ob2", stage: "sending", at: "10:41", attempts: 1,
      rcpt: [{ hdr: "To", name: "Marta Nowak", mail: "m.nowak@company.example" }],
      subject: "Budget line for Q4 renewals",
      body: ["Marta,", "The negotiating line is the 5% cap. You can close the 2027 budget on that figure.", "Karolina"] },
    { id: "ob6", stage: "retrying", at: "10:12", attempts: 2, retryAt: "10:55",
      rcpt: [{ hdr: "To", name: "Piotr Zieliński", mail: "p.zielinski@company.example" }, { hdr: "Cc", name: "Jacek Wrona", mail: "j.wrona@company.example" }],
      subject: "Re: rollout schedule",
      reason: "Nordwind's mail server did not answer.",
      body: ["Piotr,", "Phase two is confirmed for the 14th. Jacek has the legal sign-off ready.", "Karolina"] },
    { id: "ob7", stage: "unknown", at: "09:58", attempts: 1,
      rcpt: [{ hdr: "To", name: "Anna Kowalska", mail: "a.kowalska@contoso.example" }],
      subject: "Re: proposed terms for 2027",
      body: ["Anna,", "Thank you - we will come back with the 2027 terms by Friday.", "Karolina"] },
    { id: "ob1", stage: "refused", at: "09:52", attempts: 1,
      rcpt: [
        { hdr: "To", name: "Anna Kowalska", mail: "a.kowalska@contoso.example", said: "Not taken" },
        { hdr: "Cc", name: "Marta Nowak", mail: "m.nowak@company.example", said: "Not taken" },
      ],
      subject: "Re: Contract addendum - signatures",
      reason: "Nordwind's mail server would not take it: this account has sent as much mail as it may today. Nothing reached Anna or Marta. The limit resets at midnight.",
      attachments: [{ name: "Addendum-3-signed.pdf", meta: "PDF · 312 KB" }],
      body: ["Anna,", "We accept the 2 h SLA and propose a 5% CPI cap from 2027. The signed addendum is attached.", "Best regards,\nKarolina"] },
  ],
  acc2: [
    { id: "ob4", stage: "sending", at: "10:46", attempts: 1,
      rcpt: [{ hdr: "To", name: "Ewa Sikora", mail: "e.sikora@adventure.example" }],
      subject: "Weekend support - one question",
      body: ["Ewa,", "Is weekend support possible from October, and on what terms?", "Karolina"] },
    { id: "ob8", stage: "scheduled", at: "10:30", attempts: 0, leaveAt: "Mon 08:00",
      rcpt: [{ hdr: "To", name: "Robert Lis", mail: "r.lis@nordwind.example" }],
      subject: "Re: Quote for the reporting module",
      body: ["Robert,", "We accept the quote. Please send the order confirmation to finance.", "Karolina"] },
  ],
};

// Drafts MailFathom keeps whose copy in the account's Drafts folder on the mail server may be missing (its filing was
// never answered) or out of date (an older copy could not be removed after an edit): the Drafts list gathers them at
// its foot under *Out of step with your server*. `receipts` is what a draft asks for when it is sent - `dsn`, a delivery
// notification, and `mdn`, a read receipt - kept with the draft as everything else it says is.
MailFathomDesign.data.localDrafts = {
  acc1: [
    { id: "ld1", at: "09:31", subject: "Q4 supplier review - agenda",
      to: ["r.lis@nordwind.example"], cc: ["m.nowak@company.example"], receipts: { mdn: true },
      attachments: [{ name: "Supplier-scorecard-Q3.xlsx", meta: "XLSX · 48 KB" }],
      body: ["Robert,", "Before Thursday, a short agenda: delivery times against the SLA, the two open credit notes, and the 2027 price list.", "Karolina"] },
  ],
};

// A mailbox change the mail server never took, on the message it was asked for (the thread's newest). `fate` is
// exhausted — MailFathom stopped trying — or unknown — the server never answered. `act` is what was asked.
MailFathomDesign.data.changeNotices = {
  travel1: { act: "move", fate: "exhausted", notif: "nf2" },
  devops1: { act: "flag", fate: "unknown" },
};

// The unified view across every account at the top of the folder column.
MailFathomDesign.data.unifiedAccounts = {
  label: "All accounts", title: "Unified folders - three accounts", dot: "linear-gradient(135deg,var(--accent) 34%,#4d9aa8 34% 67%,#b0873a 67%)",
  counts: { Inbox: "15", Drafts: "2" },
};

// The mailboxes a message can be sent from, and the Reply-To a message carries unless another is added.
MailFathomDesign.data.mailboxes = [
  { mail: "k.kowalska@nordwind.pl", dot: "var(--accent)" },
  { mail: "kowalska.k@mail.example", dot: "#4d9aa8" },
  { mail: "board@nordwind.example", dot: "#b0873a", shared: true },
];
MailFathomDesign.data.defaultReplyTo = "k.kowalska@nordwind.example";

// The accounts Settings lists: the address and the name the administrator set, the key of the footer each sends with
// in `footers` above, whether its organization sets that footer (`footerByOrganization`), and the folders MailFathom
// reads from it. A folder is found by its `role` or by its `path` on the mail server, outermost level first, and
// carries the four switches the person may change: `sync` mirrors it, `ai` lets AI reach it, `embed` indexes it for
// search by meaning, which needs `ai`, and `create` lets MailFathom create a folder the path names and the server lacks.
// `certificates` is the account's own store of S/MIME certificates and OpenPGP keys, each with its private key, that
// `settings.sending` signs and encrypts with.
MailFathomDesign.data.mailAccounts = [
  { id: "a1", name: "Karolina Kowalska", mail: "k.kowalska@nordwind.example", dot: "var(--accent)", footer: "acc1", footerByOrganization: true, folderDelete: "DeleteOnServer",
    certificates: [
      { id: "smime-kowalska", kind: "smime", name: "Karolina Kowalska", address: "k.kowalska@nordwind.example", file: "kowalska-2027.p12", validUntil: "2027-03-14" },
      { id: "smime-kowalska-enc", kind: "smime", name: "Karolina Kowalska - encryption", address: "k.kowalska@nordwind.example", file: "kowalska-enc-2027.p12", validUntil: "2027-03-14" },
    ],
    settings: { language: "English",
      authoredDeleteServer: "LeaveOnServer",
      trustOwnDomains: true, strict: false,
      contacts: { on: true, minMessages: 2, exclusions: [{ domain: "lists.nordwind.example", subdomains: true }, { pattern: "noreply@*" }] },
      spam: { on: true, folders: ["Inbox"], moveToJunk: true, markRead: false, junkFolder: null, aiScanning: true },
      sensitive: { secrets: true, pii: true, screenOutgoing: ["Secrets"] },
      sending: { fromAddress: null, fromName: "Karolina Kowalska", fileSentCopy: true, withdrawDuplicate: true, signWith: "smime-kowalska", encryptWith: "same" } },
    folders: [
      { alias: "Inbox", role: "inbox", path: [], sync: true, ai: true, embed: true, create: false },
      { alias: "Sent", role: "sent", path: [], sync: true, ai: true, embed: true, create: false },
      { alias: "Drafts", role: "drafts", path: [], sync: true, ai: false, embed: false, create: false },
      { alias: "Archive", role: "archive", path: [], sync: true, ai: true, embed: true, create: false },
      { alias: "Spam", role: "junk", path: [], sync: true, ai: false, embed: false, create: false },
      { alias: "Trash", role: "trash", path: [], sync: true, ai: false, embed: false, create: false },
      { alias: "Projects", role: "", path: ["INBOX", "Projects"], sync: true, ai: true, embed: true, create: false },
      { alias: "Contracts", role: "", path: ["INBOX", "Contracts"], sync: true, ai: false, embed: false, create: false },
      { alias: "Archive 2026", role: "", path: ["Archive", "2026"], sync: false, ai: true, embed: false, create: true },
    ] },
  { id: "a2", name: "Karolina - private", mail: "kowalska.k@mail.example", dot: "#4d9aa8", footer: "acc2", folderDelete: "MarkDeletedLocally",
    certificates: [
      { id: "pgp-private", kind: "pgp", name: "kowalska.k@mail.example", address: "kowalska.k@mail.example", file: "kowalska-private.asc", fingerprint: "5E1A 9C42 77D0 31B8", validUntil: null },
    ],
    settings: { language: "Polish",
      authoredDeleteServer: "LeaveOnServer",
      trustOwnDomains: false, strict: false,
      contacts: { on: false, minMessages: 2, exclusions: [] },
      spam: { on: false, folders: null, moveToJunk: false, markRead: false, junkFolder: null, aiScanning: false },
      sensitive: { secrets: true, pii: false, screenOutgoing: [] },
      sending: { fromAddress: null, fromName: null, fileSentCopy: true, withdrawDuplicate: true, signWith: "pgp-private", encryptWith: null } },
    folders: [
      { alias: "Inbox", role: "inbox", path: [], sync: true, ai: true, embed: true, create: false },
      { alias: "Sent", role: "sent", path: [], sync: true, ai: true, embed: false, create: false },
      { alias: "Spam", role: "junk", path: [], sync: true, ai: false, embed: false, create: false },
      { alias: "Trash", role: "trash", path: [], sync: true, ai: false, embed: false, create: false },
    ] },
  { id: "a3", name: "Nordwind board", shared: true, mail: "board@nordwind.example", dot: "#b0873a", folderDelete: "DeleteOnServer",
    settings: { language: "English",
      authoredDeleteServer: "LeaveOnServer",
      trustOwnDomains: true, strict: true,
      contacts: { on: false, minMessages: 2, exclusions: [] },
      spam: { on: true, folders: ["Inbox"], moveToJunk: true, markRead: true, junkFolder: null, aiScanning: false },
      sensitive: { secrets: true, pii: true, screenOutgoing: ["Secrets", "Pii"] },
      sending: { fromAddress: null, fromName: null, fileSentCopy: true, withdrawDuplicate: true, signWith: null, encryptWith: null } },
    folders: [
      { alias: "Inbox", role: "inbox", path: [], sync: true, ai: true, embed: true, create: false },
    ] },
];
