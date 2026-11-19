// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

/* TASKS AS COMMITMENTS
   A task runs one of two ways — something I owe, or something I am waiting for — lives in a context, a thread or an
   event, and is with a person. It has a due day and, separately, a planned time on the calendar. This file holds everything
   the prototype draws about tasks wherever they appear: the Tasks space and its four views, the task dialog, planning
   a time, the commitments a thread, a person and an event carry, and what the calendar draws for them. The component
   spreads `props(self, ctx)` into its render. */
MailFathomDesign.tasksLogic = (() => {
  const D = MailFathomDesign.data;
  const TODAY = "08-27";
  const SYM = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;";
  const ANCHOR_GLYPH = { thread: "mail", event: "event" };
  /* Who a task is with, by name: what a task anchored to nothing shows in the anchor's place. */
  const personName = (pid) => ((D.contacts || []).find(c => c.id === pid) || {}).name || "";
  const toMin = (time) => { const m = /^(\d{1,2}):(\d{2})/.exec(time || ""); return m ? +m[1] * 60 + +m[2] : null; };
  const hhmm = (min) => String(Math.floor(min / 60)).padStart(2, "0") + ":" + String(min % 60).padStart(2, "0");
  const dayLabel = (on) => !on ? "" : on === TODAY ? "today" : on === "08-28" ? "tomorrow" : on.slice(3) + "." + on.slice(0, 2);
  const DAY_CHIPS = [
    { on: "08-27", label: "Today" }, { on: "08-28", label: "Tomorrow" }, { on: "08-31", label: "Mon 31.08" }, { on: "09-01", label: "Tue 01.09" },
  ];
  const LENGTHS = [15, 30, 45, 60, 90, 120];
  const lenLabel = (m) => m < 60 ? m + " min" : m % 60 ? Math.floor(m / 60) + " h " + (m % 60) + " min" : (m / 60) + (m === 60 ? " hr" : " hrs");
  /* Every way of asking to lay out today — *Lay out today*, the toolbar's *Lay out the day*, the calendar's *Plan my day*
     — counts against one allowance: the third ask of a session finds it spent. */
  const askLayoutFor = (self) => self.setState(s2 => {
    const n = (s2.layoutAsks || 0) + 1;
    return n >= 3 ? { layoutAsks: n, layoutSpent: true, layoutOffer: false, taskView: "today" } : { layoutAsks: n, layoutSpent: false, layoutOffer: true, taskView: "today" };
  });
  const VIEWS = [
    { id: "today", label: "Today" }, { id: "upcoming", label: "Upcoming" }, { id: "waiting", label: "Waiting for" }, { id: "proposed", label: "Proposed" },
  ];

  /* The tasks as the person's state has them: the sample, the accepted proposals, the ones made here, each with the
     edits made to it laid over it. */
  const model = (st, ctx) => {
    const over = st.taskOver || {};
    const accepted = st.taskAccepted || [];
    const apply = (t) => Object.assign({}, t, over[t.id] || {});
    const tasks = D.tasks
      .concat(D.proposedTasks.filter(p => accepted.indexOf(p.id) >= 0).map(p => Object.assign({}, p, { dir: "owe", origin: "proposal" })))
      .concat(st.extraTasks || [])
      .map(apply)
      .filter(t => !ctx.hid("tasks", t.id));
    const proposals = D.proposedTasks.filter(p => accepted.indexOf(p.id) < 0 && !ctx.hid("tasks", p.id)).map(apply);
    const replied = (t) => !!t.replied && (st.taskReopen || []).indexOf(t.id) < 0;
    const done = (t) => (st.taskDone || []).indexOf(t.id) >= 0 || replied(t);
    const meetingsOn = (on) => {
      if (!on || on.slice(0, 2) !== "08") return [];
      const d = ctx.calAllDays.find(x => x.d === on.slice(3));
      return d ? d.ev.filter(e => e.kind === "meeting" && toMin(e.time) !== null) : [];
    };
    const span = (t) => {
      if (!t.plan) return null;
      const s = toMin(t.plan.time);
      return { s, e: s + (t.mins || 30) };
    };
    const clash = (t) => {
      const sp = span(t);
      if (!sp || done(t)) return null;
      return meetingsOn(t.plan.on).find(m => { const ms = toMin(m.time); return sp.s < ms + 60 && ms < sp.e; }) || null;
    };
    const planLabel = (t) => {
      const sp = span(t);
      if (!sp) return "";
      return hhmm(sp.s) + "–" + hhmm(sp.e) + (t.plan.on === TODAY ? "" : " · " + dayLabel(t.plan.on));
    };
    const find = (id) => tasks.find(t => t.id === id) || proposals.find(p => p.id === id) || null;
    return { tasks, proposals, done, replied, clash, planLabel, find, meetingsOn, span };
  };

  const props = (self, ctx) => {
    const st = ctx.st || self.state;
    const { touch, isMobile, compact, tbFull, tbBtn, tbIcon, isSel, selMark } = ctx;
    const m = model(st, ctx);
    const view = st.taskView || "today";
    const showDone = !!st.showDone;
    const setOver = (id, patch) => self.setState(s2 => ({ taskOver: Object.assign({}, s2.taskOver || {}, { [id]: Object.assign({}, (s2.taskOver || {})[id] || {}, patch) }) }));
    const toggleDone = (t) => self.setState(s2 => ({
      taskDone: (s2.taskDone || []).indexOf(t.id) >= 0 ? (s2.taskDone || []).filter(x => x !== t.id) : (s2.taskDone || []).concat([t.id]),
    }));
    const openTask = (id) => self.setState({ taskOpen: id, taskAnchorPick: false, taskDlgSaid: null, taskDlgSaving: false, event: null, notifCenter: false });
    const openPlan = (id) => {
      const t = m.find(id);
      self.setState({ taskPlanFor: id, planPick: { on: (t && t.plan && t.plan.on) || (t && t.on && t.on >= TODAY ? t.on : TODAY), time: t && t.plan ? t.plan.time : null, mins: (t && t.mins) || 30 } });
    };
    const openAnchor = (a) => {
      if (!a) return;
      /* What a task belongs to can be gone since: a thread no longer in the mailbox is said on Tasks' status line, an
         event no longer on the calendar on the calendar's. */
      if (a.kind === "thread") {
        if (!(D.threads || []).some(t2 => t2.id === a.tid)) { self.setState({ taskOpen: null, taskSaid: "The thread could not be opened." }); return; }
        self.setState({ taskOpen: null, screen: "mail", threadId: a.tid, mailPane: "thread", threadOpen: true, cameFrom: false });
      }
      else {
        const ev = (ctx.calAllDays.find(d => d.d === a.day) || { ev: [] }).ev.map(e => Object.assign({ day: a.day }, e)).find(e => e.t === a.label) || null;
        self.setState({ taskOpen: null, screen: "cal", event: ev, calSaid: ev ? null : "That event is no longer on the calendar." });
      }
    };
    const followUp = (t) => {
      if (t.anchor && t.anchor.kind === "thread") { self.setState({ taskOpen: null }); self.startReply("reply", [t.anchor.tid]); }
    };
    const accept = (p) => {
      self.setState(s2 => ({ taskAccepted: (s2.taskAccepted || []).concat([p.id]) }));
      self.notify("“" + p.t + "” is in " + (p.on && p.on <= TODAY ? "Today" : "Upcoming") + ".", { kind: "success", title: "Task added" });
    };
    const dismiss = (p) => self.setState(s2 => ({ hidden: Object.assign({}, s2.hidden || {}, { tasks: ((s2.hidden || {}).tasks || []).concat([p.id]) }) }));

    const chip = "display:inline-flex;align-items:center;gap:5px;min-width:0;max-width:100%;white-space:nowrap;border-radius:999px;" +
      (touch ? "font-size:12px;padding:0 10px;min-height:28px;" : "font-size:11px;padding:2px 8px;");
    const row = (t, kind) => {
      const done = kind !== "proposal" && m.done(t);
      const cl = kind === "owe" ? m.clash(t) : null;
      const remN = self.remList(t.id).length;
      const a = t.anchor;
      const repliedNow = kind === "wait" && m.replied(t);
      const follow = kind === "wait" && !done && t.on && t.on <= TODAY;
      const menu = kind === "proposal" ? [
        { icon: "add_task", label: "Accept", run: () => accept(t) },
        { icon: "close", label: "Dismiss task", run: () => dismiss(t) },
      ] : [
        { icon: "open_in_full", label: "Open task", run: () => openTask(t.id) },
        { icon: done ? "radio_button_unchecked" : "check_circle", label: done ? "Mark as not done" : "Mark as done", run: () => toggleDone(t) },
      ].concat(kind === "owe" ? [{ icon: "calendar_add_on", label: t.plan ? "Move in the calendar" : "Plan in the calendar", run: () => openPlan(t.id) }] : [])
        .concat(follow ? [{ icon: "reply", label: "Follow up", run: () => followUp(t) }] : [])
        .concat([{ icon: "notifications_active", label: "Reminders", run: () => self.setState({ remFor: { key: t.id, title: t.t, timed: false, kind: "task", hint: "before 09:00 · " + (t.when || "no day"), src: "Tasks" } }) }])
        .concat(a ? [{ icon: ANCHOR_GLYPH[a.kind], label: a.kind === "thread" ? "Open the thread" : "Open the event", run: () => openAnchor(a) }] : [])
        .concat([{ icon: "check_box", label: "Select tasks", run: () => self.selStart("tasks", t.id) },
          { icon: "delete", label: "Delete task", danger: true, run: () => self.askHide("tasks", [t.id], "Delete task?", t.t + " will disappear from your tasks.") }]);
      const dueText = kind === "wait"
        ? (repliedNow ? t.replied : (follow ? "follow up today" : "follow up " + t.when))
        : (t.when || "no day");
      return {
        t: t.t, kind,
        showBox: kind !== "proposal",
        showAi: kind === "proposal",
        aiLabel: t.from === "From the Agent" ? "Agent" : "AI",
        fromLabel: kind === "proposal" ? t.from : "",
        boxGlyph: done ? "check" : kind === "wait" ? "hourglass_top" : "",
        boxTitle: kind === "wait" ? (done ? "Mark as still waiting" : "Mark as received") : (done ? "Mark as not done" : "Mark as done"),
        boxStyle: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;border-radius:" + (kind === "wait" ? "50%" : "8px") + ";" + SYM +
          (touch ? "width:26px;height:26px;font-size:17px;" : "width:20px;height:20px;font-size:14px;") +
          (done ? "border:1px solid var(--accent);background:var(--accent);color:var(--onaccent);"
            : kind === "wait" ? "border:1px dashed var(--warn);color:var(--warn);"
            : "border:1px solid var(--border2);color:transparent;"),
        toggle: (e) => { if (e) e.stopPropagation(); toggleDone(t); },
        anchorShown: !!a,
        anchorGlyph: a ? ANCHOR_GLYPH[a.kind] : "",
        anchorLabel: a ? a.label : "",
        anchorTitle: a ? (a.kind === "thread" ? "Open the thread" : "Open the event") : "",
        /* With no anchor, the person it is with stands in the chip's place: a name, not a control. */
        personShown: !a && !!t.person && !!personName(t.person),
        personLabel: personName(t.person),
        anchorStyle: chip + "overflow:hidden;text-overflow:ellipsis;color:var(--accent-d);background:var(--accent-soft);",
        openAnchor: (e) => { if (e) e.stopPropagation(); openAnchor(a); },
        estShown: kind !== "wait" && !t.plan && !!t.est,
        est: t.est || "",
        planShown: kind === "owe" && !!t.plan,
        planLabel: m.planLabel(t),
        planStyle: chip + "font-family:'Geist',system-ui,sans-serif;color:var(--text2);border:1px dashed var(--accent);background:var(--panel);",
        planBtnShown: kind === "owe" && !t.plan && !done,
        planBtnStyle: "border-radius:12px;white-space:nowrap;color:var(--accent-d);border:1px solid var(--accent-line);background:var(--accent-soft);" +
          (touch ? "font-size:13px;padding:0 14px;min-height:44px;display:flex;align-items:center;" : "font-size:11px;padding:4px 10px;"),
        plan: (e) => { if (e) e.stopPropagation(); openPlan(t.id); },
        clashShown: !!cl,
        clashLabel: cl ? "Clashes with " + cl.t + " " + cl.time : "",
        clashStyle: chip + "color:var(--err-text);background:var(--err-soft, var(--hover));border:1px solid var(--err);",
        dueText,
        dueStyle: "font-size:" + (touch ? 13 : 12) + "px;white-space:nowrap;color:" + (kind === "wait" && follow ? "var(--warn)" : repliedNow ? "var(--ok, var(--accent-d))" : "var(--text2)"),
        followShown: follow,
        follow: (e) => { if (e) e.stopPropagation(); followUp(t); },
        followStyle: "border-radius:12px;white-space:nowrap;color:var(--onaccent);background:var(--accent);" +
          (touch ? "font-size:13px;padding:0 14px;min-height:44px;display:flex;align-items:center;" : "font-size:11px;padding:5px 10px;"),
        acceptShown: kind === "proposal",
        accept: (e) => { if (e) e.stopPropagation(); accept(t); },
        dismiss: (e) => { if (e) e.stopPropagation(); dismiss(t); },
        acceptStyle: "border-radius:12px;white-space:nowrap;color:var(--onaccent);background:var(--accent);" +
          (touch ? "font-size:13px;padding:0 14px;min-height:44px;display:flex;align-items:center;" : "font-size:11px;padding:5px 10px;"),
        dismissStyle: "display:flex;align-items:center;justify-content:center;border-radius:" + (touch ? 12 : 8) + "px;color:var(--muted);" + SYM +
          (touch ? "width:44px;height:44px;font-size:20px;" : "width:26px;height:26px;font-size:16px;"),
        remShown: kind !== "proposal" && !repliedNow,
        remIcon: remN ? "notifications_active" : "notifications",
        remCount: remN > 1 ? String(remN) : "",
        remTitle: remN ? "Reminders: " + remN : "Add a reminder",
        remStyle: "display:flex;align-items:center;gap:4px;border-radius:8px;font-weight:600;border:1px solid " +
          (remN ? "var(--accent-line);background:var(--accent-soft);color:var(--accent-d);" : "var(--line);background:var(--sub);color:var(--muted);") +
          (touch ? "height:44px;padding:0 12px;font-size:12px;" : "height:26px;padding:0 7px;font-size:11px;"),
        remIconStyle: SYM + "font-size:" + (touch ? 20 : 15) + "px",
        openRem: (e) => { if (e) e.stopPropagation(); self.setState({ remFor: { key: t.id, title: t.t, timed: false, kind: "task", hint: "before 09:00 · " + (t.when || "no day"), src: "Tasks" } }); },
        press: self.pressFor("tasks", t.id, t.t, menu, undefined, a ? a.label : (personName(t.person) || undefined)),
        tap: kind === "proposal" ? () => {} : self.rowTap("tasks", t.id, () => openTask(t.id)),
        draggable: !touch && kind === "owe" && !done,
        dragStart: (e) => { self.dragTaskId = t.id; try { e.dataTransfer.setData("text/plain", t.t); e.dataTransfer.effectAllowed = "move"; } catch (x) { /* a design page only */ } },
        dragEnd: () => { self.dragTaskId = null; self.setState({ taskDropAt: null }); },
        rowStyle: (touch
          ? "display:flex;flex-direction:column;align-items:stretch;gap:9px;padding:12px 14px;"
          : "display:flex;align-items:center;gap:12px;padding:9px 14px;min-height:44px;box-sizing:border-box;") +
          "background:var(--panel);border:1px solid " + (cl ? "var(--err)" : "var(--line)") + ";border-radius:12px;" + (done ? "opacity:0.6;" : "") + selMark("tasks", t.id) +
          (!touch && kind === "owe" && !done ? "cursor:grab;" : ""),
        mainStyle: "display:flex;align-items:center;gap:" + (touch ? 12 : 10) + "px;flex:1;min-width:0",
        titleStyle: "min-width:0;line-height:1.4;font-size:" + (touch ? 15 : 14) + "px;" + (touch ? "text-wrap:pretty;" : "overflow:hidden;text-overflow:ellipsis;white-space:nowrap;") +
          (done ? "text-decoration:line-through;color:var(--muted);" : ""),
        metaStyle: touch
          ? "display:flex;align-items:center;gap:8px;flex-wrap:wrap;padding-left:38px"
          : "display:flex;align-items:center;gap:8px;flex:0 0 auto",
      };
    };

    const visible = (t) => showDone || !m.done(t);
    const owe = m.tasks.filter(t => t.dir === "owe");
    const wait = m.tasks.filter(t => t.dir === "wait");
    const inToday = (t) => (t.on && t.on <= TODAY) || (t.plan && t.plan.on === TODAY);
    const byPlan = (a, b) => toMin(a.plan.time) - toMin(b.plan.time);
    const groups = [];
    const push = (label, items, kind, note) => { if (items.length) groups.push({ label, count: items.length, note: note || "", hasNote: !!note, items: items.map(t => row(t, kind)) }); };
    if (view === "today") {
      push("Planned", owe.filter(t => visible(t) && t.plan && t.plan.on === TODAY).sort(byPlan), "owe");
      push("Due today, not planned", owe.filter(t => visible(t) && inToday(t) && !(t.plan && t.plan.on === TODAY)), "owe");
      push("Follow up", wait.filter(t => !m.done(t) && t.on && t.on <= TODAY), "wait");
    } else if (view === "upcoming") {
      const later = owe.filter(t => visible(t) && !inToday(t));
      push("Tomorrow", later.filter(t => t.on === "08-28" || (!t.on && t.plan && t.plan.on === "08-28")), "owe");
      push("This week", later.filter(t => t.on && t.on > "08-28" && t.on <= "09-02"), "owe");
      push("Later", later.filter(t => t.on && t.on > "09-02"), "owe");
      push("No day", later.filter(t => !t.on && !(t.plan && t.plan.on === "08-28")), "owe");
    } else if (view === "waiting") {
      push("Follow up today", wait.filter(t => !m.done(t) && t.on && t.on <= TODAY), "wait");
      push("Waiting", wait.filter(t => !m.done(t) && !(t.on && t.on <= TODAY)), "wait");
      push("Closed by a reply", wait.filter(t => m.replied(t)), "wait", "A waiting task closes itself when the reply arrives.");
      if (showDone) push("Received", wait.filter(t => m.done(t) && !m.replied(t)), "wait");
    } else {
      push("Today", m.proposals.filter(p => p.on && p.on <= TODAY), "proposal");
      push("Later", m.proposals.filter(p => p.on && p.on > TODAY), "proposal");
      push("No day", m.proposals.filter(p => !p.on), "proposal");
    }

    const counts = {
      today: owe.filter(t => !m.done(t) && inToday(t)).length + wait.filter(t => !m.done(t) && t.on && t.on <= TODAY).length,
      upcoming: owe.filter(t => !m.done(t) && !inToday(t)).length,
      waiting: wait.filter(t => !m.done(t)).length,
      proposed: m.proposals.length,
    };
    const empty = {
      today: ["Nothing due or planned today", "Upcoming holds what is due later."],
      upcoming: ["Nothing coming up", "A task with a later day, or with no day, waits here."],
      waiting: ["You are not waiting on anybody", "Send a message with “Remind me if there is no reply”, and it waits here until the reply comes."],
      proposed: ["No proposals", "Tasks the AI finds in your mail, and the ones the Agent suggests, wait here for you to accept."],
    }[view];

    /* Today's plan, beside the list: meetings and planned tasks hour by hour, every hour a place to drop a task. */
    const timeline = (on, opt) => {
      const o = opt || {};
      const meets = m.meetingsOn(on);
      const planned = owe.filter(t => t.plan && t.plan.on === on && !m.done(t) && t.id !== o.exceptId);
      return [8, 9, 10, 11, 12, 13, 14, 15, 16, 17].map(h => {
        const items = meets.filter(e => Math.floor(toMin(e.time) / 60) === h).map(e => ({
          t: e.t, time: e.time, glyph: "groups",
          style: "display:flex;align-items:center;gap:7px;min-width:0;border-radius:8px;padding:5px 9px;font-size:12px;background:var(--rail);border:1px solid var(--line);color:var(--text2)",
          open: () => self.setState({ event: Object.assign({ day: on.slice(3) }, e) }),
        })).concat(planned.filter(t => Math.floor(toMin(t.plan.time) / 60) === h).map(t => ({
          t: t.t, time: m.planLabel(t), glyph: "check_box_outline_blank",
          style: "display:flex;align-items:center;gap:7px;min-width:0;border-radius:8px;padding:5px 9px;font-size:12px;background:var(--panel);border:1px dashed " + (m.clash(t) ? "var(--err)" : "var(--accent)") + ";color:var(--text)",
          open: () => openTask(t.id),
        })));
        const picked = o.pickTime && Math.floor(toMin(o.pickTime) / 60) === h;
        if (picked) items.push({ t: o.pickTitle, time: o.pickLabel, glyph: "check_box_outline_blank", open: () => {},
          style: "display:flex;align-items:center;gap:7px;min-width:0;border-radius:8px;padding:5px 9px;font-size:12px;background:var(--accent-soft);border:1px solid var(--accent);color:var(--text);font-weight:600" });
        const label = (h < 10 ? "0" : "") + h + ":00";
        return {
          label, items, free: !items.length,
          pick: o.onPick ? () => o.onPick(label) : () => {},
          dragOver: (e) => { if (self.dragTaskId) { e.preventDefault(); if (st.taskDropAt !== label) self.setState({ taskDropAt: label }); } },
          drop: (e) => {
            e.preventDefault();
            const id = self.dragTaskId;
            self.dragTaskId = null;
            if (!id) return;
            setOver(id, { plan: { on, time: label, h } });
            self.setState({ taskDropAt: null });
            const t = m.find(id);
            self.notify((t ? "“" + t.t + "” " : "") + "planned at " + label + ".", { kind: "success", title: "Planned" });
          },
          rowStyle: "display:flex;gap:10px;align-items:flex-start;border-top:1px solid var(--line2);padding:5px 0;min-height:" + (touch ? 44 : 30) + "px;box-sizing:border-box;" +
            (o.onPick ? "cursor:pointer;" : "") +
            (st.taskDropAt === label && !o.onPick ? "background:var(--accent-soft);box-shadow:inset 0 0 0 1px var(--accent);border-radius:8px;" : ""),
        };
      });
    };

    /* *Lay out today* proposes; nothing moves until *Plan these*. */
    const layoutPlan = [
      { id: "t10", time: "10:00", h: 10, note: "" },
      { id: "t2", time: "16:00", h: 16, note: "moved from 15:00: it clashed with Rollout status with Piotr" },
    ].filter(p => { const t = m.find(p.id); return t && !m.done(t) && !(t.plan && t.plan.time === p.time && t.plan.on === TODAY); });
    const dueTodayN = owe.filter(t => !m.done(t) && t.on === TODAY).length;
    const eventsTodayN = (ctx.calAllDays.find(d => d.d === TODAY.slice(3)) || { ev: [] }).ev.filter(e => !e.taskId).length;
    const askLayout = () => askLayoutFor(self);

    /* The open task. */
    const ot = st.taskOpen ? m.find(st.taskOpen) : null;
    const otClash = ot ? m.clash(ot) : null;
    const otDir = ot ? ot.dir || "owe" : "owe";
    const otRem = ot ? self.remList(ot.id).length : 0;
    const anchorChoices = [
      { group: "Threads", items: [
        { kind: "thread", label: "Contract addendum - signatures", tid: "contoso" }, { kind: "thread", label: "CPI calculation 2027", tid: "marta" },
        { kind: "thread", label: "Re: rollout schedule", tid: "piotr" }, { kind: "thread", label: "Q4 delivery dates", tid: "fabrikam1" },
      ] },
      { group: "Events", items: [
        { kind: "event", label: "Call with Anna Kowalska", day: "28", time: "13:00" }, { kind: "event", label: "Rollout status with Piotr", day: "27", time: "15:00" },
      ] },
    ];

    /* Planning a time. */
    const pp = st.planPick || {};
    const pt = st.taskPlanFor ? m.find(st.taskPlanFor) : null;
    const ppClash = pt && pp.time ? m.meetingsOn(pp.on).find(e => { const s = toMin(pp.time), ms = toMin(e.time); return s < ms + 60 && ms < s + (pp.mins || 30); }) : null;
    const segBtn = (on) => "display:flex;align-items:center;justify-content:center;box-sizing:border-box;border-radius:8px;white-space:nowrap;font-size:13px;" +
      (touch ? "min-height:44px;padding:0 14px;" : "min-height:30px;padding:0 11px;") +
      (on ? "background:var(--accent);color:var(--onaccent);font-weight:600;" : "color:var(--text2);border:1px solid var(--border2);");

    return {
      taskView: view,
      /* One line under the head says what the last write or open did: *Saved.*, or why a task or its thread could not be
         opened. The next change replaces it. */
      taskSaidShown: !!st.taskSaid,
      taskSaid: st.taskSaid || "",
      taskViews: VIEWS.map(v => ({
        label: v.label,
        count: counts[v.id] ? String(counts[v.id]) : "",
        hasCount: !!counts[v.id],
        pick: () => self.setState({ taskView: v.id, taskSaid: null }),
        countStyle: "font-family:'Geist',system-ui,sans-serif;font-size:11px;border-radius:8px;padding:0 6px;" +
          (view === v.id ? (touch ? "background:var(--accent-soft);color:var(--accent-d);" : "background:var(--onaccent);color:var(--accent-d);") : "background:var(--rail);color:var(--muted);"),
        style: touch
          ? "display:flex;align-items:center;justify-content:center;gap:6px;box-sizing:border-box;min-height:36px;border-radius:8px;white-space:nowrap;font-size:13px;" +
            (isMobile ? "flex:1;padding:0 4px;" : "padding:0 14px;") +
            (view === v.id ? "background:var(--panel);color:var(--text);font-weight:600;box-shadow:0 1px 2px var(--sh-1);" : "color:var(--muted);")
          : "display:flex;align-items:center;gap:6px;border-radius:8px;white-space:nowrap;font-size:12px;padding:6px 11px;" +
            (view === v.id ? "background:var(--accent);color:var(--onaccent);font-weight:600;" : "color:var(--text2);border:1px solid var(--border2);"),
        /* On a phone the four segments share 390 px, so *Waiting for* is *Waiting*. */
        shown: isMobile && v.id === "waiting" ? "Waiting" : v.label,
      })),
      taskViewsRowStyle: touch
        ? "display:flex;align-items:center;gap:2px;background:var(--rail);border:1px solid var(--line);border-radius:10px;padding:3px" + (isMobile ? "" : ";flex:0 0 auto")
        : "display:flex;align-items:center;gap:7px;flex-wrap:wrap",
      taskSwipe: touch ? self.tabSwipe(VIEWS.map(v => v.id), view, v => self.setState({ taskView: v })) : {},
      taskHeadStyle: isMobile
        ? "display:flex;flex-direction:column;gap:8px;background:var(--sub);border-bottom:1px solid var(--line);padding:12px 14px"
        : "display:flex;align-items:center;gap:14px;flex-wrap:wrap;background:var(--sub);border-bottom:1px solid var(--line);padding:12px 24px",
      taskHeadTitle: view === "today" ? "Thursday, 27 August" : view === "upcoming" ? "Coming up" : view === "waiting" ? "Waiting for others" : "Proposed to you",
      taskHeadLine: view === "today"
        ? counts.today + " for today · " + owe.filter(t => !m.done(t) && t.plan && t.plan.on === TODAY).length + " planned" + (owe.some(t => m.clash(t)) ? " · 1 clash" : "")
        : view === "upcoming" ? counts.upcoming + " tasks after today"
        : view === "waiting" ? counts.waiting + " replies you are waiting on"
        : counts.proposed + " to accept or dismiss",
      tbTaskLabel: tbFull ? "New task" : "",
      taskActions: [
        { icon: "search", label: "Find in mail", run: () => self.setState({ screen: "agent" }) },
        { icon: "event_available", label: "Lay out the day", run: askLayout },
        { icon: "check_circle", label: showDone ? "Hide done" : "Show done", run: () => self.setState(s2 => ({ showDone: !s2.showDone })) },
        { icon: "download", label: "Export", run: () => {} },
      ].map(a2 => ({ icon: a2.icon, label: tbFull ? a2.label : "", title: a2.label, run: a2.run, style: tbFull ? tbBtn : tbIcon + ";flex:0 0 auto" })),
      tasksBodyStyle: isMobile || compact
        ? "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:16px;padding:14px"
        : "flex:1;min-height:0;display:flex;gap:0",
      tasksMainStyle: isMobile || compact
        ? "display:flex;flex-direction:column;gap:18px"
        : "flex:1;min-width:0;overflow:auto;display:flex;flex-direction:column;gap:20px;padding:20px max(26px, calc((100% - 330px - 1160px) / 2))",
      /* The day beside the list at the desktop; above it, in Today only, wherever there is no room beside. */
      tasksSideShown: !(isMobile || compact) || view === "today",
      tasksSideStyle: isMobile || compact
        ? "order:-1;display:flex;flex-direction:column;gap:10px;background:var(--sub);border:1px solid var(--line);border-radius:12px;padding:14px"
        : "width:330px;flex:0 0 330px;overflow:auto;display:flex;flex-direction:column;gap:10px;background:var(--sub);border-left:1px solid var(--line);padding:18px",
      taskGroups: groups,
      tasksEmpty: !groups.length,
      tasksEmptyTitle: empty[0],
      tasksEmptyText: empty[1],
      taskDayHint: touch ? "Plan a task from its own Plan." : "Drag a task onto an hour to plan it.",
      /* Where the day stands above the list, only the hours that hold something are drawn. */
      taskDayRows: isMobile || compact ? timeline(TODAY).filter(r => !r.free) : timeline(TODAY),
      capacityText: (dueTodayN === 1 ? "One task is due today." : dueTodayN + " tasks are due today.") + " " +
        (eventsTodayN === 1 ? "The calendar already holds one event." : "The calendar already holds " + eventsTodayN + " events."),
      layoutOfferShown: !!st.layoutOffer,
      layoutIdle: !st.layoutOffer,
      /* The third *Lay out today* of a session finds the deployment's allowance for laying out days spent. */
      layoutSpentShown: !st.layoutOffer && !!st.layoutSpent,
      layoutRows: layoutPlan.map(p => { const t = m.find(p.id); return { time: p.time + "–" + hhmm(toMin(p.time) + (t.mins || 30)), t: t.t, note: p.note, hasNote: !!p.note }; }),
      layoutNothing: !layoutPlan.length,
      layoutSomething: !!layoutPlan.length,
      layoutMissed: "Not today: Prepare the IT contract list for audit (2 hrs) — no two free hours are left today, and it is due 03.09.",
      layoutApply: () => {
        layoutPlan.forEach(p => setOver(p.id, { plan: { on: TODAY, time: p.time, h: p.h } }));
        self.setState({ layoutOffer: false });
        self.notify(layoutPlan.length + (layoutPlan.length === 1 ? " task" : " tasks") + " planned into today.", { kind: "success", title: "Day laid out" });
      },
      layoutDismiss: () => self.setState({ layoutOffer: false }),
      autoSchedule: askLayout,

      /* The controls of the task and plan dialogs, at 44 px under a finger. */
      taskLinkStyle: "display:flex;align-items:center;white-space:nowrap;color:var(--accent-d);" + (touch ? "min-height:44px;padding:0 10px;font-size:13px;border-radius:12px;" : "font-size:12px;"),
      taskIconBtnStyle: "display:flex;align-items:center;justify-content:center;border-radius:" + (touch ? 12 : 8) + "px;color:var(--muted);" + (touch ? "width:44px;height:44px;" : "width:28px;height:28px;"),
      taskBtnStyle: "display:flex;align-items:center;font-size:13px;color:var(--text2);border:1px solid var(--border2);border-radius:8px;" + (touch ? "min-height:44px;padding:0 16px;box-sizing:border-box;" : "padding:8px 15px;"),
      taskPrimaryStyle: "display:flex;align-items:center;font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:8px;" + (touch ? "min-height:44px;padding:0 18px;box-sizing:border-box;" : "padding:8px 16px;"),
      taskQuietStyle: "display:flex;align-items:center;font-size:13px;border-radius:8px;" + (touch ? "min-height:44px;padding:0 12px;box-sizing:border-box;" : "padding:8px 10px;"),
      taskSoftBtnStyle: "display:flex;align-items:center;font-size:12px;font-weight:600;color:var(--accent-d);border:1px solid var(--accent-line);background:var(--accent-soft);border-radius:8px;" + (touch ? "min-height:44px;padding:0 14px;box-sizing:border-box;" : "padding:6px 12px;"),

      /* The new-task dialog, when it was opened on a thread, a person or an event. */
      newTaskHead: st.newTaskCtx && st.newTaskCtx.dir === "wait" ? "New waiting task" : "New task",
      /* A thread or an event is what a task is anchored to; a person is who it is with, the same card, *Belongs to*. */
      newTaskCtxShown: !!(st.newTaskCtx && (st.newTaskCtx.anchor || st.newTaskCtx.withName)),
      newTaskCtxGlyph: st.newTaskCtx && st.newTaskCtx.anchor ? ANCHOR_GLYPH[st.newTaskCtx.anchor.kind] : "person",
      newTaskCtxKind: st.newTaskCtx && st.newTaskCtx.anchor ? ({ thread: "Belongs to the thread", event: "Belongs to the event" })[st.newTaskCtx.anchor.kind] : "Belongs to",
      newTaskCtxLabel: st.newTaskCtx && st.newTaskCtx.anchor ? st.newTaskCtx.anchor.label : (st.newTaskCtx && st.newTaskCtx.withName) || "",
      newTaskCtxClear: () => self.setState(s2 => ({ newTaskCtx: Object.assign({}, s2.newTaskCtx || {}, s2.newTaskCtx && s2.newTaskCtx.anchor ? { anchor: null } : { withName: null, person: null }) })),
      tkTitleDefault: (st.newTaskCtx && st.newTaskCtx.title) || "",
      tkDueDefault: (st.newTaskCtx && st.newTaskCtx.when) || "",
      newTaskDirs: [{ id: "owe", label: "I owe this" }, { id: "wait", label: "I am waiting for this" }].map(d2 => ({
        label: d2.label,
        pick: () => self.setState(s2 => ({ newTaskCtx: Object.assign({}, s2.newTaskCtx || {}, { dir: d2.id }) })),
        style: segBtn(((st.newTaskCtx && st.newTaskCtx.dir) || "owe") === d2.id) + "flex:1;",
      })),

      /* The task dialog. */
      taskDlgOpen: !!ot,
      taskDlgKey: ot ? ot.id : "",
      taskDlgTitle: ot ? ot.t : "",
      taskDlgHead: otDir === "wait" ? "Waiting for" : "Task",
      taskDlgDirs: [{ id: "owe", label: "I owe this" }, { id: "wait", label: "I am waiting for this" }].map(d2 => ({
        label: d2.label,
        pick: () => {
          if (!ot || otDir === d2.id) return;
          /* A reply already closed this waiting task, so the server will not turn it round. */
          if (m.replied(ot)) { self.setState({ taskDlgSaid: "A reply already answered this task, so which way it runs cannot change." }); return; }
          setOver(ot.id, { dir: d2.id });
          self.setState({ taskDlgSaid: null, taskSaid: "Saved." });
        },
        style: segBtn(otDir === d2.id) + "flex:1;",
      })),
      taskDlgDueLabel: otDir === "wait" ? "Follow up on" : "Due",
      taskDlgDays: [{ on: null, label: "No day" }].concat(DAY_CHIPS).concat([{ on: "09-02", label: "Wed 02.09" }]).map(c => ({
        label: c.label, pick: () => { if (!ot) return; setOver(ot.id, { on: c.on, when: dayLabel(c.on) }); self.setState({ taskDlgSaid: null, taskSaid: "Saved." }); }, style: segBtn(ot && (ot.on || null) === c.on),
      })),
      taskDlgPlanShown: otDir === "owe",
      taskDlgPlanned: !!(ot && ot.plan),
      taskDlgPlanLabel: ot && ot.plan ? m.planLabel(ot) + (ot.plan.on === TODAY ? " today" : "") : "Not planned",
      taskDlgPlanAction: ot && ot.plan ? "Move" : "Plan",
      taskDlgPlan: () => ot && openPlan(ot.id),
      taskDlgClashShown: !!otClash,
      taskDlgClash: otClash ? "Clashes with " + otClash.t + " at " + otClash.time + "." : "",
      taskDlgAnchorShown: !!(ot && ot.anchor),
      taskDlgAnchorNone: !(ot && (ot.anchor || personName(ot.person))),
      /* A task anchored to nothing shows who it is with as a row of its own: *Person* and the name, with no *Open*. */
      taskDlgPersonShown: !!(ot && !ot.anchor && personName(ot.person)),
      taskDlgBelongsSpacer: !!(ot && (ot.anchor || personName(ot.person))),
      taskDlgPersonLabel: ot ? personName(ot.person) : "",
      taskDlgPersonRemove: () => ot && setOver(ot.id, { person: null }),
      taskDlgAnchorGlyph: ot && ot.anchor ? ANCHOR_GLYPH[ot.anchor.kind] : "",
      taskDlgAnchorKind: ot && ot.anchor ? (ot.anchor.kind === "thread" ? "Thread" : "Event") : "",
      taskDlgAnchorLabel: ot && ot.anchor ? ot.anchor.label : "",
      taskDlgAnchorOpen: () => ot && openAnchor(ot.anchor),
      taskDlgAnchorRemove: () => ot && setOver(ot.id, { anchor: null }),
      taskDlgAnchorPickOpen: !!st.taskAnchorPick,
      taskDlgAnchorToggle: () => self.setState(s2 => ({ taskAnchorPick: !s2.taskAnchorPick })),
      taskDlgAnchorGroups: anchorChoices.map(g => ({
        group: g.group,
        items: g.items.map(a2 => ({
          glyph: ANCHOR_GLYPH[a2.kind], label: a2.label,
          pick: () => { if (ot) setOver(ot.id, { anchor: a2 }); self.setState({ taskAnchorPick: false }); },
          style: "display:flex;align-items:center;gap:9px;border-radius:8px;font-size:13px;" + (touch ? "min-height:44px;padding:0 10px;" : "padding:6px 10px;") +
            (ot && ot.anchor && ot.anchor.label === a2.label ? "background:var(--accent-soft);color:var(--accent-d);" : "color:var(--text2);"),
        })),
      })),
      taskDlgWaitNote: otDir === "wait" ? (ot && m.replied(ot) ? ot.replied + ", so this closed itself." : "Closes itself when the reply arrives in the thread.") : "",
      taskDlgWaitShown: otDir === "wait" && !(ot && m.replied(ot)),
      taskDlgFollowShown: !!(ot && otDir === "wait" && !m.done(ot) && ot.anchor && ot.anchor.kind === "thread"),
      taskDlgFollow: () => ot && followUp(ot),
      taskDlgReopenShown: !!(ot && m.replied(ot)),
      taskDlgReopen: () => ot && self.setState(s2 => ({ taskReopen: (s2.taskReopen || []).concat([ot.id]) })),
      taskDlgRem: otRem ? (otRem === 1 ? "1 reminder" : otRem + " reminders") : "No reminder",
      /* A reminder is measured back from the due day, so a task with no day offers no *Change*. */
      taskDlgRemChangeShown: !!(ot && ot.on),
      taskDlgSaidShown: !!st.taskDlgSaid,
      taskDlgSaid: st.taskDlgSaid || "",
      taskDlgSaveLabel: st.taskDlgSaving ? "Saving…" : "Save",
      taskDlgRemOpen: () => ot && self.setState({ remFor: { key: ot.id, title: ot.t, timed: false, kind: "task", hint: "before 09:00 · " + (ot.when || "no day"), src: "Tasks" } }),
      taskDlgDone: !!(ot && m.done(ot)),
      taskDlgDoneLabel: ot && m.done(ot) ? (otDir === "wait" ? "Still waiting" : "Mark as not done") : (otDir === "wait" ? "Mark as received" : "Mark as done"),
      taskDlgToggleDone: () => { if (ot) { toggleDone(ot); self.setState({ taskOpen: null }); } },
      taskDlgDelete: () => { if (ot) { const id = ot.id, t = ot.t; self.setState({ taskOpen: null }); self.askHide("tasks", [id], "Delete task?", t + " will disappear from your tasks."); } },
      taskDlgTitleRef: self.taskDlgTitleRef,
      /* *Save* reads *Saving…* while the write is out, and the dialog closes on *Saved.* in the status line. */
      taskDlgSave: () => {
        if (!ot || st.taskDlgSaving) return;
        const v = ((self.taskDlgTitleRef.current || {}).value || "").trim();
        if (v && v !== ot.t) setOver(ot.id, { t: v });
        self.setState({ taskDlgSaving: true });
        setTimeout(() => self.setState({ taskOpen: null, taskAnchorPick: false, taskDlgSaving: false, taskDlgSaid: null, taskSaid: "Saved." }), 700);
      },
      closeTaskDlg: () => self.setState({ taskOpen: null, taskAnchorPick: false, taskDlgSaid: null, taskDlgSaving: false }),

      /* Planning a time. */
      planOpen: !!pt,
      planTitle: pt ? pt.t : "",
      planDays: DAY_CHIPS.map(c => ({ label: c.label, pick: () => self.setState(s2 => ({ planPick: Object.assign({}, s2.planPick, { on: c.on }) })), style: segBtn(pp.on === c.on) })),
      planLengths: LENGTHS.map(n => ({ label: lenLabel(n), pick: () => self.setState(s2 => ({ planPick: Object.assign({}, s2.planPick, { mins: n }) })), style: segBtn((pp.mins || 30) === n) })),
      planRows: pt ? timeline(pp.on || TODAY, {
        exceptId: pt.id, pickTime: pp.time, pickTitle: pt.t, pickLabel: pp.time ? pp.time + "–" + hhmm(toMin(pp.time) + (pp.mins || 30)) : "",
        onPick: (label) => self.setState(s2 => ({ planPick: Object.assign({}, s2.planPick, { time: label }) })),
      }) : [],
      planHint: pp.time ? "Planned " + dayLabel(pp.on) + ", " + pp.time + "–" + hhmm(toMin(pp.time) + (pp.mins || 30)) + "." : "Pick an hour.",
      planClashShown: !!ppClash,
      planClash: ppClash ? "Clashes with " + ppClash.t + " at " + ppClash.time + ". You can still plan it." : "",
      planRemoveShown: !!(pt && pt.plan),
      planRemove: () => { if (pt) setOver(pt.id, { plan: null }); self.setState({ taskPlanFor: null }); },
      planCantSave: pp.time ? "false" : "true",
      planSaveTitle: pp.time ? "Plan" : "Pick an hour first",
      planSaveStyle: "display:flex;align-items:center;font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:8px;" + (touch ? "min-height:44px;padding:0 18px;box-sizing:border-box;" : "padding:8px 16px;") + (pp.time ? "" : "opacity:0.45;"),
      planSave: () => {
        if (!pt || !pp.time) return;
        setOver(pt.id, { plan: { on: pp.on, time: pp.time, h: Math.floor(toMin(pp.time) / 60) }, mins: pp.mins, est: lenLabel(pp.mins) });
        self.setState({ taskPlanFor: null });
        self.notify("“" + pt.t + "” " + dayLabel(pp.on) + " at " + pp.time + ".", { kind: "success", title: "Planned" });
      },
      closePlan: () => self.setState({ taskPlanFor: null }),
    };
  };

  /* What the rest of the app reads: the commitments a thread, a person and an event carry, and what the calendar
     draws for a day. */
  const commitments = (self, ctx) => {
    const st = ctx.st || self.state;
    const m = model(st, ctx);
    const openTask = (id) => self.setState({ taskOpen: id, taskAnchorPick: false, taskDlgSaid: null, taskDlgSaving: false });
    const openPlan = (id) => {
      const t = m.find(id);
      self.setState({ taskPlanFor: id, planPick: { on: (t && t.plan && t.plan.on) || (t && t.on && t.on >= TODAY ? t.on : TODAY), time: t && t.plan ? t.plan.time : null, mins: (t && t.mins) || 30 } });
    };
    const line = (t) => ({
      t: t.t,
      glyph: t.dir === "wait" ? "hourglass_top" : "check_box_outline_blank",
      meta: t.dir === "wait" ? (m.replied(t) ? t.replied : "follow up " + (t.when || "")) : m.planLabel(t) ? m.planLabel(t) : t.when ? "due " + t.when : "no day",
      open: () => openTask(t.id),
      done: m.done(t),
      style: "display:flex;align-items:center;gap:8px;min-width:0;border-radius:8px;cursor:pointer;" + (ctx.touch ? "min-height:44px;padding:0 10px;" : "padding:5px 8px;") +
        (m.done(t) ? "opacity:0.6;" : ""),
      glyphStyle: SYM + "font-size:17px;color:" + (t.dir === "wait" ? "var(--warn)" : "var(--accent-d)"),
    });
    const open = (t) => !m.done(t);
    return {
      forThread: (tid) => m.tasks.filter(t => open(t) && t.anchor && t.anchor.kind === "thread" && t.anchor.tid === tid).map(line),
      threadHasTask: (tid) => m.tasks.some(t => open(t) && t.anchor && t.anchor.kind === "thread" && t.anchor.tid === tid),
      forEvent: (label) => m.tasks.filter(t => t.anchor && t.anchor.kind === "event" && t.anchor.label === label).map(line),
      owedTo: (pid) => m.tasks.filter(t => open(t) && t.dir === "owe" && t.person === pid).map(line),
      owedBy: (pid) => m.tasks.filter(t => open(t) && t.dir === "wait" && t.person === pid).map(line),
      /* A day's tasks as calendar items: planned ones at their time, due-and-unplanned ones at the top of the day. */
      calItems: (d) => {
        const on = "08-" + d;
        const planned = m.tasks.filter(t => t.dir === "owe" && t.plan && t.plan.on === on).map(t => ({
          t: t.t, time: m.planLabel(t).split(" · ")[0], h: t.plan.h, kind: "task", taskId: t.id, done: m.done(t), clash: !!m.clash(t),
          src: t.anchor && t.anchor.kind === "thread" ? t.anchor.label : "",
        }));
        const due = m.tasks.filter(t => t.dir === "owe" && t.on === on && !(t.plan && t.plan.on === on) && !m.done(t)).map(t => ({
          t: t.t, time: "due", h: 8, kind: "due", taskId: t.id, done: false, clash: false, src: "",
        }));
        return due.concat(planned);
      },
      proposals: m.proposals.map(p => ({
        t: p.t, when: p.when || "no day", src: p.from + (p.anchor ? " · " + p.anchor.label : personName(p.person) ? " · " + personName(p.person) : ""), glyph: p.from === "From the Agent" ? "auto_awesome" : "mail",
        add: () => {
          self.setState(s2 => ({ taskAccepted: (s2.taskAccepted || []).concat([p.id]) }));
          self.notify("“" + p.t + "” is in your tasks" + (p.on ? ", due " + p.when : "") + ".", { kind: "success", title: "Task added" });
        },
        skip: () => self.setState(s2 => ({ hidden: Object.assign({}, s2.hidden || {}, { tasks: ((s2.hidden || {}).tasks || []).concat([p.id]) }) })),
      })),
      openTask, openPlan,
      askLayout: () => askLayoutFor(self),
    };
  };

  return { props, commitments, TODAY };
})();
