// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The Settings panel's sections and every setting in them, as the prototype component's render reads them. The panel's
// frame — the scrim, the head, the navigation's size — stays in component.js beside the other surfaces; what a section
// holds, and what a write answers, is here.
//
// A write answers as the `settingsWrites` property says: `saved`, `refused` (the server refused the value), `notStored`
// (it did not store it), `unanswered` (nobody answered), or `moved` (somebody changed the folders since they were read,
// which only a folder write can meet). `settingsAccess` says what this sign-in may change: `full`, `nameManaged` (the
// administrator holds the name) or `readOnly`. `telemetry` says what the server said about telemetry, and
// `notificationPermission` what this browser was told about notifications.
MailFathomDesign.settingsPanel = (self, env) => {
  const { st, touch, offline, segStyle, sharedMark, inOrg } = env;
  const { settings: SETTINGS, ownFooters: OWN_FOOTERS, footers: FOOTERS, mailAccounts: MAIL_ACCOUNTS } = MailFathomDesign.data;
  const props = self.props;
  const writes = props.settingsWrites || "saved";
  const access = props.settingsAccess || "full";
  const set = (patch) => self.setState(patch);
  const merge = (key, patch) => self.setState(s => ({ [key]: Object.assign({}, s[key], patch) }));

  /* What a person changes after signing in waits on the server; the interface language, this device's notifications
     and its layout do not. */
  const profileLocked = offline || access === "readOnly";
  const prefsLocked = offline || access === "readOnly";
  const nameLocked = profileLocked || access === "nameManaged";

  const OUT = {
    name: {
      refused: "That name was not accepted. It cannot be blank or longer than 128 characters.",
      notStored: "That name was not saved to the server, so it still holds you under the old one.",
      unanswered: "The server did not answer, so it is not known whether that name was saved.",
    },
    photo: {
      refused: "That picture was not saved to the server, so you are still drawn as you were.",
      notStored: "That picture was not saved to the server, so you are still drawn as you were.",
      unanswered: "The server did not answer, so it is not known whether that picture was saved.",
    },
    timeZone: {
      refused: "That time zone was not accepted, so your dates are still written in the old one.",
      notStored: "That time zone was not saved to the server, so your dates are still written in the old one.",
      unanswered: "The server did not answer, so it is not known whether that time zone was saved.",
    },
    language: {
      refused: "That language was not accepted, so MailFathom still writes to you in the old one.",
      notStored: "That language was not saved to the server, so MailFathom still writes to you in the old one.",
      unanswered: "The server did not answer, so it is not known whether that language was saved.",
    },
    phones: {
      refused: "That change to your numbers was not accepted, so your card still shows the old ones.",
      notStored: "That change to your numbers was not saved to the server, so your card still shows the old ones.",
      unanswered: "The server did not answer, so it is not known whether that change to your numbers was saved.",
    },
    address: {
      refused: "That address was not accepted, so colleagues are still led to the old one.",
      notStored: "That address was not saved to the server, so colleagues are still led to the old one.",
      unanswered: "The server did not answer, so it is not known whether that address was saved.",
    },
    hours: {
      refused: "Those working hours were not accepted, so your card still shows the old ones.",
      notStored: "Those working hours were not saved to the server, so your card still shows the old ones.",
      unanswered: "The server did not answer, so it is not known whether those working hours were saved.",
    },
    prefs: {
      refused: "That change was not accepted, so the setting is as it was.",
      notStored: "That change was not saved to the server, so the setting is as it was.",
      unanswered: "The server did not answer, so it is not known whether that change was saved.",
    },
    seconds: {
      refused: "That was not changed. A notification stands for a whole number of seconds, between 1 and 30.",
      notStored: "That was not changed. A notification stands for a whole number of seconds, between 1 and 30.",
      unanswered: "The server did not answer, so it is not known whether that change was saved.",
    },
  };
  const out = st.setOut || {};
  const problemOf = (key) => (out[key] && out[key] !== "saved" && OUT[key] ? OUT[key][out[key] === "moved" ? "notStored" : out[key]] : "");
  /* A write that is saved applies its patch; any other answer leaves the value as it was and says so. */
  const write = (key, patch) => self.setState(s => {
    const p = typeof patch === "function" ? patch(s) : patch;
    return Object.assign(writes === "saved" ? p : {}, { setOut: Object.assign({}, s.setOut, { [key]: writes }) });
  });

  /* Shapes the sections share. A field, a select and a button are 44 under a finger; a segment is 36 in its track. */
  const fieldPad = touch ? "min-height:44px;padding:0 12px;" : "padding:8px 10px;";
  const field = "width:100%;max-width:440px;box-sizing:border-box;font-family:inherit;font-size:14px;color:var(--text);background:var(--sub);border:1px solid var(--border2);border-radius:8px;outline:none;" + fieldPad;
  const fieldRo = "width:100%;max-width:440px;box-sizing:border-box;font-family:inherit;font-size:14px;color:var(--text2);background:var(--bg);border:1px solid var(--line);border-radius:8px;outline:none;cursor:default;" + fieldPad;
  const btn = (kind) => "display:flex;align-items:center;justify-content:center;align-self:flex-start;gap:6px;border-radius:8px;font-size:14px;font-weight:600;white-space:nowrap;box-sizing:border-box;" + (touch ? "min-height:44px;padding:0 16px;" : "padding:9px 16px;") + kind;
  const quiet = btn("background:var(--sub);border:1px solid var(--border2);color:var(--text2);");
  const primary = (on) => btn(on ? "background:var(--accent);color:var(--onaccent);" : "background:var(--border2);color:var(--panel);opacity:0.6;");
  const sw = (on, dim) => "flex:0 0 auto;margin-top:2px;width:30px;height:17px;border-radius:8px;display:flex;align-items:center;padding:2px;background:var(" + (on ? "--accent" : "--border2") + ");justify-content:" + (on ? "flex-end" : "flex-start") + (dim ? ";opacity:0.5" : "");
  const row = (dim) => "display:flex;align-items:flex-start;gap:11px;padding:9px 10px;max-width:620px;border-radius:8px;box-sizing:border-box;" + (touch ? "min-height:44px;" : "") + (dim ? "opacity:0.55;" : "");
  const seg = (on) => segStyle(on) + (touch ? ";min-height:36px;display:flex;align-items:center;justify-content:center;box-sizing:border-box" : "");
  const segTrack = "display:flex;gap:4px;padding:3px;max-width:620px;border:1px solid var(--border2);border-radius:8px;background:var(--sub)";
  const chip = (on, dim) => "display:flex;align-items:center;justify-content:center;box-sizing:border-box;border-radius:8px;font-size:13px;" + (touch ? "min-width:44px;min-height:36px;padding:0 10px;" : "min-width:40px;padding:6px 9px;") +
    (on ? "background:var(--accent);color:var(--onaccent);font-weight:600;" : "color:var(--text2);border:1px solid var(--line);background:var(--sub);") + (dim ? "opacity:0.55;" : "");
  const LOCKED_TIP = offline ? "Not connected to the server - this waits until it is back." : "This sign-in may not change it.";

  /* ---------- The sections ---------- */
  const SECTIONS = [
    { v: "profil", label: "Profile", icon: "person", hint: "Your name and picture, your language and time zone, and how colleagues reach you.", words: "name photo picture language time zone phone number address" },
    { v: "away", label: "Availability", icon: "event_available", hint: "When you work, and what MailFathom answers while you are away.", words: "working hours automatic reply out of office away vacation" },
    { v: "accounts", label: "Accounts", icon: "alternate_email", hint: "The mail accounts your administrator gave you, and everything about each that is yours to decide: its name and language, deleting, spam, contacts, scanning, sending, its folders and its footer.", words: "account name language push polling delete deletion permanently expunge spam junk contacts collect exclusions scan secrets personal data screen outgoing sender trust strict domains send from signing smime openpgp folder synchronize footer signature embeddings" },
    { v: "certs", label: "Certificates", icon: "key", hint: "The certificates and keys you sign and encrypt mail with, added once and chosen for each account.", words: "certificate key smime s/mime openpgp pgp gpg sign signing signature encrypt encryption p12 pfx asc" },
    { v: "reading", label: "Reading", icon: "chrome_reader_mode", hint: "How a message opens, and what opening it does.", words: "message view simplified original expand thread mark read ai filters" },
    { v: "ai", label: "AI", icon: "auto_awesome", hint: "The language the assistant writes in, and when it follows the message instead.", words: "language reply match" },
    { v: "notif", label: "Notifications", icon: "notifications", hint: "What reaches you, on which device, and for how long it stays.", words: "notification device seconds remove" },
    { v: "app", label: "Application", icon: "tune", hint: "The interface language, the theme, how mail is laid out, and what this device sends.", words: "language theme dark light tabs list conversations images telemetry layout" },
  ];
  const ORDER = SECTIONS.map(x => x.v);
  const cur = ORDER.includes(st.settingsTab) ? st.settingsTab : "profil";
  const q = (st.settingsQuery || "").trim().toLowerCase();
  const shown = SECTIONS.filter(x => !q || (x.label + " " + x.hint + " " + x.words).toLowerCase().includes(q));
  const curSec = SECTIONS.find(x => x.v === cur);
  const pickSectionNow = (v) => set({ settingsTab: v, acctDraft: null, folderEdit: null, footerEdit: null, acctForm: null, acctOut: null, certDraft: null, certOut: null });
  const pickSection = (v) => (self._settingsGuard ? self._settingsGuard(() => pickSectionNow(v)) : pickSectionNow(v));
  const navItem = (x) => ({
    label: x.label, icon: x.icon,
    pick: () => pickSection(x.v),
    navStyle: "display:flex;align-items:center;gap:11px;border-radius:8px;box-sizing:border-box;" + (touch ? "min-height:44px;padding:0 12px;" : "padding:8px 12px;") + "font-size:14px;" +
      (cur === x.v ? "background:var(--accent-soft);color:var(--accent-d);font-weight:600;" : "color:var(--text2);"),
    iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:19px;line-height:1;width:20px;text-align:center;" + (cur === x.v ? "" : "color:var(--muted);"),
  });

  /* ---------- Profile ---------- */
  const myName = env.myName;
  const nameBad = !myName.trim() || myName.length > 128;
  const tz = st.setTimeZone || SETTINGS.timeZone;
  const lang = st.setLanguage || SETTINGS.language;
  const LANGS = [["en", "English"], ["pl", "Polski"]];
  const phones = st.setPhones || SETTINGS.phoneNumbers;
  const PHONE_LABELS = [["mobile", "Mobile"], ["work", "Work"], ["home", "Home"], ["other", "Other"]];
  const phoneBad = (n) => !/^[0-9+\-(). ]{1,32}$/.test(n.trim()) || !/[0-9]/.test(n);
  const writePhones = (f) => write("phones", s => ({ setPhones: f((s.setPhones || SETTINGS.phoneNumbers).slice()) }));
  const editPhone = (i, k, v) => profileLocked ? null : set({ setPhones: phones.map((p, j) => j === i ? Object.assign({}, p, { [k]: v }) : p), setOut: Object.assign({}, st.setOut, { phones: null }) });
  const own = MAIL_ACCOUNTS.filter(a => !a.shared);
  const addr = st.setAddress || SETTINGS.preferredAddress;

  /* ---------- Availability ---------- */
  const DAYS = [["mon", "Mon"], ["tue", "Tue"], ["wed", "Wed"], ["thu", "Thu"], ["fri", "Fri"], ["sat", "Sat"], ["sun", "Sun"]];
  const hours = st.setHours || SETTINGS.workingHours;
  const hoursDraft = st.hoursDraft || hours;
  const hoursBad = hoursDraft.days.length > 0 && !(hoursDraft.start < hoursDraft.end);
  const writeHours = (h) => {
    if (profileLocked) return;
    set({ hoursDraft: h });
    if (h.days.length && !(h.start < h.end)) return;
    write("hours", { setHours: h, hoursDraft: null });
  };
  const tzLabel = (SETTINGS.timeZones.find(z => z[0] === tz) || [tz, tz])[1];

  const reply = st.setReply || SETTINGS.automaticReply;
  const replyDraft = st.replyDraft || reply;
  const editReply = (patch) => profileLocked ? null : set({ replyDraft: Object.assign({}, replyDraft, patch), replyOut: null });
  const LONGEST = 8000;
  const replyProblems = [
    !replyDraft.outsideText.trim() ? "write the text for people outside your organization" : "",
    replyDraft.outsideText.length > LONGEST || (replyDraft.insideText || "").length > LONGEST ? "shorten a text to at most 8000 characters" : "",
    !replyDraft.accounts.length ? "pick at least one account" : "",
    !(replyDraft.startsAt < replyDraft.endsAt) ? "end the period after it starts" : "",
  ].filter(Boolean);
  const NOW = "2026-08-27T10:47";
  const fmt = (v) => new Date(v).toLocaleString("en-GB", { weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
  const replyState = !reply.on ? "off" : NOW < reply.startsAt ? "later" : NOW < reply.endsAt ? "now" : "ended";
  const REPLY_OUT = {
    saved: "Saved. Every sender is answered once under this reply, even one an earlier reply already answered.",
    refused: "The server did not take that reply: Nordwind board is a shared mailbox, which is never one person's to answer for.",
    notStored: "That reply was not saved to the server, so the one before it still holds.",
    unanswered: "The server did not answer, so it is not known whether that reply was saved.",
    moved: "That reply was not saved to the server, so the one before it still holds.",
  };

  /* ---------- Accounts ---------- */
  const accounts = st.setAccounts || MAIL_ACCOUNTS;
  const draft = st.acctDraft ? accounts.find(a => a.id === st.acctDraft) || null : null;
  const ROLES = [["inbox", "Inbox", "inbox"], ["sent", "Sent", "send"], ["drafts", "Drafts", "draft"], ["archive", "Archive", "archive"], ["junk", "Spam", "report"], ["trash", "Trash", "delete"]];
  const roleOf = (r) => ROLES.find(x => x[0] === r);
  const folderLocked = offline || access === "readOnly";
  const fe = st.folderEdit || null;
  const feDraft = fe ? fe.draft : null;
  const others = draft && fe ? draft.folders.filter((_, i) => i !== fe.index) : [];
  const feProblem = !feDraft ? "" :
    !feDraft.alias.trim() ? "Give the folder a name." :
    feDraft.alias.trim().length > 128 ? "A folder name is 128 characters at most." :
    /[\u0000-\u001f\u007f]/.test(feDraft.alias) ? "A folder name is one line of plain text." :
    /^role:/i.test(feDraft.alias.trim()) ? "A folder name may not start with role:, which names a role rather than a folder." :
    feDraft.alias.trim().split("/").length > 3 ? "A folder name goes three levels deep at most, such as Projects/2026/Q3." :
    others.some(f => f.alias.toLowerCase() === feDraft.alias.trim().toLowerCase()) ? "This account already has a folder named " + feDraft.alias.trim() + "." :
    feDraft.by === "path" && !feDraft.pathText.trim() ? "Say where the folder sits on the mail server." :
    feDraft.by === "path" && /^\/|\/$/.test(feDraft.pathText.trim()) ? "A server path neither starts nor ends with /." :
    feDraft.by === "role" && others.some(f => f.role === feDraft.role) ? "A role belongs to one folder of an account, and " + roleOf(feDraft.role)[1] + " is already " + others.find(f => f.role === feDraft.role).alias + "'s." :
    "";
  const FOLDER_OUT = {
    saved: "Saved. The account takes the change on its next run.",
    refused: "The server did not take that change: the mail server names no folder at that path, and this folder may not create one.",
    notStored: "That change was not saved to the server, so the folders are as they were.",
    unanswered: "The server did not answer, so it is not known whether that change was saved.",
    moved: "These folders were changed somewhere else since this screen read them, so nothing was saved. They are shown as they stand now - make the change again.",
  };
  const answerFolder = (apply) => {
    const outcome = writes;
    self.setState(s => {
      const list = (s.setAccounts || MAIL_ACCOUNTS).map(a => a.id === s.acctDraft ? Object.assign({}, a, { folders: outcome === "saved" ? apply(a.folders.slice()) : a.folders }) : a);
      return { setAccounts: list, folderEdit: outcome === "saved" || outcome === "moved" ? null : s.folderEdit, folderOut: outcome };
    });
  };
  const folderOf = (d) => ({
    alias: d.alias.trim(), role: d.by === "role" ? d.role : "",
    path: d.by === "path" ? d.pathText.split("/").map(x => x.trim()).filter(Boolean) : [],
    sync: d.sync, ai: d.ai, embed: d.ai && d.embed, create: d.by === "path" && d.create,
  });
  const draftOf = (f) => ({ alias: f.alias, by: f.role ? "role" : "path", role: f.role || "inbox", pathText: (f.path || []).join("/"), sync: f.sync, ai: f.ai, embed: f.embed, create: f.create });
  const editFe = (patch) => set({ folderEdit: Object.assign({}, fe, { draft: Object.assign({}, feDraft, patch) }), folderOut: null });
  const feRows = !draft ? [] : draft.folders.map((f, i) => ({ f, i })).concat(fe && fe.index == null ? [{ f: null, i: null }] : []);

  const ownFooters = st.setOwnFooters || OWN_FOOTERS;
  const ownFooter = draft ? ownFooters[draft.id] || null : null;
  const fte = st.footerEdit || null;
  const markupLines = (markup) => markup
    .replace(/<img[^>]*src="cid:([^"]+)"[^>]*>/g, "\n[[img:$1]]\n")
    .replace(/<br\s*\/?>|<\/p>|<\/div>/g, "\n")
    .split("\n")
    .map(raw => ({ strong: /<(strong|b)>/.test(raw), t: raw.replace(/<[^>]+>/g, "").replace(/&amp;/g, "&").replace(/&lt;/g, "<").replace(/&gt;/g, ">").trim() }))
    .filter(x => x.t)
    .map(({ strong, t }) => {
      const m = t.match(/^\[\[img:(.+)\]\]$/);
      return m ? { text: "cid:" + m[1], image: true } : { text: t, image: false, strong };
    });
  const linked = fte ? (fte.markup.match(/<img[^>]*src="(https?:[^"]+)"/) || [])[1] || "" : "";
  const ftUsed = fte ? fte.images.filter(im => fte.markup.indexOf("cid:" + im.name) >= 0).length : 0;
  const ftLimit = !fte ? "" : fte.markup.length > 16384 ? "A footer is 16384 characters of markup at most - shorten it." : ftUsed > 5 ? "A footer carries five images at most - remove " + (ftUsed - 5) + "." : "";
  const ftBlock = linked ? "Upload the linked image first." : ftLimit ? ftLimit : "";
  const FOOTER_OUT = {
    saved: "Saved. The next message you send from this account carries it.",
    refused: "Your organization now sets the footer on this account, so yours was not saved.",
    notStored: "That footer was not saved to the server, so messages still go out with the one before it.",
    unanswered: "The server did not answer, so it is not known whether that footer was saved.",
    moved: "That footer was not saved to the server, so messages still go out with the one before it.",
  };
  const answerFooter = (value) => {
    const outcome = writes;
    self.setState(s => ({
      setOwnFooters: outcome === "saved" ? Object.assign({}, s.setOwnFooters || OWN_FOOTERS, { [s.acctDraft]: value }) : (s.setOwnFooters || OWN_FOOTERS),
      footerEdit: outcome === "saved" ? null : s.footerEdit,
      footerOut: outcome,
    }));
  };
  const footerDocLines = (lines) => lines.map(l => ({
    text: l.text, image: !!l.image,
    style: l.image
      ? "align-self:flex-start;display:flex;align-items:center;gap:6px;font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--muted);border:1px dashed var(--border2);border-radius:6px;padding:6px 9px"
      : "font-size:" + (l.small ? 12 : 13) + "px;line-height:1.5;color:var(--" + (l.small ? "faint" : "text2") + ");" + (l.strong ? "font-weight:600;color:var(--text);" : ""),
  }));

  /* The account's own declaration — everything about it the person may change but its folders and footer, which are
     written one at a time beside it. One draft, one Save, answered against the version that was read. */
  const clone = (o) => JSON.parse(JSON.stringify(o));
  const savedDecl = draft ? Object.assign({ name: draft.name }, clone(draft.settings)) : null;
  const decl = draft && st.acctForm && st.acctForm.id === draft.id ? st.acctForm.v : savedDecl;
  const declDirty = !!draft && JSON.stringify(decl) !== JSON.stringify(savedDecl);
  const getAt = (path) => path.split(".").reduce((o, k) => (o == null ? o : o[k]), decl);
  const putAt = (path, value) => {
    if (folderLocked || !draft) return;
    const v = clone(decl);
    const keys = path.split(".");
    keys.slice(0, -1).reduce((o, k) => o[k], v)[keys[keys.length - 1]] = value;
    set({ acctForm: { id: draft.id, v }, acctOut: null });
  };
  const isInt = (n) => typeof n === "number" && Number.isInteger(n);
  /* What the service refuses, checked as it is typed so the field says it before Save does. */
  const ADDRESS = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
  const CONTROL = /[\u0000-\u001f\u007f]/;
  const DECL_CHECKS = {
    name: (v) => !v.trim() ? "Give the account a name." : v.trim().length > 128 ? "A name is 128 characters at most." : CONTROL.test(v) ? "A name is one line of plain text." : accounts.some(a => a.id !== draft.id && a.name.toLowerCase() === v.trim().toLowerCase()) ? "Another of your accounts is already called " + v.trim() + ", so a request naming it could not say which one it meant." : "",
    "contacts.minMessages": (v) => !isInt(v) || v < 1 || v > 100 ? "A whole number from 1 to 100." : "",
    "sending.fromAddress": (v) => !v ? "" : /\s/.test(v) || CONTROL.test(v) || (v.match(/@/g) || []).length !== 1 || !ADDRESS.test(v) ? "That is not an address - it reads name@domain, with no spaces." : v.length > 320 ? "An address is 320 characters at most." : "",
  };
  const folderNames = draft ? draft.folders.map(f => f.alias) : [];
  const inboxName = draft ? (draft.folders.find(f => f.role === "inbox") || { alias: "Inbox" }).alias : "Inbox";
  const scanned = decl ? decl.spam.folders || [inboxName] : [];
  const screened = decl ? decl.sensitive.screenOutgoing || [] : [];
  const hiddenScanned = draft ? draft.folders.filter(f => scanned.indexOf(f.alias) >= 0 && (!f.ai || !f.sync)).map(f => f.alias) : [];
  const aiScanProblem = decl && decl.spam.on && decl.spam.aiScanning && hiddenScanned.length ? "AI spam scanning reads " + hiddenScanned.join(", ") + ", which " + (hiddenScanned.length === 1 ? "is" : "are") + " hidden from AI or not synchronized - make " + (hiddenScanned.length === 1 ? "it" : "them") + " visible, or leave " + (hiddenScanned.length === 1 ? "it" : "them") + " out." : "";
  const hasJunkRole = !!draft && draft.folders.some(f => f.role === "junk");
  const junkProblem = decl && decl.spam.on && decl.spam.moveToJunk && !decl.spam.junkFolder && !hasJunkRole ? "This account has no Spam folder, and MailFathom does not create one - pick where spam goes." : "";
  const checkOf = (path) => DECL_CHECKS[path] ? DECL_CHECKS[path](getAt(path)) : "";
  const declProblem = !decl ? "" :
    Object.keys(DECL_CHECKS).map(checkOf).find(Boolean) ||
    (decl.spam.on && decl.spam.folders && decl.spam.folders.length === 0 ? "Pick at least one folder to classify, or turn classification off." : "") ||
    aiScanProblem || junkProblem ||
    (decl.sending.encryptWith === "same" && !decl.sending.signWith ? "Pick what to encrypt with - nothing is signed, so there is nothing to take the same from." : "");
  const ACCT_OUT = {
    saved: "Saved. The account works this way from its next run.",
    refused: "The server did not take that change: this deployment screens outgoing mail for personal data, so this account may not stop screening for it.",
    notStored: "That change was not saved to the server, so the account works as it did.",
    unanswered: "The server did not answer, so it is not known whether that change was saved.",
    moved: "This account was changed somewhere else since this screen read it, so nothing was saved. It is shown as it stands now - make the change again.",
  };
  const saveDecl = () => {
    if (!draft || folderLocked || declProblem || !declDirty) return;
    const outcome = writes;
    self.setState(s => {
      const v = clone(decl);
      v.name = v.name.trim();
      const name = v.name;
      delete v.name;
      return {
        setAccounts: outcome === "saved" ? (s.setAccounts || MAIL_ACCOUNTS).map(a => a.id === draft.id ? Object.assign({}, a, { name, settings: v }) : a) : (s.setAccounts || MAIL_ACCOUNTS),
        acctForm: outcome === "saved" || outcome === "moved" ? null : s.acctForm,
        acctOut: outcome,
      };
    });
  };
  const lockDim = folderLocked ? ";opacity:0.55" : "";
  const lockTip = folderLocked ? LOCKED_TIP : undefined;
  const item = {
    text: (label, hint, path, o = {}) => ({ isText: true, label, hint, value: getAt(path) == null ? "" : getAt(path), placeholder: o.placeholder || "", max: o.max || 512, problem: checkOf(path),
      style: (folderLocked ? fieldRo : field) + (o.mono ? "font-family:'Geist Mono',ui-monospace,monospace;font-size:13px;" : ""),
      onInput: (e) => putAt(path, o.empty !== undefined && !e.target.value.trim() ? o.empty : e.target.value) }),
    number: (label, hint, path, o = {}) => ({ isNumber: true, label, hint, value: getAt(path), min: o.min, max: o.max, step: o.step || 1, problem: checkOf(path),
      style: (folderLocked ? fieldRo : field) + "max-width:140px;",
      onInput: (e) => putAt(path, e.target.value === "" ? null : Number(e.target.value)) }),
    seg: (label, hint, path, options) => ({ isSeg: true, label, hint, track: segTrack + lockDim, tip: lockTip,
      segs: options.map(([v, l]) => ({ label: l, style: seg(getAt(path) === v), pick: () => putAt(path, v) })) }),
    select: (label, hint, path, options) => ({ isSelect: true, label, hint, value: getAt(path) == null ? "" : getAt(path),
      options: options.map(([v, l]) => ({ v: v == null ? "" : v, label: l })), style: folderLocked ? fieldRo : field,
      onChange: (e) => putAt(path, e.target.value === "" ? null : e.target.value) }),
    sw: (label, hint, path, o = {}) => {
      const off = !!o.off;
      const on = !off && !!getAt(path);
      return { isSwitch: true, label, hint, tip: off ? o.tip : lockTip, refused: off || folderLocked,
        rowStyle: row(off || folderLocked), switchStyle: sw(on, off || folderLocked),
        toggle: () => off ? null : putAt(path, !getAt(path)) };
    },
    chips: (label, hint, chips) => ({ isChips: true, label, hint, tip: lockTip,
      chips: chips.map(c => ({ label: c.label, style: chip(c.on, folderLocked), pick: () => folderLocked ? null : c.pick() })) }),
  };
  const toggleIn = (list, v) => list.indexOf(v) >= 0 ? list.filter(x => x !== v) : list.concat([v]);
  const CERTS = st.setCertificates || SETTINGS.certificates;
  const certLabel = (c) => (c.kind === "smime" ? "S/MIME - " : "OpenPGP - ") + c.name;
  const signOptions = [[null, "Not signed"]].concat(CERTS.map(c => [c.id, certLabel(c)]));
  const encryptOptions = [[null, "Not encrypted"], ["same", "The same as for signing"]].concat(CERTS.map(c => [c.id, certLabel(c)]));
  const exclDraft = st.exclDraft || "";
  const exclKind = /[@*?]/.test(exclDraft) ? "pattern" : "domain";
  const exclV = exclDraft.trim();
  const exclProblem = !exclV ? "" :
    /\s/.test(exclV) ? "An address, a pattern or a domain has no spaces." :
    decl && decl.contacts.exclusions.some(x => (x.domain || x.pattern).toLowerCase() === exclV.toLowerCase()) ? exclV + " is already on the list." :
    exclKind === "pattern"
      ? (exclV.length > 320 ? "A pattern is 320 characters at most." : /^[*?@]+$/.test(exclV) ? "A pattern made only of *, ? and @ would match everybody." : "")
      : (exclV.length > 253 ? "A domain is 253 characters at most." : exclV.split(".").some(l => l.length < 1 || l.length > 63) ? "A domain is names of 1 to 63 characters between dots, such as lists.example." : "");
  const acctGroups = !decl ? [] : [
    { title: "The account", hint: "", items: [
      item.text("Name", "What this account is called in your folder list and wherever a mailbox is named. Another of your accounts may not share it.", "name", { max: 128 }),
      item.seg("Language", "What MailFathom writes about this mailbox's mail on its own - readings, summaries, classifications - is in this language, for everyone the account is assigned to.", "language", [["English", "English"], ["Polish", "Polski"]]),
    ] },
    { title: "Deleting", hint: "", items: [
      { isSwitch: true, label: "Delete mail permanently - here and on the mail server",
        hint: decl.authoredDeleteServer === "Expunge"
          ? "A message you delete leaves MailFathom and is removed from the mail server too."
          : "A message you delete leaves MailFathom only. It stays on the mail server untouched - neither flagged nor removed - and MailFathom never brings it back.",
        tip: lockTip, refused: folderLocked, rowStyle: row(folderLocked), switchStyle: sw(decl.authoredDeleteServer === "Expunge", folderLocked),
        toggle: () => putAt("authoredDeleteServer", decl.authoredDeleteServer === "Expunge" ? "LeaveOnServer" : "Expunge") },
    ] },
    { title: "Recognizing senders", hint: "How the mark beside a sender is decided on this account.", items: [
      item.sw("Recognize everybody at your own domains", "Anybody writing from a domain one of your accounts uses is recognized, not only your own addresses. Right for a company domain, wrong for a large provider.", "trustOwnDomains"),
      item.sw("Mark only senders something established", "Off, a contact's address is marked known even when nothing proves the message came from them. On, only what was proven is marked.", "strict"),
    ] },
    { title: "Contacts", hint: "", items: [
      item.sw("Collect contacts from this account's mail", "Records the people you correspond with, so they are recognized and offered when you write. It is a record about other people, so it is off until you turn it on.", "contacts.on"),
      Object.assign(item.number("Messages before somebody is recorded", "How many messages an address has to have written first.", "contacts.minMessages", { min: 1 }), { dim: !decl.contacts.on }),
      { isExcl: true, dim: !decl.contacts.on, label: "Never recorded", hint: "Addresses, patterns such as noreply@*, and domains this account collects nobody from.",
        rows: decl.contacts.exclusions.map((x, i) => ({
          text: x.domain || x.pattern, kind: x.domain ? "Domain" : "Pattern",
          isDomain: !!x.domain, subLabel: "With subdomains", subStyle: chip(!!x.subdomains, folderLocked),
          toggleSub: () => putAt("contacts.exclusions", decl.contacts.exclusions.map((y, j) => j === i ? Object.assign({}, y, { subdomains: !y.subdomains }) : y)),
          remove: () => putAt("contacts.exclusions", decl.contacts.exclusions.filter((_, j) => j !== i)),
        })),
        none: decl.contacts.exclusions.length === 0,
        draft: exclDraft, onDraft: (e) => set({ exclDraft: e.target.value }), problem: exclProblem,
        addShown: !folderLocked,
        addLabel: exclDraft.trim() ? "Add " + exclKind : "Add",
        addStyle: quiet + (!exclDraft.trim() || exclProblem ? "opacity:0.55;" : ""),
        add: () => {
          if (!exclDraft.trim() || exclProblem) return;
          const v = exclDraft.trim();
          set({ exclDraft: "" });
          putAt("contacts.exclusions", decl.contacts.exclusions.concat([exclKind === "pattern" ? { pattern: v } : { domain: v, subdomains: false }]));
        } },
    ] },
    { title: "Spam", hint: "", items: [
      item.sw("Classify this account's mail as spam", "Off, nothing is called spam here except what the mail server already filed.", "spam.on"),
      Object.assign(item.chips("Folders it reads", "Mail arriving in these folders is classified.", folderNames.map(n => ({ label: n, on: scanned.indexOf(n) >= 0, pick: () => putAt("spam.folders", toggleIn(scanned, n)) }))), { dim: !decl.spam.on }),
      item.sw("Move spam to the junk folder", "On the mail server too.", "spam.moveToJunk", { off: !decl.spam.on, tip: "Turn on classification first." }),
      Object.assign(item.select("Junk folder", "", "spam.junkFolder", (hasJunkRole ? [[null, "The account's Spam folder"]] : [[null, "None picked"]]).concat(folderNames.map(n => [n, n]))), { dim: !decl.spam.on || !decl.spam.moveToJunk, problem: junkProblem }),
      item.sw("Mark spam read", "", "spam.markRead", { off: !decl.spam.on, tip: "Turn on classification first." }),
      Object.assign(item.sw("AI spam scanning", "A model reads mail the rules did not decide, and names what kind of spam it is - also for mail the rules already called spam. The text of that mail leaves for the model the deployment uses.", "spam.aiScanning", { off: !decl.spam.on, tip: "Turn on classification first." }), { problem: aiScanProblem }),
    ] },
    { title: "Sensitive content", hint: "What this account's mail is scanned for before a model reads it. A scan never removes mail; it replaces what it finds with a placeholder.", items: [
      item.sw("Scan for credentials, tokens and keys", "", "sensitive.secrets"),
      item.sw("Scan for personal data", "", "sensitive.pii"),
      { isSwitch: true, label: "Hold outgoing mail that carries credentials", hint: "A message carrying them is held instead of leaving this account.", tip: lockTip, refused: folderLocked, rowStyle: row(folderLocked), switchStyle: sw(screened.indexOf("Secrets") >= 0, folderLocked), toggle: () => putAt("sensitive.screenOutgoing", toggleIn(screened, "Secrets")) },
      { isSwitch: true, label: "Hold outgoing mail that carries personal data", hint: "", tip: lockTip, refused: folderLocked, rowStyle: row(folderLocked), switchStyle: sw(screened.indexOf("Pii") >= 0, folderLocked), toggle: () => putAt("sensitive.screenOutgoing", toggleIn(screened, "Pii")) },
    ] },
    { title: "Sending", hint: "", items: [
      item.text("Send as", "The address mail is written from. Left empty, it is the account's own address.", "sending.fromAddress", { empty: null, placeholder: draft.mail, max: 254 }),
      item.text("Name beside it", "Left empty, only the address is written.", "sending.fromName", { empty: null, placeholder: "No name", max: 128 }),
      item.sw("File a copy in Sent", "Puts a copy of every message you send into this account's Sent folder.", "sending.fileSentCopy"),
      item.sw("Take it back when the provider files one too", "Some providers file their own copy; this removes the second one.", "sending.withdrawDuplicate", { off: !decl.sending.fileSentCopy, tip: "Turn on filing a copy first." }),
      item.select("Sign with", "What you send from this account is signed with it, so the reader can tell it came from you. Certificates and keys are added once, under Certificates, and chosen here.", "sending.signWith", signOptions),
      Object.assign(item.select("Encrypt with", decl.sending.encryptWith === "same" && !decl.sending.signWith ? "Nothing is signed, so there is nothing to take the same from - pick one." : "The certificate or key correspondents encrypt mail to you with, and the one MailFathom opens it with. It travels with what you sign, so they can.", "sending.encryptWith", encryptOptions), { problem: decl.sending.encryptWith === "same" && !decl.sending.signWith ? "Pick what to encrypt with." : "" }),
      { isLink: true, label: "Manage certificates and keys", go: () => pickSection("certs") },
    ] },
  ];

  /* ---------- Certificates ---------- */
  /* One store for the person, shared by every account; an account only chooses from it, for signing and for
     encryption, the same one or two different ones. */
  const SAMPLE_FILES = [{ file: "kowalska-2028.p12", kind: "smime", size: "6 KB" }, { file: "kowalska-work.asc", kind: "pgp", size: "4 KB" }];
  const cd = st.certDraft || null;
  const usedBy = (id) => accounts.filter(a => a.settings.sending.signWith === id || a.settings.sending.encryptWith === id || (a.settings.sending.encryptWith === "same" && a.settings.sending.signWith === id));
  const certProblem = !cd ? "" :
    !cd.file ? "Choose the file first." :
    cd.kind === "smime" && !cd.pass ? "A .p12 file is locked with a password - type it, so MailFathom can open it." :
    (cd.pass || "").length > 1024 ? "A password is 1024 characters at most." : "";
  const CERT_OUT = {
    saved: "Added. Choose it for an account under Accounts > Sending.",
    refused: "The server did not take that file: it holds no private key, so nothing could be signed or opened with it.",
    notStored: "That certificate was not saved to the server, so it is not in your store.",
    unanswered: "The server did not answer, so it is not known whether that certificate was saved.",
    moved: "That certificate was not saved to the server, so it is not in your store.",
  };
  const answerCert = (apply, after) => {
    const outcome = writes;
    self.setState(s => Object.assign({
      setCertificates: outcome === "saved" ? apply((s.setCertificates || SETTINGS.certificates).slice()) : (s.setCertificates || SETTINGS.certificates),
      certOut: outcome,
    }, outcome === "saved" && after ? after(s) : {}));
  };
  const fmtDay = (d) => new Date(d + "T12:00").toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric" });

  /* The footer is written in the composer's own editor. An uploaded image is a cid: reference in what is saved, and a
     drawn placeholder while it is edited, since a browser cannot load cid:. */
  const ICON = "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1;";
  const fmtBtn = "display:flex;align-items:center;justify-content:center;border-radius:8px;color:var(--text2);" + (touch ? "min-width:44px;height:44px;padding:0 9px;font-size:15px;" : "min-width:30px;height:30px;padding:0 7px;font-size:13px;");
  const placeholderSrc = (name) => "data:image/svg+xml," + encodeURIComponent("<svg xmlns='http://www.w3.org/2000/svg' width='120' height='36'><rect x='0.5' y='0.5' width='119' height='35' rx='6' fill='#eef0f3' stroke='#c9ced6' stroke-dasharray='4 3'/><text x='60' y='22' font-family='sans-serif' font-size='12' fill='#6b7280' text-anchor='middle'>" + name + "</text></svg>");
  const editableImg = (name) => '<img data-cid="' + name + '" alt="' + name + '" src="' + placeholderSrc(name) + '">';
  const toEditable = (markup) => markup.replace(/<img[^>]*src="cid:([^"]+)"[^>]*>/g, (m, name) => editableImg(name));
  const fromEditable = (html) => html.replace(/<img[^>]*data-cid="([^"]+)"[^>]*>/g, '<img src="cid:$1" alt="$1">');
  const ftExec = (cmd, arg) => {
    const el = self._ftEl;
    if (!el) return;
    el.focus();
    const sel = window.getSelection();
    if (sel && (!sel.rangeCount || !el.contains(sel.anchorNode))) { const r = document.createRange(); r.selectNodeContents(el); r.collapse(false); sel.removeAllRanges(); sel.addRange(r); }
    try { document.execCommand(cmd, false, arg || null); } catch (e) {}
    self.setState(s2 => s2.footerEdit ? { footerEdit: Object.assign({}, s2.footerEdit, { markup: fromEditable(el.innerHTML) }) } : {});
  };

  /* ---------- Preferences ---------- */
  const telemetry = props.telemetry || "forwarded";
  const blocked = props.notificationPermission === "blocked";
  const pref = (section, patch) => {
    if (prefsLocked) return;
    set({ prefsSection: section });
    write("prefs", patch);
  };
  const prefsProblemIn = (section) => st.prefsSection === section ? problemOf("prefs") : "";
  const themeNow = !st.themePref || st.themePref === "auto" ? "system" : st.themePref;

  const panel = {
    /* The frame's navigation */
    settingsSectionTitle: curSec.label,
    settingsSectionHint: curSec.hint,
    settingsNavEmpty: shown.length === 0,
    settingsNavItems: shown.map(navItem),
    settingsSwipe: touch && env.singlePane ? self.tabSwipe(ORDER, cur, pickSection) : {},
    settingsTabs: SECTIONS.map(x => ({
      label: x.label,
      pick: () => pickSection(x.v),
      style: "flex:0 0 auto;white-space:nowrap;border-bottom:2px solid " +
        (cur === x.v ? "var(--accent);color:var(--text);font-weight:600" : "transparent;color:var(--muted)") +
        (touch ? ";padding:14px 4px 12px;font-size:16px" : ";padding:10px 1px 8px;font-size:14px"),
    })),
    tabProfil: cur === "profil",
    tabAway: cur === "away",
    tabAccounts: cur === "accounts",
    tabCerts: cur === "certs",
    tabReading: cur === "reading",
    tabAi: cur === "ai",
    tabNotif: cur === "notif",
    tabApp: cur === "app",

    /* Shapes */
    sCap: "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--faint);font-weight:600",
    sCapGap: "font-family:'Geist',system-ui,sans-serif;font-size:12px;color:var(--faint);font-weight:600;padding-top:6px",
    sHint: "font-size:13px;line-height:1.5;max-width:620px;color:var(--muted);text-wrap:pretty",
    sProblem: "display:flex;align-items:flex-start;gap:7px;max-width:620px;font-size:13px;line-height:1.5;color:var(--err-text);text-wrap:pretty",
    sOk: "display:flex;align-items:flex-start;gap:7px;max-width:620px;font-size:13px;line-height:1.5;color:var(--ok-text);text-wrap:pretty",
    sDivider: "height:1px;background:var(--line2);margin:6px 0",
    sField: field,
    sFieldRo: fieldRo,
    sSelect: field + "max-width:320px;",
    sArea: "width:100%;max-width:620px;box-sizing:border-box;resize:vertical;font-family:inherit;font-size:14px;line-height:1.5;color:var(--text);background:var(--sub);border:1px solid var(--border2);border-radius:8px;padding:9px 11px;outline:none",
    sCode: "width:100%;max-width:620px;box-sizing:border-box;resize:vertical;font-family:'Geist Mono',ui-monospace,monospace;font-size:13px;line-height:1.55;color:var(--text);background:var(--sub);border:1px solid var(--border2);border-radius:8px;padding:9px 11px;outline:none",
    sQuiet: quiet,
    sSegTrack: segTrack,
    sNoteInfo: "display:flex;box-sizing:border-box;align-items:flex-start;gap:8px;padding:9px 10px;max-width:620px;border:1px solid var(--line);border-radius:8px;background:var(--sub);font-size:13px;line-height:1.5;color:var(--text2);text-wrap:pretty",
    sNoteWarn: "display:flex;box-sizing:border-box;align-items:flex-start;gap:8px;padding:9px 10px;max-width:620px;border:1px solid var(--warn);border-radius:8px;background:var(--warn-soft);font-size:13px;line-height:1.5;color:var(--warn-text);text-wrap:pretty",
    sCard: "display:flex;flex-direction:column;gap:10px;padding:12px 13px;max-width:620px;box-sizing:border-box;border:1px solid var(--line);border-radius:12px;background:var(--sub)",
    profileTip: profileLocked ? LOCKED_TIP : undefined,
    prefsTip: prefsLocked ? LOCKED_TIP : undefined,
    prefsLocked,
    lockedFolderTip: folderLocked ? LOCKED_TIP : undefined,
    sharedMarkStyle: sharedMark,

    /* Profile */
    profileLockText: offline
      ? "There is no connection to the MailFathom server, so your name, your picture, your language, your time zone, and your numbers are shown here rather than offered for you to change until it is back."
      : access === "readOnly"
        ? "This sign-in may read what the server holds about you and may not change it, so your name, your picture, your language, your time zone, and your numbers are shown here rather than offered for you to change."
        : "",
    profileLocked,
    nameLocked,
    nameFieldStyle: nameLocked ? fieldRo : field,
    onMeName: (e) => nameLocked ? null : self.setState(s => ({ myName: e.target.value, setOut: Object.assign({}, s.setOut, { name: null }) })),
    saveMeName: () => nameLocked || nameBad ? null : write("name", {}),
    nameManagedNote: access === "nameManaged" && !profileLocked ? "Your name is managed by your administrator, so it is shown here rather than offered for you to change." : "",
    nameProblem: nameBad && !nameLocked ? OUT.name.refused : problemOf("name"),
    photoProblem: st.photoError || problemOf("photo"),
    onPhotoFile: (e) => {
      const f = e.target.files && e.target.files[0];
      if (!f || profileLocked) return;
      if (!/^image\/(jpeg|png)$/.test(f.type)) { set({ photoError: "That file is neither a JPEG nor a PNG, so it was not sent." }); return; }
      if (f.size > 1048576) { set({ photoError: "That picture is larger than 1 MB, so it was not sent." }); return; }
      const r = new FileReader();
      r.onload = () => write("photo", { myPhoto: String(r.result), photoError: "" });
      r.readAsDataURL(f);
    },
    removePhoto: () => profileLocked ? null : self.askDel({
      title: "Remove the profile photo?",
      text: "We will fall back to initials. The file will have to be uploaded again.",
      label: "Remove photo",
      run: () => write("photo", { myPhoto: "", photoError: "" }),
    }),
    photoControlsShown: !profileLocked,
    personLangOptions: LANGS.map(([v, label]) => ({
      label,
      pick: () => profileLocked ? null : write("language", { setLanguage: v }),
      style: seg(lang === v) + (profileLocked ? ";opacity:0.55" : ""),
    })),
    personLangProblem: problemOf("language"),
    tzValue: tz,
    tzOptions: SETTINGS.timeZones.map(([v, label]) => ({ v, label })),
    tzFieldStyle: (profileLocked ? fieldRo : field) + "max-width:360px;",
    onTz: (e) => profileLocked ? null : write("timeZone", { setTimeZone: e.target.value }),
    tzProblem: problemOf("timeZone"),
    phoneRows: phones.map((p, i) => ({
      label: p.label,
      number: p.number,
      preferred: i === 0,
      notPreferred: i > 0 && !profileLocked,
      onLabel: (e) => profileLocked ? null : writePhones(l => l.map((x, j) => j === i ? Object.assign({}, x, { label: e.target.value }) : x)),
      onNumber: (e) => editPhone(i, "number", e.target.value),
      commit: () => profileLocked || phoneBad(phones[i].number) ? null : writePhones(() => phones),
      makePreferred: () => writePhones(l => [l[i]].concat(l.filter((_, j) => j !== i))),
      remove: () => profileLocked ? null : writePhones(l => l.filter((_, j) => j !== i)),
      removeShown: !profileLocked,
      problem: phoneBad(p.number) ? "A number needs a digit, and holds only digits, +, spaces, dashes, brackets and dots - 32 characters at most." : "",
      selectStyle: (profileLocked ? fieldRo : field) + "flex:0 0 " + (touch ? 112 : 104) + "px;width:auto;",
      numberStyle: (profileLocked ? fieldRo : field) + "flex:1;min-width:0;max-width:260px;font-variant-numeric:tabular-nums;",
    })),
    phoneLabelOptions: PHONE_LABELS.map(([v, label]) => ({ v, label })),
    phonesEmpty: phones.length === 0,
    addPhoneShown: !profileLocked,
    addPhoneFull: phones.length >= 16,
    addPhoneTip: phones.length >= 16 ? "A card holds 16 numbers at most - remove one first." : "Add a number",
    addPhone: () => phones.length >= 16 ? null : set({ setPhones: phones.concat([{ label: "mobile", number: "" }]) }),
    addPhoneStyle: quiet + (phones.length >= 16 ? "opacity:0.55;" : ""),
    phonesProblem: problemOf("phones"),
    smallBtn: "flex:0 0 auto;display:flex;align-items:center;gap:5px;font-size:13px;color:var(--muted);border-radius:8px;box-sizing:border-box;white-space:nowrap;" + (touch ? "min-height:44px;padding:0 10px;" : "padding:5px 8px;"),
    iconBtn: "flex:0 0 auto;display:flex;align-items:center;justify-content:center;border-radius:8px;color:var(--muted);" + (touch ? "width:44px;height:44px;" : "width:32px;height:32px;"),
    preferredMark: "flex:0 0 auto;display:inline-flex;align-items:center;gap:4px;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--accent-d);background:var(--accent-soft);border-radius:4px;padding:2px 6px",
    addrOptions: MAIL_ACCOUNTS.map(a => ({
      mail: a.mail,
      name: a.name,
      shared: !!a.shared,
      sharedStyle: sharedMark,
      pick: () => profileLocked ? null : write("address", { setAddress: a.mail }),
      style: row(profileLocked) + "align-items:center;",
      dotStyle: "flex:0 0 18px;width:18px;height:18px;box-sizing:border-box;border-radius:50%;border:" + (addr === a.mail ? "5px solid var(--accent)" : "2px solid var(--border2)") + ";background:var(--panel)",
    })),
    addrProblem: problemOf("address"),

    /* Availability */
    whDays: DAYS.map(([v, label]) => {
      const on = hoursDraft.days.includes(v);
      return {
        label,
        style: chip(on, profileLocked),
        pick: () => writeHours(Object.assign({}, hoursDraft, { days: on ? hoursDraft.days.filter(d => d !== v) : DAYS.map(d => d[0]).filter(d => d === v || hoursDraft.days.includes(d)) })),
      };
    }),
    whStart: hoursDraft.start,
    whEnd: hoursDraft.end,
    whFieldStyle: (profileLocked ? fieldRo : field) + "width:auto;flex:0 0 auto;font-variant-numeric:tabular-nums;",
    onWhStart: (e) => writeHours(Object.assign({}, hoursDraft, { start: e.target.value })),
    onWhEnd: (e) => writeHours(Object.assign({}, hoursDraft, { end: e.target.value })),
    whTimesShown: hoursDraft.days.length > 0,
    whNone: hoursDraft.days.length === 0,
    whHint: "Read in your time zone, " + tzLabel + ". Colleagues see them on your card; nothing else - not your status, not a notification, not the automatic reply - is decided by them.",
    whProblem: hoursBad ? "The end has to come after the start - one range for every day, and none across midnight." : problemOf("hours"),

    arOn: replyDraft.on,
    arSwitchStyle: sw(replyDraft.on, profileLocked),
    arRowStyle: row(profileLocked),
    toggleAr: () => editReply({ on: !replyDraft.on }),
    arStatus: {
      off: "Off - nobody is answered.",
      later: "Starts " + fmt(reply.startsAt) + ", and answers until " + fmt(reply.endsAt) + ".",
      now: "Answering now, until " + fmt(reply.endsAt) + ".",
      ended: "The period ended " + fmt(reply.endsAt) + ", so nobody is answered.",
    }[replyState],
    arStatusShown: replyState !== "off",
    arStatusIcon: { off: "schedule_send", later: "event_upcoming", now: "reply_all", ended: "event_busy" }[replyState],
    arStatusStyle: "display:flex;align-items:center;gap:8px;max-width:620px;font-size:13px;line-height:1.5;color:var(--" + (replyState === "now" ? "ok-text" : "muted") + ")",
    arStarts: replyDraft.startsAt,
    arEnds: replyDraft.endsAt,
    arTimeStyle: (profileLocked ? fieldRo : field) + "width:auto;max-width:240px;font-variant-numeric:tabular-nums;",
    onArStarts: (e) => editReply({ startsAt: e.target.value }),
    onArEnds: (e) => editReply({ endsAt: e.target.value }),
    arAccounts: MAIL_ACCOUNTS.map(a => {
      const on = replyDraft.accounts.includes(a.id);
      return {
        name: a.name, mail: a.mail, shared: !!a.shared, sharedStyle: sharedMark,
        refused: !!a.shared,
        tip: a.shared ? "A shared mailbox is never one person's to answer for." : undefined,
        pick: () => a.shared ? null : editReply({ accounts: on ? replyDraft.accounts.filter(x => x !== a.id) : replyDraft.accounts.concat([a.id]) }),
        rowStyle: row(profileLocked || a.shared) + "align-items:center;",
        boxStyle: "flex:0 0 18px;width:18px;height:18px;box-sizing:border-box;border-radius:5px;display:flex;align-items:center;justify-content:center;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 500;font-size:15px;line-height:1;" +
          (on ? "background:var(--accent);color:var(--onaccent);" : "border:2px solid var(--border2);color:transparent;"),
      };
    }),
    arOutsideCaption: inOrg ? "To people outside your organization" : "The reply",
    arOutside: replyDraft.outsideText,
    onArOutside: (e) => editReply({ outsideText: e.target.value }),
    arOutsideCount: replyDraft.outsideText.length + " / 8000",
    arInside: replyDraft.insideText || "",
    onArInside: (e) => editReply({ insideText: e.target.value }),
    arInsideCount: (replyDraft.insideText || "").length + " / 8000",
    arInsideShown: inOrg,
    arNoOrgNote: !inOrg,
    arReadOnly: profileLocked,
    arSaveShown: !profileLocked,
    arSaveStyle: primary(!replyProblems.length),
    arSaveOff: replyProblems.length > 0,
    arSaveTip: replyProblems.length ? "To save it, " + replyProblems.join(", ") + "." : undefined,
    arSave: () => {
      if (replyProblems.length) return;
      const outcome = writes;
      self.setState(s => ({ setReply: outcome === "saved" ? replyDraft : (s.setReply || SETTINGS.automaticReply), replyDraft: outcome === "saved" ? null : s.replyDraft, replyOut: outcome }));
    },
    arProblem: replyProblems.length ? "To save it, " + replyProblems.join(", ") + "." : st.replyOut && st.replyOut !== "saved" ? REPLY_OUT[st.replyOut] : "",
    arSaved: st.replyOut === "saved" ? REPLY_OUT.saved : "",

    /* Accounts — the list */
    acctListShown: cur === "accounts" && !draft,
    acctEditShown: cur === "accounts" && !!draft,
    acctCount: accounts.length === 1 ? "1 account" : accounts.length + " accounts",
    acctItems: accounts.map(a => ({
      name: a.name,
      mail: a.mail,
      shared: !!a.shared,
      sharedStyle: sharedMark,
      summary: a.folders.length + (a.folders.length === 1 ? " folder" : " folders") + "  ·  " +
        (a.footerByOrganization ? "Footer set by your organization" : ownFooters[a.id] ? "Your own footer" : "No footer"),
      dotStyle: "flex:0 0 8px;width:8px;height:8px;border-radius:50%;margin-top:6px;background:" + a.dot,
      edit: () => set({ acctDraft: a.id, folderEdit: null, folderOut: null, footerEdit: null, footerOut: null, acctForm: null, acctOut: null, exclDraft: "" }),
    })),

    /* Accounts — one account */
    acctBack: () => set({ acctDraft: null, folderEdit: null, footerEdit: null, acctForm: null, acctOut: null }),
    acctTitle: draft ? draft.name : "",
    acctMail: draft ? draft.mail : "",
    acctShared: draft ? !!draft.shared : false,
    acctLockText: offline
      ? "There is no connection to the MailFathom server, so this account is shown as it stands rather than offered for you to change until it is back."
      : access === "readOnly"
        ? "This sign-in may read how this account is set up and may not change it, so it is shown here rather than offered for you to change."
        : "",
    acctLocked: folderLocked,
    sLink: "display:flex;align-items:center;gap:6px;align-self:flex-start;font-size:14px;color:var(--accent-d);" + (touch ? "min-height:44px;" : ""),
    sRowHover: touch ? "cursor:pointer" : "cursor:pointer;background:var(--hover)",
    sIconHover: touch ? "cursor:pointer" : "cursor:pointer;background:var(--hover);color:var(--text)",
    acctGroups: acctGroups.map((g, gi) => Object.assign({}, g, { hasHint: !!g.hint, first: gi === 0, items: g.items.map(x => Object.assign({ isText: false, isNumber: false, isSeg: false, isSelect: false, isSwitch: false, isChips: false, isExcl: false, isLink: false, problem: "", hasHint: !!x.hint }, x, { wrapStyle: "display:flex;flex-direction:column;gap:6px;max-width:620px;" + (x.dim ? "opacity:0.55;" : "") })) })),
    acctDeclOutcome: st.acctOut && ACCT_OUT[st.acctOut] ? ACCT_OUT[st.acctOut] : "",
    acctDeclOk: st.acctOut === "saved",
    acctDeclBad: !!st.acctOut && st.acctOut !== "saved",
    acctDeclProblem: declProblem,
    acctDeclSaveShown: !folderLocked,
    acctDeclSaveStyle: primary(declDirty && !declProblem),
    acctDeclSaveOff: !declDirty || !!declProblem,
    acctDeclSaveTip: declProblem || (!declDirty ? "Nothing has changed yet." : undefined),
    acctDeclSave: saveDecl,
    acctDeclDiscardShown: declDirty && !folderLocked,
    acctDeclDiscard: () => set({ acctForm: null, acctOut: null, exclDraft: "" }),
    folderCount: draft ? (draft.folders.length === 1 ? "1 folder" : draft.folders.length + " folders") : "",
    fdRows: feRows.map(({ f, i }) => {
      const isNew = f == null;
      const editing = !!fe && fe.index === i;
      const role = f && f.role ? roleOf(f.role) : null;
      const flags = !f ? [] : [
        !f.sync ? "Not synchronized" : "",
        !f.ai ? "Hidden from AI" : "",
        f.ai && !f.embed ? "Not indexed" : "",
        f.create ? "Created if missing" : "",
      ].filter(Boolean).map(label => ({ label }));
      return {
        isNew, editing,
        alias: isNew ? "New folder" : f.alias,
        icon: isNew ? "create_new_folder" : role ? role[2] : "folder",
        where: isNew ? "" : role ? "Found by its role - the server's " + role[1] + " folder" : f.path.join(" / "),
        whereMono: !isNew && !role,
        flags,
        hasFlags: flags.length > 0,
        open: () => folderLocked || editing ? null : set({ folderEdit: { index: i, draft: draftOf(f) }, folderOut: null }),
        chevron: folderLocked ? "" : editing ? "expand_less" : "expand_more",
        rowStyle: "display:flex;align-items:center;gap:10px;box-sizing:border-box;" + (touch ? "min-height:52px;padding:6px 12px;" : "padding:8px 11px;") + (editing ? "background:var(--accent-soft);" : ""),
        wrapStyle: "display:flex;flex-direction:column;border-top:" + (i === 0 ? "none" : "1px solid var(--line2)"),
        whereStyle: "font-size:12px;color:var(--faint);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" + (!isNew && !role ? "font-family:'Geist Mono',ui-monospace,monospace;" : ""),
      };
    }),
    sFlag: "flex:0 0 auto;font-family:'Geist',system-ui,sans-serif;font-size:11px;font-weight:600;color:var(--muted);border:1px solid var(--border2);border-radius:4px;padding:0 5px;line-height:16px;white-space:nowrap",
    sFolderList: "display:flex;flex-direction:column;max-width:620px;border:1px solid var(--line);border-radius:12px;background:var(--sub);overflow:hidden",
    feName: feDraft ? feDraft.alias : "",
    onFeName: (e) => editFe({ alias: e.target.value }),
    feBySegs: [["role", "By its role"], ["path", "By its path"]].map(([v, label]) => ({
      label, style: seg(!!feDraft && feDraft.by === v), pick: () => editFe({ by: v }),
    })),
    feByRole: !!feDraft && feDraft.by === "role",
    feByPath: !!feDraft && feDraft.by === "path",
    feRole: feDraft ? feDraft.role : "inbox",
    roleOptions: ROLES.map(([v, label]) => ({ v, label })),
    onFeRole: (e) => editFe({ role: e.target.value }),
    fePath: feDraft ? feDraft.pathText : "",
    onFePath: (e) => editFe({ pathText: e.target.value }),
    feSwitches: !feDraft ? [] : [
      { k: "sync", label: "Synchronize", hint: "Keep a copy of this folder's mail in MailFathom. Off, the folder is not read at all." },
      { k: "ai", label: "Visible to AI", hint: "Agents, MCP tools and models may read this folder. Off, it is withheld from AI and from nothing else - you still read it here, and rules and retention still reach it." },
      { k: "embed", label: "Index for search by meaning", hint: "Cut its mail into passages and embed them, so a question about your mail finds it by what it says.", off: !feDraft.ai, tip: "Turn on Visible to AI first - an index is a model's reading of the folder." },
      { k: "create", label: "Create it if it is missing", hint: "If the mail server has no folder at this path, MailFathom may create one there. Off, a mistyped path stays an error you can see.", off: feDraft.by !== "path", tip: "A folder found by its role is one the mail server already has." },
    ].map(x => {
      const on = !x.off && !!feDraft[x.k];
      return {
        label: x.label, hint: x.hint, refused: !!x.off, tip: x.off ? x.tip : undefined,
        style: row(!!x.off), switchStyle: sw(on, !!x.off),
        toggle: () => x.off ? null : editFe({ [x.k]: !feDraft[x.k] }),
      };
    }),
    feProblem,
    feSaveStyle: primary(!feProblem),
    feSaveOff: !!feProblem,
    feSaveTip: feProblem || undefined,
    feSave: () => {
      if (!fe || feProblem) return;
      const folder = folderOf(feDraft);
      answerFolder(list => fe.index == null ? list.concat([folder]) : list.map((f, j) => j === fe.index ? folder : f));
    },
    feCancel: () => set({ folderEdit: null, folderOut: null }),
    feRemoveShown: !!fe && fe.index != null,
    feRemove: () => {
      if (!fe || fe.index == null || !draft) return;
      const f = draft.folders[fe.index];
      self.askDel({
        title: "Stop reading " + f.alias + "?",
        text: "MailFathom stops reading " + f.alias + " from " + draft.name + " on the account's next run. Nothing on the mail server is touched, and the folder can be added again.",
        label: "Stop reading it",
        run: () => answerFolder(list => list.filter((_, j) => j !== fe.index)),
      });
    },
    addFolderShown: !!draft && !folderLocked && !fe,
    addFolder: () => set({ folderEdit: { index: null, draft: { alias: "", by: "path", role: "inbox", pathText: "", sync: true, ai: true, embed: true, create: false } }, folderOut: null }),
    folderOut: st.folderOut && FOLDER_OUT[st.folderOut] ? FOLDER_OUT[st.folderOut] : "",
    folderOutOk: st.folderOut === "saved",
    folderOutBad: !!st.folderOut && st.folderOut !== "saved",

    ftOrg: !!draft && !!draft.footerByOrganization,
    ftOwn: !!draft && !draft.footerByOrganization && !!ownFooter && !fte,
    ftNone: !!draft && !draft.footerByOrganization && !ownFooter && !fte,
    ftEditing: !!fte,
    ftLines: !draft ? [] : draft.footerByOrganization ? footerDocLines(FOOTERS[draft.footer] || []) : ownFooter ? footerDocLines(markupLines(ownFooter.markup)) : [],
    ftShowsPreview: !!draft && !fte && (!!draft.footerByOrganization || !!ownFooter),
    ftOwnActions: !!draft && !draft.footerByOrganization && !!ownFooter && !fte && !folderLocked,
    ftNoneActions: !!draft && !draft.footerByOrganization && !ownFooter && !fte && !folderLocked,
    ftEdit: () => set({ footerEdit: { markup: ownFooter.markup, images: ownFooter.images.slice(), seed: String(Date.now()) }, footerOut: null }),
    ftWrite: () => set({ footerEdit: { markup: "<p><strong>" + env.myName + "</strong></p>", images: [], seed: String(Date.now()) }, footerOut: null }),
    ftEditorRef: (el) => {
      self._ftEl = el;
      if (el && fte && el.dataset.seed !== fte.seed) { el.innerHTML = toEditable(fte.markup); el.dataset.seed = fte.seed; }
    },
    onFtInput: (e) => {
      const markup = fromEditable(e.currentTarget.innerHTML);
      self.setState(s2 => ({ footerEdit: Object.assign({}, s2.footerEdit, { markup }), footerOut: null }));
    },
    ftFmtButtons: [
      { label: "B", title: "Bold", style: "font-weight:700", cmd: "bold" },
      { label: "I", title: "Italic", style: "font-style:italic;font-family:Georgia,serif", cmd: "italic" },
      { label: "U", title: "Underline", style: "text-decoration:underline", cmd: "underline" },
      { label: "S", title: "Strikethrough", style: "text-decoration:line-through", cmd: "strikeThrough" },
      { label: "\u2022-", title: "Bulleted list", style: "", cmd: "insertUnorderedList" },
      { label: "format_clear", title: "Clear formatting", style: ICON, cmd: "removeFormat" },
    ].map(b => ({ label: b.label, title: b.title, style: fmtBtn + b.style, run: (e) => { if (e && e.preventDefault) e.preventDefault(); ftExec(b.cmd, b.arg); } })),
    ftImageBtnStyle: fmtBtn + (ftUsed >= 5 ? ";opacity:0.45" : ""),
    ftImageTip: ftUsed >= 5 ? "A footer carries five images at most." : "Upload an image",
    ftImageOff: ftUsed >= 5,
    ftInsertImage: (e) => {
      if (e && e.preventDefault) e.preventDefault();
      if (!fte || ftUsed >= 5) return;
      const n = fte.images.length + 1;
      const name = "image" + n;
      ftExec("insertHTML", editableImg(name));
      self.setState(s2 => ({ footerEdit: Object.assign({}, s2.footerEdit, { images: s2.footerEdit.images.concat([{ name, file: "image-" + n + ".png", size: "12 KB" }]), markup: self._ftEl ? fromEditable(self._ftEl.innerHTML) : s2.footerEdit.markup }) }));
    },
    sFooterEditorWrap: "display:flex;flex-direction:column;max-width:620px;box-sizing:border-box;border:1px solid var(--border2);border-radius:10px;background:var(--sub);overflow:hidden",
    sFooterBar: "display:flex;align-items:center;gap:2px;flex-wrap:wrap;padding:4px 6px;border-bottom:1px solid var(--line2)",
    sFooterEditor: "min-height:96px;padding:12px 13px;font-size:14px;line-height:1.6;color:var(--text2);outline:none",
    ftRemove: () => self.askDel({
      title: "Remove your footer for " + draft.name + "?",
      text: "Messages you send from " + draft.mail + " go out without a footer until you write one again.",
      label: "Remove footer",
      run: () => answerFooter(null),
    }),
    ftProblem: linked ? "An image is linked rather than uploaded: " + linked + ". Upload it and refer to it as cid:<name> - loading it from the web would tell its server who read your mail." : ftLimit,
    ftSaveTip: ftBlock || undefined,
    ftSaveStyle: primary(!ftBlock),
    ftSaveOff: !!ftBlock,
    ftSave: () => ftBlock ? null : answerFooter({ markup: fte.markup, images: fte.images.filter(im => fte.markup.indexOf("cid:" + im.name) >= 0) }),
    ftCancel: () => set({ footerEdit: null, footerOut: null }),
    ftOutcome: st.footerOut && FOOTER_OUT[st.footerOut] ? FOOTER_OUT[st.footerOut] : "",
    ftOutcomeOk: st.footerOut === "saved",
    ftOutcomeBad: !!st.footerOut && st.footerOut !== "saved",
    sImageChip: "display:flex;align-items:center;gap:8px;max-width:620px;box-sizing:border-box;padding:" + (touch ? "0 4px 0 11px;min-height:44px" : "5px 4px 5px 10px") + ";border:1px solid var(--line);border-radius:8px;background:var(--sub);font-size:13px",
    sPreview: "display:flex;flex-direction:column;gap:2px;padding:12px 13px;max-width:620px;box-sizing:border-box;border:1px dashed var(--border2);border-radius:10px",

    /* Certificates */
    certLockText: folderLocked ? (offline ? "There is no connection to the MailFathom server, so your certificates and keys are shown here rather than offered for you to change until it is back." : "This sign-in may read your certificates and keys and may not change them.") : "",
    certRows: CERTS.map(c => {
      const users = usedBy(c.id);
      return {
        icon: c.kind === "smime" ? "verified_user" : "key",
        name: c.name,
        kind: c.kind === "smime" ? "S/MIME certificate" : "OpenPGP key",
        detail: c.address + "  ·  " + (c.validUntil ? "valid until " + fmtDay(c.validUntil) : "fingerprint " + c.fingerprint),
        usage: users.length ? "Chosen by " + users.map(a => a.name).join(", ") : "No account uses it yet",
        removeShown: !folderLocked,
        remove: () => self.askDel({
          title: "Remove " + c.name + "?",
          text: users.length
            ? users.map(a => a.name).join(" and ") + " stop" + (users.length === 1 ? "s" : "") + " signing and encrypting with it, and encrypted mail sent to it can no longer be opened here."
            : "Encrypted mail sent to it can no longer be opened here. The file has to be added again to use it.",
          label: "Remove",
          run: () => answerCert(l => l.filter(x => x.id !== c.id), s2 => ({ setAccounts: (s2.setAccounts || MAIL_ACCOUNTS).map(a => {
            const sd = Object.assign({}, a.settings.sending);
            if (sd.signWith === c.id) sd.signWith = null;
            if (sd.encryptWith === c.id || (sd.encryptWith === "same" && sd.signWith == null)) sd.encryptWith = null;
            return Object.assign({}, a, { settings: Object.assign({}, a.settings, { sending: sd }) });
          }) })),
        }),
      };
    }),
    certNone: CERTS.length === 0,
    certAddShown: !folderLocked && !cd,
    certAdd: () => set({ certDraft: { file: "", kind: "", pass: "" }, certOut: null }),
    certAdding: !!cd,
    certFile: cd && cd.file ? cd.file : "",
    certFileKind: cd && cd.file ? (cd.kind === "smime" ? "S/MIME certificate with its private key" : "OpenPGP secret key") : "",
    certHasFile: !!(cd && cd.file),
    certPickFile: () => {
      const n = ((st.certPicks || 0) % SAMPLE_FILES.length);
      const f = SAMPLE_FILES[n];
      set({ certDraft: Object.assign({}, cd, { file: f.file, kind: f.kind, size: f.size }), certPicks: (st.certPicks || 0) + 1, certOut: null });
    },
    certPass: cd ? cd.pass : "",
    onCertPass: (e) => set({ certDraft: Object.assign({}, cd, { pass: e.target.value }), certOut: null }),
    certPassHint: cd && cd.kind === "pgp" ? "Only if the key is locked with a passphrase." : "The password the .p12 or .pfx file was exported with.",
    certProblem,
    certSaveStyle: primary(!!cd && !certProblem),
    certSaveOff: !cd || !!certProblem,
    certSaveTip: certProblem || undefined,
    certSave: () => {
      if (!cd || certProblem) return;
      const id = "cert-" + Date.now();
      const kind = cd.kind;
      const c = kind === "smime"
        ? { id, kind, name: "Karolina Kowalska - 2028", address: "k.kowalska@nordwind.example", file: cd.file, validUntil: "2028-06-30" }
        : { id, kind, name: "k.kowalska@nordwind.example", address: "k.kowalska@nordwind.example", file: cd.file, fingerprint: "A04C 18E2 6B9F 7D13", validUntil: null };
      answerCert(l => l.concat([c]), () => ({ certDraft: null }));
    },
    certCancel: () => set({ certDraft: null, certOut: null }),
    certOutcome: st.certOut && CERT_OUT[st.certOut] ? CERT_OUT[st.certOut] : "",
    certOutOk: st.certOut === "saved",
    certOutBad: !!st.certOut && st.certOut !== "saved",

    /* AI — what is set elsewhere, listed here as a way there rather than as a second control. */
    aiRefs: (() => {
      const folders = accounts.reduce((n, a) => n + a.folders.length, 0);
      const visible = accounts.reduce((n, a) => n + a.folders.filter(f => f.ai).length, 0);
      const scanning = accounts.filter(a => a.settings.spam.on && a.settings.spam.aiScanning).map(a => a.name);
      const screened = accounts.filter(a => a.settings.sensitive.secrets || a.settings.sensitive.pii).map(a => a.name);
      return [
        { label: "Visible to AI and indexed for search by meaning", where: "Accounts › each folder", state: visible + " of " + folders + " folders visible to AI", go: () => pickSection("accounts") },
        { label: "AI spam scanning", where: "Accounts › Spam", state: scanning.length ? "On for " + scanning.join(", ") : "Off on every account", go: () => pickSection("accounts") },
        { label: "Scanning before a model reads mail", where: "Accounts › Sensitive content", state: screened.length ? "On for " + screened.join(", ") : "Off on every account", go: () => pickSection("accounts") },
        { label: "Language of what MailFathom writes about a mailbox", where: "Accounts › The account", state: accounts.map(a => a.name + ": " + (a.settings.language === "Polish" ? "Polski" : "English")).join(", "), go: () => pickSection("accounts") },
        { label: "AI simplified message view", where: "Reading › Message view", state: (st.msgView || "simple") === "ai" ? "In use" : "Not in use", go: () => pickSection("reading") },
        { label: "AI filters in the folder tree", where: "Reading", state: st.aiFiltersShown !== false ? "Shown" : "Hidden", go: () => pickSection("reading") },
      ].map((r, i) => Object.assign(r, { rowStyle: "display:flex;align-items:center;gap:10px;padding:" + (touch ? "8px 12px;min-height:52px;" : "9px 11px;") + "box-sizing:border-box;" + (i ? "border-top:1px solid var(--line2)" : "") }));
    })(),
    /* AI */
    aiLangName: (LANGS.find(l => l[0] === lang) || LANGS[0])[1],
    goProfile: () => pickSection("profil"),

    /* Reading, Application and Notifications */
    prefsLockText: offline
      ? "There is no connection to the MailFathom server, so the settings that follow you between machines are shown here as they stand until it is back. The interface language and what this device does are still yours to set."
      : access === "readOnly"
        ? "This sign-in may not change the settings that follow you between machines, so they are shown here as they stand. The interface language and what this device does are still yours to set."
        : "",
    prefRowStyle: row(prefsLocked),
    prefSegTrack: segTrack + (prefsLocked ? ";opacity:0.55" : ""),
    readingProblem: prefsProblemIn("reading"),
    appProblem: prefsProblemIn("app"),
    aiProblem: prefsProblemIn("ai"),
    notifProblem: prefsProblemIn("notif") || problemOf("seconds"),
    markReadOnOpen: st.markReadOnOpen !== false,
    markReadSwitchStyle: sw(st.markReadOnOpen !== false, prefsLocked),
    toggleMarkRead: () => pref("reading", { markReadOnOpen: st.markReadOnOpen === false }),
    aiFiltersShown: st.aiFiltersShown !== false,
    aiFiltersShownSwitchStyle: sw(st.aiFiltersShown !== false, prefsLocked),
    toggleAiFiltersShown: () => pref("reading", { aiFiltersShown: st.aiFiltersShown === false }),
    settingsThemeSegs: [["system", "System"], ["light", "Light"], ["dark", "Dark"]].map(([v, label]) => ({
      label, style: seg(themeNow === v),
      pick: () => pref("app", { themePref: v === "system" ? "auto" : v, themeOverride: null }),
    })),
    themeHint: themeNow === "system" ? "Follows this device: light or dark as the system is set." : "The same on every machine you sign in to.",
    openInTabs: (st.workMode || "tabs") === "tabs",
    openInTabsSwitchStyle: sw((st.workMode || "tabs") === "tabs", prefsLocked),
    toggleOpenInTabs: () => {
      if (prefsLocked) return;
      const v = (st.workMode || "tabs") === "tabs" ? "classic" : "tabs";
      set({ prefsSection: "app" });
      write("prefs", s => ({ workMode: v,
        tabs: v === "tabs" ? [{ key: "thread:" + s.threadId, kind: "thread", id: s.threadId }] : [],
        activeTab: v === "tabs" ? "thread:" + s.threadId : null }));
    },
    tabsWidthNote: env.wide ? "" : "Tabs need a window at least 1180 px wide - at this width mail opens beside the list either way.",
    listSegs: [[false, "One message per row"], [true, "One conversation per row"]].map(([v, label]) => ({
      label, style: seg(!!st.convMail === v), pick: () => pref("reading", { convMail: v }),
    })),
    telemetryForwarded: telemetry === "forwarded",
    telemetryNotForwarded: telemetry === "notForwarded",
    telemetryUnanswered: telemetry === "unanswered",
    telemetrySwitchShown: telemetry !== "notForwarded",
    telemetryDestText: "Sent to " + SETTINGS.telemetryAddress + ", which forwards it to whichever collector it is configured with. It carries which screens you opened and how long they took, never your mail, your addresses, your folders, or your password.",
    telemetrySwitchStyle: sw(!!st.telemetryOptOut, prefsLocked),
    toggleTelemetry: () => pref("app", { telemetryOptOut: !st.telemetryOptOut }),
    notifBlocked: blocked,
    notifDeviceShownOn: !blocked && !!st.notifDevice,
    notifDeviceSwitchStyle: sw(!blocked && !!st.notifDevice, blocked),
    notifDeviceRowStyle: row(blocked),
    notifDeviceTip: blocked ? "This browser blocks notifications from here." : undefined,
    toggleNotifDevice: () => blocked ? null : set({ notifDevice: !st.notifDevice }),
    notifAutoClearSwitchStyle: sw(!!st.notifAutoClear, prefsLocked),
    toggleNotifAutoClear: () => pref("notif", { notifAutoClear: !st.notifAutoClear }),
    onNotifDuration: (e) => {
      if (prefsLocked) return;
      const n = parseInt(e.target.value, 10);
      write("seconds", { notifDuration: isNaN(n) ? 5 : Math.max(1, Math.min(30, n)) });
    },
    aiMatchSwitchStyle: sw(!!st.aiMatchReply, prefsLocked),
    toggleAiMatch: () => pref("ai", { aiMatchReply: !st.aiMatchReply }),
    autoExpandSwitchStyle: sw(!!st.autoExpand, prefsLocked),
    toggleAutoExpand: () => pref("reading", { autoExpand: !st.autoExpand, histOpen: {} }),
    autoImagesSwitchStyle: sw(!!st.autoImages, prefsLocked),
    toggleAutoImages: () => pref("reading", { autoImages: !st.autoImages }),
    setViewAi: () => pref("reading", { msgView: "ai" }),
    setViewSimple: () => pref("reading", { msgView: "simple" }),
    setViewHtml: () => pref("reading", { msgView: "html", htmlDoc: null }),
  };

  /* Leaving with a draft that waits on Save asks first: save it, leave it unsaved, or stay. Closing the panel, another
     section and the way back out of an account all go through here. */
  const replyDirty = !!st.replyDraft && JSON.stringify(st.replyDraft) !== JSON.stringify(reply);
  const folderDirty = !!fe && (fe.index == null ? !!feDraft.alias.trim() : JSON.stringify(feDraft) !== JSON.stringify(draftOf(draft.folders[fe.index])));
  const footerDirty = !!fte && fte.markup !== (ownFooter ? ownFooter.markup : "<p><strong>" + env.myName + "</strong></p>");
  const pending = [
    replyDirty && { name: "the automatic reply", ok: !replyProblems.length, save: panel.arSave },
    declDirty && { name: "this account's settings", ok: !declProblem, save: saveDecl },
    folderDirty && { name: "the folder you are editing", ok: !feProblem, save: panel.feSave },
    footerDirty && { name: "the footer", ok: !ftBlock, save: panel.ftSave },
    !!cd && !!cd.file && { name: "the certificate you are adding", ok: !certProblem, save: panel.certSave },
  ].filter(Boolean);
  const DISCARD = { replyDraft: null, acctForm: null, folderEdit: null, footerEdit: null, certDraft: null, exclDraft: "" };
  const guard = (leave) => {
    if (!pending.length) { leave(); return; }
    self._leave = leave;
    set({ leaveAsk: true });
  };
  self._settingsGuard = guard;
  const names = pending.map(x => x.name);
  const listed = names.length > 1 ? names.slice(0, -1).join(", ") + " and " + names[names.length - 1] : names[0] || "";
  return Object.assign(panel, {
    acctBack: () => guard(panel.acctBack),
    leaveAskOpen: !!st.leaveAsk && pending.length > 0,
    leaveAskText: "Changes to " + listed + " are not saved yet.",
    leaveCantSave: !pending.every(x => x.ok),
    leaveBtnQuiet: quiet,
    leaveBtnDanger: quiet.replace("color:var(--text2);", "color:var(--err-text);"),
    leaveSaveTip: pending.every(x => x.ok) ? undefined : "Something here cannot be saved as it stands - stay and finish it, or leave without saving.",
    leaveSaveStyle: primary(pending.every(x => x.ok)),
    leaveSave: () => {
      if (!pending.every(x => x.ok)) return;
      pending.forEach(x => x.save());
      set({ leaveAsk: false });
      if (writes === "saved" && self._leave) { const go = self._leave; self._leave = null; setTimeout(go, 0); }
    },
    leaveDiscard: () => {
      const go = self._leave;
      self._leave = null;
      set(Object.assign({ leaveAsk: false }, DISCARD));
      if (go) setTimeout(go, 0);
    },
    leaveStay: () => { self._leave = null; set({ leaveAsk: false }); },
  });
};
