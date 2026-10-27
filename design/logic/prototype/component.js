// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

MailFathomDesign.artboards.prototype = (DCLogic, React) => {
  const {
    me: { name: ME, address: MY_MAIL },
    app: { version: APP_VERSION, build: APP_BUILD },
    avatars: AVATARS, contacts: CONTACTS, contactInsights: CONTACT_AI,
    threads: THREADS, earlierMessages, threadMailBodies, threadStateSources: STATE_SRC, replyTo: HDR_REPLY_TO, cc: HDR_CC,
    mailBodies, documentPreviews, composeDrafts: COMPOSE_DRAFTS, newMessageTo: NEW_MESSAGE_TO,
    accounts: ACCOUNTS, unifiedAccounts: UNIFIED_ACCOUNTS, mailboxes: MAILBOXES, defaultReplyTo: DEFAULT_REPLY_TO, mailAccounts: MAIL_ACCOUNTS,
    home: HOME, watchedQuestions: WATCHED, suggestedQuestions: SUGGESTED_QUESTIONS, sampleText: SAMPLE_TEXT,
    calendarBriefing: CALENDAR_BRIEFING, eventFromThread: EVENT_FROM_THREAD, taskCapacity: TASK_CAPACITY,
    starterChips: STARTER_CHIPS, anotherTimeReply: ANOTHER_TIME, agentScheduledEvents: AGENT_SCHEDULED,
    plans: PLANS,
    calendarDays: CAL_DAYS, proposedEvents: CAL_PROPOSED, focusBlocks: CAL_FOCUS, weekdayDates: DOW_DAY, tasks: TASKS,
    timelineToday: BK_TIMELINE_TODAY, draftContoso: BK_DRAFT_CONTOSO, demoConversationId: DEMO_CONV_ID, demoScript: DEMO_SCRIPT,
    nextDefault: NEXT_DEFAULT, seedConversations: SEED_CONVS, agentReplies: AGENT_REPLIES, agentFallback: AGENT_FALLBACK,
    agentDraftHtml: AGENT_DRAFT_HTML, draftRewrites: DRAFT_REWRITES, rewritePresets: REWRITE_PRESETS, selectionReplies: SEL_REPLIES,
    fieldAlternatives: FIELD_ALTS, peopleActions: PEOPLE_ACTS, moreDemos: MORE_DEMOS,
    notifications: NOTIFS, incomingNotifications: INCOMING, demoBatches: DEMO_BATCHES,
  } = MailFathomDesign.data;
  const { mdPlain, mdView, MDTXT } = MailFathomDesign.markdown;
  const { HTML_SHELL, embedHtml, htmlFallback } = MailFathomDesign.mailHtml;

  /* CLIENT STATES — INVENTORY (drawn in “client-states.dc.html”).
     Every state below is revealed there by the property state="<id>", at layout = telefon | fold | tablet | desktop | all
     and theme = light | dark | both; clicking a ledger row at the top of that file opens the same artboards.
     Tokens, notices, chips, buttons, skeleton shimmer and toast are this file's; the only new component is the
     radio list in “Kind of folder” (2.4a).
     DISCOVER
       d_refusal   1.1  question refused before a run - one line under the field: signed out, not permitted (neutral);
                        unavailable, unreadable answer (warning + Try again). The question stays in the field.
       d_cancel    1.2a cancelling (spinner, disabled “Cancelling…”) and cancel failed (warning, “Try stopping again”).
       d_ended     1.2b run ended - card takes the running bar's slot above arrived blocks; answer gains “cut short”.
                        Tones: finished (no card) · stopped by you · limit reached (period allowance with when-line,
                        run allowance) · try again (timed out, temporarily unavailable) · could not finish (failed,
                        gone, stopped) · not possible here (unavailable, retrieval refused).
       d_finished  1.2c completed - no card; “Read {n} messages” goes in the run line.
       d_coming    1.3a “still coming” dashed card trailing blocks: loading (shimmer), error, offline (+ Retry only here).
       d_empty     1.3b nothing composed (no action) · answer from a newer version · unknown block (dashed neutral).
       ev_opening  1.4a inspector opening - header from the citation, shimmer body, “Opening the source…”.
       ev_failed   1.4b read failures: unavailable, unreadable (warning); signed out, missing (neutral).
       ev_private  1.4c private source - lock badge + administrator remedy. A “not permitted” read failure renders this.
       ev_outdated 1.4d outdated stored copy - warning, then the quoted passage in a plain box; Open in mail stays.
       ev_kinds    1.4e whole-message citation · attachment file row · not openable from the client · text in an image.
       d_watched   1.5  watched questions on the start screen: changed, no change, paused (allowance spent), could
                        not refresh, and nothing watched yet; opening one runs it, × stops watching.
     MAIL
       m_verdict   2.1  sender verdict under the sender line: warning box (authenticated by another domain / nothing
                        authenticated), healthy as one quiet line.
       m_more      2.2a thread paging at its end: Read more · reading more · “That is the whole of this conversation.”
       m_partial   2.2b partial failure in place of the missing messages; head notes (more people, more messages).
       m_pane      2.2c whole pane: failed, offline, nothing you are allowed to see - “Nothing open” layout.
       m_folders   2.3a folder column: skeleton (text kept for screen readers), failed, offline (dimmed last tree),
                        no account.
       m_list      2.3b list: empty folder, nothing matches (+ Clear filters), partial failure strip, empty while failing.
       m_kind      2.4a New folder with “Kind of folder” first; a role hides name + inside and shows the hint.
       m_roles     2.4b tree roles Flagged (flag), Important (label_important), All mail (all_inbox), Outbox (outbox).
       m_search    2.5  described search: filter chips, “What this search is ranked by” chips, unused-phrase note,
                        “Reading what you wrote…”, words-only placeholder.
       m_pending   2.6  pending changes in the connection summary: indicator states, panel (sheet on phone) with
                        needs-decision, failed, did not reach deployment, pending count; tracking stopped; late toast.
       m_sync      2.7  how current the accounts are: green with nothing to open, or amber opening onto name + state.
     TASKS · CALENDAR · PEOPLE
       c_edit      3.1  event edited in place: All day, Starts / Ends, Save disabled with reason.
       t_layout    3.2  Lay out today: waiting, offer (Add to calendar / Leave it), nothing left, allowance spent,
                        unavailable (button hidden).
       t_proposed  3.3a proposed task (AI tag, Accept, Dismiss task), “No day” group, status line, partly refused.
       t_states    3.3b empty list, everything done, reading, reading more, failed.
       p_none      3.4a “Open somebody to read their page.” (fold and wider); status line on a person's page.
       p_states    3.4b empty address books, reading, failed, conversations reading / failed / none.
     AGENT
       a_steer     4.1  steering note during a run: Send reads “Steer”, tag “Joins at the next turn” → “Taken in”.
       a_failed    4.2a failed answer (error notice + Try again); not-sent line under the field, five reasons.
       a_read      4.2b conversation read failure (five reasons), history reading / failed / empty, refusal toast,
                        “Delete 3 conversations?”. */

  /* Shared shimmer for every loading skeleton. */
  const SHIM = "border-radius:6px;background:linear-gradient(90deg,var(--line) 0%,var(--line2) 42%,var(--line) 78%);background-size:260% 100%;animation:mfshim 1.05s linear infinite;";

  /* The AI block chrome, one family wherever a block stands — the Agent's (`bkView`) and Start's answer and steps.
     A card at radius 12, a header of icon tile + title + meta with the phase chip on the right, and a footer
     tray, flush with the card's edges, that holds the controls and the way out. `tone` is `settled`,
     `pending` (the accent edge, tile and tray) or `declined` (a dashed outline and nothing else).
     logic/ai-blocks.js keeps an identical copy for the catalogue artboard — change both together. */
  const BLK_ICON = { answer: "auto_awesome", timeline: "timeline", evidence: "fact_check", threadstate: "forum", facttable: "table_chart", attachments: "attach_file", people: "group", draft: "edit_note", event: "event", task: "task_alt", action: "bolt", trace: "route" };
  const BLK_PAD = (isMobile) => isMobile ? { y: 14, x: 14 } : { y: 16, x: 18 };
  const BLK = {
    card: (isMobile, tone) => {
      const p = BLK_PAD(isMobile);
      return "display:flex;flex-direction:column;gap:12px;min-width:0;border-radius:12px;padding:" + p.y + "px " + p.x + "px;" +
        (tone === "declined" ? "background:transparent;border:1px dashed var(--border2);"
          : "background:var(--panel);border:1px solid " + (tone === "pending" ? "var(--accent-line)" : "var(--line)") + ";box-shadow:0 1px 2px var(--sh-1),inset 0 1px 0 var(--inset);");
    },
    head: "display:flex;align-items:center;gap:10px;flex-wrap:wrap;min-height:26px",
    tile: (tone) => "flex:0 0 26px;width:26px;height:26px;display:inline-flex;align-items:center;justify-content:center;border-radius:8px;" +
      (tone === "pending" || tone === "accent" ? "background:var(--accent-soft);color:var(--accent-d);box-shadow:inset 0 0 0 1px var(--accent-line)"
        : tone === "declined" ? "background:transparent;color:var(--faint);box-shadow:inset 0 0 0 1px var(--line)"
        : "background:var(--sub);color:var(--text2);box-shadow:inset 0 0 0 1px var(--line2)"),
    glyph: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1",
    title: "font-size:13px;font-weight:600;color:var(--text);letter-spacing:-0.005em;white-space:nowrap",
    meta: "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--faint);min-width:0",
    hint: "margin-left:auto;font-size:12px;color:var(--muted)",
    chip: (kind) => "margin-left:auto;display:inline-flex;align-items:center;gap:5px;border-radius:999px;padding:3px 10px 3px 7px;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;white-space:nowrap;" +
      (kind === "pending" ? "color:var(--accent-d);background:var(--accent-soft);"
        : kind === "done" ? "color:var(--ok-text);background:var(--ok-soft);"
        : kind === "failed" ? "color:var(--warn-text);background:var(--warn-soft);"
        : "color:var(--muted);background:var(--sub);"),
    foot: (isMobile, tone) => {
      const p = BLK_PAD(isMobile);
      return "margin:2px -" + p.x + "px -" + p.y + "px -" + p.x + "px;padding:12px " + p.x + "px;border-radius:0 0 11px 11px;" +
        (isMobile ? "display:flex;flex-direction:column;gap:8px;" : "display:flex;align-items:center;gap:8px;flex-wrap:wrap;") +
        (tone === "pending" ? "background:var(--accent-soft);border-top:1px solid var(--accent-line);" : "background:transparent;border-top:1px solid var(--line2);");
    },
    btn: (isMobile, primary) => "display:inline-flex;align-items:center;justify-content:center;gap:7px;border-radius:9px;font-size:14px;white-space:nowrap;box-sizing:border-box;" +
      (isMobile ? "width:100%;min-height:44px;padding:0 16px;" : "min-height:36px;padding:0 15px;") +
      (primary ? "font-weight:600;color:var(--onaccent);background:var(--accent);border:1px solid var(--accent);box-shadow:inset 0 1px 0 rgba(255,255,255,0.16),0 1px 2px var(--sh-1);"
        : "font-weight:500;color:var(--text);background:var(--panel);border:1px solid var(--border2);box-shadow:0 1px 1px var(--sh-1);"),
    link: (isMobile) => "display:inline-flex;align-items:center;justify-content:center;gap:7px;border:1px solid var(--border2);border-radius:999px;background:transparent;color:var(--text2);font-size:13px;white-space:nowrap;box-sizing:border-box;" +
      (isMobile ? "width:100%;min-height:44px;padding:0 14px;" : "min-height:32px;padding:0 13px 0 11px;"),
    label: "font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--muted);letter-spacing:0.01em",
    /* A timeline is a rail of dots: across on the desktop, down the left edge on the phone. */
    tlItem: (isMobile, last) => isMobile
      ? "position:relative;display:flex;flex-direction:column;gap:2px;padding:0 0 " + (last ? 2 : 14) + "px 22px"
      : "position:relative;flex:1;min-width:120px;display:flex;flex-direction:column;gap:3px;padding:20px 16px 2px 0",
    tlLine: (isMobile, last) => last ? "display:none"
      : isMobile ? "position:absolute;left:4px;top:13px;bottom:-4px;width:1px;background:var(--border2)"
      : "position:absolute;left:0;right:0;top:4px;height:1px;background:var(--border2)",
    tlDot: (isMobile) => "position:absolute;left:0;top:" + (isMobile ? 4 : 0) + "px;width:9px;height:9px;box-sizing:border-box;border-radius:50%;background:var(--panel);border:2px solid var(--accent)",
    tlDate: "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--muted)",
    tlTitle: "font-size:13.5px;font-weight:600;line-height:1.35;color:var(--text);text-wrap:pretty",
    tlDetail: "font-size:12px;color:var(--faint);text-wrap:pretty",
  };

  function srcTypeInfo(type) {
    const M = {
      mail: { icon: "mail", label: "message" },
      attachment: { icon: "attach_file", label: "attachment" },
      calendar: { icon: "calendar_month", label: "event" },
      contact: { icon: "person", label: "contact" },
      multi: { icon: "layers", label: "multiple sources" },
    };
    return M[type] || M.mail;
  }
  const SRC_BADGE_STYLE = "display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:500;color:var(--muted);background:var(--sub);border-radius:999px;padding:2px 8px;white-space:nowrap;margin-left:auto;cursor:default";
  const SRC_BADGE_ICON_STYLE = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:14px;line-height:1";
  function srcBadge(item) {
    const types = item.srcTypes && item.srcTypes.length > 1 ? item.srcTypes : [item.srcType || "mail"];
    if (types.length > 1) {
      return { icon: "layers", label: "multiple sources", title: types.map(t => srcTypeInfo(t).label).join(", ") };
    }
    const si = srcTypeInfo(types[0]);
    return { icon: si.icon, label: si.label, title: si.label };
  }

  const DEFAULT_UNREAD = {};
  THREADS.slice(0, 5).forEach(t => { DEFAULT_UNREAD[t.id] = true; });

  const FRAMES = { tablet: { w: 1024, h: 768 }, fold: { w: 884, h: 832 }, phone: { w: 390, h: 844 } };

  /* NOTIFICATION CENTRE GESTURES (phone) — one place for the numbers the implementation reads.
     commitFrac  0.32  distance as a fraction of panel height above which the gesture commits
     commitVel   0.5   px/ms - velocity at which a short flick commits regardless of distance
     springMs    260   spring-back when neither distance nor velocity was met
     springEase        the curve of that spring-back
     barSlop     12    px upward after which the nav bar hands the gesture to the panel (losing its own touch)
     rowSlop     10    px after which a row hands over the gesture and cancels its long-press */
  const NG = { commitFrac: 0.32, commitVel: 0.5, springMs: 260, springEase: "cubic-bezier(.32,.72,0,1)", barSlop: 12, rowSlop: 10 };

  const FONT = "display:flex;height:100dvh;overflow:hidden;background:var(--bg);color:var(--text);font-family:'Geist',system-ui,sans-serif;font-size:15px";

  const avatarOf = (name) => AVATARS[name] || "";

  const initialsOf = (n) => (n || "?").split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]).join("").toUpperCase();
  const lowerFirst = (s) => (s ? s.charAt(0).toLowerCase() + s.slice(1) : s);
  const segStyle = (on) => "flex:1;text-align:center;padding:6px 8px;border-radius:8px;font-size:13px;white-space:nowrap;" + (on ? "background:var(--accent);color:var(--onaccent)" : "color:var(--muted)");

  const M = (from, when, paras, atts, html) => ({ from, when, initials: initialsOf(from), mine: from === ME, paras, atts: atts || [], html: html || "" });

  /* The originals wrapped in the document shell they arrive in, and the earlier messages built as the newest one is. */
  const MAIL_HTML = {};
  Object.keys(mailBodies).forEach(k => { MAIL_HTML[k] = HTML_SHELL(mailBodies[k].title, mailBodies[k].html); });
  const THREAD_HTML = {};
  Object.keys(threadMailBodies).forEach(t => { THREAD_HTML[t] = MAIL_HTML[threadMailBodies[t]]; });
  const HERO_MSGS = {};
  Object.keys(earlierMessages).forEach(t => {
    HERO_MSGS[t] = earlierMessages[t].map(m => M(m.from, m.when, m.paras, m.attachments, m.html && MAIL_HTML[m.html]));
  });

  const msgsFor = (t) => {
    const when = (t.meta || "").split(" · ")[1] || "";
    const last = M(t.from || "Sender", when, t.body || [], t.attachments || [], THREAD_HTML[t.id]);
    const hero = HERO_MSGS[t.id];
    if (hero) return hero.concat([last]);
    const count = parseInt(((t.meta || "").match(/thread: (\d+)/) || [])[1] || "1", 10);
    if (count < 2) return [last];
    const s1 = ((t.state || [])[0] || {}).value;
    const s2 = ((t.state || [])[1] || {}).value;
    const prior = [M(t.from || "Sender", "earlier", [{ text: "Following up on what we agreed earlier: " + lowerFirst(s1 || "the thread topic") + "." }])];
    if (s2 && s2 !== "None") prior.push(M(ME, "earlier", [{ text: "Thank you. Still open: " + lowerFirst(s2) + "." }]));
    return prior.concat([last]);
  };

  const docContent = (d) => {
    if (!d) return [];
    const n = d.name || "";
    if (n.toLowerCase().indexOf("addendum") >= 0) return documentPreviews.addendum;
    if (d.type === "XLSX") return [{ s: true, text: (n || "Spreadsheet").toUpperCase() }].concat(documentPreviews.spreadsheet);
    return [
      { s: true, text: (d.type || "Document") + " · " + (d.size || "") },
      { h: true, text: n.replace(/\.[a-z]+$/i, "") },
    ].concat(documentPreviews.other);
  };

  const REM_TIMED = [0, 5, 15, 30, 60, 1440];
  const REM_DATED = [0, 60, 240, 1440, 2880];
  const REM_DEFAULT = [15];
  const remLabel = (m, zero) => {
    if (m === 0) return zero || "at the time";
    if (m < 60) return m + " min before";
    if (m < 1440) { const h = m / 60; return h + (h === 1 ? " hour before" : " hours before"); }
    const d = m / 1440;
    return d + (d === 1 ? " day before" : " days before");
  };

  /* COLLECTED CONTACTS — built automatically from the headers of received mail (senders not in the address book).
     In the real app this list is produced while indexing the mailbox and is not synced to the server
     until the user moves a contact into their own. */
  const collectFromMail = (known) => {
    const map = {};
    THREADS.forEach(t => {
      if (known.indexOf(t.from) >= 0) return;
      const meta = t.meta || "";
      const mail = (meta.match(/[\w.+-]+@[\w.-]+/) || [""])[0];
      const when = meta.split(" · ")[1] || t.time;
      if (!map[t.from]) map[t.from] = {
        id: "auto-" + t.id, name: t.from, role: "Collected from mail", org: t.org || (mail ? mail.split("@")[1] : "-"),
        mail: mail || "-", last: when, owed: "-", collected: true, msgs: 0,
        threads: [], docs: [],
      };
      const c = map[t.from];
      c.msgs += parseInt((meta.match(/thread: (\d+)/) || [])[1] || "1", 10);
      c.threads.push({ id: t.id, label: t.subject, meta: (meta.split("thread: ")[1] || "") });
      (t.attachments || []).forEach(a => c.docs.push(a));
    });
    return Object.keys(map).map(k => {
      const c = map[k];
      c.note = "Contact created automatically from received mail - " + c.msgs + (c.msgs === 1 ? " message" : " messages") +
        " across " + c.threads.length + (c.threads.length === 1 ? " thread" : " threads") + ". Not in the address book; add to your own to fill in role and notes.";
      return c;
    });
  };

  const parseEventPrompt = (raw) => {
    const txt = (raw || "").trim();
    const low = txt.toLowerCase();
    let day = "";
    Object.keys(DOW_DAY).forEach(k => { if (!day && low.indexOf(k) >= 0) day = DOW_DAY[k]; });
    const dm = low.match(/\b(\d{1,2})[.\s]?(?:aug|august|08)\b/);
    if (dm) day = dm[1];
    const tm = low.match(/\b(\d{1,2})[:.](\d{2})\b/) || low.match(/\b(?:at|after|by)\s+(\d{1,2})\b/);
    const time = tm ? (tm[1].length < 2 ? "0" + tm[1] : tm[1]) + ":" + (tm[2] || "00") : "";
    const dur = low.match(/\b(\d{2,3})\s*(?:min|minutes?)/) || low.match(/\b(\d)\s*(?:h|hrs?|hours?)/);
    const who = txt.match(/with\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)/i);
    let title = txt
      .replace(/\bon\s+\w+day\b/gi, " ")
      .replace(/\b(at|after|by)\s+\d{1,2}([:.]\d{2})?(am|pm)?\b/gi, " ")
      .replace(/\b\d{1,3}\s*(min|minutes?|h|hrs?|hours?)\b/gi, " ")
      .replace(/\s{2,}/g, " ")
      .replace(/[,;]\s*$/, "")
      .trim();
    if (!title) title = "New event";
    title = title.charAt(0).toUpperCase() + title.slice(1);
    const chips = [];
    chips.push(day ? day + ".08" : "28.08 (default)");
    chips.push(time || "12:00 (default)");
    if (dur) chips.push(dur[0].replace(/\s+/g, " "));
    if (who) chips.push("attendee: " + who[1]);
    if (low.indexOf("addendum") >= 0 || low.indexOf("contoso") >= 0) chips.push("thread: Contract addendum");
    return { title, day: day || "28", time: time || "12:00", chips };
  };

  /* MESSAGE ADDRESS HEADERS — in a real mailbox these come from the server (From / Reply-To / To / Cc).
     Here addresses are derived from the contact book, while Reply-To and CC are thread data, because only
     some correspondence carries them - which is exactly why expanding has to be optional. */
  const slugMail = (name) => {
    const p = (name || "").toLowerCase().replace(/ł/g, "l").replace(/ą/g, "a").replace(/ć/g, "c").replace(/ę/g, "e")
      .replace(/ń/g, "n").replace(/ó/g, "o").replace(/ś/g, "s").replace(/[żź]/g, "z").split(/\s+/).filter(Boolean);
    if (!p.length) return "";
    return (p.length > 1 ? p[0][0] + "." + p[p.length - 1] : p[0]) + "@company.example";
  };
  const addrOf = (name) => {
    if (name === ME) return MY_MAIL;
    const c = CONTACTS.filter(x => x.name === name)[0];
    if (c && c.mail && c.mail !== "-") return c.mail;
    const t = THREADS.filter(x => x.from === name)[0];
    const fromMeta = t && ((t.meta || "").match(/[\w.+-]+@[\w.-]+/) || [])[0];
    return fromMeta || slugMail(name);
  };
  const fullAddr = (name) => name === addrOf(name) ? name : name + " <" + addrOf(name) + ">";

  const msgAddrRows = (thread, m, msgs) => {
    const parts = msgs.map(x => x.from).filter((n, i, a) => a.indexOf(n) === i);
    const to = (m.mine ? parts.filter(n => n !== ME) : [ME].concat(parts.filter(n => n !== ME && n !== m.from)));
    const cc = (HDR_CC[thread.id] || []).filter(n => n !== m.from && to.indexOf(n) < 0);
    const replyTo = !m.mine && HDR_REPLY_TO[thread.id] ? HDR_REPLY_TO[thread.id] : "";
    const rows = [];
    if (replyTo) rows.push({ label: "Reply-to", value: replyTo });
    if (to.length) rows.push({ label: "To", value: to.map(fullAddr).join(", ") });
    if (cc.length) rows.push({ label: "Cc", value: cc.map(fullAddr).join(", ") });
    return { rows, count: (replyTo ? 1 : 0) + to.length + cc.length };
  };

  function formatMsgTime(ts) {
    if (!ts) return "";
    const diff = Date.now() - ts;
    if (diff < 45000) return "now";
    const min = Math.floor(diff / 60000);
    if (min < 60) return min + " " + (min === 1 ? "minute" : "minutes") + " ago";
    const hrs = Math.floor(diff / 3600000);
    if (hrs < 24) return hrs + " " + (hrs === 1 ? "hour" : "hours") + " ago";
    const d = new Date(ts);
    const pad = (n) => String(n).padStart(2, "0");
    return pad(d.getDate()) + "." + pad(d.getMonth() + 1) + "." + d.getFullYear() + " " + pad(d.getHours()) + ":" + pad(d.getMinutes());
  }

  const NKIND = {
    mail: { icon: "mail", c: "var(--accent-2)", soft: "var(--accent-soft)", toast: "info" },
    cal: { icon: "event", c: "var(--ok-text)", soft: "var(--ok-soft)", toast: "info" },
    task: { icon: "task_alt", c: "var(--text2)", soft: "var(--hover)", toast: "neutral" },
    system: { icon: "sync", c: "var(--muted)", soft: "var(--hover)", toast: "neutral" },
  };

  const isNotifUnread = (n, read) => (read && read[n.id] !== undefined ? !read[n.id] : !!n.unread);

  // The instant behind a relative age, as the client words it in full in a notification row's menu.
  const notifInstant = (mins) => new Date(Date.now() - (Number(mins) || 0) * 60000).toLocaleString("en-GB", { dateStyle: "full", timeStyle: "short" });

  const notifTime = (mins) => {
    const m = Number(mins) || 0;
    if (m < 1) return "now";
    if (m < 60) return m + " min";
    if (m < 1440) return Math.round(m / 60) + " h";
    if (m < 2880) return "yesterday";
    return Math.round(m / 1440) + " days ago";
  };

  class Component extends DCLogic {
    state = { workMode: "classic", tabs: [{ key: "thread:contoso", kind: "thread", id: "contoso" }], activeTab: "thread:contoso", screen: "start", phase: "idle", query: "", planKey: null, ready: {}, ev: null, threadId: "contoso", fromCite: null, cameFrom: false, mailAnswer: false, mailPane: "list", themeOverride: null, stateOpen: false, vw: typeof window !== "undefined" ? window.innerWidth : 1440, coarse: typeof matchMedia === "function" && matchMedia("(pointer: coarse)").matches, listW: null, remMap: { t1: [15], t3: [1440, 60] }, remUnit: "min", convs: SEED_CONVS(), bkPhase: {}, bkState: {}, bkResult: {}, bkEdit: {}, revs: [], rev: 0, clarify: null, refineText: "", watched: WATCHED.map(w => Object.assign({}, w)), reported: {}, todayHidden: [], briefOn: false, briefOfferHidden: false, resultFilter: null, selBar: null, selAsk: "", bkText: {}, bkChange: {}, bkVals: {}, bkField: null, fieldAsk: "", agentQuote: null, bkPick: {}, bkNote: {}, bkExcl: {}, bkOpen: {} };
    timers = [];
    rootRef = React.createRef();
    agentScrollRef = React.createRef();

    /* List scroll memory — for the session only, held in memory (gone when the app closes).
       Every list has its own key; the ref and handler are memoised so React does not detach them
       on every render. */
    scrollY = {};
    scrollEls = {};
    scrollRefs = {};
    scrollHandlers = {};

    keepScroll = (key) => {
      if (!this.scrollRefs[key]) this.scrollRefs[key] = (el) => {
        this.scrollEls[key] = el;
        if (el && this.scrollY[key]) el.scrollTop = this.scrollY[key];
      };
      return this.scrollRefs[key];
    };

    keepScrollOn = (key) => {
      if (!this.scrollHandlers[key]) this.scrollHandlers[key] = (e) => { this.scrollY[key] = e.target.scrollTop; };
      return this.scrollHandlers[key];
    };

    resetScroll = (key) => {
      this.scrollY[key] = 0;
      if (this.scrollEls[key]) this.scrollEls[key].scrollTop = 0;
    };

    restoreScrolls = () => {
      Object.keys(this.scrollY).forEach(k => {
        const el = this.scrollEls[k], y = this.scrollY[k];
        /* We restore only when the container is back at the top (remount or end of loading). */
        if (el && y && el.scrollTop === 0 && el.scrollHeight > el.clientHeight + y) el.scrollTop = y;
      });
    };
    railRef = React.createRef();
    agentInputRef = React.createRef();
    photoRef = React.createRef();
    historySearchRef = React.createRef();
    listSearchRef = React.createRef();

    componentDidMount() {
      if (document.fonts && document.fonts.ready) document.fonts.ready.then(this.tbReset);
      document.addEventListener("mousedown", this.onDocDown, true);
      window.addEventListener("message", this.onHtmlFrameMessage);
      window.addEventListener("popstate", this.onBackPressed);
      document.addEventListener("selectionchange", this.onSelChange);
      document.addEventListener("pointerdown", this.onSelPointer, true);
      document.addEventListener("pointerup", this.onSelPointer, true);
      this.backSync();
      MailFathomDesign.fathomUi.install(this);
      this.measure = () => {
        const el = this.rootRef.current;
        const w = (el && el.clientWidth) || (typeof window !== "undefined" ? window.innerWidth : 1440);
        const rail = this.railRef.current;
        const rh = rail && rail.offsetHeight ? rail.offsetHeight : 0;
        const h = (el && el.clientHeight) || 0;
        const patch = {};
        if (w && w !== this.state.vw) patch.vw = w;
        if (h && h !== this.state.vh) patch.vh = h;
        if (rh && rh !== this.state.railH) patch.railH = rh;
        const coarse = typeof matchMedia === "function" && matchMedia("(pointer: coarse)").matches;
        if (coarse !== !!this.state.coarse) patch.coarse = coarse;
        if (Object.keys(patch).length) this.setState(patch);
      };
      if (typeof ResizeObserver !== "undefined" && this.rootRef.current) {
        this.ro = new ResizeObserver(this.measure);
        this.ro.observe(this.rootRef.current);
      }
      window.addEventListener("resize", this.measure);
      if (typeof matchMedia === "function") matchMedia("(pointer: coarse)").addEventListener("change", this.measure);
      this.measure();
      this.timers.push(setTimeout(() => this.simulateNotif(), 14000));
    }

    toggleSelect = (id) => {
      const sel = Array.isArray(this.state.sel) ? this.state.sel : [];
      const next = sel.indexOf(id) >= 0 ? sel.filter(x => x !== id) : sel.concat([id]);
      this.setState(Object.assign({ sel: next }, this.composeCtxPatch(next)));
    };

    /* While the composer is open, what the list points at is what the AI drafts from: a click makes that message the
       context, a selection makes the selection the context. The first context a composition takes is its starting
       point, and a context that differs from it is what the composer warns about. */
    composeCtxPatch = (ids) => {
      if (!this.state.compose || !ids.length) return {};
      return { composeCtx: ids.slice(), composeCtxBase: this.state.composeCtxBase || ids.slice() };
    };

    /* Loading from the server: a short skeleton instead of content. The duration comes from the
       streamDelay tweak but is kept within realistic bounds - a few hundred ms, not seconds. */
    loadDelay = (f) => Math.round(Math.max(160, Math.min(900, (this.props.streamDelay ?? 550) * f)));

    loadList = (patch) => {
      this.setState(Object.assign({ listLoading: true }, patch || {}));
      clearTimeout(this._listLoadT);
      this._listLoadT = setTimeout(() => this.setState({ listLoading: false }), this.loadDelay(0.7));
      this.timers.push(this._listLoadT);
    };

    /* HTML preview and attachment preview also fetch content from the server — same skeleton as a message. */
    loadHtml = () => {
      clearTimeout(this._htmlLoadT);
      this._htmlLoadT = setTimeout(() => this.setState({ htmlLoading: false }), this.loadDelay(0.75));
      this.timers.push(this._htmlLoadT);
    };

    openDocLoad = (patch) => {
      this.setState(Object.assign({ docLoading: true }, patch));
      clearTimeout(this._docLoadT);
      this._docLoadT = setTimeout(() => this.setState({ docLoading: false }), this.loadDelay(0.8));
      this.timers.push(this._docLoadT);
    };

    loadMsg = () => {
      clearTimeout(this._msgLoadT);
      this._msgLoadT = setTimeout(() => this.setState({ msgLoading: false }), this.loadDelay(0.55));
      this.timers.push(this._msgLoadT);
    };

    pickFolder = (key, label) => {
      if ((this.state.folderKey || "acc1·Inbox") === key) return;
      this.resetScroll("mail");
      this.loadList({ folderKey: key, folderLabel: label, sel: [], sideDrawer: false });
    };

    pickThread = (id, e) => {
      if (this.suppressClick) { this.suppressClick = false; return; }
      const sel = Array.isArray(this.state.sel) ? this.state.sel : [];
      if ((e && (e.ctrlKey || e.metaKey || e.shiftKey)) || sel.length) { this.toggleSelect(id); return; }
      const unread = Object.assign({}, this.state.unread || DEFAULT_UNREAD);
      delete unread[id];
      /* Some threads have an unread message from the middle of the history —
         we open exactly that one, not the newest. */
      const entry = { piotr: 2 };
      const patch = Object.assign({ threadId: id, listWide: false, mailAnswer: false, replyChooser: null, cameFrom: this.state.cameFrom && id === "contoso", mailPane: "thread", threadOpen: true, unread: unread,
        activeMsg: Object.assign({}, this.state.activeMsg, { [id]: typeof entry[id] === "number" ? entry[id] : null }),
        histOpen: Object.assign({}, this.state.histOpen, { [id]: !!this.state.autoExpand }) }, this.composeCtxPatch([id]));
      patch.msgLoading = true;
      if (this.tabsMode()) this.openTab("thread:" + id, patch);
      else this.setState(patch);
      this.loadMsg();
    };

    swipeStart = (id, e) => {
      const t = e.touches && e.touches[0];
      this._swx = t ? t.clientX : 0;
      this._swy = t ? t.clientY : 0;
      this._swid = id;
      this.startLongPress(id);
    };

    swipeMove = (id, e) => {
      const t = e.touches && e.touches[0];
      if (!t || this._swid !== id) return;
      const dx = t.clientX - this._swx, dy = t.clientY - this._swy;
      if (Math.abs(dy) > 26 && Math.abs(dy) > Math.abs(dx)) { this.swipeCancel(); return; }
      if (Math.abs(dx) < 14) return;
      this.cancelLongPress();
      const cl = Math.max(-148, Math.min(148, dx));
      if (Math.round(cl / 4) !== Math.round((this.state.swipeDx || 0) / 4) || this.state.swipeId !== id) {
        this.setState({ swipeId: id, swipeDx: cl });
      }
    };

    swipeCancel = () => {
      this.cancelLongPress();
      this._swid = null;
      if (this.state.swipeId) this.setState({ swipeId: null, swipeDx: 0 });
    };

    swipeEnd = (id) => {
      this.cancelLongPress();
      const dx = this.state.swipeDx || 0;
      this._swid = null;
      if (this.state.swipeId !== id || Math.abs(dx) < 96) {
        if (this.state.swipeId) this.setState({ swipeId: null, swipeDx: 0 });
        return;
      }
      this.suppressClick = true;
      setTimeout(() => { this.suppressClick = false; }, 400);
      if (dx < 0) {
        this.setState({ swipeId: null, swipeDx: 0, threadId: id, mailPane: "thread", threadOpen: true, compose: true, composeMin: false });
      } else {
        this.setState({ swipeId: null, swipeDx: 0 });
        this.animateRowsOut([id], () => this.setState(s2 => ({
          archived: (s2.archived || []).concat([id]),
          lastArchived: id,
        })), "arc");
      }
    };

    undoArchive = () => this.setState(s2 => ({
      archived: (s2.archived || []).filter(x => x !== s2.lastArchived),
      lastArchived: null,
    }));

    /* MAIL TOOLBAR ACTIONS — prototype: they change local state and show a confirmation,
       they do not perform server operations. Target: the selection, or the open thread when empty. */
    opTargets = (ids) => {
      if (ids && ids.length) return ids;
      const sel = Array.isArray(this.state.sel) ? this.state.sel : [];
      return sel.length ? sel.slice() : [this.state.threadId];
    };

    countWord = (n) => (n === 1 ? "1 thread" : n + " threads");

    notify = (text, opt) => {
      const o = (opt === true || opt === false || opt == null) ? { undo: opt === true } : opt;
      const id = "t" + (this._tSeq = (this._tSeq || 0) + 1);
      const t = {
        id: id,
        kind: o.kind || "neutral",
        title: o.title || text,
        body: o.title ? text : (o.body || ""),
        undo: !!o.undo,
        actionLabel: o.actionLabel || (o.undo ? "Undo" : ""),
        onAction: o.onAction || null,
        ms: o.ms || 5000,
        sticky: o.kind === "loading" || !!o.sticky,
        cancelText: o.cancelText || "",
      };
      this.setState(s => ({ toasts: [t].concat(s.toasts || []).slice(0, 4) }));
      if (!t.sticky) this.armToast(id, t.ms);
      return id;
    };

    armToast = (id, ms) => {
      this._tT = this._tT || {};
      clearTimeout(this._tT[id]);
      this._tT[id] = setTimeout(() => this.dismissToast(id), ms);
    };

    dismissToast = (id) => {
      this._tT = this._tT || {};
      clearTimeout(this._tT[id]);
      this.setState(s => ({ toasts: (s.toasts || []).map(x => (x.id === id ? Object.assign({}, x, { out: true }) : x)) }));
      setTimeout(() => this.setState(s => ({ toasts: (s.toasts || []).filter(x => x.id !== id) })), 200);
    };

    hideToast = () => { (this.state.toasts || []).forEach(t => this.dismissToast(t.id)); };

    notifyTask = (o) => {
      const id = this.notify(o.body || "", { kind: "loading", title: o.title, cancelText: o.cancelText });
      this._tasks = this._tasks || {};
      this._tasks[id] = setTimeout(() => {
        delete this._tasks[id];
        this.setState(s => ({ toasts: (s.toasts || []).map(x => (x.id === id ? Object.assign({}, x, { kind: "success", title: o.doneTitle, body: o.doneBody || "", sticky: false }) : x)) }));
        this.armToast(id, 5000);
      }, o.ms || 2200);
      return id;
    };

    askCancelToast = (id) => {
      const t = (this.state.toasts || []).filter(x => x.id === id)[0];
      this.setState({ cancelAsk: { id: id, title: "Abort the operation?", text: (t && t.cancelText) || "The operation is running. Closing the notification aborts it - partial results will not be saved." } });
    };

    abortTask = () => {
      const ask = this.state.cancelAsk;
      this.setState({ cancelAsk: null });
      if (!ask) return;
      if (this._tasks && this._tasks[ask.id]) { clearTimeout(this._tasks[ask.id]); delete this._tasks[ask.id]; }
      this.dismissToast(ask.id);
      this.notify("The operation was aborted. Nothing was saved.", { kind: "warning", title: "Cancelled" });
    };

    startReply = (mode, ids) => {
      const id = this.opTargets(ids)[0];
      const patch = { compose: true, composeMin: false, replyMode: mode, replyThreadId: id, composeCtx: [id], composeCtxBase: [id], threadId: id, mailPane: "thread", threadOpen: true, sel: [] };
      if (this.state.workMode === "tabs") this.openTab("compose", patch);
      else this.setState(patch);
    };

    /* *Draft a reply* goes straight to the reply composer and writes the AI draft into its editor there. */
    draftReply = () => {
      this.pendingAiWrite = true;
      this.startReply("reply", [this.state.threadId]);
    };

    archiveThreads = (ids) => {
      const t = this.opTargets(ids);
      const op = { kind: "archive", ids: t };
      this.setState({ lastOp: op, sel: [], mailPane: "list" });
      this.animateRowsOut(t, () => { if (op.undone) return; this.setState(s => ({ archived: (s.archived || []).concat(t) })); }, "arc");
      this.notify("Archived " + this.countWord(t.length), true);
    };

    /* Motion — the one place every surface's arrival and departure is decided. `styles/base.css` holds the kinds
       (`data-motion`) and their numbers; this holds when each plays.
       A surface is open while a state key below is truthy. When the key empties, the render keeps reading its last
       value for MOTION_EXIT_MS, so the surface stays mounted and drawn with its content, and `mo[<key>]` reads `out`
       — its `data-phase` — until the timer unmounts it. A pane key (mailPane, peoplePane) records which way
       it last moved instead, read by `paneMotion`, so one pane at a time pushes forward and comes back.
       ponytail: the held value is read by the whole render, so anything else gated on the same key (the FAB waits on
       `compose`) follows it 240 ms late; a per-surface snapshot is the upgrade if that ever shows. */
    static MOTION_HELD = ["threadSheet", "more", "ctxMenu", "userMenu", "sideDrawer", "compose", "settings",
      "aiSum", "moveFor", "newFolder", "remFor", "event", "agentReturn", "msgLoading", "replyChooser",
      "newTask", "newEvent", "newContact", "filtersOpen", "catchupOpen", "fathomMenu", "fathomAnswer",
      "delFolder", "htmlAsk", "closeAsk", "sendAsk", "deleteConvAsk", "closeAllAsk", "confirmDel", "cancelAsk"];
    static MOTION_PANES = ["mailPane", "peoplePane"];
    static MOTION_EXIT_MS = 240;

    motionHold = (state) => {
      const C = this.constructor;
      const prev = this._moPrev || (this._moPrev = {});
      const held = this._moHeld || (this._moHeld = {});
      const dir = this._moDir || (this._moDir = {});
      const live = (v) => Array.isArray(v) ? v.length > 0 : !!v;
      const st = Object.assign({}, state);
      const mo = {};
      C.MOTION_HELD.forEach(k => {
        if (live(state[k])) delete held[k];
        else if (live(prev[k]) && !held[k]) {
          held[k] = { v: prev[k] };
          setTimeout(() => { if (held[k] && !live(this.state[k])) { delete held[k]; this.forceUpdate(); } }, C.MOTION_EXIT_MS);
        }
        prev[k] = state[k];
        if (held[k]) st[k] = held[k].v;
        mo[k] = held[k] ? "out" : "in";
      });
      /* A pane reset in the same act that switches space (More → People, a notification opening a thread) is the
         space switch's, so only a move made with the screen unchanged pushes or comes back. */
      const sameScreen = prev.screen === state.screen;
      C.MOTION_PANES.forEach(k => {
        const v = state[k] || "list";
        if (sameScreen && prev[k] !== undefined && prev[k] !== v) dir[k] = { to: v === "list" ? "back" : "push", at: Date.now() };
        prev[k] = v;
      });
      prev.screen = state.screen;
      return { st, mo };
    };

    /* "push" on the detail and "back" on the list for 600 ms after the pane moved; "none" otherwise, so a pane that
       mounts for any other reason (a space switch, the first render) does not slide. */
    paneMotion = (k, side) => {
      const d = (this._moDir || {})[k];
      return d && Date.now() - d.at < 600 && d.to === side ? side : "none";
    };

    /* Back — the browser's (and Android's) back steps back one thing at a time, the way the client's does: the
       surface on top first, then a view standing in place of another in a one-pane composition, then the space before.
       Every step that stands records one history entry at the same address; a press consumes one and the top step
       closes, and a step closed by its own control gives its entry back. A space switch is an entry of its own, and
       going back across one closes everything still standing, the way the client's change of destination does.
       ponytail: steps close in BACK_LAYERS order when several stand at once; the client keeps the true opening order. */
    static BACK_LAYERS = ["sideDrawer", "doc", "htmlDoc", "compose", "threadSheet", "notifCenter", "settings", "userMenu",
      "more", "aiSum", "moveFor", "newFolder", "remFor", "event", "ctxMenu", "replyChooser", "catchupOpen", "fathomMenu", "fathomSmart", "fathomAnswer"];

    backSteps = (state) => {
      const FR = FRAMES[this.props.layout ?? "auto"] || null;
      const w = FR ? FR.w : (state.vw || 1440);
      const live = (v) => Array.isArray(v) ? v.length > 0 : !!v;
      const steps = [];
      if (state.screen === "mail" && w < 820 && (state.mailPane || "list") !== "list") steps.push("mailPane");
      if (state.screen === "people" && w < 700 && (state.peoplePane || "list") !== "list") steps.push("peoplePane");
      /* The notification centre slides away before its key empties, so it stands for as long as it is shown. */
      this.constructor.BACK_LAYERS.forEach(k => { if (k === "notifCenter" ? state.notifCenter && state.notifShown : live(state[k])) steps.push(k); });
      return steps;
    };

    backClosing = (k, patch) => {
      if (k === "notifCenter") this.closeNotifCenter();
      else if (this._ownClose && this._ownClose[k]) this._ownClose[k]();
      else patch[k] = k.endsWith("Pane") ? "list" : null;
    };

    backSync = () => {
      if (typeof window === "undefined" || this._backPops > 0) return;
      const screen = this.state.screen, depth = this.backSteps(this.state).length;
      let at = this._backAt;
      if (!at) { window.history.replaceState({ mfBack: true, screen, depth: 0 }, ""); at = { screen, depth: 0 }; }
      if (at.screen !== screen) { window.history.pushState({ mfBack: true, screen, depth: 0 }, ""); at = { screen, depth: 0 }; }
      if (depth > at.depth) for (let d = at.depth + 1; d <= depth; d++) window.history.pushState({ mfBack: true, screen, depth: d }, "");
      else if (depth < at.depth) { this._backPops = (this._backPops || 0) + 1; window.history.go(depth - at.depth); }
      this._backAt = { screen, depth };
    };

    onBackPressed = (e) => {
      if (this._backPops > 0) { this._backPops -= 1; this.backSync(); return; }
      const to = e.state && e.state.mfBack ? e.state : { screen: this._backAt ? this._backAt.screen : this.state.screen, depth: 0 };
      this._backAt = { screen: to.screen, depth: to.depth };
      const steps = this.backSteps(this.state);
      const patch = {};
      if (to.screen !== this.state.screen) {
        steps.forEach(k => { if (!k.endsWith("Pane")) this.backClosing(k, patch); });
        this.setState(patch, () => this.goScreen(to.screen));
        return;
      }
      steps.slice(to.depth).reverse().forEach(k => this.backClosing(k, patch));
      this.setState(patch);
    };

    ovStyle = (z, mobile) => "position:fixed;z-index:" + z + ";display:flex;background:var(--scrim);" +
      (mobile ? "top:0;left:0;right:0;bottom:" + this.barH() + "px;align-items:stretch;justify-content:stretch" : "inset:0;align-items:center;justify-content:center;padding:" + (this._low ? 8 : 22) + "px");

    /* Short confirmations (text + up to 3 buttons) always as a centred popup,
       on phones too - full screen is reserved for windows with forms. */
    ovAskStyle = (z, mobile) => "position:fixed;z-index:" + z + ";display:flex;align-items:center;justify-content:center;background:var(--scrim);" +
      (mobile ? "top:0;left:0;right:0;bottom:" + this.barH() + "px;padding:20px" : "inset:0;padding:" + (this._low ? 8 : 22) + "px");

    barH = () => Math.max(70, (this.state.railH || 84)) + 2;

    goAgentWith = (label) => {
      this.setState({ screen: "agent", convId: null, agentCtx: label, agentInput: "", threadSheet: false, agentThinking: false, more: false, agentFocusPending: true });
    };

    localPt = (cx, cy) => {
      const el = this.rootRef.current;
      const r = el && el.getBoundingClientRect ? el.getBoundingClientRect() : null;
      return r ? { x: cx - r.left, y: cy - r.top } : { x: cx, y: cy };
    };

    pressFor = (kind, id, title, items, ms, detail) => {
      const open = (cx, cy, fromTouch) => {
        const p = this.localPt(cx, cy);
        this.armCtx(fromTouch);
        this.setState({ ctxMenu: { x: p.x, y: p.y, title: title, detail: detail, kind: kind, id: id, items: items } });
      };
      return {
        ctx: (e) => { if (e && e.preventDefault) { e.preventDefault(); e.stopPropagation(); } open(e.clientX, e.clientY, false); },
        down: (e) => {
          const t = e && e.touches && e.touches[0];
          const x = t ? t.clientX : 0, y = t ? t.clientY : 0;
          clearTimeout(this._lpT);
          this._lpT = setTimeout(() => { this._lpFired = Date.now(); open(x, y, true); }, ms || 460);
        },
        up: () => { clearTimeout(this._lpT); },
      };
    };

    /* After a long press the browser still sends a "ghost" click where the finger was —
       it landed on the freshly opened menu and closed it instantly. So the menu is briefly unclickable. */
    armCtx = (fromTouch) => {
      clearTimeout(this._ctxArmT);
      this._ctxArmed = !fromTouch;
      if (fromTouch) this._ctxArmT = setTimeout(() => { this._ctxArmed = true; }, 450);
    };

    ctxArmed = () => this._ctxArmed !== false;

    lpJustFired = () => this._lpFired && Date.now() - this._lpFired < 700;

    selOf = (kind) => (this.state.selBy || {})[kind] || [];

    selActive = (kind) => this.selOf(kind).length > 0;

    selSet = (kind, ids) => this.setState(s2 => ({ selBy: Object.assign({}, s2.selBy || {}, { [kind]: ids }), ctxMenu: null }));

    selStart = (kind, id) => this.selSet(kind, [id]);

    selToggle = (kind, id) => {
      const ids = this.selOf(kind).slice();
      const i = ids.indexOf(id);
      if (i >= 0) ids.splice(i, 1); else ids.push(id);
      this.selSet(kind, ids);
    };

    selClearKind = (kind) => this.selSet(kind, []);

    rowTap = (kind, id, run) => (e) => {
      if (this.lpJustFired()) return;
      if (e && (e.ctrlKey || e.metaKey || e.shiftKey)) {
        if (e.preventDefault) e.preventDefault();
        this.selToggle(kind, id);
        return;
      }
      if (this.selActive(kind)) { this.selToggle(kind, id); return; }
      run();
    };

    askHide = (kind, ids, title, text) => this.setState({
      ctxMenu: null,
      confirmDel: {
        title: title,
        text: text,
        label: "Delete",
        run: () => this.setState(s2 => {
          const map = Object.assign({}, s2.hidden || {});
          map[kind] = (map[kind] || []).concat(ids);
          return { hidden: map, selBy: Object.assign({}, s2.selBy || {}, { [kind]: [] }) };
        }),
      },
    });

    setNotifsRead = (ids, read) => this.setState(s2 => {
      const map = Object.assign({}, s2.notifRead || {});
      ids.forEach(id => { map[id] = read; });
      return { notifRead: map, selBy: Object.assign({}, s2.selBy || {}, { notifs: [] }) };
    });

    askDel = (o) => this.setState({ confirmDel: o });

    inTrash = () => /·Trash$/.test(this.state.folderKey || "acc1·Inbox");

    deleteThreads = (ids) => {
      const t = this.opTargets(ids);
      if (!t.length) return;
      if (this.inTrash()) {
        this.askDel({
          title: t.length === 1 ? "Delete this thread permanently?" : "Delete " + this.countWord(t.length) + " permanently?",
          text: "Already in Trash - deleting now removes " + this.countWord(t.length) + " together with attachments. This cannot be undone.",
          label: "Delete permanently",
          run: () => this.purgeThreads(t),
        });
        return;
      }
      this.askDel({
        title: t.length === 1 ? "Delete this thread?" : "Delete " + this.countWord(t.length) + "?",
        text: "Moving to trash - " + this.countWord(t.length) + ". They disappear from trash after 30 days.",
        label: "Move to trash",
        run: () => this.doDeleteThreads(t),
      });
    };

    purgeThreads = (t) => {
      this.setState({ lastOp: null, sel: [], mailPane: "list" });
      this.animateRowsOut(t, () => this.setState(s => ({ deleted: (s.deleted || []).concat(t) })), "del");
      this.notify("Permanently deleted - " + this.countWord(t.length), false);
    };

    doDeleteThreads = (t) => {
      const op = { kind: "delete", ids: t };
      this.setState({ lastOp: op, sel: [], mailPane: "list" });
      this.animateRowsOut(t, () => { if (op.undone) return; this.setState(s => ({ deleted: (s.deleted || []).concat(t) })); }, "del");
      this.notify("Moved to trash - " + this.countWord(t.length), true);
    };

    toggleFlagThreads = (ids) => {
      const t = this.opTargets(ids);
      const flags = Object.assign({}, this.state.flags || {});
      const turnOn = t.some(id => !flags[id]);
      t.forEach(id => { if (turnOn) flags[id] = true; else delete flags[id]; });
      this.setState({ flags: flags, sel: [] });
      this.notify((turnOn ? "Flagged " : "Flag removed - ") + this.countWord(t.length), false);
    };

    markUnread = (ids) => {
      const t = this.opTargets(ids);
      const unread = Object.assign({}, this.state.unread || DEFAULT_UNREAD);
      t.forEach(id => { unread[id] = true; });
      this.setState({ unread: unread, sel: [] });
      this.notify("Marked as unread - " + this.countWord(t.length), false);
    };

    accountMenu = (key, label) => [
      { icon: "create_new_folder", label: "New folder", run: () => this.openNewFolder(key, label) },
      { icon: "mark_email_read", label: "Mark all as read", run: () => this.notify("All read in " + label, false) },
    ];

    folderMenu = (key, accLabel, path, canNest, custom) => {
      const label = path.split("/").pop();
      const items = [];
      if (canNest) items.push({ icon: "create_new_folder", label: "New folder inside", run: () => this.openNewFolder(key, accLabel, path) });
      items.push({ icon: "mark_email_read", label: "Mark all as read", run: () => this.notify("All read in " + label, false) });
      if (custom) items.push({ icon: "edit", label: "Edit folder", run: () => this.openEditFolder(key, accLabel, path) });
      if (custom) items.push({ icon: "delete", label: "Delete folder", danger: true, run: () => this.askDeleteFolder(key, accLabel, path) });
      return items;
    };

    /* Creating a folder asks for a name and a place in the account's own folder tree. MailFathom
       puts it on the mail server itself and never shows or takes a server path - accounts are
       administrator-managed and the path is not the user's business. Three levels deep at most. */
    MAX_DEPTH = 3;

    STD_LABELS = ["Inbox", "Sent", "Drafts", "Archive", "Spam", "Trash"];

    /* Places a folder may sit in. In edit mode the folder itself, everything under it, and any
       parent too shallow to hold its subtree drop out of the list. */
    folderLocations = (key, movePath) => {
      const extra = ((this.state.extraFolders || {})[key] || []).slice().sort();
      const all = this.STD_LABELS.concat(extra.filter(p => this.STD_LABELS.indexOf(p) < 0));
      const own = movePath ? movePath.split("/").length : 0;
      const subtree = movePath
        ? [movePath].concat(extra.filter(p => p.indexOf(movePath + "/") === 0))
            .reduce((mx, p) => Math.max(mx, p.split("/").length - own + 1), 1)
        : 1;
      return [{ v: "", label: "Top level of the account" }].concat(all
        .filter(p => !(movePath && (p === movePath || p.indexOf(movePath + "/") === 0)))
        .filter(p => p.split("/").length + subtree <= this.MAX_DEPTH)
        .map(p => ({ v: p, label: p.split("/").join(" / ") })));
    };

    locLabel = (parent) => (parent ? "inside " + parent.split("/").join(" / ") : "top level");

    openNewFolder = (key, label, parent) => this.setState({
      newFolder: { key: key, label: label, mode: "create" },
      nfName: "", nfParent: parent || "", moveFor: null,
    });

    openEditFolder = (key, label, path) => this.setState({
      newFolder: { key: key, label: label, mode: "edit", path: path },
      nfName: path.split("/").pop(), nfParent: path.split("/").slice(0, -1).join("/"), moveFor: null,
    });

    saveFolder = () => (this.state.newFolder && this.state.newFolder.mode === "edit" ? this.applyFolderEdit() : this.createFolder());

    applyFolderEdit = () => {
      const nf = this.state.newFolder;
      const name = (this.state.nfName || "").trim().replace(/\//g, " ");
      if (!nf || !name) return;
      const parent = this.state.nfParent || "";
      const oldPath = nf.path;
      const newPath = (parent ? parent + "/" : "") + name;
      if (newPath !== oldPath && ((this.state.extraFolders || {})[nf.key] || []).indexOf(newPath) >= 0) {
        this.notify("There is already a folder called “" + name + "” " + this.locLabel(parent) + ".", { kind: "warning", title: "Pick another name" });
        return;
      }
      this.setState(s => {
        const ex = Object.assign({}, s.extraFolders || {});
        ex[nf.key] = (ex[nf.key] || []).map(p => (p === oldPath ? newPath : p.indexOf(oldPath + "/") === 0 ? newPath + p.slice(oldPath.length) : p));
        const pref = nf.key + "·" + oldPath;
        const remap = (src) => {
          const out = {};
          Object.keys(src).forEach(k => {
            if (k === pref) out[nf.key + "·" + newPath] = src[k];
            else if (k.indexOf(pref + "/") === 0) out[nf.key + "·" + newPath + k.slice(pref.length)] = src[k];
            else out[k] = src[k];
          });
          return out;
        };
        const active = s.folderKey === pref;
        return { extraFolders: ex, folderOpen: remap(s.folderOpen || {}), newFolder: null,
          folderKey: active ? nf.key + "·" + newPath : s.folderKey, folderLabel: active ? name : s.folderLabel };
      });
      this.notify(nf.label + " · " + this.locLabel(parent), { kind: "success", title: "Folder “" + name + "” updated" });
    };

    closeNewFolder = () => this.setState({ newFolder: null });

    onNfName = (e) => this.setState({ nfName: e.target.value });

    onNfParent = (e) => this.setState({ nfParent: e.target.value });

    createFolder = () => {
      const nf = this.state.newFolder;
      const name = (this.state.nfName || "").trim().replace(/\//g, " ");
      if (!nf || !name) return;
      const parent = this.state.nfParent || "";
      this.setState(s => {
        const ex = Object.assign({}, s.extraFolders || {});
        const list = (ex[nf.key] || []).slice();
        const under = parent ? parent + "/" : "";
        let label = name, n = 1;
        while (list.indexOf(under + label) >= 0) { n++; label = name + " " + n; }
        list.push(under + label);
        ex[nf.key] = list;
        /* Reveal the new folder: open every ancestor on the way down to it. */
        const fo = Object.assign({}, s.folderOpen || {});
        if (parent) parent.split("/").forEach((seg, i, arr) => { fo[nf.key + "·" + arr.slice(0, i + 1).join("/")] = true; });
        return { extraFolders: ex, folderOpen: fo, newFolder: null, accOpen: Object.assign({}, s.accOpen, { [nf.key]: true }) };
      });
      this.notify(nf.label + " · " + this.locLabel(parent), { kind: "success", title: "Folder “" + name + "” created" });
    };

    askDeleteFolder = (key, accLabel, path) => this.setState(s => ({ delFolder: { key: key, label: accLabel, path: path, extraFolders: s.extraFolders, moved: s.moved } }));

    closeDeleteFolder = () => this.setState({ delFolder: null });

    confirmDeleteFolder = () => {
      const df = this.state.delFolder;
      if (!df) return;
      const name = df.path.split("/").pop();
      this.setState(s => {
        const ex = Object.assign({}, s.extraFolders || {});
        const gone = (ex[df.key] || []).filter(p => p === df.path || p.indexOf(df.path + "/") === 0);
        ex[df.key] = (ex[df.key] || []).filter(p => gone.indexOf(p) < 0);
        const fo = Object.assign({}, s.folderOpen || {});
        gone.forEach(p => { delete fo[df.key + "·" + p]; });
        /* Mail stored in the folders that go away goes with them. */
        const names = gone.map(p => p.split("/").pop());
        const moved = Object.assign({}, s.moved || {});
        const hit = Object.keys(moved).filter(id => names.indexOf(moved[id]) >= 0);
        hit.forEach(id => { delete moved[id]; });
        const wasOpen = gone.indexOf((s.folderKey || "").split("·")[1]) >= 0;
        return { extraFolders: ex, folderOpen: fo, delFolder: null, moved: moved,
          deleted: (s.deleted || []).concat(hit),
          folderKey: wasOpen ? "acc1·Inbox" : s.folderKey, folderLabel: wasOpen ? "Inbox" : s.folderLabel };
      });
      this.notify("Removed from " + df.label, { kind: "success", title: "Folder “" + name + "” deleted" });
    };

    addFolder = (key, label) => {
      this.setState(s => {
        const ex = Object.assign({}, s.extraFolders || {});
        const list = (ex[key] || []).slice();
        let name = "New folder", n = 1;
        while (list.indexOf(name) >= 0) { n++; name = "New folder " + n; }
        list.push(name);
        ex[key] = list;
        return { extraFolders: ex, accOpen: Object.assign({}, s.accOpen, { [key]: true }), moveFor: null, sideDrawer: s.sideDrawer };
      });
      this.notify("Folder created in " + label, false);
    };

    openMove = (ids) => this.setState({ moveFor: this.opTargets(ids) });
    closeMove = () => this.setState({ moveFor: null });

    moveThreads = (folder, account, ids) => {
      const t = ids || this.state.moveFor || [];
      this.setState(s => {
        const moved = Object.assign({}, s.moved || {});
        t.forEach(id => { moved[id] = folder; });
        return { moved: moved, moveFor: null, sel: [], lastOp: { kind: "move", ids: t } };
      });
      t.forEach(id => this.setRowAnim(id, "move", 900));
      this.notify("Moved to “" + folder + "”" + (account ? " in " + account : "") + " - " + this.countWord(t.length), true);
    };

    /* Drag a thread (or the whole selection) from the list onto a folder in the sidebar. */
    dragThreadsStart = (id, e) => {
      const sel = this.state.sel || [];
      const ids = sel.indexOf(id) >= 0 && sel.length > 1 ? sel.slice() : [id];
      this.cancelLongPress();
      this.setState({ dragIds: ids, dragOverKey: "" });
      if (e && e.dataTransfer) {
        e.dataTransfer.effectAllowed = "move";
        try { e.dataTransfer.setData("text/plain", ids.join(",")); } catch (err) {}
      }
    };
    dragThreadsEnd = () => { if (this.state.dragIds) this.setState({ dragIds: null, dragOverKey: "" }); };
    dragOverFolder = (fk, e) => {
      if (!(this.state.dragIds || []).length) return;
      if (e) { if (e.preventDefault) e.preventDefault(); if (e.dataTransfer) e.dataTransfer.dropEffect = "move"; }
      if (this.state.dragOverKey !== fk) this.setState({ dragOverKey: fk });
    };
    dragLeaveFolder = (fk) => { if (this.state.dragOverKey === fk) this.setState({ dragOverKey: "" }); };
    dropOnFolder = (folder, account, e) => {
      if (e && e.preventDefault) e.preventDefault();
      const ids = this.state.dragIds || [];
      this.setState({ dragIds: null, dragOverKey: "" });
      if (ids.length) this.moveThreads(folder, account, ids);
    };

    undoOp = () => {
      const op = this.state.lastOp;
      this.hideToast();
      if (!op) return;
      op.undone = true; /* an undo fired inside the leave animation must cancel the pending removal */
      const gone = (list) => (list || []).filter(x => op.ids.indexOf(x) < 0);
      if (op.kind === "archive") this.setState(s => ({ archived: gone(s.archived), lastOp: null }));
      else if (op.kind === "delete") this.setState(s => ({ deleted: gone(s.deleted), lastOp: null }));
      else if (op.kind === "move") this.setState(s => {
        const moved = Object.assign({}, s.moved || {});
        op.ids.forEach(id => { delete moved[id]; });
        return { moved: moved, lastOp: null };
      });
    };

    startLongPress = (id, x, y) => {
      this.cancelLongPress();
      this.lpTimer = setTimeout(() => {
        this.suppressClick = true;
        if (navigator.vibrate) navigator.vibrate(12);
        this.armCtx(true);
        this.openMailMenu(id, x || 0, y || 0);
        setTimeout(() => { this.suppressClick = false; }, 700);
      }, 420);
    };

    /* AI summary — the per-message reading, always shown next to the fragment it rests on. */
    openAiSum = (id) => this.setState({ aiSum: id, ctxMenu: null });
    closeAiSum = () => this.setState({ aiSum: null });

    mdPlain = (s) => String(s || "")
      .replace(/```[\s\S]*?```/g, " ")
      .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
      .replace(/^\s*\|.*$/gm, " ")
      .replace(/^\s{0,3}#{1,6}\s*/gm, "")
      .replace(/^\s{0,3}>\s*/gm, "")
      .replace(/^\s*[-*+]\s+/gm, "")
      .replace(/^\s*\d+\.\s+/gm, "")
      .replace(/[*_`~]/g, "")
      .replace(/\s+/g, " ")
      .trim();

    aiSumCards = (th) => {
      const HEAD = { Agreed: "What is settled", "Open question": "What is still open", Commitment: "What is the commitment", Deadline: "What the deadline is", "Version difference": "What changed between versions" };
      const WHY = {
        Agreed: "The message puts this forward as settled, and nothing later in it walks the point back.",
        "Open question": "The message raises this and never answers it, so it stays open on our side.",
        Commitment: "The message names both the action and the side that owes it.",
        Deadline: "The message gives the date outright rather than leaving it to be worked out.",
        "Version difference": "A later message corrects an earlier one, and the newer wording is the one that holds.",
      };
      const msgs = (th.body || []).map(b => this.mdPlain(b.md || b.text));
      const last = Math.max(0, msgs.length - 1);
      const cards = [];
      (th.state || []).forEach((s, i) => {
        if (!s.value || s.value === "None") return;
        const src = (STATE_SRC[th.id] || [])[i];
        cards.push({
          heading: HEAD[s.label] || s.label, claim: s.value, why: WHY[s.label] || "",
          idx: typeof src === "number" ? Math.min(src, last) : last,
        });
      });
      /* Three readings at most — the modal is a summary, not a second copy of the thread. */
      if (th.ai && cards.length < 3) cards.push({ heading: "What this is about", claim: th.ai, why: "This is the reading the message list shows - it comes from the subject and the opening of the message.", idx: 0 });
      return cards.slice(0, 3).map(c => ({
        heading: c.heading, claim: c.claim, why: c.why,
        quote: msgs[c.idx] || msgs.join(" ") || th.subject,
        showSrc: msgs.length > 1,
        srcLabel: "Open message " + (c.idx + 1) + " in the thread",
        openSrc: () => {
          this.setState({ aiSum: null, threadId: th.id, mailPane: "thread", threadOpen: true });
          this.revealMessage(th.id, c.idx);
        },
      }));
    };

    openMailMenu = (id, cx, cy) => {
      const th = THREADS.find(t => t.id === id) || {};
      const flagged = !!(this.state.flags || {})[id];
      const p = this.localPt(cx || 0, cy || 0);
      this.setState({
        ctxMenu: {
          x: p.x, y: p.y, kind: "mail", id: id, title: th.subject || "Message", detail: th.from,
          items: [
            { icon: "auto_awesome", label: "AI summary", run: () => this.openAiSum(id) },
            { icon: "check_box", label: "Select messages", run: () => this.toggleSelect(id) },
            { icon: "auto_awesome", label: "Ask the agent", run: () => this.goAgentWith("thread “" + (th.subject || "mail") + "”") },
            { icon: "reply", label: "Reply", run: () => { this.setState({ threadId: id, mailPane: "thread", threadOpen: true }); this.startReply("reply"); } },
            { icon: "forward", label: "Forward", run: () => { this.setState({ threadId: id, mailPane: "thread", threadOpen: true }); this.startReply("fwd"); } },
            { icon: "archive", label: "Archive", run: () => this.archiveThreads([id]) },
            { icon: "flag", label: flagged ? "Remove flag" : "Flag", run: () => this.toggleFlagThreads([id]) },
            { icon: "mark_email_unread", label: "Mark as unread", run: () => this.markUnread([id]) },
            { icon: "drive_file_move", label: "Move\u2026", run: () => this.setState({ moveFor: [id] }) },
            { icon: "delete", label: "Delete", danger: true, run: () => this.deleteThreads([id]) },
          ],
        },
      });
    };

    cancelLongPress = () => { if (this.lpTimer) { clearTimeout(this.lpTimer); this.lpTimer = null; } };

    bodyRef = React.createRef();
    composeFootRef = React.createRef();
    userMenuRef = React.createRef();
    evTitleRef = React.createRef();
    evPromptRef = React.createRef();
    evDayRef = React.createRef();
    evTimeRef = React.createRef();
    evEndDayRef = React.createRef();
    evEndTimeRef = React.createRef();
    evNotesRef = React.createRef();
    ctNameRef = React.createRef();
    ctMailRef = React.createRef();
    ctOrgRef = React.createRef();
    ctRoleRef = React.createRef();
    tkPromptRef = React.createRef();
    remNumTaskRef = React.createRef();
    remNumEventRef = React.createRef();
    remNumModalRef = React.createRef();
    tkTitleRef = React.createRef();
    tkDueRef = React.createRef();
    tkEstRef = React.createRef();

    onDocDown = (e) => {
      const el = this.userMenuRef.current;
      if (this.state.userMenu && el && !el.contains(e.target)) this.setState({ userMenu: false });
      /* On the phone the filters band stands over the list rather than beside it, so a press anywhere outside the band
         and its control folds it away as its control would. */
      if (this.state.filtersOpen && this._isMobile && !e.target.closest?.("[data-filters-band],[data-filters-toggle]")) this.setState({ filtersOpen: false });
    };

    tabsMode = () => {
      const mode = this.props.layout ?? "auto";
      const F = FRAMES[mode];
      const w = F ? F.w : (this.state.vw || 1440);
      return w >= 1180 && (this.state.workMode || "tabs") === "tabs";
    };

    tabKind = (key) => key === "compose" ? "compose"
      : key.indexOf("doc:") === 0 ? "doc"
      : key.indexOf("html:") === 0 ? "html" : "thread";

    /* Embedded message content: the frame has an opaque origin (sandbox="allow-scripts" and nothing else),
       so it cannot see the app's DOM, storage or cookies. Height arrives solely
       via postMessage from the measuring script injected before the message markup.

       Acceptance criteria #1507 - named heights:
       • initial (before the first report): 320px, no animation on the first fit,
         so attaching a message does not jump the conversation;
       • fallback (no report within 2.5 s): a 1600px frame in a 620px container with overflow:auto
         - the only case where the embedded view scrolls internally;
       • very long message: no maximum (clamped at 40000px), frame at full height,
         the conversation scrolls, no nested scrollbar. */
    htmlFrames = new Map();

    htmlFrameRef = (f) => {
      if (!f) return;
      const id = f.getAttribute("data-frame-id");
      if (!id) return;
      const prev = this.htmlFrames.get(id);
      if (prev && prev.frame === f) return;
      if (prev && prev.timer) clearTimeout(prev.timer);
      const rec = { frame: f, timer: null };
      this.htmlFrames.set(id, rec);
      rec.timer = setTimeout(() => this.htmlFrameFallback(id), 2500);
    };

    htmlFrameStatus = (id, text) => {
      const el = document.querySelector('[data-fit-status="' + id + '"]');
      if (el) el.textContent = text;
    };

    htmlFrameFallback = (id) => {
      const rec = this.htmlFrames.get(id);
      if (!rec || rec.measured) return;
      rec.fallback = true;
      const wrap = rec.frame.parentElement;
      /* The only case with its own scrolling: no measurement arrived, so we do not know
         the content height. The frame gets 1600px in a 620px container with overflow:auto. */
      rec.frame.style.height = "1600px";
      if (wrap) { wrap.style.height = "620px"; wrap.style.overflow = "auto"; }
      this.htmlFrameStatus(id, "Could not measure the content height - this one frame scrolls separately");
    };

    onHtmlFrameMessage = (e) => {
      const d = e && e.data;
      if (!d || typeof d !== "object") return;
      const id = d.frameId, h = d.height;
      if (typeof id !== "string" || typeof h !== "number" || !isFinite(h)) return;
      const rec = this.htmlFrames.get(id);
      if (!rec || rec.fallback) return;
      if (e.source !== rec.frame.contentWindow) return;
      if (rec.timer) { clearTimeout(rec.timer); rec.timer = null; }
      const next = Math.max(80, Math.min(Math.round(h) + 2, 40000));
      rec.applied = (rec.applied || 0) + 1;
      /* Safety net: after a dozen or so corrections, or on micro-jitter, we stop listening
         so no content can grow the frame indefinitely. */
      if (rec.measured && (rec.applied > 16 || Math.abs(next - rec.height) <= 3)) return;
      rec.measured = true;
      rec.height = next;
      /* No maximum height — the frame grows to the full content, the conversation scrolls. */
      rec.frame.style.height = next + "px";
      this.htmlFrameStatus(id, "HTML content in isolation - scripts and remote resources blocked");
    };

    tabPatch = (tb) => {
      if (!tb) return {};
      if (tb.kind === "doc") return { screen: "mail", doc: tb.att, docZoom: 1, htmlDoc: null };
      if (tb.kind === "html") return { screen: "mail", doc: null, htmlDoc: tb.htmlDoc, htmlSrcMode: false };
      if (tb.kind === "compose") return { screen: "mail", doc: null, htmlDoc: null, compose: true };
      return { screen: "mail", threadId: tb.id, mailPane: "thread", threadOpen: true, doc: null, htmlDoc: null };
    };

    openTab = (key, patch, extra) => {
      this.setState(s => {
        const tabs = s.tabs || [];
        const kind = this.tabKind(key);
        const id = key.indexOf(":") > 0 ? key.slice(key.indexOf(":") + 1) : key;
        const next = tabs.some(t => t.key === key) ? tabs : tabs.concat([Object.assign({ key, kind, id }, extra || {})]);
        return Object.assign({ tabs: next, activeTab: key }, patch || {});
      });
    };

    closeTab = (key) => {
      this.setState(s => {
        const tabs = (s.tabs || []).filter(t => t.key !== key);
        const wasActive = s.activeTab === key;
        const nextTab = wasActive ? (tabs.length ? tabs[tabs.length - 1] : null) : null;
        const patch = { tabs, activeTab: wasActive ? (nextTab ? nextTab.key : null) : s.activeTab };
        if (key === "compose") Object.assign(patch, { compose: false, composeCtx: [], composeCtxBase: null, replyThreadId: null });
        if (key.indexOf("doc:") === 0) patch.doc = null;
        if (key.indexOf("html:") === 0) patch.htmlDoc = null;
        if (wasActive && nextTab) Object.assign(patch, this.tabPatch(nextTab));
        return patch;
      });
    };

    remList = (key) => {
      const map = this.state.remMap || {};
      return map[key] || [];
    };

    remToggle = (key, m) => this.setState(s => {
      const map = Object.assign({}, s.remMap || {});
      const cur = map[key] || [];
      map[key] = cur.indexOf(m) >= 0 ? cur.filter(x => x !== m) : cur.concat(m).sort((a, b) => a - b);
      return { remMap: map };
    });

    remSet = (key, list) => this.setState(s => {
      const map = Object.assign({}, s.remMap || {});
      map[key] = list.slice();
      return { remMap: map };
    });

    remAddCustom = (key, ref) => {
      const el = ref && ref.current;
      const n = parseInt((el && el.value) || "", 10);
      if (!n || n < 1) return;
      const unit = this.state.remUnit || "min";
      const m = unit === "days" ? n * 1440 : unit === "hrs" ? n * 60 : n;
      if (el) el.value = "";
      if (this.remList(key).indexOf(m) < 0) this.remToggle(key, m);
    };

    remBlock = (key, o) => {
      const list = this.remList(key).slice().sort((a, b) => b - a);
      const unit = this.state.remUnit || "min";
      const timed = !!o.timed;
      const zero = o.zero || (timed ? "at the time" : "on the day");
      const chip = (on) => "display:flex;align-items:center;gap:5px;height:30px;padding:0 11px;border-radius:8px;font-size:13px;white-space:nowrap;border:1px solid " +
        (on ? "var(--accent);background:var(--accent-soft);color:var(--accent-d);font-weight:600" : "var(--line);background:var(--panel);color:var(--text2)");
      return {
        any: list.length > 0,
        hint: o.hint || "",
        countLabel: list.length === 0 ? "off" : list.length === 1 ? "1 reminder" : list.length + " reminders",
        countStyle: "font-size:11px;padding:2px 8px;border-radius:12px;" + (list.length ? "background:var(--accent-soft);color:var(--accent-d)" : "background:var(--hover);color:var(--muted)"),
        presets: (timed ? REM_TIMED : REM_DATED).map(m => {
          const on = list.indexOf(m) >= 0;
          return { label: remLabel(m, zero), icon: on ? "check" : "add", style: chip(on), toggle: () => this.remToggle(key, m) };
        }),
        list: list.map(m => ({ label: remLabel(m, zero), remove: () => this.remToggle(key, m) })),
        numRef: o.numRef,
        units: ["min", "hrs", "days"].map(u => ({
          label: u,
          pick: () => this.setState({ remUnit: u }),
          style: "display:flex;align-items:center;height:26px;padding:0 10px;font-size:12px;border-radius:8px;" +
            (unit === u ? "background:var(--accent);color:var(--onaccent);font-weight:600" : "color:var(--text2)"),
        })),
        add: () => this.remAddCustom(key, o.numRef),
      };
    };

    remAnchor = (o) => (o.time && o.time !== "all day"
      ? "before " + o.time + (o.day ? " · " + o.day + ".08" : "")
      : "09:00 on the day of the " + (o.taskLike ? "deadline" : "event"));

    remFire = (title, list, kind, src, go) => {
      if (!list || !list.length) return;
      this._fired = this._fired || {};
      if (this._fired[title]) return;
      this._fired[title] = true;
      const m = Math.min.apply(null, list);
      this.remTimers = this.remTimers || [];
      this.remTimers.push(setTimeout(() => this.pushNotif({
        kind: kind,
        title: "Reminder: " + title,
        body: m === 0 ? "Starting now." : remLabel(m, "").replace(" before", "") + " left.",
        src: src,
        go: go,
      }), 18000));
    };

    goScreen = (id, patch) => {
      /* The first entry into mail starts with an empty panel; after that the open message
         is remembered for the session (in-memory state - gone when the app closes). */
      const p = Object.assign({ screen: id, replyChooser: null }, patch || {});
      if (id !== "mail" && this.state.resultFilter) p.resultFilter = null;
      /* Entering mail from another screen = fetching the list from the server. */
      if (id === "mail" && this.state.screen !== "mail") {
        if (p.mailPane === "thread") { p.msgLoading = true; this.loadMsg(); }
        this.loadList(p);
        return;
      }
      this.setState(p);
    };

    notifAll = () => this.state.notifs || NOTIFS;
    notifUnread = () => this.notifAll().filter(n => isNotifUnread(n, this.state.notifRead || {}));

    toggleNotifRead = (n, e) => {
      if (e && e.stopPropagation) e.stopPropagation();
      const read = Object.assign({}, this.state.notifRead || {});
      read[n.id] = isNotifUnread(n, read);
      this.setState({ notifRead: read });
    };

    openNotifCenter = () => {
      clearTimeout(this._notifT);
      this.setState({ notifCenter: true, notifShown: false, notifDrag: 0, userMenu: false });
      this._notifT = setTimeout(() => this.setState({ notifShown: true }), 20);
    };

    closeNotifCenter = () => {
      if (!this.state.notifCenter) return;
      clearTimeout(this._notifT);
      this._notifG = null;
      this.notifGestDetach();
      this.setState({ notifShown: false });
      this._notifT = setTimeout(() => this.setState({ notifCenter: false, notifDrag: 0 }), 280);
    };

    toggleNotifCenter = () => {
      if (this.state.notifCenter) this.closeNotifCenter(); else this.openNotifCenter();
    };

    /* TWO GESTURES ACROSS THE WHOLE SURFACE (phone, coarse pointer).
       "open"  - an upward move started anywhere on the nav bar pulls the panel out.
       "close" - a downward move started anywhere on the open panel (header, filters,
                 row, empty state) pushes it away. The panel follows the finger 1:1 and the
                 scrim opacity is tied directly to the distance travelled. Tapping the bell is unchanged. */
    notifPanelRef = React.createRef();
    notifListRef = React.createRef();

    notifPanelH = () => {
      const el = this.notifPanelRef.current;
      return (el && el.offsetHeight) || Math.max(320, (this.state.vh || 844) - this.barH());
    };

    notifSetDrag = (px) => { if ((this.state.notifDrag || 0) !== px) this.setState({ notifDrag: px }); };

    notifGestDetach = () => {
      document.removeEventListener("pointermove", this._notifMove);
      document.removeEventListener("pointerup", this._notifUp);
      document.removeEventListener("pointercancel", this._notifUp);
      document.removeEventListener("touchmove", this._notifMove);
      document.removeEventListener("touchend", this._notifUp);
    };

    notifGest = (mode) => (e) => {
      if (mode === "close" && (this.state.ctxMenu || this.lpJustFired())) return;
      if (mode === "open" && this.state.notifCenter) return;
      this.notifGestDetach();
      const t = (e.touches && e.touches[0]) || e;
      const g = { mode: mode, y0: t.clientY, yPrev: t.clientY, tPrev: Date.now(), v: 0, live: false, anchor: 0 };
      this._notifG = g;
      if (mode === "open") this._notifBarTap = false;

      this._notifMove = (ev) => {
        if (!this._notifG) return;
        const p = (ev.touches && ev.touches[0]) || ev;
        const now = Date.now();
        g.v = (p.clientY - g.yPrev) / Math.max(1, now - g.tPrev);
        g.yPrev = p.clientY; g.tPrev = now;
        const dy = p.clientY - g.y0;

        if (mode === "open") {
          if (!g.live) {
            if (dy > NG.barSlop) { this.notifGestDetach(); this._notifG = null; return; }
            if (-dy < NG.barSlop) return;
            /* The bar hands over the gesture: only now does its own touch get cancelled. */
            g.live = true; g.anchor = p.clientY; this._notifBarTap = true;
            const H = this.notifPanelH();
            this.setState({ notifCenter: true, notifShown: true, notifDrag: H, notifH: H, userMenu: false, more: false });
            return;
          }
          const H = this.state.notifH || this.notifPanelH();
          if (ev.cancelable) ev.preventDefault();
          this.notifSetDrag(Math.max(0, H - Math.max(0, Math.min(H, g.anchor - p.clientY))));
          return;
        }

        if (!g.live) {
          /* Movement beyond the row slop cancels the long press — one or the other, never both. */
          if (Math.abs(dy) > NG.rowSlop) clearTimeout(this._lpT);
          if (dy < NG.rowSlop) return;
          const list = this.notifListRef.current;
          if (list && list.scrollTop > 0.5) { g.y0 = p.clientY; return; }
          /* The list is at the top — the same uninterrupted move becomes a panel dismissal. */
          g.live = true; g.anchor = p.clientY;
          this.setState({ notifH: this.notifPanelH() });
          return;
        }
        if (ev.cancelable) ev.preventDefault();
        this.notifSetDrag(Math.max(0, p.clientY - g.anchor));
      };

      this._notifUp = () => {
        this.notifGestDetach();
        const gg = this._notifG;
        this._notifG = null;
        if (!gg) return;
        if (!gg.live) { this.forceUpdate(); return; }
        const H = this.state.notifH || this.notifPanelH();
        const drag = this.state.notifDrag || 0;
        const travelFrac = mode === "close" ? drag / H : 1 - drag / H;
        const flick = mode === "close" ? g.v > NG.commitVel : g.v < -NG.commitVel;
        const back = mode === "close" ? g.v < -NG.commitVel : g.v > NG.commitVel;
        const commit = !back && (travelFrac > NG.commitFrac || flick);
        if (mode === "close") { if (commit) this.closeNotifCenter(); else this.notifSpringBack(); }
        else if (commit) this.notifSpringBack(); else this.closeNotifCenter();
      };

      document.addEventListener("pointermove", this._notifMove);
      document.addEventListener("pointerup", this._notifUp);
      document.addEventListener("pointercancel", this._notifUp);
      document.addEventListener("touchmove", this._notifMove, { passive: false });
      document.addEventListener("touchend", this._notifUp);
    };

    /* Spring back when neither distance nor velocity was met. */
    notifSpringBack = () => this.setState({ notifDrag: 0, notifSpring: true }, () => {
      clearTimeout(this._notifSpringT);
      this._notifSpringT = setTimeout(() => this.setState({ notifSpring: false }), NG.springMs);
    });

    /* An upward move on the bar takes the touch away from the element it started on. */
    railTapGuard = (e) => {
      if (!this._notifBarTap) return;
      this._notifBarTap = false;
      if (e && e.preventDefault) { e.preventDefault(); e.stopPropagation(); }
    };

    markAllNotifsRead = () => {
      const read = Object.assign({}, this.state.notifRead || {});
      this.notifAll().forEach(n => { read[n.id] = true; });
      this.setState({ notifRead: read });
    };

    openNotif = (n) => {
      const read = Object.assign({}, this.state.notifRead || {});
      read[n.id] = true;
      this.setState({ notifRead: read });
      this.closeNotifCenter();
      if (!n.go) return;
      if (n.go.thread) this.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: n.go.thread, cameFrom: false, sel: [] });
      else this.goScreen(n.go.screen, n.go.screen === "mail" ? { mailPane: "list" } : {});
    };

    pushNotif = (it) => {
      const id = "in" + (this._nSeq = (this._nSeq || 0) + 1);
      const n = Object.assign({}, it, { id: id, unread: true, mins: 0 });
      this.setState(s => ({ notifs: [n].concat(s.notifs || NOTIFS), notifAnim: Object.assign({}, s.notifAnim, { [id]: "in" }) }));
      const clr = setTimeout(() => this.setState(s => {
        const m = Object.assign({}, s.notifAnim); delete m[id]; return { notifAnim: m };
      }), 700);
      this.timers.push(clr);
      const kind = (NKIND[it.kind] || NKIND.system).toast;
      let tid;
      tid = this.notify(it.body, {
        kind: kind, title: it.title, actionLabel: "Show",
        onAction: () => { this.dismissToast(tid); this.openNotif(n); },
      });
      return n;
    };

    /* Row-level animation flags. Only the touched tile animates — the list is never re-rendered
       as a whole, so scroll position and every other row stay exactly where they were. */
    setRowAnim = (id, kind, ms) => {
      this.setState(s => ({ rowAnim: Object.assign({}, s.rowAnim, { [id]: kind }) }));
      const t = setTimeout(() => this.setState(s => {
        const m = Object.assign({}, s.rowAnim); delete m[id]; return { rowAnim: m };
      }), ms);
      this.timers.push(t);
    };

    /* "out" is the feedback for a removal the person performed — archive, delete, purge. The row
       collapses under the gesture that removed it, then the state change lands. A row that goes
       because the mailbox was re-read never plays it: that arrives as a new list, not as an act. */
    animateRowsOut = (ids, commit, tone) => {
      const list = (ids || []).filter(Boolean);
      if (!list.length) { commit(); return; }
      const kind = tone === "del" ? "outdel" : tone === "arc" ? "outarc" : "out";
      list.forEach(id => this.setRowAnim(id, kind, 460));
      this.timers.push(setTimeout(commit, 380));
    };

    markUnread = (id) => this.setState(s => ({ unread: Object.assign({}, s.unread || DEFAULT_UNREAD, { [id]: true }) }));

    /* The global refresh — re-reads the mail in front of the reader and the notification centre.
       Waiting is a pulse, never a spinner. What a re-read turns up in the prototype is the staged
       batch: a message that arrived, one that changed, one that is no longer where it was. */
    refreshAll = () => {
      if (this.state.refreshBusy) return;
      const step = (this._demoStep = (this._demoStep || 0));
      this._demoStep = step + 1;
      const b = DEMO_BATCHES[step % DEMO_BATCHES.length];
      const prev = this._demoLastId;
      this.setState({ refreshBusy: true });

      /* 1 · a new message lands at the top of the list and in the notification centre */
      const id = "demo" + (step + 1);
      THREADS.unshift(Object.assign({ id: id }, b.mail));
      this._demoLastId = id;
      this.markUnread(id);
      this.setRowAnim(id, "in", 720);
      this.pushNotif({ kind: "mail", title: b.mail.from + " - new message", body: b.notif,
        src: "Mail · " + (b.mail.org || "Inbox"), go: { screen: "mail", thread: id } });

      /* 2 · a thread already on the list gets a reply — the same tile updates in place */
      const t2 = setTimeout(() => {
        const th = THREADS.find(x => x.id === b.edit.id);
        if (th && (this.state.deleted || []).indexOf(th.id) < 0) {
          /* the reply joins the conversation after its state was derived — the reading is now behind */
          Object.assign(th, b.edit.patch, { stateStale: true });
          this.markUnread(th.id);
          this.setRowAnim(th.id, "edit", 1700);
          this.pushNotif({ kind: "mail", title: b.editNotif.title, body: b.editNotif.body,
            src: "Mail · " + (th.org || "Inbox"), go: { screen: "mail", thread: b.editNotif.thread } });
        }
      }, 1000);

      /* 3 · one message is no longer where it was — the re-read simply returns a list without it,
         so no leaving animation plays: "out" belongs to removals the person performed. */
      const t3 = setTimeout(() => {
        const done = (this.state.deleted || []);
        const gone = prev && done.indexOf(prev) < 0 ? prev : (done.indexOf(b.fallbackRemove) < 0 ? b.fallbackRemove : null);
        if (!gone) { this.setState({ refreshBusy: false, lastSync: Date.now() }); return; }
        this.notify(b.removeNote, false);
        this.setState(s => ({ deleted: (s.deleted || []).concat([gone]), refreshBusy: false, lastSync: Date.now() }));
      }, 2050);
      this.timers.push(t2, t3);
    };

    simulateNotif = () => {
      const i = (this._inSeq = (this._inSeq || 0)) % INCOMING.length;
      this._inSeq = i + 1;
      this.pushNotif(INCOMING[i]);
    };

    exec = (cmd, arg) => {
      const el = this.bodyRef.current;
      if (el) el.focus();
      try { document.execCommand(cmd, false, arg || null); } catch (e) {}
    };

    setBodyHtml = (html) => {
      const el = this.bodyRef.current;
      if (el) el.innerHTML = html;
    };

    aiWrite = (kind) => {
      const el = this.bodyRef.current;
      if (!el) return;
      this.aiUndo = el.innerHTML;
      const full = COMPOSE_DRAFTS.full;
      const shortV = COMPOSE_DRAFTS.short;
      const paras = kind === "short" ? shortV : full;
      el.innerHTML = "";
      const block = document.createElement("div");
      block.id = "ai-draft-block";
      block.style.borderLeft = "3px solid var(--accent)";
      block.style.paddingLeft = "14px";
      block.style.marginLeft = "-14px";
      el.appendChild(block);
      this.setState({ aiBusy: true, aiDraft: false });
      const step = Math.max(120, (this.props.streamDelay ?? 550) * 0.55);
      paras.forEach((p, i) => {
        this.timers.push(setTimeout(() => {
          const isLast = i === paras.length - 1;
          const node = document.createElement("p");
          node.style.margin = isLast ? "12px 0 0 0" : "0 0 12px 0";
          node.innerHTML = p;
          (isLast ? el : block).appendChild(node);
          if (isLast) this.setState({ aiBusy: false, aiDraft: true });
        }, step * (i + 1)));
      });
    };

    aiRevert = () => {
      if (typeof this.aiUndo === "string") this.setBodyHtml(this.aiUndo);
      this.setState({ aiDraft: false });
    };

    unmarkAiDraft = () => {
      const el = this.bodyRef.current;
      const block = el && el.querySelector("#ai-draft-block");
      if (block) {
        block.style.borderLeft = "none";
        block.style.paddingLeft = "0";
        block.style.marginLeft = "0";
        block.removeAttribute("id");
      }
    };

    pushMsg = (s2, msgs) => {
      const convs = (s2.convs || []).slice();
      let id = s2.convId;
      let i = convs.findIndex(c => c.id === id);
      if (i < 0) {
        id = "c" + Date.now();
        convs.push({ id, title: "", when: "now", msgs: [] });
        i = convs.length - 1;
      }
      const conv = Object.assign({}, convs[i]);
      conv.msgs = conv.msgs.concat(msgs.map(m => Object.assign({ ts: Date.now() }, m)));
      if (!conv.title) {
        const first = msgs.find(m => m.role === "user");
        if (first) conv.title = first.text.length > 34 ? first.text.slice(0, 34) + "…" : first.text;
      }
      convs[i] = conv;
      return { convs, convId: id };
    };

    autoSchedule = () => {
      this.setState(s2 => {
        const todo = TASKS.concat(s2.extraTasks || []).filter(t2 => t2.group === "Today" && (s2.taskDone || []).indexOf(t2.id) < 0 && (s2.taskSched || []).indexOf(t2.id) < 0);
        let h = 11;
        const blocks = todo.map(t2 => {
          const slot = { day: "27", time: (h < 10 ? "0" : "") + h + ":00", h, t: t2.t, kind: "meeting", src: "task" };
          h += 2;
          return slot;
        });
        return {
          taskSched: (s2.taskSched || []).concat(todo.map(t2 => t2.id)),
          calAdded: (s2.calAdded || []).concat(blocks),
        };
      });
    };

    /* Real-time playback of the scripted example. One timer at a time so the run can be skipped
       or interrupted at any point; every arrival scrolls the thread and animates in. */
    demoAt = 0;

    demoSpeed = () => Math.max(0.3, Math.min(2, (this.props.streamDelay ?? 550) / 550));

    /* Every real example plays the same way; which one is playing is `this.demo`. */
    playDemo = (demo) => {
      const d = demo && demo.script ? demo : { id: DEMO_CONV_ID, title: "Real example - contract addendum", script: DEMO_SCRIPT };
      this.demo = d;
      clearTimeout(this.demoT);
      this.demoAt = 0;
      this.setState(s2 => ({
        convs: (s2.convs || []).filter(c => c.id !== d.id)
          .concat([{ id: d.id, title: d.title, when: "now", msgs: [] }]),
        screen: "agent", convId: d.id, agentInput: "", agentThinking: false, agentStatus: "", demoRun: true,
      }));
      this.startAgentPin();
      this.demoNext();
    };

    demoNext = () => {
      const step = (this.demo ? this.demo.script : DEMO_SCRIPT)[this.demoAt];
      if (!step) { this.stopAgentPin(); this.setState({ agentThinking: false, agentStatus: "", demoRun: false }); return; }
      this.demoT = setTimeout(() => {
        this.demoAt += 1;
        this.demoApply(step, false);
        this.demoNext();
      }, Math.round((step.d || 600) * this.demoSpeed()));
      this.timers.push(this.demoT);
    };

    demoApply = (step, instant) => {
      this.setState(s2 => {
        const convs = (s2.convs || []).slice();
        const i = convs.findIndex(c => c.id === (this.demo ? this.demo.id : DEMO_CONV_ID));
        if (i < 0) return null;
        const conv = Object.assign({}, convs[i]);
        const msgs = conv.msgs.slice();
        const onLast = (fn) => {
          if (!msgs.length) return;
          const m = Object.assign({}, msgs[msgs.length - 1]);
          fn(m);
          msgs[msgs.length - 1] = m;
        };
        if (step.u) msgs.push({ role: "user", text: step.u, ts: Date.now(), anim: !instant });
        if (step.b) msgs.push(Object.assign({ role: "bot", ts: Date.now(), anim: !instant, bullets: [], blocks: [] }, step.b));
        if (step.t) onLast(m => { m.text = m.text ? m.text + " " + step.t : step.t; });
        /* A longer answer lands one whole paragraph at a time — never letter by letter. */
        if (step.p) onLast(m => { m.paras = (m.paras || []).concat([{ t: step.p, anim: !instant }]); });
        if (step.n) onLast(m => { m.next = step.n.slice(0, 3); });
        if (step.k) onLast(m => { m.blocks = (m.blocks || []).concat([Object.assign({ anim: !instant }, step.k)]); });
        conv.msgs = msgs;
        convs[i] = conv;
        const out = { convs };
        /* The indicator stays up for the whole run — content landing is not the end of the work,
           so it only goes away when there is nothing left to load. */
        if (step.s) { out.agentStatus = step.s; out.agentThinking = true; }
        else { out.agentThinking = !!s2.demoRun; }
        return out;
      });
      if (!instant) this.scrollAgentDown();
    };

    /* Cancel stops the playback or a pending reply where it stands — nothing is rolled back,
       what already arrived stays, and the thread says the run was stopped. */
    agentCancel = () => {
      if (!this.state.agentThinking && !this.state.demoRun) return;
      this.stopAgentPin();
      clearTimeout(this.demoT);
      clearTimeout(this._askT);
      this.demoAt = Infinity;
      this.setState(s2 => Object.assign(
        { agentThinking: false, agentStatus: "", demoRun: false },
        this.pushMsg(s2, [{ role: "bot", anim: true, scope: "stopped by you", text: "Stopped. What arrived so far stays - tell me where to pick it up.", bullets: [], blocks: [] }])
      ));
      this.scrollAgentDown();
    };

    demoStop = () => {
      this.stopAgentPin();
      clearTimeout(this.demoT);
      if (this.state.demoRun) this.setState({ demoRun: false, agentThinking: false, agentStatus: "" });
    };

    /* Proposed next actions — only when the agent has stopped working, only from the last reply,
       never more than three. */
    nextActs = (msgs, st) => {
      const lm = msgs && msgs.length ? msgs[msgs.length - 1] : null;
      if (st.agentThinking || st.demoRun || !lm || lm.role !== "bot") return [];
      return (lm.next || []).slice(0, 3).map(label => ({
        label,
        aria: "Ask the agent to: " + label,
        iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1;color:var(--accent-d)",
        style: "display:inline-flex;align-items:center;gap:7px;font-size:13px;color:var(--text2);background:var(--panel);border:1px solid var(--border2);border-radius:8px;padding:7px 12px 7px 10px;text-align:left",
        run: () => this.agentAsk(label),
        key: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); this.agentAsk(label); } },
      }));
    };

    /* Keeping the tail in view. Two rAFs so the freshly inserted block has been laid out before we
       measure; smooth only for short hops, an instant jump when a big widget would otherwise leave
       the status line below the fold; and we stay out of the way once the user scrolls up. */
    scrollAgentDown = () => {
      cancelAnimationFrame(this._agRaf);
      this._agRaf = requestAnimationFrame(() => requestAnimationFrame(this.pinAgentBottom));
    };

    pinAgentBottom = () => {
      const el = this.agentScrollRef.current;
      if (!el || this._agAway) return;
      const gap = el.scrollHeight - el.clientHeight - el.scrollTop;
      if (gap <= 2) return;
      if (gap > el.clientHeight * 0.85 || !el.scrollTo) el.scrollTop = el.scrollHeight;
      else el.scrollTo({ top: el.scrollHeight, behavior: "smooth" });
    };

    startAgentPin = () => {
      this._agAway = false;
      clearInterval(this._agPin);
      this._agPin = setInterval(this.pinAgentBottom, 140);
    };

    stopAgentPin = () => {
      clearInterval(this._agPin);
      this.scrollAgentDown();
    };

    onAgentWheel = () => {
      const el = this.agentScrollRef.current;
      if (!el) return;
      this._agAway = el.scrollHeight - el.clientHeight - el.scrollTop > 40;
    };

    /* A bar standing over a passage would be left behind by the scroll; one docked above the field is not. */
    closeFloatingSelBar = () => { if (this.state.selBar && !this.state.selBar.answer && !this._passageDocked) { this.dropSelection(); this.setState({ selBar: null }); } };
    onStartScroll = () => this.closeFloatingSelBar();
    openWatched = (w) => { this.setState(s2 => ({ watched: (s2.watched || []).map(x => (x.id === w.id && x.state === "changed" ? Object.assign({}, x, { state: "same", note: "opened just now" }) : x)) })); this.run(w.q); };
    onAgentScroll = (e) => {
      const el = e.target;
      this.closeFloatingSelBar();
      if (el.scrollHeight - el.clientHeight - el.scrollTop < 40) this._agAway = false;
    };

    agentAsk = (text) => {
      const q = (text || "").trim();
      if (!q) return;
      const quote = this.state.agentQuote;
      this.demoStop();
      this.setState(s2 => Object.assign(
        { screen: "agent", agentInput: "", agentThinking: true, agentQuote: null },
        this.pushMsg(s2, [Object.assign({ role: "user", anim: true, text: q }, quote ? { quote: quote.text } : {})])
      ));
      this.startAgentPin();
      this._askT = setTimeout(() => {
        const hit = AGENT_REPLIES.find(r => r.match.test(q)) || AGENT_FALLBACK;
        this.setState(s2 => Object.assign(
          { agentThinking: false },
          this.pushMsg(s2, [{ role: "bot", anim: true, scope: hit.scope, text: hit.text, bullets: hit.bullets || [], blocks: hit.blocks || [], next: hit.next || NEXT_DEFAULT }])
        ));
        this.stopAgentPin();
      }, Math.max(500, (this.props.streamDelay ?? 550) * 1.1));
      this.timers.push(this._askT);
    };

    /* The bar a selection opens. It stands over the passage on a fine pointer and docks above the field on touch,
       where the system's own selection menu already sits over the passage. */
    selBarView = (st, touch, convMsgs) => {
      const sb = st.selBar;
      /* A step on Start keys its draft as `start#<step>`; the Agent's as `<conversation>#<message>#<block>`. */
      const startDraft = !!sb && sb.kind === "draft" && sb.key.indexOf("start#") === 0;
      const bar = sb && (sb.kind === "compose" ? st.compose && !st.composeMin : st.screen === (startDraft ? "start" : "agent")) ? sb : null;
      this._passageDocked = touch;
      if (!bar || !bar.rect) return { passageBarShown: false };
      /* The composer's selection is a draft too, rewritten in the editor rather than in a block. */
      const inCompose = bar.kind === "compose";
      const isDraft = bar.kind === "draft" || inCompose;
      const parts = bar.kind === "draft" ? bar.key.split("#") : [];
      const msg = bar.kind === "draft" && !startDraft ? convMsgs[+parts[1]] : null;
      const blk = startDraft ? HOME.steps[+parts[1]] : msg && (msg.blocks || [])[+parts[2]];
      if (bar.kind === "draft" && !blk) return { passageBarShown: false };
      const r = bar.rect, W = isDraft ? 440 : 400;
      const above = r.top > 300;
      const pos = touch
        ? (r.inputWidth ? "left:" + Math.round(r.inputLeft) + "px;width:" + Math.round(r.inputWidth) + "px;" : "left:10px;right:10px;") + "bottom:" + Math.round(r.height - r.inputTop + 8) + "px;"
        : "width:" + W + "px;left:" + Math.round(Math.max(12, Math.min(r.left, r.width - W - 12))) + "px;" + (above ? "top:" + Math.round(r.top - 10) + "px;transform:translateY(-100%);" : "top:" + Math.round(r.bottom + 10) + "px;");
      const keyer = (run) => (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); run(); } };
      const chip = "display:inline-flex;align-items:center;gap:6px;font-size:13px;color:var(--text2);background:var(--panel);border:1px solid var(--border2);border-radius:999px;white-space:nowrap;box-sizing:border-box;" + (touch ? "min-height:36px;padding:0 13px" : "padding:6px 11px");
      const icon = (n) => "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1;color:var(--muted)";
      const act = (label, ic, run, aria) => ({ label, icon: ic, run, key: keyer(run), aria: aria || label, style: chip, iconStyle: icon() });
      const ans = bar.answer || null;
      const ask = () => (inCompose ? this.rewriteComposeAsk() : this.rewriteAsk(blk));
      return {
        passageBarShown: true,
        passageBarIsDraft: isDraft,
        passageBarIsText: !isDraft,
        passageBarAria: inCompose ? "Rewrite the selected part of the message" : isDraft ? "Rewrite the selected part of the draft" : "Do something with the selected passage",
        passageBarTitle: isDraft ? "Rewrite this part" : "Selected from " + bar.from,
        passageBarIcon: isDraft ? "edit_note" : "format_quote",
        passageBarQuote: "“" + (bar.text || "") + "”",
        passageBarStyle: "position:fixed;z-index:90;" + pos + "box-sizing:border-box;display:flex;flex-direction:column;gap:10px;background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:12px 12px 12px 14px;box-shadow:0 18px 44px var(--sh-3),0 1px 0 var(--inset) inset",
        passageBarKeep: (e) => { if (e.target.tagName !== "INPUT") e.preventDefault(); },
        passageBarKey: (e) => { if (e.key === "Escape") this.closeSelBar(); },
        passageCloseBar: this.closeSelBar,
        passagePresets: isDraft ? REWRITE_PRESETS.map(pr => act(pr.label, pr.icon, () => (inCompose ? this.rewriteCompose(pr.key, pr.label) : this.rewriteSel(blk, pr.key, pr.label)), "Rewrite the selection: " + pr.label.toLowerCase())) : [],
        /* The composer and Start have no conversation to quote a passage into, so they offer the rewrite alone. */
        passageQuoteShown: !inCompose && !startDraft,
        passageAskValue: st.selAsk || "",
        passageOnAsk: (e) => this.setState({ selAsk: e.target.value }),
        passageAskKey: (e) => { if (e.key === "Enter") ask(); if (e.key === "Escape") this.closeSelBar(); },
        passageAskRun: ask, passageAskRunKey: keyer(ask),
        passageAskRowStyle: "display:flex;align-items:center;gap:8px;background:var(--sub);border:1px solid var(--line2);border-radius:10px;padding:" + (touch ? "4px 4px 4px 12px" : "4px 4px 4px 11px"),
        passageAskPlaceholder: touch && r.width < 700 ? "Or say how…" : "Or say how - e.g. mention the 2023 addendum",
        passageCloseSize: touch ? "width:44px;height:44px;margin:-10px -8px -10px 0;" : "width:28px;height:28px;",
        passageAskBtnStyle: "font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:8px;white-space:nowrap;box-sizing:border-box;display:inline-flex;align-items:center;" + (touch ? "min-height:44px;padding:0 14px" : "padding:6px 12px"),
        /* Alone on its row it is a button rather than a chip, so it takes the full touch target. */
        passageQuoteAct: Object.assign(act("Ask about this", "format_quote", this.quoteSel, "Quote the selection in the field and ask about it"), { style: chip.replace("min-height:36px", "min-height:44px") }),
        passageActs: isDraft ? [] : [
          act("Ask about this", "format_quote", this.quoteSel, "Quote the passage in the field and ask about it"),
          act("Explain", "lightbulb", () => this.answerSel("explain"), "Explain the passage here"),
          act("Check against the sources", "fact_check", () => this.answerSel("check"), "Check the passage against the sources, here"),
          act("Make it a task", "task_alt", this.selToTask, "Propose a task from the passage"),
        ],
        passageHasAnswer: !!ans,
        passageAnswerBusy: !!(ans && ans.busy),
        passageAnswerReady: !!(ans && !ans.busy),
        passageAnswerLabel: ans ? SEL_REPLIES[ans.kind].label : "",
        passageAnswerText: ans && ans.text ? ans.text : "",
        passageAnswerStyle: "display:flex;flex-direction:column;gap:8px;background:var(--sub);border:1px solid var(--line2);border-radius:10px;padding:11px 12px",
        passageSkel: [96, 82, 58].map(w => ({ style: SHIM + "height:9px;border-radius:5px;width:" + w + "%" })),
        passageKeepAnswer: this.keepSelAnswer, passageKeepAnswerKey: keyer(this.keepSelAnswer),
        passageAnswerBtnStyle: "align-self:flex-start;font-size:12.5px;color:var(--accent-d);border:1px solid var(--accent-line);border-radius:8px;box-sizing:border-box;display:inline-flex;align-items:center;" + (touch ? "min-height:44px;padding:0 12px" : "padding:5px 10px"),
        passageRowStyle: "display:flex;gap:" + (touch ? 8 : 6) + "px;" + (touch && r.width < 700 ? "overflow-x:auto;flex-wrap:nowrap;scrollbar-width:none" : "flex-wrap:wrap"),
      };
    };

    /* One block, drawn exactly as the block catalogue draws it — plus, for the actionable types, the controls
       and the pending / accepted / declined phase. */
    bkView = (b, key, isMobile, surface, touch) => {
      const st = this.state;
      /* Start takes one step and never sends: no *Another time*, which is a conversation, and a draft opens in the
         composer instead of being sent from here. Its fields and a draft's sentences change exactly as the Agent's do. */
      const onStart = surface === "start";
      /* A field changed on its own is laid over the proposal, and accepting takes the changed values. */
      const vals = (st.bkVals || {})[key] || {};
      const FIELD_KEY = { When: "when", Duration: "duration", Due: "due", Reminder: "reminder" };
      const bv = Object.keys(vals).length ? Object.assign({}, b, ...Object.keys(vals).map(k => ({ [FIELD_KEY[k]]: vals[k] })), vals.When ? { conflict: "" } : {}) : b;
      const accept = () => (onStart ? this.acceptStartStep(key, bv) : this.acceptBk(key, bv));
      const change = (st.bkChange || {})[key] || null;
      const dParas = b.type === "draft" ? this.draftParas(key, b) : [];
      const phase = (st.bkPhase || {})[key] || b.phase || null;
      const bstate = (st.bkState || {})[key] || b.state || "ready";
      const editRaw = (st.bkEdit || {})[key];
      const editing = typeof editRaw === "string";
      const LABEL = { answer: "Answer", timeline: "Timeline", evidence: "Evidence", threadstate: "Thread state", facttable: "Fact table", attachments: "Attachments", people: "People", trace: "How I got here", draft: "Draft", event: "Event proposal", task: "Task proposal", action: "Suggested action" };
      const declined = phase === "declined";
      const accepted = phase === "accepted";
      const failed = accepted && !!b.failed;
      const ready = bstate === "ready" || bstate === "partial";

      const icon = (size, color) => "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:" + size + "px;line-height:1;flex:0 0 auto;color:" + color;
      const tone = declined ? "declined" : phase === "pending" ? "pending" : "settled";
      const kStyle = "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--muted);font-weight:500";
      const vStyle = "min-width:0;font-size:14px;color:var(--text);text-wrap:pretty";
      const btn = (variant) => BLK.btn(isMobile, variant === "primary") + (touch ? "min-height:44px;" : "");
      const iconTile = (bg, color) => "display:inline-flex;align-items:center;justify-content:center;width:32px;height:32px;flex:0 0 32px;border-radius:9px;background:" + bg + ";font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1;color:" + color;
      const keyer = (run) => (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); run(); } };
      /* What the reader can ask of one item. Start's steps do not take these. */
      const live = !onStart && ready;
      const itemAct = (label, ic, run, aria) => ({ label, icon: ic, run, key: keyer(run), aria: aria || label,
        style: "display:inline-flex;align-items:center;gap:5px;font-size:12.5px;color:var(--accent-d);border-radius:7px;white-space:nowrap;" + (touch ? "min-height:44px;box-sizing:border-box;padding:0 10px" : "padding:3px 7px") + ";margin-left:" + (touch ? -10 : -7) + "px",
        iconStyle: icon(15, "var(--accent-d)") });
      const actsStyle = "display:flex;flex-wrap:wrap;gap:" + (isMobile ? "0 14px" : "0 12px") + ";margin-left:" + (isMobile ? 0 : 28) + "px";
      const noteOf = (nkey, indent) => {
        const n = (st.bkNote || {})[nkey];
        const drop = () => this.dropNote(nkey);
        return {
          hasNote: !!n, noteBusy: !!(n && n.busy), noteReady: !!(n && !n.busy),
          noteLabel: n ? "AI · " + n.label : "", noteText: (n && n.text) || "",
          dropNote: drop, dropNoteKey: keyer(drop),
          noteStyle: "display:flex;flex-direction:column;gap:7px;margin-left:" + (isMobile || !indent ? 0 : indent) + "px;background:var(--sub);border:1px solid var(--line2);border-left:2px solid var(--accent);border-radius:9px;padding:9px 11px;animation:mfblkin 0.4s cubic-bezier(0.22,0.61,0.36,1) both",
          noteHeadStyle: "display:flex;align-items:center;gap:8px;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--muted)",
          noteTextStyle: "font-size:13.5px;line-height:1.55;color:var(--text);text-wrap:pretty",
          dropStyle: "display:inline-flex;align-items:center;justify-content:center;" + (touch ? "width:44px;height:44px;margin:-14px -10px -14px 0;" : "") + "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1;color:var(--muted)",
          noteSkel: [94, 70].map(w => ({ style: SHIM + "height:9px;border-radius:5px;width:" + w + "%" })),
        };
      };
      const pick = (st.bkPick || {})[key] || null;
      /* The trace is how the answer was built, not the answer: it stays folded to its header until it is opened. */
      const foldable = b.type === "trace";
      const unfolded = foldable && !!(st.bkOpen || {})[key];
      const toggleFold = () => this.setState(s2 => ({ bkOpen: Object.assign({}, s2.bkOpen, { [key]: !(s2.bkOpen || {})[key] }) }));
      const excl = (st.bkExcl || {})[key] || [];

      /* Controls only exist while the proposal is live and the block actually came back. */
      let raw = [];
      if (phase === "pending" && ready) {
        if (b.type === "event") raw = [
          { label: "Add to calendar", aria: "Add “" + b.title + "” to the calendar, " + (b.when || ""), primary: true, run: accept },
        ].concat(onStart ? [] : [
          { label: "Another time", aria: "Ask for another time for “" + b.title + "”", run: () => this.anotherTimeBk(key, b) },
        ]).concat([
          { label: "Decline", aria: "Decline putting “" + b.title + "” in the calendar", run: () => this.declineBk(key) },
        ]);
        else if (b.type === "task") raw = [
          { label: "Add", aria: "Add the task “" + b.title + "”, due " + (b.due || ""), primary: true, run: accept },
          { label: "Decline", aria: "Decline the task “" + b.title + "”", run: () => this.declineBk(key) },
        ];
        else if (b.type === "draft" && change) raw = change.busy ? [] : [
          { label: "Keep the change", aria: "Keep the rewritten text in the draft to " + b.to, primary: true, run: () => this.keepChange(key, b) },
          { label: "Try again", aria: "Ask the agent for another version of the same sentences", run: () => this.retryChange(key, b) },
          { label: "Undo", aria: "Leave the draft as it was", run: () => this.dropChange(key) },
        ];
        else if (b.type === "draft" && onStart) raw = [
          { label: "Open in composer", aria: "Open the draft to " + b.to + " in the composer - nothing is sent from here", primary: true, run: accept },
          { label: "Discard", aria: "Discard the draft to " + b.to, run: () => this.declineBk(key) },
        ];
        else if (b.type === "draft") raw = editing
          ? [{ label: "Send", aria: "Send the edited draft to " + b.to, primary: true, run: () => this.acceptBk(key, b) },
             { label: "Stop editing", aria: "Stop editing the draft to " + b.to, run: () => this.editBk(key, false) }]
          : [{ label: "Send", aria: "Send the draft to " + b.to, primary: true, run: () => this.acceptBk(key, b) },
             { label: "Edit here", aria: "Edit the draft to " + b.to + " inside the conversation", run: () => this.editBk(key, true, dParas.join("\n\n")) },
             { label: "Discard", aria: "Discard the draft to " + b.to, run: () => this.declineBk(key) }];
        else if (b.type === "action") raw = [
          { label: b.cta || "Do it", aria: b.title, primary: true, run: accept },
          { label: "Decline", aria: "Decline: " + b.title, run: () => this.declineBk(key) },
        ];
      }
      if (failed) raw = [{ label: "Try again", aria: "Try again: " + b.title, primary: true, run: () => this.setBk(key, { phase: "pending" }) }];

      const controls = raw.map(c => ({ label: c.label, aria: c.aria, run: c.run, key: keyer(c.run), style: btn(c.primary ? "primary" : "quiet") }));

      const nav = (declined ? [] : (b.nav || [])).map(n => ({
        label: n.label, icon: n.icon, aria: n.label + " - leaves the conversation, you can come straight back",
        run: () => this.goLook(n), key: keyer(() => this.goLook(n)),
        iconStyle: icon(16, "var(--muted)"),
        style: BLK.link(isMobile) + (touch ? "min-height:44px;" : ""),
      }));

      const chip = phase === "pending" ? { icon: "help", label: "needs confirmation", kind: "pending" }
        : failed ? { icon: "error", label: "not done", kind: "failed" }
        : accepted ? { icon: "check_circle", label: "done", kind: "done" }
        : declined ? { icon: "block", label: "declined", kind: "declined" } : null;

      const fields = b.type === "event" ? [
        { k: "When", v: bv.when }, { k: "Duration", v: bv.duration }, { k: "Attendees", v: b.attendees }, { k: "From", v: "thread “" + (b.from || "") + "”" },
      ] : b.type === "task" ? [
        { k: "Due", v: bv.due }, { k: "From", v: "message “" + (b.from || "") + "”" },
      ].concat(bv.reminder ? [{ k: "Reminder", v: bv.reminder }] : []) : b.type === "draft" ? [
        { k: "To", v: b.to }, { k: "Subject", v: b.subject },
      ] : [];

      const cols = (b.cols || []).length;
      /* Cells carry the column gap as their own right padding, so a row's hairline runs unbroken. */
      const cellPad = (ci) => "padding-right:" + (ci === cols - 1 ? 0 : isMobile ? 16 : 24) + "px;";
      const cells = b.type === "facttable"
        ? (b.cols || []).map((c, ci) => ({ t: c, style: BLK.label + ";padding-bottom:8px;border-bottom:1px solid var(--line);white-space:nowrap;" + cellPad(ci) }))
          .concat([].concat.apply([], (b.rows || []).map((r, ri) => r.map((c, ci) => ({
            t: c,
            style: "font-size:13.5px;padding-top:9px;padding-bottom:9px;white-space:nowrap;" + cellPad(ci) + (ci === 0 ? "font-weight:600;color:var(--text);" : "color:var(--text2);") + (ri ? "border-top:1px solid var(--line2);" : ""),
          })))))
        : [];

      return {
        label: LABEL[b.type] || "Block",
        meta: b.type === "answer" ? (b.cites || []).length + ((b.cites || []).length === 1 ? " citation" : " citations")
          : b.type === "draft" && (st.bkText || {})[key] ? "reply · rewritten with the agent"
          : b.meta || "",
        /* The thread itself is flat, so the blocks carry the separation: they sit on panel with a
           visible edge instead of melting into the page. */
        cardStyle: BLK.card(isMobile, tone) + (b.anim ? "animation:mfblkin 0.52s cubic-bezier(0.22,0.61,0.36,1) both;" : "") + (foldable && !unfolded ? "cursor:pointer;" : ""),
        cardHover: foldable && !unfolded ? "border-color:var(--accent-line);background:var(--hover)" : "",
        cardClick: foldable && !unfolded ? toggleFold : null,
        isFoldable: foldable,
        headRole: foldable ? "button" : null,
        headTab: foldable ? 0 : null,
        headExpanded: foldable ? unfolded : null,
        headAria: foldable ? (unfolded ? "Fold how the answer was built" : "Show how the answer was built, " + (b.items || []).length + " steps") : null,
        headClick: foldable && unfolded ? toggleFold : null,
        headKey: foldable ? keyer(toggleFold) : null,
        foldIcon: unfolded ? "expand_less" : "expand_more",
        foldIconStyle: icon(20, "var(--muted)") + ";margin-left:auto",
        headStyle: BLK.head + (foldable && touch ? ";min-height:44px;box-sizing:border-box" : ""),
        icon: BLK_ICON[b.type] || "widgets",
        tileStyle: BLK.tile(b.type === "answer" ? "accent" : tone),
        glyphStyle: BLK.glyph,
        labelStyle: BLK.title,
        metaStyle: BLK.meta,
        hasChip: !!chip,
        chipLabel: chip ? chip.label : "",
        chipIcon: chip ? chip.icon : "",
        chipIconStyle: icon(14, "inherit"),
        chipStyle: BLK.chip(chip ? chip.kind : ""),

        isDeclined: declined,
        showBody: !declined && (!foldable || unfolded),
        declinedStyle: "display:flex;align-items:center;gap:8px;flex-wrap:wrap;font-size:13px;color:var(--muted)",
        declinedIconStyle: "display:none",
        strikeStyle: "text-decoration:line-through;min-width:0",
        declinedWord: "Declined",
        declinedNote: "nothing was done",

        isLoading: bstate === "loading",
        isError: bstate === "error",
        isEmpty: bstate === "empty",
        isPartial: bstate === "partial",
        ready: ready,
        skel: [92, 76, 54].map(w => ({ style: SHIM + "height:10px;border-radius:5px;width:" + w + "%" })),
        emptyBodyStyle: "display:flex;align-items:center;gap:12px;flex-wrap:wrap",
        emptyIconStyle: iconTile("var(--sub)", "var(--faint)"),
        errIconStyle: iconTile("var(--warn-soft)", "var(--warn)"),
        emptyIcon: b.type === "draft" ? "edit_note" : b.type === "task" ? "task_alt" : b.type === "event" ? "event_busy" : "find_in_page",
        emptyTextStyle: "flex:1 1 200px;min-width:0;font-size:13.5px;color:var(--text2);text-wrap:pretty",
        emptyText: b.emptyText || "Nothing came back for this one.",
        errorText: b.errorText || "This block could not be built.",
        retry: () => this.retryBk(key),
        retryKey: keyer(() => this.retryBk(key)),
        retryAria: "Try again: " + (LABEL[b.type] || "block").toLowerCase(),
        retryStyle: btn("quiet"),
        noteStyle: "display:flex;align-items:center;gap:8px;font-size:12.5px;color:var(--muted);background:var(--sub);border-radius:8px;padding:7px 10px",
        noteIconStyle: icon(16, "var(--muted)"),
        partialNote: b.partialNote || "Still being read - more may appear.",

        isTimeline: b.type === "timeline",
        isEvidence: b.type === "evidence",
        /* The Agent's answer (`isAnswer`): confidence and *cited*, each explaining itself (`[data-tip]`), the prose with
           its citations at the end of it, and the verdict's sentence under it. */
        isAnswer: b.type === "answer",
        answerProse: b.text || "",
        answerTextStyle: "font-size:" + (isMobile ? 16 : 17) + "px;line-height:1.6;color:var(--text);text-wrap:pretty",
        answerConfLabel: (b.confidence || "moderate") + " confidence",
        answerConfTip: ({
          high: "The messages say this outright, and the answer restates what they say.",
          moderate: "The messages carry this, with a step of inference you may want to check.",
          low: "This is the best reading of partial sources, and it may be wrong.",
        })[b.confidence || "moderate"],
        answerConfStyle: "display:flex;align-items:center;font-size:11px;border-radius:12px;padding:3px 9px;" + (b.confidence === "high"
          ? "background:var(--ok-soft);color:var(--ok-text)"
          : b.confidence === "low" ? "background:var(--warn-soft);color:var(--warn-text)" : "background:var(--rail);color:var(--muted)"),
        answerCites: (b.cites || []).map(e => ({
          n: e.n, open: () => this.openAgentEv(e), openKey: keyer(() => this.openAgentEv(e)),
          openAria: "Citation " + e.n + ": " + e.source + " - opens the source",
        })),
        citeChipStyle: "display:inline-flex;align-items:center;justify-content:center;min-width:" + (isMobile ? 36 : 20) + "px;height:" + (isMobile ? 36 : 20) + "px;box-sizing:border-box;margin-left:4px;vertical-align:" + (isMobile ? "middle" : "1px") + ";font-size:11px;color:var(--accent-d);background:var(--accent-soft);border-radius:4px;padding:2px 7px;cursor:pointer",
        answerNoteStyle: "font-size:13px;color:var(--muted);text-wrap:pretty",
        /* The Agent's sources are what it read, not something checked against what it wrote: cited, never supported. */
        citedLabel: "cited",
        citedTip: "The answer names the messages it was written from, and nothing checked that they back it - open a citation to see for yourself.",
        citedChipStyle: "align-self:flex-start;display:flex;align-items:center;gap:4px;font-size:11px;background:var(--rail);color:var(--muted);border-radius:12px;padding:2px 8px",
        citedIconStyle: icon(12, "inherit"),
        isThreadState: b.type === "threadstate",
        isTable: b.type === "facttable",
        isFiles: b.type === "attachments",
        isPeople: b.type === "people",
        isTrace: b.type === "trace",
        /* The tray a picked set or range opens, inside the block it was picked from. */
        hasPeoplePick: Array.isArray(pick) && pick.length > 0,
        peoplePickLabel: Array.isArray(pick) ? pick.length + " selected" : "",
        peopleActs: Array.isArray(pick) && pick.length ? (() => {
          const names = pick.map(i => b.items[i].name);
          return [
            itemAct("Draft to " + (names.length === 1 ? names[0].split(" ")[0] : "these"), "edit_note", () => this.draftToPeople(key, names), "Draft a message to " + this.namesList(names)),
            itemAct("Invite to a meeting", "event", () => this.invitePeople(key, names), "Invite " + this.namesList(names) + " to a meeting"),
            itemAct("Clear", "close", () => this.clearPick(key), "Clear the selection"),
          ];
        })() : [],
        hasRange: b.type === "timeline" && !!pick && pick.from !== undefined,
        rangeLabel: b.type === "timeline" && pick && pick.from !== undefined
          ? (pick.to === undefined ? "From " + b.items[pick.from].date + " · press where the period ends" : b.items[pick.from].date + " - " + b.items[pick.to].date + " · " + (pick.to - pick.from + 1) + (pick.to === pick.from ? " item" : " items"))
          : "",
        rangeActs: b.type === "timeline" && pick && pick.from !== undefined ? (pick.to === undefined ? [] : [
          itemAct("Summarise this period", "summarize", () => {
            const its = b.items.slice(pick.from, pick.to + 1);
            this.noteBk(key + "#range", "Summary of " + its[0].date + " - " + its[its.length - 1].date,
              its.length + (its.length === 1 ? " item: " : " items: ") + its.map(x => x.title).join("; ") + ". " + (b.periodNote || "Read from the calendar alone - nothing in the mail moves any of them."));
          }, "Summarise the selected period"),
        ]).concat([itemAct("Clear", "close", () => this.clearPick(key), "Clear the period")]) : [],
        trayStyle: "display:flex;align-items:center;gap:" + (isMobile ? "4px 14px" : "6px 14px") + ";flex-wrap:wrap;background:var(--accent-soft);border:1px solid var(--accent-line);border-radius:10px;padding:" + (isMobile ? "6px 12px" : "7px 12px"),
        trayLabelStyle: "font-family:'Geist',system-ui,sans-serif;font-size:12px;font-weight:600;color:var(--accent-d);margin-right:auto",
        rangeNote: noteOf(key + "#range", 0),
        peopleHint: live && b.type === "people" && !(Array.isArray(pick) && pick.length),
        timelineHint: live && b.type === "timeline" && !(pick && pick.from !== undefined),
        hintStyle: "display:flex;align-items:center;gap:7px;font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--muted)",
        hintIconStyle: icon(15, "var(--faint)"),
        isDraft: b.type === "draft",
        isProposal: b.type === "event" || b.type === "task" || b.type === "action",

        tlWrapStyle: isMobile ? "display:flex;flex-direction:column;gap:0;padding-top:2px" : "display:flex;gap:0;overflow-x:auto;padding-top:2px",
        items: b.type === "timeline" ? (b.items || []).map((t, i, all) => {
          const inRange = !!pick && pick.from !== undefined && i >= pick.from && i <= (pick.to === undefined ? pick.from : pick.to);
          const press = () => this.pickBk(key, i, true);
          return {
          date: t.date, title: t.title, detail: t.detail,
          press: live ? press : null, pressKey: live ? keyer(press) : null, role: live ? "button" : null, tab: live ? 0 : null,
          pressed: inRange, aria: t.date + " " + t.title + (inRange ? " - in the selected period" : " - press to start or end a period"),
          style: BLK.tlItem(isMobile, i === all.length - 1) + (live ? ";cursor:pointer;border-radius:8px" : "") + (inRange ? ";background:var(--accent-soft)" : ""),
          lineStyle: BLK.tlLine(isMobile, i === all.length - 1),
          dotStyle: BLK.tlDot(isMobile) + (inRange ? ";background:var(--accent);box-shadow:0 0 0 3px var(--accent-soft)" : ""),
          dateStyle: BLK.tlDate,
          titleStyle: BLK.tlTitle,
          detailStyle: BLK.tlDetail,
        }; }) : b.type === "evidence" ? (b.items || []).map((e, i) => Object.assign({
          n: e.n, source: e.source, snippet: e.snippet, freshness: e.freshness,
          open: () => this.openAgentEv(e), openKey: keyer(() => this.openAgentEv(e)),
          openAria: "Citation " + e.n + ": " + e.source + " - opens the source",
          isPrivate: !!e.private, hasSnippet: !e.private && !!e.snippet,
          badge: excl.indexOf(i) >= 0 ? "left out · answered again below" : e.badge,
          excluded: excl.indexOf(i) >= 0,
          hasActs: live && excl.indexOf(i) < 0,
          acts: live && excl.indexOf(i) < 0 ? [
            itemAct("Why this source", "help", () => this.noteBk(key + "#" + i, "Why this source", e.why || "It is the closest match to what the answer claims, and the most recent source that says it."), "Why the answer uses " + e.source),
          ].concat((b.items || []).length - excl.length > 1 ? [
            itemAct("Leave it out and answer again", "do_not_disturb_on", () => this.excludeSource(key, b, i), "Answer again without " + e.source),
          ] : []) : [],
          actsStyle,
          style: "display:flex;flex-direction:column;gap:7px;padding:" + (i ? 12 : 2) + "px 0 12px 0;" + (i ? "border-top:1px solid var(--line2);" : "") + (excl.indexOf(i) >= 0 ? "opacity:0.55;" : ""),
          nStyle: "flex:0 0 auto;display:inline-flex;align-items:center;justify-content:center;min-width:" + (isMobile ? 36 : 20) + "px;height:" + (isMobile ? 36 : 20) + "px;padding:0 5px;box-sizing:border-box;border-radius:6px;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--accent-d);background:var(--accent-soft);cursor:pointer",
          badgeStyle: "font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:500;color:var(--muted);background:var(--sub);border-radius:999px;padding:2px 8px;white-space:nowrap",
          /* The quote and its provenance line up under the source's name rather than under its number. */
          privStyle: "margin-left:" + (isMobile ? 0 : 28) + "px;font-size:13px;line-height:1.5;color:var(--faint);font-style:italic;text-wrap:pretty",
          snipStyle: "margin-left:" + (isMobile ? 0 : 28) + "px;font-size:13.5px;line-height:1.5;color:var(--text2);border-left:2px solid var(--accent-line);padding:1px 0 1px 11px;text-wrap:pretty",
          subStyle: "margin-left:" + (isMobile ? 0 : 28) + "px;font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--faint)",
        }, noteOf(key + "#" + i, 28))) : b.type === "attachments" ? (b.items || []).map((f, i) => Object.assign({
          name: f.name, meta: f.meta, icon: f.icon,
          hasActs: live && !!(f.summary || f.changes),
          acts: !live ? [] : [].concat(f.summary ? [itemAct("Summarise", "summarize", () => this.noteBk(key + "#" + i, "Summary of " + f.name, f.summary), "Summarise " + f.name)] : [])
            .concat(f.changes ? [itemAct("What changed", "difference", () => this.noteBk(key + "#" + i, "What changed in " + f.name, f.changes), "What changed in " + f.name + " since the previous version")] : []),
          actsStyle: "display:flex;flex-wrap:wrap;gap:" + (isMobile ? "0 14px" : "0 12px") + ";margin-left:" + (isMobile ? 0 : 44) + "px;margin-top:-4px",
          wrapStyle: "display:flex;flex-direction:column;gap:8px;padding:" + (i ? 10 : 2) + "px 0 10px 0;" + (i ? "border-top:1px solid var(--line2);" : ""),
          style: "display:flex;align-items:center;gap:12px",
          iconStyle: iconTile("var(--sub)", "var(--text2)") + ";box-shadow:inset 0 0 0 1px var(--line2)",
          nameStyle: "font-size:14px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap",
          metaStyle: "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--faint)",
        }, noteOf(key + "#" + i, 44))) : b.type === "people" ? (b.items || []).map((p, i) => {
          const on = Array.isArray(pick) && pick.indexOf(i) >= 0;
          const toggle = () => this.pickBk(key, i, false);
          return {
            initials: p.initials, name: p.name, role: p.role, last: p.last,
            on, toggle: live ? toggle : null, toggleKey: live ? keyer(toggle) : null, role2: live ? "checkbox" : null, tab: live ? 0 : null,
            aria: p.name + ", " + p.role,
            style: "display:flex;align-items:center;gap:12px;padding:" + (isMobile ? "10px 8px" : "8px 8px") + ";margin:0 -8px;border-radius:9px;" + (live ? "cursor:pointer;" : "") + (on ? "background:var(--accent-soft);" : "") + (i ? "border-top:1px solid var(--line2);" : ""),
            boxStyle: "display:" + (live ? "flex" : "none") + ";align-items:center;justify-content:center;width:18px;height:18px;flex:0 0 18px;border-radius:5px;border:1.5px solid " + (on ? "var(--accent)" : "var(--border2)") + ";background:" + (on ? "var(--accent)" : "transparent") + ";font-family:'Material Symbols Rounded';font-variation-settings:'wght' 500;font-size:14px;line-height:1;color:var(--onaccent)",
            check: on ? "check" : "",
            avStyle: "display:flex;align-items:center;justify-content:center;width:32px;height:32px;flex:0 0 32px;border-radius:50%;background:var(--sub);box-shadow:inset 0 0 0 1px var(--line2);font-family:'Geist',system-ui,sans-serif;font-size:12px;font-weight:600;color:var(--text2)",
            nameStyle: "font-size:14px;font-weight:600;color:var(--text)",
            roleStyle: "font-size:12.5px;color:var(--muted);text-wrap:pretty",
            lastStyle: "margin-left:auto;font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--faint);white-space:nowrap",
          };
        }) : b.type === "trace" ? (b.items || []).map((t, i) => Object.assign({
          n: String(i + 1), step: t.step, detail: t.detail,
          hasActs: live && !!t.why,
          acts: live && t.why ? [itemAct("Why this step", "help", () => this.noteBk(key + "#" + i, "Why this step", t.why), "Why the agent took step " + (i + 1))] : [],
          actsStyle,
          style: "display:flex;flex-direction:column;gap:6px;padding:" + (i ? 10 : 2) + "px 0 10px 0;" + (i ? "border-top:1px solid var(--line2);" : ""),
          nStyle: "flex:0 0 auto;display:inline-flex;align-items:center;justify-content:center;width:20px;height:20px;border-radius:50%;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--text2);background:var(--sub);box-shadow:inset 0 0 0 1px var(--line2)",
          stepStyle: "font-size:14px;color:var(--text);min-width:0",
          detailStyle: "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--faint);margin-left:" + (isMobile ? 0 : 28) + "px",
        }, noteOf(key + "#" + i, 28))) : [],

        colsStyle: isMobile ? "display:flex;flex-direction:column;gap:16px" : "display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:22px",
        stateCols: (b.cols || []).map(c => ({
          label: c.label,
          style: "display:flex;flex-direction:column;gap:8px;min-width:0",
          labelStyle: BLK.label + ";padding-bottom:7px;border-bottom:1px solid var(--line2)",
          items: (c.items || []).map(it => ({
            t: it.t,
            style: "font-size:13px;line-height:1.45;text-wrap:pretty;border-left:2px solid " + (it.late ? "var(--warn)" : "var(--border2)") + ";padding:1px 0 1px 10px;" + (it.late ? "color:var(--text);font-weight:500;" : "color:var(--text2);"),
          })),
        })),

        tableScrollStyle: "overflow-x:auto;max-width:100%",
        tableStyle: "display:grid;grid-template-columns:repeat(" + (cols || 1) + ",max-content);gap:0;min-width:min-content",
        cells: cells,

        fields: fields.filter(f => !!f.v).map(f => {
          const fieldAlts = (b.alts || FIELD_ALTS)[f.k];
          const canChange = phase === "pending" && ready && !!fieldAlts;
          const fid = key + "|" + f.k;
          const open = canChange && st.bkField === fid;
          const toggle = () => this.setState(s2 => ({ bkField: s2.bkField === fid ? null : fid, fieldAsk: "" }));
          const askSubmit = () => { const v = (this.state.fieldAsk || "").trim(); if (v) this.setFieldVal(key, f.k, v); };
          return {
          k: f.k, v: f.v, kStyle: kStyle, vStyle: vStyle,
          canChange, fixed: !canChange, open,
          changed: vals[f.k] !== undefined,
          toggle, toggleKey: keyer(toggle),
          changeAria: "Change " + f.k.toLowerCase() + ", now " + f.v + " - the agent suggests alternatives",
          valBtnStyle: "display:inline-flex;align-items:center;gap:7px;align-self:flex-start;max-width:100%;font-size:14px;color:var(--text);border-radius:7px;box-sizing:border-box;padding:" + (touch ? "6px 8px;min-height:44px" : "2px 6px") + ";margin:" + (touch ? "-10px -8px" : "-2px -6px") + ";border:1px dashed " + (open ? "var(--accent)" : "transparent") + ";background:" + (open ? "var(--accent-soft)" : "transparent"),
          editIcon: open ? "expand_less" : "edit",
          editIconStyle: icon(15, "var(--faint)"),
          tagStyle: "font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--accent-d);background:var(--accent-soft);border-radius:999px;padding:1px 7px",
          panelStyle: "display:flex;flex-direction:column;gap:6px;padding:10px;border-radius:10px;background:var(--sub);border:1px solid var(--line2)",
          panelLabelStyle: BLK.label,
          alts: (fieldAlts || []).filter(a => a.v !== f.v).map(a => ({
            v: a.v, why: a.why, hasWhy: !!a.why,
            pick: () => this.setFieldVal(key, f.k, a.v), pickKey: keyer(() => this.setFieldVal(key, f.k, a.v)),
            aria: "Set " + f.k.toLowerCase() + " to " + a.v + (a.why ? " - " + a.why : ""),
            style: "display:flex;align-items:baseline;gap:10px;flex-wrap:wrap;border-radius:8px;box-sizing:border-box;padding:" + (touch ? "13px 10px;min-height:44px" : "7px 9px") + ";background:var(--panel);border:1px solid var(--line)",
            vStyle: "font-size:13.5px;font-weight:600;color:var(--text)",
            whyStyle: "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--muted)",
          })),
          askValue: open ? st.fieldAsk || "" : "",
          onAsk: (e) => this.setState({ fieldAsk: e.target.value }),
          askKey: (e) => { if (e.key === "Enter") askSubmit(); if (e.key === "Escape") toggle(); },
          askSubmit, askSubmitKey: keyer(askSubmit),
          askPlaceholder: isMobile ? (f.k === "When" ? "Or say when…" : "Or say it…") : f.k === "When" ? "Or say when - e.g. after lunch on Thursday" : "Or say it in your words",
          askRowStyle: "display:flex;align-items:center;gap:8px;background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:" + (touch ? "4px 4px 4px 10px" : "4px 4px 4px 10px"),
          askInputStyle: "flex:1;min-width:0;border:none;outline:none;background:transparent;font-family:inherit;font-size:13.5px;color:var(--text);padding:" + (isMobile ? "6px 0" : "3px 0"),
          askBtnStyle: btn("quiet") + "width:auto;flex:0 0 auto;",
          rowStyle: "display:grid;grid-template-columns:" + (isMobile ? 72 : 88) + "px minmax(0,1fr);gap:12px;align-items:baseline;padding:6px 0;border-top:1px solid var(--line2)",
        }; }),
        hasFields: fields.some(f => !!f.v),
        fieldsStyle: "display:flex;flex-direction:column",
        propTitleStyle: "font-size:" + (isMobile ? 15 : 16) + "px;font-weight:600;line-height:1.35;letter-spacing:-0.01em;text-wrap:pretty",
        title: b.title || "",
        hasConflict: !!b.conflict && !accepted,
        conflict: b.conflict || "",
        conflictStyle: "display:flex;align-items:flex-start;gap:8px;font-size:13px;line-height:1.45;color:var(--warn-text);background:var(--warn-soft);border-radius:9px;padding:9px 11px;text-wrap:pretty",
        conflictIconStyle: icon(16, "var(--warn)"),
        hasReason: !!b.reason,
        reason: b.reason || "",
        reasonStyle: "font-size:13.5px;line-height:1.5;color:var(--text2);text-wrap:pretty",
        hasEffect: !!b.effect,
        effect: b.effect || "",
        effectStyle: "display:flex;align-items:flex-start;gap:7px;font-size:13px;line-height:1.45;color:var(--muted);text-wrap:pretty",
        effectIconStyle: icon(16, "var(--muted)"),

        editing: editing,
        notEditing: !editing,
        editText: editing ? editRaw : "",
        onEdit: (e) => this.editBk(key, true, e.target.value),
        /* The draft reads as a letter: its paragraphs sit in one well, and editing swaps the well for a field of the same shape. */
        letterStyle: "display:flex;flex-direction:column;gap:10px;background:var(--sub);border:1px solid var(--line2);border-radius:10px;padding:" + (isMobile ? "12px 13px" : "14px 16px"),
        textareaStyle: "width:100%;box-sizing:border-box;min-height:" + (isMobile ? 150 : 130) + "px;resize:vertical;font-family:inherit;font-size:14px;line-height:1.6;color:var(--text);background:var(--sub);border:1px solid var(--accent-line);border-radius:10px;padding:" + (isMobile ? "12px 13px" : "14px 16px"),
        /* A pending draft is a selection scope of its own: what is selected there can be rewritten in place. A piece
           being rewritten pulses; a rewritten one shows the old text struck beside the new until it is kept or undone. */
        paras: dParas.map((t, pi) => {
          const piece = change && change.pieces.find(x => x.pi === pi);
          const plain = "";
          const segs = !piece ? [{ t, style: plain }]
            : [{ t: t.slice(0, piece.s), style: plain }]
              .concat(change.busy
                ? [{ t: piece.old, style: "background:var(--accent-soft);border-radius:4px;animation:mfpulse 1s ease-in-out infinite" }]
                : [{ t: piece.old, style: "color:var(--muted);text-decoration:line-through;text-decoration-color:var(--faint)" },
                   { t: " ", style: plain },
                   { t: piece.nw, style: "background:var(--accent-soft);color:var(--text);border-radius:4px;box-shadow:0 1.5px 0 var(--accent);padding:0 1px" }])
              .concat([{ t: t.slice(piece.e), style: plain }]);
          return {
            t, segs: segs.filter(x => x.t),
            sel: phase === "pending" && !editing && !change ? "draft|" + key + "|" + pi : onStart ? "" : "text|the draft",
            style: "font-size:14px;line-height:1.6;color:var(--text);text-wrap:pretty",
          };
        }),
        draftHint: phase === "pending" && ready && !editing && !change,
        draftHintStyle: "display:flex;align-items:center;gap:7px;font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--muted)",
        draftHintIconStyle: icon(15, "var(--faint)"),
        hasChange: !!change,
        changeBusy: !!(change && change.busy),
        changeNote: !change ? "" : change.busy
          ? "Rewriting " + change.pieces.length + (change.pieces.length === 1 ? " passage" : " passages") + " · " + change.label
          : change.label + " · the struck text stays until you keep the change",
        changeIcon: change && change.busy ? "progress_activity" : "auto_awesome",
        changeNoteStyle: "display:flex;align-items:center;gap:8px;min-width:0;flex:" + (isMobile ? "0 0 auto" : "1 1 220px") + ";font-size:13px;line-height:1.45;color:var(--accent-d);text-wrap:pretty",
        changeIconStyle: icon(16, "var(--accent-d)") + (change && change.busy ? ";animation:mfspin 1.15s linear infinite" : ""),

        hasFoot: controls.length > 0 || nav.length > 0 || (accepted && !declined) || !!change,
        footStyle: BLK.foot(isMobile, tone),
        hasControls: controls.length > 0,
        controls: controls,
        controlsStyle: isMobile ? "display:flex;flex-direction:column;gap:8px" : "display:flex;gap:8px;flex-wrap:wrap;align-items:center",
        hasResult: accepted && !declined,
        resultText: (st.bkResult || {})[key] || b.result || ((b.done || "Done") + " · " + this.hm()),
        resultIcon: failed ? "error" : "check_circle",
        resultIconStyle: icon(16, failed ? "var(--warn)" : "var(--ok)"),
        resultStyle: "display:flex;align-items:center;gap:8px;min-width:0;font-size:13px;line-height:1.45;color:" + (failed ? "var(--warn-text)" : "var(--ok-text)") + ";text-wrap:pretty",
        hasNav: nav.length > 0,
        nav: nav,
        /* The way out sits at the far end of the tray when anything stands beside it. */
        navStyle: isMobile ? "display:flex;flex-direction:column;gap:8px" : "display:flex;gap:8px;flex-wrap:wrap" + (controls.length || accepted ? ";margin-left:auto" : ""),
      };
    };

    /* Approving, declining and repairing a block. The phase lives outside the message, so the seeded
       histories stay immutable and a re-proposal can reuse the very same block object. */
    hm = (t) => { const d = new Date(t || Date.now()); return (d.getHours() < 10 ? "0" : "") + d.getHours() + ":" + (d.getMinutes() < 10 ? "0" : "") + d.getMinutes(); };

    setBk = (key, patch) => this.setState(s2 => ({
      bkPhase: Object.assign({}, s2.bkPhase, patch.phase ? { [key]: patch.phase } : {}),
      bkState: Object.assign({}, s2.bkState, patch.state ? { [key]: patch.state } : {}),
      bkResult: Object.assign({}, s2.bkResult, patch.result ? { [key]: patch.result } : {}),
    }));

    acceptBk = (key, b) => {
      this.setBk(key, { phase: "accepted", result: (b.done || "Done") + " · " + this.hm() });
      this.editBk(key, false);
      if (b.type === "event") {
        const t = (b.when || "").match(/\d{2}:\d{2}/);
        this.setState(s2 => ({
          calAdded: (s2.calAdded || []).concat([{ day: "27", time: t ? t[0] : "10:00", h: t ? parseInt(t[0], 10) : 10, t: b.title, kind: "meeting", src: "agent block" }]),
        }));
      }
    };

    declineBk = (key) => this.setBk(key, { phase: "declined" });
    retryBk = (key) => this.setBk(key, { state: "ready" });

    editBk = (key, on, text) => this.setState(s2 => ({
      bkEdit: Object.assign({}, s2.bkEdit, { [key]: on ? (typeof text === "string" ? text : ((s2.bkEdit || {})[key] || "")) : null }),
    }));

    /* Selecting inside the conversation. The scope a selection sits in (`data-sel`) decides what it offers: a
       passage of a pending draft can be rewritten in place, any other passage asked about, explained, checked or
       turned into a task. A selection that crosses two scopes offers nothing. */
    /* A touch selection is made by a long press and its handles, which end in no mouseup or touchend the
       conversation hears, and a mouse drag may end outside it. So the bar also follows the document's selection:
       a mouse opens it on release, anything else once the selection settles. That path only opens or updates the
       bar - focusing its own field collapses the selection, so emptying it never closes the bar from here. */
    onSelPointer = (e) => {
      if (e.pointerType !== "mouse") return;
      this._mouseHeld = e.type === "pointerdown";
      if (!this._mouseHeld && (this.state.screen === "agent" || this.state.screen === "start" || this.state.compose)) this.onAgentSelect(null, true);
    };
    onSelChange = () => {
      if (this._mouseHeld || (this.state.screen !== "agent" && this.state.screen !== "start" && !this.state.compose)) return;
      this.onAgentSelect(null, true, 350);
    };

    onAgentSelect = (e, openOnly, wait) => {
      clearTimeout(this._selT);
      this._selT = setTimeout(() => {
        const sel = window.getSelection ? window.getSelection() : null;
        const text = sel && !sel.isCollapsed ? sel.toString().replace(/\s+/g, " ").trim() : "";
        const cur = this.state.selBar;
        if (!text) { if (!openOnly && cur && !cur.answer) this.setState({ selBar: null }); return; }
        if (this.composeSelect(sel, text, cur, openOnly)) return;
        const scopeOf = (n) => { const el = n && (n.nodeType === 1 ? n : n.parentElement); return el && el.closest ? el.closest("[data-sel]") : null; };
        const a = scopeOf(sel.anchorNode), f = scopeOf(sel.focusNode);
        if (!a || !f || !a.getAttribute("data-sel") || !f.getAttribute("data-sel")) return;
        const [kind, key, piA] = a.getAttribute("data-sel").split("|");
        const fk = f.getAttribute("data-sel").split("|");
        const range = sel.getRangeAt(0);
        const rr = range.getBoundingClientRect(), root = this.rootRef.current.getBoundingClientRect();
        const inp = this.agentInputRef.current;
        const rect = { top: rr.top - root.top, bottom: rr.bottom - root.top, left: rr.left - root.left, width: root.width, height: root.height,
          inputTop: inp ? inp.getBoundingClientRect().top - root.top : root.height - 90 };
        /* Docked under touch, the bar spans the field it sits on, so it never covers the rail or the history. */
        const box = inp && inp.parentElement ? inp.parentElement.getBoundingClientRect() : null;
        if (box) { rect.inputTop = box.top - root.top; rect.inputLeft = box.left - root.left; rect.inputWidth = box.width; }
        const short = text.length > 140 ? text.slice(0, 137) + "…" : text;
        if (openOnly && cur && cur.text === short) return;
        if (kind === "draft" && fk[0] === "draft" && fk[1] === key) {
          /* Offsets are counted inside each paragraph, so the rewrite can be laid over the text the block holds. */
          const offset = (el, node, off) => { const r = document.createRange(); r.setStart(el, 0); r.setEnd(node, off); return r.toString().length; };
          const pa = +piA, pf = +fk[2];
          const startFirst = pa < pf || (pa === pf && range.startContainer === sel.anchorNode && range.startOffset === sel.anchorOffset);
          const [p1, e1] = startFirst ? [pa, a] : [pf, f];
          const [p2, e2] = startFirst ? [pf, f] : [pa, a];
          this.setState({ selBar: { kind: "draft", key, text: short, rect,
            from: { pi: p1, at: offset(e1, range.startContainer, range.startOffset) },
            to: { pi: p2, at: offset(e2, range.endContainer, range.endOffset) } }, selAsk: "" });
          return;
        }
        if (kind !== "text" || fk[0] !== "text") return;
        this.setState({ selBar: { kind: "text", text: short, full: text, from: key, rect }, selAsk: "" });
      }, wait || 0);
    };

    /* Closing the bar lets go of the selection too, or the document's next selection event would open it again. */
    dropSelection = () => { if (window.getSelection) window.getSelection().removeAllRanges(); };
    closeSelBar = () => { this.dropSelection(); this.setState({ selBar: null, selAsk: "" }); };

    /* The paragraphs a draft holds now: the agent's own, or the last version the reader kept. */
    draftParas = (key, b) => (this.state.bkText || {})[key] || b.paras || [];

    /* A rewrite widens the selection to the whole sentences it touches, so what comes back reads as a sentence
       rather than as a splice, and every piece is shown struck and replaced before anything is kept. */
    rewriteSel = (b, variant, label) => {
      const bar = this.state.selBar;
      if (!bar || bar.kind !== "draft") return;
      const key = bar.key, paras = this.draftParas(key, b);
      const SENT = /.+?(?:[.!?]+["”’)]*(?=\s|$)|$)\s*/g;
      const spans = (t) => { const out = []; let m; SENT.lastIndex = 0; while ((m = SENT.exec(t))) { if (!m[0]) break; out.push({ s: m.index, e: m.index + m[0].trimEnd().length }); } return out; };
      const pieces = [];
      for (let pi = bar.from.pi; pi <= bar.to.pi; pi++) {
        const t = paras[pi] || "";
        const lo = pi === bar.from.pi ? bar.from.at : 0, hi = pi === bar.to.pi ? bar.to.at : t.length;
        const hit = spans(t).filter(x => x.e > lo && x.s < hi);
        if (!hit.length) continue;
        const sS = hit[0].s, sE = hit[hit.length - 1].e;
        pieces.push({ pi, s: sS, e: sE, old: t.slice(sS, sE), nw: null });
      }
      if (!pieces.length) return;
      window.getSelection && window.getSelection().removeAllRanges();
      this.setState(s2 => ({ selBar: null, selAsk: "", bkChange: Object.assign({}, s2.bkChange, { [key]: { busy: true, variant, label, pieces } }) }));
      this.timers.push(setTimeout(() => {
        const swap = (old) => old.split(/(?<=[.!?]["”’)]*)\s+/).map(x => ((DRAFT_REWRITES[x.trim()] || {})[variant]) || x).join(" ");
        const done = pieces.map(p => Object.assign({}, p, { nw: swap(p.old) }));
        if (done.every(p => p.nw === p.old)) {
          this.setState(s2 => ({ bkChange: Object.assign({}, s2.bkChange, { [key]: null }) }));
          this.notify("The agent found nothing to change there.", { title: label });
          return;
        }
        this.setState(s2 => ({ bkChange: Object.assign({}, s2.bkChange, { [key]: { busy: false, variant, label, pieces: done } }) }));
      }, Math.max(500, (this.props.streamDelay ?? 550) * 1.4)));
    };

    rewriteAsk = (b) => {
      const words = (this.state.selAsk || "").trim();
      if (!words) return;
      const preset = REWRITE_PRESETS.find(p => p.words.test(words)) || REWRITE_PRESETS[1];
      this.rewriteSel(b, preset.key, "“" + words + "”");
    };

    keepChange = (key, b) => {
      const ch = (this.state.bkChange || {})[key];
      if (!ch || ch.busy) return;
      const before = this.draftParas(key, b);
      const after = before.slice();
      ch.pieces.slice().sort((x, y) => y.s - x.s).forEach(p => { after[p.pi] = after[p.pi].slice(0, p.s) + p.nw + after[p.pi].slice(p.e); });
      this.setState(s2 => ({ bkText: Object.assign({}, s2.bkText, { [key]: after }), bkChange: Object.assign({}, s2.bkChange, { [key]: null }) }));
      this.notify(ch.label + " · kept in the draft", { title: "Change kept", actionLabel: "Undo",
        onAction: () => this.setState(s2 => ({ bkText: Object.assign({}, s2.bkText, { [key]: before }) })) });
    };

    dropChange = (key) => this.setState(s2 => ({ bkChange: Object.assign({}, s2.bkChange, { [key]: null }) }));

    /* *Try again* asks for another version of the same sentences: the next preset round, under the same request. */
    retryChange = (key, b) => {
      const ch = (this.state.bkChange || {})[key];
      if (!ch || ch.busy) return;
      const order = REWRITE_PRESETS.map(p => p.key);
      const next = order[(order.indexOf(ch.variant) + 1) % order.length];
      const p0 = ch.pieces[0], pl = ch.pieces[ch.pieces.length - 1];
      this.setState({ selBar: { kind: "draft", key, from: { pi: p0.pi, at: p0.s }, to: { pi: pl.pi, at: pl.e } } }, () => this.rewriteSel(b, next, ch.label.replace(/ · another version$/, "") + " · another version"));
    };

    /* The composer takes the same rewrite as the Agent's draft. Its body is the reader's own editor, so a paragraph
       is any block in it that holds no other block, and offsets are counted inside each one. */
    composeParas = () => {
      const el = this.bodyRef.current;
      if (!el) return [];
      const leaves = Array.from(el.querySelectorAll("p, div, li, blockquote")).filter(n => !n.querySelector("p, div, li, blockquote"));
      return leaves.length ? leaves : [el];
    };

    composeSelect = (sel, text, cur, openOnly) => {
      const el = this.bodyRef.current;
      if (!el || !el.contains(sel.anchorNode) || !el.contains(sel.focusNode)) return false;
      if (this.state.cChange) return true;
      const range = sel.getRangeAt(0);
      const paras = this.composeParas();
      const at = (node, off) => {
        const pi = paras.findIndex(n => n === node || n.contains(node));
        if (pi < 0) return null;
        const r = document.createRange(); r.setStart(paras[pi], 0); r.setEnd(node, off);
        return { pi, at: r.toString().length };
      };
      const from = at(range.startContainer, range.startOffset), to = at(range.endContainer, range.endOffset);
      if (!from || !to) return true;
      const short = text.length > 140 ? text.slice(0, 137) + "…" : text;
      if (openOnly && cur && cur.kind === "compose" && cur.text === short) return true;
      const rr = range.getBoundingClientRect(), root = this.rootRef.current.getBoundingClientRect();
      const rect = { top: rr.top - root.top, bottom: rr.bottom - root.top, left: rr.left - root.left, width: root.width, height: root.height, inputTop: root.height - 90 };
      /* Docked under touch, the bar sits on the composer's foot, across the composer's own width. */
      const foot = this.composeFootRef.current;
      if (foot) { const b = foot.getBoundingClientRect(); rect.inputTop = b.top - root.top; rect.inputLeft = b.left - root.left; rect.inputWidth = b.width; }
      this.setState({ selBar: { kind: "compose", text: short, rect, from, to }, selAsk: "" });
      return true;
    };

    /* The character range [s, e) of a paragraph as a DOM range, across whatever formatting it holds. */
    paraRange = (para, s, e) => {
      const r = document.createRange();
      const walk = document.createTreeWalker(para, NodeFilter.SHOW_TEXT);
      let n, pos = 0, started = false;
      while ((n = walk.nextNode())) {
        const len = n.nodeValue.length;
        if (!started && s <= pos + len) { r.setStart(n, s - pos); started = true; }
        if (started && e <= pos + len) { r.setEnd(n, e - pos); return r; }
        pos += len;
      }
      return started ? (r.setEndAfter(para.lastChild), r) : null;
    };

    /* A rewrite in the composer marks the whole sentences the selection touches in place: they pulse while the
       agent works, then show struck beside the new text, and the editor is held until the change is kept or undone. */
    rewriteCompose = (variant, label, from, to) => {
      const bar = this.state.selBar;
      from = from || (bar && bar.kind === "compose" ? bar.from : null);
      to = to || (bar && bar.kind === "compose" ? bar.to : null);
      const el = this.bodyRef.current;
      if (!from || !to || !el) return;
      const before = this.state.cChange ? this.state.cChange.before : el.innerHTML;
      if (this.state.cChange) el.innerHTML = before;
      const paras = this.composeParas();
      const SENT = /.+?(?:[.!?]+["”’)]*(?=\s|$)|$)\s*/g;
      const pieces = [];
      for (let pi = from.pi; pi <= to.pi; pi++) {
        const t = paras[pi] ? paras[pi].textContent : "";
        const lo = pi === from.pi ? from.at : 0, hi = pi === to.pi ? to.at : t.length;
        const hit = []; let m; SENT.lastIndex = 0;
        while ((m = SENT.exec(t))) { if (!m[0]) break; const x = { s: m.index, e: m.index + m[0].trimEnd().length }; if (x.e > lo && x.s < hi) hit.push(x); }
        if (!hit.length) continue;
        pieces.push({ pi, s: hit[0].s, e: hit[hit.length - 1].e, old: t.slice(hit[0].s, hit[hit.length - 1].e), nw: null });
      }
      if (!pieces.length) return;
      this.dropSelection();
      pieces.slice().reverse().forEach((pc, i) => {
        const r = this.paraRange(paras[pc.pi], pc.s, pc.e);
        if (!r) return;
        const mark = document.createElement("span");
        mark.setAttribute("data-rewrite", String(pieces.length - 1 - i));
        mark.style.cssText = "background:var(--accent-soft);border-radius:4px;animation:mfpulse 1s ease-in-out infinite";
        mark.appendChild(r.extractContents());
        r.insertNode(mark);
      });
      this.setState({ selBar: null, selAsk: "", cChange: { busy: true, variant, label, pieces, before } });
      this.timers.push(setTimeout(() => {
        const ch = this.state.cChange;
        if (!ch || !this.bodyRef.current) return;
        const swap = (old) => old.split(/(?<=[.!?]["”’)]*)\s+/).map(x => ((DRAFT_REWRITES[x.trim()] || {})[variant]) || x).join(" ");
        const done = pieces.map(pc => Object.assign({}, pc, { nw: swap(pc.old) }));
        if (done.every(pc => pc.nw === pc.old)) {
          this.bodyRef.current.innerHTML = before;
          this.setState({ cChange: null });
          this.notify("The agent found nothing to change there.", { title: label });
          return;
        }
        const esc = (x) => x.replace(/&/g, "&amp;").replace(/</g, "&lt;");
        this.bodyRef.current.querySelectorAll("[data-rewrite]").forEach(mark => {
          const pc = done[+mark.getAttribute("data-rewrite")];
          mark.style.cssText = "";
          if (pc.nw === pc.old) { mark.replaceWith(...mark.childNodes); return; }
          mark.innerHTML = '<span style="color:var(--muted);text-decoration:line-through;text-decoration-color:var(--faint)">' + esc(pc.old) + "</span> "
            + '<span style="background:var(--accent-soft);color:var(--text);border-radius:4px;box-shadow:0 1.5px 0 var(--accent);padding:0 1px">' + esc(pc.nw) + "</span>";
        });
        this.setState({ cChange: Object.assign({}, ch, { busy: false, pieces: done }) });
      }, Math.max(500, (this.props.streamDelay ?? 550) * 1.4)));
    };

    rewriteComposeAsk = () => {
      const words = (this.state.selAsk || "").trim();
      if (!words) return;
      const preset = REWRITE_PRESETS.find(pr => pr.words.test(words)) || REWRITE_PRESETS[1];
      this.rewriteCompose(preset.key, "“" + words + "”");
    };

    keepComposeChange = () => {
      const ch = this.state.cChange, el = this.bodyRef.current;
      if (!ch || ch.busy || !el) return;
      el.querySelectorAll("[data-rewrite]").forEach(mark => { const pc = ch.pieces[+mark.getAttribute("data-rewrite")]; mark.replaceWith(document.createTextNode(pc.nw)); });
      this.setState({ cChange: null });
      this.notify(ch.label + " · kept in the message", { title: "Change kept", actionLabel: "Undo",
        onAction: () => { if (this.bodyRef.current) this.bodyRef.current.innerHTML = ch.before; } });
    };

    dropComposeChange = () => {
      const ch = this.state.cChange;
      if (ch && this.bodyRef.current) this.bodyRef.current.innerHTML = ch.before;
      this.setState({ cChange: null });
    };

    /* *Try again* puts the sentences back and asks for them under the next preset, as the Agent's does. */
    retryComposeChange = () => {
      const ch = this.state.cChange;
      if (!ch || ch.busy) return;
      const order = REWRITE_PRESETS.map(pr => pr.key);
      const next = order[(order.indexOf(ch.variant) + 1) % order.length];
      const p0 = ch.pieces[0], pl = ch.pieces[ch.pieces.length - 1];
      this.rewriteCompose(next, ch.label.replace(/ · another version$/, "") + " · another version", { pi: p0.pi, at: p0.s }, { pi: pl.pi, at: pl.e });
    };

    /* A passage asked about is answered where it stands, in the bar over it, rather than at the bottom of the
       conversation; *Keep in the conversation* is what writes it there. */
    answerSel = (kind) => {
      const bar = this.state.selBar;
      if (!bar) return;
      this.setState({ selBar: Object.assign({}, bar, { answer: { kind, busy: true } }) });
      this.timers.push(setTimeout(() => {
        const cur = this.state.selBar;
        if (!cur || cur.text !== bar.text) return;
        this.setState({ selBar: Object.assign({}, cur, { answer: { kind, busy: false, text: SEL_REPLIES[kind].text } }) });
      }, Math.max(500, (this.props.streamDelay ?? 550) * 1.3)));
    };

    keepSelAnswer = () => {
      const bar = this.state.selBar;
      if (!bar || !bar.answer || bar.answer.busy) return;
      const r = SEL_REPLIES[bar.answer.kind];
      this.dropSelection();
      this.setState(s2 => Object.assign({ selBar: null }, this.pushMsg(s2, [
        { role: "user", anim: true, text: r.label, quote: bar.text },
        { role: "bot", anim: true, scope: "1 passage · from " + bar.from, text: r.text, bullets: [], blocks: [] },
      ])));
      this.scrollAgentDown && this.scrollAgentDown();
    };

    quoteSel = () => {
      const bar = this.state.selBar;
      if (!bar) return;
      this.dropSelection();
      this.setState({ selBar: null, agentQuote: { text: bar.text, from: bar.kind === "draft" ? "the draft" : bar.from } }, () => {
        const el = this.agentInputRef.current; if (el) el.focus();
      });
    };

    /* One passage becomes one task proposal, still to be approved like any other. */
    selToTask = () => {
      const bar = this.state.selBar;
      if (!bar) return;
      const t = (bar.full || bar.text).replace(/^[“"]|[”"]$/g, "");
      const title = t.length > 72 ? t.slice(0, 69).trimEnd() + "…" : t;
      this.dropSelection();
      this.setState(s2 => Object.assign({ selBar: null, agentThinking: true }, this.pushMsg(s2, [{ role: "user", anim: true, text: "Turn this into a task", quote: bar.text }])));
      this.timers.push(setTimeout(() => this.setState(s2 => Object.assign({ agentThinking: false }, this.pushMsg(s2, [{
        role: "bot", anim: true, scope: "tasks · from this conversation", text: "Here it is as a task. Nothing is added until you approve it.", bullets: [],
        blocks: [{ type: "task", meta: "tasks", phase: "pending", title: title.charAt(0).toUpperCase() + title.slice(1), due: SEL_REPLIES.task.due, reminder: SEL_REPLIES.task.reminder, from: "this conversation", done: "Added to your task list" }],
      }]))), Math.max(400, (this.props.streamDelay ?? 550))));
    };

    /* One field of a proposal changed on its own: the agent's alternatives come with their reason, and a typed
       value is taken as said. The rest of the proposal stays as it was. */
    setFieldVal = (key, k, v) => this.setState(s2 => ({
      bkVals: Object.assign({}, s2.bkVals, { [key]: Object.assign({}, (s2.bkVals || {})[key], { [k]: v }) }),
      bkField: null, fieldAsk: "",
    }));

    /* One turn the reader asked for by pressing rather than typing: their line, then the agent's answer. */
    agentTurn = (userMsg, botMsg) => {
      this.setState(s2 => Object.assign({ agentThinking: true }, this.pushMsg(s2, [Object.assign({ role: "user", anim: true }, userMsg)])));
      this.scrollAgentDown();
      this.timers.push(setTimeout(() => {
        this.setState(s2 => Object.assign({ agentThinking: false }, this.pushMsg(s2, [Object.assign({ role: "bot", anim: true, bullets: [], blocks: [] }, botMsg)])));
        this.scrollAgentDown();
      }, Math.max(450, (this.props.streamDelay ?? 550) * 1.1)));
    };

    /* An answer about one item of a block is written under that item, not at the bottom of the conversation. */
    noteBk = (nkey, label, text) => {
      this.setState(s2 => ({ bkNote: Object.assign({}, s2.bkNote, { [nkey]: { label, busy: true } }) }));
      this.timers.push(setTimeout(() => this.setState(s2 => ({ bkNote: Object.assign({}, s2.bkNote, { [nkey]: { label, busy: false, text } }) })),
        Math.max(450, (this.props.streamDelay ?? 550) * 1.2)));
    };

    dropNote = (nkey) => this.setState(s2 => ({ bkNote: Object.assign({}, s2.bkNote, { [nkey]: null }) }));

    /* People are picked as a set; a timeline is picked as a range - the first press opens it, the second closes it. */
    pickBk = (key, i, range) => this.setState(s2 => {
      const cur = (s2.bkPick || {})[key];
      const next = range
        ? (!cur || cur.to !== undefined ? { from: i } : { from: Math.min(cur.from, i), to: Math.max(cur.from, i) })
        : ((cur || []).indexOf(i) >= 0 ? cur.filter(x => x !== i) : (cur || []).concat([i]).sort((x, y) => x - y));
      return { bkPick: Object.assign({}, s2.bkPick, { [key]: next }), bkNote: Object.assign({}, s2.bkNote, { [key + "#range"]: null }) };
    });

    clearPick = (key) => this.setState(s2 => ({ bkPick: Object.assign({}, s2.bkPick, { [key]: null }), bkNote: Object.assign({}, s2.bkNote, { [key + "#range"]: null }) }));

    namesList = (names) => names.length < 3 ? names.join(" and ") : names.slice(0, -1).join(", ") + " and " + names[names.length - 1];

    draftToPeople = (key, names) => {
      this.clearPick(key);
      this.agentTurn({ text: "Draft a message to " + this.namesList(names) }, {
        scope: "draft · " + names.length + (names.length === 1 ? " person" : " people"),
        text: "Here is a draft to " + (names.length === 1 ? names[0] : "all " + names.length) + ". Nothing goes out until you approve it.",
        blocks: [{ type: "draft", phase: "pending", meta: "new message", to: this.namesList(names), subject: PEOPLE_ACTS.draft.subject, paras: PEOPLE_ACTS.draft.paras, done: "Sent" }],
      });
    };

    invitePeople = (key, names) => {
      this.clearPick(key);
      const iv = PEOPLE_ACTS.invite;
      this.agentTurn({ text: "Invite " + this.namesList(names) + " to a meeting" }, {
        scope: "calendar · " + (names.length + 1) + " calendars",
        text: "The first half-hour all of you have free is Thursday morning. Nothing is sent until you approve it.",
        blocks: [{ type: "event", meta: "calendar · work", phase: "pending", title: iv.title, when: iv.when, duration: iv.duration, attendees: "You, " + names.join(", "), from: "Contract addendum - signatures", done: "Invitation sent", nav: [{ label: "Open the calendar", icon: "calendar_month", kind: "gocal" }] }],
      });
    };

    /* Leaving one source out is a new answer, written as a new turn; the old one stays, marking what was left out. */
    excludeSource = (key, b, i) => {
      const excl = ((this.state.bkExcl || {})[key] || []).concat([i]);
      const it = b.items[i];
      const rest = b.items.filter((_, j) => excl.indexOf(j) < 0);
      this.setState(s2 => ({ bkExcl: Object.assign({}, s2.bkExcl, { [key]: excl }) }));
      this.agentTurn({ text: "Answer again without “" + it.source + "”" }, {
        scope: "answered again · " + rest.length + (rest.length === 1 ? " source" : " sources"),
        text: it.without || "The reading holds on the remaining sources, with one step less confidence.",
        blocks: rest.length ? [Object.assign({}, b, { meta: rest.length + (rest.length === 1 ? " source" : " sources") + " · without " + it.source, items: rest })] : [],
      });
    };

    /* “Another time” is not a silent reshuffle — it says so in the conversation and comes back
       with the same proposal at a new hour, still needing approval. */
    anotherTimeBk = (key, b) => {
      this.declineBk(key);
      this.setState(s2 => Object.assign({ agentThinking: true }, this.pushMsg(s2, [{ role: "user", text: "Find another time for “" + b.title + "”" }])));
      this.timers.push(setTimeout(() => this.setState(s2 => Object.assign({ agentThinking: false }, this.pushMsg(s2, [{
        role: "bot", scope: ANOTHER_TIME.scope,
        text: ANOTHER_TIME.text,
        blocks: [Object.assign({}, b, { phase: "pending", when: ANOTHER_TIME.when, conflict: "" })],
      }]))), Math.max(400, (this.props.streamDelay ?? 550))));
    };

    /* A source an Agent answer cites, opened for checking: beside the conversation on the desktop, over it below that.
       It belongs to the conversation it was cited in, and closing it hands the keyboard back to the pressed citation. */
    openAgentEv = (e) => {
      this._agentEvOpener = typeof document !== "undefined" ? document.activeElement : null;
      this.setState({ agentEv: { conv: this.state.convId || null, n: e.n, source: e.source, badge: e.badge, snippet: e.snippet, freshness: e.freshness } });
    };
    closeAgentEv = () => {
      const opener = this._agentEvOpener;
      this._agentEvOpener = null;
      this.setState({ agentEv: null });
      if (opener && opener.focus) setTimeout(() => opener.focus(), 0);
    };

    /* Going to look at a thread or the calendar must not lose the place in the conversation. */
    goLook = (a) => {
      this.setState({ agentReturn: this.state.convId || null });
      this.agentAction(a);
    };

    backToConv = () => this.setState(s2 => ({ screen: "agent", convId: s2.agentReturn || s2.convId, agentReturn: null }));

    agentAction = (a) => {
      if (a.kind === "draft") {
        this.pendingDraft = AGENT_DRAFT_HTML;
        if (this.tabsMode()) this.openTab("compose", { screen: "mail", compose: true, composeMin: false });
        else this.setState({ screen: "mail", compose: true, composeMin: false });
      } else if (a.kind === "cal") {
        this.setState(s2 => ({
          screen: "cal",
          calView: "Day",
          calAdded: (s2.calAdded || []).concat(AGENT_SCHEDULED),
        }));
      } else if (a.kind === "gocal") this.setState({ screen: "cal", calView: "Day" });
      else if (a.kind === "thread") this.setState({ screen: "mail", threadId: a.tid, mailPane: "thread", threadOpen: true, cameFrom: false });
      else if (a.kind === "planday") this.agentAsk("Plan my day");
    };

    /* Toolbar — full labels as long as they fit the available width.
       A new screen is rendered "at full size", we measure the real content width and only then
       collapse to icons if needed. The required width is cached per toolbar,
       so on window resize the decision is instant and flicker-free. */
    _tbNeed = {};
    _tbNeedFab = {};

    tbRef = el => {
      if (this._tbEl === el) return;
      if (this._tbRO) { this._tbRO.disconnect(); this._tbRO = null; }
      this._tbEl = el;
      if (el && typeof ResizeObserver === "function") {
        this._tbRO = new ResizeObserver(() => this.tbMeasure());
        this._tbRO.observe(el);
      }
      this.tbMeasure();
    };

    /* "full" = primary button in the bar + labels; "fab" = labels stay but the primary button
       moves into a floating circle (the phone form); "icons" = icons only. */
    tbModeFor(key) { return (this.state.tbMode || {})[key] || "full"; }

    /* Once the icon font (ligatures) loads, widths change — we measure again. */
    tbReset = () => {
      this._tbNeed = {};
      this._tbNeedFab = {};
      this.setState({ tbMode: {} }, this.tbMeasure);
    };

    tbMeasure = () => {
      const el = this._tbEl, key = this._tbKey;
      if (!el || !key) return;
      if (document.fonts && document.fonts.status !== "loaded") return;
      const avail = el.clientWidth;
      const kids = Array.prototype.slice.call(el.children);
      if (!avail || !kids.length) return;
      const mode = this.tbModeFor(key);
      if (mode !== "icons") {
        /* Sum of the children's real widths — not the edges, because "Select all"
           has margin-left:auto and would stretch the measurement to the whole bar. */
        const cs = getComputedStyle(el);
        const gap = parseFloat(cs.columnGap || cs.gap || 0) || 0;
        let need = parseFloat(cs.paddingLeft || 0) + parseFloat(cs.paddingRight || 0) + gap * (kids.length - 1);
        kids.forEach(k => {
          const ks = getComputedStyle(k);
          /* margin:auto pushes an element to the end of the bar — we count real gaps only. */
          const mg = m => { const v = parseFloat(m || 0); return isNaN(v) ? 0 : Math.min(v, 8); }; // margin:auto does not count towards the required width
          need += k.offsetWidth + mg(ks.marginLeft) + mg(ks.marginRight);
        });
        if (mode === "full" && this._tbHasPri && kids.length > 2) {
          const ks0 = getComputedStyle(kids[0]);
          const pri = kids[0].offsetWidth + (parseFloat(ks0.marginLeft) || 0) + (parseFloat(ks0.marginRight) || 0);
          const div = kids[1].offsetWidth + 4;
          this._tbNeed[key] = need;
          this._tbNeedFab[key] = need - pri - div - gap * 2;
        } else {
          this._tbNeedFab[key] = need;
          if (this._tbNeed[key] == null) this._tbNeed[key] = need;
        }
      }
      const needFull = this._tbNeed[key], needFab = this._tbNeedFab[key];
      if (needFab == null) return;
      const next = (this._tbHasPri && needFull != null && needFull <= avail + 1) ? "full"
        : needFab <= avail + 1 ? (this._tbHasPri ? "fab" : "full")
        : "icons";
      if (this.tbModeFor(key) !== next) {
        this.setState(s => {
          const m = Object.assign({}, s.tbMode);
          m[key] = next;
          return { tbMode: m };
        });
      }
    };

    componentDidUpdate(prevProps) {
      /* `space` opens a space the way its rail item does, so a capture reaches it in one step at every
         composition - on a phone Tasks, Calendar and People sit behind More. */
      const space = this.props.space;
      if (space && space !== (prevProps || {}).space) {
        this.goScreen(space === "calendar" ? "cal" : space, space === "people" ? { peoplePane: "list" } : space === "mail" ? { mailPane: "list" } : {});
      }
      this.backSync();
      this.tbMeasure();
      if (!this.state.listLoading) this.restoreScrolls();
      if (this.pendingDraft && this.bodyRef.current) {
        this.bodyRef.current.innerHTML = this.pendingDraft;
        this.pendingDraft = null;
        this.setState({ aiDraft: true });
      }
      if (this.pendingAiWrite && this.bodyRef.current) {
        this.pendingAiWrite = false;
        this.aiWrite("full");
      }
      if (this.state.historyFocusPending && this.historySearchRef.current) {
        this.historySearchRef.current.focus();
        this.setState({ historyFocusPending: false });
      }
      if (this.state.agentFocusPending && this.agentInputRef.current) {
        this.agentInputRef.current.focus();
        this.setState({ agentFocusPending: false });
      }
      if (this.state.listSearchFocusPending && this.listSearchRef.current) {
        this.listSearchRef.current.focus();
        this.setState({ listSearchFocusPending: false });
      }
    }

    revealMessage = (tid, idx) => {
      const k = tid + ":" + idx;
      this.setState(s => ({
        msgOpen: Object.assign({}, s.msgOpen, { [k]: true }),
        /* We leave the thread collapsed to the selected message — the pill offers "Show all". */
        histOpen: Object.assign({}, s.histOpen, { [tid]: false }),
        activeMsg: Object.assign({}, s.activeMsg, { [tid]: idx }),
        msgFlash: k,
      }));
      this.timers.push(setTimeout(() => this.setState({ msgFlash: null }), 2200));
    };

    beginHistoryResize = (e) => {
      e.preventDefault();
      const startX = e.clientX;
      const startW = this.state.historyPanelW || 250;
      const move = (ev) => {
        const w = Math.max(200, Math.min(480, startW + (ev.clientX - startX)));
        this.setState({ historyPanelW: w });
      };
      const up = () => {
        window.removeEventListener("mousemove", move);
        window.removeEventListener("mouseup", up);
        document.body.style.userSelect = "";
        document.body.style.cursor = "";
      };
      document.body.style.userSelect = "none";
      document.body.style.cursor = "col-resize";
      window.addEventListener("mousemove", move);
      window.addEventListener("mouseup", up);
    };

    beginSideResize = (e) => {
      e.preventDefault();
      const startX = e.clientX;
      const startW = Math.max(210, Math.min(420, this.state.sideW || 210));
      const move = (ev) => this.setState({ sideW: Math.max(210, Math.min(420, startW + (ev.clientX - startX))) });
      const up = () => {
        window.removeEventListener("mousemove", move);
        window.removeEventListener("mouseup", up);
        document.body.style.userSelect = "";
        document.body.style.cursor = "";
      };
      document.body.style.userSelect = "none";
      document.body.style.cursor = "col-resize";
      window.addEventListener("mousemove", move);
      window.addEventListener("mouseup", up);
    };

    beginResize = (e) => {
      e.preventDefault();
      const startX = e.clientX;
      const startW = this._listW || 340;
      const move = (ev) => {
        const w = Math.max(232, Math.min(560, startW + (ev.clientX - startX)));
        this.setState({ listW: w });
      };
      const up = () => {
        window.removeEventListener("mousemove", move);
        window.removeEventListener("mouseup", up);
        document.body.style.userSelect = "";
        document.body.style.cursor = "";
      };
      document.body.style.userSelect = "none";
      document.body.style.cursor = "col-resize";
      window.addEventListener("mousemove", move);
      window.addEventListener("mouseup", up);
    };

    componentWillUnmount() {
      document.removeEventListener("mousedown", this.onDocDown, true);
      window.removeEventListener("message", this.onHtmlFrameMessage);
      window.removeEventListener("popstate", this.onBackPressed);
      document.removeEventListener("selectionchange", this.onSelChange);
      document.removeEventListener("pointerdown", this.onSelPointer, true);
      document.removeEventListener("pointerup", this.onSelPointer, true);
      this.htmlFrames.forEach(r => { if (r.timer) clearTimeout(r.timer); });
      this.htmlFrames.clear();
      this.timers.forEach(clearTimeout);
      if (this.measure) window.removeEventListener("resize", this.measure);
      if (this.ro) this.ro.disconnect();
    }

    planFor(q) {
      const s = (q || "").toLowerCase();
      if (s.includes("polic") || s.includes("file") || s.includes("document") || s.includes("attach")) return "files";
      if (s.includes("who") || s.includes("person") || s.includes("people") || s.includes("approv")) return "people";
      return "contract";
    }

    run(q) {
      this.timers.forEach(clearTimeout);
      this.timers = [];
      const key = this.planFor(q);
      this.setState({ query: q, planKey: key, phase: "running", ready: {}, ev: null, cameFrom: false, mailAnswer: false, resultFilter: null });
      /* Start answers short: the answer and the messages it came from. Anything longer is the Agent's. */
      this.streamBlocks(["answer"]);
    }

    streamBlocks(blocks) {
      const step = Math.max(120, (this.props.streamDelay ?? 550));
      blocks.forEach((b, i) => {
        this.timers.push(setTimeout(() => {
          this.setState(s => ({ ready: Object.assign({}, s.ready, { [b]: true, evidence: true }) }));
        }, step * (i + 1)));
      });
      this.timers.push(setTimeout(() => this.setState({ phase: "done" }), step * (blocks.length + 1)));
    }

    /* The plan behind the answer on screen. */
    askPlan(st) {
      return st.planKey ? PLANS[st.planKey] : null;
    }

    /* Start's one step is pressed and can be taken back: the toast carries the undo. */
    acceptStartStep = (key, b) => {
      if (b.type === "draft") {
        this.setBk(key, { phase: "accepted", result: "Opened in the composer · " + this.hm() });
        /* It lands as an AI draft still to be accepted there, exactly as one written in the composer would. */
        this.pendingDraft = this.draftParas(key, b).map(t => "<p>" + t + "</p>").join("");
        /* The same reply composer *Draft a reply* opens, so the thread stands as the draft's context. */
        this.goScreen("mail", { composeSubjEdit: b.subject, closeAsk: false, sendAsk: null });
        this.startReply("reply", ["contoso"]);
        this.notify("Nothing is sent until you send it from there.", { kind: "success", title: "Draft opened in the composer" });
        return;
      }
      this.acceptBk(key, b);
      this.notify(b.title, { kind: "success", title: b.done || "Done", actionLabel: "Undo", onAction: () => { this.hideToast(); this.setBk(key, { phase: "pending" }); } });
    };

    renderVals() {
      const { st, mo } = this.motionHold(this.state);
      const aiSumThread = st.aiSum ? THREADS.find(t => t.id === st.aiSum) : null;
      const hidMap = st.hidden || {};
      const hid = (k, id) => (hidMap[k] || []).indexOf(id) >= 0;
      const selMap = st.selBy || {};
      const isSel = (kind, id) => (selMap[kind] || []).indexOf(id) >= 0;
      const selMark = (kind, id) => (isSel(kind, id) ? "background:var(--accent-soft);box-shadow:inset 3px 0 0 var(--accent);" : "");
      const plan = this.askPlan(st);
      const selBarKind = st.notifOpen ? "notifs"
        : st.screen === "tasks" ? "tasks"
        : st.screen === "cal" ? "events"
        : st.screen === "people" ? "people"
        : st.screen === "agent" ? "convs"
        : "";
      const selBarIds = (selMap[selBarKind] || []);
      const hasRun = st.phase !== "idle";
      const ready = st.ready;

      const mode = this.props.layout ?? "auto";
      const FR = FRAMES[mode] || null;
      /* Three axes, and every rule below reads exactly one of them — state-inventory.md § How a window is composed.
         Width decides the columns, height the vertical density, the pointer the targets, gestures and hover. Nothing
         reads orientation or aspect: upright and on its side are just two combinations of a width and a height band. */
      const w = FR ? FR.w : (st.vw || 1440);
      const h = FR ? FR.h : (st.vh || 900);
      const isMobile = w < 700;
      const singlePane = isMobile || w < 820;
      const compact = !isMobile && w < 1180;
      const narrow = !isMobile && w < 1020;
      const wide = w >= 1920;
      const low = h < 480;
      const tall = h >= 960;
      /* The framed previews are the three touch devices; anything else asks the pointer, as the client does. */
      const touch = FR ? true : mode === "desktop" ? false : !!st.coarse;
      const startWide = !isMobile && w >= 1680;
      const agentCorner = (isMobile ? 20 : 24) + (touch ? 56 : 52) + 12 - (isMobile ? 14 : 26);
      /* Start's deck of steps: what is still to take, plus a step just taken or declined until the reader moves on. */
      const stepSettled = (i) => ((st.bkPhase || {})["start#" + i] || "pending") !== "pending";
      const stepDeck = HOME.steps.map((b, i) => i).filter(i => !(stepSettled(i) && (st.stepsGone || []).indexOf(i) >= 0));
      const stepAt = Math.max(0, Math.min(stepDeck.length - 1, st.stepAt || 0));
      const moveStep = (by) => this.setState(s2 => {
        const gone = (s2.stepsGone || []).concat(stepDeck.filter(stepSettled));
        const left = HOME.steps.map((b, i) => i).filter(i => !(stepSettled(i) && gone.indexOf(i) >= 0));
        const pos = left.indexOf(stepDeck[stepAt]);
        const before = stepDeck.slice(0, stepAt).filter(i => left.indexOf(i) >= 0).length;
        const to = pos >= 0 ? pos + by : by > 0 ? before : before - 1;
        return { stepsGone: gone, stepAt: Math.max(0, Math.min(left.length - 1, to)) };
      });
      /* Start's frame: the header and the day share one left and one right edge, so the field, the watched cards, the
         columns and the cards in them all end on the same lines. The asides grow with the window within their bounds and
         the main column takes the rest, so a large screen is filled rather than left dark on the right; only past
         2400 px does the frame stop growing. The header's carousel and the day read the same widths, so they agree. */
      const frameX = isMobile ? 14 : narrow ? 18 : 28;
      const frameMax = 2400;
      const asideX = compact ? "300px" : startWide ? "clamp(340px, 17%, 460px)" : "clamp(340px, 25%, 400px)";
      const asideY = "clamp(400px, 21%, 560px)";
      const framePad = (top, bottom) => (isMobile || narrow || compact)
        ? "padding:" + top + "px " + frameX + "px " + bottom + "px;"
        : "padding:" + top + "px max(" + frameX + "px, calc(100% - " + (frameMax + frameX) + "px)) " + bottom + "px " + frameX + "px;";
      const watchPer = isMobile ? 1 : (narrow || compact) ? 2 : startWide ? 4 : 3;
      const watchList = st.watched || [];
      const watchPages = Math.max(1, Math.ceil(watchList.length / watchPer));
      const watchAt = Math.max(0, Math.min(watchPages - 1, st.watchAt || 0));
      const stepNav = (on) => "display:inline-flex;align-items:center;justify-content:center;box-sizing:border-box;border-radius:9px;border:1px solid var(--border2);color:" + (on ? "var(--text2)" : "var(--faint)") + ";font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;" +
        (touch ? "width:44px;height:44px;font-size:22px;" : "width:30px;height:30px;font-size:19px;") + (on ? "" : "opacity:0.5;");
      const railW = compact ? 66 : 96;
      this._low = low;
      this._isMobile = isMobile;
      /* A dialog takes what a low window has and stops growing in a tall one. */
      const dlgMaxH = low ? "calc(100dvh - 16px)" : tall ? "min(calc(100dvh - 44px), 760px)" : "calc(100dvh - 44px)";
      const toastCompact = w < 900;
      /* The centre opens on what the reader has not seen yet; *All* is one press away. */
      const notifFilter = st.notifFilter || "unread";
      const notifAll = (st.notifs || NOTIFS).filter(n => !hid("notifs", n.id));
      const notifRead = st.notifRead || {};
      const notifUnreadCount = notifAll.filter(n => isNotifUnread(n, notifRead)).length;
      const notifIn = !!st.notifShown;
      const notifDrag = st.notifDrag || 0;
      const notifDragging = !!(this._notifG && this._notifG.live);
      const notifH = st.notifH || Math.max(320, (st.vh || 844) - this.barH());
      /* Scrim opacity straight from the distance travelled — panel and scrim move with the finger. */
      const notifProg = notifIn ? Math.max(0, 1 - Math.min(1, notifDrag / notifH)) : 0;
      const notifShown = notifFilter === "unread" ? notifAll.filter(n => isNotifUnread(n, notifRead)) : notifAll;
      const avSz = isMobile ? 34 : 22;
      const mailPane = st.mailPane || "list";
      /* The one-pane head folds as the thread scrolls; a low window keeps it folded and a tall one keeps it open. */
      const thCollapsed = low || (!!st.thScroll && !tall);
      /* Width decides that the folder list stands as a column only from 1180; the pointer decides what it folds into
         below that — a drawer under a finger, which has no hover to read a strip of icons by, and the 58 px strip of
         icons under a mouse. The phone has no rail to fold beside, so its list is always the drawer. */
      const drawer = isMobile || (compact && !!touch);
      const drawerOpen = !!st.sideDrawer;
      const rowStyleFor = (active, selected) =>
        "position:relative;display:flex;flex-direction:column;user-select:none;-webkit-user-select:none;" +
        (isMobile ? "gap:6px;padding:14px 16px;min-height:76px;justify-content:center;" : "gap:5px;padding:12px 14px;") + "transition:background 120ms ease,box-shadow 120ms ease;" +
        (selected
          ? "background:var(--accent-soft);box-shadow:inset 0 1px 0 var(--inset),inset 4px 0 0 var(--accent),0 3px 9px var(--sh-2);z-index:2;"
          : active
          ? "background:var(--accent-soft);box-shadow:inset 0 1px 0 var(--inset),inset 3px 0 0 var(--accent-2),0 3px 9px var(--sh-2);z-index:1;"
          : "background:var(--panel);box-shadow:inset 0 1px 0 var(--inset),0 1px 2px var(--sh-1);");
      const tbBtn = "display:flex;align-items:center;gap:7px;flex:0 0 auto;font-size:13px;color:var(--text2);padding:7px 11px;border-radius:8px;white-space:nowrap;transition:background 120ms ease";
      const tbIcon = "display:flex;align-items:center;justify-content:center;color:var(--text2);border-radius:8px;" +
        (touch ? "font-size:20px;width:42px;height:42px;" : "font-size:15px;width:34px;height:34px;");
      const selBtn = "display:flex;align-items:center;gap:7px;flex:0 0 auto;font-size:13px;color:var(--accent-d);padding:7px 11px;border-radius:8px;white-space:nowrap";
      const sel = Array.isArray(st.sel) ? st.sel : [];
      const screenKey = st.screen || "mail";
      this._tbKey = screenKey === "mail" && sel.length ? "mailSel" : screenKey;
      const tbHasPri = !isMobile && this._tbKey !== "mailSel";
      this._tbHasPri = tbHasPri;
      const tbMode = this.tbModeFor(this._tbKey);
      const tbFull = tbMode !== "icons";
      const tbPriInBar = tbHasPri && tbMode === "full";
      const tbFabForced = tbHasPri && tbMode !== "full";
      const inspW = narrow ? 288 : compact ? 320 : 400;

      const railBase = isMobile
        ? "flex:1;min-width:0;min-height:56px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:4px;padding:6px 0;border-radius:16px;font-size:12px;"
        : "display:flex;flex-direction:column;align-items:center;gap:4px;width:calc(100% - 14px);box-sizing:border-box;border-radius:12px;padding:" + (low ? (touch ? 0 : 4) : compact ? 8 : 9) + "px 0;" + (low ? "min-height:" + (touch ? 44 : 36) + "px;justify-content:center;" : "");
      /* The phone marks the current place with a pill behind its glyph rather than a tile around the whole item: a
         tile the full height of the bar reads as a pressed button, and the bar is navigation. */
      const activeStyle = isMobile
        ? "--nav-mark:var(--accent-soft);color:var(--accent-d);font-weight:600;"
        : "background:var(--accent-soft);color:var(--accent-d);font-weight:600;";
      const idleColor = "color:var(--muted);";
      const myName = st.myName !== undefined ? st.myName : ME;
      const myPhoto = st.myPhoto || "";
      const themePref = st.themePref || "auto";
      const sysDark = typeof matchMedia === "function" && matchMedia("(prefers-color-scheme: dark)").matches;
      const theme = themePref === "auto"
        ? (st.themeOverride || (this.props.theme ?? (sysDark ? "dark" : "light")))
        : themePref;

      const openEv = (cite) => () => {
        if (!plan) return;
        const e = plan.evidence.find(x => x.n === cite) || plan.evidence[0];
        this.setState({ ev: e.n });
      };
      const evObj = plan && st.ev ? plan.evidence.find(x => x.n === st.ev) : null;
      const agentEvObj = st.agentEv && st.agentEv.conv === (st.convId || null) ? st.agentEv : null;
      const thread = THREADS.find(t => t.id === st.threadId) || THREADS[0];
      const threadAddr = msgAddrRows(thread, (msgsFor(thread) || [])[0] || { from: thread.from, mine: false }, msgsFor(thread) || []);
      const flags = st.flags || {};
      /* The demo starts with the five newest threads unread — opening a thread clears it. */
      const unreadMap = st.unread || DEFAULT_UNREAD;
      const movedMap = st.moved || {};
      const rowAnim = st.rowAnim || {};
      const rowOut = (id) => rowAnim[id] === "out" || rowAnim[id] === "outdel" || rowAnim[id] === "outarc";
      /* the wash names the act: red for a deletion, orange for archive and move */
      const rowWash = (id) => rowAnim[id] === "outdel" ? "animation:mfrowdel 460ms ease-out both;"
        : rowAnim[id] === "outarc" ? "animation:mfrowarc 460ms ease-out both;"
        : rowAnim[id] === "move" ? "animation:mfrowmoved 900ms ease-out both;"
        : rowAnim[id] === "edit" ? "animation:mfrowedit 1700ms ease-out both;" : "";
      const notifAnim = st.notifAnim || {};
      const actTargets = sel.length ? sel : [st.threadId];
      const flagOn = actTargets.every(id => !!flags[id]);
      const wantTabs = (st.workMode || "tabs") === "tabs";
      const rmode = st.replyMode || null;
      const composeKind = rmode === "reply" ? "Reply" : rmode === "all" ? "Reply all" : rmode === "fwd" ? "Forward" : "New message";
      /* A reply keeps the conversation it answers, whatever the list is pointed at afterwards. */
      const replyThread = (rmode && THREADS.find(t => t.id === st.replyThreadId)) || thread;
      const threadParticipants = (msgsFor(replyThread) || []).map(m => m.from).filter((n, i, a) => a.indexOf(n) === i);
      const composeTo = rmode === "fwd" ? [] : rmode === "all" ? threadParticipants : rmode === "reply" ? threadParticipants.slice(-1) : NEW_MESSAGE_TO.slice();
      const composeSubj = rmode === "fwd" ? "Fwd: " + replyThread.subject : rmode ? "Re: " + replyThread.subject : "";
      const composeCtx = (st.composeCtx || []).filter(id => THREADS.some(t => t.id === id));
      const composeCtxBase = st.composeCtxBase || null;
      const composeCtxChanged = !!composeCtxBase && (composeCtx.length !== composeCtxBase.length || composeCtx.some(id => composeCtxBase.indexOf(id) < 0));
      const ctxSubject = (id) => (THREADS.find(t => t.id === id) || {}).subject || "";
      const curSubj = st.composeSubjEdit !== undefined ? st.composeSubjEdit : composeSubj;
      const bodyTxt = () => {
        const el = this.bodyRef.current;
        return el ? (el.innerText || "").replace(/\u200b/g, "").trim() : "";
      };
      const finishCompose = () => {
        const patch = { compose: false, composeCtx: [], composeCtxBase: null, replyThreadId: null, aiDraft: false, aiBusy: false, closeAsk: false, sendAsk: null, composeSubjEdit: undefined, ccOpen: false, composeCc: [], composeBcc: [], ccInput: "", bccInput: "", composeReplyTo: [], replyToInput: "", cChange: null,
          selBar: st.selBar && st.selBar.kind === "compose" ? null : st.selBar };
        if ((st.workMode || "classic") === "tabs") this.closeTab("compose");
        this.setState(patch);
      };
      const askSend = () => {
        /* Nothing is sent while a rewrite waits to be kept or undone. */
        if (st.cChange) return;
        const w = [];
        if (!curSubj.trim()) w.push("The message has no subject.");
        if (!bodyTxt()) w.push("The message body is empty.");
        if (!composeTo.length) w.push("The message has no recipient.");
        if (st.aiDraft) w.push("The AI draft has not been accepted yet - check the text before sending.");
        this.setState({ sendAsk: { warnings: w } });
      };
      const mailAct = {
        reply: () => this.startReply("reply"),
        reply_all: () => this.startReply("all"),
        forward: () => this.startReply("fwd"),
        archive: () => this.archiveThreads(),
        delete: () => this.deleteThreads(),
        flag: () => this.toggleFlagThreads(),
        mark_email_unread: () => this.markUnread(),
        drive_file_move: () => this.openMove(),
        fullscreen: () => this.setState({ stateBarOff: true }),
        fullscreen_exit: () => this.setState({ stateBarOff: false }),
      };
      const msgs = msgsFor(thread);
      const htmlViewMode = st.msgView === "html";
      const lastIdx = msgs.length - 1;
      const openMap = st.msgOpen || {};
      const mKey = (i) => thread.id + ":" + i;
      const histRaw = (st.histOpen || {})[thread.id];
      const histOpen = typeof histRaw === "boolean" ? histRaw : !!st.autoExpand;
      /* The message picked from the list; the newest in the thread by default. */
      const rawActive = (st.activeMsg || {})[thread.id];
      const activeIdx = Math.max(0, Math.min(typeof rawActive === "number" ? rawActive : lastIdx, lastIdx));
      const activeIsNewest = activeIdx === lastIdx;
      const hiddenCount = histOpen ? 0 : lastIdx;
      const items = [];
      if (msgs.length > 1) {
        items.push({
          isPill: true, isMsg: false, key: "pill",
          pillLabel: !hiddenCount
            ? (activeIsNewest ? "Hide earlier messages" : "Hide the other messages")
            : activeIsNewest
              ? "Show " + hiddenCount + (hiddenCount === 1 ? " earlier message" : " earlier messages")
              : "Show all messages",
          /* Showing unfolds the messages it brings; hiding folds them away first and only then takes them out. */
          togglePill: () => {
            if (!histOpen) { this.setState(s2 => ({ histUnfolded: thread.id, histOpen: Object.assign({}, s2.histOpen, { [thread.id]: true }) })); return; }
            this.setState({ histFolding: thread.id });
            setTimeout(() => this.setState(s2 => ({ histFolding: null, histOpen: Object.assign({}, s2.histOpen, { [thread.id]: false }) })), this.constructor.MOTION_EXIT_MS);
          },
        });
      }
      msgs.forEach((m, i) => {
        /* A collapsed thread shows only the message picked from the list — even when it is not
           the newest; the pill then reveals both earlier and newer ones. */
        if (hiddenCount && i !== activeIdx) return;
        const k = mKey(i);
        const open = true;
        const flash = st.msgFlash === k;
        const isActive = i === activeIdx && msgs.length > 1 && !hiddenCount;
        const remoteRefs = m.remoteRefs != null ? m.remoteRefs
          : m.mine ? 0 : (((m.from || "") + (m.when || "")).split("").reduce((a, c) => (a * 31 + c.charCodeAt(0)) >>> 0, 7) % 3 === 0 ? 0 : 3 + (((m.from || "").length * 7 + (m.when || "").length) % 16));
        const remoteOn = !!(st.remoteLoaded || {})[k];
        const folding = st.histFolding === thread.id;
        items.push({
          isMsg: true, isPill: false, key: k, isActive,
          unfoldMotion: i !== activeIdx && (folding || st.histUnfolded === thread.id) ? "unfold" : "none",
          unfoldPhase: folding && i !== activeIdx ? "out" : "in",
          remoteBlocked: remoteRefs > 0 && !remoteOn,
          remoteLoaded: remoteRefs > 0 && remoteOn,
          remoteCountLabel: "Removed references: " + remoteRefs,
          loadRemote: () => this.setState(s2 => ({ remoteLoaded: Object.assign({}, s2.remoteLoaded, { [k]: true }) })),
          remoteBoxStyle: "display:flex;flex-direction:column;gap:9px;background:var(--sub);border:1px solid var(--line);border-radius:12px;padding:" + (touch ? "14px 15px" : "13px 16px"),
          remoteBtnStyle: "font-size:" + (touch ? 14 : 13) + "px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:8px;" + (touch ? "padding:11px 16px;min-height:44px;box-sizing:border-box;display:flex;align-items:center" : "padding:7px 14px"),
          from: m.from, initials: m.initials, when: m.when,
          hasAvatar: !!avatarOf(m.from), noAvatar: !avatarOf(m.from),
          avatarImgStyle: "width:30px;height:30px;flex:0 0 30px;border-radius:50%;background-color:var(--border2);background-position:center;background-size:cover;background-repeat:no-repeat;background-image:url(" + avatarOf(m.from) + ")",
          role: m.mine ? "Ty" : "",
          num: (i + 1) + " / " + msgs.length,
          open, collapsed: !open,
          hasAtts: m.atts.length > 0,
          atts: m.atts.map(x => ({ type: x.type, name: x.name, size: x.size, open: () => (this.tabsMode()
            ? this.openTab("doc:" + x.name, { doc: x, docZoom: 1, docLoading: true }, { att: x })
            : this.openDocLoad({ doc: x, docZoom: 1 })), download: (e) => { if (e && e.stopPropagation) e.stopPropagation(); this.notifyTask({ title: "Downloading file…", body: x.name, cancelText: "Downloading \u201C" + x.name + "\u201D is in progress. Aborting removes the incomplete file.", doneTitle: "File downloaded", doneBody: x.name, ms: 1800 }); } })),
          showDownloadAll: m.atts.length > 1,
          downloadAll: () => this.notifyTask({ title: "Packing attachments…", body: m.atts.length + " files → attachments.zip", cancelText: "The ZIP archive is being prepared. Aborting discards the partially packed file.", doneTitle: "Archive ready", doneBody: "attachments.zip - " + m.atts.length  + " files", ms: 2600 }),
          clipLabel: m.atts.length ? String(m.atts.length) : "",
          clipStyle: "display:flex;align-items:center;gap:3px;color:var(--faint);font-size:" + (touch ? 13 : 11) + "px",
          clipIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;font-size:" + (touch ? 18 : 14) + "px",
          whenStyle: "color:var(--faint);font-size:" + (touch ? 13 : 11) + "px",
          htmlBtnStyle: "display:flex;align-items:center;justify-content:center;border-radius:8px;color:var(--faint);font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;" +
            (touch ? "width:44px;height:44px;font-size:20px;border:1px solid var(--line);" : "width:28px;height:28px;font-size:18px;"),
          showHtmlBtn: !htmlViewMode,
          simpleView: !htmlViewMode,
          htmlView: htmlViewMode,
          htmlFrameId: "fit:" + thread.id + ":" + i,
          htmlEmbedSrc: htmlViewMode ? embedHtml(m.html || htmlFallback(m, thread.subject), "fit:" + thread.id + ":" + i) : "",
          htmlFrameRef: this.htmlFrameRef,
          htmlRich: !!m.html,
          htmlTitle: m.html ? "Show the original message (rich content)" : "Show the original message",
          openHtml: () => {
            const doc = { title: thread.subject, from: m.from, when: m.when, rich: !!m.html, src: m.html || htmlFallback(m, thread.subject) };
            this.setState({ htmlAsk: { key: "html:" + thread.id + ":" + i, doc } });
          },
          preview: (m.paras[0] || {}).text || mdPlain((m.paras[0] || {}).md || ""),
          toggle: () => this.setState(s2 => ({ msgOpen: Object.assign({}, s2.msgOpen, { [k]: !open }), msgFlash: null })),
          wrapStyle: (open
            ? "flex:0 0 auto;display:flex;flex-direction:column;transition:background 140ms ease;" +
              (flash
                ? "border-left:3px solid var(--accent);padding-left:11px;background:var(--accent-soft);"
                : isActive ? "border-left:3px solid var(--accent);padding-left:11px;" : "")
            : "flex:0 0 auto;display:flex;flex-direction:column;border:1px solid " + (flash ? "var(--accent)" : "var(--line)") +
              ";border-radius:12px;background:var(--sub);overflow:hidden;transition:border-color 140ms ease;")
            + "box-sizing:border-box;width:100%;max-width:clamp(680px, 62vw, 1100px);margin-left:auto;margin-right:auto",
          headPadStyle: open
            ? "display:flex;align-items:center;gap:11px;padding:0 0 9px 0"
            : "display:flex;align-items:center;gap:11px;padding:11px 13px",
          bodyPadStyle: "display:flex;flex-direction:column;align-items:stretch;gap:11px;padding:0 0 4px 0;width:100%;text-align:left",
          avatarStyle:
            "width:30px;height:30px;flex:0 0 30px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:11px;letter-spacing:0.02em;" +
            (m.mine ? "background:var(--accent);color:var(--onaccent);" : "background:var(--border2);color:var(--text2);"),
          paras: m.paras.reduce((acc, bb) => bb.md ? acc.concat(mdView(bb.md)) : acc.concat([{
            isText: true,
            runs: [{ text: bb.text, plain: true, isLink: false, href: "", style: "" }],
            style: bb.hl && st.cameFrom
              ? "font-size:16px;line-height:1.55;background:var(--hl);border-left:3px solid var(--hl-line);padding:13px 16px;text-wrap:pretty"
              : MDTXT,
          }]), []),
        });
      });
      const selectionOn = sel.length > 0;
      const aiHints = this.props.aiHints ?? true;
      const ownContacts = CONTACTS.concat(st.extraContacts || []);
      const hiddenCollected = st.hiddenCollected || [];
      const collectedContacts = collectFromMail(ownContacts.map(c => c.name)).filter(c => hiddenCollected.indexOf(c.name) < 0);
      const allContacts = ownContacts.concat(collectedContacts);
      const peopleTab = st.peopleTab || "own";
      const shownContacts = peopleTab === "collected" ? collectedContacts : ownContacts;
      const allTasks = TASKS.concat(st.extraTasks || []).filter(t2 => !hid("tasks", t2.id));
      const openTodayTasks = allTasks.filter(t2 => t2.group === "Today" && (st.taskDone || []).indexOf(t2.id) < 0);
      const openToday = openTodayTasks.length;
      const estToday = openTodayTasks.reduce((a, t2) => a + (parseInt(t2.est, 10) || 30), 0) + " min";
      const convList = st.convs || [];
      const activeConv = convList.find(c => c.id === st.convId) || null;
      const convMsgs = activeConv ? activeConv.msgs : [];
      const activeConvList = convList.filter(c => !c.archived);
      const archivedConvList = convList.filter(c => c.archived);
      const histQuery = (st.historySearch || "").trim().toLowerCase();
      const matchesHistQuery = (c) => {
        if (!histQuery) return true;
        const lastMsg = c.msgs[c.msgs.length - 1];
        return ((c.title || "New conversation") + " " + (lastMsg ? lastMsg.text : "")).toLowerCase().includes(histQuery);
      };
      const convRow = (c, isArchivedList) => {
        const lastMsg = c.msgs[c.msgs.length - 1];
        const isOpen = c.id === (activeConv || {}).id;
        /* On the phone the history covers the conversation, so picking one closes it. */
        const openConv = () => this.setState(isMobile ? { convId: c.id, history: false } : { convId: c.id });
        const rowPress = this.pressFor("convs", c.id, c.title || "New conversation", [
          { icon: "check_box", label: "Select conversations", run: () => this.selStart("convs", c.id) },
          { icon: "forum", label: "Open conversation", run: openConv },
          { icon: isArchivedList ? "unarchive" : "archive", label: isArchivedList ? "Restore from archive" : "Archive", run: () => this.setState(s2 => ({ convs: (s2.convs || []).map(x => x.id === c.id ? Object.assign({}, x, { archived: !isArchivedList }) : x), convId: (!isArchivedList && s2.convId === c.id) ? null : s2.convId })) },
          { icon: "delete", label: "Delete conversation", danger: true, run: () => this.setState({ deleteConvAsk: c.id }) },
        ]);
        const when = c.when || "today";
        const day = when === "now" || when === "today" || /^\d{1,2}:\d{2}$/.test(when) ? "Today" : when === "yesterday" ? "Yesterday" : "Earlier";
        return {
          title: c.title || "New conversation",
          /* The group already says yesterday; the row repeats only what the group does not. */
          when: day === "Yesterday" && !isArchivedList ? "" : when,
          day: day,
          preview: lastMsg ? lastMsg.text : "no messages",
          activate: this.rowTap("convs", c.id, openConv),
          press: rowPress,
          /* One quiet control instead of an archive and a delete glyph on every row: it opens the
             same menu a right-click or a long press does. */
          hasActions: !!c.id,
          openMenu: (e) => { if (e && e.stopPropagation) e.stopPropagation(); rowPress.ctx(e); },
          menuStyle: "flex:0 0 auto;width:" + (touch ? 44 : 24) + "px;height:" + (touch ? 44 : 24) + "px;margin:" + (touch ? "-10px -10px -10px 0" : "-3px -4px -3px 0") + ";display:flex;align-items:center;justify-content:center;border-radius:6px;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1;color:var(--faint)",
          /* A readable list, not a stack of cards: the open one carries a tint and an accent title. */
          style: "display:flex;flex-direction:column;gap:3px;border-radius:10px;padding:" + (isMobile ? "11px 12px" : "8px 10px") + ";" +
            (isOpen || isSel("convs", c.id) ? "background:var(--accent-soft);" : "") +
            (isSel("convs", c.id) ? "box-shadow:inset 0 0 0 1px var(--accent-line);" : ""),
          titleStyle: "flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:" + (isMobile ? 15 : 13) + "px;line-height:1.35;" +
            (isOpen ? "font-weight:600;color:var(--accent-d);" : "font-weight:500;color:var(--text);"),
          whenStyle: "flex:0 0 auto;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-variant-numeric:tabular-nums;white-space:nowrap;color:" + (isOpen ? "var(--accent-d)" : "var(--faint)"),
          previewStyle: "font-size:" + (isMobile ? 13 : 12) + "px;line-height:1.4;color:var(--muted);overflow:hidden;text-overflow:ellipsis;white-space:nowrap",
        };
      };
      const convGroups = (rows) => ["Today", "Yesterday", "Earlier"]
        .map(d => ({ label: d, rows: rows.filter(r => r.day === d) }))
        .filter(g => g.rows.length);
      const person = allContacts.find(c => c.id === st.personId) || allContacts[0];
      const personAi = CONTACT_AI[person.id] || {};
      const evStyle = (kind) => "display:flex;flex-direction:column;gap:3px;border-radius:8px;padding:8px 11px;border:1px solid " + (kind === "deadline"
        ? "var(--hl-line);background:var(--hl);"
        : kind === "mail"
        ? "var(--line);background:var(--sub);"
        : "var(--line);background:var(--panel);box-shadow:0 1px 2px var(--sh-1);");
      const calAllDays = CAL_DAYS.map(d => Object.assign({}, d, { ev: d.ev.concat((st.calAdded || []).filter(x => x.day === d.d)) }));
      const calToday = calAllDays.find(d => d.today) || calAllDays[0];
      const monthCells = [];
      for (let i = 0; i < 5; i++) monthCells.push({ blank: true, n: "", ev: [] });
      for (let n = 1; n <= 31; n++) {
        const src = calAllDays.find(d => parseInt(d.d, 10) === n);
        monthCells.push({ n: String(n), today: n === 27, ev: src ? src.ev.slice(0, 2) : [], blank: false });
      }
      const tabsMode = w >= 1180 && (st.workMode || "tabs") === "tabs";
      const emptyApp = false;
      const filterCount = [!!st.fUnread, !!st.fFlagged, !!st.fAttach, !!st.fRange || !!st.fFrom || !!st.fTo, (st.fSort || "Newest") !== "Newest"].filter(Boolean).length;
      const sideCollapsed = drawer ? false : (st.sideCollapsed !== undefined ? !!st.sideCollapsed : compact);
      const noop = () => {};
      /* A real account carries all six standard folders. The unified view drops the three that only
         make sense per account - Archive, Spam and Trash are always opened in the account they live in. */
      const STD_FOLDERS = (counts, unified) => [
        { icon: "inbox", label: "Inbox" },
        { icon: "send", label: "Sent" },
        { icon: "draft", label: "Drafts" },
        { icon: "archive", label: "Archive", perAccount: true },
        { icon: "report", label: "Spam", perAccount: true },
        { icon: "delete", label: "Trash", perAccount: true },
      ].filter(mb => !(unified && mb.perAccount))
        .map(mb => ({ icon: mb.icon, label: mb.label, count: (counts || {})[mb.label] || "" }));
      const sideW = drawer ? 0 : sideCollapsed ? 58 : Math.max(210, Math.min(420, st.sideW || (wide ? 240 : 210)));
      /* The list's width is the width band's until a mouse drags it: 284 below 1180, 340 on a desktop, 420 from 1920. */
      const listW = Math.max(232, Math.min(st.listW || (compact ? 284 : wide ? 420 : 340), w - railW - sideW - (narrow ? 300 : 420)));
      this._listW = listW;
      const composeTabActive = tabsMode && st.activeTab === "compose" && !!st.compose;
      const docInPane = tabsMode && (st.activeTab || "").indexOf("doc:") === 0 && !!st.doc;
      const htmlInPane = tabsMode && (st.activeTab || "").indexOf("html:") === 0 && !!st.htmlDoc;
      const composeInPane = !isMobile && !!st.compose && (tabsMode ? composeTabActive : true);
      const tabItems = (tabsMode ? (st.tabs || []) : []).map(tb => {
        const isDoc = tb.kind === "doc";
        const isHtml = tb.kind === "html";
        const th = tb.kind === "thread" ? (THREADS.find(x => x.id === tb.id) || {}) : null;
        const active = st.activeTab === tb.key;
        return {
          key: tb.key,
          title: isDoc ? tb.att.name : isHtml ? "HTML · " + (tb.htmlDoc ? tb.htmlDoc.title : "message") : th ? th.subject : "New message",
          icon: isDoc ? "description" : isHtml ? "code" : th ? "mail" : "edit_square",
          activate: () => this.setState(Object.assign({ activeTab: tb.key }, this.tabPatch(tb))),
          close: (e) => { if (e && e.stopPropagation) e.stopPropagation(); this.closeTab(tb.key); },
          style: "display:flex;align-items:center;gap:8px;max-width:230px;padding:8px 11px;border-radius:8px 8px 0 0;font-size:13px;white-space:nowrap;border:1px solid " +
            (active ? "var(--line);border-bottom-color:var(--panel);background:var(--panel);color:var(--text);box-shadow:0 -1px 0 var(--inset) inset;"
                    : "transparent;background:transparent;color:var(--muted);"),
          titleStyle: "overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" + (active ? "font-weight:600;" : ""),
        };
      });

      /* Empty reading panel: in tab mode when no tab is open, and outside it —
         until something has been opened during this visit to mail. */
      const emptyMail = !composeInPane && !docInPane && !htmlInPane &&
        (tabsMode ? tabItems.length === 0 : (!singlePane && !st.threadOpen));
      /* Two panes in less than 1020 px: while a thread is open the list folds to a strip of senders, so the thread
         gets the width; the strip's head widens it again. A width rule — a mouse gets the same strip as a finger. */
      const foldTwoPanes = !singlePane && narrow;
      const listStrip = foldTwoPanes && !emptyMail && !st.listWide && !selectionOn && !(st.stateBarOff);
      /* What is inside the thread pane reads the pane's own width, not the window's: the same window gives the pane
         more or less as the list is folded, dragged, or put away. These are the pane's container breakpoints. */
      const paneW = singlePane ? w - (isMobile ? 0 : railW) : w - railW - sideW - (listStrip ? 76 : listW);
      const paneCompact = paneW < 760;
      const paneNarrow = paneW < 560;
      const stateCards = !isMobile && paneCompact;
      const readPad = (min) => "max(" + min + "px, calc((100% - 880px) / 2))";
      /* Past the column the pane has spare width, and a tinted band across it would run empty on both sides of
         the cards. So there the state stands as a box in the column itself, its cards on the head's edge. */
      const readingColumn = !singlePane && paneW > 924;

      const vals = {
        mo,
        moDialog: isMobile ? "sheet" : "pop",
        moSettings: singlePane ? "sheet" : "pop",
        /* Every sheet a finger can pull down to close shows where to take hold of it. */
        sheetGrabber: isMobile,
        settingsGrabber: singlePane,
        grabberStyle: "flex:0 0 auto;display:flex;justify-content:center;align-items:center;height:20px;background-image:linear-gradient(var(--border2),var(--border2));background-size:38px 4px;background-position:center;background-repeat:no-repeat;",
        moSettingsScrim: singlePane ? "none" : "scrim",
        moCompose: composeInPane ? "fade" : isMobile ? "sheet" : "rise",
        moSide: drawer ? "drawer" : "none",
        moMailList: singlePane ? this.paneMotion("mailPane", "back") : "none",
        moMailDetail: singlePane ? this.paneMotion("mailPane", "push") : "none",
        moPeopleList: isMobile ? this.paneMotion("peoplePane", "back") : "none",
        moPeopleDetail: isMobile ? this.paneMotion("peoplePane", "push") : "none",
        rootRef: this.rootRef,
        themeAttr: theme,
        themeIcon: theme === "dark" ? "light_mode" : "dark_mode",
        themeLabel: theme === "dark" ? "Light" : "Dark",
        toggleTheme: () => this.setState({ themeOverride: theme === "dark" ? "light" : "dark" }),
        themeMenuLabel: theme === "dark" ? "Light theme" : "Dark theme",
        isPhone: isMobile,
        userMenuRef: this.userMenuRef,
        userMenuOpen: !!st.userMenu,
        toggleUserMenu: () => this.setState({ userMenu: !st.userMenu }),
        closeUserMenu: () => { if (st.userMenu) this.setState({ userMenu: false }); },
        meName: myName,
        meInitials: myPhoto ? "" : initialsOf(myName),
        mePhoto: myPhoto,
        meHasPhoto: !!myPhoto,
        onMeName: (e) => this.setState({ myName: e.target.value }),
        photoInputRef: this.photoRef,
        pickPhoto: () => (this.photoRef.current ? this.photoRef.current.click() : null),
        photoError: st.photoError || "",
        onPhotoFile: (e) => {
          const f = e.target.files && e.target.files[0];
          if (!f) return;
          if (f.size > 1048576) { this.setState({ photoError: "Plik ma " + (f.size / 1048576).toFixed(1) + " MB - limit to 1 MB." }); return; }
          const r = new FileReader();
          r.onload = () => this.setState({ myPhoto: String(r.result), photoError: "" });
          r.readAsDataURL(f);
        },
        confirmDelOpen: !!st.confirmDel,
        confirmDelTitle: st.confirmDel ? st.confirmDel.title : "",
        confirmDelText: st.confirmDel ? st.confirmDel.text : "",
        confirmDelLabel: st.confirmDel ? (st.confirmDel.label || "Delete") : "",
        cancelConfirmDel: () => this.setState({ confirmDel: null }),
        runConfirmDel: () => {
          const c = st.confirmDel;
          this.setState({ confirmDel: null });
          if (c && c.run) c.run();
        },
        removePhoto: () => this.askDel({
          title: "Remove the profile photo?",
          text: "We will fall back to initials. The file will have to be uploaded again.",
          label: "Remove photo",
          run: () => this.setState({ myPhoto: "", photoError: "" }),
        }),
        mePreviewStyle: "width:46px;height:46px;flex:0 0 46px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:18px;letter-spacing:0.02em;background:var(--border2);color:var(--text2);" +
          (myPhoto ? "background-image:url(" + myPhoto + ");background-size:cover;background-position:center;" : ""),
        avatarStyle:
          "width:" + (touch ? 44 : 40) + "px;height:" + (touch ? 44 : 40) + "px;border-radius:50%;background:var(--border2);" + (myPhoto ? "background-image:url(" + myPhoto + ");background-size:cover;background-position:center;" : "") + "display:flex;align-items:center;justify-content:center;font-size:14px;color:var(--text2);transition:transform 120ms ease,box-shadow 120ms ease;box-shadow:0 1px 0 var(--inset) inset,0 1px 3px var(--sh-2);" +
          (st.userMenu ? "outline:2px solid var(--accent-line);outline-offset:2px;" : ""),
        railThemeStyle: railBase + "color:var(--muted);",
        notMobile: !isMobile,
        railIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;font-size:" + (isMobile ? 24 : 22) + "px" +
          (isMobile ? ";width:56px;height:32px;border-radius:16px;display:flex;align-items:center;justify-content:center;background:var(--nav-mark,transparent);transition:background 120ms ease" : ""),
        railMoreStyle: railBase + (st.more ? activeStyle : idleColor),
        moreOpen: !!st.more,
        toggleMore: () => this.setState(s2 => ({ more: !s2.more })),
        closeMore: () => this.setState({ more: false }),
        moreItems: [
          { icon: "task_alt", label: "Tasks", go: { screen: "tasks" } },
          { icon: "calendar_month", label: "Calendar", go: { screen: "cal" } },
          { icon: "group", label: "People", go: { screen: "people", peoplePane: "list" } },
        ].map(mi => ({
          icon: mi.icon, label: mi.label,
          run: () => this.setState(Object.assign({ more: false }, mi.go)),
          style: "display:flex;align-items:center;gap:16px;box-sizing:border-box;min-height:52px;padding:0 14px;border-radius:12px;font-size:15px;color:" +
            (mi.go.screen === st.screen ? "var(--accent-d);background:var(--accent-soft);font-weight:600" : "var(--text)"),
        })),
        /* Refresh and Settings stand below the spaces, as the controls the bar has no sixth place for: the phone has no
           floating refresh over the screen, because a control parked over the content hides the row under it. */
        moreRefresh: () => { this.setState({ more: false }); this.refreshAll(); },
        moreSettings: () => this.setState({ more: false, settings: true }),
        moreRowStyle: "display:flex;align-items:center;gap:16px;box-sizing:border-box;min-height:52px;padding:0 14px;border-radius:12px;font-size:15px;color:var(--text)",
        railLabelStart: "Start",
        railLabelMail: "Mail",
        railLabelAgent: "Agent",
        railLabelTasks: "Tasks",
        railLabelCal: "Calendar",
        railLabelPeople: "People",
        railLabelSettings: isMobile ? "Settings" : "Settings",
        railLabelStyle: isMobile
          ? "font-size:12px;line-height:1.1;text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:100%"
          : low ? "width:1px;height:1px;margin:-1px;overflow:hidden;opacity:0;white-space:nowrap" : compact ? "font-size:12px;line-height:1.1;text-align:center" : "font-size:12px",
        rootStyle: FONT + (isMobile ? ";flex-direction:column-reverse" : "") + (FR
          ? ";width:" + FR.w + "px;max-width:100%;height:" + FR.h + "px;max-height:calc(100dvh - 40px);margin:20px auto;transform:translateZ(0);border:1px solid var(--border2);border-radius:" + (isMobile ? 24 : 12) + "px;overflow:hidden;box-shadow:0 20px 50px var(--sh-2)"
          : ""),
        ctxMenuOpen: !!st.ctxMenu,
        ctxMenuTitle: st.ctxMenu ? st.ctxMenu.title : "",
        ctxMenuDetail: (st.ctxMenu && st.ctxMenu.detail) || "",
        ctxMenuHasDetail: !!(st.ctxMenu && st.ctxMenu.detail),
        closeCtxMenu: () => { if (this.ctxArmed()) this.setState({ ctxMenu: null }); },
        ctxMenuItems: (st.ctxMenu ? st.ctxMenu.items : []).map(mi => ({
          icon: mi.icon, label: mi.label,
          run: () => { if (!this.ctxArmed()) return; this.setState({ ctxMenu: null }); if (mi.run) mi.run(); },
          style: "display:flex;align-items:center;gap:11px;border-radius:8px;padding:0 12px;color:" + (mi.danger ? "var(--err-text)" : "var(--text)") +
            (touch ? ";min-height:48px;font-size:15px" : ";min-height:36px;font-size:14px"),
        })),
        ctxMenuStyle: (() => {
          const c = st.ctxMenu || { x: 0, y: 0, items: [] };
          const base = "display:flex;flex-direction:column;gap:2px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:7px 6px 8px;box-shadow:0 20px 46px var(--sh-3);position:fixed;z-index:99;";
          if (isMobile) return base + "left:50%;top:50%;transform:translate(-50%,-50%);width:min(300px,calc(100% - 36px));max-height:calc(100% - 120px);overflow:auto";
          const W = touch ? 262 : 236;
          const H = 46 + (c.items || []).length * (touch ? 50 : 38);
          const bw = st.vw || 1200;
          const bh = st.vh || (typeof window !== "undefined" ? window.innerHeight : 900);
          const x = Math.max(10, Math.min(c.x, bw - W - 10));
          const y = Math.max(10, Math.min(c.y, bh - H - 10));
          return base + "left:" + x + "px;top:" + y + "px;width:" + W + "px";
        })(),
        selBarShown: !!selBarIds.length,
        selClear: () => this.selClearKind(selBarKind),
        selBarCount: selBarIds.length + " selected",
        selBarStyle: "flex:0 0 auto;display:flex;align-items:center;gap:12px;background:var(--accent-soft);border-bottom:1px solid var(--accent-line);" +
          (isMobile ? "padding:9px 10px 9px 14px;" : "padding:8px 14px 8px 18px;"),
        selBarActions: (() => {
          const ids = selBarIds.slice();
          const kind = selBarKind;
          const mk = (icon, label, run, danger) => ({ icon: icon, label: label, run: run, shown: isMobile ? "" : label, danger: !!danger });
          const st2 = (a) => Object.assign({}, a, {
            style: "display:flex;align-items:center;gap:7px;border-radius:8px;border:1px solid var(--line);white-space:nowrap;color:" + (a.danger ? "var(--err-text)" : "var(--text2)") +
              (touch ? ";min-height:44px;padding:0 12px;font-size:13px" : ";min-height:32px;padding:0 10px;font-size:13px"),
          });
          if (kind === "tasks") return [
            mk("check_circle", "Zrobione", () => this.setState(s3 => ({ taskDone: (s3.taskDone || []).concat(ids), selBy: Object.assign({}, s3.selBy || {}, { tasks: [] }) }))),
            mk("calendar_month", "Schedule", () => this.setState(s3 => ({ taskSched: (s3.taskSched || []).concat(ids), selBy: Object.assign({}, s3.selBy || {}, { tasks: [] }) }))),
            mk("delete", "Delete", () => this.askHide("tasks", ids, "Delete tasks?", ids.length + " tasks will disappear from the list."), true),
          ].map(st2);
          if (kind === "notifs") return [
            mk("mark_email_read", "Read", () => this.setNotifsRead(ids, true)),
            mk("mark_email_unread", "Unread", () => this.setNotifsRead(ids, false)),
            mk("delete", "Delete", () => this.askHide("notifs", ids, "Delete notifications?", ids.length + " notifications will disappear from the centre."), true),
          ].map(st2);
          if (kind === "people") return [
            mk("edit", "Write", () => { this.setState({ compose: true, composeMin: false, replyMode: null }); this.selClearKind(kind); }),
            mk("download", "Export", () => { this.notifyTask({ title: "Exporting contacts\u2026", body: ids.length + " contacts", doneTitle: "Contacts exported", ms: 1800 }); this.selClearKind(kind); }),
            mk("delete", "Delete", () => this.askHide("people", ids, "Delete contacts?", ids.length + " contacts will disappear from the list."), true),
          ].map(st2);
          if (kind === "convs") return [
            mk("archive", "Archive", () => this.setState(s3 => ({ convs: (s3.convs || []).map(x => ids.indexOf(x.id) >= 0 ? Object.assign({}, x, { archived: true }) : x), selBy: Object.assign({}, s3.selBy || {}, { convs: [] }) }))),
            mk("delete", "Delete", () => this.askDel({ title: "Delete conversations?", text: ids.length + " conversations will be permanently deleted.", label: "Delete", run: () => this.setState(s3 => ({ convs: (s3.convs || []).filter(x => ids.indexOf(x.id) < 0), convId: ids.indexOf(s3.convId) >= 0 ? null : s3.convId, selBy: Object.assign({}, s3.selBy || {}, { convs: [] }) })) }), true),
          ].map(st2);
          if (kind === "events") return [
            mk("notifications_active", "Reminders", () => { this.setState({ remFor: { key: ids[0], title: ids.length + " events", timed: true, kind: "cal", hint: "before the event time", src: "Calendar" } }); this.selClearKind(kind); }),
            mk("delete", "Delete", () => this.askDel({ title: "Delete events?", text: ids.length + " events will disappear from the calendar.", label: "Delete", run: () => this.setState(s3 => ({ calAdded: (s3.calAdded || []).filter(x => ids.indexOf(x.day + "|" + x.time + "|" + x.t) < 0), selBy: Object.assign({}, s3.selBy || {}, { events: [] }) })) }), true),
          ].map(st2);
          return [];
        })(),
        closeBtnStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;color:var(--muted);border-radius:" + (touch ? "8px;width:44px;height:44px;" : "8px;width:28px;height:28px;"),
        closeBtnAutoStyle: "margin-left:auto;flex:0 0 auto;display:flex;align-items:center;justify-content:center;color:var(--muted);border-radius:" + (touch ? "8px;width:44px;height:44px;" : "8px;width:28px;height:28px;"),
        closeIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;font-size:" + (touch ? 24 : 18) + "px",
        toastCloseStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;border-radius:12px;color:var(--muted);font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;" +
          (touch ? "width:44px;height:44px;font-size:24px;" : "width:30px;height:30px;font-size:20px;"),
        tabToolBtnStyle: "align-self:center;margin:0 10px 0 6px;display:flex;align-items:center;justify-content:center;color:var(--muted);border-radius:8px;" +
          (touch ? "width:44px;height:44px;flex:0 0 44px;" : "width:28px;height:28px;flex:0 0 28px;"),
        tabToolIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;font-size:" + (touch ? 22 : 18) + "px",
        ovZ95: this.ovStyle(95, isMobile),
        ovZ96: this.ovStyle(96, isMobile),
        ovZ97: this.ovStyle(97, isMobile),
        ovZ98: this.ovStyle(98, isMobile),
        ovZ99: this.ovStyle(99, isMobile),
        ovAsk95: this.ovAskStyle(95, isMobile),
        ovAsk97: this.ovAskStyle(97, isMobile),
        ovAsk98: this.ovAskStyle(98, isMobile),
        ovAsk99: this.ovAskStyle(99, isMobile),
        dlgFormWide: isMobile
          ? "width:100%;height:100%;display:flex;flex-direction:column;background:var(--panel);overflow:auto"
          : "width:460px;max-width:100%;max-height:" + dlgMaxH + ";display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 22px 52px var(--sh-3);overflow:auto",
        dlgForm: isMobile
          ? "width:100%;height:100%;display:flex;flex-direction:column;background:var(--panel);overflow:auto"
          : "width:440px;max-width:100%;max-height:" + dlgMaxH + ";display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 22px 52px var(--sh-3);overflow:auto",
        dlgAsk: isMobile
          ? "box-sizing:border-box;width:100%;max-width:344px;max-height:100%;overflow:auto;display:flex;flex-direction:column;gap:13px;background:var(--panel);border:1px solid var(--line);border-radius:16px;padding:19px 18px;box-shadow:0 24px 56px var(--sh-3)"
          : "width:400px;max-width:100%;max-height:" + dlgMaxH + ";overflow:auto;display:flex;flex-direction:column;gap:13px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:18px;box-shadow:0 22px 50px var(--sh-3)",
        remRowStyle: touch ? "display:flex;align-items:center;gap:8px;flex-wrap:wrap" : "display:flex;align-items:center;gap:7px",
        remHintStyle: touch ? "width:100%;font-size:12px;color:var(--faint);line-height:1.4" : "flex:1;min-width:0;font-size:11px;color:var(--faint);text-align:right",
        dlgList: isMobile
          ? "box-sizing:border-box;width:100%;height:100%;display:flex;flex-direction:column;gap:5px;background:var(--panel);padding:16px 14px 22px;overflow:auto"
          : "width:330px;max-width:100%;max-height:" + dlgMaxH + ";overflow:auto;display:flex;flex-direction:column;gap:4px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:14px;box-shadow:0 22px 50px var(--sh-3)",
        railLogoShown: !isMobile && !low,
        railCaptionShown: !low,
        logoStyle: (compact ? "width:30px;height:30px" : "width:40px;height:40px") + ";border-radius:12px;display:block;flex:0 0 auto",
        railStyle: isMobile
          ? "flex:0 0 auto;min-height:70px;box-sizing:border-box;display:flex;flex-direction:row;align-items:stretch;justify-content:space-between;gap:3px;background:var(--rail);border-top:1px solid var(--line);padding:6px 6px calc(8px + env(safe-area-inset-bottom)) 6px;touch-action:none"
          : "width:" + railW + "px;flex:0 0 " + railW + "px;display:flex;flex-direction:column;align-items:center;gap:" + (narrow ? 10 : 14) + "px;overflow:hidden;background:var(--rail);border-right:1px solid var(--line);padding:16px 0",
        /* The nav scrolls on short viewports; the bottom cluster (demo, bell, avatar) stays pinned. */
        railNavStyle: isMobile
          ? "flex:1;min-width:0;display:flex;flex-direction:row;align-items:stretch;justify-content:space-between;gap:3px"
          : "flex:1 1 auto;width:100%;min-height:0;overflow-y:auto;overflow-x:hidden;display:flex;flex-direction:column;align-items:center;gap:" + (narrow ? 2 : 4) + "px",
        isCal: st.screen === "cal" && !emptyApp,
        isTasks: st.screen === "tasks" && !emptyApp,
        goTasks: () => this.goScreen("tasks"),
        railTasksStyle: (isMobile ? "display:none;" : railBase + (st.screen === "tasks" ? activeStyle : idleColor)),
        railNotifStyle: railBase + (st.notifCenter ? activeStyle : idleColor),
        notifDotStyle: "position:absolute;top:-5px;right:-9px;min-width:17px;height:17px;box-sizing:border-box;padding:0 4px;display:flex;align-items:center;justify-content:center;border-radius:8px;background:var(--err);color:#fff;font-size:11px;font-weight:700;border:1.5px solid var(--rail)",
        tbTaskLabel: tbFull ? "New task" : "",
        taskActions: [
          { icon: "search", label: "Find in mail", run: () => this.setState({ screen: "agent" }) },
          { icon: "event", label: "Lay out the day", run: () => this.autoSchedule() },
          { icon: "check_circle", label: "Show done", run: () => this.setState(s2 => ({ showDone: !s2.showDone })) },
          { icon: "download", label: "Export", run: () => {} },
        ].map(a => ({ icon: a.icon, label: tbFull ? a.label : "", run: a.run, style: tbFull ? tbBtn : tbIcon + ";flex:0 0 auto" })),
        tasksBodyStyle: isMobile || compact
          ? "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:16px;padding:14px"
          : "flex:1;min-height:0;display:flex;gap:0",
        tasksMainStyle: isMobile || compact
          ? "display:flex;flex-direction:column;gap:18px"
          : "flex:1;min-width:0;overflow:auto;display:flex;flex-direction:column;gap:20px;padding:20px max(26px, calc((100% - 310px - 1160px) / 2))",
        tasksSideStyle: isMobile || compact
          ? "display:flex;flex-direction:column;gap:10px;background:var(--sub);border:1px solid var(--line);border-radius:12px;padding:14px"
          : "width:310px;flex:0 0 310px;overflow:auto;display:flex;flex-direction:column;gap:10px;background:var(--sub);border-left:1px solid var(--line);padding:18px",
        taskGroups: ["Today", "This week", "Later"].map(g => {
          const items = allTasks.filter(t2 => t2.group === g && (st.showDone || (st.taskDone || []).indexOf(t2.id) < 0));
          return {
            label: g,
            count: items.length,
            items: items.map(t2 => {
              const done = (st.taskDone || []).indexOf(t2.id) >= 0;
              const sched = (st.taskSched || []).indexOf(t2.id) >= 0;
              const remN = this.remList(t2.id).length;
              return {
                t: t2.t, est: t2.est, when: t2.when, src: t2.src, ai: !!t2.ai,
          dueStyle: "font-size:13px;color:var(--text2);white-space:nowrap",
                press: this.pressFor("tasks", t2.id, t2.t, [
                  { icon: "check_box", label: "Select tasks", run: () => this.selStart("tasks", t2.id) },
                  { icon: done ? "radio_button_unchecked" : "check_circle", label: done ? "Mark as not done" : "Mark as done", run: () => this.setState(s3 => ({ taskDone: done ? (s3.taskDone || []).filter(x => x !== t2.id) : (s3.taskDone || []).concat([t2.id]) })) },
                  { icon: "notifications_active", label: "Reminders", run: () => this.setState({ remFor: { key: t2.id, title: t2.t, timed: false, kind: "task", hint: "before 09:00 \u00b7 " + t2.when, src: "Tasks" } }) },
                  { icon: "calendar_month", label: "Schedule in the calendar", run: () => this.setState(s3 => ({ taskSched: (s3.taskSched || []).concat([t2.id]), calAdded: (s3.calAdded || []).concat([{ day: t2.day || "27", time: "11:00", h: 11, t: t2.t, kind: "meeting", src: "zadanie" }]) })) },
                  { icon: "open_in_new", label: "Open source thread", run: () => this.setState({ screen: "mail", threadId: t2.tid || "contoso", mailPane: "thread", threadOpen: true, cameFrom: false }) },
                  { icon: "delete", label: "Delete task", danger: true, run: () => this.askHide("tasks", [t2.id], "Delete task?", t2.t + " will disappear from the task list.") },
                ]),
                tap: this.rowTap("tasks", t2.id, () => {}),
                selected: isSel("tasks", t2.id),
                remCount: remN > 1 ? String(remN) : "",
                remIcon: remN ? "notifications_active" : "notifications",
                remTitle: remN ? "Reminders: " + remN : "Add a reminder",
                remStyle: "display:flex;align-items:center;gap:4px;border-radius:8px;font-weight:600;border:1px solid " +
                  (touch ? "" : "") + (remN ? "var(--accent-line);background:var(--accent-soft);color:var(--accent-d);" : "var(--line);background:var(--sub);color:var(--muted);") +
                  (touch ? "height:44px;padding:0 14px;font-size:12px;" : "height:28px;padding:0 9px;font-size:11px;"),
                remIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;font-size:" + (touch ? 20 : 16) + "px",
                openRem: () => this.setState({ remFor: { key: t2.id, title: t2.t, timed: false, kind: "task", hint: "before 09:00 · " + t2.when, src: "Tasks" } }),
                check: done ? "check" : "",
                toggle: () => this.setState(s2 => ({
                  taskDone: (s2.taskDone || []).indexOf(t2.id) >= 0
                    ? (s2.taskDone || []).filter(x => x !== t2.id)
                    : (s2.taskDone || []).concat([t2.id]),
                })),
                openSrc: () => this.setState({ screen: "mail", threadId: t2.tid || "contoso", mailPane: "thread", threadOpen: true, cameFrom: false }),
                schedule: () => this.setState(s2 => sched ? {} : ({
                  taskSched: (s2.taskSched || []).concat([t2.id]),
                  calAdded: (s2.calAdded || []).concat([{ day: t2.day || "27", time: "11:00", h: 11, t: t2.t, kind: "meeting", src: "task" }]),
                })),
                schedLabel: sched ? "in the calendar" : "Schedule",
                schedStyle: "border-radius:12px;white-space:nowrap;" + (touch ? "font-size:13px;padding:9px 14px;" : "font-size:11px;padding:5px 10px;") + (sched
                  ? "color:var(--muted);background:var(--rail);border:1px solid var(--line);"
                  : "color:var(--onaccent);background:var(--accent);"),
                rowStyle: (touch
                  ? "display:flex;flex-direction:column;align-items:stretch;gap:11px;padding:13px 14px;"
                  : "display:flex;align-items:center;gap:12px;flex-wrap:wrap;padding:11px 14px;") +
                  "background:var(--panel);border:1px solid var(--line);border-radius:12px;" + (done ? "opacity:0.55;" : "") + selMark("tasks", t2.id),
                mainStyle: "display:flex;align-items:" + (touch ? "flex-start" : "center") + ";gap:12px;flex:1;min-width:0",
                metaStyle: touch
                  ? "display:flex;align-items:center;gap:10px;flex-wrap:wrap;padding-left:38px"
                  : "display:flex;align-items:center;gap:10px;flex-wrap:wrap",
                boxStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;border-radius:8px;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;line-height:1;" +
                  (touch ? "width:26px;height:26px;font-size:18px;" : "width:20px;height:20px;font-size:14px;") +
                  "border:1px solid " + (done ? "var(--accent);background:var(--accent);color:var(--onaccent);" : "var(--border2);color:transparent;"),
                titleStyle: "line-height:1.4;text-wrap:pretty;font-size:" + (touch ? 15 : 14) + "px;" + (done ? "text-decoration:line-through;color:var(--muted);" : ""),
              };
            }),
          };
        }),
        taskAgenda: (calAllDays.find(d => d.today) || { ev: [] }).ev.map(e => ({ time: e.time, t: e.t })),
        capacityText: "Today you have " + openToday + " tasks (≈ " + estToday + ") and " + TASK_CAPACITY.meetings + ". " + TASK_CAPACITY.advice,
        autoSchedule: () => this.autoSchedule(),
        newTaskOpen: !!st.newTask,
        openNewTask: () => { this.remSet("new:task", REM_DEFAULT); this.setState({ newTask: true }); },
        tkRem: this.remBlock("new:task", { timed: false, numRef: this.remNumTaskRef, hint: "09:00 on the day it is due" }),
        remModalOpen: !!st.remFor,
        remModalSubtitle: st.remFor ? st.remFor.title + (st.remFor.hint ? " - " + st.remFor.hint : "") : "",
        remModal: this.remBlock(st.remFor ? st.remFor.key : "none", {
          timed: !!(st.remFor && st.remFor.timed),
          numRef: this.remNumModalRef,
          hint: st.remFor ? st.remFor.hint : "",
        }),
        closeRemModal: () => {
          const f = st.remFor;
          if (f) this.remFire(f.title, this.remList(f.key), f.kind === "task" ? "task" : "cal", f.src || "Calendar", { screen: f.kind === "task" ? "tasks" : "cal" });
          this.setState({ remFor: null });
        },
        remModalClear: () => { if (st.remFor) this.remSet(st.remFor.key, []); },
        closeNewTask: () => this.setState({ newTask: false }),
        tkPromptRef: this.tkPromptRef, tkTitleRef: this.tkTitleRef, tkDueRef: this.tkDueRef, tkEstRef: this.tkEstRef,
        tkParse: () => {
          const raw = ((this.tkPromptRef.current || {}).value || "").trim();
          const p = parseEventPrompt(raw);
          const est = (raw.match(/\d{1,3}\s*(?:min|minutes?|h|hrs?|hours?)\b/i) || ["30 min"])[0];
          if (this.tkTitleRef.current) this.tkTitleRef.current.value = p.title;
          if (this.tkDueRef.current) this.tkDueRef.current.value = p.day + ".09";
          if (this.tkEstRef.current) this.tkEstRef.current.value = est;
        },
        saveTask: () => {
          const t2 = (this.tkTitleRef.current || {}).value || "New task";
          const when = (this.tkDueRef.current || {}).value || "no due date";
          const est = (this.tkEstRef.current || {}).value || "30 min";
          const rem = this.remList("new:task");
          const id = "tk" + Date.now();
          this.setState(s2 => ({
            newTask: false,
            remMap: Object.assign({}, s2.remMap || {}, { [id]: rem.slice() }),
            extraTasks: (s2.extraTasks || []).concat([{
              id: id, t: t2, when, day: (when.match(/^(\d{1,2})/) || [])[1] || "28",
              group: when === "today" ? "Today" : "This week", src: "added manually", tid: "contoso", ai: false, est,
            }]),
          }));
          this.notify(rem.length
            ? "Due " + when + " · " + (rem.length === 1 ? remLabel(rem[0], "on the day") : rem.length + " reminders")
            : "Due " + when + " · no reminder", { kind: "success", title: "Task added" });
          this.remFire(t2, rem, "task", "Tasks", { screen: "tasks" });
        },
        isAgent: st.screen === "agent" && !emptyApp,
        goAgent: () => this.goScreen("agent"),
        railAgentStyle: railBase + (st.screen === "agent" ? activeStyle : idleColor),
        /* The hairline after Start and Agent: the spaces that look across everything stand above it, the spaces that hold below. */
        railSepStyle: "flex:0 0 auto;align-self:stretch;height:1px;margin:4px 16px;background:var(--line)",
        agentScrollStyle: isMobile
          ? "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:14px;padding:16px 14px"
          : "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:16px;padding:22px max(26px, calc((100% - 1290px) / 2))",
        /* The field and the line under it keep Fathom's corner free (its disc, its edge and 12 px of air), so the button
           never stands over Send or Cancel; the context, the quote and the suggestions above them run the full width. */
        agentBoxStyle: "display:flex;align-items:center;gap:" + (isMobile ? 8 : 12) + "px;background:var(--panel);border:2px solid var(--accent);border-radius:12px;" + (touch ? "padding:6px 6px 6px 14px" : "padding:10px 13px") + ";margin-right:" + agentCorner + "px",
        agentHintStyle: "font-size:11px;color:var(--faint);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-right:" + agentCorner + "px",
        agentInputWrapStyle: isMobile
          ? "display:flex;flex-direction:column;gap:9px;background:var(--panel);border-top:1px solid var(--line);padding:12px 14px"
          : "display:flex;flex-direction:column;gap:10px;background:var(--panel);border-top:1px solid var(--line);padding:14px max(26px, calc((100% - 1290px) / 2)) 16px",
        agentHint: isMobile ? "Nothing is sent without your approval." : "The agent proposes actions - nothing is sent without your approval.",
        agentThinking: !!st.agentThinking,
        agentScrollRef: this.agentScrollRef,
        onAgentScroll: this.onAgentScroll,
        onAgentWheel: this.onAgentWheel,
        agentNext: this.nextActs(convMsgs, st),
        agentNextShown: this.nextActs(convMsgs, st).length > 0,
        /* What the agent is doing right now — it changes as the work moves on, it is not one fixed line. */
        agentStatus: st.agentStatus || "Going through your mail",
        /* Cancel sits next to Send at all times, greyed out until there is work to stop. */
        agentCancelActive: !!(st.agentThinking || st.demoRun),
        agentCancelDisabled: !(st.agentThinking || st.demoRun),
        agentCancelTab: (st.agentThinking || st.demoRun) ? 0 : -1,
        agentCancelAria: (st.agentThinking || st.demoRun) ? "Stop what the agent is doing" : "Cancel - nothing is running",
        /* Under a finger the field's two buttons reach the 44 px floor, the phone draws them as glyphs so the field keeps
           the width its prompt needs, and no button names a key a touch screen has no keyboard to press. */
        notTouch: !touch,
        agentSendLabel: touch ? "Send" : "Send ⏎",
        agentBtnIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;font-size:22px;line-height:1;display:block",
        agentSendStyle: "font-size:13px;color:var(--onaccent);background:var(--accent);border-radius:8px;white-space:nowrap;box-sizing:border-box;display:flex;align-items:center;justify-content:center;" +
          (isMobile ? "width:44px;height:44px;padding:0;flex:0 0 44px" : touch ? "min-height:44px;padding:0 18px" : "padding:8px 16px"),
        agentCancelStyle: "font-size:13px;border-radius:8px;white-space:nowrap;box-sizing:border-box;display:flex;align-items:center;justify-content:center;" +
          (isMobile ? "width:44px;height:44px;padding:0;flex:0 0 44px;" : touch ? "min-height:44px;padding:0 16px;" : "padding:8px 14px;") + "border:1px solid " +
          ((st.agentThinking || st.demoRun)
            ? "var(--err);color:var(--onaccent);background:var(--err);font-weight:600;"
            : "var(--line);color:var(--faint);background:transparent;"),
        agentCancelHover: (st.agentThinking || st.demoRun)
          ? "cursor:pointer;opacity:0.88"
          : "cursor:default",
        agentCancel: this.agentCancel,
        agentCancelKey: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); this.agentCancel(); } },
        convTitle: activeConv && activeConv.title ? activeConv.title : "Agent",
        convBarShown: tabsMode && convList.length > 1,
        convTabs: convList.map(c => ({
          title: c.title || "New conversation",
          activate: () => this.setState({ convId: c.id }),
          close: (e) => {
            if (e && e.stopPropagation) e.stopPropagation();
            this.setState(s2 => {
              const rest = (s2.convs || []).filter(x => x.id !== c.id);
              return { convs: rest, convId: s2.convId === c.id ? (rest.length ? rest[rest.length - 1].id : null) : s2.convId };
            });
          },
          style: "display:flex;align-items:center;gap:8px;max-width:220px;padding:8px 11px;border-radius:8px 8px 0 0;font-size:13px;white-space:nowrap;border:1px solid " +
            (c.id === (activeConv || {}).id ? "var(--line);border-bottom-color:var(--sub);background:var(--panel);color:var(--text);" : "transparent;background:transparent;color:var(--muted);"),
          titleStyle: "overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" + (c.id === (activeConv || {}).id ? "font-weight:600;" : ""),
        })),
        agentNewConv: () => this.setState(isMobile ? { convId: null, agentInput: "", agentThinking: false, history: false } : { convId: null, agentInput: "", agentThinking: false }),
        historyOpen: st.history == null ? !isMobile : !!st.history,
        historyLabel: isMobile ? "" : "History",
        toggleHistory: () => this.setState(s2 => ({ history: !s2.history })),
        historyPanelW: Math.max(200, Math.min(480, st.historyPanelW || 250)),
        showHistoryResizer: !isMobile,
        startHistoryResize: this.beginHistoryResize,
        resetHistoryResize: () => this.setState({ historyPanelW: 250 }),
        historyCloseShown: isMobile,
        // A drawer over the conversation on the phone, so a press beside it puts it away as its close control does.
        historyScrimShown: isMobile && (st.history == null ? !isMobile : !!st.history),
        historyPanelStyle: isMobile
          ? "position:absolute;top:0;bottom:0;left:0;width:min(320px,86%);box-sizing:border-box;z-index:45;display:flex;flex-direction:column;gap:1px;overflow:auto;background:var(--rail);border-right:1px solid var(--line);box-shadow:0 0 40px var(--sh-3);padding:12px 12px 18px"
          : "width:" + Math.max(200, Math.min(480, st.historyPanelW || 250)) + "px;flex:0 0 " + Math.max(200, Math.min(480, st.historyPanelW || 250)) + "px;display:flex;flex-direction:column;gap:1px;overflow:auto;scrollbar-width:thin;background:var(--rail);border-right:1px solid var(--line);padding:14px 10px",
        convHistoryGroups: convGroups((activeConvList.length ? activeConvList : [{ id: null, title: "New conversation", when: "now", msgs: [] }]).filter(matchesHistQuery).slice().reverse().map(c => convRow(c, false))),
        historyCount: activeConvList.length,
        historyEmpty: !!histQuery && !activeConvList.filter(matchesHistQuery).length,
        historySearchStyle: "flex:0 0 auto;display:flex;align-items:center;gap:8px;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:0 10px;height:" + (isMobile ? 44 : 34) + "px;margin:" + (isMobile ? "4px 0 16px" : "2px 0 12px"),
        historyGroupLabelStyle: "font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--faint);padding:" + (isMobile ? "10px 12px 6px" : "8px 10px 4px"),
        hasArchived: archivedConvList.length > 0,
        archivedCount: archivedConvList.length,
        archiveSectionOpen: !!st.historyArchiveOpen,
        toggleArchiveSection: () => this.setState(s2 => ({ historyArchiveOpen: !s2.historyArchiveOpen })),
        archiveChevron: st.historyArchiveOpen ? "expand_more" : "chevron_right",
        archivedHistory: archivedConvList.filter(matchesHistQuery).slice().reverse().map(c => convRow(c, true)),
        historySearchRef: this.historySearchRef,
        historySearchValue: st.historySearch || "",
        onHistorySearch: (e) => this.setState({ historySearch: e.target.value }),
        clearHistorySearch: () => this.setState({ historySearch: "" }),
        openHistorySearch: () => this.setState({ history: true, historyFocusPending: true }),
        deleteConvAskOpen: !!st.deleteConvAsk,
        cancelDeleteConv: () => this.setState({ deleteConvAsk: null }),
        confirmDeleteConv: () => this.setState(s2 => {
          const rest = (s2.convs || []).filter(x => x.id !== s2.deleteConvAsk);
          return { convs: rest, convId: s2.convId === s2.deleteConvAsk ? null : s2.convId, deleteConvAsk: null };
        }),
        agentShowChips: convMsgs.length < 2,
        /* Each starter chip opens the conversation it belongs to — a finished history, not an empty screen. */
        agentChips: STARTER_CHIPS.map(c => ({
          label: c.label,
          aria: "Open the conversation: " + c.label,
          hasIcon: false, icon: "", iconStyle: "",
          style: "display:inline-flex;align-items:center;gap:6px;font-size:13px;color:var(--text2);background:var(--panel);border:1px solid var(--border2);border-radius:16px;padding:7px 13px;white-space:nowrap",
          run: () => { this.demoStop(); return (st.convs || []).some(x => x.id === c.id)
            ? this.setState({ screen: "agent", convId: c.id, agentInput: "" })
            : this.agentAsk(c.label); },
          key: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); this.demoStop(); if ((st.convs || []).some(x => x.id === c.id)) this.setState({ screen: "agent", convId: c.id, agentInput: "" }); else this.agentAsk(c.label); } },
        })).concat([{
          /* The one chip that plays instead of opening — content arrives live, block by block. */
          label: "Real example",
          aria: "Play the real example conversation in real time",
          hasIcon: true, icon: "play_arrow",
          iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1",
          style: "display:inline-flex;align-items:center;gap:6px;font-size:13px;font-weight:600;color:var(--accent-d);background:var(--accent-soft);border:1px solid var(--accent-line);border-radius:16px;padding:7px 13px 7px 11px;white-space:nowrap",
          run: () => this.playDemo(),
          key: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); this.playDemo(); } },
        }]).concat(MORE_DEMOS.map(dm => ({
          label: dm.label,
          aria: "Play the real example in real time: " + dm.title.replace(/^Real example - /, ""),
          hasIcon: true, icon: "play_arrow",
          iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1",
          style: "display:inline-flex;align-items:center;gap:6px;font-size:13px;font-weight:600;color:var(--accent-d);background:var(--accent-soft);border:1px solid var(--accent-line);border-radius:16px;padding:7px 13px 7px 11px;white-space:nowrap",
          run: () => this.playDemo(dm),
          key: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); this.playDemo(dm); } },
        }))),
        agentInput: st.agentInput || "",
        onAgentSelect: this.onAgentSelect,
        onStartScroll: this.onStartScroll,
        agentPlaceholder: st.agentQuote ? (isMobile ? "Ask about this…" : "Ask about the quoted passage") : isMobile ? "Tell me what to do…" : "Tell me what to do - write, schedule, check",
        agentQuoteShown: !!st.agentQuote,
        agentQuoteText: st.agentQuote ? st.agentQuote.text : "",
        agentQuoteFrom: st.agentQuote ? "From " + st.agentQuote.from : "",
        clearAgentQuote: () => this.setState({ agentQuote: null }),
        agentQuoteX: touch ? "width:44px;height:44px;margin:-12px -10px -12px 0;" : "",
        ...this.selBarView(st, touch, convMsgs),
        onAgentInput: (e) => this.setState({ agentInput: e.target.value }),
        onAgentKey: (e) => { if (e.key === "Enter") this.agentAsk(st.agentInput); },
        agentSend: () => this.agentAsk(st.agentInput),

        agentMsgs: (convMsgs.length ? convMsgs : [{
          role: "bot",
          scope: "3 accounts · mail, calendar",
          text: "Hi. I can write a message, lay out your day or check what is stuck. I always show the result for approval - nothing goes out without you.",
          bullets: [],
          blocks: [],
          ts: Date.now(),
        }]).map((m, mi) => ({
          isUser: m.role === "user",
          isBot: m.role === "bot",
          hasQuote: !!m.quote,
          quote: m.quote || "",
          quoteStyle: "font-size:13px;line-height:1.45;color:var(--text2);border-left:2px solid var(--accent);padding:1px 0 1px 10px;margin-bottom:6px;text-wrap:pretty",
          time: formatMsgTime(m.ts),
          text: m.text,
          hasText: !!m.text,
          md: (m.text ? mdView(m.text) : []).concat((m.paras || []).reduce((acc, p) => acc.concat(mdView(p.t).map(b => Object.assign({}, b, {
            style: b.style + (p.anim ? ";animation:mfmsgin 0.44s cubic-bezier(0.22,0.61,0.36,1) both" : ""),
          }))), [])),
          hasMd: !!m.text || (m.paras || []).length > 0,
          scope: m.scope || "",
          working: !!m.working,
          hasParas: (m.paras || []).length > 0,
          paras: (m.paras || []).map(p => ({
            t: p.t,
            style: "font-size:15px;line-height:1.62;color:var(--text2);text-wrap:pretty" + (p.anim ? ";animation:mfmsgin 0.44s cubic-bezier(0.22,0.61,0.36,1) both" : ""),
          })),
          bullets: (m.bullets || []).map(t2 => ({ t: t2 })),
          hasBlocks: (m.blocks || []).length > 0,
          blocks: (m.blocks || []).map((b, bi) => this.bkView(b, (activeConv ? activeConv.id : "new") + "#" + mi + "#" + bi, isMobile, undefined, touch)),
          blocksStyle: "display:flex;flex-direction:column;gap:" + (isMobile ? 10 : 11) + "px",
          rowStyle: "display:flex;justify-content:" + (m.role === "user" ? "flex-end" : "flex-start"),
          colStyle: "display:flex;flex-direction:column;gap:4px;min-width:0;align-items:" + (m.role === "user" ? "flex-end" : "flex-start") + (m.role === "user" ? "" : ";flex:1"),
          /* Quiet by default: the user turn is a lightly tinted block, the agent turn is plain text
             on the page. The only cards in the thread are the result blocks themselves. */
          bubbleStyle: (m.role === "user"
            ? "max-width:" + (isMobile ? "92%" : "775px") + ";font-size:15px;line-height:1.55;color:var(--text);background:var(--accent-soft);border-radius:12px;padding:11px 14px;text-wrap:pretty"
            : "width:100%;max-width:" + (isMobile ? "100%" : "900px") + ";display:flex;flex-direction:column;gap:11px")
            + (m.anim ? ";animation:mfmsgin 0.44s cubic-bezier(0.22,0.61,0.36,1) both" : ""),
        })),
        isPeople: st.screen === "people" && !emptyApp,
        goCal: () => this.goScreen("cal"),
        goPeople: () => this.goScreen("people", { peoplePane: "list" }),
        railCalStyle: (isMobile ? "display:none;" : railBase + (st.screen === "cal" ? activeStyle : idleColor)),
        railPeopleStyle: (isMobile ? "display:none;" : railBase + (st.screen === "people" ? activeStyle : idleColor)),

        isStart: st.screen === "start" && !emptyApp,
        isMail: st.screen === "mail" && !emptyApp,
        goStart: () => this.goScreen("start"),
        goMail: () => this.goScreen("mail", { cameFrom: false, mailPane: "list" }),
        /* Whether every account is current stands once, beside the profile it belongs to, rather than in a space's header. */
        railSyncStyle: "display:flex;flex-direction:column;align-items:center;gap:4px;font-size:10px;font-weight:500;line-height:1.2;color:var(--muted);text-align:center;border-radius:8px;" + (touch ? "min-height:44px;justify-content:center;" : "padding:3px 2px;"),
        railSyncDot: "width:8px;height:8px;border-radius:50%;background:var(--ok);box-shadow:0 0 0 3px color-mix(in oklch, var(--ok) 22%, transparent)",
        railSyncLabel: "Synced",
        railStartStyle: railBase + (st.screen === "start" ? activeStyle : idleColor),
        railMailStyle: railBase + (st.screen === "mail" ? activeStyle : idleColor),

        headerStyle: isMobile
          ? "display:flex;align-items:center;gap:8px;flex:0 0 auto;box-sizing:border-box;min-height:56px;padding:6px 12px 6px 16px;border-bottom:1px solid var(--line);background:var(--panel)"
          : "display:flex;align-items:center;gap:16px;padding:0 26px;height:56px;flex:0 0 56px;border-bottom:1px solid var(--line);background:var(--panel)",
        askPlaceholder: isMobile ? "Ask or find…" : "Ask or find anything in your mail",
        /* The composer's foot under a finger: one 44 row, Send filled, Attach a symbol alone on the phone,
           and Save draft a text action whose target is the row's height rather than its words. */
        composeSendStyle: touch
          ? "display:inline-flex;align-items:center;justify-content:center;min-height:44px;padding:0 20px;box-sizing:border-box;font-size:14px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:8px"
          : "font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:8px;padding:9px 18px",
        composeAttachStyle: touch
          ? "display:inline-flex;align-items:center;justify-content:center;gap:6px;min-height:44px;min-width:44px;padding:0 " + (isMobile ? 0 : 14) + "px;box-sizing:border-box;font-size:14px;color:var(--text2);border:1px solid var(--border2);border-radius:8px"
          : "font-size:13px;color:var(--text2);border:1px solid var(--border2);border-radius:8px;padding:9px 13px",
        composeAttachIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:" + (touch ? 20 : 18) + "px;line-height:1" + (touch ? "" : ";vertical-align:-3px;margin-right:6px"),
        composeAttachLabel: !isMobile,
        composeSaveStyle: touch
          ? "display:inline-flex;align-items:center;min-height:44px;padding:0 6px;font-size:13px;color:var(--muted)"
          : "font-size:12px;color:var(--muted)",
        composeAiPlaceholder: isMobile ? "Write what this is about…" : "Write what this is about - e.g. “we accept the 2 h SLA but ask for a 5% CPI cap”",
        askLabel: touch ? "Ask" : "Ask ⏎",
        askBtnStyle: "font-size:13px;color:var(--onaccent);background:var(--accent);border-radius:8px;white-space:nowrap;box-sizing:border-box;display:flex;align-items:center;" +
          (touch ? "min-height:44px;padding:0 18px" : "padding:8px 16px"),
        searchBoxStyle: "display:flex;align-items:center;gap:" + (isMobile ? 10 : 12) + "px;border:2px solid var(--accent);border-radius:12px;" + (touch ? "padding:6px 6px 6px 14px" : "padding:11px 14px") + "",
        /* Start's header: the field, its suggestions and what is watched stay put; the band under them (Up next and
           one step each) scrolls with the day and shares their surface, so the header reads as one piece cut off from
           the day below. */
        searchWrapStyle: "flex:0 0 auto;position:relative;z-index:1;overflow:hidden;scrollbar-gutter:stable;background:var(--panel);border-bottom:1px solid var(--line);" + (st.phase === "idle" ? "box-shadow:0 6px 14px -12px var(--sh-3);" : ""),
        searchInnerStyle: "display:flex;flex-direction:column;box-sizing:border-box;" + (isMobile ? "gap:10px;" + framePad(12, 12) : "gap:12px;" + framePad(16, 16)),
        idleStyle: "flex:1;display:flex;flex-direction:column;overflow:auto;scrollbar-gutter:stable;min-height:0",
        startBodyStyle: "box-sizing:border-box;" + (isMobile
          ? "display:flex;flex-direction:column;gap:22px;" + framePad(20, 28)
          : narrow
          ? "display:flex;flex-direction:column;gap:22px;" + framePad(24, 32)
          : startWide
          ? "display:grid;grid-template-columns:minmax(0,1fr) " + asideX + " " + asideY + ";gap:28px;align-items:start;" + framePad(26, 40)
          : "display:flex;gap:28px;" + framePad(26, 40)),
        idleTitleStyle: isMobile
          ? "font-size:24px;font-weight:600;letter-spacing:-0.015em;text-wrap:pretty"
          : "font-size:28px;font-weight:600;letter-spacing:-0.015em",
        askChipStyle: "display:inline-flex;align-items:center;gap:6px;font-size:13px;color:var(--text2);background:var(--panel);border:1px solid var(--line);border-radius:999px;white-space:nowrap;box-sizing:border-box;" + (touch ? "min-height:36px;padding:0 13px 0 10px" : "padding:5px 12px 5px 9px"),
        accountsStyle: isMobile
          ? "display:flex;flex-direction:column;gap:10px"
          : "width:" + (compact ? 250 : 290) + "px;flex:0 0 " + (compact ? 250 : 290) + "px;display:flex;flex-direction:column;gap:10px",
        citeHint: (touch ? "tap" : "click") + " a citation to see the evidence",
        answerHintStyle: isMobile
          ? "display:none"
          : "margin-left:auto;font-size:12px;color:var(--muted)",

        resultStyle: isMobile
          ? "flex:1;display:flex;flex-direction:column;min-height:0;overflow:auto"
          : "flex:1;display:flex;min-height:0",
        resultMainStyle: isMobile
          ? "display:flex;flex-direction:column;gap:12px;padding:14px;min-width:0"
          : "flex:1;display:flex;flex-direction:column;gap:14px;padding:20px 24px;overflow:auto;min-width:0",
        timelineRowStyle: isMobile ? "display:flex;flex-direction:column;gap:0;padding-top:2px" : "display:flex;gap:0;overflow-x:auto;padding-top:2px",
        /* Start's answer and steps wear the same chrome as the Agent's (`BLK`). */
        blk: {
          card: BLK.card(isMobile, "settled"),
          pendingCard: BLK.card(isMobile, "pending"),
          head: BLK.head,
          tile: BLK.tile("settled"),
          accentTile: BLK.tile("accent"),
          glyph: BLK.glyph,
          title: BLK.title,
          meta: BLK.meta,
          label: BLK.label,
          pendingChip: BLK.chip("pending"),
          chipGlyph: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:14px;line-height:1",
          pendingFoot: BLK.foot(isMobile, "pending"),
          primaryBtn: BLK.btn(isMobile, true) + (touch ? "min-height:44px;" : ""),
          quietBtn: BLK.btn(isMobile, false) + (touch ? "min-height:44px;" : ""),
          propTitle: "font-size:" + (isMobile ? 15 : 16) + "px;font-weight:600;line-height:1.35;letter-spacing:-0.01em;text-wrap:pretty",
          answerText: "font-size:" + (isMobile ? 16 : 17) + "px;line-height:1.6;color:var(--text);text-wrap:pretty",
          tlDate: BLK.tlDate,
          tlTitle: BLK.tlTitle,
          tlDetail: BLK.tlDetail,
        },
        tableGridStyle: "display:grid;grid-template-columns:1.3fr 1fr 0.8fr 1.4fr;gap:0" + (isMobile ? ";min-width:540px" : ""),
        galleryGridStyle: isMobile
          ? "display:grid;grid-template-columns:repeat(2,1fr);gap:10px"
          : "display:grid;grid-template-columns:repeat(4,1fr);gap:10px",

        inspectorPanelStyle: isMobile
          ? (evObj
              ? "position:fixed;inset:0;z-index:40;display:flex;flex-direction:column;background:var(--sub)"
              : "display:flex;flex-direction:column;background:var(--rail);border-top:1px solid var(--line)")
          : "width:" + inspW + "px;flex:0 0 " + inspW + "px;display:flex;flex-direction:column;background:var(--rail);border-left:1px solid var(--line);min-height:0",
        evWrapStyle: isMobile
          ? "display:flex;flex-direction:column"
          : "display:flex;flex-direction:column;min-height:0;flex:1",
        evListStyle: isMobile
          ? "display:flex;flex-direction:column;gap:9px;padding:13px 14px"
          : "flex:1;display:flex;flex-direction:column;gap:9px;padding:14px 18px;overflow:auto;min-height:0",

        query: st.query,
        onQuery: (e) => this.setState({ query: e.target.value }),
        onKey: (e) => { if (e.key === "Enter" && (st.query || "").trim()) this.run(st.query); },
        submit: () => { if ((st.query || "").trim()) this.run(st.query); },
        continueInAgent: () => {
          const q = st.query || "";
          /* The hand-off carries the answer on screen, not just the question. */
          this.setState(s2 => Object.assign({ convId: null }, this.pushMsg(Object.assign({}, s2, { convId: null }), [
            { role: "user", text: q },
            { role: "bot", scope: "continuing from Start", text: "I have the context of that result: " + q + ". Here is the one step I would take next.", bullets: [], blocks: [{
              type: "action", meta: "one step", phase: "pending",
              title: "Draft a reply from this result",
              reason: "The result already carries the numbers and the sources the reply needs.",
              effect: "Writes a draft into this conversation - you approve it before anything is sent.",
              cta: "Write it", done: "Draft prepared",
            }] },
          ])));
          this.setState({ screen: "agent" });
        },
        reset: () => { this.timers.forEach(clearTimeout); this.setState({ phase: "idle", query: "", planKey: null, ready: {}, ev: null }); },
        todoGridStyle: "display:flex;flex-direction:column;gap:10px",
        homeRowStyle: "display:flex;flex-direction:column;gap:12px",
        homeCardStyle: "flex:1;min-width:0;display:flex;flex-direction:column;gap:9px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:15px 17px",
        todayItems: HOME.todayItems.filter(t2 => (st.todayHidden || []).indexOf(t2.id) < 0).slice(0, isMobile ? 2 : 3).map(t2 => ({
          title: t2.title, due: t2.due, why: t2.why, src: t2.src, rule: t2.rule,
          open: () => this.setState({ screen: "mail", threadId: t2.tid, mailPane: "thread", threadOpen: true, cameFrom: false }),
          /* Dismissing is a judgement about the item, so it is remembered and can be taken back. */
          dismiss: (e) => {
            if (e && e.stopPropagation) e.stopPropagation();
            this.setState(s2 => ({ todayHidden: (s2.todayHidden || []).concat([t2.id]) }));
            this.notify("“" + t2.title + "” will not be raised again today.", { title: "Marked not important", actionLabel: "Undo", onAction: () => { this.hideToast(); this.setState(s2 => ({ todayHidden: (s2.todayHidden || []).filter(x => x !== t2.id) })); } });
          },
          dismissAria: "Not important: " + t2.title,
          ruleStyle: "display:inline-flex;align-items:center;gap:5px;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:500;color:var(--muted);background:var(--sub);border-radius:999px;padding:2px 8px 2px 6px;white-space:nowrap",
          style: "display:flex;flex-direction:column;gap:6px;background:var(--panel);border:1px solid " + (t2.urgent ? "var(--hl-line)" : "var(--line)") + ";border-radius:12px;padding:13px 15px",
          dueStyle: "margin-left:auto;font-size:11px;white-space:nowrap;border-radius:12px;padding:3px 8px;" + (t2.urgent
            ? "background:var(--hl);color:var(--text);"
            : "background:var(--rail);color:var(--muted);border:1px solid var(--line);"),
        })),
        homeAgenda: HOME.agenda.map(a => ({
          when: a.when, t: a.t,
          dotStyle: "width:8px;height:8px;flex:0 0 8px;border-radius:50%;background:" + (a.kind === "deadline" ? "var(--hl-line)" : "var(--accent)"),
        })),
        /* Text the template shows as written — see sample-text.js. */
        sample: SAMPLE_TEXT,
        briefingText: HOME.briefing,
        briefingStatsStyle: isMobile
          ? "display:grid;grid-template-columns:1fr 1fr;gap:12px 16px;border-top:1px solid var(--line2);padding-top:11px"
          : "display:flex;gap:16px;flex-wrap:wrap;border-top:1px solid var(--line2);padding-top:11px",
        briefingStats: HOME.briefingStats,
        quickActions: [
          { icon: "event", label: "Plan the day", run: () => this.agentAsk("Plan my day") },
          { icon: "history", label: "What slipped?", run: () => this.agentAsk("What slipped this week?") },
          { icon: "auto_awesome", label: "Ask the agent", run: () => this.setState({ screen: "agent" }) },
        ].map(a => ({
          icon: a.icon, label: a.label, run: a.run,
          style: "display:flex;align-items:center;gap:9px;font-size:13px;color:var(--text2);background:var(--panel);border:1px solid var(--border2);border-radius:8px;padding:9px 14px;white-space:nowrap",
        })),
        goMail: () => this.goScreen("mail", { mailPane: "list" }),
        digest: HOME.digest.map(d => ({
          n: d.n, label: d.label, note: d.note,
          open: () => this.setState({ screen: "mail", threadId: d.tid, mailPane: "thread", threadOpen: true, cameFrom: false }),
          countStyle: "flex:0 0 30px;height:30px;display:flex;align-items:center;justify-content:center;border-radius:8px;font-size:14px;font-weight:600;background:" +
            (d.n > 3 ? "var(--accent-soft);color:var(--accent-d);" : "var(--rail);color:var(--text2);border:1px solid var(--line);"),
        })),
        /* Start's day. Everything here is read off Mail, Calendar, Tasks and People by rules, and every item opens
           where it lives; the model writes only what the reader asks it to - the briefing and a meeting's prep. */
        dayCounts: HOME.dayCounts.map(c => {
          const open = c.key === "cal" ? () => this.goScreen("cal")
            : c.key === "tasks" ? () => this.goScreen("tasks")
            : c.key === "waiting" ? () => this.goScreen("mail", { mailPane: "list", threadOpen: false, cameFrom: false, resultFilter: { q: "", why: "waiting on your reply", ids: HOME.waiting.map(w => w.tid) } })
            : () => this.goScreen("mail", { mailPane: "list", cameFrom: false });
          return { n: c.n, label: c.label, icon: c.icon, open, aria: c.n + " " + c.label + " - open",
            style: "display:inline-flex;align-items:center;gap:7px;font-size:13px;color:var(--text2);background:var(--panel);border:1px solid var(--line);border-radius:999px;white-space:nowrap;box-sizing:border-box;" + (touch ? "min-height:36px;padding:0 13px 0 10px;" : "padding:5px 12px 5px 9px;") };
        }),
        dayCountsStyle: "display:flex;gap:8px;" + (isMobile ? "flex-wrap:nowrap;overflow-x:auto;scrollbar-width:none;margin:0 -14px;padding:0 14px" : "flex-wrap:wrap"),
        /* From 1680 px Start takes a third column, so a large screen holds the people waiting and what needs the reader
           beside the day instead of below it; under that the two stand in one aside. */
        dayMainStyle: isMobile || narrow
          ? "display:flex;flex-direction:column;gap:22px;min-width:0"
          : "flex:1;display:flex;flex-direction:column;gap:24px;min-width:0",
        dayAsideStyle: isMobile || narrow
          ? "display:flex;flex-direction:column;gap:22px;min-width:0"
          : startWide
          ? "display:contents"
          : "width:" + asideX + ";flex:0 0 " + asideX + ";display:flex;flex-direction:column;gap:22px",
        sectionHeadStyle: "display:flex;align-items:baseline;gap:9px",
        sectionLabelStyle: "font-family:'Geist',system-ui,sans-serif;font-size:11px;color:var(--muted);font-weight:600;white-space:nowrap",

        /* Up next: the facts stand by default; the prep is written by the model only on request. */
        nextUp: HOME.nextUp,
        nextPeople: HOME.nextUp.people.map(pp => ({ initials: pp.initials, name: pp.name })),
        nextFacts: HOME.nextUp.facts.map(f => ({ text: f })),
        prepIdle: !st.startPrep,
        prepBusy: st.startPrep === "busy",
        prepReady: st.startPrep === "ready",
        prepLines: HOME.nextUp.prep.map(t2 => ({ text: t2 })),
        prepSkel: [92, 78, 64].map(w => ({ style: SHIM + "height:10px;border-radius:5px;width:" + w + "%" })),
        runPrep: () => {
          this.setState({ startPrep: "busy" });
          this.timers.push(setTimeout(() => this.setState({ startPrep: "ready" }), Math.max(600, (this.props.streamDelay ?? 550) * 1.6)));
        },
        dropPrep: () => this.setState({ startPrep: null }),
        openNextCal: () => this.goScreen("cal"),
        openNextThread: () => this.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: HOME.nextUp.tid, cameFrom: false }),
        prepareWithAgent: () => this.goAgentWith("meeting “" + HOME.nextUp.title + "”"),
        /* Up next and one step each: one segment, two columns of one height whose heads, card tops and card bottoms
           meet on the same lines; under two panes' width they stack. */
        pairStyle: (isMobile || narrow || compact) ? "display:flex;flex-direction:column;gap:22px;min-width:0" : "display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:28px;align-items:stretch;min-width:0",
        pairColStyle: "display:flex;flex-direction:column;gap:10px;min-width:0",
        pairHeadStyle: "display:flex;align-items:center;gap:9px;min-width:0;box-sizing:border-box;min-height:" + (touch ? 44 : 30) + "px",
        nextCardStyle: "flex:1;box-sizing:border-box;display:flex;flex-direction:column;gap:12px;background:var(--panel);border:1px solid var(--accent-line);border-radius:14px;padding:" + (isMobile ? "14px" : "16px 18px") + ";box-shadow:0 1px 2px var(--sh-1),inset 0 1px 0 var(--inset)",
        nextActsStyle: "margin-top:auto;display:flex;gap:8px;flex-wrap:wrap;align-items:center;border-top:1px solid var(--line2);padding-top:12px",
        nextBtn: BLK.btn(isMobile, false) + "width:auto;flex:0 0 auto;" + (touch ? "min-height:44px;" : ""),
        nextBtnPrimary: BLK.btn(isMobile, true) + "width:auto;flex:0 0 auto;" + (touch ? "min-height:44px;" : ""),
        avatarStyle: "display:inline-flex;align-items:center;justify-content:center;width:26px;height:26px;flex:0 0 26px;border-radius:50%;font-size:10.5px;font-weight:600;color:var(--text2);background:var(--rail);border:1px solid var(--line)",

        /* The day as it runs: the calendar and the tasks on one line, with a mark for now. */
        dayPlan: HOME.dayPlan.map(d => {
          const isNow = d.kind === "now";
          const booked = d.proposed && (st.bkPhase || {})["start#" + d.step] === "accepted";
          const movedTo = ((st.bkVals || {})["start#" + d.step] || {}).When;
          if (booked) d = Object.assign({}, d, { proposed: false, note: movedTo ? "booked from Start for " + movedTo : "booked from Start" });
          const past = d.state === "past";
          const dot = d.kind === "meeting" ? "var(--accent)" : d.kind === "focus" ? "transparent" : d.kind === "task" ? "var(--hl-line)" : "var(--accent)";
          return {
            time: d.time, title: d.title, sub: [d.end ? "until " + d.end : "", d.note || ""].filter(Boolean).join(" · "), isNow, isItem: !isNow,
            open: d.go === "cal" ? () => this.goScreen("cal") : d.go === "tasks" ? () => this.goScreen("tasks") : null,
            rowStyle: "display:flex;gap:12px;align-items:flex-start;border-radius:10px;padding:" + (touch ? "10px 10px" : "8px 10px") + ";" + (touch ? "min-height:44px;box-sizing:border-box;" : "") +
              (d.state === "next" ? "background:var(--accent-soft);" : "") + (past ? "opacity:0.55;" : ""),
            timeStyle: "flex:0 0 46px;font-family:'Geist',system-ui,sans-serif;font-size:12px;font-variant-numeric:tabular-nums;color:" + (d.state === "next" ? "var(--accent-d)" : "var(--muted)") + ";padding-top:2px",
            dotStyle: "width:9px;height:9px;flex:0 0 9px;margin-top:5px;border-radius:" + (d.kind === "task" ? "3px" : "50%") + ";background:" + dot + ";" + (d.proposed ? "border:1.5px dashed var(--accent);box-sizing:border-box;" : ""),
            titleStyle: "font-size:14px;line-height:1.35;color:var(--text);text-wrap:pretty;" + (d.state === "next" ? "font-weight:600;" : "") + (past ? "text-decoration:line-through;text-decoration-color:var(--faint);" : "") + (d.proposed ? "font-style:italic;color:var(--text2);" : ""),
          };
        }),
        dayNowStyle: "display:flex;align-items:center;gap:8px;padding:2px 10px;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--accent-d)",
        dayCardStyle: "display:flex;flex-direction:column;gap:2px;background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:8px",

        /* Who waits on the reader, led by what they asked for rather than by who they are: a ledger of debts, each with
           how long it has waited (the meter) and a reply one press away, longest first. */
        waiting: HOME.waiting.map(w => {
          const old = w.days >= 2;
          return {
            ask: w.ask, who: w.name + " · " + w.org + (w.asked > 1 ? " · asked " + (w.asked === 2 ? "twice" : w.asked + " times") : ""),
            since: w.since, due: w.due, hasDue: !!w.due, name: w.name,
            open: () => this.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: w.tid, cameFrom: false }),
            reply: (e) => { if (e && e.stopPropagation) e.stopPropagation(); this.goAgentWith("a reply to " + w.name + " · " + w.ask.toLowerCase()); },
            replyAria: "Draft a reply to " + w.name,
            rowStyle: "display:flex;flex-direction:column;gap:7px;border-radius:10px;padding:" + (touch ? "12px 10px" : "11px 10px") + ";" + (touch ? "min-height:44px;box-sizing:border-box;" : ""),
            meter: [0, 1, 2].map(k => ({ style: "width:14px;height:5px;border-radius:3px;background:" + (k <= w.days ? (old ? "var(--warn)" : "var(--accent)") : "var(--line)") })),
            sinceStyle: "font-size:11.5px;font-variant-numeric:tabular-nums;white-space:nowrap;color:" + (old ? "var(--warn-text);font-weight:600" : "var(--muted)"),
            dueStyle: "font-size:11px;font-weight:600;white-space:nowrap;border-radius:999px;padding:2px 8px;background:var(--warn-soft);color:var(--warn-text)",
          };
        }),
        waitingCardStyle: "display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:6px",
        waitingSepStyle: "height:1px;background:var(--line2);margin:0 10px",
        waitReplyStyle: "display:inline-flex;align-items:center;gap:6px;font-size:12.5px;font-weight:600;color:var(--accent-d);white-space:nowrap;border-radius:8px;" + (touch ? "min-height:44px;padding:0 6px;" : "padding:3px 6px;"),

        /* One step each: the Agent's proposal blocks with Start's controls - pressed, and undone from the toast. They
           stand as a deck, one at a time: a step taken or declined stays where it is with its result and Undo, and
           leaves the deck when the reader moves on. */
        startSteps: stepDeck.map((i, k) => {
          const v = this.bkView(HOME.steps[i], "start#" + i, isMobile, "start", touch);
          const away = k !== stepAt;
          return Object.assign({}, v, {
            away: away ? "true" : undefined,
            slotStyle: "grid-area:1 / 1;display:flex;flex-direction:column;min-width:0;" + (away ? "visibility:hidden;pointer-events:none;" : ""),
            cardStyle: v.cardStyle + ";flex:1;box-sizing:border-box",
            footStyle: (v.footStyle || "") + ";margin-top:auto",
          });
        }),
        stepStackStyle: "flex:1;display:grid;min-width:0",
        stepDeckAny: stepDeck.length > 0,
        stepDeckEmpty: stepDeck.length === 0,
        stepDeckCount: stepDeck.length ? (stepAt + 1) + " of " + stepDeck.length : "",
        stepDeckNote: stepDeck.length ? (stepAt + 1) + " of " + stepDeck.length : "",
        stepDeckMany: stepDeck.length > 1,
        stepPrev: () => moveStep(-1),
        stepNext: () => moveStep(1),
        stepPrevStyle: stepNav(stepAt > 0),
        stepNextStyle: stepNav(stepAt < stepDeck.length - 1),
        stepDots: stepDeck.map((i, k) => ({ style: "height:4px;border-radius:2px;transition:width 160ms ease;background:" + (k === stepAt ? "var(--accent);width:14px" : "var(--border2);width:6px") })),

        /* Under the answer, the messages it came from - the find half of *Ask or find*. */
        foundRows: plan && plan.sources ? plan.sources.map(id => THREADS.find(t2 => t2.id === id)).filter(Boolean).map(t2 => ({
          from: t2.from, subject: t2.subject, time: t2.time,
          open: () => this.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: t2.id, cameFrom: true, fromCite: null }),
          rowStyle: "display:flex;align-items:center;gap:12px;border-top:1px solid var(--line2);padding:" + (touch ? "12px 2px" : "9px 2px") + ";" + (touch ? "min-height:44px;box-sizing:border-box;" : ""),
        })) : [],
        showAskChips: st.phase === "idle",

        /* The counters are counts; the written briefing is the model's and stays off until it is turned on. */
        todayCounts: HOME.todayCounts,
        todayEmpty: HOME.todayItems.every(t2 => (st.todayHidden || []).indexOf(t2.id) >= 0),
        /* Today: the counts, and the written briefing as their extension. Folded, the card is the counts alone; unfolded,
           the same counts head it and the briefing (or the offer to turn it on) reads on beneath them. */
        todayOpen: !!st.todayOpen,
        todayToggle: () => this.setState(s2 => ({ todayOpen: !s2.todayOpen })),
        todayToggleLabel: st.todayOpen ? (isMobile ? "Less" : "Counts only") : st.briefOn ? (isMobile ? "Briefing" : "Read the briefing") : (isMobile ? "Briefing · off" : "Written briefing · off"),
        todayToggleIcon: st.todayOpen ? "unfold_less" : "unfold_more",
        todayCardStyle: "display:flex;flex-direction:column;gap:12px;box-sizing:border-box;background:var(--panel);border:1px solid var(--line);border-radius:14px;" +
          (st.todayOpen ? "box-shadow:inset 3px 0 0 var(--accent);" : "") + (isMobile ? "padding:12px 13px 13px 14px" : "padding:13px 16px 14px 17px"),
        todayToggleStyle: "margin-left:auto;display:inline-flex;align-items:center;gap:6px;font-size:12.5px;font-weight:600;color:var(--accent-d);white-space:nowrap;border-radius:8px;" + (touch ? "min-height:44px;padding:0 8px;" : "padding:4px 8px;"),
        briefOn: !!st.briefOn && !!st.todayOpen,
        briefOffer: !st.briefOn && !!st.todayOpen,
        briefOfferText: HOME.briefingOffer,
        turnBriefOn: () => { this.setState({ briefOn: true, todayOpen: true }); this.notify("It appears here each morning, under the counts.", { kind: "success", title: "Written briefing on" }); },
        hideBriefOffer: () => this.setState({ todayOpen: false }),
        turnBriefOff: () => { this.setState({ briefOn: false, todayOpen: false }); this.notify("Unfold the counts to turn it back on.", { title: "Written briefing off" }); },
        briefNotUseful: () => this.notify("Thanks - tomorrow's briefing weighs that.", { title: "Marked not useful" }),

        hasWatched: (st.watched || []).length > 0,
        /* Watching sits under the field it came from: one strip of the questions kept, each a card that says whether
           its answer moved, scrolling sideways when there are more than fit. */
        watchBandStyle: "display:flex;flex-direction:column;gap:8px;min-width:0;border-top:1px solid var(--line2);padding-top:" + (isMobile ? 10 : 12) + "px",
        watchHeadStyle: "display:flex;align-items:center;gap:8px;min-width:0;min-height:" + (touch ? 44 : 30) + "px",
        watchEyeStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:17px;line-height:1;color:var(--accent-d)",
        watchCount: String((st.watched || []).length),
        watchCountStyle: "font-size:11px;font-weight:600;font-variant-numeric:tabular-nums;color:var(--muted);background:var(--sub);border-radius:999px;padding:1px 7px",
        watchChangedAny: (st.watched || []).some(w => w.state === "changed"),
        watchChangedText: (() => { const n = (st.watched || []).filter(w => w.state === "changed").length; return n === 1 ? "1 answer moved" : n + " answers moved"; })(),
        watchChangedStyle: "display:inline-flex;align-items:center;gap:5px;font-size:11.5px;font-weight:600;color:var(--accent-d);white-space:nowrap",
        /* Folded by default: the head alone, and what moved named in it, so news is seen without the strip being open. */
        watchOpen: !!st.watchOpen && watchList.length > 0,
        watchShut: !st.watchOpen && watchList.length > 0,
        watchPaging: !!st.watchOpen && watchList.length > 0,
        watchToggle: () => this.setState(s2 => ({ watchOpen: !s2.watchOpen })),
        watchToggleLabel: st.watchOpen ? (isMobile ? "" : "Hide") : (isMobile ? "" : "Show all"),
        watchToggleIcon: st.watchOpen ? "unfold_less" : "unfold_more",
        watchToggleAria: st.watchOpen ? "Hide the watched questions" : "Show all watched questions",
        watchToggleStyle: "flex:0 0 auto;display:inline-flex;align-items:center;justify-content:center;gap:6px;font-size:12.5px;font-weight:600;color:var(--accent-d);white-space:nowrap;border-radius:8px;box-sizing:border-box;" + (touch ? "min-height:44px;min-width:44px;padding:0 8px;" : "padding:4px 8px;"),
        watchMoved: isMobile ? [] : watchList.filter(w => w.state === "changed").slice(0, startWide ? 3 : 2).map(w => ({ q: w.q, open: () => this.openWatched(w) })),
        watchMovedRowStyle: "flex:1;min-width:0;display:flex;align-items:center;gap:8px;overflow:hidden",
        watchMovedStyle: "flex:0 1 auto;min-width:0;max-width:340px;display:inline-flex;align-items:center;gap:7px;font-size:12.5px;font-weight:500;color:var(--text);background:var(--accent-soft);border:1px solid var(--accent-line);border-radius:999px;box-sizing:border-box;" + (touch ? "min-height:36px;padding:0 12px 0 10px;" : "padding:4px 11px 4px 9px;"),
        watchMovedDotStyle: "width:6px;height:6px;flex:0 0 6px;border-radius:50%;background:var(--accent)",
        watchQuiet: !isMobile && !watchList.some(w => w.state === "changed"),
        watchQuietText: "nothing moved since you last looked",
        watchHint: isMobile ? "" : "refreshed when new mail matches · each refresh counts against your allowance",
        watchHintStyle: "flex:1;min-width:0;text-align:right;font-size:11px;color:var(--faint);min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap",
        /* A carousel of fixed cards: the same count per page at each width, each card the same size whatever its question,
           and the arrows in the head, so the strip never changes shape as questions come and go. On two panes the cards
           take the day's columns - the two halves of the main one, then each aside - so their edges continue its lines. */
        watchRowStyle: "display:grid;min-width:0;" + (isMobile ? "grid-template-columns:minmax(0,1fr)"
          : narrow ? "grid-template-columns:repeat(2,minmax(0,1fr));gap:12px"
          : compact ? "grid-template-columns:minmax(0,1fr) " + asideX + ";gap:28px"
          : "grid-template-columns:minmax(0,1fr) minmax(0,1fr) " + asideX + (startWide ? " " + asideY : "") + ";gap:28px"),
        watchPageText: watchList.length <= watchPer ? (watchList.length === 1 ? "1 question" : watchList.length + " questions")
          : (watchAt * watchPer + 1) + (watchPer > 1 ? "–" + Math.min(watchList.length, (watchAt + 1) * watchPer) : "") + " of " + watchList.length,
        watchPageStyle: "font-size:11px;color:var(--faint);font-variant-numeric:tabular-nums;white-space:nowrap;margin-right:2px",
        watchPrev: () => this.setState({ watchAt: Math.max(0, watchAt - 1) }),
        watchNext: () => this.setState({ watchAt: Math.min(watchPages - 1, watchAt + 1) }),
        watchPrevOff: watchAt === 0 ? "true" : undefined,
        watchNextOff: watchAt >= watchPages - 1 ? "true" : undefined,
        watchPrevStyle: stepNav(watchAt > 0),
        watchNextStyle: stepNav(watchAt < watchPages - 1),
        watchQStyle: "flex:1;min-width:0;font-size:13.5px;font-weight:500;line-height:1.4;color:var(--text);display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;text-wrap:pretty",
        watchStopStyle: "display:inline-flex;align-items:center;justify-content:center;flex:0 0 auto;border-radius:8px;color:var(--faint);font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;font-size:18px;" +
          (touch ? "width:44px;height:44px;margin:-10px -8px 0 0;" : "width:28px;height:28px;margin:-4px -4px 0 0;"),
        watchEmptyStyle: "font-size:13px;line-height:1.45;color:var(--muted);border:1px dashed var(--border2);border-radius:11px;padding:10px 13px;text-wrap:pretty",
        noWatched: !(st.watched || []).length,
        watchedPage: watchList.slice(watchAt * watchPer, (watchAt + 1) * watchPer).map(w => ({
          q: w.q, note: w.note,
          open: () => this.openWatched(w),
          stop: (e) => {
            if (e && e.stopPropagation) e.stopPropagation();
            this.setState(s2 => ({ watched: (s2.watched || []).filter(x => x.id !== w.id) }));
            this.notify("“" + w.q + "” no longer refreshes.", { title: "Stopped watching" });
          },
          stopAria: "Stop watching: " + w.q,
          isChanged: w.state === "changed", isPaused: w.state === "paused", isFailed: w.state === "failed",
          /* A card whose answer moved carries the accent edge, so it is found before it is read. */
          rowStyle: "min-width:0;height:" + (touch ? 92 : 78) + "px;display:flex;flex-direction:column;justify-content:space-between;gap:6px;box-sizing:border-box;background:var(--bg);border:1px solid " + (w.state === "changed" ? "var(--accent-line)" : "var(--line)") + ";border-radius:12px;padding:" + (touch ? "12px 12px 11px 14px" : "10px 10px 9px 13px") + (w.state === "changed" ? ";box-shadow:inset 3px 0 0 var(--accent)" : ""),
          stateStyle: "display:flex;align-items:center;gap:6px;min-width:0;font-size:12px;line-height:1.4;" + (w.state === "changed" ? "color:var(--accent-d);font-weight:600;"
            : w.state === "paused" || w.state === "failed" ? "color:var(--warn-text);" : "color:var(--muted);"),
          dotStyle: "width:7px;height:7px;flex:0 0 7px;border-radius:50%;background:" + (w.state === "changed" ? "var(--accent)" : w.state === "paused" || w.state === "failed" ? "var(--warn)" : "var(--border2)"),
        })),
        suggestions: SUGGESTED_QUESTIONS.map(x => ({ text: x.text, pick: () => this.run(x.text) })),

        isWatching: !!(st.watched || []).find(w => w.q === st.query),
        watchLabel: (st.watched || []).find(w => w.q === st.query) ? "Watching" : "Watch",
        watchIcon: (st.watched || []).find(w => w.q === st.query) ? "notifications_active" : "notification_add",
        toggleWatch: () => {
          const has = (st.watched || []).find(w => w.q === st.query);
          if (has) { this.setState(s2 => ({ watched: (s2.watched || []).filter(w => w.q !== st.query) })); this.notify("It no longer refreshes on your start screen.", { title: "Stopped watching" }); return; }
          this.setState(s2 => ({ watched: [{ id: "w" + Date.now(), q: st.query, state: "same", note: "watching from now" }].concat(s2.watched || []) }));
          this.notify("It stays on your start screen and refreshes when new mail matches it. Each refresh counts against your allowance.", { kind: "success", title: "Watching this question" });
        },
        watchStyle: "display:inline-flex;align-items:center;gap:6px;font-size:13px;border-radius:999px;box-sizing:border-box;white-space:nowrap;" + (touch ? "min-height:44px;padding:0 14px 0 11px;" : "padding:5px 12px 5px 9px;") +
          ((st.watched || []).find(w => w.q === st.query) ? "color:var(--accent-d);background:var(--accent-soft);border:1px solid var(--accent-line);" : "color:var(--text2);background:transparent;border:1px solid var(--border2);"),


        /* The result opens as the mail it was built from. */
        sourcesLabel: plan && plan.sources ? "Show the " + plan.sources.length + " messages in Mail" : "",
        openSources: () => { if (plan && plan.sources) this.goScreen("mail", { mailPane: "list", threadOpen: false, cameFrom: false, resultFilter: { q: st.query, ids: plan.sources.slice() } }); },
        answerFootStyle: "display:flex;align-items:center;gap:8px;flex-wrap:wrap;border-top:1px solid var(--line2);padding-top:11px",
        answerLinkStyle: BLK.link(isMobile) + (touch ? "min-height:44px;" : ""),

        /* On a phone the two header actions shrink to a glyph and a word, so the title keeps its line. */
        agentBtnLabel: isMobile ? "Agent" : "Continue with the agent",
        newQLabel: isMobile ? "Today" : "Back to your day",
        headBtnPad: touch ? "min-height:44px;box-sizing:border-box;padding:0 13px 0 11px;" : "padding:7px 14px 7px 11px;",
        /* Touch lifts every control on these surfaces to the 44 px target, glyph-only ones included. */
        tgt: touch ? "display:inline-flex;align-items:center;justify-content:center;min-height:44px;min-width:44px;box-sizing:border-box;" : "",
        tgtX: touch ? "width:44px;height:44px;flex:0 0 44px;" : "",

        showIdle: st.phase === "idle",
        hasRun,
        running: st.phase === "running",
        progressText: plan ? (isMobile ? "Searching your mail…" : "Searching your mail in 3 accounts…") : "",
        runFinished: hasRun && st.phase === "done",
        runMeta: plan ? plan.meta : "",
        confidence: plan ? plan.confidence : "",
        confidenceChipStyle: "display:flex;align-items:center;font-size:11px;border-radius:12px;padding:3px 9px;" + (plan && plan.confidence === "high confidence"
          ? "background:var(--ok-soft);color:var(--ok-text)"
          : plan && plan.confidence === "low confidence" ? "background:var(--warn-soft);color:var(--warn-text)" : "background:var(--rail);color:var(--muted)"),
        /* Every confidence chip explains its band (base.css `[data-tip]`). */
        confidenceTip: plan ? ({
          "high confidence": "The messages say this outright, and the answer restates what they say.",
          "moderate confidence": "The messages carry this, with a step of inference you may want to check.",
          "low confidence": "This is the best reading of partial sources, and it may be wrong.",
        })[plan.confidence] || "" : "",
        gapNote: plan && plan.gap ? plan.gap : false,
        answerReady: !!ready.answer,
        answerParts: plan ? plan.parts.map(p => {
          const disputed = !!(st.reported || {})[p.cite];
          return {
            text: p.text, cite: p.cite, open: openEv(p.cite), disputed,
            title: disputed ? "You reported this claim" : "Open source " + p.cite,
            textStyle: disputed ? "text-decoration:underline wavy var(--warn);text-underline-offset:4px" : "",
            supStyle: "font-size:11px;border-radius:4px;padding:2px 6px;margin-left:3px;" + (disputed ? "color:var(--warn-text);background:var(--warn-soft);" : "color:var(--accent-d);background:var(--accent-soft);"),
          };
        }) : [],

        evidenceCount: plan ? plan.evidence.length + " sources · " + (touch ? "tap" : "click") + " to see the passage" : "",
        evidence: plan && ready.evidence ? plan.evidence.map(e => { const sb = srcBadge(e); return { n: e.n, title: e.title, snippet: e.snippet, meta: e.meta, open: () => this.setState({ ev: e.n }), badgeStyle: SRC_BADGE_STYLE, badgeIconStyle: SRC_BADGE_ICON_STYLE, badgeIcon: sb.icon, badgeLabel: sb.label, badgeTitle: sb.title }; }) : [],
        inspectorOpen: !!evObj,
        inspectorClosed: !evObj,
        closeInspector: () => this.setState({ ev: null }),
        inspectorIndex: evObj && plan ? "Evidence " + evObj.n + " of " + plan.evidence[plan.evidence.length - 1].n : "",
        inspectorTitle: evObj ? evObj.title : "",
        inspectorMeta: evObj ? evObj.meta : "",
        inspectorBadgeStyle: SRC_BADGE_STYLE,
        inspectorBadgeIconStyle: SRC_BADGE_ICON_STYLE,
        inspectorBadgeIcon: evObj ? srcBadge(evObj).icon : "",
        inspectorBadgeLabel: evObj ? srcBadge(evObj).label : "",
        inspectorBadgeTitle: evObj ? srcBadge(evObj).title : "",
        inspectorPre: evObj ? evObj.pre : "",
        inspectorQuote: evObj ? evObj.quote : "",
        inspectorPost: evObj ? evObj.post : "",
        inspectorSupports: evObj ? evObj.supports : "",
        inspectorMail: evObj ? evObj.mail : "",
        /* A report is about the one claim this fragment supports, and changes nothing else. */
        inspectorReported: !!(evObj && (st.reported || {})[evObj.n]),
        inspectorNotReported: !(evObj && (st.reported || {})[evObj.n]),
        reportClaim: () => {
          if (!evObj) return;
          this.setState(s2 => ({ reported: Object.assign({}, s2.reported, { [evObj.n]: true }) }));
          this.notify("The claim it supports is marked as disputed in this answer. Nothing else changed.", { title: "Reported" });
        },
        openInMail: () => this.setState({ screen: "mail", threadId: "contoso", cameFrom: true, fromCite: st.ev, mailAnswer: false, mailPane: "thread", threadOpen: true }),

        /* The Agent's evidence inspector (`agentEvOpen`): beside the conversation as a 380 px column from 1180 px up
           (`agentEvBeside`), over it as the phone's full sheet below (`agentEvOver`). */
        agentEvOpen: !!agentEvObj,
        agentEvBeside: !!agentEvObj && !isMobile && !compact,
        agentEvOver: !!agentEvObj && (isMobile || compact),
        agentEvPanelStyle: !isMobile && !compact
          ? "width:380px;flex:0 0 380px;display:flex;flex-direction:column;min-height:0;border-left:1px solid var(--line);background:var(--sub)"
          : "position:fixed;inset:0;z-index:40;display:flex;flex-direction:column;background:var(--sub)",
        agentEvIndex: agentEvObj ? "Source " + agentEvObj.n : "",
        agentEvTitle: agentEvObj ? agentEvObj.source : "",
        agentEvBadge: agentEvObj ? agentEvObj.badge : "",
        agentEvMeta: agentEvObj ? agentEvObj.freshness : "",
        agentEvQuote: agentEvObj ? agentEvObj.snippet : "",
        closeAgentEv: this.closeAgentEv,
        openAgentEvMail: () => { this.setState({ agentEv: null }); this.goLook({ kind: "thread", tid: "contoso" }); },

        sideExpanded: !sideCollapsed,
        sideCollapsed,
        toggleSide: drawer
          ? (() => this.setState({ sideDrawer: false }))
          : (() => this.setState(s2 => ({ sideCollapsed: !s2.sideCollapsed }))),
        /* The panel's show/hide control leads the mail toolbar, kept apart from *New message* by a
           rule, so a long folder tree never scrolls it away. The drawer closes from its own head. */
        sideToggleIcon: sideCollapsed ? "left_panel_open" : "left_panel_close",
        sideToggleTitle: sideCollapsed ? "Show folders and filters" : "Hide folders and filters",
        sideDrawerHead: drawer,
        sideToggleShown: !drawer && st.screen === "mail",
        sideScrollStyle: "flex:1;min-height:0;overflow-y:auto;overflow-x:hidden;scrollbar-width:thin;display:flex;flex-direction:column;gap:4px;" + (drawer ? "padding:4px 14px 16px" : sideCollapsed ? "padding:12px 9px" : "padding:14px 14px 12px"),
        sideToggleBtnStyle: "flex:0 0 auto;width:" + (touch ? 44 : 32) + "px;height:" + (touch ? 44 : 32) + "px;display:flex;align-items:center;justify-content:center;border-radius:8px;color:var(--muted);font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:20px;line-height:1",
        showSide: !drawer || drawerOpen,
        sideScrim: drawer && drawerOpen,
        closeDrawer: () => this.setState({ sideDrawer: false }),
        showFolderBtn: drawer,
        listSearchRef: this.listSearchRef,
        listSearchActive: !!st.listSearchOn,
        listSearchIdle: !st.listSearchOn,
        listSearchValue: st.listSearch || "",
        onListSearch: (e) => this.setState({ listSearch: e.target.value }),
        openListSearch: () => this.setState({ listSearchOn: true, listSearchFocusPending: true }),
        /* One line at every width: the narrow list panes get the short prompt, and the field itself opens the search,
           so a touch composition drops the magnifier beside it rather than spending 44 px on a second way in. */
        listSearchPlaceholder: touch ? "Search mail…" : "Search or describe what you need…",
        listHeadStyle: "display:flex;align-items:center;gap:" + (touch ? 8 : 9) + "px;padding:" + (touch ? "8px 12px" : "10px 12px") + ";border-bottom:1px solid var(--line)",
        listIconBtnStyle: "flex:0 0 auto;width:34px;height:34px;display:flex;align-items:center;justify-content:center;border-radius:8px;color:var(--muted)",
        toggleFilters: () => this.setState(s2 => ({ filtersOpen: !s2.filtersOpen })),
        filtersOpen: !!st.filtersOpen,
        filtersActive: filterCount > 0,
        filtersCount: filterCount,
        filtersIcon: filterCount > 0 ? "filter_alt" : "tune",
        filtersTitle: filterCount > 0 ? "Filters (" + filterCount + ") - click to show" : "Filters",
        filtersBtnStyle: "flex:0 0 auto;min-width:" + (touch ? 44 : 34) + "px;height:" + (touch ? 44 : 34) + "px;" + (touch ? "box-sizing:border-box;" : "") + "display:flex;align-items:center;justify-content:center;gap:3px;padding:0 7px;border-radius:8px;" +
          (filterCount > 0 ? "color:var(--accent-d);background:var(--accent-soft);" : "color:var(--muted);"),
        filtersSummary: filterCount > 0 ? "Active filters: " + filterCount : "No active filters",
        clearFilters: () => this.setState({ fUnread: false, fFlagged: false, fAttach: false, fSort: "Newest", fRange: "", fFrom: "", fTo: "" }),
        filterToggles: [
          { key: "fUnread", label: "Unread", icon: "mark_email_unread" },
          { key: "fFlagged", label: "Flagged", icon: "flag" },
          { key: "fAttach", label: "With attachments", icon: "attach_file" },
        ].map(f => {
          const on = !!st[f.key];
          return {
            label: f.label, icon: f.icon,
            toggle: () => this.setState(s2 => ({ [f.key]: !s2[f.key] })),
            iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1",
            style: "display:flex;align-items:center;gap:6px;font-size:13px;border-radius:16px;padding:6px 11px;border:1px solid " +
              (on ? "var(--accent);background:var(--accent-soft);color:var(--accent-d);font-weight:600;" : "var(--line);background:var(--panel);color:var(--text2);"),
          };
        }),
        sortOptions: ["Newest", "Oldest", "Sender", "Subject"].map(s => ({
          label: s,
          pick: () => this.setState({ fSort: s }),
          style: "font-size:13px;border-radius:16px;padding:6px 11px;border:1px solid " +
            ((st.fSort || "Newest") === s ? "var(--accent);background:var(--accent-soft);color:var(--accent-d);font-weight:600;" : "var(--line);background:var(--panel);color:var(--text2);"),
        })),
        rangeOptions: ["Today", "Last 7 days", "Last 30 days", "This year"].map(r => ({
          label: r,
          pick: () => this.setState(s2 => ({ fRange: s2.fRange === r ? "" : r, fFrom: "", fTo: "" })),
          style: "font-size:13px;border-radius:16px;padding:6px 11px;border:1px solid " +
            ((st.fRange || "") === r ? "var(--accent);background:var(--accent-soft);color:var(--accent-d);font-weight:600;" : "var(--line);background:var(--panel);color:var(--text2);"),
        })),
        filterFrom: st.fFrom || "",
        filterTo: st.fTo || "",
        onFilterFrom: (e) => this.setState({ fFrom: e.target.value, fRange: "" }),
        onFilterTo: (e) => this.setState({ fTo: e.target.value, fRange: "" }),
        searchFieldStyle: "flex:1;min-width:0;display:flex;align-items:center;color:var(--faint);background:var(--rail);border:1px solid var(--line);border-radius:24px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" + (touch
          ? "box-sizing:border-box;min-height:44px;padding:0 16px;font-size:14px;"
          : "padding:9px 13px;font-size:13px;"),
        openFolders: () => this.setState({ sideDrawer: true }),
        folderBtnStyle: "flex:0 0 auto;width:" + (touch ? 44 : 36) + "px;height:" + (touch ? 44 : 36) + "px;box-sizing:border-box;display:flex;align-items:center;justify-content:center;border-radius:" + (touch ? 12 : 8) + "px;color:var(--text2);background:var(--rail);border:1px solid var(--line)",
        sideStyle: drawer
          ? "position:fixed;top:0;bottom:0;left:0;width:262px;z-index:80;display:flex;flex-direction:column;min-height:0;background:var(--panel);border-right:1px solid var(--line);box-shadow:0 0 40px var(--sh-3)"
          : sideCollapsed
          ? "width:58px;flex:0 0 58px;display:flex;flex-direction:column;min-height:0;background:var(--sub);border-right:1px solid var(--line)"
          : "width:" + sideW + "px;flex:0 0 " + sideW + "px;display:flex;flex-direction:column;min-height:0;background:var(--sub);border-right:1px solid var(--line)",
        showSideResizer: !drawer && !sideCollapsed && !touch,
        startSideResize: this.beginSideResize,
        resetSideResize: () => this.setState({ sideW: 210 }),
        filtersWrapStyle: sideCollapsed
          ? "flex:0 0 auto;display:flex;flex-direction:column;gap:3px;border-bottom:1px solid var(--line);padding-bottom:10px;margin-bottom:8px"
          : "flex:0 0 auto;display:flex;flex-direction:column;gap:2px;border-bottom:1px solid var(--line);padding-bottom:10px;margin-bottom:8px",
        sideCollapsedView: sideCollapsed,
        aiFiltersOpen: st.aiFiltersOpen !== false,
        aiFiltersChevron: st.aiFiltersOpen !== false ? "expand_more" : "chevron_right",
        aiFiltersTitle: st.aiFiltersOpen !== false ? "Collapse AI filters" : "Show AI filters",
        toggleAiFilters: () => this.setState(s2 => ({ aiFiltersOpen: s2.aiFiltersOpen === false })),
        sideGroups: [{ key: "all", label: UNIFIED_ACCOUNTS.label, title: UNIFIED_ACCOUNTS.title, dot: UNIFIED_ACCOUNTS.dot, folders: STD_FOLDERS(UNIFIED_ACCOUNTS.counts, true) }]
          .concat(ACCOUNTS.map(a => ({ key: a.key, label: a.label, title: a.address, dot: a.dot, folders: STD_FOLDERS(a.counts) })))
          .map(g => {
          const open = (st.accOpen || {})[g.key] !== false;
          const press = g.key === "all" ? null : this.pressFor("account", g.key, g.label, this.accountMenu(g.key, g.label), undefined, g.title);
          return {
            label: g.label, title: press ? g.title : null, open, chevron: open ? "expand_more" : "chevron_right",
            showHeader: !sideCollapsed, showDot: sideCollapsed,
            ctxOpen: press ? press.ctx : noop, pressDown: press ? press.down : noop, pressUp: press ? press.up : noop,
            toggle: () => this.setState(s2 => ({ accOpen: Object.assign({}, s2.accOpen, { [g.key]: !((s2.accOpen || {})[g.key] !== false) }) })),
            wrapStyle: sideCollapsed ? "display:flex;flex-direction:column;gap:3px;padding-bottom:7px" : "display:flex;flex-direction:column;gap:2px;padding-bottom:9px",
            headStyle: "display:flex;align-items:center;gap:8px;font-family:'Geist',system-ui,sans-serif;font-size:11px;color:var(--muted);padding:5px 9px;border-radius:6px;font-weight:600",
            dotStyle: "width:8px;height:8px;flex:0 0 8px;border-radius:50%;background:" + g.dot,
            folders: (() => {
              const extra = (st.extraFolders || {})[g.key] || [];
              const fOpen = st.folderOpen || {};
              const kidsOf = (p) => extra.filter(x => x.indexOf(p + "/") === 0 && x.indexOf("/", p.length + 1) < 0);
              const rows = [];
              const walk = (path, icon, count, depth, custom) => {
                const kids = kidsOf(path);
                const open = !!fOpen[g.key + "·" + path];
                rows.push({ path, icon, count, depth, custom, kids: kids.length, open });
                if (kids.length && open) kids.forEach(k => walk(k, "folder", "", depth + 1, true));
              };
              g.folders.forEach(f => walk(f.label, f.icon, f.count, 0, false));
              extra.filter(x => x.indexOf("/") < 0 && !g.folders.some(f => f.label === x)).forEach(x => walk(x, "folder", "", 0, true));
              return sideCollapsed ? rows.filter(r => r.depth === 0) : rows;
            })().map(node => {
              const label = node.path.split("/").pop();
              const fk = g.key + "·" + node.path;
              const active = (st.folderKey || "acc1·Inbox") === fk;
              const dragOver = st.dragOverKey === fk;
              const canNest = node.path.split("/").length < this.MAX_DEPTH;
              const said = g.title + " - " + node.path.split("/").join(" / ") + (node.count ? " · " + node.count : "");
              const fpress = g.key === "all" ? null : this.pressFor("folder", fk, label, this.folderMenu(g.key, g.label, node.path, canNest, node.custom), undefined, said);
              return {
              ctxOpen: fpress ? fpress.ctx : noop, pressDown: fpress ? fpress.down : noop, pressUp: fpress ? fpress.up : noop,
              icon: node.icon, label: label, count: node.count, expanded: !sideCollapsed,
              title: fpress ? said : null,
              pick: () => this.pickFolder(fk, label),
              dragOver: (e) => this.dragOverFolder(fk, e),
              dragLeave: () => this.dragLeaveFolder(fk),
              drop: (e) => this.dropOnFolder(label, g.label, e),
              twisty: node.open ? "expand_more" : "chevron_right",
              hasKids: node.kids > 0 && !sideCollapsed,
              twistyTitle: node.open ? "Collapse subfolders" : "Show subfolders",
              twistyStyle: "flex:0 0 18px;width:18px;height:18px;display:flex;align-items:center;justify-content:center;border-radius:6px;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1;color:var(--faint)",
              toggleKids: (e) => { if (e && e.stopPropagation) e.stopPropagation(); this.setState(s2 => ({ folderOpen: Object.assign({}, s2.folderOpen, { [fk]: !((s2.folderOpen || {})[fk]) }) })); },
              style: (sideCollapsed
                ? "display:flex;align-items:center;justify-content:center;height:34px;border-radius:8px;font-size:14px;"
                : "display:flex;align-items:center;gap:10px;font-size:14px;padding:7px 11px 7px " + (11 + node.depth * 15) + "px;border-radius:8px;") +
                (active ? "background:var(--accent-soft);color:var(--accent-d);font-weight:600;" : "") +
                (dragOver ? "background:var(--accent-soft);color:var(--accent-d);box-shadow:inset 0 0 0 2px var(--accent);" : ""),
              iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;text-align:center;" + (sideCollapsed ? "font-size:24px;width:24px;" : "font-size:18px;width:20px;flex:0 0 20px;") + (active ? "" : "color:var(--muted);"),            countStyle: active ? "font-size:13px" : "font-size:13px;color:var(--faint)",
            };
            }),
          };
        }),

        /* Loading skeletons — folder list and message window. */
        listLoading: !!st.listLoading,
        listReady: !st.listLoading,
        listSkelStyle: "display:flex;flex-direction:column;gap:1px;padding:1px 0",
        listSkelRows: [74, 58, 68, 50, 71, 61, 66, 54].slice(0, isMobile ? 7 : 8).map(w => ({
          rowStyle: "display:flex;align-items:center;gap:10px;background:var(--panel);padding:" + (isMobile ? "15px 14px" : "12px 12px"),
          avStyle: SHIM + "flex:0 0 " + (isMobile ? 34 : 22) + "px;width:" + (isMobile ? 34 : 22) + "px;height:" + (isMobile ? 34 : 22) + "px;border-radius:50%",
          colStyle: "flex:1;min-width:0;display:flex;flex-direction:column;gap:8px",
          l1Style: SHIM + "height:9px;width:" + Math.max(28, w - 26) + "%",
          l2Style: SHIM + "height:9px;width:" + w + "%",
        })),
        msgLoading: !!st.msgLoading,
        msgSkelStyle: "position:absolute;inset:0;z-index:14;display:flex;flex-direction:column;gap:14px;background:var(--panel);padding:" + (singlePane ? "16px 16px" : "20px 24px"),
        msgSkelSubjStyle: SHIM + "height:15px;width:" + (singlePane ? 74 : 52) + "%;border-radius:6px",
        msgSkelMetaStyle: SHIM + "height:10px;width:" + (singlePane ? 56 : 34) + "%",
        msgSkelHeadStyle: "display:flex;align-items:center;gap:11px;border-top:1px solid var(--line2);padding-top:16px",
        msgSkelAvStyle: SHIM + "flex:0 0 30px;width:30px;height:30px;border-radius:50%",
        msgSkelBodyStyle: "display:flex;flex-direction:column;gap:11px;padding-top:4px",
        msgSkelLines: [97, 92, 99, 68, 0, 95, 88, 44].map(w => ({
          style: w ? SHIM + "height:10px;width:" + w + "%" : "height:6px",
        })),
        aiFilters: [
          { icon: "pending_actions", label: "Needs a decision" },
          { icon: "handshake", label: "Commitments" },
          { icon: "schedule", label: "Deadlines this week" },
        ].map(fl => ({
          icon: fl.icon, label: fl.label, expanded: !sideCollapsed, title: "AI filter: " + fl.label,
          style: sideCollapsed
            ? "display:flex;align-items:center;justify-content:center;height:34px;border-radius:8px;color:var(--muted)"
            : "display:flex;align-items:center;gap:10px;font-size:14px;color:var(--text2);padding:7px 11px;border-radius:8px",
          iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;text-align:center;color:var(--muted);" + (sideCollapsed ? "font-size:24px;width:24px;" : "font-size:18px;width:20px;"),
        })),


        tbEventLabel: tbFull ? "New event" : "",
        tbContactLabel: tbFull ? "New contact" : "",
        calActions: [
          { icon: "today", label: "Today" },
          { icon: "event_available", label: "Find a slot" },
          { icon: "mail", label: "Invitation" },
          { icon: "download", label: "Export" },
          { icon: "print", label: "Print" },
        ].map(a => ({ icon: a.icon, title: a.label, label: tbFull ? a.label : "", style: tbFull ? tbBtn : tbIcon + ";flex:0 0 auto" })),
        peopleActions: [
          { icon: "edit", label: "Write" },
          { icon: "event", label: "Schedule a meeting" },
          { icon: "merge", label: "Merge duplicates" },
          { icon: "upload", label: "Import" },
          { icon: "download", label: "Export" },
        ].map(a => ({ icon: a.icon, title: a.label, label: tbFull ? a.label : "", style: tbFull ? tbBtn : tbIcon + ";flex:0 0 auto" })),
        fieldStyle: "flex:1;min-width:0;font-family:inherit;font-size:14px;color:var(--text);background:var(--sub);border:1px solid var(--line);border-radius:8px;padding:10px 12px;outline:none",
        newEventOpen: !!st.newEvent,
        openNewEvent: () => { this.remSet("new:event", REM_DEFAULT); this.setState({ newEvent: true, evParsed: null, evAllDay: false }); },
        evNotesRef: this.evNotesRef, evEndDayRef: this.evEndDayRef, evEndTimeRef: this.evEndTimeRef,
        evAllDay: !!st.evAllDay,
        evTimeStyle: st.evAllDay
          ? "display:none"
          : "flex:0 0 96px;width:96px;box-sizing:border-box;font-family:inherit;font-size:14px;color:var(--text);background:var(--sub);border:1px solid var(--line);border-radius:8px;padding:10px 12px;outline:none",
        evToggleAllDay: () => this.setState(s2 => ({ evAllDay: !s2.evAllDay })),
        evAllDayBox: st.evAllDay
          ? "width:18px;height:18px;flex:0 0 18px;border-radius:6px;background:var(--accent);color:var(--onaccent);display:flex;align-items:center;justify-content:center;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;font-size:16px;line-height:1"
          : "width:18px;height:18px;flex:0 0 18px;box-sizing:border-box;border-radius:6px;border:1.5px solid var(--border2);background:var(--panel)",
        evAllDayMark: st.evAllDay ? "check" : "",
        // Side by side for days alone, stacked once each carries a time - a day and a time do not fit twice in one row.
        evSpanRow: st.evAllDay ? "display:flex;gap:10px" : "display:flex;flex-direction:column;gap:13px",
        evLengthLabel: st.evAllDay ? "1 day" : "1 hour",
        evRem: this.remBlock("new:event", { timed: true, numRef: this.remNumEventRef, hint: "before the event time" }),
        closeNewEvent: () => this.setState({ newEvent: false }),
        evTitleRef: this.evTitleRef, evDayRef: this.evDayRef, evTimeRef: this.evTimeRef, evPromptRef: this.evPromptRef,
        promptFieldStyle: "font-family:inherit;font-size:14px;color:var(--text);background:var(--panel);border:2px solid var(--accent);border-radius:8px;padding:10px 12px;outline:none",
        evParsedShown: !!st.evParsed,
        evParsedChips: (st.evParsed || []).map(x => ({ label: x })),
        evParse: () => {
          const raw = (this.evPromptRef.current || {}).value || "";
          const p = parseEventPrompt(raw);
          if (this.evTitleRef.current) this.evTitleRef.current.value = p.title;
          if (this.evDayRef.current) this.evDayRef.current.value = p.day;
          if (this.evTimeRef.current) this.evTimeRef.current.value = p.time;
          if (this.evEndDayRef.current) this.evEndDayRef.current.value = p.day;
          this.setState({ evParsed: p.chips });
        },
        evFromThread: () => {
          if (this.evTitleRef.current) this.evTitleRef.current.value = thread.subject;
          if (this.evDayRef.current) this.evDayRef.current.value = EVENT_FROM_THREAD.day;
          if (this.evTimeRef.current) this.evTimeRef.current.value = EVENT_FROM_THREAD.time;
          this.setState({ evParsed: ["from thread: " + thread.subject, EVENT_FROM_THREAD.date, EVENT_FROM_THREAD.time, "attendee: " + thread.from] });
        },
        saveEvent: () => {
          const t = (this.evTitleRef.current || {}).value || "New event";
          const day = ((this.evDayRef.current || {}).value || "28").replace(/[^0-9]/g, "").slice(0, 2) || "28";
          const time = (this.evTimeRef.current || {}).value || "12:00";
          const rem = this.remList("new:event");
          this.setState(s2 => ({
            newEvent: false,
            remMap: Object.assign({}, s2.remMap || {}, { [day + "|" + time + "|" + t]: rem.slice() }),
            calAdded: (s2.calAdded || []).concat([{ day, time, h: parseInt(time, 10) || 12, t, kind: "meeting" }]),
          }));
          this.notify(day + ".08, " + time + " · " + (rem.length
            ? (rem.length === 1 ? remLabel(rem[0]) : rem.length + " reminders")
            : "no reminder"), { kind: "success", title: "Event added" });
          this.remFire(t, rem, "cal", "Calendar", { screen: "cal" });
        },
        newContactOpen: !!st.newContact,
        openNewContact: () => this.setState({ newContact: true, ctEditing: false }),
        ctDialogTitle: st.ctEditing ? "Edit contact" : "New contact",
        ctSaveLabel: st.ctEditing ? "Save changes" : "Save contact",
        closeNewContact: () => this.setState({ newContact: false }),
        ctNameRef: this.ctNameRef, ctMailRef: this.ctMailRef, ctOrgRef: this.ctOrgRef, ctRoleRef: this.ctRoleRef,
        saveContact: () => {
          if (st.ctEditing) {
            this.setState({ newContact: false, ctEditing: false });
            this.notify("Contact saved", { kind: "success" });
            return;
          }
          const name = (this.ctNameRef.current || {}).value || "New contact";
          const mail = (this.ctMailRef.current || {}).value || "";
          const org = (this.ctOrgRef.current || {}).value || "";
          const role = (this.ctRoleRef.current || {}).value || "";
          const id = "new" + Date.now();
          this.setState(s2 => ({
            newContact: false,
            personId: id,
            peoplePane: "detail",
            extraContacts: (s2.extraContacts || []).concat([{
              id, name, role: role || "-", org: org || "-", mail, last: "no correspondence yet",
              owed: "-", note: "Contact added manually. After the first exchange of messages the relationship state appears here.",
              threads: [], docs: [],
            }]),
          }));
        },
        eventOpen: !!st.event,
        eventRemList: (st.event ? this.remList(st.event.day + "|" + st.event.time + "|" + st.event.t) : []).slice().sort((a, b) => b - a).map(m => ({
          label: remLabel(m, st.event && st.event.time === "all day" ? "on the day" : "at the time"),
          remove: () => this.remToggle(st.event.day + "|" + st.event.time + "|" + st.event.t, m),
        })),
        eventRemAny: !!(st.event && this.remList(st.event.day + "|" + st.event.time + "|" + st.event.t).length),
        eventRemNone: !!(st.event && !this.remList(st.event.day + "|" + st.event.time + "|" + st.event.t).length),
        eventRemAction: st.event && this.remList(st.event.day + "|" + st.event.time + "|" + st.event.t).length ? "Change" : "Add a reminder",
        eventOpenRem: () => {
          const e = st.event;
          if (!e) return;
          const key = e.day + "|" + e.time + "|" + e.t;
          if (!this.remList(key).length) this.remSet(key, REM_DEFAULT);
          this.setState({ remFor: { key: key, title: e.t, timed: e.time !== "all day", kind: "cal", hint: this.remAnchor({ time: e.time, day: e.day }), src: "Calendar" } });
        },
        eventTitle: st.event ? st.event.t : "",
        eventWhen: st.event ? (st.event.day ? st.event.day + " August · " : "") + st.event.time : "",
        eventKind: st.event ? (st.event.kind === "deadline" ? "Deadline" : st.event.kind === "mail" ? "From mail" : "Meeting") : "",
        eventKindStyle: "font-size:11px;border-radius:12px;padding:3px 9px;" + (st.event && st.event.kind === "deadline"
          ? "background:var(--hl);color:var(--text);border:1px solid var(--hl-line);"
          : "background:var(--rail);color:var(--text2);border:1px solid var(--line);"),
        eventSrc: st.event ? (st.event.src || "") : "",
        eventHasSrc: !!(st.event && st.event.src),
        eventNote: st.event && st.event.kind === "deadline"
          ? "Deadline detected in the correspondence - changing the date in the thread updates this entry."
          : "Event linked to a thread; attendees come from the thread.",
        closeEvent: () => this.setState({ event: null }),
        eventOpenThread: () => this.setState({ event: null, screen: "mail", mailPane: "thread", threadOpen: true }),
        eventDelete: () => this.askDel({
          title: "Delete this event?",
          text: (st.event ? "“" + st.event.t + "”" : "The event") + " will disappear from the calendar along with its reminders.",
          label: "Delete event",
          run: () => this.setState(s2 => ({
            event: null,
            calAdded: (s2.calAdded || []).filter(x => !(x.t === (s2.event || {}).t)),
          })),
        }),
        calBriefing: CALENDAR_BRIEFING,
        calTitleRowStyle: isMobile
          ? "display:flex;align-items:flex-start;gap:10px"
          : "display:flex;align-items:center;gap:14px;flex:1;min-width:0",
        calNavRowStyle: isMobile
          ? "margin-left:auto;display:flex;align-items:center;gap:6px"
          : "margin-left:auto;display:flex;align-items:center;gap:7px",
        /* Under a finger the four views are one segmented control, the same one People's two lists use, rather than
           four loose buttons: one choice among four reads as one control. */
        calViewsRowStyle: touch
          ? "display:flex;align-items:center;gap:2px;background:var(--rail);border:1px solid var(--line);border-radius:10px;padding:3px" + (isMobile ? "" : ";flex:0 0 auto")
          : "display:flex;align-items:center;gap:7px;flex-wrap:wrap",
        calTodayBtnStyle: "color:var(--text2);border:1px solid var(--border2);border-radius:8px;white-space:nowrap;" +
          (touch ? "box-sizing:border-box;display:flex;align-items:center;min-height:44px;font-size:13px;padding:0 14px" : "font-size:12px;padding:6px 12px"),
        calViews: ["Day", "Week", "Month", "Agenda"].map(v => ({
          label: v,
          pick: () => this.setState({ calView: v }),
          style: touch
            ? "display:flex;align-items:center;justify-content:center;box-sizing:border-box;min-height:36px;border-radius:8px;white-space:nowrap;font-size:13px;" +
              (isMobile ? "flex:1;padding:0 4px;" : "padding:0 14px;") +
              ((st.calView || "Week") === v
                ? "background:var(--panel);color:var(--text);font-weight:600;box-shadow:0 1px 2px var(--sh-1);"
                : "color:var(--muted);")
            : "border-radius:8px;white-space:nowrap;text-align:center;font-size:12px;padding:6px 11px;" +
              ((st.calView || "Week") === v
                ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
                : "color:var(--text2);border:1px solid var(--border2);"),
        })),
        calIsWeek: (st.calView || "Week") === "Week",
        calIsDay: st.calView === "Day",
        calIsMonth: st.calView === "Month",
        calIsAgenda: st.calView === "Agenda",
        calDayWrapStyle: isMobile
          ? "flex:0 0 auto;display:flex;flex-direction:column;padding:6px 14px 16px 14px"
          : narrow
          ? "flex:1;min-width:0;overflow:auto;display:flex;flex-direction:column;padding:6px 14px 16px 14px"
          : "flex:1;min-width:0;overflow:auto;display:flex;flex-direction:column;padding:8px 24px 20px 24px",
        calHours: [8, 9, 10, 11, 12, 13, 14, 15, 16, 17].map(h => ({
          label: (h < 10 ? "0" : "") + h + ":00",
          ev: calToday.ev.filter(e => (e.h || 0) === h).map(e => ({
            t: e.t, time: e.time, src: e.src || "", hasSrc: !!e.src,
            style: evStyle(e.kind) + (isSel("events", calToday.d + "|" + e.time + "|" + e.t) ? "background:var(--accent-soft);border-color:var(--accent);box-shadow:inset 3px 0 0 var(--accent);" : ""),
            press: this.pressFor("events", (calToday.d) + "|" + e.time + "|" + e.t, e.t, [
              { icon: "check_box", label: "Select events", run: () => this.selStart("events", (calToday.d) + "|" + e.time + "|" + e.t) },
              { icon: "event", label: "Open event", run: () => this.setState({ event: Object.assign({ day: calToday.d }, e) }) },
              { icon: "notifications_active", label: "Reminders", run: () => this.setState({ remFor: { key: (calToday.d) + "|" + e.time + "|" + e.t, title: e.t, timed: e.time !== "all day", kind: "cal", hint: this.remAnchor({ time: e.time, day: calToday.d }), src: "Calendar" } }) },
              { icon: "auto_awesome", label: "Ask the agent", run: () => this.goAgentWith("event \u201C" + e.t + "\u201D") },
              { icon: "delete", label: "Delete event", danger: true, run: () => this.askDel({ title: "Delete this event?", text: "\u201C" + e.t + "\u201D will disappear from the calendar along with its reminders.", label: "Delete event", run: () => this.setState(s3 => ({ calAdded: (s3.calAdded || []).filter(x => x.t !== e.t) })) }) },
            ]),
            open: this.rowTap("events", calToday.d + "|" + e.time + "|" + e.t, () => this.setState({ event: Object.assign({ day: calToday.d }, e) })),
          })),
        })),
        calMonthOuterStyle: isMobile
          ? "flex:0 0 auto;display:flex;flex-direction:column"
          : "flex:1;min-width:0;display:flex;flex-direction:column",
        calDowStyle: isMobile
          ? "font-family:'Geist',system-ui,sans-serif;font-size:11px;color:var(--muted);text-align:center;padding:0 0 4px;font-weight:600"
          : "font-family:'Geist',system-ui,sans-serif;font-size:11px;color:var(--muted);padding:0 0 6px 4px;font-weight:600",
        calMonthWrapStyle: isMobile
          ? "flex:0 0 auto;display:grid;grid-template-columns:repeat(7,1fr);gap:2px;padding:10px 8px 8px;align-content:start"
          : narrow
          ? "flex:1;min-width:0;overflow:auto;display:grid;grid-template-columns:repeat(7,1fr);gap:3px;padding:12px 14px"
          : "flex:1;min-width:0;overflow:auto;display:grid;grid-template-columns:repeat(7,1fr);gap:4px;padding:14px 24px 20px 24px;align-content:start",
        calDowLabels: ["MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"].map(l => ({ label: l })),
        calMonthCells: monthCells.map(c => ({
          n: c.n,
          pick: () => (c.blank ? null : this.setState({ calSelDay: c.n })),
          hasMarks: isMobile && !c.blank && c.ev.length > 0,
          marks: (isMobile ? c.ev.slice(0, 3) : []).map(e => ({
            style: "width:5px;height:5px;border-radius:50%;background:" + (e.kind === "deadline" ? "var(--hl-line)" : "var(--accent)"),
          })),
          style: isMobile
            ? "aspect-ratio:1;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:4px;border-radius:12px;" +
              (c.blank ? "background:transparent;" : (c.n === (st.calSelDay || "27")
                ? "background:var(--accent-soft);border:1px solid var(--accent-line);"
                : "background:var(--panel);border:1px solid var(--line2);"))
            : "min-height:88px;display:flex;flex-direction:column;gap:3px;border-radius:8px;padding:6px 7px;" +
              (c.blank ? "background:transparent;" : "background:" + (c.today ? "var(--accent-soft)" : "var(--panel)") + ";border:1px solid var(--line);"),
          numStyle: isMobile
            ? (c.today
              ? "width:26px;height:26px;display:flex;align-items:center;justify-content:center;border-radius:50%;background:var(--accent);color:var(--onaccent);font-size:14px;font-weight:600"
              : "font-size:14px;color:var(--text2)")
            : (c.today ? "font-size:12px;font-weight:600;color:var(--accent-d)" : "font-size:12px;color:var(--muted)"),
          dots: (isMobile ? [] : c.ev).map(e => ({
            t: e.t, open: () => this.setState({ event: Object.assign({ day: c.n }, e) }),
            style: "font-size:11px;line-height:1.3;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;border-radius:4px;padding:2px 5px;" +
              (e.kind === "deadline" ? "background:var(--hl);color:var(--text);" : "background:var(--rail);color:var(--text2);"),
          })),
        })),
        calMonthDayShown: isMobile,
        calMonthDayLabel: (st.calSelDay || "27") + " sierpnia",
        calMonthDayCount: (() => {
          const d = calAllDays.find(x => x.d === (st.calSelDay || "27"));
          const n = d ? d.ev.length : 0;
          return n === 0 ? "" : n === 1 ? "1 event" : n + " events";
        })(),
        calMonthDayEmpty: !((calAllDays.find(x => x.d === (st.calSelDay || "27")) || { ev: [] }).ev.length),
        calMonthDayEvents: ((calAllDays.find(x => x.d === (st.calSelDay || "27")) || { ev: [] }).ev).map(e => ({
          t: e.t, time: e.time,
          open: () => this.setState({ event: Object.assign({ day: st.calSelDay || "27" }, e) }),
          style: "display:flex;flex-direction:column;gap:3px;border-radius:12px;padding:11px 13px;border:1px solid " +
            (e.kind === "deadline" ? "var(--hl-line);background:var(--hl);" : "var(--line);background:var(--panel);"),
        })),
        calAgendaWrapStyle: isMobile
          ? "flex:0 0 auto;display:flex;flex-direction:column;padding:6px 14px 16px 14px"
          : narrow
          ? "flex:1;min-width:0;overflow:auto;display:flex;flex-direction:column;padding:6px 14px 16px 14px"
          : "flex:1;min-width:0;overflow:auto;display:flex;flex-direction:column;padding:8px 24px 20px 24px",
        agRowStyle: isMobile
          ? "display:flex;flex-direction:column;gap:8px;border-top:1px solid var(--line2);padding:12px 0"
          : "display:flex;gap:16px;align-items:flex-start;border-top:1px solid var(--line2);padding:12px 0",
        agDayColStyle: isMobile
          ? "display:flex;align-items:baseline;gap:8px"
          : "flex:0 0 78px;display:flex;flex-direction:column;gap:2px",
        agDowStyle: "font-family:'Geist',system-ui,sans-serif;color:var(--muted);font-weight:600;font-size:11px",
        /* A week column at the fold or the tablet is a hundred pixels wide, too narrow for a finger-sized link beside
           the event's own title; the event opens its thread from its detail there, as it does on the phone's list. */
        calWeekLinks: !touch,
        srcLinkStyle: touch
          ? "align-self:flex-start;display:inline-flex;align-items:center;gap:7px;box-sizing:border-box;min-height:44px;min-width:0;max-width:100%;padding:0 12px;border:1px solid var(--line);border-radius:12px;background:var(--sub);font-size:13px;color:var(--accent-d);overflow:hidden;text-overflow:ellipsis;white-space:nowrap"
          : "align-self:flex-start;display:inline-flex;align-items:center;gap:5px;min-width:0;font-size:12px;color:var(--accent-d);max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap",
        calAgenda: calAllDays.filter(d => d.ev.length).map(d => ({
          day: d.d + ".08",
          dow: d.dow,
          dayStyle: "font-weight:600;font-size:" + (isMobile ? 14 : 15) + "px;" + (d.today ? "color:var(--accent-d);" : ""),
          ev: d.ev.map(e => ({
            t: e.t, time: e.time, src: e.src || "", hasSrc: !!e.src,
            press: this.pressFor("events", (d.d) + "|" + e.time + "|" + e.t, e.t, [
              { icon: "check_box", label: "Select events", run: () => this.selStart("events", (d.d) + "|" + e.time + "|" + e.t) },
              { icon: "event", label: "Open event", run: () => this.setState({ event: Object.assign({ day: d.d }, e) }) },
              { icon: "notifications_active", label: "Reminders", run: () => this.setState({ remFor: { key: (d.d) + "|" + e.time + "|" + e.t, title: e.t, timed: e.time !== "all day", kind: "cal", hint: this.remAnchor({ time: e.time, day: d.d }), src: "Calendar" } }) },
              { icon: "auto_awesome", label: "Ask the agent", run: () => this.goAgentWith("event \u201C" + e.t + "\u201D") },
              { icon: "delete", label: "Delete event", danger: true, run: () => this.askDel({ title: "Delete this event?", text: "\u201C" + e.t + "\u201D will disappear from the calendar along with its reminders.", label: "Delete event", run: () => this.setState(s3 => ({ calAdded: (s3.calAdded || []).filter(x => x.t !== e.t) })) }) },
            ]),
            open: this.rowTap("events", d.d + "|" + e.time + "|" + e.t, () => this.setState({ event: Object.assign({ day: d.d }, e) })),
            dotStyle: "width:8px;height:8px;flex:0 0 8px;border-radius:50%;background:" + (isSel("events", d.d + "|" + e.time + "|" + e.t) ? "var(--accent)" : e.kind === "deadline" ? "var(--hl-line)" : e.kind === "mail" ? "var(--border2)" : "var(--accent)"),
            rowStyle: "display:flex;flex-direction:column;gap:3px;border-radius:8px;padding:6px 8px;margin:0 -8px;" + (isSel("events", d.d + "|" + e.time + "|" + e.t) ? "background:var(--accent-soft);box-shadow:inset 3px 0 0 var(--accent);" : ""),
          })),
        })),
        calFocus: CAL_FOCUS.filter(f => (st.calDone || []).indexOf(f.t) < 0).map(f => ({
          t: f.t, why: f.why, slot: f.day + ".08, " + f.time + " · " + f.len,
          add: () => this.setState(s2 => ({
            calDone: (s2.calDone || []).concat([f.t]),
            calAdded: (s2.calAdded || []).concat([{ day: f.day, time: f.time, h: parseInt(f.time, 10), t: f.t, kind: "meeting", src: "blok od AI" }]),
          })),
          skip: () => this.setState(s2 => ({ calDone: (s2.calDone || []).concat([f.t]) })),
        })),
        calHeadStyle: isMobile
          ? "display:flex;flex-direction:column;gap:5px;background:var(--sub);border-bottom:1px solid var(--line);padding:13px 14px"
          : "display:flex;align-items:center;gap:14px;background:var(--sub);border-bottom:1px solid var(--line);padding:14px 24px",
        calNavStyle: "display:flex;align-items:center;justify-content:center;border:1px solid var(--border2);color:var(--text2);border-radius:8px;" + (touch ? "width:44px;height:44px;font-size:16px;" : "width:30px;height:30px;font-size:14px;"),
        /* Under a finger the week takes the whole width and *Detected in mail* follows it: beside a 312 px side panel
           the tablet's seven columns no longer fit, and a week that scrolls sideways hides its own weekend. */
        calBodyStyle: touch || narrow
          ? "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:14px;padding:14px"
          : "flex:1;min-height:0;display:flex;gap:0",
        calGridStyle: isMobile
          ? "display:flex;flex-direction:column;gap:10px"
          : narrow || touch
          ? "flex:0 0 auto;min-height:420px;display:grid;grid-template-columns:repeat(7,minmax(112px,1fr));gap:0;overflow-x:auto;background:var(--panel);border:1px solid var(--line);border-radius:12px"
          : "flex:1;min-width:0;display:grid;grid-template-columns:repeat(7,1fr);gap:0;overflow:auto",
        calDays: CAL_DAYS.map((d, i) => ({
          dow: d.dow, d: d.d,
          colStyle: isMobile
            ? "display:flex;flex-direction:column;gap:7px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:11px 13px"
            : "display:flex;flex-direction:column;gap:7px;min-height:0;padding:11px 10px;border-right:" + (i === 6 ? "none" : "1px solid var(--line2)") + ";background:" + (d.today ? "var(--accent-soft)" : "transparent"),
          headStyle: "display:flex;align-items:baseline;gap:7px;padding-bottom:6px;border-bottom:1px solid var(--line2)",
          numStyle: d.today
            ? "font-size:16px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:50%;width:26px;height:26px;display:flex;align-items:center;justify-content:center"
            : "font-size:16px;font-weight:600",
          ev: (d.ev.concat((st.calAdded || []).filter(x => x.day === d.d))).map(e => ({
            time: e.time, t: e.t, src: e.src || "", hasSrc: !!e.src,
            press: this.pressFor("events", (d.d) + "|" + e.time + "|" + e.t, e.t, [
              { icon: "check_box", label: "Select events", run: () => this.selStart("events", (d.d) + "|" + e.time + "|" + e.t) },
              { icon: "event", label: "Open event", run: () => this.setState({ event: Object.assign({ day: d.d }, e) }) },
              { icon: "notifications_active", label: "Reminders", run: () => this.setState({ remFor: { key: (d.d) + "|" + e.time + "|" + e.t, title: e.t, timed: e.time !== "all day", kind: "cal", hint: this.remAnchor({ time: e.time, day: d.d }), src: "Calendar" } }) },
              { icon: "auto_awesome", label: "Ask the agent", run: () => this.goAgentWith("event \u201C" + e.t + "\u201D") },
              { icon: "delete", label: "Delete event", danger: true, run: () => this.askDel({ title: "Delete this event?", text: "\u201C" + e.t + "\u201D will disappear from the calendar along with its reminders.", label: "Delete event", run: () => this.setState(s3 => ({ calAdded: (s3.calAdded || []).filter(x => x.t !== e.t) })) }) },
            ]),
            open: this.rowTap("events", d.d + "|" + e.time + "|" + e.t, () => this.setState({ event: Object.assign({ day: d.d }, e) })),
            style: "display:flex;flex-direction:column;gap:3px;border-radius:8px;padding:8px 10px;border:1px solid " + (e.kind === "deadline"
              ? "var(--hl-line);background:var(--hl);"
              : e.kind === "mail"
              ? "var(--line);background:var(--sub);"
              : "var(--line);background:var(--panel);box-shadow:0 1px 2px var(--sh-1);") +
              (isSel("events", d.d + "|" + e.time + "|" + e.t) ? "background:var(--accent-soft);border-color:var(--accent);box-shadow:inset 3px 0 0 var(--accent);" : ""),
          })),
        })),
        calSideStyle: touch || narrow
          ? "flex:0 0 auto;display:flex;flex-direction:column;gap:11px;background:var(--sub);border:1px solid var(--line);border-radius:12px;padding:14px"
          : "width:312px;flex:0 0 312px;display:flex;flex-direction:column;gap:11px;overflow:auto;background:var(--sub);border-left:1px solid var(--line);padding:16px 18px",
        calProposed: CAL_PROPOSED.filter(p => (st.calDone || []).indexOf(p.id) < 0).map(p => ({
          t: p.t, when: p.when, src: p.src,
          add: () => this.setState(s2 => ({
            calDone: (s2.calDone || []).concat([p.id]),
            calAdded: (s2.calAdded || []).concat([{ day: p.when.slice(0, 2), time: "all day", t: p.t, kind: "deadline" }]),
          })),
          skip: () => this.setState(s2 => ({ calDone: (s2.calDone || []).concat([p.id]) })),
        })),
        calNoProposals: CAL_PROPOSED.every(p => (st.calDone || []).indexOf(p.id) >= 0),

        showPeopleList: !isMobile || st.peoplePane !== "detail",
        showPersonDetail: !isMobile || st.peoplePane === "detail",
        showPeopleBack: isMobile && st.peoplePane === "detail",
        backToPeople: () => this.setState({ peoplePane: "list" }),
        peopleCount: shownContacts.length + (shownContacts.length === 1 ? " person" : " people"),
        peopleTabHint: peopleTab === "collected" ? "from received mail headers" : "address book",
        showOwnContacts: () => this.setState({ peopleTab: "own" }),
        showCollectedContacts: () => this.setState({ peopleTab: "collected" }),
        tabOwnStyle: "display:flex;align-items:center;justify-content:center;box-sizing:border-box;flex:1;text-align:center;border-radius:8px;font-size:13px;white-space:nowrap;" + (touch ? "min-height:36px;padding:0 10px;" : "padding:6px 10px;") + (peopleTab !== "collected"
          ? "background:var(--panel);color:var(--text);font-weight:600;box-shadow:0 1px 2px var(--sh-1);"
          : "background:transparent;color:var(--muted);"),
        tabCollectedStyle: "display:flex;align-items:center;justify-content:center;box-sizing:border-box;flex:1;text-align:center;border-radius:8px;font-size:13px;white-space:nowrap;" + (touch ? "min-height:36px;padding:0 10px;" : "padding:6px 10px;") + (peopleTab === "collected"
          ? "background:var(--panel);color:var(--text);font-weight:600;box-shadow:0 1px 2px var(--sh-1);"
          : "background:transparent;color:var(--muted);"),
        personCollected: !!person.collected,
        personOwn: !person.collected,
        // The same dialog as writing somebody down, opened on what the record holds.
        editContact: () => {
          this.setState({ newContact: true, ctEditing: true });
          setTimeout(() => {
            if (this.ctNameRef.current) this.ctNameRef.current.value = person.name;
            if (this.ctMailRef.current) this.ctMailRef.current.value = person.mail;
            if (this.ctOrgRef.current) this.ctOrgRef.current.value = person.org === "-" ? "" : person.org;
            if (this.ctRoleRef.current) this.ctRoleRef.current.value = person.role === "-" ? "" : person.role;
          }, 0);
        },
        addToOwnLabel: narrow || isMobile ? "Add" : "Add to my contacts",
        personReadOnlyNote: "Read-only - editable once added to your contacts",
        removeCollected: () => this.askDel({
          title: "Remove contact from collected?",
          text: person.name + " disappears from the collected list. They come back automatically if they write again.",
          label: "Remove from collected",
          run: () => this.setState(s2 => {
          const rest = collectedContacts.filter(c => c.name !== person.name);
          const at = collectedContacts.findIndex(c => c.name === person.name);
          const nextOne = rest[Math.min(at, rest.length - 1)];
          return {
            hiddenCollected: (s2.hiddenCollected || []).concat([person.name]),
            peopleTab: nextOne ? "collected" : "own",
            personId: nextOne ? nextOne.id : (CONTACTS[0] || {}).id,
            peoplePane: isMobile ? "list" : s2.peoplePane,
          };
          }),
        }),
        addToOwn: () => this.setState(s2 => ({
          peopleTab: "own",
          personId: "own-" + person.id,
          extraContacts: (s2.extraContacts || []).concat([Object.assign({}, person, {
            id: "own-" + person.id, collected: false, role: person.role === "Collected from mail" ? "-" : person.role,
            note: "Contact moved from collected. Fill in role and notes - the data comes only from mail headers.",
          })]),
        })),
        peopleListStyle: isMobile
          ? "flex:1;display:flex;flex-direction:column;min-height:0;min-width:0"
          : "width:" + (narrow ? 268 : 300) + "px;flex:0 0 " + (narrow ? 268 : 300) + "px;display:flex;flex-direction:column;border-right:1px solid var(--line);min-height:0",
        peopleRows: shownContacts.map(c => ({
          press: this.pressFor("people", c.id, c.name, [
            { icon: "check_box", label: "Select contacts", run: () => this.selStart("people", c.id) },
            { icon: "edit", label: "Write a message", run: () => this.setState({ compose: true, composeMin: false, replyMode: null }) },
            { icon: "person", label: "Open contact", run: () => this.setState({ personId: c.id, peoplePane: "detail" }) },
            { icon: "event", label: "Propose a meeting", run: () => this.setState({ screen: "cal", newEvent: true, evParsed: null }) },
            { icon: "delete", label: "Delete contact", danger: true, run: () => this.askHide("people", [c.id], "Delete contact?", c.name + " will disappear from the contact list.") },
          ]),
          name: c.name, org: c.org, last: c.last, initials: initialsOf(c.name),
          selected: isSel("people", c.id),
          hasAvatar: !!avatarOf(c.name), noAvatar: !avatarOf(c.name),
          avatarImgStyle: "width:32px;height:32px;flex:0 0 32px;border-radius:50%;background-color:var(--border2);background-position:center;background-size:cover;background-repeat:no-repeat;background-image:url(" + avatarOf(c.name) + ")",
          wait: (CONTACT_AI[c.id] || {}).wait || "", waiting: !!(CONTACT_AI[c.id] || {}).wait,
          open: this.rowTap("people", c.id, () => this.setState({ personId: c.id, peoplePane: "detail" })),
          style: "display:flex;align-items:center;gap:11px;padding:11px 15px;background:" + (c.id === person.id ? "var(--accent-soft)" : "var(--panel)") +
            ";box-shadow:inset 0 1px 0 var(--inset)," + (c.id === person.id ? "inset 3px 0 0 var(--accent-2)," : "") + "0 1px 2px var(--sh-1);" + selMark("people", c.id),
          avatarStyle: "width:32px;height:32px;flex:0 0 32px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:11px;background:var(--border2);color:var(--text2)",
        })),
        personName: person.name,
        personRole: person.role,
        personOrg: person.org,
        personMail: person.mail,
        personNote: person.note,
        personInitials: initialsOf(person.name),
        personAvatarImgStyle: "width:52px;height:52px;flex:0 0 52px;border-radius:50%;background-color:var(--border2);background-position:center;background-size:cover;background-repeat:no-repeat;background-image:url(" + avatarOf(person.name) + ")",
        personHasAvatar: !!avatarOf(person.name),
        personNoAvatar: !avatarOf(person.name),
        personAvatarStyle: "width:52px;height:52px;flex:0 0 52px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:18px;background:var(--accent);color:var(--onaccent)",
        personWrite: () => (this.tabsMode()
          ? this.openTab("compose", { screen: "mail", compose: true, composeMin: false })
          : this.setState({ screen: "mail", compose: true, composeMin: false })),
        personHeadStyle: isMobile
          ? "display:flex;flex-direction:column;gap:9px;background:var(--sub);border-bottom:1px solid var(--line);padding:14px"
          : "display:flex;flex-direction:column;gap:9px;background:var(--sub);border-bottom:1px solid var(--line);padding:18px max(24px, calc((100% - 1160px) / 2))",
        personBodyStyle: isMobile
          ? "flex:1;overflow:auto;min-height:0;display:flex;flex-direction:column;gap:12px;padding:14px"
          : "flex:1;overflow:auto;min-height:0;display:flex;flex-direction:column;gap:14px;padding:18px max(24px, calc((100% - 1160px) / 2))",
        personStatsStyle: isMobile || narrow ? "display:flex;flex-direction:column;gap:9px" : "display:flex;gap:10px",
        personNext: personAi.next || "",
        personHasNext: !!personAi.next,
        personPatterns: [
          { label: "Replies", value: personAi.reply || "" },
          { label: "Most active", value: personAi.peak || "" },
          { label: "Style", value: personAi.style || "" },
        ].filter(p => !!p.value),
        personStats: [
          { label: "Last contact", value: person.last },
          { label: "Open", value: person.owed },
        ],
        personColsStyle: isMobile || narrow ? "display:flex;flex-direction:column;gap:12px" : "display:flex;gap:12px",
        personThreads: person.threads.map(t3 => ({
          label: t3.label, meta: t3.meta,
          open: () => this.setState({ screen: "mail", threadId: t3.id, mailPane: "thread", threadOpen: true, cameFrom: false }),
        })),
        personDocs: person.docs.map(d => ({
          type: d.type, name: d.name, size: d.size,
          open: () => (this.tabsMode()
            ? this.openTab("doc:" + d.name, { doc: d, docZoom: 1, docLoading: true }, { att: d })
            : this.openDocLoad({ doc: d, docZoom: 1 })),
        })),
        personNoDocs: person.docs.length === 0,

        /* On tablets and narrower screens "Hide panels" also removes the message list —
           the correspondence gets the full width (on desktop the list stays, there is room). */
        showThreadList: (!singlePane || mailPane === "list") && !(compact && st.stateBarOff && mailPane !== "list"),
        showContentPane: !singlePane || mailPane === "thread" || !!st.compose || !!st.doc,
        showThreadPane: (!singlePane || mailPane === "thread") && !composeInPane && !docInPane && !htmlInPane && !emptyMail,
        showMailBack: singlePane && mailPane === "thread",
        backBtnStyle: "flex:0 0 auto;width:" + (isMobile ? 48 : touch ? 44 : 34) + "px;height:" + (isMobile ? 48 : touch ? 44 : 34) + "px;display:flex;align-items:center;justify-content:center;border-radius:12px;color:var(--text2)",
        /* One pane gives the subject a row of its own under the back button and the actions, two lines before it is cut,
           so a long subject is read rather than guessed from its first two words; scrolled, it folds back to one line. */
        threadHeadRowStyle: singlePane
          ? "display:flex;flex-wrap:wrap;align-items:center;gap:2px 9px;min-width:0"
          : "display:flex;align-items:center;gap:9px;min-width:0",
        subjectStyle: singlePane
          ? "order:3;flex:1 1 100%;min-width:0;box-sizing:border-box;padding:0 8px " + (thCollapsed ? 0 : 2) + "px;font-size:" + (thCollapsed ? 16 : 18) + "px;font-weight:600;line-height:1.3;letter-spacing:-0.01em;text-wrap:pretty;display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:" + (thCollapsed ? 1 : 2) + ";overflow:hidden;transition:font-size 140ms ease"
          : low ? "font-size:16px;font-weight:600;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" : "font-size:20px;font-weight:600;min-width:0",
        /* Expandable thread address details — the chevron sits in the sender row so that, collapsed,
           it does not take a separate row in the header. */
        metaWrapStyle: (singlePane && thCollapsed) || low
          ? "display:none"
          : "display:flex;flex-direction:column;gap:2px;min-width:0",
        metaStyle: "display:flex;align-items:center;gap:4px;min-width:0;max-width:100%;align-self:flex-start;border-radius:6px;color:var(--muted);" +
          (singlePane
            ? "font-size:12px;margin:0 1px 2px 1px;padding:1px 5px"
            : "font-size:13px;margin-left:-5px;padding:1px 5px"),
        threadAddrChevStyle: "flex:0 0 auto;display:inline-block;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1;color:var(--faint);transition:transform .13s ease" +
          (st.threadAddrOpen ? ";transform:rotate(90deg)" : ""),
        threadAddrOpen: !!st.threadAddrOpen,
        threadAddrHasMore: !st.threadAddrOpen && threadAddr.count > 0,
        threadAddrMore: "· " + threadAddr.count + " more people",
        threadAddrRows: threadAddr.rows,
        threadAddrTitle: st.threadAddrOpen ? "Collapse address details" : "Other people on this message",
        threadAddrRowsStyle: "display:flex;flex-direction:column;gap:3px;padding:2px 0 3px " + (singlePane ? "27px" : "22px"),
        toggleThreadAddr: () => this.setState(s2 => ({ threadAddrOpen: !s2.threadAddrOpen })),
        threadActions: [
          { icon: "reply", label: "Reply", title: "Reply", run: () => this.startReply("reply", [thread.id]) },
          { icon: "forward", label: "Forward", title: "Forward", run: () => this.startReply("fwd", [thread.id]) },
          { icon: "flag", label: flagOn ? "Unflag" : "Flag", title: flagOn ? "Remove flag" : "Flag", run: () => this.toggleFlagThreads([thread.id]) },
        /* Two panes under a finger carry these three in the toolbar above already, so the header keeps *Ask* alone and
           the subject keeps the width a second copy would take from it. */
        ].filter(() => singlePane || !touch).map(a => ({
          title: a.title,
          run: a.run,
          label: singlePane || paneNarrow ? a.icon : a.label,
          style: singlePane || paneNarrow
            ? "width:" + (isMobile ? 48 : touch ? 44 : 34) + "px;height:" + (isMobile ? 48 : touch ? 44 : 34) + "px;display:flex;align-items:center;justify-content:center;border-radius:" + (touch ? 12 : 8) + "px;color:var(" + (a.icon === "flag" && flagOn ? "--warn" : "--text2") + ");font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:22px;line-height:1"
            : "display:flex;align-items:center;padding:5px 8px;border-radius:8px;font-size:13px;color:var(" + (a.icon === "flag" && flagOn ? "--warn" : "--muted") + ")",
        })),
        backToList: () => this.setState({ mailPane: "list", replyChooser: null }),
        threadListStyle: "position:relative;" + (singlePane
          ? "flex:1;display:flex;flex-direction:column;min-height:0;min-width:0"
          : "width:" + (listStrip ? 76 : listW) + "px;flex:0 0 " + (listStrip ? 76 : listW) + "px;display:flex;flex-direction:column;min-height:0" + (listStrip ? ";background:var(--sub);border-right:1px solid var(--line)" : "")),
        listStrip,
        listFull: !listStrip,
        listNarrowable: foldTwoPanes && !emptyMail && !!st.listWide && !selectionOn,
        widenList: () => this.setState({ listWide: true }),
        narrowList: () => this.setState({ listWide: false }),
        stripHeadStyle: "flex:0 0 auto;display:flex;justify-content:center;padding:8px 0;border-bottom:1px solid var(--line)",
        stripBtnStyle: "width:44px;height:44px;display:flex;align-items:center;justify-content:center;border-radius:12px;color:var(--text2)",
        stripScrollStyle: "flex:1;min-height:0;overflow-y:auto;display:flex;flex-direction:column;align-items:center;gap:4px;padding:8px 0",
        selectionOn,
        selectionOff: !selectionOn,
        showMailToolbar: !selectionOn && !singlePane,
        undoShown: isMobile && !!st.lastArchived && (st.archived || []).indexOf(st.lastArchived) >= 0,
        undoDelete: this.undoArchive,
        showScreenFab: (isMobile || tbFabForced) && !st.newTask && !st.newEvent && !st.newContact && !st.settings &&
          !(st.screen === "people" && (st.peoplePane || "list") === "detail"),
        tbPrimaryShown: tbPriInBar,
        showFab: !st.compose && !st.doc && !selectionOn && (singlePane ? mailPane === "list" : tbFabForced),
        /* A space's own button stands above Fathom's, which keeps the corner on every space. */
        fabStyle: "position:absolute;right:" + ((isMobile ? 20 : 24) + ((touch ? 56 : 52) - 54) / 2) + "px;bottom:" + ((isMobile ? 20 : 24) + (touch ? 56 : 52) + 12) + "px;z-index:30;width:54px;height:54px;border-radius:16px;display:flex;align-items:center;justify-content:center;font-size:20px;background:var(--accent);color:var(--onaccent);box-shadow:0 6px 18px var(--sh-2)",
        /* In multi-panel layouts the circle sits over the thread list, not the reading panel —
           otherwise it would cover the reply composer. */
        mailFabStyle: "position:absolute;right:" + (singlePane ? (isMobile ? 20 : 24) + ((touch ? 56 : 52) - 54) / 2 : 16) + "px;bottom:" + (singlePane ? (isMobile ? 20 : 24) + (touch ? 56 : 52) + 12 : 16) + "px;z-index:30;" +
          "width:54px;height:54px;border-radius:16px;display:flex;align-items:center;justify-content:center;font-size:20px;background:var(--accent);color:var(--onaccent);box-shadow:0 6px 18px var(--sh-2)",
        selectionCount: sel.length + " selected",
        clearSelection: () => this.setState({ sel: [] }),
        selectAllThreads: () => this.setState({ sel: THREADS.map(t => t.id) }),
        tbRef: this.tbRef,
        toolbarStyle: "flex:0 0 auto;display:flex;align-items:center;gap:2px;overflow-x:auto;background:var(--panel);border-bottom:1px solid var(--line);box-shadow:0 1px 2px var(--sh-1);" +
          (isMobile ? "box-sizing:border-box;min-height:56px;padding:6px 8px 6px 16px" : "padding:8px 12px"),
        /* Every space names itself at the top of the phone, at one size: the title a reader looks for before the actions
           beside it, which then stand at the bar's far end. */
        screenTitleStyle: isMobile
          ? "font-size:20px;font-weight:600;letter-spacing:-0.01em;line-height:1.2"
          : "font-size:16px;font-weight:600",
        tbTitleStyle: "flex:0 1 auto;min-width:0;margin-right:auto;padding-right:8px;font-size:20px;font-weight:600;letter-spacing:-0.01em;line-height:1.2;white-space:nowrap;overflow:hidden;text-overflow:ellipsis",
        agentTitleStyle: isMobile
          ? "order:1;flex:1;min-width:0;font-size:20px;font-weight:600;letter-spacing:-0.01em;line-height:1.2;white-space:nowrap;overflow:hidden;text-overflow:ellipsis"
          : "flex:1;min-width:0;font-size:16px;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis",
        agentHistBtnStyle: "display:flex;align-items:center;gap:8px;font-size:13px;color:var(--text2);border:1px solid var(--border2);border-radius:8px;box-sizing:border-box;" +
          (isMobile ? "width:44px;height:44px;justify-content:center;padding:0" : touch ? "min-height:44px;padding:0 14px" : "padding:7px 12px"),
        agentSearchBtnStyle: "display:flex;align-items:center;justify-content:center;font-size:13px;color:var(--text2);border:1px solid var(--border2);border-radius:8px;box-sizing:border-box;" +
          (isMobile ? "order:2;width:44px;height:44px;padding:0" : touch ? "min-width:44px;min-height:44px;padding:0" : "padding:7px 10px"),
        agentNewBtnStyle: "display:flex;align-items:center;gap:7px;font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:8px;white-space:nowrap;box-shadow:0 1px 2px var(--sh-2);box-sizing:border-box;" +
          (isMobile ? "order:3;width:44px;height:44px;justify-content:center;padding:0" : touch ? "min-height:44px;padding:0 15px" : "padding:7px 13px"),
        tbNewLabel: tbFull ? "New message" : "",
        tbPrimaryStyle: tbFull
          ? "display:flex;align-items:center;gap:7px;flex:0 0 auto;font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);padding:8px 13px;border-radius:8px;white-space:nowrap;box-shadow:0 1px 2px var(--sh-2)"
          : "display:flex;align-items:center;justify-content:center;border-radius:8px;background:var(--accent);color:var(--onaccent);flex:0 0 auto;" + (touch ? "width:44px;height:44px" : "width:34px;height:34px"),
        toolbarActions: [
          { icon: "reply", label: "Reply", key: 1 },
          { icon: "reply_all", label: "Reply all" },
          { icon: "forward", label: "Forward" },
          { icon: "archive", label: "Archive", key: 1 },
          { icon: "delete", label: "Delete", key: 1 },
          { icon: "flag", label: flagOn ? "Remove flag" : "Flag", key: 1 },
          { icon: "mark_email_unread", label: "Unread" },
          { icon: "drive_file_move", label: "Move" },
          /* Condensed view: hides the agreed/questions bar under the thread header.
             The setting is global for mail, so it survives switching messages. */
          /* Glyph only — fullscreen/exit reads instantly, the label lives in the tooltip. */
          { icon: st.stateBarOff ? "fullscreen_exit" : "fullscreen", label: st.stateBarOff ? "Show thread panels" : "Hide panels - correspondence only", iconOnly: true, key: 1 },
        ].map(a => ({
           icon: a.icon, title: a.label, label: (tbFull && !a.iconOnly) ? a.label : "", run: mailAct[a.icon],
           style: ((tbFull && !a.iconOnly) ? tbBtn : tbIcon + ";flex:0 0 auto") +
             (a.icon === "flag" && flagOn ? ";color:var(--warn);background:var(--warn-soft)" : "") +
             (a.icon === "fullscreen_exit" ? ";color:var(--accent-d);background:var(--accent-soft)" : "") +
             /* Focus mode is a view toggle, not a message action — kept separate, at the right edge. */
             (a.iconOnly ? ";margin-left:auto" : ""),
         })),
        selBarStyle: "flex:0 0 auto;display:flex;align-items:center;gap:2px;overflow-x:auto;padding:8px 12px;background:var(--accent-soft);border-bottom:1px solid var(--accent-line)",
        selectionActions: [
          { icon: "archive", label: "Archive" },
          { icon: "delete", label: "Delete" },
          { icon: "flag", label: "Flag" },
          { icon: "mark_email_unread", label: "Unread" },
          { icon: "drive_file_move", label: "Move" },
        ].map(a => ({ icon: a.icon, title: a.label, label: tbFull ? a.label : "", run: mailAct[a.icon], style: tbFull ? selBtn : tbIcon + ";flex:0 0 auto;color:var(--accent-d)" })),
        aiSumOpen: !!aiSumThread,
        aiSumSubject: aiSumThread ? aiSumThread.subject : "",
        /* Wide: a reading and its passage side by side, one hairline between readings and no cards
           inside the dialog. Phone and fold: the whole screen, stacked, the passage held to four lines. */
        ...(() => {
          const full = w < 900;
          return {
            aiSumCards: aiSumThread ? this.aiSumCards(aiSumThread) : [],
            aiSumAuthor: "Written by AI",
            aiSumFootText: full ? "Quoted from this message alone" : "Read from this message alone - every reading quotes the passage it rests on.",
            aiSumOverlayStyle: full
              ? (isMobile ? this.ovStyle(97, true) : "position:fixed;inset:0;z-index:97;display:flex;align-items:stretch;justify-content:stretch;background:var(--scrim)")
              : "position:fixed;inset:0;z-index:97;display:flex;align-items:center;justify-content:center;background:var(--scrim);padding:32px",
            moAiSum: full ? "sheet" : "pop",
            dlgAi: full
              ? "box-sizing:border-box;width:100%;height:100%;display:flex;flex-direction:column;background:var(--panel);overflow:hidden"
              : "width:" + (narrow ? 760 : 920) + "px;max-width:100%;max-height:" + (low ? "calc(100dvh - 16px)" : "min(86dvh, 860px)") + ";display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:16px;box-shadow:0 28px 64px var(--shadow);overflow:hidden",
            aiSumHeadStyle: "flex:0 0 auto;display:flex;align-items:center;gap:12px;border-bottom:1px solid var(--line);" + (full ? "padding:10px 10px 10px 16px" : "padding:18px 18px 16px 26px"),
            aiSumGlyphStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;border-radius:10px;background:var(--accent-soft);color:var(--accent-d);font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;" + (full ? "width:32px;height:32px;font-size:18px" : "width:38px;height:38px;font-size:21px"),
            aiSumTitleStyle: "font-weight:600;letter-spacing:-0.01em;" + (full ? "font-size:16px" : "font-size:18px"),
            aiSumBodyStyle: "flex:1;min-height:0;overflow-y:auto;display:flex;flex-direction:column;" + (full ? "padding:4px 16px 16px" : "padding:6px 26px 18px"),
            aiSumRowStyle: full
              ? "display:flex;flex-direction:column;gap:10px;padding:14px 0;background:var(--panel)"
              : "display:grid;grid-template-columns:minmax(0,5fr) minmax(0,6fr);gap:28px;padding:22px 0;background:var(--panel)",
            aiSumClaimStyle: "font-weight:600;line-height:1.35;text-wrap:balance;color:var(--text);" + (full ? "font-size:16px" : "font-size:19px;letter-spacing:-0.01em"),
            aiSumQuoteStyle: "font-size:" + (full ? 13 : 14) + "px;line-height:1.6;color:var(--text2);background:var(--sub);border-left:2px solid var(--accent-line);border-radius:0 10px 10px 0;padding:" + (full ? "8px 11px" : "12px 16px") + ";text-wrap:pretty;" +
              (full ? "display:-webkit-box;-webkit-line-clamp:4;-webkit-box-orient:vertical;overflow:hidden" : "max-height:180px;overflow:auto"),
            aiSumSrcStyle: "align-self:flex-start;display:flex;align-items:center;gap:6px;font-size:12px;color:var(--accent-d);border-radius:8px;padding:" + (touch ? "9px 8px" : "5px 8px") + ";margin-left:-8px",
            aiSumFootStyle: "flex:0 0 auto;display:flex;align-items:center;gap:8px;border-top:1px solid var(--line);background:var(--sub);font-size:12px;color:var(--faint);" + (full ? "padding:9px 16px" : "padding:11px 26px"),
          };
        })(),
        closeAiSum: this.closeAiSum,
        moveOpen: !!(st.moveFor && st.moveFor.length),
        moveTitle: st.moveFor ? "Move " + this.countWord(st.moveFor.length) : "",
        closeMove: this.closeMove,
        moveGroups: ACCOUNTS.map(a => ({ key: a.key, label: a.label, dot: a.dot, folders: a.moveFolders })).map(g => ({
          label: g.label,
          wrapStyle: "display:flex;flex-direction:column;gap:1px;padding-bottom:6px",
          headStyle: "display:flex;align-items:center;gap:8px;font-family:'Geist',system-ui,sans-serif;font-size:11px;color:var(--muted);padding:8px 9px 5px;font-weight:600",
          dotStyle: "width:8px;height:8px;flex:0 0 8px;border-radius:50%;background:" + g.dot,
          newFolder: () => this.openNewFolder(g.key, g.label),
          newFolderStyle: "display:flex;align-items:center;gap:11px;padding:" + (isMobile ? "11px 14px" : "8px 13px") + ";border-radius:8px;font-size:13px;color:var(--accent-d)",
          folders: g.folders.concat(((st.extraFolders || {})[g.key] || []).map(n => ({ icon: "folder", label: n }))).map(mf => ({
            icon: mf.icon, label: mf.label, pick: () => this.moveThreads(mf.label, g.label),
            style: "display:flex;align-items:center;gap:11px;padding:" + (isMobile ? "13px 14px" : "10px 13px") + ";border-radius:8px;font-size:14px;color:var(--text2)",
          })),
        })),
        undoOp: this.undoOp,
        hideToast: this.hideToast,
        railBellIcon: notifUnreadCount ? "notifications_active" : "notifications",
        newFolderOpen: !!st.newFolder,
        newFolderTitle: st.newFolder && st.newFolder.mode === "edit" ? "Edit folder" : "New folder",
        nfSaveLabel: st.newFolder && st.newFolder.mode === "edit" ? "Save changes" : "Create folder",
        createFolder: this.saveFolder,
        delFolderOpen: !!st.delFolder,
        delFolderName: st.delFolder ? st.delFolder.path.split("/").pop() : "",
        /* Four outcomes, four sentences: nested folders go too, mail stored in them goes with them,
           and whether that mail can still be recovered is the account's own delete setting. */
        delFolderText: (() => {
          const df = st.delFolder;
          if (!df) return "";
          const nested = ((df.extraFolders || {})[df.key] || []).filter(p => p.indexOf(df.path + "/") === 0);
          const names = [df.path].concat(nested).map(p => p.split("/").pop());
          const held = Object.keys(df.moved || {}).filter(id => names.indexOf((df.moved || {})[id]) >= 0).length;
          const acc = (st.accounts || [])[df.key === "acc2" ? 1 : 0];
          const hard = !acc || acc.hardDelete !== false;
          const what = nested.length
            ? "The folder and the " + (nested.length === 1 ? "folder nested" : nested.length + " folders nested") + " inside it"
            : "The folder";
          return what + " will be deleted in " + df.label + " and on your mail server. " + (held
            ? (held === 1 ? "The conversation" : "All " + held + " conversations") + " stored in it " + (hard
                ? "will be deleted permanently on both servers - there will be nothing left to restore."
                : "will move to Trash, so you can still bring " + (held === 1 ? "it" : "them") + " back.")
            : "It holds no mail, and deleting it cannot be undone.");
        })(),
        closeDeleteFolder: this.closeDeleteFolder,
        confirmDeleteFolder: this.confirmDeleteFolder,
        dangerBtnStyle: "display:flex;align-items:center;font-size:13px;font-weight:600;color:var(--onaccent);background:var(--err);border:1px solid var(--err);border-radius:8px;padding:9px 14px",
        newFolderAccount: st.newFolder ? st.newFolder.label : "",
        closeNewFolder: this.closeNewFolder,
        nfName: st.nfName || "",
        nfParent: st.nfParent || "",
        onNfName: this.onNfName,
        onNfParent: this.onNfParent,
        nfLocations: st.newFolder ? this.folderLocations(st.newFolder.key, st.newFolder.mode === "edit" ? st.newFolder.path : "") : [],
        nfPathHint: "MailFathom creates the folder on this account's mail server and picks where it sits there - you only choose where it shows up in your folder list.",
        nfCancelStyle: "display:flex;align-items:center;font-size:13px;font-weight:600;color:var(--text2);background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:9px 14px",
        nfCreateStyle: "display:flex;align-items:center;font-size:13px;font-weight:600;border-radius:8px;padding:9px 14px;" +
          ((st.nfName || "").trim()
            ? "color:var(--onaccent);background:var(--accent);border:1px solid var(--accent);"
            : "color:var(--faint);background:var(--rail);border:1px solid var(--line);"),
        nfCreateHover: (st.nfName || "").trim() ? "cursor:pointer;opacity:0.9" : "cursor:not-allowed",
        /* The one refresh: it re-reads the mail in front of the reader and the notification centre.
           Solid border, no spinner - waiting is the pulse the prototype uses for work in progress. */
        refreshAll: this.refreshAll,
        refreshIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;font-size:20px;line-height:1;display:block" +
          (st.refreshBusy ? ";animation:mfpulse 1s ease-in-out infinite" : ""),
        /* Under a finger the rail's three corner controls meet the 44 px floor rather than shrinking with the rail. */
        refreshBtnStyle: "position:relative;flex:0 0 auto;width:" + (touch ? 44 : 34) + "px;height:" + (touch ? 44 : 34) + "px;display:flex;align-items:center;justify-content:center;border-radius:12px;border:1px solid " +
          (st.refreshBusy ? "var(--accent-line);background:var(--accent-soft);color:var(--accent-d);" : "var(--line);background:var(--panel);color:var(--text2);") +
          "transition:background 140ms ease,color 140ms ease,border-color 140ms ease",
        railBellStyle: "position:relative;flex:0 0 auto;width:" + (touch ? 44 : 46) + "px;height:" + (touch ? 44 : 46) + "px;display:flex;align-items:center;justify-content:center;border-radius:12px;border:1px solid " +
          (st.notifCenter ? "var(--accent-line);background:var(--accent-soft);color:var(--accent-d);" : "var(--line);background:var(--panel);color:var(--text2);box-shadow:0 1px 2px var(--sh-1);"),
        notifBadgeStyle: "position:absolute;top:-6px;right:-6px;min-width:21px;height:21px;box-sizing:border-box;padding:0 5px;display:flex;align-items:center;justify-content:center;border-radius:12px;background:var(--err);color:#fff;font-size:12px;font-weight:700;border:2px solid var(--rail)",
        notifHasUnread: notifUnreadCount > 0,
        notifNewBadgeStyle: "flex:0 0 auto;font-weight:600;border-radius:12px;background:var(--err-soft);color:var(--err-text);" + (touch ? "font-size:12px;padding:5px 11px" : "font-size:11px;padding:3px 9px"),
        notifCloseStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;color:var(--muted);border-radius:12px;" + (touch ? "width:44px;height:44px;border:1px solid var(--line)" : "width:32px;height:32px"),
        markAllReadStyle: "margin-left:auto;font-size:" + (touch ? 13 : 12) + "px;color:var(--accent-d);" + (touch ? "display:flex;align-items:center;min-height:44px;padding:0 6px" : ""),
        /* Settings under a finger: the finder, every row of its navigation and every field reach 44 px. */
        settingsFindStyle: "display:flex;align-items:center;gap:8px;" + (touch ? "box-sizing:border-box;height:44px;" : "height:36px;") + "padding:0 10px;margin:0 2px 12px;background:var(--panel);border:1px solid var(--line);border-radius:10px",
        settingsInputPad: touch ? "min-height:44px;padding:0 12px;" : "padding:7px 10px;",
        settingsSmallBtnPad: touch ? "min-height:44px;padding:0 12px;" : "padding:4px 9px;",
        notifCloseIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:" + (touch ? 24 : 20) + "px;line-height:1",
        notifCountLabel: notifUnreadCount > 9 ? "9+" : String(notifUnreadCount),
        notifNewLabel: notifUnreadCount === 1 ? "1 new" : notifUnreadCount + " new",
        notifCenterOpen: !!st.notifCenter,
        closeNotifCenter: this.closeNotifCenter,
        toggleNotifCenter: this.toggleNotifCenter,
        markAllNotifsRead: this.markAllNotifsRead,
        notifPanelRef: this.notifPanelRef,
        notifListRef: this.notifListRef,
        notifPanelDown: isMobile ? this.notifGest("close") : null,
        railGestDown: isMobile ? this.notifGest("open") : null,
        railTapGuard: isMobile ? this.railTapGuard : null,
        notifGrabStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;padding:8px 0 2px;cursor:grab",
        notifScrimStyle: "position:fixed;inset:0;z-index:88;background:var(--scrim);" +
          (notifDragging ? "transition:none;" : "transition:opacity " + (st.notifSpring ? NG.springMs + "ms " + NG.springEase : "260ms ease") + ";") +
          "opacity:" + (isMobile ? notifProg : notifIn ? 1 : 0),
        notifPanelStyle: "position:fixed;z-index:89;top:0;" +
          (isMobile ? "left:0;right:0;bottom:" + this.barH() + "px;" : "bottom:0;left:" + railW + "px;width:min(400px,calc(100vw - " + (railW + 24) + "px));border-right:1px solid var(--line);") +
          "display:flex;flex-direction:column;background:var(--panel);box-shadow:26px 0 60px var(--sh-3);" +
          (isMobile
            ? "touch-action:pan-y;transform:translateY(" + (notifIn ? notifDrag + "px" : "100%") + ");" +
              (notifDragging
                ? "transition:none;"
                : st.notifSpring
                ? "transition:transform " + NG.springMs + "ms " + NG.springEase + ";"
                : "transition:transform 300ms cubic-bezier(.32,.72,0,1);")
            : "transform:translateX(" + (notifIn ? "0" : "-102%") + ");transition:transform 260ms cubic-bezier(.3,.7,.1,1);"),
        notifTabs: [{ k: "unread", label: "Unread" + (notifUnreadCount ? " · " + notifUnreadCount : "") }, { k: "all", label: "All" }].map(x => ({
          label: x.label,
          pick: () => this.setState({ notifFilter: x.k }),
          style: "display:flex;align-items:center;" + (touch ? "box-sizing:border-box;height:44px;padding:0 14px;" : "height:30px;padding:0 12px;") + "border-radius:8px;font-size:13px;border:1px solid " +
            (notifFilter === x.k ? "var(--accent);background:var(--accent-soft);color:var(--accent-d);font-weight:600" : "var(--border2);background:var(--panel);color:var(--text2)"),
        })),
        notifEmpty: notifShown.length === 0,
        notifItems: notifShown.map(n => {
          const K = NKIND[n.kind] || NKIND.system;
          const unread = isNotifUnread(n, st.notifRead || {});
          return {
            title: n.title, body: n.body, src: n.src, icon: K.icon, unread: unread,
            time: notifTime(n.mins),
            press: this.pressFor("notifs", n.id, n.title, [
              { icon: "check_box", label: "Select notifications", run: () => this.selStart("notifs", n.id) },
              { icon: unread ? "mark_email_read" : "mark_email_unread", label: unread ? "Mark as read" : "Mark as unread", run: () => this.toggleNotifRead(n) },
              { icon: "open_in_new", label: "Open source", run: () => this.openNotif(n) },
              { icon: "delete", label: "Delete notification", danger: true, run: () => this.askHide("notifs", [n.id], "Delete notification?", n.title + " will disappear from the notification centre.") },
            ], 420, notifInstant(n.mins)),
            open: this.rowTap("notifs", n.id, () => this.openNotif(n)),
            toggleRead: (e) => this.toggleNotifRead(n, e),
            readIcon: unread ? "mark_email_read" : "mark_email_unread",
            readTitle: unread ? "Mark as read" : "Mark as unread",
            readBtnStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;border:1px solid " + (unread ? "var(--accent-line)" : "var(--line)") + ";border-radius:12px;" + (touch ? "width:44px;height:44px;" : "width:32px;height:32px;") + "background:" + (unread ? "var(--accent-soft)" : "var(--panel)") + ";color:" + (unread ? "var(--accent-d)" : "var(--muted)"),
            readIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:" + (touch ? 22 : 20) + "px;line-height:1",
            titleStyle: "flex:1;min-width:0;font-size:14px;line-height:1.35;text-wrap:pretty;font-weight:" + (unread ? "600" : "500") + ";color:" + (unread ? "var(--text)" : "var(--text2)"),
            iconWrapStyle: "flex:0 0 auto;width:32px;height:32px;display:flex;align-items:center;justify-content:center;border-radius:12px;background:" + K.soft,
            iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;font-size:20px;line-height:1;color:" + K.c,
            style: "display:flex;align-items:flex-start;gap:12px;padding:13px 16px;border-bottom:1px solid var(--line2);" + (unread ? "background:var(--sub);" : "") + selMark("notifs", n.id) +
              (notifAnim[n.id] === "in" ? "overflow:hidden;animation:mfrowin 480ms cubic-bezier(.22,1,.36,1) both;" : ""),
          };
        }),
        /* On the phone and the fold a toast is a slim strip at the top: it says one thing and goes. */
        toastWrapStyle: "position:fixed;z-index:97;top:" + (toastCompact ? 8 : 18) + "px;" + (isMobile ? "left:8px;right:8px;" : toastCompact ? "left:50%;transform:translateX(-50%);width:min(440px,calc(100vw - 24px));" : "right:18px;width:min(400px,calc(100vw - 36px));") + "display:flex;flex-direction:column;gap:" + (toastCompact ? 6 : 10) + "px;pointer-events:none",
        toastTitleStyle: "font-weight:600;color:var(--text);text-wrap:pretty;" + (toastCompact ? "font-size:14px;line-height:1.3;" : "font-size:15px;line-height:1.35;"),
        toastBodyStyle: "color:var(--text2);text-wrap:pretty;" + (toastCompact ? "font-size:13px;line-height:1.35;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical;overflow:hidden;" : "font-size:14px;line-height:1.45;"),
        toastTextStyle: "flex:1;min-width:0;display:flex;flex-direction:column;" + (toastCompact ? "gap:1px;padding:2px 0;" : "gap:4px;padding-top:2px;"),
        toastActionStyle: "display:flex;align-items:center;border:1px solid var(--border2);border-radius:8px;font-size:13px;font-weight:600;color:var(--text);white-space:nowrap;" + (toastCompact ? "flex:0 0 auto;align-self:center;min-height:36px;padding:0 12px;" : "align-self:flex-start;min-height:32px;margin-top:5px;padding:0 13px;"),
        toasts: (st.toasts || []).map(t => {
          const K = {
            neutral: ["info", "var(--text2)", "var(--hover)", "var(--border2)"],
            success: ["check_circle", "var(--ok-text)", "var(--ok-soft)", "var(--ok)"],
            error: ["error", "var(--err-text)", "var(--err-soft)", "var(--err)"],
            warning: ["warning", "var(--warn-text)", "var(--warn-soft)", "var(--warn)"],
            info: ["campaign", "var(--accent-2)", "var(--accent-soft)", "var(--accent)"],
            loading: ["progress_activity", "var(--accent-2)", "var(--accent-soft)", "var(--accent)"],
          }[t.kind] || ["info", "var(--text2)", "var(--hover)", "var(--border2)"];
          const loading = t.kind === "loading";
          return {
            id: t.id,
            icon: K[0],
            title: t.title,
            body: t.body,
            hasBody: !!t.body,
            actionBelow: !!t.actionLabel && !toastCompact,
            actionInline: !!t.actionLabel && toastCompact,
            actionLabel: t.actionLabel,
            action: t.onAction || this.undoOp,
            close: loading ? () => this.askCancelToast(t.id) : () => this.dismissToast(t.id),
            closeTitle: loading ? "Abort operation" : "Close",
            hasBar: !t.sticky && !t.out,
            barStyle: "position:absolute;left:0;right:0;bottom:0;height:2px;transform-origin:left;opacity:0.55;background:" + K[3] + ";animation:mftbar " + (t.ms || 5000) + "ms linear both",
            iconWrapStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;background:" + K[2] + ";" + (toastCompact ? "width:28px;height:28px;border-radius:8px;" : "width:34px;height:34px;border-radius:12px;"),
            iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;line-height:1;font-size:" + (toastCompact ? 18 : 22) + "px;color:" + K[1] + (loading ? ";animation:mfspin 1.1s linear infinite" : ""),
            style: "pointer-events:auto;position:relative;overflow:hidden;display:flex;align-items:flex-start;" + (toastCompact ? "align-items:center;gap:10px;padding:6px 4px 6px 8px;border-radius:12px;" : "gap:12px;padding:13px 12px 15px 13px;border-radius:16px;") + "background:var(--panel);border:1px solid var(--line);box-shadow:0 16px 38px var(--sh-2),0 2px 6px var(--sh-1);opacity:" + (toastCompact ? 1 : 0.8) + ";transition:opacity .15s ease;animation:" + (t.out ? "mftout .2s ease forwards" : "mftin .24s cubic-bezier(.2,.85,.3,1) backwards"),
          };
        }),
        cancelAskOpen: !!st.cancelAsk,
        cancelAskTitle: st.cancelAsk ? st.cancelAsk.title : "",
        cancelAskText: st.cancelAsk ? st.cancelAsk.text : "",
        keepTask: () => this.setState({ cancelAsk: null }),
        abortTaskNow: this.abortTask,
        /* A drag handle is a fine pointer's: under a finger the list keeps the width its band gives it. */
        showResizer: !singlePane && !touch && !listStrip,
        startResize: this.beginResize,
        resetResize: () => this.setState({ listW: null }),
        /* The thread list leaves the DOM when a message opens (single-panel layout),
           so we keep the scroll position ourselves and restore it on return. */
        listScrollRef: this.keepScroll("mail"),
        onListScroll: this.keepScrollOn("mail"),
        peopleListRef: this.keepScroll("people"),
        onPeopleListScroll: this.keepScrollOn("people"),
        tasksBodyRef: this.keepScroll("tasks"),
        onTasksScroll: this.keepScrollOn("tasks"),
        calScrollRef: this.keepScroll("cal"),
        onCalScroll: this.keepScrollOn("cal"),
        listScrollStyle: "position:relative;flex:1;overflow:auto;min-height:160px;background:var(--sub)",
        listInnerStyle: "display:flex;flex-direction:column;gap:1px;padding:1px 0",
        threadHeadStyle: singlePane
          ? "flex:0 0 auto;display:flex;flex-direction:column;gap:2px;padding:" + (thCollapsed ? "3px 6px" : "6px 8px 8px 8px") + ";border-bottom:1px solid var(--line);background:var(--panel);transition:padding 140ms ease"
          : "display:flex;flex-direction:column;gap:" + (low ? 2 : 7) + "px;padding:" + (low ? 8 : 16) + "px " + readPad(22) + ";border-bottom:1px solid var(--line)",
        showThreadStateBar: (!singlePane || !thCollapsed) && !stateCards && !st.stateBarOff,
        /* Condensed view: only the correspondence remains. On a narrow screen the header stays —
           mieszka w nim przycisk powrotu do listy. */
        showThreadHead: !st.stateBarOff || singlePane,
        replyFabShown: !st.stateBarOff,
        /* The one AI entry point of a thread: a floating glyph pinned to the pane's bottom-right corner, over the
           thread rather than in it, so it stays put however far the conversation scrolls. Pressing it unfolds two
           choices above it, *Draft a reply* and *Ask*, and turns the glyph into a close; Escape, back, a press
           outside or a choice folds them again. */
        replyChooserOpen: !st.stateBarOff && !!st.replyChooser,
        toggleReplyChooser: () => this.setState(s => ({ replyChooser: s.replyChooser ? null : true })),
        closeReplyChooser: () => this.setState({ replyChooser: null }),
        replyFabIcon: st.replyChooser ? "close" : "auto_awesome",
        replyFabTitle: st.replyChooser ? "Close" : "Draft a reply or ask about this thread",
        replyFabStyle: "position:absolute;z-index:16;display:flex;align-items:center;justify-content:center;color:var(--onaccent);background:var(--accent);box-shadow:0 10px 26px var(--sh-3),0 2px 6px var(--sh-2);" +
          (isMobile ? "right:16px;bottom:16px;width:56px;height:56px;border-radius:18px;" : "right:" + (singlePane ? 16 : 22) + "px;bottom:" + (singlePane ? 16 : 20) + "px;width:48px;height:48px;border-radius:14px;"),
        replyChooserStyle: "position:absolute;z-index:17;display:flex;flex-direction:column;align-items:flex-end;gap:8px;" +
          (isMobile ? "right:16px;bottom:84px;" : "right:" + (singlePane ? 16 : 22) + "px;bottom:" + (singlePane ? 72 : 76) + "px;"),
        replyChoiceStyle: "display:flex;align-items:center;gap:10px;white-space:nowrap;color:var(--text);background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 10px 26px var(--sh-3),0 2px 6px var(--sh-2);font-weight:600;" +
          (touch ? "min-height:48px;padding:0 18px 0 14px;font-size:15px;" : "height:40px;padding:0 16px 0 12px;font-size:14px;"),
        chooseDraftReply: () => { this.setState({ replyChooser: null }); this.draftReply(); },
        chooseAskThread: () => { this.setState({ replyChooser: null }); this.goAgentWith("thread “" + thread.subject + "”"); },
        /* A reading that predates the conversation's newest message: a message joined or left after
           the state was derived, so the reading is held back instead of being shown out of date. */
        stateStale: !!thread.stateStale,
        stateFresh: !thread.stateStale,
        stateInBody: stateCards && !st.stateBarOff && !thread.stateStale,
        staleInBody: stateCards && !st.stateBarOff && !!thread.stateStale,
        threadAiLine: thread.ai || (thread.state[0] ? thread.state[0].value : ""),
        threadSheetOpen: !!st.threadSheet,
        agentJumpRowStyle: "flex:0 0 auto;display:flex;align-items:center;gap:8px;flex-wrap:wrap;border-bottom:1px solid var(--line);background:var(--sub);padding:" + (paneCompact ? "9px 16px" : "10px 24px"),
        askSheetLabel: isMobile || paneCompact ? "" : "Thread state",
        askSheetAltStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;gap:7px;border:1px solid var(--border2);border-radius:999px;color:var(--text2);white-space:nowrap;" +
          "box-sizing:border-box;" + (isMobile ? "width:48px;height:48px;padding:0;font-size:14px" : "min-height:" + (touch ? 44 : 38) + "px;padding:0 " + (paneCompact ? 12 : 14) + "px;font-size:13px"),
        openThreadSheet: () => this.setState({ threadSheet: true }),
        threadSheetAsks: [
          "Summarise where this stands",
          "What the other side expects from me",
          "Draft a reply for me",
        ].map(q => ({ label: q, run: q === "Draft a reply for me"
          ? () => { this.setState({ threadSheet: false }); this.draftReply(); }
          : () => this.setState({ threadSheet: false, mailAnswer: true }) })),
        goAgentBtnStyle: "display:flex;align-items:center;justify-content:center;gap:9px;border:1px solid var(--accent-line);border-radius:12px;background:var(--accent-soft);color:var(--accent-d);font-weight:600;" +
          (touch ? "min-height:50px;padding:0 14px;font-size:14px" : "min-height:40px;padding:0 14px;font-size:13px"),
        askAgentThread: () => this.goAgentWith("thread “" + thread.subject + "”"),
        agentCtxShown: !!(st.agentCtx || (activeConv && activeConv.ctx && !st.agentCtxCleared)),
        agentInputRef: this.agentInputRef,
        agentCtxLabel: st.agentCtx || (activeConv ? activeConv.ctx : "") || "",
        clearAgentCtx: () => this.setState({ agentCtx: null, agentCtxCleared: true }),
        convReturnShown: !!st.agentReturn && (st.screen !== "agent" || mo.agentReturn === "out"),
        convReturnLabel: "Back to the conversation",
        backToConv: this.backToConv,
        backToConvKey: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); this.backToConv(); } },
        convReturnStyle: "position:fixed;z-index:120;left:50%;transform:translateX(-50%);" + (isMobile ? "bottom:78px;" : "bottom:22px;") +
          "display:inline-flex;align-items:center;gap:8px;min-height:44px;padding:0 18px;border-radius:24px;font-size:14px;font-weight:600;color:var(--onaccent);background:var(--accent);border:1px solid var(--accent);box-shadow:0 6px 20px var(--sh-1)",
        convReturnIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1",
        closeThreadSheet: () => this.setState({ threadSheet: false }),
        onThreadScroll: (e) => {
          const y = e.target.scrollTop, c = y > 40;
          if (c !== !!this.state.thScroll) this.setState({ thScroll: c });
        },
        threadTouchStart: (e) => {
          const t2 = e.touches && e.touches[0];
          this._sx = t2 ? t2.clientX : 0; this._sy = t2 ? t2.clientY : 0;
        },
        threadTouchEnd: (e) => {
          const t2 = e.changedTouches && e.changedTouches[0];
          if (!t2 || !singlePane) return;
          const dx = t2.clientX - this._sx, dy = t2.clientY - this._sy;
          if (Math.abs(dx) < 72 || Math.abs(dy) > 56) return;
          const i = THREADS.findIndex(x => x.id === thread.id);
          const next = THREADS[dx < 0 ? i + 1 : i - 1];
          if (next) this.setState({ threadId: next.id, thScroll: false, stateOpen: false });
        },
        threadStateStyle: singlePane
          ? "flex:0 0 auto;display:flex;align-items:center;gap:9px;background:var(--sub);border-bottom:1px solid var(--line);padding:9px 12px"
          : readingColumn
            ? "flex:0 0 auto;display:flex;flex-direction:column;gap:6px;background:var(--sub);border:1px solid var(--line);border-radius:8px;margin:14px max(9px, calc((100% - 906px) / 2)) 0;padding:9px 12px"
            : "display:flex;flex-direction:column;gap:6px;background:var(--sub);border-bottom:1px solid var(--line);padding:9px " + readPad(22),
        stateRowStyle: isMobile || paneNarrow ? "display:flex;flex-direction:column;gap:6px;padding-top:1px"
          : thread.state.length >= 4 ? "display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px 20px" : "display:flex;gap:12px 20px;flex-wrap:wrap",
        stateBodyRowStyle: thread.state.length >= 4
          ? "flex:0 0 auto;display:grid;grid-template-columns:" + (paneNarrow ? "minmax(0,1fr)" : "repeat(2,minmax(0,1fr))") + ";gap:7px;background:var(--sub);border:1px solid var(--line);border-radius:8px;padding:8px"
          : "flex:0 0 auto;display:flex;gap:7px;flex-wrap:wrap;background:var(--sub);border:1px solid var(--line);border-radius:8px;padding:8px",
        stateChips: thread.state.map(st2 => ({
          label: st2.label,
          value: st2.value,
          valueStyle: thread.state.length >= 4
            ? "font-size:13px;line-height:1.35;text-wrap:pretty"
            : "font-size:13px;line-height:1.3;overflow:hidden;text-overflow:ellipsis;white-space:nowrap",
          style: "flex:1 1 190px;min-width:0;display:flex;flex-direction:column;gap:1px;background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:6px 9px",
        })),
        isMobileState: singlePane,
        mobileStateLine: singlePane && !thread.stateStale,
        showStateCards: !singlePane && !thread.stateStale,
        stateToggleStyle: singlePane
          ? "margin-left:auto;font-size:12px;color:var(--accent-d);background:var(--accent-soft);border-radius:12px;padding:3px 10px"
          : "display:none",
        stateToggleLabel: st.stateOpen ? "hide" : "3 fields",
        /* The bottom padding keeps the last line of the thread clear of the floating reply button. */
        threadBodyStyle: singlePane
          ? "flex:1;display:flex;flex-direction:column;gap:10px;padding:12px 12px " + (st.stateBarOff ? 12 : 84) + "px;overflow:auto;min-height:0"
          : "flex:1;display:flex;flex-direction:column;gap:12px;padding:18px " + readPad(22) + " " + (st.stateBarOff ? 18 : 86) + "px;overflow:auto;min-height:0",
        backBannerStyle: isMobile
          ? "display:flex;flex-direction:column;gap:3px;background:var(--accent-soft);border-bottom:1px solid var(--accent-line);padding:9px 14px"
          : "display:flex;align-items:center;gap:10px;background:var(--accent-soft);border-bottom:1px solid var(--accent-line);padding:10px 22px",

        cameFromResult: st.cameFrom,
        fromCitation: st.fromCite || "",
        cameFromNote: st.fromCite ? "opened from citation " + st.fromCite + " · your answer is kept" : "opened from the answer · your answer is kept",
        backToResult: () => this.setState({ screen: "start", cameFrom: false }),
        /* Mail opened from Start - an answer's sources, or who is waiting on the reader - is an ordinary filtered
           list, with the way back to Start on it. */
        resultFilterOn: !!st.resultFilter,
        resultFilterCount: st.resultFilter ? st.resultFilter.ids.length + " messages" : "",
        resultFilterWhy: st.resultFilter ? (st.resultFilter.why || "behind the answer to “" + st.resultFilter.q + "”") : "",
        clearResultFilter: () => this.setState({ resultFilter: null }),
        backToResultFromList: () => this.setState({ screen: "start", resultFilter: null }),
        resultFilterStyle: "flex:0 0 auto;display:flex;flex-direction:column;gap:4px;background:var(--accent-soft);border-bottom:1px solid var(--accent-line);padding:" + (isMobile ? "10px 12px 6px" : "9px 12px 5px"),
        threads: THREADS.filter(t => (st.archived || []).indexOf(t.id) < 0 && (st.deleted || []).indexOf(t.id) < 0 && (!st.resultFilter || st.resultFilter.ids.indexOf(t.id) >= 0)).map(t => ({
          flagged: !!flags[t.id],
          isUnread: !!unreadMap[t.id],
          movedTo: movedMap[t.id] || "",
          showMoved: !!movedMap[t.id],
          swipeWrapStyle: "position:relative;overflow:hidden" +
            (rowAnim[t.id] === "in" ? ";animation:mfrowin 480ms cubic-bezier(.22,1,.36,1) both"
              : rowOut(t.id) ? ";animation:mfrowout 440ms cubic-bezier(.4,0,.2,1) both;pointer-events:none" : ""),
          swiping: isMobile && st.swipeId === t.id && Math.abs(st.swipeDx || 0) > 8,
          swipeBgStyle: "position:absolute;inset:0;display:flex;align-items:center;" +
            ((st.swipeDx || 0) < 0
              ? "justify-content:flex-end;padding-right:22px;background:var(--accent-soft);color:var(--accent-d);"
              : "justify-content:flex-start;padding-left:22px;background:var(--warn-soft);color:var(--warn-text);"),
          swipeIcon: (st.swipeDx || 0) < 0 ? "reply" : "archive",
          swipeLabel: (st.swipeDx || 0) < 0 ? "Reply" : "Archive",
          swipeIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:22px;line-height:1;margin-right:8px" + (Math.abs(st.swipeDx || 0) >= 96 ? ";font-variation-settings:'wght' 500" : ""),
          swipeTextStyle: "font-size:13px;font-weight:600;opacity:" + (Math.abs(st.swipeDx || 0) >= 96 ? "1" : "0.55"),
          aiStyle: isMobile ? "display:flex;align-items:center;gap:8px;font-size:13px;color:var(--muted);line-height:1.35" : "display:flex;align-items:center;gap:8px;font-size:12px;color:var(--muted)",
          from: t.from, org: t.org, time: t.time, subject: t.subject, ai: t.ai,
          hasAvatar: !!avatarOf(t.from), noAvatar: !avatarOf(t.from), initials: initialsOf(t.from),
          avatarStyle: "width:" + avSz + "px;height:" + avSz + "px;flex:0 0 " + avSz + "px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:" + (isMobile ? 12 : 11) + "px;letter-spacing:0.02em;background:var(--border2);color:var(--text2)",
          avatarImgStyle: "width:" + avSz + "px;height:" + avSz + "px;flex:0 0 " + avSz + "px;border-radius:50%;background-color:var(--border2);background-position:center;background-size:cover;background-repeat:no-repeat;background-image:url(" + avatarOf(t.from) + ");display:block",
          /* Unread is the weight of the words, as every mail client draws it: the sender and the subject in 600, a
             read row in 400. The colours are the read row's either way, so weight is the one signal. No dot. */
          fromStyle: (isMobile ? "flex:1;min-width:0;font-size:15px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap" : "font-size:14px;white-space:nowrap") + ";font-weight:" + (unreadMap[t.id] ? "600" : "400"),
          subjStyle: (isMobile ? "font-size:14px;color:var(--text2);overflow:hidden;text-overflow:ellipsis;white-space:nowrap" : "font-size:14px") + ";font-weight:" + (unreadMap[t.id] ? "600" : "400"),
          orgStyle: isMobile || compact ? "display:none" : "font-size:12px;color:var(--muted);white-space:nowrap",
          showAi: aiHints && !!t.ai,
          openAiSum: (e) => { if (e && e.stopPropagation) e.stopPropagation(); this.openAiSum(t.id); },
          aiBtnStyle: "width:" + (touch ? 32 : 24) + "px;height:" + (touch ? 32 : 24) + "px;flex:0 0 " + (touch ? 32 : 24) + "px;display:flex;align-items:center;justify-content:center;border-radius:8px;color:var(--muted);font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:" + (touch ? 20 : 16) + "px;line-height:1",
          selected: sel.indexOf(t.id) >= 0,
          select: (e) => this.pickThread(t.id, e),
          stripStyle: "position:relative;width:52px;height:52px;flex:0 0 52px;display:flex;align-items:center;justify-content:center;border-radius:14px;" +
            (t.id === st.threadId ? "background:var(--accent-soft);box-shadow:inset 0 0 0 2px var(--accent-line)" : ""),
          stripAvatarStyle: "width:36px;height:36px;flex:0 0 36px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:12px;letter-spacing:0.02em;background:var(--border2);color:var(--text2)" +
            (avatarOf(t.from) ? ";background-position:center;background-size:cover;background-repeat:no-repeat;background-image:url(" + avatarOf(t.from) + ")" : ""),
          stripInitials: avatarOf(t.from) ? "" : initialsOf(t.from),
          stripLabel: t.from + " - " + t.subject,
          longPress: (e) => { if (e && e.preventDefault) { e.preventDefault(); e.stopPropagation(); } this.armCtx(false); this.openMailMenu(t.id, e.clientX, e.clientY); },
          touchStart: (e) => {
            const p = e && e.touches && e.touches[0];
            if (isMobile) this.swipeStart(t.id, e);
            this.startLongPress(t.id, p ? p.clientX : 0, p ? p.clientY : 0);
          },
          touchMove: (e) => { this.cancelLongPress(); if (isMobile) this.swipeMove(t.id, e); },
          // Folded to a strip, the row answers a tap and its menu alone: too narrow to carry a swipe across.
          stripTouchStart: (e) => { const p = e && e.touches && e.touches[0]; this.startLongPress(t.id, p ? p.clientX : 0, p ? p.clientY : 0); },
          stripTouchEnd: () => this.cancelLongPress(),
          touchEnd: () => { this.cancelLongPress(); if (isMobile) this.swipeEnd(t.id); },
          dragStart: (e) => this.dragThreadsStart(t.id, e),
          dragEnd: this.dragThreadsEnd,
          rowStyle: rowStyleFor(!isMobile && t.id === st.threadId && !selectionOn, sel.indexOf(t.id) >= 0) +
            ((st.dragIds || []).indexOf(t.id) >= 0 ? "opacity:0.45;" : "") +
            (isMobile && st.swipeId === t.id
              ? "transform:translateX(" + (st.swipeDx || 0) + "px);"
              : "transform:translateX(0);transition:transform 160ms ease,background 120ms ease;") +
            rowWash(t.id),
          count: parseInt(((t.meta || "").match(/thread: (\d+)/) || [])[1] || "1", 10),
          multi: parseInt(((t.meta || "").match(/thread: (\d+)/) || [])[1] || "1", 10) > 1,
        })),
        activeSubject: thread.subject,
        activeMeta: thread.meta,
        activeState: thread.state.map((s, i) => {
          const src = (STATE_SRC[thread.id] || [])[i];
          const idx = typeof src === "number" ? Math.min(src, lastIdx) : lastIdx;
          return {
            label: s.label,
            value: s.value,
            srcLabel: "message " + (idx + 1) + " · " + msgs[idx].from.split(" ")[0],
            showSrc: msgs.length > 1,
            openSrc: () => this.revealMessage(thread.id, idx),
          };
        }),
        activeMessages: items,
        showExpandAll: false,
        showCitationTag: st.cameFrom && thread.id === "contoso",
        closeMailAnswer: () => this.setState({ mailAnswer: false }),
        mailAnswer: st.mailAnswer && thread.id === "contoso",
        aiHints,

        composeMode: st.replyMode || "new",
        composeKindLabel: composeKind,
        composeOpen: !!st.compose && (!tabsMode || composeTabActive) && !docInPane,
        composeExpanded: (!!st.compose && !st.composeMin) || composeInPane,
        composeChromeShown: !composeTabActive,
        openCompose: () => (tabsMode
          ? this.openTab("compose", { compose: true, composeMin: false, replyMode: null, replyThreadId: null, composeCtx: [], composeCtxBase: null, composeSubjEdit: undefined, closeAsk: false, sendAsk: null })
          : this.setState({ compose: true, composeMin: false, replyMode: null, replyThreadId: null, composeCtx: [], composeCtxBase: null, composeSubjEdit: undefined, closeAsk: false, sendAsk: null })),
        composeSubjValue: curSubj,
        onComposeSubj: (e) => this.setState({ composeSubjEdit: e.target.value }),
        closeCompose: () => (bodyTxt() || curSubj.trim() ? this.setState({ closeAsk: true }) : finishCompose()),
        keepEditing: () => this.setState({ closeAsk: false, sendAsk: null }),
        discardCompose: () => { finishCompose(); this.notify("Draft discarded", false); },
        saveDraftAndClose: () => { finishCompose(); this.notify("The message is waiting in Drafts.", { kind: "success", title: "Draft saved" }); },
        closeAskOpen: !!st.closeAsk,
        composeKeyDown: (e) => { if ((e.ctrlKey || e.metaKey) && e.key === "Enter") { e.preventDefault(); askSend(); } },
        /* A rewrite waiting in the composer holds the editor and stands in the foot in place of *Send*. */
        composeEditable: st.cChange ? "false" : "true",
        composeFootRef: this.composeFootRef,
        composeEditorScroll: () => { if (st.selBar && st.selBar.kind === "compose" && !this._passageDocked) this.closeSelBar(); },
        composeIdle: !st.cChange,
        composeHasChange: !!st.cChange,
        composeChangeBusy: !!(st.cChange && st.cChange.busy),
        composeChangeReady: !!(st.cChange && !st.cChange.busy),
        composeChangeNote: !st.cChange ? "" : st.cChange.busy
          ? "Rewriting " + st.cChange.pieces.length + (st.cChange.pieces.length === 1 ? " passage" : " passages") + " · " + st.cChange.label
          : st.cChange.label + " · the struck text stays until you keep the change",
        composeChangeIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:16px;line-height:1;flex:0 0 auto;color:var(--accent-d)" + (st.cChange && st.cChange.busy ? ";animation:mfspin 1.15s linear infinite" : ""),
        composeChangeIcon: st.cChange && st.cChange.busy ? "progress_activity" : "auto_awesome",
        composeChangeNoteStyle: "display:flex;align-items:center;gap:8px;min-width:0;flex:" + (isMobile ? "1 1 100%" : "1 1 220px") + ";font-size:13px;line-height:1.45;color:var(--accent-d);text-wrap:pretty",
        composeChangeActs: !(st.cChange && !st.cChange.busy) ? [] : [
          { label: "Keep the change", aria: "Keep the rewritten text in the message", primary: true, run: this.keepComposeChange },
          { label: "Try again", aria: "Ask the agent for another version of the same sentences", run: this.retryComposeChange },
          { label: "Undo", aria: "Leave the message as it was", run: this.dropComposeChange },
        ].map(c => ({ label: c.label, aria: c.aria, run: c.run,
          key: (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); c.run(); } },
          style: "display:inline-flex;align-items:center;justify-content:center;box-sizing:border-box;border-radius:8px;font-size:13px;white-space:nowrap;" + (touch ? "min-height:44px;padding:0 16px;" : "padding:8px 14px;")
            + (c.primary ? "font-weight:600;color:var(--onaccent);background:var(--accent);" : "color:var(--text2);border:1px solid var(--border2);") })),
        askSend: askSend,
        sendAskOpen: !!st.sendAsk,
        sendWarnings: ((st.sendAsk && st.sendAsk.warnings) || []).map(w => ({ text: w })),
        sendHasWarn: !!(st.sendAsk && st.sendAsk.warnings.length),
        sendConfirmLabel: st.sendAsk && st.sendAsk.warnings.length ? "Send anyway" : "Send",
        sendSummaryTo: composeTo.length ? composeTo.join(", ") : "- no recipient -",
        sendSummarySubj: curSubj.trim() || "- no subject -",
        confirmSend: () => {
          const to = composeTo.length ? composeTo.join(", ") : "no recipient";
          finishCompose();
          this.notifyTask({
            title: "Sending message…",
            body: "Do: " + to,
            cancelText: "The message is being sent. Aborting stops delivery and saves it as a draft.",
            doneTitle: "Message sent",
            doneBody: "Do: " + to,
            ms: 2400,
          });
        },
        toggleComposeMin: () => (composeInPane ? null : this.setState(s => ({ composeMin: !s.composeMin }))),
        composeShellStyle: composeInPane
          ? "flex:1;display:flex;flex-direction:column;min-height:0;min-width:0;background:var(--panel)"
          : isMobile
          ? "position:fixed;inset:0;z-index:90;display:flex;flex-direction:column;background:var(--panel)"
          : "position:fixed;right:26px;bottom:0;width:680px;max-width:calc(100vw - 52px);max-height:" + (low ? "calc(100dvh - 8px)" : tall ? "min(86dvh, 760px)" : "86dvh") + ";z-index:90;display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-bottom:none;border-radius:12px 12px 0 0;box-shadow:0 -1px 0 var(--inset) inset,0 18px 44px var(--sh-3)",
        composeTitle: (st.composeMin ? composeKind + " - collapsed" : composeKind),
        composeToChips: composeTo.map(n => ({ name: n })),
        ccOpen: !!st.ccOpen,
        ccTitle: st.ccOpen ? "Hide Cc and Bcc fields" : "Show Cc and Bcc fields",
        /* The phone's field labels take the width of the longest of them rather than a desktop column, so a recipient
           chip keeps one line beside its label. */
        /* A low window lowers the editor's floor so the foot with *Send* stays on screen; the editor scrolls itself. */
        composeEditorStyle: "flex:1;min-height:" + (low ? 72 : 170) + "px;overflow:auto;padding:16px 17px;border-left:2px solid transparent;font-size:15px;line-height:1.65;color:var(--text2);outline:none",
        composeHeadStyle: "display:flex;align-items:center;gap:12px;padding:" + (low ? "0 6px 0 15px" : "11px 15px") + ";background:var(--sub);border-bottom:1px solid var(--line);border-radius:12px 12px 0 0",
        /* In a low window the fields, the AI drafting and the editor scroll together and the foot with *Send* stays
           pinned under them; anywhere else the wrapper is not a box at all. */
        composeScrollStyle: low ? "flex:1;min-height:0;display:flex;flex-direction:column;overflow:auto" : "display:contents",
        threadSheetStyle: "display:flex;flex-direction:column;gap:12px;max-height:" + (low ? "calc(100dvh - 8px)" : tall ? "min(82dvh, 640px)" : "82dvh") + ";overflow:auto;background:var(--panel);border-top-left-radius:16px;border-top-right-radius:16px;padding:10px 16px 20px 16px",
        composeBodyStyle: "display:flex;flex-direction:column;min-height:0;flex:1;--compose-label:" + (isMobile ? 58 : 88) + "px",
        ccToggleStyle: "flex:0 0 auto;font-size:12px;white-space:nowrap;border-radius:8px;" + (touch ? "display:flex;align-items:center;min-height:44px;padding:0 10px;" : "padding:2px 8px;") + (st.ccOpen ? "color:var(--accent-d);background:var(--accent-soft)" : "color:var(--muted)"),
        toggleCc: () => this.setState(s => ({ ccOpen: !s.ccOpen })),
        composeCcChips: (st.composeCc || []).map((n, i) => ({ name: n, remove: () => this.setState(s => ({ composeCc: (s.composeCc || []).filter((_, j) => j !== i) })) })),
        composeBccChips: (st.composeBcc || []).map((n, i) => ({ name: n, remove: () => this.setState(s => ({ composeBcc: (s.composeBcc || []).filter((_, j) => j !== i) })) })),
        composeReplyToChips: (st.composeReplyTo || []).map((n, i) => ({ name: n, remove: () => this.setState(s => ({ composeReplyTo: (s.composeReplyTo || []).filter((_, j) => j !== i) })) })),
        replyToValue: st.replyToInput || "",
        replyToPlaceholder: (st.composeReplyTo || []).length ? "another address…" : "default " + DEFAULT_REPLY_TO,
        onReplyToInput: (e) => this.setState({ replyToInput: e.target.value }),
        onReplyToKey: (e) => {
          if (e.key !== "Enter" && e.key !== ",") return;
          e.preventDefault();
          const v = (this.state.replyToInput || "").trim();
          if (v) this.setState(s => ({ composeReplyTo: (s.composeReplyTo || []).concat(v), replyToInput: "" }));
        },
        ccValue: st.ccInput || "",
        bccValue: st.bccInput || "",
        onCcInput: (e) => this.setState({ ccInput: e.target.value }),
        onBccInput: (e) => this.setState({ bccInput: e.target.value }),
        onCcKey: (e) => {
          if (e.key !== "Enter" && e.key !== ",") return;
          e.preventDefault();
          const v = (this.state.ccInput || "").trim();
          if (v) this.setState(s => ({ composeCc: (s.composeCc || []).concat(v), ccInput: "" }));
        },
        onBccKey: (e) => {
          if (e.key !== "Enter" && e.key !== ",") return;
          e.preventDefault();
          const v = (this.state.bccInput || "").trim();
          if (v) this.setState(s => ({ composeBcc: (s.composeBcc || []).concat(v), bccInput: "" }));
        },
        composeToEmpty: composeTo.length === 0,
        composeSubjLine: composeSubj,
        minLabel: composeInPane ? "" : st.composeMin ? "expand_less" : "minimize",
        composeMinShown: !composeInPane && !isMobile,
        composeMinBtnStyle: "display:flex;align-items:center;justify-content:center;width:28px;height:28px;border-radius:8px;color:var(--muted)",
        composeCloseBtnStyle: "display:flex;align-items:center;justify-content:center;border-radius:" + (touch ? "8px;width:44px;height:44px;border:1px solid var(--line);" : "8px;width:28px;height:28px;") + "color:var(--muted)",
        composeChromeIconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:" + (touch ? 24 : 18) + "px;line-height:1",
        fmtBarShown: !touch || !!st.fmtBar,
        fmtToggleShown: touch,
        fmtToggleLabel: st.fmtBar ? "Hide formatting" : "Text formatting",
        fmtToggleIcon: st.fmtBar ? "expand_less" : "expand_more",
        toggleFmtBar: () => this.setState(s2 => ({ fmtBar: !s2.fmtBar })),
        fmtHintShown: !touch,
        fmtBarStyle: "display:flex;align-items:center;flex-wrap:wrap;border-bottom:1px solid var(--line2);" + (touch ? "gap:6px;padding:9px 12px" : "gap:2px;padding:6px 12px"),

        htmlOpen: !!st.htmlDoc && (!tabsMode || htmlInPane),
        htmlTitleText: st.htmlDoc ? st.htmlDoc.title : "",
        htmlMeta: st.htmlDoc ? st.htmlDoc.from + " · " + st.htmlDoc.when : "",
        htmlSrc: st.htmlDoc ? st.htmlDoc.src : "",
        closeHtml: () => (htmlInPane ? this.closeTab(st.activeTab) : this.setState({ htmlDoc: null })),
        htmlOverlayStyle: htmlInPane
          ? "flex:1;display:flex;min-height:0;min-width:0;background:var(--sub)"
          : isMobile
          ? "position:fixed;top:0;left:0;right:0;bottom:" + this.barH() + "px;z-index:88;display:flex;background:var(--sub)"
          : singlePane
          ? "position:fixed;inset:0;z-index:88;display:flex;background:var(--sub)"
          : "position:fixed;inset:0;z-index:88;display:flex;align-items:center;justify-content:center;padding:26px;background:var(--scrim)",
        htmlWindowStyle: htmlInPane || singlePane
          ? "flex:1;display:flex;flex-direction:column;min-width:0;min-height:0;background:var(--panel)"
          : "width:min(1000px,100%);height:" + (tall ? "min(90dvh,1100px,100%)" : "min(90dvh,100%)") + ";display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 26px 60px var(--scrim);overflow:hidden",
        htmlFrameStyle: "flex:1;min-height:0;width:100%;border:0;background:#ffffff" + (st.htmlLoading ? ";visibility:hidden" : ""),
        htmlLoading: !!st.htmlLoading,
        htmlSkelStyle: "position:absolute;inset:0;z-index:6;display:flex;flex-direction:column;gap:14px;background:#ffffff;padding:26px 30px",
        htmlSkelLines: [{ w: 46, h: 15 }, { w: 28, h: 10 }, { w: 0, h: 0 }, { w: 98, h: 11 }, { w: 94, h: 11 }, { w: 99, h: 11 }, { w: 62, h: 11 }, { w: 0, h: 0 }, { w: 100, h: 90 }, { w: 88, h: 11 }, { w: 51, h: 11 }].map(l => ({
          style: l.w
            ? "border-radius:6px;background:linear-gradient(90deg,#e9edf2 0%,#f6f8fa 42%,#e9edf2 78%);background-size:260% 100%;animation:mfshim 1.05s linear infinite;height:" + l.h + "px;width:" + l.w + "%"
            : "height:6px",
        })),
        docLoading: !!st.docLoading,
        docReady: !st.docLoading,
        docSkelStyle: "flex:0 0 auto;width:" + Math.round(620 * (st.docZoom || 1)) + "px;max-width:100%;min-height:" + Math.round(830 * (st.docZoom || 1)) + "px;display:flex;flex-direction:column;gap:15px;background:#ffffff;border:1px solid var(--line);border-radius:4px;box-shadow:0 10px 30px var(--sh-2);padding:44px 46px",
        docSkelLines: [{ w: 34, h: 10 }, { w: 72, h: 17 }, { w: 0, h: 0 }, { w: 97, h: 11 }, { w: 93, h: 11 }, { w: 99, h: 11 }, { w: 58, h: 11 }, { w: 0, h: 0 }, { w: 96, h: 11 }, { w: 90, h: 11 }, { w: 44, h: 11 }, { w: 0, h: 0 }, { w: 99, h: 11 }, { w: 66, h: 11 }].map(l => ({
          style: l.w
            ? "border-radius:6px;background:linear-gradient(90deg,#e9edf2 0%,#f6f8fa 42%,#e9edf2 78%);background-size:260% 100%;animation:mfshim 1.05s linear infinite;height:" + l.h + "px;width:" + l.w + "%"
            : "height:8px",
        })),
        docOpen: !!st.doc && (!tabsMode || docInPane),
        docOverlayStyle: docInPane
          ? "flex:1;display:flex;min-height:0;min-width:0;background:var(--sub)"
          : isMobile
          ? "position:fixed;top:0;left:0;right:0;bottom:" + this.barH() + "px;z-index:97;display:flex;background:var(--sub)"
          : "position:fixed;inset:0;z-index:97;display:flex;align-items:center;justify-content:center;padding:28px;background:var(--scrim)",
        docName: st.doc ? st.doc.name : "",
        docType: st.doc ? st.doc.type : "",
        docMeta: st.doc ? st.doc.size : "",
        closeDoc: () => (docInPane ? this.closeTab(st.activeTab) : this.setState({ doc: null })),
        docZoomLabel: Math.round((st.docZoom || 1) * 100) + "%",
        docZoomIn: () => this.setState(s2 => ({ docZoom: Math.min(1.6, (s2.docZoom || 1) + 0.15) })),
        docZoomOut: () => this.setState(s2 => ({ docZoom: Math.max(0.7, (s2.docZoom || 1) - 0.15) })),
        stopProp: (e) => { if (e && e.stopPropagation) e.stopPropagation(); },
        docWindowStyle: docInPane
          ? "flex:1;min-width:0;display:flex;flex-direction:column;background:var(--panel);overflow:hidden"
          : isMobile
          ? "flex:1;min-width:0;display:flex;flex-direction:column;background:var(--panel);overflow:hidden"
          : "width:min(940px,100%);height:min(88dvh,100%);display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 26px 60px var(--scrim);overflow:hidden",
        docBtnStyle: "display:flex;align-items:center;justify-content:center;color:var(--text2);border-radius:" + (touch ? "8px;width:44px;height:44px;font-size:15px;" : "8px;width:28px;height:28px;font-size:13px;"),
        docPageStyle: "flex:0 0 auto;width:" + Math.round(620 * (st.docZoom || 1)) + "px;max-width:100%;min-height:" + Math.round(830 * (st.docZoom || 1)) + "px;display:flex;flex-direction:column;background:#ffffff;color:#1a1f26;border-radius:4px;box-shadow:0 18px 40px var(--scrim);padding:" + Math.round(56 * (st.docZoom || 1)) + "px " + Math.round(58 * (st.docZoom || 1)) + "px",
        docLines: docContent(st.doc).map(l => ({
          text: l.text,
          style: l.h
            ? "font-size:" + Math.round(19 * (st.docZoom || 1)) + "px;font-weight:600;letter-spacing:-0.01em"
            : l.s
            ? "font-size:" + Math.round(12 * (st.docZoom || 1)) + "px;color:#7b8592;font-weight:600"
            : "font-size:" + Math.round(14 * (st.docZoom || 1)) + "px;line-height:1.65;color:#39414d;text-wrap:pretty",
        })),
        userMailboxes: MAILBOXES.map(mb => ({
          mail: mb.mail, dot: mb.dot, current: false, pick: null,
          style: "display:flex;align-items:center;gap:8px;padding:3px 0;color:var(--muted)",
        })),
        tabsRowAvailable: w >= 1180,
        tabsRowStyle: "display:flex;align-items:center;gap:10px;padding:10px 13px;font-size:13px" + (w >= 1180 ? "" : ";opacity:0.5"),
        tabsRowNote: w >= 1180 ? "" : "available on a wider screen",
        tabsSwitchStyle: "flex:0 0 auto;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (wantTabs ? "--accent" : "--border2") + ");justify-content:" + (wantTabs ? "flex-end" : "flex-start"),
        toggleTabsMode: () => (w >= 1180 ? this.setState({ workMode: wantTabs ? "classic" : "tabs" }) : null),
        denseKnobStyle: "width:13px;height:13px;border-radius:50%;background:var(--panel)",
        themeSegs: [["auto", "Auto"], ["light", "Light"], ["dark", "Dark"]].map(([v, label]) => ({
          label,
          pick: () => this.setState({ themePref: v, themeOverride: null }),
          style: "flex:1;text-align:center;font-size:12px;padding:5px 0;border-radius:8px;" + (themePref === v
            ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
            : "color:var(--muted);"),
        })),
        settingsScrimStyle: isMobile
          ? "position:fixed;top:0;left:0;right:0;bottom:" + this.barH() + "px;z-index:96;display:flex;background:var(--bg)"
          : "position:fixed;inset:0;z-index:96;display:flex;background:var(--bg)",
        appVersion: "v" + APP_VERSION,
        appBuild: APP_BUILD,
        /* Settings take the whole window at every size: the list of sections is meant to grow, and a
           dialog leaves no room for a navigation that grows with it. */
        /* Past 1440 px Settings stands as a 1440 px surface centred on the window rather than stretching to its edge. */
        settingsCardStyle: "flex:1;min-width:0;max-width:1440px;margin:0 auto;display:flex;flex-direction:column;background:var(--panel)" + (w > 1440 ? ";box-shadow:-1px 0 0 var(--line),1px 0 0 var(--line)" : ""),
        notifDevice: !!st.notifDevice,
        notifDeviceSwitchStyle: "flex:0 0 auto;margin-top:2px;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (st.notifDevice ? "--accent" : "--border2") + ");justify-content:" + (st.notifDevice ? "flex-end" : "flex-start"),
        toggleNotifDevice: () => this.setState(s => ({ notifDevice: !s.notifDevice })),
        notifDuration: st.notifDuration == null ? 5 : st.notifDuration,
        notifDurationLabel: (st.notifDuration == null ? 5 : st.notifDuration) + " s",
        onNotifDuration: (e) => {
          const n = parseInt(e.target.value, 10);
          this.setState({ notifDuration: isNaN(n) ? 5 : Math.max(1, Math.min(30, n)) });
        },
        settingsMainStyle: "flex:1;min-height:0;display:flex;" + (singlePane ? "flex-direction:column;" : ""),
        settingsNavShown: !singlePane,
        settingsStripShown: singlePane,
        settingsNavStyle: "flex:0 0 " + (narrow ? 220 : 264) + "px;min-height:0;overflow-y:auto;scrollbar-width:thin;display:flex;flex-direction:column;background:var(--sub);border-right:1px solid var(--line);padding:16px 12px 14px",
        settingsScrollStyle: "flex:1;min-width:0;min-height:0;overflow-y:auto;scrollbar-width:thin",
        settingsBodyStyle: singlePane
          ? "display:flex;flex-direction:column;gap:11px;padding:14px 16px 28px"
          : "display:flex;flex-direction:column;gap:12px;max-width:760px;padding:" + (narrow ? "26px 28px 40px" : "34px 48px 48px"),
        settingsHeadStyle: "flex:0 0 auto;display:flex;align-items:center;gap:10px;border-bottom:1px solid var(--line);background:var(--sub);" + (singlePane ? "padding:12px 12px 10px 16px" : "padding:10px 14px 10px 20px;min-height:36px"),
        settingsTitleStyle: singlePane ? "font-size:18px;font-weight:600" : "font-size:16px;font-weight:600",
        settingsQuery: st.settingsQuery || "",
        onSettingsQuery: (e) => this.setState({ settingsQuery: e.target.value }),
        tabProfil: (st.settingsTab || "profil") === "profil",
        tabApp: st.settingsTab === "app",
        ...(() => {
          const sections = [
            { v: "profil", label: "Profile", icon: "person", group: "You", hint: "Your name and photo, as the people you write to see them." },
            { v: "accounts", label: "Accounts", icon: "alternate_email", group: "Mail", hint: "The mail accounts your administrator set up, and how each one syncs, deletes and sends." },
            { v: "ai", label: "AI", icon: "auto_awesome", group: "Assistant", hint: "The language the assistant writes in, and what it may read." },
            { v: "notif", label: "Notifications", icon: "notifications", group: "Assistant", hint: "What reaches you, on which device, and for how long it stays." },
            { v: "app", label: "Application", icon: "tune", group: "This device", hint: "How messages are shown, and the layout this device remembers." },
          ];
          const cur = st.settingsTab || "profil";
          const q = (st.settingsQuery || "").trim().toLowerCase();
          const shown = sections.filter(x => !q || (x.label + " " + x.hint).toLowerCase().includes(q));
          const curSec = sections.find(x => x.v === cur) || sections[0];
          const item = (x) => ({
            label: x.label, icon: x.icon,
            pick: () => this.setState({ settingsTab: x.v }),
            navStyle: "display:flex;align-items:center;gap:11px;border-radius:8px;box-sizing:border-box;" + (touch ? "min-height:44px;padding:0 12px;" : "padding:8px 12px;") + "font-size:14px;" +
              (cur === x.v ? "background:var(--accent-soft);color:var(--accent-d);font-weight:600;" : "color:var(--text2);"),
            iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:19px;line-height:1;width:20px;text-align:center;" + (cur === x.v ? "" : "color:var(--muted);"),
          });
          return {
            settingsSectionTitle: curSec.label,
            settingsSectionHint: curSec.hint,
            settingsNavEmpty: shown.length === 0,
            settingsNavGroups: ["You", "Mail", "Assistant", "This device"]
              .map(g => ({ label: g, items: shown.filter(x => x.group === g).map(item) }))
              .filter(g => g.items.length),
          };
        })(),
        settingsTabs: [["profil", "Profile"], ["accounts", "Accounts"], ["ai", "AI"], ["notif", "Notifications"], ["app", "Application"]].map(([v, label]) => ({
          label,
          pick: () => this.setState({ settingsTab: v }),
          style: "flex:0 0 auto;white-space:nowrap;border-bottom:2px solid " +
            ((st.settingsTab || "profil") === v
              ? "var(--accent);color:var(--text);font-weight:600"
              : "transparent;color:var(--muted)") +
            (touch ? ";padding:14px 4px 12px;font-size:16px" : ";padding:10px 1px 8px;font-size:14px"),
        })),
        autoImages: !!st.autoImages,
        autoImagesSwitchStyle: "flex:0 0 auto;margin-top:2px;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (st.autoImages ? "--accent" : "--border2") + ");justify-content:" + (st.autoImages ? "flex-end" : "flex-start"),
        toggleAutoImages: () => this.setState(s => ({ autoImages: !s.autoImages })),
        autoExpand: !!st.autoExpand,
        autoExpandSwitchStyle: "flex:0 0 auto;margin-top:2px;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (st.autoExpand ? "--accent" : "--border2") + ");justify-content:" + (st.autoExpand ? "flex-end" : "flex-start"),
        toggleAutoExpand: () => this.setState(s2 => ({ autoExpand: !s2.autoExpand, histOpen: {} })),
        htmlModeWarn: st.msgView === "html",
        htmlAskOpen: !!st.htmlAsk,
        cancelHtmlAsk: () => this.setState({ htmlAsk: null }),
        confirmHtmlAsk: () => {
          const ask = st.htmlAsk;
          if (!ask) return;
          this.setState({ htmlAsk: null });
          if (this.tabsMode()) this.openTab(ask.key, { htmlDoc: ask.doc, htmlSrcMode: false, doc: null, htmlLoading: true }, { htmlDoc: ask.doc });
          else this.setState({ htmlDoc: ask.doc, htmlSrcMode: false, htmlLoading: true });
          this.loadHtml();
        },
        setViewAi: () => this.setState({ msgView: "ai" }),
        setViewSimple: () => this.setState({ msgView: "simple" }),
        setViewHtml: () => this.setState({ msgView: "html", htmlDoc: null }),
        viewAiStyle: segStyle(st.msgView === "ai"),
        viewSimpleStyle: segStyle(st.msgView !== "ai" && st.msgView !== "html"),
        viewHtmlStyle: segStyle(st.msgView === "html"),
        viewHint: st.msgView === "html"
          ? "Messages show the original as its sender wrote it, inline; the control on the message head goes."
          : st.msgView === "ai"
            ? "The same deterministic cleanup as Simplified, with AI deciding only what to keep and what to drop - it never rewrites a word, so the text stays exactly as the sender wrote it; only the presentation changes. Usually cleaner than plain Simplified, especially on newsletters and long reply chains."
            : "Messages are shown as cleaned-up text; the original is one control away on the message head.",
        telemetryOptOut: !!st.telemetryOptOut,
        telemetrySwitchStyle: "flex:0 0 auto;margin-top:2px;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (st.telemetryOptOut ? "--accent" : "--border2") + ");justify-content:" + (st.telemetryOptOut ? "flex-end" : "flex-start"),
        toggleTelemetry: () => this.setState(s => ({ telemetryOptOut: !s.telemetryOptOut })),
        ...(() => {
          const ENC = [["auto", "Auto"], ["none", "None"], ["starttls", "STARTTLS"], ["ssl", "SSL/TLS"]];
          const DEF = structuredClone(MAIL_ACCOUNTS);
          const accounts = st.accounts || DEF;
          const d = st.acctDraft || null;
          const upd = (k, v) => this.setState(s => ({ acctDraft: Object.assign({}, s.acctDraft, { [k]: v }), acctTest: null }));
          const test = st.acctTest || null;
          const missing = !d ? [] : [
            [(d.imapHost || "").trim(), "IMAP host"],
            [(d.imapPort || "").trim(), "IMAP port"],
            [(d.smtpHost || "").trim(), "SMTP host"],
            [(d.smtpPort || "").trim(), "SMTP port"],
            [((d.login || "").trim() || (d.mail || "").trim()), "login or e-mail address"],
            [(d.pass || "").trim(), "password"],
          ].filter(p => !p[0]).map(p => p[1]);
          const ev = k => e => upd(k, e.target.value);
          const canSave = !!d && !!(d.name || "").trim() && !!(d.mail || "").trim();
          const hardDel = d ? d.hardDelete !== false : true;
          const purgeGone = d ? d.purgeGone !== false : true;
          const saveSent = d ? d.saveSent !== false : true;
          const secretScan = d ? d.secretScan !== false : true;
          const sw = on => "flex:0 0 auto;margin-top:2px;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (on ? "--accent" : "--border2") + ");justify-content:" + (on ? "flex-end" : "flex-start");
          const FOLD = [["inbox", "Inbox", "inbox", "INBOX"], ["sent", "Sent", "send", "Sent"], ["drafts", "Drafts", "draft", "Drafts"], ["archive", "Archive", "archive", "Archive"], ["junk", "Spam", "report", "Junk"], ["trash", "Trash", "delete", "Trash"]];
          const DEFFOLD = { inbox: "INBOX", sent: "Sent", drafts: "Drafts", archive: "Archive", junk: "Junk", trash: "Trash" };
          const folders = (d && d.folders) || DEFFOLD;
          const updFolder = (k, v) => this.setState(s => ({
            acctDraft: Object.assign({}, s.acctDraft, { folders: Object.assign({}, (s.acctDraft || {}).folders || DEFFOLD, { [k]: v }) }),
          }));
          const btn = (kind) => "display:flex;align-items:center;justify-content:center;align-self:flex-start;gap:6px;padding:9px 16px;border-radius:8px;font-size:14px;font-weight:600;white-space:nowrap;" + kind;
          const onAiLang = v => this.setState({ aiLang: v });
          const aiLang = st.aiLang || "en";
          return {
            userMailboxes: accounts.map(a => ({
              mail: a.mail, dot: a.dot, current: false, pick: null,
              style: "display:flex;align-items:center;gap:8px;padding:3px 0;color:var(--muted)",
            })),
            tabAi: st.settingsTab === "ai",
            tabNotif: st.settingsTab === "notif",
            resetLayoutStyle: btn("background:var(--sub);border:1px solid var(--border2);color:var(--text2);"),
            resetLayoutDone: !!st.layoutReset,
            resetLayout: () => {
              this.setState({
                listW: null, historyPanelW: null,
                notifH: null, notifDrag: null, docZoom: null, layoutReset: true,
              });
              setTimeout(() => this.setState({ layoutReset: false }), 3200);
            },
            notifAutoClear: !!st.notifAutoClear,
            notifAutoClearSwitchStyle: sw(!!st.notifAutoClear),
            toggleNotifAutoClear: () => this.setState(s => ({ notifAutoClear: !s.notifAutoClear })),
            notifAutoClearAfter: st.notifAutoClearAfter || "24h",
            onNotifAutoClearAfter: e => this.setState({ notifAutoClearAfter: e.target.value }),
            notifAfterOptions: [["now", "Right away"], ["1h", "After 1 hour"], ["24h", "After 24 hours"], ["7d", "After 7 days"]].map(([v, label]) => ({ v, label })),
            aiLangOptions: [["en", "English"], ["pl", "Polski"]].map(([v, label]) => ({
              label,
              pick: () => onAiLang(v),
              style: "padding:7px 13px;border-radius:8px;font-size:14px;" + (aiLang === v
                ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
                : "color:var(--text2);border:1px solid var(--line);background:var(--sub);"),
            })),
            aiMatchReply: !!st.aiMatchReply,
            aiMatchSwitchStyle: "flex:0 0 auto;margin-top:2px;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (st.aiMatchReply ? "--accent" : "--border2") + ");justify-content:" + (st.aiMatchReply ? "flex-end" : "flex-start"),
            toggleAiMatch: () => this.setState(s => ({ aiMatchReply: !s.aiMatchReply })),
            tabAccounts: st.settingsTab === "accounts",
            acctListShown: st.settingsTab === "accounts" && !d,
            acctEditShown: st.settingsTab === "accounts" && !!d,
            acctCount: accounts.length === 1 ? "1 account" : accounts.length + " accounts",
            acctItems: accounts.map(a => ({
              name: a.name,
              mail: a.mail,
              servers: "IMAP " + a.imapHost + ":" + a.imapPort + "  ·  SMTP " + a.smtpHost + ":" + a.smtpPort,
              dotStyle: "flex:0 0 8px;width:8px;height:8px;border-radius:50%;margin-top:6px;background:" + a.dot,
              edit: () => this.setState({ acctDraft: Object.assign({}, a), acctPassShown: false }),
            })),
            acctBack: () => this.setState({ acctDraft: null }),
            acctTitle: d ? "Edit account" : "",
            acctIsExisting: !!d && !!d.id,
            acctName: d ? d.name : "",
            acctMail: d ? d.mail : "",
            acctLogin: d ? d.login : "",
            acctPass: d ? d.pass : "",
            acctImapHost: d ? d.imapHost : "",
            acctImapPort: d ? d.imapPort : "",
            acctImapEnc: d ? (d.imapEnc || "auto") : "auto",
            acctSmtpHost: d ? d.smtpHost : "",
            acctSmtpPort: d ? d.smtpPort : "",
            acctSmtpEnc: d ? (d.smtpEnc || "auto") : "auto",
            acctSecretScan: secretScan,
            acctSecretScanSwitchStyle: sw(secretScan),
            acctSecretScanWarn: secretScan,
            toggleAcctSecretScan: () => upd("secretScan", !secretScan),
            onAcctName: ev("name"),
            onAcctLogin: ev("login"),
            onAcctPass: ev("pass"),
            onAcctImapHost: ev("imapHost"),
            onAcctImapPort: ev("imapPort"),
            onAcctImapEnc: ev("imapEnc"),
            onAcctSmtpHost: ev("smtpHost"),
            onAcctSmtpPort: ev("smtpPort"),
            onAcctSmtpEnc: ev("smtpEnc"),
            encOptions: ENC.map(([v, label]) => ({ v, label })),
            acctTestStyle: btn("background:var(--sub);border:1px solid var(--border2);color:var(--text2);"),
            acctTestRun: () => this.setState({
              acctTest: missing.length
                ? { ok: false, title: "Cannot test the connection", text: "Fill in first: " + missing.join(", ") + "." }
                : { ok: true, title: "Connection works", text: "Signed in to " + (d.imapHost || "") + " and sent a test handshake to " + (d.smtpHost || "") + "." },
            }),
            acctTestShown: !!test,
            acctTestOk: !!test && test.ok,
            acctTestBad: !!test && !test.ok,
            acctTestTitle: test ? test.title : "",
            acctTestText: test ? test.text : "",
            acctEarliest: d ? (d.earliest || "") : "",
            onAcctEarliest: ev("earliest"),
            acctEarliestHint: d && d.earliest
              ? "Mail that arrived before this date stays on the mail server and is never downloaded - it will not show up in the list or in search."
              : "No limit: everything the mail server still holds gets downloaded.",
            acctEarliestClearShown: !!(d && d.earliest),
            acctEarliestClear: () => upd("earliest", ""),
            acctFolders: FOLD.map(([k, label, icon, ph]) => ({
              label, icon, ph,
              value: folders[k] || "",
              set: e => updFolder(k, e.target.value),
            })),
            acctHardDelete: hardDel,
            acctHardDeleteSwitchStyle: sw(hardDel),
            toggleAcctHardDelete: () => upd("hardDelete", !hardDel),
            acctHardDeleteHint: hardDel
              ? "Deleting removes the message from both the MailFathom server and the mail server it came from (IMAP). It is gone from every device and there is nothing left to restore."
              : "The message stays on both servers, only marked as deleted, and you can bring it back at any time.",
            acctPurgeGone: purgeGone,
            acctPurgeGoneSwitchStyle: sw(purgeGone),
            toggleAcctPurgeGone: () => upd("purgeGone", !purgeGone),
            acctPurgeGoneHint: purgeGone
              ? "A message removed in the IMAP source disappears from MailFathom on the next sync."
              : "A message removed in the IMAP source is only flagged as deleted in MailFathom and stays searchable.",
            acctSaveSent: saveSent,
            acctSaveSentSwitchStyle: sw(saveSent),
            toggleAcctSaveSent: () => upd("saveSent", !saveSent),
            acctPassType: st.acctPassShown ? "text" : "password",
            acctPassIcon: st.acctPassShown ? "visibility_off" : "visibility",
            toggleAcctPass: () => this.setState(s => ({ acctPassShown: !s.acctPassShown })),
            acctLoginHint: d && !(d.login || "").trim() ? "Empty = the e-mail address is used as the login." : "",
            acctSaveStyle: btn(canSave
              ? "background:var(--accent);color:var(--onaccent);"
              : "background:var(--border2);color:var(--panel);opacity:0.6;pointer-events:none;"),
            acctSave: () => {
              if (!canSave) return;
              this.setState(s => {
                const list = (s.accounts || DEF).slice();
                const dr = s.acctDraft;
                if (!dr) return { acctDraft: null };
                if (dr.id) {
                  const i = list.findIndex(a => a.id === dr.id);
                  if (i >= 0) list[i] = dr; else list.push(dr);
                } else list.push(Object.assign({}, dr, { id: "a" + Date.now() }));
                return { accounts: list, acctDraft: null };
              });
            },
            acctCancelStyle: btn("background:var(--sub);border:1px solid var(--border2);color:var(--text2);"),
          };
        })(),
        settingsOpen: !!st.settings,
        openSettings: () => this.setState({ settings: true, userMenu: false }),
        closeSettings: () => this.setState({ settings: false, acctDraft: null, acctDel: null }),
        langOptions: [["en", "English"], ["pl", "Polski"]].map(([v, label]) => ({
          label,
          pick: () => this.setState({ lang: v }),
          style: "padding:7px 13px;border-radius:8px;font-size:13px;" + ((st.lang || "en") === v
            ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
            : "color:var(--text2);border:1px solid var(--line);background:var(--sub);"),
        })),
        themeOptions: [["light", "Light"], ["dark", "Dark"]].map(([v, label]) => ({
          label,
          pick: () => this.setState({ themeOverride: v }),
          style: "flex:1;text-align:center;font-size:13px;padding:9px 12px;border-radius:8px;" + (theme === v
            ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
            : "color:var(--text2);border:1px solid var(--line);background:var(--sub);"),
        })),
        workModeOptions: [["classic", "Classic"], ["tabs", "Tabs"]].map(([v, label]) => ({
          label,
          pick: () => this.setState(s2 => ({
            workMode: v,
            tabs: v === "tabs" ? [{ key: "thread:" + s2.threadId, kind: "thread", id: s2.threadId }] : [],
            activeTab: v === "tabs" ? "thread:" + s2.threadId : null,
          })),
          style: "flex:1;text-align:center;font-size:13px;padding:9px 12px;border-radius:8px;" + ((st.workMode || "tabs") === v
            ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
            : "color:var(--text2);border:1px solid var(--line);background:var(--sub);"),
        })),
        workModeHint: tabsMode
          ? "Every open mail, attachment and new message gets its own tab above the content panel."
          : "Mail opens in the panel next to the list; a new message replaces the content panel.",
        tabsBarShown: tabsMode && tabItems.length > 0,
        noTabsOpen: emptyMail,
        emptyMailText: tabsMode
          ? "Pick a message from the list, write a new one or ask about the whole correspondence history - each opens as a tab."
          : "Pick a message from the list, write a new one or ask about the whole correspondence history.",
        openLastThread: () => this.openTab("thread:" + thread.id, { screen: "mail", threadId: thread.id, mailPane: "thread", threadOpen: true }),
        closeAllAsk: !!st.closeAllAsk,
        closeAllInfo: "Open tabs: " + (st.closeAllAsk ? st.closeAllAsk.count : 0) + ". An unsent draft will be discarded.",
        askCloseAll: () => this.setState({ closeAllAsk: { count: tabItems.length } }),
        cancelCloseAll: () => this.setState({ closeAllAsk: false }),
        confirmCloseAll: () => this.setState({ tabs: [], activeTab: null, compose: false, composeCtx: [], composeCtxBase: null, replyThreadId: null, closeAllAsk: false }),
        tabItems,
        workModeLabel: tabsMode ? "Mode: tabs" : "Mode: classic",
        toggleWorkMode: () => this.setState(s => ({
          workMode: (s.workMode || "tabs") === "tabs" ? "classic" : "tabs",
          userMenu: false,
          tabs: (s.workMode || "tabs") === "tabs" ? [] : [{ key: "thread:" + s.threadId, kind: "thread", id: s.threadId }],
          activeTab: (s.workMode || "tabs") === "tabs" ? null : "thread:" + s.threadId,
        })),
        bodyRef: this.bodyRef,
        fmtButtons: [
          { key: "b", label: "B", title: "Bold", style: "font-weight:700", run: () => this.exec("bold") },
          { key: "i", label: "I", title: "Italic", style: "font-style:italic;font-family:Georgia,serif", run: () => this.exec("italic") },
          { key: "u", label: "U", title: "Underline", style: "text-decoration:underline", run: () => this.exec("underline") },
          { key: "s", label: "S", title: "Strikethrough", style: "text-decoration:line-through", run: () => this.exec("strikeThrough") },
          { key: "ul", label: "•-", title: "Bulleted list", style: "", run: () => this.exec("insertUnorderedList") },
          { key: "ol", label: "1.", title: "Numbered list", style: "", run: () => this.exec("insertOrderedList") },
          { key: "q", label: "format_quote", title: "Quote", style: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1;", run: () => this.exec("formatBlock", "blockquote") },
          { key: "lnk", label: "link", title: "Insert link", style: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1;", run: () => this.exec("createLink", "https://") },
          { key: "clr", label: "format_clear", title: "Clear formatting", style: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1;", run: () => this.exec("removeFormat") },
        ].map(b => ({ key: b.key, label: b.label, title: b.title, style: "display:flex;align-items:center;justify-content:center;border-radius:" + (touch ? "8px;min-width:44px;height:44px;padding:0 9px;font-size:15px;border:1px solid var(--line);" : "8px;min-width:30px;height:30px;padding:0 7px;font-size:13px;") + "color:var(--text2);" + b.style, run: b.run })),
        aiActions: [
          { label: "Write a draft", run: () => this.aiWrite("full") },
          { label: "Shorten", run: () => this.aiWrite("short") },
          { label: "Expand", run: () => this.aiWrite("full") },
          { label: "Fix the language", run: () => this.aiWrite("full") },
        ].map(a => ({
          label: a.label, run: a.run,
          style: "color:var(--accent-d);background:var(--panel);border:1px solid var(--accent-line);border-radius:" + (touch ? 18 : 16) + "px;white-space:nowrap;" + (touch ? "display:flex;align-items:center;box-sizing:border-box;min-height:36px;padding:0 14px;font-size:13px" : "padding:6px 12px;font-size:12px"),
        })),
        tones: ["Formal", "Neutral", "Direct"].map(t => ({
          label: t,
          pick: () => this.setState({ tone: t }),
          style: "border-radius:" + (touch ? 18 : 12) + "px;white-space:nowrap;" + (touch ? "display:flex;align-items:center;box-sizing:border-box;min-height:36px;padding:0 14px;font-size:13px;" : "padding:5px 11px;font-size:12px;") + ((st.tone || "Neutralny") === t
            ? "background:var(--accent);color:var(--onaccent);"
            : "color:var(--text2);border:1px solid var(--line);"),
        })),
        aiBusy: !!st.aiBusy,
        aiBusyLabel: rmode ? "Generating a reply" : "Drafting",
        aiDraftShown: !!st.aiDraft,
        aiRevert: this.aiRevert,
        acceptDraft: () => { this.unmarkAiDraft(); this.setState({ aiDraft: false }); },
        /* The AI's context: one chip per message, the starting point the warning is measured against, and
           *Restore* to go back to it. */
        composeCtxChips: composeCtx.map(id => ({
          subject: ctxSubject(id),
          remove: () => this.setState({ composeCtx: composeCtx.filter(x => x !== id) }),
          style: "display:flex;align-items:center;gap:5px;max-width:" + (isMobile ? 220 : 260) + "px;box-sizing:border-box;background:var(--panel);border:1px solid var(--accent-line);border-radius:6px;color:var(--text2);font-size:12px;" +
            (touch ? "min-height:36px;padding:0 2px 0 9px" : "padding:2px 3px 2px 8px"),
          removeStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;border-radius:4px;color:var(--faint);font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;" +
            (touch ? "width:32px;height:32px;font-size:18px" : "width:18px;height:18px;font-size:15px"),
        })),
        composeCtxEmpty: !composeCtx.length,
        composeCtxHint: isMobile ? "none - the AI drafts from what you write above" : "none yet - click a message in the list to draft from it",
        composeCtxSteady: !composeCtxChanged,
        composeCtxChanged,
        composeCtxBaseLabel: composeCtxBase ? ctxSubject(composeCtxBase[0]) + (composeCtxBase.length > 1 ? " and " + (composeCtxBase.length - 1) + " more" : "") : "",
        restoreComposeCtx: () => this.setState({ composeCtx: (composeCtxBase || []).slice() }),
        ctxRestoreStyle: "flex:0 0 auto;display:flex;align-items:center;gap:5px;font-weight:600;color:var(--warn-text);border-radius:6px;" +
          (touch ? "min-height:44px;padding:0 10px;font-size:13px" : "padding:3px 8px;font-size:12px"),
      };
      /* What back calls for a surface whose own close does more than empty its key: asking before a draft is thrown
         away, and closing the tab the surface stands in. */
      Object.assign(vals, MailFathomDesign.fathomUi.vals(this, { st, isMobile, touch, singlePane, narrow, thread, person }));
      this._ownClose = { compose: vals.closeCompose, doc: vals.closeDoc, htmlDoc: vals.closeHtml };
      return vals;
    }
  }

  return Component;
};
