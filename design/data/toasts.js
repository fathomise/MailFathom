// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

MailFathomDesign.data.toastsSamples = [
  { kind: "neutral", name: "Neutral", title: "Archived 3 threads", body: "", action: "Undo",
    desc: "Confirmation of an ordinary operation. No colour - it does not take attention but offers a way back." },
  { kind: "success", name: "Success", title: "Message sent", body: "To: anna.kowalska@lexmar.example", action: "",
    desc: "Operation finished. Green accent only on the icon and the timer bar." },
  { kind: "error", name: "Error", title: "Not sent - it waits in the Outbox", body: "“Re: Contract addendum - signatures” is in the Outbox of Nordwind · work. Send it again or edit it there.", opens: true,
    desc: "Something failed and needs a decision. Always with where it stands, and pressed anywhere it goes there - never a status code." },
  { kind: "error", name: "Error - a change that did not land", title: "A change did not reach your mailbox", body: "Move - the mail server would not take it, so the message is still where the server holds it.", opens: true,
    desc: "Raised with its notification, unless an open tab is already following that change - then its pending line and toast stand for it." },
  { kind: "warning", name: "Warning - outcome unknown", title: "Not known whether this reached the server", body: "The connection went as this change was made. It stays shown as you asked until the server answers again, and then as the server holds it.",
    desc: "A write that got no answer - the connection was lost under it, or a gateway answered 502, 503 or 504. Never “nothing was saved”, which a client that heard nothing cannot know, and no Try again that could record it twice." },
  { kind: "neutral", name: "Neutral - still sending", title: "Still sending…", body: "The server has not answered yet.", lasting: true,
    desc: "A write with no answer after 10 s. Quiet: no colour and no timer bar - it stands until the write is answered or ends as unknown. Its close control only closes it." },
  { kind: "info", name: "Information - what the server holds", title: "The screen now shows what the server holds", body: "Changes made as the connection went are shown as the server holds them now.",
    desc: "Raised once, when the read made after the server answers again changes what an unknown write had drawn. A read that confirms every such write says nothing." },
  { kind: "warning", name: "Warning - a draft out of step", title: "Draft out of step with your server", body: "“Q4 supplier review - agenda” is kept in MailFathom, but your mail server's copy may be missing or out of date.", opens: true,
    desc: "Nothing is lost - MailFathom keeps the draft - but another mail app may not show it as it stands here." },
  { kind: "info", name: "Information - a new message", title: "Anna Kowalska", body: "Re: Contract addendum - signatures", opens: true,
    desc: "Raised with a new notification. The card is the notification on the screen: pressed anywhere, it opens the message and marks it read." },
  { kind: "warning", name: "Warning", title: "Cancelled", body: "The operation was aborted before saving - nothing was changed.", action: "",
    desc: "A side effect or an interruption. Not an error, but the user needs to know." },
  { kind: "info", name: "Information", title: "Index extended", body: "Search now covers the contents of PDF attachments.", action: "",
    desc: "A system message unrelated to the last click." },
  { kind: "loading", name: "Running operation", title: "Packing attachments…", body: "14 files → attachments.zip", action: "",
    desc: "Does not vanish on its own. Closing it asks for confirmation and aborts the operation." },
];

MailFathomDesign.data.toastsBurst = [
  { kind: "neutral", title: "Archived 3 threads", action: "Undo" },
  { kind: "success", title: "Message sent", body: "To: team@lexmar.example" },
  { kind: "info", title: "Index extended", body: "Search now covers the contents of PDF attachments." },
  { kind: "warning", title: "2 recipients skipped", body: "Addresses outside the domain need legal approval." },
  { kind: "error", title: "Not sent - it waits in the Outbox", body: "“Re: Contract addendum - signatures” is in the Outbox of Nordwind · work. Send it again or edit it there.", opens: true },
];

MailFathomDesign.data.toastsMessages = {
  restored: { title: "Restored 3 threads", body: "The threads are back in the inbox." },
  cancelled: { title: "Cancelled", body: "The operation was aborted before saving - nothing was changed." },
  cancelAskText: "The operation is running. Closing the toast aborts it - partial results will not be saved.",
};

MailFathomDesign.data.toastsTasks = {
  archive: {
    cancelText: "The ZIP archive is being prepared. Aborting discards the partially packed file.",
    doneTitle: "Archive ready", doneBody: "attachments.zip - 14 files", ms: 6000,
  },
  mailboxSync: {
    title: "Syncing the mailbox…", body: "1,240 of 3,800 messages",
    cancelText: "The sync is running. Aborting stops it where it is - downloaded messages stay, the rest has to be fetched again.",
    doneTitle: "Mailbox synced", doneBody: "3,800 messages, 12 new threads", ms: 5200,
  },
  sendFailure: {
    title: "Sending message…", body: "To: anna.kowalska@lexmar.example",
    cancelText: "The message is being sent. Aborting stops it here, so it is not sent.",
    doneKind: "error", doneTitle: "Not sent - it waits in the Outbox",
    doneBody: "“Re: Contract addendum - signatures” is in the Outbox of Nordwind · work. Send it again or edit it there.",
    doneOpens: true, ms: 3400,
  },
};

MailFathomDesign.data.toastsBlockingOperations = {
  migration: {
    title: "Mailbox migration in progress",
    body: "Moving 3,800 messages and 214 attachments to the new server. The app is locked so nothing is lost halfway.",
    finishedTitle: "Migration finished", finishedBody: "3,800 messages on the new server, 0 errors.",
    abortedBody: "Messages already moved stay on the new server, the rest is unchanged.",
  },
  indexRebuild: {
    title: "Rebuilding the index",
    body: "The search index is being rebuilt. The time depends on the number of messages.",
    finishedTitle: "Index rebuilt", finishedBody: "Search now runs on a fresh index.",
    abortedBody: "The index was restored to its previous version.",
  },
};

MailFathomDesign.data.toastsBlockSpecs = [
  { k: "When", v: "Only operations where aborting halfway corrupts data - migration, bulk change, export." },
  { k: "Exit", v: "Background clicks and Esc do nothing. The only way out is Cancel → “Really abort?”." },
  { k: "After aborting", v: "The overlay disappears and a warning toast says what was saved and what was not." },
];
