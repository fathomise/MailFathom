// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Who is signed in, and the build the About screen names.
MailFathomDesign.data.me = { name: "Karolina Kowalska", address: "k.kowalska@company.example" };
MailFathomDesign.data.app = { version: "2.4.2", build: "build 20260907" };

// The mail accounts in the folder column and the Move dialog: `counts` are the unread badges on the standard
// folders, and `moveFolders` the folders Move offers in that account.
MailFathomDesign.data.accounts = [
  { key: "acc1", label: "Nordwind · work", address: "k.kowalska@nordwind.example", dot: "var(--accent)", counts: { Inbox: "8", Drafts: "2", Spam: "3" },
    moveFolders: [
      { icon: "inbox", label: "Inbox" }, { icon: "archive", label: "Archive" },
      { icon: "folder", label: "Projects" }, { icon: "folder", label: "Contracts" }, { icon: "folder", label: "Archive 2026" },
    ] },
  { key: "acc2", label: "Personal mail", address: "kowalska.k@mail.example", dot: "#4d9aa8", counts: { Inbox: "4" },
    moveFolders: [
      { icon: "inbox", label: "Inbox" }, { icon: "archive", label: "Archive" },
      { icon: "folder", label: "Clients" }, { icon: "folder", label: "To decide" },
    ] },
];

// What has not left each account yet: its Outbox, drawn as a mark on that account's Sent row while it holds anything.
// `stage` is waiting (handed over, nothing transmitted), sending (going out now) or refused (the server would not take
// it, with its reason in words). An account missing here has nothing waiting, so its Sent row carries no mark.
MailFathomDesign.data.outbox = {
  acc1: [
    { id: "ob1", stage: "refused", to: "Anna Kowalska", toMail: "a.kowalska@contoso.example", at: "09:52",
      subject: "Re: Contract addendum - signatures",
      reason: "Nordwind's mail server would not take it: this account has sent as much mail as it may today. Nothing reached Anna. The limit resets at midnight.",
      body: ["Anna,", "We accept the 2 h SLA and propose a 5% CPI cap from 2027. If that works for you, I will send the addendum for signature tomorrow.", "Best regards,\nKarolina"] },
    { id: "ob2", stage: "sending", to: "Marta Nowak", toMail: "m.nowak@company.example", at: "10:41",
      subject: "Budget line for Q4 renewals",
      body: ["Marta,", "The negotiating line is the 5% cap. You can close the 2027 budget on that figure.", "Karolina"] },
    { id: "ob3", stage: "waiting", to: "Tomasz Bąk", toMail: "t.bak@fabrikam.example", at: "10:44",
      subject: "Re: UAT testing date",
      body: ["Tomasz,", "12.11 works for us. Please confirm the test accounts by 02.09 so audit gets the list in time.", "Thanks,\nKarolina"] },
  ],
  acc2: [
    { id: "ob4", stage: "sending", to: "Ewa Sikora", toMail: "e.sikora@adventure.example", at: "10:46",
      subject: "Weekend support - one question",
      body: ["Ewa,", "Is weekend support possible from October, and on what terms?", "Karolina"] },
  ],
};

// The unified view across both accounts at the top of the folder column.
MailFathomDesign.data.unifiedAccounts = {
  label: "All accounts", title: "Unified folders - two accounts", dot: "linear-gradient(135deg,var(--accent) 50%,#4d9aa8 50%)",
  counts: { Inbox: "12", Drafts: "2" },
};

// The mailboxes a message can be sent from, and the Reply-To a message carries unless another is added.
MailFathomDesign.data.mailboxes = [
  { mail: "k.kowalska@nordwind.pl", dot: "var(--accent)" },
  { mail: "kowalska.k@mail.example", dot: "#4d9aa8" },
  { mail: "board@nordwind.example", dot: "#b0873a" },
];
MailFathomDesign.data.defaultReplyTo = "k.kowalska@nordwind.example";

// The accounts Settings lists, with what their connection forms hold.
MailFathomDesign.data.mailAccounts = [
  { id: "a1", name: "Karolina Kowalska", mail: "k.kowalska@nordwind.pl", login: "k.kowalska@nordwind.pl", pass: "hunter-nordwind-24", dot: "var(--accent)", imapHost: "imap.nordwind.pl", imapPort: "993", imapEnc: "ssl", smtpHost: "smtp.nordwind.pl", smtpPort: "587", smtpEnc: "starttls", earliest: "", folders: { inbox: "INBOX", sent: "INBOX.Sent", drafts: "INBOX.Drafts", archive: "INBOX.Archive", junk: "INBOX.Spam", trash: "INBOX.Trash" } },
  { id: "a2", name: "Karolina - private", mail: "kowalska.k@mail.example", login: "kowalska.k", pass: "mailexample2026", dot: "#4d9aa8", imapHost: "imap.mail.example", imapPort: "993", imapEnc: "ssl", smtpHost: "smtp.mail.example", smtpPort: "465", smtpEnc: "ssl" },
  { id: "a3", name: "Nordwind board", mail: "board@nordwind.example", login: "board", pass: "board-shared-key", dot: "#b0873a", imapHost: "imap.nordwind.example", imapPort: "143", imapEnc: "starttls", smtpHost: "smtp.nordwind.example", smtpPort: "587", smtpEnc: "starttls" },
];
