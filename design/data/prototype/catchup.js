// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The catch-up: what Start shows the first time the reader opens it after an absence, as a run of cards they step
// through. Two things start it: more than two days since the reader last opened MailFathom, or an all-day *Holiday*
// (or *Out of office*) on their own calendar that ended since. It stays on Start for 24 hours from the moment it is
// first opened, or for good once it is saved. The absence is 11 to 26 August; today is Thursday 27 August.
//
// Every item on a card can be pressed, and every sentence selected, for the quick actions its kind carries
// (`MailFathomDesign.data.fathom.itemActions`). `more` is what *Give me more* and *Context* answer with, `short` what
// *Summarize* answers with, and `pl` what *Translate* answers with; an item without them gets the action's own line.
MailFathomDesign.data.catchup = {
  range: "11-26 August",
  days: 16,
  reasons: {
    away: { label: "16 days away", line: "You were last here on Monday 10 August, 16 days ago." },
    holiday: { label: "Holiday, 11-26 August", line: "Your calendar said Holiday from 11 to 26 August." },
  },
  slides: [
    {
      id: "hello", layout: "stats", tone: "accent", icon: "waving_hand",
      eyebrow: "While you were away",
      title: "Welcome back, Karolina",
      lede: "Here is what happened, in eight cards. Press anything for more.",
      items: [
        { id: "s-mail", kind: "stat", n: "284", label: "messages arrived", go: "mail",
          short: "284 messages in 37 conversations. 221 of them need nothing from you.",
          more: "Most came in the second week: the Q4 rollout thread alone grew by 96 messages after Piotr shared the new plan on 17 August." },
        { id: "s-conv", kind: "stat", n: "37", label: "conversations", go: "mail",
          short: "37 conversations moved. 3 wait on you, 9 settled, the rest are newsletters and notifications.",
          more: "Four of them are new: the audit register, the office move, the Fabrikam billing change and a Contoso renewal question." },
        { id: "s-meet", kind: "stat", n: "6", label: "meetings went ahead", go: "cal",
          short: "Six meetings went ahead without you; two left notes in your mail.",
          more: "The rollout review on 21 August moved phase three to 12 November. The other notes are in Marta's finance sync." },
        { id: "s-need", kind: "stat", n: "3", label: "still need you", go: "waiting",
          short: "Anna, Tomasz and the audit team are waiting on you.",
          more: "Anna's is the oldest and the most urgent: the Contoso decision is due tomorrow." },
      ],
    },
    {
      id: "needs", layout: "list", tone: "accent", icon: "pending_actions",
      eyebrow: "Still needs you",
      title: "Three things wait on you",
      lede: "Everything else either settled or can wait.",
      items: [
        { id: "n-contoso", kind: "thread", icon: "forum", label: "The CPI cap counter-proposal", sub: "Anna Kowalska, Contoso · asked twice, last on 24 August", badge: "due tomorrow", tid: "contoso",
          short: "Contoso wants indexation without a cap; you have not answered since 7 August.",
          more: "Anna asked again on 24 August and copied her legal team. The draft on Start already proposes a 5% cap.",
          pl: "Kontrpropozycja z limitem CPI. Anna Kowalska pytała dwa razy, ostatnio 24 sierpnia." },
        { id: "n-fabrikam", kind: "thread", icon: "forum", label: "A Q4 delivery date for Fabrikam", sub: "Tomasz Bąk, Fabrikam · since 19 August", badge: "02.09", tid: "fabrikam1",
          short: "Fabrikam needs a confirmed Q4 date; phase three of the rollout depends on it.",
          more: "Tomasz offered 14 or 21 October. Piotr's plan from 17 August only works with the 14th.",
          pl: "Termin dostawy Q4 dla Fabrikam. Tomasz Bąk czeka od 19 sierpnia." },
        { id: "n-audit", kind: "thread", icon: "forum", label: "The IT contract register for the audit", sub: "Audit · requested 20 August", badge: "03.09", tid: "audyt1",
          short: "The audit wants every IT contract with its addenda by 3 September.",
          more: "Twelve contracts are already in the shared folder; the Contoso addendum is the one missing.",
          pl: "Rejestr umów IT do audytu, wymagany do 3 września." },
      ],
    },
    {
      id: "settled", layout: "list", tone: "ok", icon: "task_alt",
      eyebrow: "Settled without you",
      title: "Nine threads closed themselves",
      lede: "Nothing to do here. These are the four you would have been asked about.",
      items: [
        { id: "r-calc", kind: "settled", icon: "check_circle", label: "Marta approved the August calculation", sub: "Finance · 19 August", tid: "marta",
          short: "Marta approved it after Jan answered her two questions." },
        { id: "r-rollout", kind: "settled", icon: "check_circle", label: "The rollout review moved phase three to 12 November", sub: "Piotr Zieliński · 21 August", tid: "piotr",
          short: "Agreed in the meeting and confirmed by Piotr in the thread the same day." },
        { id: "r-invoice", kind: "settled", icon: "check_circle", label: "Adventure Works paid invoice 07/2026", sub: "Payments · 14 August", tid: "faktura1",
          short: "Paid in full, three days early." },
        { id: "r-office", kind: "settled", icon: "check_circle", label: "The office move is postponed to October", sub: "HR · 18 August", tid: "hr1",
          short: "HR moved it; nothing is asked of you before the new date." },
      ],
    },
    {
      id: "risks", layout: "list", tone: "warn", icon: "warning",
      eyebrow: "Worth a careful look",
      title: "Two things look risky",
      lede: "Raised by rules from the messages themselves, not by a guess.",
      items: [
        { id: "k-bank", kind: "risk", level: "danger", icon: "gpp_maybe", label: "An invoice asks to pay a new bank account", sub: "From fabrikam-billing.co, not fabrikam.com · 22 August", tid: "fabrikam1",
          short: "The sender's domain is one letter group away from Fabrikam's, and the account number changed.",
          more: "Fabrikam's real billing address wrote last on 3 July from fabrikam.com. Confirm by phone with Tomasz before anything is paid." },
        { id: "k-contoso", kind: "risk", level: "warn", icon: "schedule", label: "The Contoso decision is due tomorrow", sub: "Asked twice, still unanswered", tid: "contoso",
          short: "Missing it means the 2027 terms renew at Contoso's proposal." },
      ],
    },
    {
      id: "people", layout: "bars", tone: "accent", icon: "group",
      eyebrow: "Who wrote most",
      title: "Piotr was your top correspondent",
      lede: "41 messages, nearly all about the rollout.",
      items: [
        { id: "p-piotr", kind: "person", initials: "PZ", label: "Piotr Zieliński", sub: "Delivery", n: 41, pid: "piotr",
          short: "Piotr ran the rollout planning while you were away and expects a go or no-go on phase three.",
          more: "You last met on 7 August. He is in today's 15:00 rollout status." },
        { id: "p-anna", kind: "person", initials: "AK", label: "Anna Kowalska", sub: "Contoso", n: 23, pid: "anna",
          short: "Anna wrote about the 2027 terms and is waiting for the counter-proposal.",
          more: "Contoso's procurement lead. Every one of her 23 messages is in the addendum thread." },
        { id: "p-tomasz", kind: "person", initials: "TB", label: "Tomasz Bąk", sub: "Fabrikam", n: 17, pid: "tomasz",
          short: "Tomasz wants a Q4 date and flagged the billing change himself on 24 August." },
        { id: "p-marta", kind: "person", initials: "MN", label: "Marta Nowak", sub: "Finance", n: 12, pid: "marta",
          short: "Marta closed the August calculation and needs nothing more." },
      ],
    },
    {
      id: "topics", layout: "chips", tone: "accent", icon: "category",
      eyebrow: "What it was about",
      title: "The rollout took a third of your mail",
      lede: "Topics read off subjects and threads by rules.",
      items: [
        { id: "t-rollout", kind: "topic", label: "Q4 rollout", n: 96, size: 3,
          short: "Phase two is confirmed for 12 November; phase three waits on Fabrikam's date." },
        { id: "t-contoso", kind: "topic", label: "Contoso addendum", n: 41, size: 2,
          short: "One open question: indexation with or without a cap." },
        { id: "t-invoices", kind: "topic", label: "Invoices", n: 33, size: 2,
          short: "Two paid, one to approve, one flagged as risky." },
        { id: "t-audit", kind: "topic", label: "Audit", n: 18, size: 1,
          short: "One request: the IT contract register by 3 September." },
        { id: "t-office", kind: "topic", label: "Office move", n: 11, size: 1,
          short: "Postponed to October." },
        { id: "t-news", kind: "topic", label: "Newsletters", n: 142, size: 1,
          short: "Nothing in them mentions you or your projects." },
      ],
    },
    {
      id: "archive", layout: "stats", tone: "neutral", icon: "inventory_2",
      eyebrow: "Safe to archive",
      title: "221 messages ask nothing of you",
      lede: "Newsletters, notifications and finished threads. One press files them all, and it can be undone.",
      cta: { id: "archive", label: "Archive all 221", icon: "archive" },
      items: [
        { id: "a-news", kind: "stat", n: "142", label: "newsletters", go: "mail",
          short: "From 11 senders; none of them mentions you." },
        { id: "a-notif", kind: "stat", n: "61", label: "notifications", go: "mail",
          short: "Calendar, Teams and the expense tool. All of them already acted on." },
        { id: "a-done", kind: "stat", n: "18", label: "finished threads", go: "mail",
          short: "Each ended with a thank-you or a confirmation." },
      ],
    },
    {
      id: "done", layout: "end", tone: "accent", icon: "flag",
      eyebrow: "That is all of it",
      title: "You are caught up",
      lede: "Three decisions are left, and they are on Start. This catch-up stays there for 24 hours, or for good once you save it.",
      cta: { id: "close", label: "Back to your day", icon: "arrow_forward" },
      items: [],
    },
  ],
  // Exporting writes a file on this device and nothing else: no model reads it and nothing is sent.
  exportFormats: [
    { id: "pdf", icon: "picture_as_pdf", label: "PDF, one page per card" },
    { id: "png", icon: "image", label: "Images, one per card" },
    { id: "md", icon: "description", label: "Text with links to the threads" },
  ],
};
