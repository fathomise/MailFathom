// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Fathom and the catch-up: the button in the bottom-right corner of Start and the Agent, its Smart mode, the answer card both
// write into, and the run of cards Start opens after an absence. `component.js` calls `install` once and merges `vals`
// into what the template reads; the sample content is `data/prototype/fathom.js` and `data/prototype/catchup.js`.
MailFathomDesign.fathomUi = (() => {
  const D = () => MailFathomDesign.data;
  const GLYPH = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;line-height:1;";

  const fill = (s, label) => String(s || "").replace("{label}", label || "this");

  /* What an `answer` says: the action's own line, else what the target carries for that action, else the fallback. */
  const answerText = (a, t) => {
    if (t.sel) return (a.line || "") + "“" + t.sel + "” " + (a.id === "translate" ? "(tłumaczenie w kliencie)" : "reads as the passage it was selected from, in two lines.");
    if (a.line) return a.line;
    const byAction = { summarize: t.short, more: t.more || t.short, translate: t.pl, why: t.text, want: t.text, decided: t.more || t.short };
    return byAction[a.id] || t.text || fill((D().fathom.fallback[a.id] || D().fathom.fallback.default), t.label);
  };

  function install(self) {
    /* Opening the catch-up: what Start does the first time it is opened after an absence. It stays on Start until the
       reader is done with it, or until the next absence replaces it. */
    self.startCatchup = (reason) => {
      self.timers.forEach(clearTimeout);
      self.setState({
        catchup: { reason: reason }, catchupOpen: true, catchupArchived: false,
        screen: "start", phase: "idle", fathomMenu: false, fathomSmart: false, fathomAnswer: null, ctxMenu: null, more: false,
      });
    };
    self.openCatchup = () => self.setState({ catchupOpen: true, fathomMenu: false, fathomSmart: false, fathomAnswer: null });
    self.closeCatchup = () => self.setState({ catchupOpen: false, fathomAnswer: null });
    self.doneCatchup = () => {
      const was = self.state.catchup;
      self.setState({ catchup: null, catchupOpen: false, fathomAnswer: null });
      self.notify("It is gone from Start. Nothing in your mail changed.", { title: "Catch-up done", actionLabel: "Undo", onAction: () => { self.hideToast(); self.setState({ catchup: was }); } });
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
      /* In a thread, drafting asks first what the reply should say, as the thread's own drafter does. */
      if (a.do === "draft") {
        if (self.state.screen === "mail" && self.state.threadOpen) { self.setState(Object.assign({}, closeAll, { replyDock: true })); return; }
        self.setState(closeAll); self.draftReply(); return;
      }
      if (a.do === "prep") {
        self.setState(Object.assign({ startPrep: "busy" }, closeAll));
        self.timers.push(setTimeout(() => self.setState({ startPrep: "ready" }), Math.max(600, (self.props.streamDelay ?? 550) * 1.6)));
        return;
      }
      if (a.do === "catchup") { self.setState(closeAll); self.openCatchup(); return; }
      if (a.do === "catchupDone") { self.setState(closeAll); self.doneCatchup(); return; }
      if (a.do === "catchupExport") { self.setState(closeAll); self.exportCatchup(t.x || 200, t.y || 200); return; }
      if (a.do === "go") {
        const go = a.go || t.go || (t.tid ? "thread" : t.pid ? "people" : "mail");
        self.setState(Object.assign({ catchupOpen: false }, closeAll));
        if (go === "thread") self.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: t.tid || "contoso", cameFrom: false });
        else if (go === "waiting") self.goScreen("mail", { mailPane: "list", threadOpen: false, cameFrom: false });
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
    self._smartKey = null;
    /* Smart from the keyboard: Tab and Shift+Tab step through the components on screen in reading order, Enter or Space
       opens the one outlined, and its menu takes the focus. */
    const smartList = () => Array.from(document.querySelectorAll("[data-smart]")).filter(el => {
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0 && r.bottom > 0 && r.top < window.innerHeight && !!smartOf(el);
    });
    const smartOutline = (el) => {
      const s = smartOf(el);
      self.setState({ smartHover: Object.assign({ id: s.id, name: s.comp.name, label: s.label }, smartRect(el)) });
    };
    const smartStep = (back) => {
      const list = smartList();
      if (!list.length) return;
      const at = list.indexOf(self._smartKey);
      const next = at < 0 ? (back ? list.length - 1 : 0) : (at + (back ? -1 : 1) + list.length) % list.length;
      self._smartKey = list[next];
      self._smartKey.scrollIntoView({ block: "nearest" });
      smartOutline(self._smartKey);
    };
    const smartOpenKeyed = () => {
      const el = self._smartKey;
      const s = smartOf(el);
      const r = smartRect(el);
      const x = r.x + 12, y = r.y + Math.min(r.h, 44);
      self.fathomMenuFor(s.comp.name, s.label || s.comp.what, s.comp.actions, { label: s.label || s.comp.name, text: s.text, tid: s.tid, x: x, y: y }, x, y);
      setTimeout(() => { const first = document.querySelector("[data-ctx-menu] [role=menuitem]"); if (first) first.focus(); }, 0);
    };
    self.onSmartMove = (e) => {
      if (!self.state.fathomSmart || self.state.ctxMenu) return;
      if (!e && self._smartKey && document.contains(self._smartKey)) { smartOutline(self._smartKey); return; }
      if (e && e.clientX != null) { self._smartPt = { x: e.clientX, y: e.clientY }; self._smartKey = null; }
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
      if (st.fathomSmart && !st.ctxMenu && e.key === "Tab") { e.preventDefault(); smartStep(e.shiftKey); return; }
      if (st.fathomSmart && !st.ctxMenu && (e.key === "Enter" || e.key === " ") && self._smartKey && document.contains(self._smartKey)) { e.preventDefault(); smartOpenKeyed(); return; }
      if (e.key === "Escape") {
        if (st.ctxMenu) return;
        if (st.fathomAnswer) self.setState({ fathomAnswer: null });
        else if (st.fathomSmart) { self._smartKey = null; self.setState({ fathomSmart: false, smartHover: null }); }
        else if (st.fathomMenu) self.setState({ fathomMenu: false });
        else if (st.catchupOpen) self.closeCatchup();
        return;
      }
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
      window.addEventListener("keydown", self.onFathomKey, true);
    }
  }

  function vals(self, c) {
    const { st, isMobile, touch, narrow } = c;
    const F = D().fathom, CU = D().catchup;
    const barH = isMobile ? self.barH() : 0;

    /* ——— The Fathom button ——— */
    const supported = st.screen === "start" || st.screen === "agent";
    const menuKey = st.screen === "agent" ? "agent" : "start";
    const where = st.screen === "agent" ? "the conversation" : "your day";
    const fabShown = supported && !st.catchupOpen && !st.compose && !st.doc && !st.htmlDoc && !st.settings && !st.notifCenter;
    const fabSize = isMobile ? 56 : 48;
    /* 24 px from both edges, 20 on a phone; in the Agent it stands level with the field it sits beside (`fathomDock`). */
    const fabEdge = isMobile ? 20 : 24;
    const fabBottom = st.screen === "agent" && st.fathomDock != null ? st.fathomDock : fabEdge + barH;
    const smartOn = supported && !!st.fathomSmart;
    const menuRun = (a) => () => {
      if (a.do === "agent") { self.setState({ fathomMenu: false }); self.goAgentWith(a.ctx || where); return; }
      self.runFathom(a, { label: where });
    };
    const rowStyle = "display:flex;align-items:center;gap:10px;white-space:nowrap;color:var(--text);background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 10px 26px var(--sh-3),0 2px 6px var(--sh-2);font-weight:600;" +
      (touch ? "min-height:48px;padding:0 18px 0 14px;font-size:15px;" : "height:40px;padding:0 16px 0 12px;font-size:14px;");
    const v = {
      fathomFabShown: fabShown,
      fathomFabIcon: st.fathomMenu || smartOn ? "close" : "auto_awesome",
      fathomFabTitle: smartOn ? "Leave Smart" : st.fathomMenu ? "Close" : "Fathom - ask or act on what is on screen",
      fathomFabExpanded: !!st.fathomMenu,
      toggleFathom: () => {
        if (self.state.fathomSmart) { self._smartKey = null; self.setState({ fathomSmart: false, smartHover: null }); return; }
        self.setState(s => ({ fathomMenu: !s.fathomMenu, fathomAnswer: null }));
      },
      fathomFabStyle: "position:fixed;z-index:88;right:" + fabEdge + "px;bottom:" + fabBottom + "px;width:" + fabSize + "px;height:" + fabSize + "px;box-sizing:border-box;" +
        "display:flex;align-items:center;justify-content:center;border-radius:" + (isMobile ? 18 : 14) + "px;color:var(--onaccent);background:var(--accent);box-shadow:0 10px 26px var(--sh-3),0 2px 6px var(--sh-2)",
      fathomFabGlyph: GLYPH + "font-size:22px;font-variation-settings:'wght' 400",
      fathomMenuOpen: supported && !!st.fathomMenu,
      closeFathomMenu: () => self.setState({ fathomMenu: false }),
      fathomMenuItems: (F.menu[menuKey] || []).map(a => ({ icon: a.icon, label: a.label, run: menuRun(a), style: rowStyle })),
      fathomRowStyle: rowStyle,
      fathomMenuStyle: "position:fixed;z-index:89;right:" + fabEdge + "px;bottom:" + (fabBottom + fabSize + (isMobile ? 12 : 8)) + "px;display:flex;flex-direction:column;align-items:flex-end;gap:8px",
      startSmart: () => { self._smartKey = null; self.setState({ fathomMenu: false, fathomSmart: true, smartHover: null }); },
      openAgentFromFathom: () => { self.setState({ fathomMenu: false }); self.goScreen("agent"); },
      smartHint: isMobile ? "Tap anything for its actions" : touch ? "Tap anything to see what Fathom can do with it" : "Point at anything, or Tab to it, to see what Fathom can do with it",
      smartOn,
      smartBarStyle: "position:fixed;z-index:87;left:50%;transform:translateX(-50%);top:" + (isMobile ? 10 : 14) + "px;display:flex;align-items:center;gap:10px;max-width:calc(100% - 24px);box-sizing:border-box;" +
        "background:var(--panel);border:1px solid var(--accent-line);border-radius:999px;box-shadow:0 10px 26px var(--sh-3);font-size:13px;color:var(--text2);" + (touch ? "padding:4px 4px 4px 14px;" : "padding:5px 5px 5px 14px;"),
      smartDoneStyle: "display:inline-flex;align-items:center;justify-content:center;border-radius:999px;font-weight:600;color:var(--onaccent);background:var(--accent);white-space:nowrap;" + (touch ? "min-height:44px;min-width:64px;padding:0 18px;font-size:14px;" : "min-height:30px;padding:0 13px;font-size:13px;"),
      leaveSmart: () => { self._smartKey = null; self.setState({ fathomSmart: false, smartHover: null }); },
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
    };

    /* ——— The answer card both Fathom and the catch-up write into ——— */
    const ans = st.fathomAnswer;
    Object.assign(v, {
      fathomAnswerOpen: supported && !!ans,
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

    /* ——— The catch-up: a card on Start, and one page of panels it opens ——— */
    const cu = st.catchup;
    const reason = cu ? CU.reasons[cu.reason] || CU.reasons.away : CU.reasons.away;
    const hello = CU.slides.find(sl => sl.id === "hello");
    const PANEL_ORDER = ["needs", "risks", "settled", "people", "topics", "archive"];
    const panels = CU.slides.filter(sl => sl.id !== "hello").sort((a, b) => PANEL_ORDER.indexOf(a.id) - PANEL_ORDER.indexOf(b.id));
    const needN = (CU.slides.find(sl => sl.id === "needs") || { items: [] }).items.length;
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
    const toneTile = (tone) => "flex:0 0 30px;width:30px;height:30px;border-radius:9px;display:flex;align-items:center;justify-content:center;" + GLYPH + "font-size:18px;" +
      (tone === "ok" ? "background:var(--ok-soft);color:var(--ok-text)" : tone === "warn" ? "background:var(--warn-soft);color:var(--warn-text)" : tone === "neutral" ? "background:var(--sub);color:var(--muted)" : "background:var(--accent-soft);color:var(--accent-d)");
    const quietBtn = "display:inline-flex;align-items:center;justify-content:center;gap:7px;border-radius:9px;font-size:13px;font-weight:600;white-space:nowrap;box-sizing:border-box;color:var(--text);background:var(--panel);border:1px solid var(--border2);" + (touch ? "min-height:44px;padding:0 14px;" : "min-height:32px;padding:0 12px;");
    const primaryBtn = "display:inline-flex;align-items:center;justify-content:center;gap:7px;border-radius:9px;font-size:13px;font-weight:600;white-space:nowrap;box-sizing:border-box;color:var(--onaccent);background:var(--accent);" + (touch ? "min-height:44px;padding:0 16px;" : "min-height:32px;padding:0 14px;");
    const iconBtn = "display:inline-flex;align-items:center;justify-content:center;border-radius:9px;color:var(--muted);" + GLYPH + (touch ? "width:44px;height:44px;font-size:22px;" : "width:32px;height:32px;font-size:19px;");
    const rowLine = (touch ? "min-height:52px;box-sizing:border-box;" : "") + "border-radius:10px;";
    const itemView = (sl, it, maxN) => ({
      n: it.n, label: it.label, sub: it.sub || "", badge: it.badge || "", hasBadge: !!it.badge, initials: it.initials || "", icon: it.icon || "",
      open: openItem(it),
      aria: (it.kind === "stat" ? it.n + " " + it.label : it.label) + " - actions",
      statStyle: "display:flex;flex-direction:column;gap:3px;border-radius:12px;padding:12px 14px;background:var(--panel);border:1px solid var(--line);text-align:left",
      statNStyle: "font-size:" + (isMobile ? 26 : 30) + "px;line-height:1;font-weight:650;letter-spacing:-0.02em;font-variant-numeric:tabular-nums",
      statLabelStyle: "font-size:12.5px;line-height:1.35;color:var(--text2)",
      rowStyle: "display:flex;align-items:flex-start;gap:11px;padding:10px 8px;" + rowLine,
      rowIconStyle: GLYPH + "font-size:19px;padding-top:1px;color:" + (it.level === "danger" ? "var(--err-text)" : sl.tone === "warn" ? "var(--warn-text)" : sl.tone === "ok" ? "var(--ok-text)" : "var(--accent-d)"),
      badgeStyle: "flex:0 0 auto;font-size:11px;font-weight:600;border-radius:999px;padding:3px 8px;white-space:nowrap;background:var(--hl);color:var(--text)",
      barRowStyle: "display:flex;align-items:center;gap:11px;padding:9px 8px;" + rowLine,
      barStyle: "height:6px;border-radius:3px;background:var(--accent);width:" + Math.round(((it.n || 0) / maxN) * 100) + "%",
      avatarStyle: "flex:0 0 30px;width:30px;height:30px;border-radius:9px;display:flex;align-items:center;justify-content:center;font-size:11px;font-weight:600;background:var(--accent-soft);color:var(--accent-d)",
      chipStyle: "display:inline-flex;align-items:baseline;gap:7px;border-radius:999px;font-weight:600;background:var(--sub);border:1px solid var(--line2);" +
        (it.size === 3 ? "font-size:20px;padding:9px 17px;" : it.size === 2 ? "font-size:16px;padding:8px 14px;" : "font-size:14px;padding:6px 12px;") + (touch ? "min-height:44px;box-sizing:border-box;" : ""),
      chipNStyle: "font-size:12px;font-weight:500;color:var(--muted);font-variant-numeric:tabular-nums",
    });
    Object.assign(v, {
      cuCardShown: !!cu && st.phase === "idle",
      cuCardTitle: "While you were away",
      cuCardSub: reason.label + " · " + needN + " still need you · on one page",
      cuCardIcon: cu && cu.reason === "holiday" ? "beach_access" : "flight_land",
      cuCardIconStyle: toneTile("accent"),
      cuCardStyle: (isMobile || narrow ? "order:2;" : "") + "display:flex;align-items:center;gap:12px;flex-wrap:wrap;box-sizing:border-box;border-radius:14px;background:var(--panel);border:1px solid var(--accent-line);box-shadow:inset 3px 0 0 var(--accent);" + (isMobile ? "padding:12px 12px 12px 15px;" : "padding:12px 14px 12px 17px;"),
      cuOpenBtnStyle: primaryBtn,
      cuQuietBtnStyle: quietBtn,
      openCatchup: self.openCatchup,
      cuDone: (e) => { if (e && e.stopPropagation) e.stopPropagation(); self.doneCatchup(); },

      cuOpen: !!st.catchupOpen && !!cu,
      cuScrimStyle: "position:fixed;inset:0;z-index:95;display:flex;align-items:center;justify-content:center;background:" + (isMobile ? "var(--bg)" : "var(--scrim)") + ";" + (isMobile ? "" : "padding:24px"),
      cuSheetStyle: "display:flex;flex-direction:column;box-sizing:border-box;overflow:hidden;background:var(--bg);color:var(--text);" +
        (isMobile ? "width:100%;height:100%;" : "width:min(1180px,100%);height:min(920px,100%);border-radius:18px;border:1px solid var(--line);box-shadow:0 30px 80px var(--sh-3);"),
      cuHeadStyle: "display:flex;align-items:center;gap:10px;flex:0 0 auto;background:var(--panel);border-bottom:1px solid var(--line);" + (isMobile ? "padding:10px 10px 10px 14px;" : "padding:14px 16px 14px 22px;"),
      cuHeadIconStyle: toneTile("accent"),
      cuHeadTitle: "While you were away",
      cuHeadSub: reason.label + " · " + CU.range,
      cuHeadLabels: !isMobile,
      cuIconBtn: iconBtn,
      cuQuiet: quietBtn,
      cuExport: (e) => { e.stopPropagation(); const p = self.localPt(e.clientX, e.clientY); self.exportCatchup(p.x - 220, p.y + 16); },
      cuClose: (e) => { if (e && e.stopPropagation) e.stopPropagation(); self.closeCatchup(); },
      cuBodyStyle: "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:18px;" + (isMobile ? "padding:16px 14px 28px;" : "padding:24px 26px 32px;"),
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
      cuHelloTitle: hello.title,
      cuHelloLede: reason.line + " " + hello.lede,
      cuHelloTitleStyle: "font-size:" + (isMobile ? 24 : 28) + "px;line-height:1.15;font-weight:600;letter-spacing:-0.015em;text-wrap:balance;user-select:text",
      cuHelloLedeStyle: "font-size:14px;line-height:1.5;color:var(--text2);text-wrap:pretty;user-select:text;max-width:70ch",
      cuStatsStyle: "display:grid;grid-template-columns:" + (isMobile ? "1fr 1fr" : "repeat(4,1fr)") + ";gap:10px",
      cuStats: hello.items.map(it => itemView(hello, it, 1)),
      cuHint: isMobile ? "Tap anything, or hold text, for more" : touch ? "Tap anything, or hold text, for more" : "Press anything, or select text, for more",
      cuGridStyle: isMobile ? "display:flex;flex-direction:column;gap:12px" : "column-count:" + (narrow ? 1 : 2) + ";column-gap:14px",
      cuPanels: panels.map(sl => {
        const maxN = Math.max.apply(null, sl.items.map(i => i.n || 0).concat([1]));
        const archived = sl.cta && sl.cta.id === "archive" && !!st.catchupArchived;
        return {
          key: sl.id, eyebrow: sl.eyebrow, title: sl.title, lede: sl.lede, icon: sl.icon,
          tileStyle: toneTile(sl.tone),
          panelStyle: "display:flex;flex-direction:column;gap:10px;break-inside:avoid;box-sizing:border-box;background:var(--panel);border:1px solid var(--line);border-radius:14px;" + (isMobile ? "padding:14px;" : "padding:16px 16px 14px;margin:0 0 14px;"),
          isList: sl.layout === "list", isBars: sl.layout === "bars", isChips: sl.layout === "chips", isStats: sl.layout === "stats",
          items: sl.items.map(it => itemView(sl, it, maxN)),
          hasCta: !!sl.cta,
          ctaLabel: sl.cta ? (archived ? "Archived · Undo" : sl.cta.label) : "",
          ctaIcon: sl.cta ? (archived ? "undo" : sl.cta.icon) : "",
          ctaStyle: archived ? quietBtn : primaryBtn,
          cta: (e) => {
            e.stopPropagation();
            const was = !!self.state.catchupArchived;
            self.setState({ catchupArchived: !was });
            if (!was) self.notify("They are in Archive, and the threads that need you are untouched.", { title: "221 messages archived", actionLabel: "Undo", onAction: () => { self.hideToast(); self.setState({ catchupArchived: false }); } });
          },
        };
      }),
      cuPanelStatsStyle: "display:grid;grid-template-columns:repeat(3,1fr);gap:8px",
    });

    /* ——— Start: what could hurt, and what arrived since the reader last looked ——— */
    const HOME = D().home;
    const heads = HOME.headsUp.filter(h => (st.headsHidden || []).indexOf(h.id) < 0).sort((x, y) => x.rank - y.rank);
    const sl = HOME.sinceLast;
    Object.assign(v, {
      hasHeadsUp: heads.length > 0,
      /* Most urgent first, whatever the kind: the order is how soon the harm lands. Each risk is one row of a ledger -
         its kind, what it is, how long is left, and what to check - with the reasons, the rule and *It is safe* under
         the row when it is unfolded. The first one carries the warn edge, so the list says where to start. */
      headsNote: isMobile ? "most urgent first" : "most urgent first · raised by rules from the messages themselves",
      headsListStyle: "display:flex;flex-direction:column;background:var(--panel);border:1px solid var(--line);border-radius:14px;overflow:hidden;box-shadow:0 1px 2px var(--sh-1)",
      headsUp: heads.map((h, i) => {
        const open = (st.headsOpen || null) === h.id;
        return {
          title: h.title, why: h.why, check: h.check, rule: h.rule, tid: h.tid, when: h.when,
          kindLabel: h.kind === "security" ? "Security" : "Deadline",
          kindIcon: h.kind === "security" ? "shield" : "event_busy",
          isOpen: open,
          chevron: open ? "expand_less" : "expand_more",
          toggleAria: (open ? "Fold " : "Why this was raised: ") + h.title,
          toggle: () => self.setState(s => ({ headsOpen: s.headsOpen === h.id ? null : h.id })),
          open: () => self.goScreen("mail", { mailPane: "thread", threadOpen: true, threadId: h.tid, cameFrom: false }),
          safe: () => {
            self.setState(s => ({ headsHidden: (s.headsHidden || []).concat([h.id]) }));
            self.notify("“" + h.title + "” will not be raised again.", { title: h.kind === "security" ? "Marked as safe" : "Dismissed", actionLabel: "Undo", onAction: () => { self.hideToast(); self.setState(s => ({ headsHidden: (s.headsHidden || []).filter(x => x !== h.id) })); } });
          },
          safeLabel: h.kind === "security" ? "It is safe" : "Dismiss",
          rowStyle: "display:flex;flex-direction:column;gap:10px;min-width:0;" + (i > 0 ? "border-top:1px solid var(--line2);" : "") +
            (i === 0 ? "box-shadow:inset 3px 0 0 var(--warn);" : "") + "padding:" + (isMobile ? "11px 8px 11px 13px" : "11px 10px 11px 16px") + (open ? ";background:var(--sub)" : ""),
          tileStyle: "flex:0 0 28px;width:28px;height:28px;border-radius:8px;display:flex;align-items:center;justify-content:center;" + GLYPH + "font-size:17px;" +
            (i === 0 ? "background:var(--warn-soft);color:var(--warn-text)" : h.kind === "security" ? "background:var(--sub);color:var(--err-text)" : "background:var(--sub);color:var(--muted)"),
          titleStyle: "min-width:0;font-size:14px;font-weight:600;line-height:1.35;" + (isMobile ? "text-wrap:pretty" : "overflow:hidden;text-overflow:ellipsis;white-space:nowrap"),
          lineStyle: "min-width:0;font-size:12.5px;line-height:1.4;color:var(--text2);" + (open ? "display:none" : "overflow:hidden;text-overflow:ellipsis;white-space:nowrap"),
          whenStyle: "flex:0 0 auto;font-size:11px;font-weight:600;white-space:nowrap;border-radius:999px;padding:2px 8px;" +
            (i === 0 ? "background:var(--warn-soft);color:var(--warn-text)" : "background:var(--sub);color:var(--text2)"),
          kindStyle: "font-weight:600;color:" + (h.kind === "security" ? "var(--err-text)" : "var(--warn-text)"),
          ruleStyle: "display:inline-flex;align-items:center;gap:5px;font-size:11px;font-weight:500;color:var(--muted);background:var(--panel);border-radius:999px;padding:2px 8px 2px 6px;white-space:nowrap",
        };
      }),
      headsOpenInline: !isMobile,
      headsOpenBelow: isMobile,
      headsTitleRowStyle: "display:flex;align-items:center;gap:6px 8px;min-width:0;" + (isMobile ? "flex-wrap:wrap" : ""),
      headsMoreStyle: "display:flex;flex-direction:column;gap:8px;padding-left:" + (isMobile ? 0 : 39) + "px",
      headsChevronStyle: "display:inline-flex;align-items:center;justify-content:center;box-sizing:border-box;border-radius:8px;color:var(--muted);" + GLYPH + "line-height:1;" + (touch ? "width:44px;height:44px;font-size:22px;" : "width:30px;height:30px;font-size:20px;"),
      headsBtnStyle: "display:inline-flex;align-items:center;justify-content:center;gap:6px;border-radius:9px;font-size:13px;font-weight:600;white-space:nowrap;box-sizing:border-box;color:var(--text);background:var(--panel);border:1px solid var(--border2);" + (touch ? "min-height:44px;padding:0 14px;" : "min-height:30px;padding:0 11px;"),
      headsQuietStyle: "display:inline-flex;align-items:center;justify-content:center;border-radius:9px;font-size:13px;color:var(--muted);white-space:nowrap;box-sizing:border-box;border:1px solid var(--border2);background:var(--panel);" + (touch ? "min-height:44px;padding:0 12px;" : "min-height:30px;padding:0 11px;"),
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
      sinceGridStyle: isMobile || c.startWide ? "display:flex;flex-direction:column;gap:8px" : "display:grid;grid-template-columns:repeat(3,1fr);gap:9px",
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
