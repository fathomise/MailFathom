// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Fathom and the catch-up: the button in the bottom-right corner of every space, its Smart mode, the answer card both
// write into, and the run of cards Start opens after an absence. `component.js` calls `install` once and merges `vals`
// into what the template reads; the sample content is `data/prototype/fathom.js` and `data/prototype/catchup.js`.
MailFathomDesign.fathomUi = (() => {
  const D = () => MailFathomDesign.data;
  const DAY_MS = 24 * 60 * 60 * 1000;
  const GLYPH = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;";
  const reducedMotion = () => typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* A catch-up stands on Start for 24 hours from the moment it was first opened, and for good once saved. */
  const catchupLive = (c) => !!c && (c.saved || Date.now() - c.openedAt < DAY_MS);
  const hoursLeft = (c) => Math.max(1, Math.ceil((DAY_MS - (Date.now() - c.openedAt)) / 3600000));

  const fill = (s, label) => String(s || "").replace("{label}", label || "this");

  /* What an `answer` says: the action's own line, else what the target carries for that action, else the fallback. */
  const answerText = (a, t) => {
    if (t.sel) return (a.line || "") + "“" + t.sel + "” " + (a.id === "translate" ? "(tłumaczenie w kliencie)" : "reads as the passage it was selected from, in two lines.");
    if (a.line) return a.line;
    const byAction = { summarize: t.short, more: t.more || t.short, translate: t.pl, why: t.text, want: t.text, decided: t.more || t.short };
    return byAction[a.id] || t.text || fill((D().fathom.fallback[a.id] || D().fathom.fallback.default), t.label);
  };

  function install(self) {
    /* Opening the catch-up: what Start does the first time it is opened after an absence. */
    self.startCatchup = (reason) => {
      self.timers.forEach(clearTimeout);
      self.setState({
        catchup: { reason: reason, openedAt: Date.now(), saved: false, seenAll: false },
        catchupOpen: true, catchupIdx: 0, catchupPaused: false, catchupArchived: false,
        screen: "start", phase: "idle", fathomMenu: false, fathomSmart: false, fathomAnswer: null, ctxMenu: null, more: false,
      });
    };
    self.openCatchup = () => self.setState({ catchupOpen: true, catchupIdx: 0, catchupPaused: false, fathomMenu: false, fathomSmart: false, fathomAnswer: null });
    self.closeCatchup = () => self.setState({ catchupOpen: false, fathomAnswer: null });
    self.stepCatchup = (by) => {
      const n = D().catchup.slides.length;
      const i = Math.max(0, Math.min(n - 1, (self.state.catchupIdx || 0) + by));
      self.setState(s => ({ catchupIdx: i, fathomAnswer: null, catchup: i === n - 1 && s.catchup ? Object.assign({}, s.catchup, { seenAll: true }) : s.catchup }));
    };
    self.saveCatchup = () => {
      const saved = !(self.state.catchup && self.state.catchup.saved);
      self.setState(s => ({ catchup: Object.assign({}, s.catchup, { saved: saved }) }));
      self.notify(saved ? "It stays on Start after the 24 hours, until you remove it." : "It goes from Start 24 hours after you first opened it.",
        { kind: saved ? "success" : "neutral", title: saved ? "Catch-up saved" : "No longer saved" });
    };
    self.exportCatchup = (x, y) => {
      self.armCtx(false);
      self.setState({ ctxMenu: {
        x: x, y: y, title: "Export the catch-up", detail: "Written on this device. Nothing is sent.",
        items: D().catchup.exportFormats.map(f => ({ icon: f.icon, label: f.label, run: () =>
          self.notify("catch-up-" + D().catchup.range.replace(/\s+/g, "-").toLowerCase() + "." + f.id + " is in your Downloads.", { kind: "success", title: "Exported" }) })),
      } });
    };

    /* A target is anything an action can act on: an item on a card, a component Smart found, or a selection. */
    self.runFathom = (a, t) => {
      const closeAll = { fathomMenu: false, fathomSmart: false, smartHover: null, fathomAnswer: null };
      if (a.do === "answer") {
        self.setState({ fathomMenu: false, fathomSmart: false, smartHover: null, fathomAnswer: { title: a.label, about: t.label || "", text: answerText(a, t), ctx: t.label || a.label } });
        return;
      }
      if (a.do === "agent" || a.do === "agentNew") {
        self.setState(Object.assign({ catchupOpen: false }, closeAll));
        if (a.do === "agentNew") self.setState({ screen: "agent", convId: null, agentCtx: null });
        else self.goAgentWith(a.ctx ? a.ctx + (t.label ? " · " + t.label : "") : t.label || "this");
        return;
      }
      if (a.do === "toast") {
        self.setState(closeAll);
        self.notify(a.toast, { title: a.label, actionLabel: "Undo", onAction: () => self.hideToast() });
        return;
      }
      if (a.do === "draft") { self.setState(closeAll); self.draftReply(); return; }
      if (a.do === "prep") {
        self.setState(Object.assign({ startPrep: "busy" }, closeAll));
        self.timers.push(setTimeout(() => self.setState({ startPrep: "ready" }), Math.max(600, (self.props.streamDelay ?? 550) * 1.6)));
        return;
      }
      if (a.do === "catchup") { self.setState(closeAll); self.openCatchup(); return; }
      if (a.do === "catchupSave") { self.setState(closeAll); self.saveCatchup(); return; }
      if (a.do === "catchupExport") { self.setState(closeAll); self.exportCatchup(t.x || 200, t.y || 200); return; }
      if (a.do === "go") {
        const go = a.go || t.go || (t.tid ? "thread" : t.pid ? "people" : "mail");
        self.setState(Object.assign({ catchupOpen: false }, closeAll));
        if (go === "thread") self.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: t.tid || "contoso", cameFrom: false });
        else if (go === "waiting") self.goScreen("mail", { mailPane: "list", threadOpen: false, cameFrom: false, resultFilter: { q: "", why: "waiting on your reply", ids: D().home.waiting.map(w => w.tid) } });
        else if (go === "people") self.goScreen("people", { peoplePane: "list" });
        else if (go === "cal") self.goScreen("cal");
        else self.goScreen("mail", { mailPane: "list", cameFrom: false });
      }
    };

    self.fathomMenuFor = (title, detail, actions, t, x, y) => {
      self.armCtx(false);
      self.setState({ ctxMenu: { x: x, y: y, title: title, detail: detail, items: actions.map(a => ({ icon: a.icon, label: a.label, run: () => self.runFathom(a, t) })) } });
    };

    /* Smart: the next press lands on a component instead of acting. Listened for on the window in the capture phase,
       so it runs before anything the page itself does with a press, and the page still scrolls as it always does. */
    const smartTarget = (x, y) => {
      if (typeof document === "undefined") return null;
      const els = document.elementsFromPoint(x, y);
      for (const el of els) {
        if (el.closest && el.closest("[data-fathom-ui]")) return { ui: true };
        const c = el.closest && el.closest("[data-smart]");
        if (c) return { el: c };
      }
      return null;
    };
    const smartRect = (el) => {
      const r = el.getBoundingClientRect();
      const root = self.rootRef.current && self.rootRef.current.getBoundingClientRect();
      const ox = root ? root.left : 0, oy = root ? root.top : 0;
      return { x: r.left - ox, y: r.top - oy, w: r.width, h: r.height };
    };
    const smartOf = (el) => {
      const id = el.getAttribute("data-smart");
      const comp = D().fathom.components[id];
      return comp ? { id: id, comp: comp, label: el.getAttribute("data-smart-label") || "", text: el.getAttribute("data-smart-text") || "", tid: el.getAttribute("data-smart-ref") || "" } : null;
    };
    self._smartPt = null;
    self.onSmartMove = (e) => {
      if (!self.state.fathomSmart || self.state.ctxMenu) return;
      if (e && e.clientX != null) self._smartPt = { x: e.clientX, y: e.clientY };
      const p = self._smartPt;
      if (!p) return;
      const hit = smartTarget(p.x, p.y);
      const s = hit && hit.el ? smartOf(hit.el) : null;
      const prev = self.state.smartHover;
      if (!s) { if (prev) self.setState({ smartHover: null }); return; }
      const r = smartRect(hit.el);
      if (prev && prev.id === s.id && prev.label === s.label && prev.x === r.x && prev.y === r.y && prev.w === r.w) return;
      self.setState({ smartHover: Object.assign({ id: s.id, name: s.comp.name, label: s.label }, r) });
    };
    self.onSmartPress = (e) => {
      if (!self.state.fathomSmart || self.state.ctxMenu) return;
      const hit = smartTarget(e.clientX, e.clientY);
      if (hit && hit.ui) return;
      e.preventDefault();
      e.stopPropagation();
      if (e.type !== "click") return;
      const s = hit && hit.el ? smartOf(hit.el) : null;
      if (!s) return;
      const r = smartRect(hit.el);
      self.setState({ smartHover: Object.assign({ id: s.id, name: s.comp.name, label: s.label }, r) });
      const p = self.localPt(e.clientX, e.clientY);
      self.fathomMenuFor(s.comp.name, s.label || s.comp.what, s.comp.actions, { label: s.label || s.comp.name, text: s.text, tid: s.tid, x: p.x, y: p.y }, p.x, p.y);
    };
    self.onFathomKey = (e) => {
      const st = self.state;
      if (e.key === "Escape") {
        if (st.ctxMenu) return;
        if (st.fathomAnswer) self.setState({ fathomAnswer: null });
        else if (st.fathomSmart) self.setState({ fathomSmart: false, smartHover: null });
        else if (st.fathomMenu) self.setState({ fathomMenu: false });
        else if (st.catchupOpen) self.closeCatchup();
        return;
      }
      if (st.catchupOpen && !st.ctxMenu && (e.key === "ArrowRight" || e.key === "ArrowLeft")) self.stepCatchup(e.key === "ArrowRight" ? 1 : -1);
    };
    /* In the Agent, Fathom stands level with the field it sits beside: its centre on the field's centre, kept in step
       as the field moves (a context chip, a quote, the suggestions folding away). */
    self.syncFathomDock = () => {
      const box = self.state.screen === "agent" ? document.querySelector("[data-agent-box]") : null;
      const fab = document.querySelector("[data-fathom-fab]");
      const dock = box && fab ? Math.round(window.innerHeight - box.getBoundingClientRect().top - box.offsetHeight / 2 - fab.offsetHeight / 2) : null;
      if (dock !== (self.state.fathomDock ?? null)) self.setState({ fathomDock: dock });
    };
    if (typeof window !== "undefined") {
      setInterval(self.syncFathomDock, 150);
      ["pointerdown", "mousedown", "pointerup", "click"].forEach(t => window.addEventListener(t, self.onSmartPress, true));
      window.addEventListener("pointermove", self.onSmartMove, true);
      window.addEventListener("scroll", () => self.onSmartMove(null), true);
      window.addEventListener("keydown", self.onFathomKey);
    }
  }

  function vals(self, c) {
    const { st, isMobile, touch, singlePane, thread, person } = c;
    const F = D().fathom, CU = D().catchup;
    const barH = isMobile ? self.barH() : 0;

    /* ——— The Fathom button ——— */
    const threadShown = st.screen === "mail" && !!st.threadOpen && (!singlePane || st.mailPane === "thread");
    const peopleOpen = st.screen === "people" && !!st.personId && (!singlePane || st.peoplePane === "page" || st.peoplePane === "person");
    const menuKey = st.screen === "mail" ? (threadShown ? "mailThread" : "mailList") : st.screen === "people" ? "people" : (F.menu[st.screen] ? st.screen : "start");
    const where = st.screen === "mail" ? (threadShown ? thread.subject : "Inbox")
      : st.screen === "cal" ? "this week" : st.screen === "tasks" ? "your tasks" : st.screen === "people" ? (peopleOpen ? person.name : "your contacts")
      : st.screen === "agent" ? "the conversation" : "your day";
    const space = { start: "Start", mail: "Mail", cal: "Calendar", tasks: "Tasks", people: "People", agent: "Agent" }[st.screen] || "Start";
    const fabShown = !st.catchupOpen && !st.compose && !st.doc && !st.htmlDoc && !st.settings;
    const fabSize = touch ? 56 : 52;
    /* 24 px from both edges, 20 on a phone; in the Agent it stands level with the field it sits beside (`fathomDock`). */
    const fabEdge = isMobile ? 20 : 24;
    const fabBottom = st.screen === "agent" && st.fathomDock != null ? st.fathomDock : fabEdge + barH;
    const smartOn = !!st.fathomSmart;
    const menuRun = (a) => () => {
      if (a.do === "agent") { self.setState({ fathomMenu: false }); self.goAgentWith(a.ctx || (st.screen === "mail" ? "thread “" + thread.subject + "”" : where)); return; }
      if (a.do === "answer") { self.setState({ fathomMenu: false, fathomAnswer: { title: a.label, about: space + " · " + where, text: F.menuAnswers[a.id] || "", ctx: a.label } }); return; }
      self.runFathom(a, { label: where });
    };
    const rowStyle = "display:flex;align-items:center;gap:11px;border-radius:9px;color:var(--text);" + (touch ? "min-height:48px;padding:0 12px;font-size:15px;" : "min-height:38px;padding:0 10px;font-size:14px;");
    const v = {
      fathomFabShown: fabShown,
      fathomFabIcon: st.fathomMenu || smartOn ? "close" : "auto_awesome",
      fathomFabTitle: smartOn ? "Leave Smart" : st.fathomMenu ? "Close" : "Fathom - ask or act on what is on screen",
      fathomFabExpanded: !!st.fathomMenu,
      toggleFathom: () => {
        if (self.state.fathomSmart) { self.setState({ fathomSmart: false, smartHover: null }); return; }
        self.setState(s => ({ fathomMenu: !s.fathomMenu, fathomAnswer: null }));
      },
      fathomFabSmart: smartOn ? "1" : "0",
      /* A disc whose glyph alone names it; its gradient, gloss and sheen live in prototype.css. */
      fathomFabStyle: "position:fixed;z-index:88;right:" + fabEdge + "px;bottom:" + fabBottom + "px;width:" + fabSize + "px;height:" + fabSize + "px;box-sizing:border-box;" +
        "display:flex;align-items:center;justify-content:center;border-radius:50%;color:oklch(0.86 0.07 250)",
      fathomFabGlyph: GLYPH + "position:relative;z-index:1;font-size:" + (touch ? 26 : 24) + "px;font-variation-settings:'FILL' 1,'wght' 400",
      fathomMenuOpen: !!st.fathomMenu,
      closeFathomMenu: () => self.setState({ fathomMenu: false }),
      fathomMenuWhere: space + " · " + where,
      fathomMenuItems: (F.menu[menuKey] || []).map(a => ({ icon: a.icon, label: a.do === "agent" && menuKey === "people" && peopleOpen ? "Ask about " + person.name : a.label, run: menuRun(a), style: rowStyle })),
      fathomRowStyle: rowStyle,
      fathomMenuStyle: "position:fixed;z-index:89;right:" + fabEdge + "px;bottom:" + (fabBottom + fabSize + 12) + "px;width:" + (isMobile ? "min(320px,calc(100% - 32px))" : "296px") + ";box-sizing:border-box;" +
        "display:flex;flex-direction:column;gap:2px;background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:8px 6px;box-shadow:0 20px 46px var(--sh-3),0 2px 6px var(--sh-2)",
      startSmart: () => self.setState({ fathomMenu: false, fathomSmart: true, smartHover: null }),
      openAgentFromFathom: () => { self.setState({ fathomMenu: false }); self.goScreen("agent"); },
      smartHint: isMobile ? "Tap anything for its actions" : touch ? "Tap anything to see what Fathom can do with it" : "Point at anything to see what Fathom can do with it",
      smartOn,
      smartBarStyle: "position:fixed;z-index:87;left:50%;transform:translateX(-50%);top:" + (isMobile ? 10 : 14) + "px;display:flex;align-items:center;gap:10px;max-width:calc(100% - 24px);box-sizing:border-box;" +
        "background:var(--panel);border:1px solid var(--accent-line);border-radius:999px;box-shadow:0 10px 26px var(--sh-3);font-size:13px;color:var(--text2);" + (touch ? "padding:4px 4px 4px 14px;" : "padding:5px 5px 5px 14px;"),
      smartDoneStyle: "display:inline-flex;align-items:center;justify-content:center;border-radius:999px;font-weight:600;color:var(--onaccent);background:var(--accent);white-space:nowrap;" + (touch ? "min-height:40px;padding:0 16px;font-size:14px;" : "min-height:30px;padding:0 13px;font-size:13px;"),
      leaveSmart: () => self.setState({ fathomSmart: false, smartHover: null }),
      smartHoverShown: smartOn && !!st.smartHover,
      smartBoxStyle: (() => {
        const h = st.smartHover || { x: 0, y: 0, w: 0, h: 0 };
        return "position:fixed;z-index:86;pointer-events:none;left:" + (h.x - 4) + "px;top:" + (h.y - 4) + "px;width:" + (h.w + 8) + "px;height:" + (h.h + 8) + "px;box-sizing:border-box;" +
          "border:2px solid var(--accent);border-radius:12px;background:color-mix(in oklch, var(--accent) 9%, transparent);transition:left 90ms ease,top 90ms ease,width 90ms ease,height 90ms ease";
      })(),
      smartTagStyle: (() => {
        const h = st.smartHover || { x: 0, y: 0 };
        const above = h.y > 34;
        return "position:fixed;z-index:86;pointer-events:none;left:" + Math.max(6, h.x - 4) + "px;top:" + (above ? h.y - 30 : h.y + 6) + "px;display:inline-flex;align-items:center;gap:6px;" +
          "font-size:12px;font-weight:600;color:var(--onaccent);background:var(--accent);border-radius:7px;padding:4px 9px 4px 7px;white-space:nowrap;box-shadow:0 4px 12px var(--sh-2)";
      })(),
      smartTagText: st.smartHover ? st.smartHover.name + (st.smartHover.label ? " · " + st.smartHover.label : "") : "",
      smartTagGlyph: GLYPH + "font-size:14px",
      /* The thread's own floating button is this one now. */
      replyFabShown: false,
    };

    /* ——— The answer card both Fathom and the catch-up write into ——— */
    const ans = st.fathomAnswer;
    Object.assign(v, {
      fathomAnswerOpen: !!ans,
      fathomAnswerTitle: ans ? ans.title : "",
      fathomAnswerAbout: ans ? ans.about : "",
      fathomAnswerHasAbout: !!(ans && ans.about),
      fathomAnswerText: ans ? ans.text : "",
      closeFathomAnswer: () => self.setState({ fathomAnswer: null }),
      fathomAnswerAgent: () => {
        const a = self.state.fathomAnswer;
        self.setState({ fathomAnswer: null, catchupOpen: false });
        self.goAgentWith(a ? a.ctx : "Fathom");
      },
      fathomAnswerStyle: "position:fixed;z-index:97;box-sizing:border-box;display:flex;flex-direction:column;gap:10px;background:var(--panel);border:1px solid var(--accent-line);border-radius:16px;" +
        "box-shadow:0 22px 50px var(--sh-3),0 2px 6px var(--sh-2);padding:14px 16px 14px 16px;" +
        (isMobile ? "left:12px;right:12px;bottom:" + (st.catchupOpen ? 16 : fabBottom + fabSize + 12) + "px;"
          : st.catchupOpen ? "left:50%;transform:translateX(-50%);bottom:28px;width:420px;" : "right:" + fabEdge + "px;bottom:" + (fabBottom + fabSize + 12) + "px;width:380px;"),
      fathomAnswerBtnStyle: "display:inline-flex;align-items:center;gap:7px;border-radius:9px;font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);box-sizing:border-box;" + (touch ? "min-height:44px;padding:0 15px;" : "min-height:34px;padding:0 13px;"),
      fathomAnswerCloseStyle: "margin-left:auto;display:inline-flex;align-items:center;justify-content:center;border-radius:8px;color:var(--faint);" + GLYPH + (touch ? "width:44px;height:44px;font-size:20px;margin-right:-10px;" : "width:28px;height:28px;font-size:18px;margin-right:-6px;"),
    });

    /* ——— The catch-up on Start: the ring that reopens it ——— */
    const cu = st.catchup;
    const live = catchupLive(cu);
    const reason = cu ? CU.reasons[cu.reason] || CU.reasons.away : CU.reasons.away;
    Object.assign(v, {
      cuRingShown: live && st.phase === "idle",
      cuRingTitle: "While you were away",
      cuRingSub: cu ? reason.label + " · " + CU.slides.length + " cards · " + (cu.saved ? "kept until you remove it" : "reopen for " + hoursLeft(cu) + " h") : "",
      cuRingSaved: !!(cu && cu.saved),
      cuRingIcon: cu && cu.reason === "holiday" ? "beach_access" : "flight_land",
      openCatchup: self.openCatchup,
      cuRingRowStyle: "display:flex;align-items:center;gap:13px;box-sizing:border-box;border-radius:14px;background:var(--panel);border:1px solid var(--line);" + (touch ? "padding:10px 12px;min-height:64px;" : "padding:9px 12px 9px 10px;"),
      cuRingStyle: "flex:0 0 46px;width:46px;height:46px;border-radius:50%;padding:2.5px;box-sizing:border-box;background:" +
        (cu && cu.seenAll ? "var(--border2)" : "conic-gradient(from 210deg, var(--accent), var(--accent-2), var(--hl-line), var(--accent))"),
      cuRingInnerStyle: "width:100%;height:100%;border-radius:50%;display:flex;align-items:center;justify-content:center;background:var(--panel);box-shadow:inset 0 0 0 2px var(--panel);color:var(--accent-d);" + GLYPH + "font-size:21px",
    });

    /* ——— The catch-up's cards ——— */
    const n = CU.slides.length;
    const idx = Math.max(0, Math.min(n - 1, st.catchupIdx || 0));
    const slide = CU.slides[idx];
    const filled = slide.layout === "stats" && slide.tone === "accent" || slide.layout === "end";
    const paused = !!st.catchupPaused || !!st.ctxMenu || !!st.fathomAnswer;
    const inkSoft = filled ? "color-mix(in oklch, var(--onaccent) 78%, transparent)" : "var(--text2)";
    const stageBg = filled ? "var(--accent)" : slide.tone === "ok" ? "var(--ok-soft)" : slide.tone === "warn" ? "var(--warn-soft)" : slide.tone === "neutral" ? "var(--sub)" : "var(--accent-soft)";
    const itemBg = filled ? "background:color-mix(in oklch, var(--onaccent) 14%, transparent);border:1px solid color-mix(in oklch, var(--onaccent) 22%, transparent);"
      : "background:var(--panel);border:1px solid var(--line);box-shadow:0 1px 2px var(--sh-1);";
    const target = (it, e) => {
      const p = e && e.clientX != null ? self.localPt(e.clientX, e.clientY) : { x: 200, y: 200 };
      return Object.assign({}, it, { x: p.x, y: p.y });
    };
    const openItem = (it) => (e) => {
      if (e && e.stopPropagation) e.stopPropagation();
      if (typeof window !== "undefined" && String(window.getSelection && window.getSelection()).trim()) return;
      const t = target(it, e);
      const title = it.kind === "stat" ? it.n + " " + it.label : it.label;
      self.fathomMenuFor(title, it.kind === "stat" ? "While you were away" : it.sub || "", F.itemActions[it.kind] || [], Object.assign({}, t, { label: title }), t.x, t.y);
    };
    const maxN = Math.max.apply(null, slide.items.map(i => i.n || 0).concat([1]));
    const btn = (primary) => "display:inline-flex;align-items:center;justify-content:center;gap:8px;border-radius:999px;font-weight:600;white-space:nowrap;box-sizing:border-box;" + (touch ? "min-height:48px;padding:0 20px;font-size:15px;" : "min-height:42px;padding:0 18px;font-size:14px;") +
      (primary ? (filled ? "color:var(--accent-d);background:var(--onaccent);" : "color:var(--onaccent);background:var(--accent);") : (filled ? "color:var(--onaccent);border:1px solid color-mix(in oklch, var(--onaccent) 40%, transparent);" : "color:var(--text);background:var(--panel);border:1px solid var(--border2);"));
    const iconBtn = "display:inline-flex;align-items:center;justify-content:center;border-radius:50%;" + GLYPH + (touch ? "width:44px;height:44px;font-size:22px;" : "width:34px;height:34px;font-size:19px;");
    Object.assign(v, {
      cuOpen: !!st.catchupOpen && !!cu,
      cuScrimStyle: "position:fixed;inset:0;z-index:95;display:flex;align-items:center;justify-content:center;gap:18px;background:" + (isMobile ? stageBg : "color-mix(in oklch, var(--bg) 70%, black 30%)") + ";" + (isMobile ? "" : "padding:24px"),
      cuStageStyle: "position:relative;display:flex;flex-direction:column;box-sizing:border-box;overflow:hidden;color:" + (filled ? "var(--onaccent)" : "var(--text)") + ";background:" + stageBg + ";transition:background 260ms var(--ease-out);" +
        (isMobile ? "width:100%;height:100%;padding:14px 16px 16px;" : "width:min(460px,100%);min-height:min(660px,100%);max-height:min(800px,100%);border-radius:24px;padding:16px 22px 20px;box-shadow:0 30px 80px rgba(0,0,0,0.35);"),
      cuSideShown: !isMobile,
      cuPrevStyle: iconBtn + "color:var(--onaccent);background:color-mix(in oklch, var(--onaccent) 14%, transparent);" + (idx === 0 ? "opacity:0.35;" : ""),
      cuNextStyle: iconBtn + "color:var(--onaccent);background:color-mix(in oklch, var(--onaccent) 14%, transparent);" + (idx === n - 1 ? "opacity:0.35;" : ""),
      cuPrev: (e) => { if (e && e.stopPropagation) e.stopPropagation(); self.stepCatchup(-1); },
      cuNext: (e) => { if (e && e.stopPropagation) e.stopPropagation(); self.stepCatchup(1); },
      cuBars: CU.slides.map((s, i) => ({
        trackStyle: "flex:1;height:3px;border-radius:3px;overflow:hidden;background:color-mix(in oklch, currentColor 22%, transparent)",
        fillStyle: "height:100%;border-radius:3px;background:currentColor;" + (i < idx ? "width:100%;" : i > idx ? "width:0;" :
          "width:0;animation:mfstory 12s linear forwards;animation-play-state:" + (paused ? "paused" : "running") + ";"),
        end: i === idx ? () => { if (!reducedMotion() && i < n - 1) self.stepCatchup(1); } : null,
      })),
      cuHeadText: "Catch-up",
      cuHeadSub: reason.label,
      cuIcon: GLYPH + "font-size:20px",
      cuIconBtn: iconBtn + "color:inherit;",
      cuPauseIcon: st.catchupPaused ? "play_arrow" : "pause",
      cuPauseTitle: st.catchupPaused ? "Play" : "Pause",
      cuTogglePause: (e) => { e.stopPropagation(); self.setState(s => ({ catchupPaused: !s.catchupPaused })); },
      cuSaveIcon: cu && cu.saved ? "bookmark_added" : "bookmark",
      cuSaveTitle: cu && cu.saved ? "Saved - press to unsave" : "Save, so it stays after 24 hours",
      cuSave: (e) => { e.stopPropagation(); self.saveCatchup(); },
      cuExport: (e) => { e.stopPropagation(); const p = self.localPt(e.clientX, e.clientY); self.exportCatchup(p.x - 220, p.y + 16); },
      cuClose: (e) => { if (e && e.stopPropagation) e.stopPropagation(); self.closeCatchup(); },
      /* A tap on a card's left third goes back and anywhere else goes on, as a story does; a press on an item or a
         control is that item's, and a press that ends a text selection is the selection's. */
      cuTap: (e) => {
        if (e.target && e.target.closest && e.target.closest("[data-cu-skip]")) return;
        if (typeof window !== "undefined" && String(window.getSelection && window.getSelection()).trim()) return;
        const r = e.currentTarget.getBoundingClientRect();
        self.stepCatchup(e.clientX - r.left < r.width / 3 ? -1 : 1);
      },
      cuSelect: (e) => {
        setTimeout(() => {
          const sel = typeof window !== "undefined" && window.getSelection ? window.getSelection() : null;
          const text = sel ? String(sel).trim() : "";
          if (text.length < 3 || !sel.rangeCount) return;
          const r = sel.getRangeAt(0).getBoundingClientRect();
          const p = self.localPt(r.left, r.bottom + 6);
          const shown = text.length > 60 ? text.slice(0, 57) + "…" : text;
          self.fathomMenuFor("Selected text", "“" + shown + "”", F.selection, { label: shown, sel: shown }, p.x, p.y);
        }, 0);
      },
      cuSlideKey: slide.id,
      cuEyebrow: slide.eyebrow,
      cuTitle: slide.title,
      cuLede: slide.id === "hello" ? reason.line + " " + slide.lede : slide.lede,
      cuSlideIcon: slide.icon,
      cuEyebrowStyle: "display:flex;align-items:center;gap:8px;font-size:13px;font-weight:600;color:" + (filled ? "var(--onaccent)" : slide.tone === "ok" ? "var(--ok-text)" : slide.tone === "warn" ? "var(--warn-text)" : "var(--accent-d)"),
      cuTitleStyle: "font-size:" + (isMobile ? 30 : 34) + "px;line-height:1.08;font-weight:650;letter-spacing:-0.025em;text-wrap:balance;user-select:text",
      cuLedeStyle: "font-size:16px;line-height:1.5;color:" + inkSoft + ";text-wrap:pretty;user-select:text",
      cuBodyStyle: "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:14px;padding:" + (isMobile ? "26px 2px 10px" : "34px 2px 10px") + ";" + (slide.layout === "end" ? "justify-content:center;" : ""),
      cuIsStats: slide.layout === "stats",
      cuIsList: slide.layout === "list",
      cuIsBars: slide.layout === "bars",
      cuIsChips: slide.layout === "chips",
      cuStatsStyle: "display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:8px",
      cuItems: slide.items.map(it => ({
        n: it.n, label: it.label, sub: it.sub || "", badge: it.badge || "", hasBadge: !!it.badge, initials: it.initials || "", icon: it.icon || "",
        open: openItem(it),
        aria: (it.kind === "stat" ? it.n + " " + it.label : it.label) + " - actions",
        statStyle: "display:flex;flex-direction:column;gap:4px;border-radius:16px;padding:14px 14px 13px;text-align:left;" + itemBg,
        statNStyle: "font-size:" + (isMobile ? 34 : 40) + "px;line-height:1;font-weight:650;letter-spacing:-0.03em;font-variant-numeric:tabular-nums",
        statLabelStyle: "font-size:13px;line-height:1.35;color:" + inkSoft,
        rowStyle: "display:flex;align-items:flex-start;gap:12px;border-radius:14px;padding:12px 13px;" + itemBg + (touch ? "min-height:56px;box-sizing:border-box;" : ""),
        rowIconStyle: GLYPH + "font-size:20px;padding-top:1px;color:" + (it.level === "danger" ? "var(--err-text)" : slide.tone === "warn" ? "var(--warn-text)" : slide.tone === "ok" ? "var(--ok-text)" : "var(--accent-d)"),
        badgeStyle: "flex:0 0 auto;font-size:11px;font-weight:600;border-radius:999px;padding:3px 8px;white-space:nowrap;background:var(--hl);color:var(--text)",
        barRowStyle: "display:flex;align-items:center;gap:12px;border-radius:14px;padding:10px 12px;" + itemBg + (touch ? "min-height:56px;box-sizing:border-box;" : ""),
        barStyle: "height:8px;border-radius:4px;background:var(--accent);width:" + Math.round(((it.n || 0) / maxN) * 100) + "%",
        avatarStyle: "flex:0 0 34px;width:34px;height:34px;border-radius:50%;display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:600;background:var(--accent-soft);color:var(--accent-d);box-shadow:inset 0 0 0 1px var(--accent-line)",
        chipStyle: "display:inline-flex;align-items:baseline;gap:7px;border-radius:999px;font-weight:600;" + itemBg +
          (it.size === 3 ? "font-size:24px;padding:12px 20px;" : it.size === 2 ? "font-size:19px;padding:10px 17px;" : "font-size:15px;padding:8px 14px;") + (touch ? "min-height:44px;box-sizing:border-box;" : ""),
        chipNStyle: "font-size:12px;font-weight:500;color:var(--muted);font-variant-numeric:tabular-nums",
      })),
      cuHasCta: !!slide.cta,
      cuCtaLabel: slide.cta ? (slide.cta.id === "archive" && st.catchupArchived ? "Archived · Undo" : slide.cta.label) : "",
      cuCtaIcon: slide.cta ? (slide.cta.id === "archive" && st.catchupArchived ? "undo" : slide.cta.icon) : "",
      cuCtaStyle: btn(true),
      cuCta: (e) => {
        e.stopPropagation();
        if (slide.cta.id === "close") { self.closeCatchup(); return; }
        const was = !!self.state.catchupArchived;
        self.setState({ catchupArchived: !was });
        if (!was) self.notify("They are in Archive, and the threads that need you are untouched.", { title: "221 messages archived", actionLabel: "Undo", onAction: () => { self.hideToast(); self.setState({ catchupArchived: false }); } });
      },
      cuIsEnd: slide.layout === "end",
      cuEndSave: (e) => { e.stopPropagation(); self.saveCatchup(); },
      cuEndSaveLabel: cu && cu.saved ? "Saved" : "Save",
      cuEndExport: (e) => { e.stopPropagation(); const p = self.localPt(e.clientX, e.clientY); self.exportCatchup(p.x - 120, p.y - 190); },
      cuGhostStyle: btn(false),
      cuFootStyle: "display:flex;align-items:center;gap:10px;padding-top:12px;font-size:12px;white-space:nowrap;color:" + inkSoft,
      cuFootText: (idx + 1) + " of " + n + " · " + (cu && cu.saved ? "saved" : cu ? "on Start for " + hoursLeft(cu) + " h" : ""),
      cuHint: isMobile ? "Tap an edge to move" : touch ? "Tap an edge to move, hold text to select" : "Press anything, or select text, for more",
      cuHintStyle: "margin-left:auto;text-align:right;color:" + inkSoft,
    });

    /* ——— Start: what could hurt, and what arrived since the reader last looked ——— */
    const HOME = D().home;
    const heads = HOME.headsUp.filter(h => (st.headsHidden || []).indexOf(h.id) < 0);
    const sl = HOME.sinceLast;
    Object.assign(v, {
      hasHeadsUp: heads.length > 0,
      headsUp: heads.map(h => ({
        title: h.title, why: h.why, check: h.check, rule: h.rule, icon: h.icon, tid: h.tid,
        open: () => self.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: h.tid, cameFrom: false }),
        safe: () => {
          self.setState(s => ({ headsHidden: (s.headsHidden || []).concat([h.id]) }));
          self.notify("“" + h.title + "” will not be raised again.", { title: h.level === "danger" ? "Marked as safe" : "Dismissed", actionLabel: "Undo", onAction: () => { self.hideToast(); self.setState(s => ({ headsHidden: (s.headsHidden || []).filter(x => x !== h.id) })); } });
        },
        safeLabel: h.level === "danger" ? "It is safe" : "Dismiss",
        cardStyle: "display:flex;gap:12px;align-items:flex-start;border-radius:12px;background:var(--panel);border:1px solid " + (h.level === "danger" ? "color-mix(in oklch, var(--err) 45%, var(--line))" : "var(--line)") + ";" +
          "box-shadow:inset 3px 0 0 " + (h.level === "danger" ? "var(--err)" : "var(--warn)") + ",0 1px 2px var(--sh-1);padding:" + (isMobile ? "13px 13px 13px 15px" : "14px 16px 14px 18px"),
        tileStyle: "flex:0 0 30px;width:30px;height:30px;border-radius:9px;display:flex;align-items:center;justify-content:center;" + GLYPH + "font-size:18px;" +
          (h.level === "danger" ? "background:var(--err-soft);color:var(--err-text)" : "background:var(--warn-soft);color:var(--warn-text)"),
        ruleStyle: "display:inline-flex;align-items:center;gap:5px;font-size:11px;font-weight:500;color:var(--muted);background:var(--sub);border-radius:999px;padding:2px 8px 2px 6px;white-space:nowrap",
      })),
      headsBtnStyle: "display:inline-flex;align-items:center;justify-content:center;gap:6px;border-radius:9px;font-size:13px;font-weight:600;white-space:nowrap;box-sizing:border-box;color:var(--text);background:var(--panel);border:1px solid var(--border2);" + (touch ? "min-height:44px;padding:0 14px;" : "min-height:32px;padding:0 12px;"),
      headsQuietStyle: "display:inline-flex;align-items:center;justify-content:center;border-radius:9px;font-size:13px;color:var(--muted);white-space:nowrap;box-sizing:border-box;" + (touch ? "min-height:44px;padding:0 12px;" : "min-height:32px;padding:0 10px;"),
      sinceAt: sl.at,
      sinceArrived: sl.arrived + " arrived",
      sinceGroups: sl.groups.map(g => {
        const leave = g.key === "leave";
        const done = leave && !!st.sinceArchived;
        return {
          n: g.n, label: done ? "archived" : g.label, note: g.note, icon: g.icon, isLeave: leave && !done, isDone: done,
          cellStyle: "display:flex;flex-direction:column;gap:3px;min-width:0;border-radius:10px;padding:" + (isMobile ? "10px 12px" : "11px 13px") + ";" +
            (g.key === "needs" ? "background:var(--accent-soft);box-shadow:inset 0 0 0 1px var(--accent-line);" : "background:var(--sub);") + (done ? "opacity:0.6;" : ""),
          nStyle: "display:flex;align-items:center;gap:7px;font-size:22px;font-weight:650;letter-spacing:-0.02em;font-variant-numeric:tabular-nums;color:" + (g.key === "needs" ? "var(--accent-d)" : "var(--text)"),
          glyph: GLYPH + "font-size:17px;color:" + (g.key === "needs" ? "var(--accent-d)" : "var(--muted)"),
        };
      }),
      sinceGridStyle: isMobile ? "display:flex;flex-direction:column;gap:8px" : "display:grid;grid-template-columns:repeat(3,1fr);gap:9px",
      sinceArchive: () => {
        self.setState({ sinceArchived: true });
        self.notify("Six newsletters and notifications are in Archive.", { title: "Archived", actionLabel: "Undo", onAction: () => { self.hideToast(); self.setState({ sinceArchived: false }); } });
      },
      sinceLinkStyle: "align-self:flex-start;display:inline-flex;align-items:center;gap:5px;font-size:12.5px;font-weight:600;color:var(--accent-d);border-radius:7px;" + (touch ? "min-height:44px;" : "margin-top:3px;"),
    });

    /* ——— The prototype's own control: what a return looks like ——— */
    const tryItems = () => [
      { icon: "flight_land", label: "Back after 16 days away", run: () => self.startCatchup("away") },
      { icon: "beach_access", label: "Back from a Holiday in the calendar", run: () => self.startCatchup("holiday") },
    ].concat(self.state.catchup ? [
      { icon: "hourglass_bottom", label: "Let 24 hours pass", run: () => self.setState(s => ({ catchup: Object.assign({}, s.catchup, { openedAt: Date.now() - DAY_MS - 1000 }) })) },
      { icon: "delete", label: "Forget the catch-up", run: () => self.setState({ catchup: null, catchupOpen: false }) },
    ] : []);
    Object.assign(v, {
      tryCatchup: (e) => {
        const p = self.localPt(e.clientX, e.clientY);
        self.armCtx(false);
        self.setState({ more: false, ctxMenu: { x: p.x + 14, y: Math.max(10, p.y - 190), title: "Prototype · the catch-up", detail: "What Start shows after an absence", items: tryItems() } });
      },
      tryBtnStyle: "display:flex;align-items:center;justify-content:center;border-radius:10px;color:var(--muted);border:1px dashed var(--border2);" + GLYPH + (touch ? "width:44px;height:44px;font-size:22px;" : "width:40px;height:40px;font-size:20px;"),
    });
    return v;
  }

  return { install, vals };
})();
