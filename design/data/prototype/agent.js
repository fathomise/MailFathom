// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

{
  /* ——— Agent answer blocks ———
     The agent renders the SAME catalogue as Discover (see “MailFathom Result Blocks”):
     ANSWER, EVIDENCE, TIMELINE, FACT TABLE, PEOPLE, THREAD STATE, ATTACHMENTS, DRAFT, SUGGESTED ACTION.
     What differs in a conversation is that an actionable block carries real controls and therefore
     three phases: pending (proposal + controls), accepted (result line), declined (one quiet struck line). */

  const atToday = (h, m) => { const d = new Date(); d.setHours(h, m || 0, 0, 0); return d.getTime(); };

  const BK_TIMELINE_TODAY = {
    type: "timeline", meta: "today · 5 items",
    items: [
      { date: "09:00", title: "Fabrikam rollout review", detail: "Piotr Zieliński · 45 min" },
      { date: "11:00", title: "Call with Wrona Law Office", detail: "addendum wording · 30 min" },
      { date: "13:00", title: "Call with Anna Kowalska", detail: "Contoso · 30 min" },
      { date: "15:00", title: "Rollout status with Piotr", detail: "until 16:30" },
      { date: "all day", title: "Decision: Contoso addendum", detail: "deadline today" },
    ],
  };

  const BK_DRAFT_CONTOSO = {
    type: "draft", meta: "reply · 96 words",
    to: "Anna Kowalska", subject: "Re: Contract addendum - signatures",
    paras: [
      "Thank you for the addendum. We accept shortening the response time for critical incidents to 2 hours on business days.",
      "In return we ask for an upper cap on CPI indexation of 5% per year. At the 4.1% forecast this keeps the cost predictable for both sides through 2027.",
      "If the cap is acceptable, we are ready to close the decision by 28 August.",
    ],
    done: "Sent into “Contract addendum - signatures”",
    nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "contoso" }],
  };

  /* REAL EXAMPLE — a scripted conversation played back in real time. Content never arrives token
     by token: a widget appears only once it is whole. Between arrivals the status line says what
     the agent is doing right now, and it changes as the work moves on.
     Step kinds - u: user turn · s: status · b: new agent bubble · t: text appended · k: block appended. */
  const DEMO_CONV_ID = "c-real";
  const DEMO_SCRIPT = [
    { d: 300, u: "Anna Kowalska is pressing on the addendum. Go through the thread and get me a reply." },
    { d: 450, s: "Reading your request" },
    { d: 950, s: "Searching 3 accounts for the thread" },
    { d: 1150, b: { scope: "3 accounts · mail, calendar", text: "Found it - “Contract addendum - signatures”. 6 messages since 18 August, 3 attachments." } },
    { d: 600, s: "Reading 6 messages" },
    {
      d: 1700, b: { scope: "thread · 6 messages", text: "Here is where the thread actually stands." },
      k: {
        type: "threadstate", meta: "this thread",
        cols: [
          { label: "Agreed", items: [{ t: "2 h response time for critical incidents" }, { t: "Fee rises 8% from January" }] },
          { label: "Open questions", items: [{ t: "Cap on CPI indexation" }, { t: "Penalty for a missed response" }] },
          { label: "Commitments", items: [{ t: "You: counter-proposal by 28 August" }] },
        ],
      },
    },
    { d: 500, s: "Opening 3 attachments" },
    { d: 1400, s: "Checking them against the framework contract" },
    {
      d: 1500,
      k: {
        type: "evidence", meta: "3 sources",
        items: [
          { n: "1", source: "Contract addendum - signatures", badge: "mail", snippet: "“…indexation by the CPI published for the previous year…”", freshness: "yesterday · Anna Kowalska",
            why: "It is the wording on the table now - the newest message that states the indexation, and the one the reply answers.",
            without: "Without the thread itself I can only compare the two documents: the 2023 cap stands, but I cannot say the new draft drops it - that claim came from this message." },
          { n: "2", source: "CPI addendum 2023.pdf", badge: "attachment", snippet: "“Indexation shall not exceed 5% per year.”", freshness: "18 August · attachment",
            why: "It is the only source that puts the 5% cap in writing, so the whole reading turns on it.",
            without: "Without the 2023 addendum the cap rests on Anna's mail alone, which mentions it only indirectly - I would no longer call it agreed, only discussed." },
          { n: "3", source: "Framework contract.pdf", badge: "attachment", snippet: "“Response time 4 h, penalty 0.2% of the monthly fee.”", freshness: "March · attachment",
            why: "It carries the penalty clause, which is how I could say the shorter response time adds no new exposure.",
            without: "Without the framework contract the reading of the cap is unchanged, but I can no longer say the penalty stays at 0.2% - that figure came only from there." },
        ],
      },
    },
    { d: 600, s: "Weighing the 2023 cap against the new wording" },
    { d: 1500, b: { scope: "reading · 3 sources", text: "" } },
    { d: 700, p: "**Short version:** the cap you already agreed has quietly disappeared. The 2023 addendum says:\n\n> Indexation shall not exceed 5% per year.\n\nThe new draft only points at *the CPI published for the previous year*, with no ceiling on it at all." },
    { d: 1500, p: "**That matters more than the 8% headline does.** At the 4.1% forecast the first year costs you roughly the same either way - but with no cap, a repeat of 2022 would push the fee up by a fifth in one step, and nothing in the framework contract stops it." },
    { d: 800, s: "Checking the penalty in the framework contract" },
    { d: 1700, p: "The response time is the smaller trade:\n\n- **Critical incidents:** response drops from 4 h to 2 h\n- **Penalty:** stays at `0.2%` of the monthly fee - the same figure as in the framework contract\n\nSo no new exposure there." },
    { d: 700, s: "Putting my reading together" },
    { d: 1600, p: "## My reading\n\n1. Take the **8%**.\n2. Insist the **5% cap** comes back in writing.\n3. Leave the penalty alone.\n\nThat keeps the whole thing closable by **28 August**, the date you already promised Anna." },
    { d: 1900, u: "Accept the 8%, but keep the 5% cap. Draft it." },
    { d: 450, s: "Writing the draft" },
    { d: 1500, s: "Matching the tone to your earlier replies" },
    {
      d: 1900, b: { scope: "draft · from 6 messages and 3 attachments", text: "Draft is ready. Nothing goes out until you approve it." },
      k: Object.assign({}, BK_DRAFT_CONTOSO, { phase: "pending", cta: "Send it" }),
    },
    { d: 700, s: "Looking for what follows from this" },
    {
      d: 1400,
      k: {
        type: "task", meta: "tasks", phase: "pending",
        title: "Close the CPI cap decision with Anna Kowalska",
        due: "28 August", from: "Contract addendum - signatures", reminder: "2 days before",
        done: "Added to your task list",
        cta: "Add it",
      },
    },
    { d: 550, s: "Checking your calendar for a slot" },
    {
      d: 1400,
      k: {
        type: "event", meta: "calendar · work", phase: "pending",
        title: "Call with Anna Kowalska - CPI cap",
        when: "27 August 13:00-13:30", duration: "30 min", attendees: "You, Anna Kowalska", from: "Contract addendum - signatures",
        done: "Added to the calendar",
        cta: "Propose it",
        nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
      },
    },
    { d: 1000, b: { scope: "waiting for you", text: "" } },
    { d: 600, p: "Nothing has left your outbox. Three things above wait on you:\n\n- [ ] The reply to Anna\n- [ ] The task for the 28th\n- [ ] The half-hour with Anna\n\nApprove the draft and I will send it - the other two follow on their own." },
    { d: 1800, p: "If you would rather not concede the 8% at all, say so and I will rewrite the draft around holding January's fee flat. *Be aware* that the 28 August date then becomes unlikely - Anna's last two messages both pushed for a decision this week." },
    /* Work is done — three things the agent can do next, never more. */
    { d: 500, n: ["Rewrite the draft firmer", "What if Anna refuses the cap?", "Open the 2023 addendum"] },
  ];

  /* Fallback next steps for any reply that does not name its own. */
  const NEXT_DEFAULT = ["Turn this into a task", "Draft a reply", "Show me the sources"];

  const SEED_CONVS = () => [
    {
      id: "c-plan", title: "Plan my day", when: "Monday",
      msgs: [
        { role: "user", text: "Plan my day", ts: atToday(9, 41) },
        {
          role: "bot", ts: atToday(9, 41), scope: "today · 3 accounts",
          text: "Four fixed items today and one all-day decision. Two focus blocks cover what is actually at risk.",
          blocks: [
            BK_TIMELINE_TODAY,
            {
              type: "event", meta: "calendar · work", phase: "accepted",
              title: "Focus block: CPI cap counter-proposal",
              when: "today 10:00-11:00", duration: "1 h", attendees: "You", from: "Contract addendum - signatures",
              result: "Added to the calendar · 09:42",
              nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
            },
            {
              type: "event", meta: "calendar · work", phase: "pending",
              title: "Focus block: IT contract audit list",
              when: "today 16:00-17:00", duration: "1 h", attendees: "You", from: "IT contract audit",
              conflict: "overlaps Rollout status with Piotr (15:00-16:30)",
              done: "Added to the calendar",
              nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
            },
          ],
        },
        { role: "user", text: "Move the second one to tomorrow", ts: atToday(9, 44) },
        {
          role: "bot", ts: atToday(9, 44), scope: "calendar · tomorrow",
          text: "Moved. Tomorrow morning is free before the Fabrikam call.",
          blocks: [{
            type: "event", meta: "calendar · work", phase: "pending",
            title: "Focus block: IT contract audit list",
            when: "tomorrow 09:00-10:00", duration: "1 h", attendees: "You", from: "IT contract audit",
            done: "Added to the calendar",
            nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
          }],
        },
      ],
    },
    {
      id: "c-slip", title: "What slipped this week?", when: "yesterday",
      msgs: [
        { role: "user", text: "What slipped this week?", ts: atToday(8, 20) },
        {
          role: "bot", ts: atToday(8, 20), scope: "7 days · 3 accounts",
          text: "Two commitments have passed their date, and both of them are yours.",
          blocks: [
            {
              type: "threadstate", meta: "3 threads · 7 days",
              cols: [
                { label: "Agreed", items: [{ t: "2 h response time for critical incidents" }, { t: "Decision closes on 28 August" }] },
                { label: "Open questions", items: [{ t: "Upper cap on CPI indexation - no answer from Contoso" }] },
                { label: "Commitments", items: [
                  { t: "You: notice-period list to the audit - overdue since 02.09", late: true },
                  { t: "You: confirm the 12.11 date to Fabrikam - overdue since 04.09", late: true },
                ] },
              ],
            },
            {
              type: "task", meta: "tasks · from mail", phase: "pending",
              title: "Send the notice-period list to the audit",
              due: "tomorrow, 17:00", from: "IT contract audit", reminder: "09:00 tomorrow",
              done: "Added to tasks",
              nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "audyt1" }],
            },
            {
              type: "action", meta: "one step", phase: "declined",
              title: "Mark the Fabrikam thread as waiting on them",
              reason: "No reply from Tomasz Zieliński for 5 days.",
              effect: "Changes the thread state only - nothing is sent.",
              cta: "Mark it",
            },
          ],
        },
      ],
    },
    {
      id: "c-draft", title: "Draft a reply to Contoso", when: "11:02",
      msgs: [
        { role: "user", text: "Draft a reply to Contoso", ts: atToday(11, 2) },
        {
          role: "bot", ts: atToday(11, 2), scope: "thread · 6 messages",
          text: "We accept the 2 h SLA and ask for a 5% annual cap on indexation in return. Two sources carry the numbers.",
          blocks: [
            {
              type: "evidence", meta: "2 sources",
              items: [
                { n: "1", source: "SLA addendum.pdf", badge: "attachment", snippet: "“Response time drops from 4 to 2 hours, fee +8%…”", freshness: "current · 18.11.2025",
                  why: "It is the current version of the addendum, and the only source that pairs the 2-hour response time with the 8% fee.",
                  without: "Without the addendum I can still draft the reply, but the 8% and the 2 hours would come from Anna's mail rather than from a signed document." },
                { n: "2", source: "Private message - Marta Nowak", badge: "private source", private: true, freshness: "today",
                  why: "Its content is private to Marta, but its conclusion - that Finance accepts the 8% - is kept, which is why the draft can agree to the fee.",
                  without: "Without Marta's message the draft still stands, but I would no longer say Finance accepts the 8% - only that the addendum proposes it." },
              ],
            },
            Object.assign({ phase: "pending" }, BK_DRAFT_CONTOSO),
          ],
        },
        { role: "user", text: "Send it", ts: atToday(11, 7) },
        {
          role: "bot", ts: atToday(11, 7), scope: "thread · Contoso",
          text: "Sent. I will flag it if nothing comes back before the 28 August decision.",
          blocks: [Object.assign({ phase: "accepted", result: "Sent into “Contract addendum - signatures” · 11:07" }, BK_DRAFT_CONTOSO)],
        },
      ],
    },
    {
      id: "c-summary", title: "Summarise the addendum thread", when: "15:52",
      ctx: "thread “Contract addendum - signatures”",
      msgs: [
        { role: "user", text: "Summarise", ts: atToday(15, 52) },
        {
          role: "bot", ts: atToday(15, 52), scope: "this thread",
          text: "",
          blocks: [
            {
              /* The Agent's answer: the same block as Discover's, with a confidence the run reported and a verdict
                 that is always *cited* — the Agent names what it read, and nothing checked it. */
              type: "answer", confidence: "moderate",
              text: "Anna Kowalska accepts the 2 h response time for critical incidents and the 8% fee rise from January. She asks for your counter-proposal on the indexation cap by 28 August - the new draft names the CPI with no ceiling, so the cap is the one point still open.",
              cites: [
                { n: "1", source: "Contract addendum - signatures", badge: "mail", snippet: "“…we accept the 2 h response time and the new fee. Please send your proposal on the indexation cap by 28 August.”", freshness: "yesterday · Anna Kowalska" },
              ],
            },
          ],
        },
      ],
    },
    {
      id: "c-thread", title: "Contract addendum - what is agreed", when: "15:40",
      ctx: "thread “Contract addendum - signatures”",
      msgs: [
        { role: "user", text: "What has been agreed here so far?", ts: atToday(15, 12) },
        {
          role: "bot", ts: atToday(15, 12), scope: "thread · 6 messages", working: true,
          text: "",
          blocks: [
            {
              type: "threadstate", meta: "this thread",
              cols: [
                { label: "Agreed", items: [{ t: "2 h response time for critical incidents" }, { t: "Fee rises by 8% from January" }] },
                { label: "Open questions", items: [{ t: "Upper cap on CPI indexation" }] },
                { label: "Commitments", items: [{ t: "You: counter-proposal by 28 August" }] },
              ],
            },
            { type: "evidence", state: "loading", meta: "reading the attachments…" },
          ],
        },
      ],
    },
    {
      id: "c-states", title: "Blocks that did not come back whole", when: "now",
      msgs: [
        { role: "user", text: "Check the Fabrikam delivery dates", ts: atToday(7, 30) },
        {
          role: "bot", ts: atToday(7, 30), scope: "2 accounts · partial",
          text: "One account did not answer in time, so part of this is incomplete.",
          blocks: [
            { type: "event", state: "loading", meta: "checking the calendar…", phase: "pending", title: "Fabrikam delivery date confirmation" },
            {
              type: "task", state: "error", meta: "tasks", phase: "pending",
              title: "Confirm the 12.11 date to Fabrikam",
              errorText: "The task list did not answer.",
            },
            { type: "draft", state: "empty", meta: "reply", phase: "pending", to: "Tomasz Zieliński", title: "Reply to Fabrikam", emptyText: "Nothing to draft - the last message in the thread is yours." },
            {
              type: "evidence", state: "partial", meta: "1 of 3 sources",
              items: [{ n: "1", source: "Q4 delivery dates", badge: "mail", snippet: "“Delivery moved to 12.11…”", freshness: "yesterday · Tomasz Zieliński" }],
              partialNote: "Fabrikam’s archive is still being read - more sources may appear.",
            },
            {
              type: "action", meta: "one step", phase: "accepted", failed: true,
              title: "Label the Fabrikam thread with the 12.11 date",
              effect: "Adds a label to the thread.",
              cta: "Add it",
              result: "Could not add it - the mail server rejected the write · 07:31",
            },
          ],
        },
      ],
    },
    {
      id: "c-invoice", title: "Invoice 08/2026 - what is missing", when: "Monday", archived: true,
      msgs: [
        { role: "user", text: "What is missing on invoice 08/2026?", ts: atToday(10, 5) },
        {
          role: "bot", ts: atToday(10, 5), scope: "1 account · accounting",
          text: "The invoice has no purchase-order number, which is why accounting bounced it.",
          blocks: [{
            type: "action", meta: "one step", phase: "accepted",
            title: "Ask Marek Wiśniewski for the PO number",
            effect: "Sends a one-line question into the invoice thread.",
            cta: "Ask him", result: "Sent into “Invoice 08/2026” · 10:06",
            nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "faktura1" }],
          }],
        },
      ],
    },
  ];

  const AGENT_REPLIES = [
    {
      match: /(plan|schedule).*(day)|plan my day/i,
      scope: "calendar · 14 new messages",
      text: "Two meetings today and one deadline tomorrow. One focus block would cover what is actually at risk.",
      bullets: [],
      blocks: [
        BK_TIMELINE_TODAY,
        {
          type: "event", meta: "calendar · work", phase: "pending",
          title: "Focus block: CPI cap counter-proposal",
          when: "today 16:00-17:00", duration: "1 h", attendees: "You", from: "Contract addendum - signatures",
          conflict: "overlaps Rollout status with Piotr (15:00-16:30)",
          done: "Added to the calendar",
          nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
        },
      ],
    },
    {
      match: /slip|missed|forgot|overdue/i,
      scope: "7 days · 3 accounts",
      text: "Three things have been waiting on you for more than two days - the audit list is the one with a date on it.",
      bullets: [],
      blocks: [
        {
          type: "task", meta: "tasks · from mail", phase: "pending",
          title: "Send the notice-period list to the audit",
          due: "tomorrow, 17:00", from: "IT contract audit", reminder: "09:00 tomorrow",
          done: "Added to tasks",
          nav: [{ label: "Open the thread", icon: "forum", kind: "thread", tid: "audyt1" }],
        },
        {
          type: "action", meta: "one step", phase: "pending",
          title: "Mark the Fabrikam thread as waiting on them",
          reason: "No reply from Tomasz Zieliński for 5 days.",
          effect: "Changes the thread state only - nothing is sent.",
          cta: "Mark it", done: "Marked as waiting on them",
          nav: [{ label: "Open the Fabrikam thread", icon: "forum", kind: "thread", tid: "fabrikam1" }],
        },
      ],
    },
    {
      match: /contoso|addendum|cpi|reply|write|draft/i,
      scope: "thread: Contract addendum - 6 messages",
      text: "We accept the 2 h SLA and ask for a 5% annual cap on indexation in return. The draft is ready for review.",
      bullets: [],
      blocks: [Object.assign({ phase: "pending" }, BK_DRAFT_CONTOSO)],
      sources: [
        { n: "1", title: "Contract addendum - signatures", snippet: "“Response time drops from 4 to 2 hours, fee +8%…”", meta: "18.11.2025 · SLA addendum.pdf", tid: "contoso" },
        { n: "2", title: "CPI calculation 2027", snippet: "“A 4.1% CPI forecast adds €10,542…”", meta: "yesterday · Marta Nowak", tid: "marta" },
      ],
    },
  ];

  const AGENT_FALLBACK = {
    scope: "all your mail",
    text: "I went through the correspondence in that scope. I can draft a reply, block time in the calendar or show the threads behind it - tell me what should happen next.",
    bullets: [],
    blocks: [{
      type: "action", meta: "one step", phase: "pending",
      title: "Draft a reply to the oldest unanswered thread",
      reason: "Three threads have been waiting on you for more than two days.",
      effect: "Writes a draft into the conversation - you approve it before it is sent.",
      cta: "Write it", done: "Draft prepared",
    }],
  };

  const AGENT_DRAFT_HTML = '<div id="ai-draft-block" style="border-left:3px solid var(--accent);padding-left:14px;margin-left:-14px"><p style="margin:0 0 12px 0">Hello,</p><p style="margin:0 0 12px 0">thank you for the addendum. We accept shortening the response time for critical incidents to 2 hours on business days.</p><p style="margin:0 0 12px 0">At the same time we ask for an upper cap on CPI indexation of 5% per year. At the 4.1% forecast this gives both sides a predictable cost through 2027.</p><p style="margin:0">If the cap is acceptable, we are ready to close the decision by 28 August.</p></div><p style="margin:12px 0 0 0">Best regards,<br />Karolina Kowalska</p>';

  Object.assign(MailFathomDesign.data, {
    timelineToday: BK_TIMELINE_TODAY,
    draftContoso: BK_DRAFT_CONTOSO,
    demoConversationId: DEMO_CONV_ID,
    demoScript: DEMO_SCRIPT,
    nextDefault: NEXT_DEFAULT,
    seedConversations: SEED_CONVS,
    agentReplies: AGENT_REPLIES,
    agentFallback: AGENT_FALLBACK,
    agentDraftHtml: AGENT_DRAFT_HTML,
  });
}

// The chips that open a finished conversation from an empty agent screen.
MailFathomDesign.data.starterChips = [
  { label: "Plan my day", id: "c-plan" },
  { label: "What slipped this week?", id: "c-slip" },
  { label: "Draft a reply to Contoso", id: "c-draft" },
];

// What the agent answers when a proposed time is turned down.
MailFathomDesign.data.anotherTimeReply = {
  scope: "calendar · next free slot",
  text: "The next clear hour is tomorrow morning, before the Fabrikam call.",
  when: "tomorrow 09:00-10:00",
};

// What the agent's calendar block puts on the calendar once accepted.
MailFathomDesign.data.agentScheduledEvents = [
  { day: "27", time: "10:00", h: 10, t: "Reply to Marta - calculation", kind: "meeting", src: "agent block" },
  { day: "27", time: "16:00", h: 16, t: "CPI cap counter-proposal", kind: "meeting", src: "agent block" },
];

// What the agent offers for a selection in a draft. A rewrite widens the selection to the sentences it touches,
// so each entry is keyed by one sentence of a seeded draft and carries one version per preset.
MailFathomDesign.data.draftRewrites = {
  "Thank you for the addendum.": {
    shorter: "Thanks for the addendum.",
    formal: "Thank you for sending the addendum.",
    softer: "Thank you for the addendum, and for turning it round so quickly.",
    firmer: "We have reviewed the addendum.",
  },
  "We accept shortening the response time for critical incidents to 2 hours on business days.": {
    shorter: "We accept a 2-hour response time for critical incidents.",
    formal: "We accept the reduction of the response time for critical incidents to 2 hours on business days.",
    softer: "We are happy to accept the 2-hour response time for critical incidents on business days.",
    firmer: "We accept the 2-hour response time for critical incidents on business days, and no further change to it.",
  },
  "In return we ask for an upper cap on CPI indexation of 5% per year.": {
    shorter: "In return, we ask for a 5% annual cap on CPI indexation.",
    formal: "In return, we request an upper limit on CPI indexation of 5% per annum.",
    softer: "In return, we would ask you to consider a 5% annual cap on CPI indexation.",
    firmer: "In return, CPI indexation has to be capped at 5% per year.",
  },
  "At the 4.1% forecast this keeps the cost predictable for both sides through 2027.": {
    shorter: "That keeps 2027 predictable for both sides.",
    formal: "Given the 4.1% forecast, this ensures predictable costs for both parties through 2027.",
    softer: "With the 4.1% forecast, we think this keeps things predictable for both of us through 2027.",
    firmer: "At the 4.1% forecast the cap costs you nothing, and it is the condition for our signature.",
  },
  "If the cap is acceptable, we are ready to close the decision by 28 August.": {
    shorter: "With the cap agreed, we can close by 28 August.",
    formal: "Should the cap be acceptable, we are prepared to finalise the decision by 28 August.",
    softer: "If the cap works for you, we would be glad to close this by 28 August.",
    firmer: "With the cap in writing, we will close the decision by 28 August.",
  },
};

// The presets a draft selection offers, and the words in a typed instruction that pick one of them.
MailFathomDesign.data.rewritePresets = [
  { key: "shorter", label: "Shorter", icon: "short_text", words: /short|brief|concise|cut|trim/i },
  { key: "formal", label: "More formal", icon: "work", words: /formal|polite|official|professional/i },
  { key: "softer", label: "Softer", icon: "sentiment_satisfied", words: /soft|warm|friend|gentle|kind/i },
  { key: "firmer", label: "Firmer", icon: "front_hand", words: /firm|strong|direct|insist|clear/i },
];

// What the agent answers about a selected passage, in place, without moving the conversation.
MailFathomDesign.data.selectionReplies = {
  explain: {
    label: "Explain",
    text: "In plain terms: this turns on the 2023 addendum. The 5% cap was agreed there, and the new wording drops it without saying so - accepting it as written gives the cap up.",
  },
  check: {
    label: "Check against the sources",
    text: "Supported by 2 of the 3 sources - the 2023 addendum says it in so many words, and nothing later in the thread changes it. Anna's last message only refers to it indirectly.",
  },
  task: { due: "Friday, 17:00", reminder: "the day before, 09:00" },
};

// The alternatives the agent offers for one field of an event or a task proposal, each with its reason.
MailFathomDesign.data.fieldAlternatives = {
  When: [
    { v: "tomorrow 09:00-09:30", why: "first free half-hour" },
    { v: "tomorrow 14:00-14:30", why: "after the Fabrikam call" },
    { v: "Thursday 11:00-11:30", why: "both calendars free" },
  ],
  Duration: [
    { v: "15 min", why: "one open question" },
    { v: "45 min", why: "room for the penalty clause" },
  ],
  Due: [
    { v: "today, 17:00", why: "before the end of the day" },
    { v: "Friday, 17:00", why: "end of the week" },
    { v: "26 August", why: "two days before the deadline" },
  ],
  Reminder: [
    { v: "the day before, 09:00", why: "your usual" },
    { v: "1 hour before", why: "short notice" },
    { v: "none", why: "" },
  ],
};

// What a selection of people turns into: a draft to them, or a meeting with them.
MailFathomDesign.data.peopleActions = {
  draft: {
    subject: "Contoso 2027 - where we stand",
    paras: [
      "A short update before tomorrow's decision on the Contoso 2027 terms.",
      "We accept the 2-hour response time and the 8% fee, and we are asking for the 5% cap on indexation to be restated in the addendum. The legal opinion supports that.",
      "If anything in this does not match what you have, tell me today.",
    ],
  },
  invite: { title: "Contoso 2027 - decision call", when: "Thursday 11:00-11:30", duration: "30 min" },
};

// Two more real examples, each played back like the first one and each showing a different part of the agent:
// a meeting put together from people and a timeline, and a document check before signing.
MailFathomDesign.data.moreDemos = [
  {
    id: "c-real-rollout", label: "Real example · go/no-go", title: "Real example - Fabrikam go/no-go",
    script: [
      { d: 300, u: "Get the right people together for the Fabrikam rollout go/no-go this week." },
      { d: 450, s: "Reading your request" },
      { d: 900, s: "Finding the rollout threads and who is in them" },
      { d: 1300, b: { scope: "4 threads · 2 accounts", text: "Four threads carry the rollout. Here is where it stands against the plan." },
        k: {
          type: "timeline", meta: "Fabrikam rollout · 5 milestones",
          periodNote: "Everything before the go/no-go is done; everything after it waits on that one decision.",
          items: [
            { date: "02.09", title: "Test environment handed over", detail: "done · Piotr Zieliński" },
            { date: "09.09", title: "Load test passed", detail: "done · 1,200 users" },
            { date: "16.09", title: "Data migration rehearsal", detail: "done · 2 h 40 min" },
            { date: "this week", title: "Go/no-go decision", detail: "not scheduled yet" },
            { date: "12.11", title: "Delivery date", detail: "Fabrikam asked to confirm" },
          ],
        } },
      { d: 800, s: "Checking who decided what in those threads" },
      { d: 1500, k: {
          type: "people", meta: "5 people · from the rollout threads",
          items: [
            { initials: "TZ", name: "Tomasz Zieliński", role: "Fabrikam · project manager", last: "today" },
            { initials: "PZ", name: "Piotr Zieliński", role: "Delivery lead · owns the plan", last: "yesterday" },
            { initials: "EL", name: "Ewa Lis", role: "QA · signed off the load test", last: "09.09" },
            { initials: "MN", name: "Marta Nowak", role: "Finance · approves the change budget", last: "Monday" },
            { initials: "KD", name: "Kamil Duda", role: "Fabrikam · IT operations", last: "16.09" },
          ],
        } },
      { d: 700, p: "**Four of them decide, one is optional.** Tomasz and Kamil carry Fabrikam's side, Piotr owns the plan and Ewa signed off the tests. Marta only needs to be there if the date moves - the budget is already approved." },
      { d: 900, s: "Looking for a slot in 4 calendars" },
      { d: 1600, k: {
          type: "event", meta: "calendar · work", phase: "pending",
          title: "Fabrikam rollout - go/no-go",
          when: "Thursday 15:00-15:45", duration: "45 min", attendees: "You, Tomasz Zieliński, Kamil Duda, Piotr Zieliński, Ewa Lis",
          from: "Rollout plan - Fabrikam Q4",
          conflict: "Kamil Duda is out until 15:30 on Thursday - the agent can only see that he is busy",
          done: "Invitations sent",
          nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }],
        } },
      { d: 700, s: "Writing down how I got there" },
      { d: 1300, k: {
          type: "trace", meta: "how this answer was built · 4 steps",
          items: [
            { step: "Searched 2 accounts for the rollout threads", detail: "4 threads · 37 messages", why: "The rollout is discussed in both your work and your shared delivery mailbox, so neither alone has the whole picture." },
            { step: "Read the milestones from the plan and the test reports", detail: "plan v4, 2 reports", why: "A go/no-go is only meaningful against what is done, so the timeline comes before the people." },
            { step: "Picked the people who decided something", detail: "5 of 11 participants", why: "Six people only replied or were copied. The five here approved, signed off or own a part of the plan." },
            { step: "Checked 4 calendars for 45 minutes this week", detail: "1 slot, with a conflict", why: "Thursday afternoon is the only time all four deciders are in; Kamil's calendar shows him busy until 15:30, which I cannot see past." },
          ],
        } },
      { d: 500, n: ["Move it to after 15:30", "Draft the agenda", "Who has not replied in the threads?"] },
    ],
  },
  {
    id: "c-real-sign", label: "Real example · safe to sign?", title: "Real example - is it safe to sign",
    script: [
      { d: 300, u: "Marta sent a new calculation. What changed since last week, and is it safe to sign the Contoso addendum?" },
      { d: 450, s: "Reading your request" },
      { d: 900, s: "Opening the two versions of the calculation" },
      { d: 1400, b: { scope: "2 versions · 3 documents", text: "Three documents decide this. Each can be summarised or compared with the version before it." },
        k: {
          type: "attachments", meta: "3 documents",
          items: [
            { name: "Calculation_2027.xlsx", icon: "table_chart", meta: "yesterday · Marta Nowak · version 2",
              summary: "Two sheets. The 2027 cost at the 4.1% CPI forecast is €16,190 with or without the cap; at a 2022-style 14% it is €17,730 uncapped against €16,330 capped.",
              changes: "Against last week's version: the 14% scenario and the capped column are new, and the fee base moved from €1,200 to €1,296. The forecast row is unchanged." },
            { name: "Addendum_v4_draft.docx", icon: "description", meta: "today · Anna Kowalska · version 4",
              summary: "Two pages. The 2-hour response time, the 8% fee from January, and indexation by the CPI - with no ceiling stated anywhere.",
              changes: "Against version 3: only the effective date moved, from 1 December to 1 January. The indexation clause is word for word the same - the cap is still missing." },
            { name: "Legal opinion - indexation.docx", icon: "description", meta: "05.09 · Wrona Law Office",
              summary: "Three pages. Dropping the cap is a material change to the 2023 terms and needs both signatures; the opinion recommends restating the 5% cap in the addendum itself." },
          ],
        } },
      { d: 800, s: "Putting the numbers side by side" },
      { d: 1500, k: {
          type: "facttable", meta: "2027 cost · from the calculation",
          cols: ["Scenario", "No cap", "5% cap", "Difference"],
          rows: [
            ["CPI 4.1% (forecast)", "€16,190", "€16,190", "€0"],
            ["CPI 8%", "€16,790", "€16,330", "€460"],
            ["CPI 14% (2022 repeat)", "€17,730", "€16,330", "€1,400"],
          ],
        } },
      { d: 900, s: "Checking the claim against the sources" },
      { d: 1400, k: {
          type: "evidence", meta: "3 sources",
          items: [
            { n: "1", source: "Addendum_v4_draft.docx", badge: "attachment", snippet: "“…indexed annually by the CPI published for the previous year.”", freshness: "today · Anna Kowalska",
              why: "It is the text you would sign, and the clause in it is where the cap is missing.",
              without: "Without the draft itself I can only say the cap was discussed, not that it is missing from what you would sign." },
            { n: "2", source: "Calculation_2027.xlsx", badge: "attachment", snippet: "“CPI 14%: €17,730 uncapped / €16,330 capped”", freshness: "yesterday · Marta Nowak",
              why: "It is the only source that prices the missing cap - €1,400 in a 2022-style year.",
              without: "Without Marta's calculation the risk is real but unpriced - I could not put a number on it." },
            { n: "3", source: "Legal opinion - indexation.docx", badge: "attachment", snippet: "“…recommends restating the cap in the addendum itself.”", freshness: "05.09 · Wrona Law Office",
              why: "It turns the missing cap from a commercial point into a legal one - both signatures are needed to drop it.",
              without: "Without the opinion the advice is the same, but it rests on the numbers alone rather than on the 2023 terms." },
          ],
        } },
      { d: 700, b: { scope: "my reading", text: "" } },
      { d: 600, p: "**Not as it stands.** Nothing in version 4 changed the indexation clause, so the 5% cap is still missing. At the forecast it costs nothing; in a year like 2022 it costs **€1,400**." },
      { d: 1400, p: "Sign once the cap is back in the clause - one sentence, which the legal opinion already words for you." },
      { d: 700, s: "Looking for what follows from this" },
      { d: 1300, k: {
          type: "task", meta: "tasks · from mail", phase: "pending",
          title: "Ask Anna to restate the 5% cap in the addendum before signing",
          due: "tomorrow, 12:00", from: "Re: Addendum v4 - for signature", reminder: "the day before, 09:00",
          done: "Added to your task list",
        } },
      { d: 500, n: ["Draft the sentence for Anna", "What if CPI stays at 4%?", "Open the legal opinion"] },
    ],
  },
];
