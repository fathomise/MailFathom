// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Fathom: the button in the bottom-right corner of Start and Mail and the answer card it writes into.
// `component.js` calls `install` once and merges `vals` into what the template reads; the sample content is
// `data/prototype/fathom.js`.
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
    /* A target is anything an action can act on: what the screen holds. */
    self.runFathom = (a, t) => {
      const closeAll = { fathomMenu: false, fathomAnswer: null };
      if (a.do === "answer") {
        self.setState({ fathomMenu: false, fathomAnswer: { title: a.label, about: t.label || "", text: answerText(a, t), ctx: t.label || a.label } });
        return;
      }
      if (a.do === "agent") {
        self.setState(closeAll);
        self.goAgentWith(a.ctx ? a.ctx + (t.label ? " · " + t.label : "") : t.label || "this");
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
      if (a.do === "go") {
        const go = a.go || t.go || (t.tid ? "thread" : t.pid ? "people" : "mail");
        self.setState(closeAll);
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

    self.onFathomKey = (e) => {
      const st = self.state;
      if (e.key !== "Escape" || st.ctxMenu) return;
      if (st.fathomAnswer) self.setState({ fathomAnswer: null });
      else if (st.fathomMenu) self.setState({ fathomMenu: false });
    };
    if (typeof window !== "undefined") window.addEventListener("keydown", self.onFathomKey, true);
  }

  function vals(self, c) {
    const { st, isMobile, touch, narrow } = c;
    const F = D().fathom;
    const barH = c.navBottom ? self.barH() : 0;

    /* ——— The Fathom button ——— */
    /* It stands on Start and Mail. */
    const supported = st.screen === "start";
    const menuKey = st.screen;
    const where = "your day";
    const fabShown = (supported || st.screen === "mail") && !st.compose && !st.doc && !st.htmlDoc && !st.settings &&
      !st.notifCenter && !st.replyDock && !(isMobile && (st.sel || []).length);
    const fabSize = isMobile ? 56 : 48;
    /* 24 px from both edges, 20 on a phone, above its bottom bar. */
    const fabEdge = isMobile ? 20 : 24;
    const fabBottom = fabEdge + barH;
    /* Beside an unfolded Right now panel the button gives way to the panel's own buttons, which hold the same choices;
       beside a folded one it stands on the strip, centred in its 56 px. */
    const inPanel = !!c.rightNowDocked && !c.rightNowFolded;
    const fabRight = c.rightNowDocked && c.rightNowFolded ? ((56 - fabSize) / 2) + "px" : fabEdge + "px";
    const menuRun = (a) => () => {
      if (a.do === "agent") { self.setState({ fathomMenu: false }); self.goAgentWith(a.ctx || where); return; }
      self.runFathom(a, { label: where });
    };
    const rowStyle = "display:flex;align-items:center;gap:10px;white-space:nowrap;color:var(--text);background:var(--panel);border:1px solid var(--line);border-radius:12px;box-shadow:0 10px 26px var(--sh-3),0 2px 6px var(--sh-2);font-weight:600;" +
      (touch ? "min-height:48px;padding:0 18px 0 14px;font-size:15px;" : "height:40px;padding:0 16px 0 12px;font-size:14px;");
    const v = {
      fathomFabShown: fabShown && !inPanel,
      fathomFabIcon: st.fathomMenu ? "close" : "auto_awesome",
      fathomFabTitle: st.fathomMenu ? "Close" : "Fathom - ask or act on what is on screen",
      fathomFabExpanded: !!st.fathomMenu,
      toggleFathom: () => {
        self.setState(s => ({ fathomMenu: !s.fathomMenu, fathomAnswer: null }));
      },
      fathomFabStyle: "position:fixed;z-index:88;right:" + fabRight + ";bottom:" + fabBottom + "px;width:" + fabSize + "px;height:" + fabSize + "px;box-sizing:border-box;" +
        "display:flex;align-items:center;justify-content:center;border-radius:" + (isMobile ? 18 : 14) + "px;color:var(--onaccent);background:var(--accent);box-shadow:0 10px 26px var(--sh-3),0 2px 6px var(--sh-2)",
      fathomFabGlyph: GLYPH + "font-size:22px;font-variation-settings:'wght' 400",
      fathomMenuOpen: fabShown && !inPanel && !!st.fathomMenu,
      closeFathomMenu: () => self.setState({ fathomMenu: false }),
      fathomMenuItems: [].concat(
        c.fathomCreate ? [c.fathomCreate] : [],
        (F.menu[menuKey] || []).map(a => Object.assign({}, a, { run: menuRun(a) })),
        c.fathomReply || [],
      ).map(a => ({ icon: a.icon, label: a.label, run: () => { self.setState({ fathomMenu: false }); a.run(); }, style: rowStyle })),
      panelActionsShown: inPanel && st.screen !== "agent",
      panelActionsNote: ({ start: "for your day", mail: st.threadOpen ? "for this conversation" : "for your mail", tasks: "for your tasks", cal: "for your calendar", people: "for your people" })[st.screen] || "",
      fathomRowStyle: rowStyle,
      fathomMenuStyle: "position:fixed;z-index:89;right:" + fabRight + ";bottom:" + (fabBottom + fabSize + (isMobile ? 12 : 8)) + "px;display:flex;flex-direction:column;align-items:flex-end;gap:8px",
      openAgentFromFathom: () => { self.setState({ fathomMenu: false }); self.goScreen("agent"); },
    };

    /* ——— The answer card Fathom writes into ——— */
    const ans = st.fathomAnswer;
    Object.assign(v, {
      panelActions: v.fathomMenuItems,
      fathomAnswerOpen: supported && !!ans,
      fathomAnswerTitle: ans ? ans.title : "",
      fathomAnswerAbout: ans ? ans.about : "",
      fathomAnswerHasAbout: !!(ans && ans.about),
      fathomAnswerText: ans ? ans.text : "",
      closeFathomAnswer: () => self.setState({ fathomAnswer: null }),
      fathomAnswerAgent: () => {
        const a = self.state.fathomAnswer;
        self.setState({ fathomAnswer: null });
        self.goAgentWith(a ? a.ctx : "Fathom");
      },
      fathomAnswerStyle: "position:fixed;z-index:97;box-sizing:border-box;display:flex;flex-direction:column;gap:10px;background:var(--panel);border:1px solid var(--accent-line);border-radius:16px;" +
        "box-shadow:0 22px 50px var(--sh-3),0 2px 6px var(--sh-2);padding:14px 16px 14px 16px;" +
        (isMobile ? "left:12px;right:12px;bottom:" + (fabBottom + fabSize + 12) + "px;"
          : "right:" + fabRight + ";bottom:" + (fabBottom + fabSize + 12) + "px;width:380px;"),
      fathomAnswerBtnStyle: "display:inline-flex;align-items:center;gap:7px;border-radius:9px;font-size:13px;font-weight:600;color:var(--onaccent);background:var(--accent);box-sizing:border-box;" + (touch ? "min-height:44px;padding:0 15px;" : "min-height:34px;padding:0 13px;"),
      fathomAnswerCloseStyle: "margin-left:auto;display:inline-flex;align-items:center;justify-content:center;border-radius:8px;color:var(--faint);" + GLYPH + (touch ? "width:44px;height:44px;font-size:20px;margin-right:-10px;" : "width:28px;height:28px;font-size:18px;margin-right:-6px;"),
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

    return v;
  }

  return { install, vals };
})();
