// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What the writing assistant drafts into an open message, at full length and short. `signOff` closes a draft only on an
// account with no footer: where the account has one, the footer carries the closing and the draft ends at its own words.
MailFathomDesign.data.composeDrafts = {
  full: [
    "Hello,",
    "thank you for the addendum. We accept shortening the response time for critical incidents to 2 hours on business days.",
    "At the same time we ask for an upper cap on CPI indexation of 5% per year. At the 4.1% forecast this gives both sides a predictable cost through 2027.",
    "If the cap is acceptable, we are ready to close the decision by 28 August.",
  ],
  short: [
    "Hello,",
    "we accept the 2 h SLA. We ask for a 5% annual cap on CPI indexation - then we can close the decision by 28.08.",
  ],
  signOff: "Best regards,<br />Karolina Kowalska",
};

// Who a new message is addressed to before anybody is chosen.
MailFathomDesign.data.newMessageTo = ["Anna Kowalska"];

// Sending a message later. `now` is the moment the sample stands at, which a time at or before cannot be scheduled for;
// `zone` is the reader's time zone from their profile, which every time below is named in; `presets` are the ready
// times *Sending options* offers under *Send later*, each with the date and time it resolves to.
MailFathomDesign.data.sendLater = {
  now: { date: "2026-08-27", time: "10:47" },
  zone: { id: "Europe/Warsaw", offset: "UTC+02:00" },
  presets: [
    { key: "tomorrowAm", icon: "wb_twilight", label: "Tomorrow morning", date: "2026-08-28", time: "08:00" },
    { key: "tomorrowPm", icon: "light_mode", label: "Tomorrow afternoon", date: "2026-08-28", time: "13:00" },
    { key: "monday", icon: "date_range", label: "Monday morning", date: "2026-08-31", time: "08:00" },
  ],
};

// What the formatting bar writes beyond bold, italic, lists, a quote, and a link, as ADR 0047 settles it. Every swatch and
// step has a name, so a colour or a size is never told by itself alone. A colour is the value sent; a size is a step, which the HTML part writes in `sentPx` and every surface here draws as the client's own
// type step (`MailFathomDesign.markdown.MDSIZE`), relative to the 15 px body.
MailFathomDesign.data.composeFormatting = {
  // The palette: a column per hue and a row per shade, each value inside the band ADR 0047 holds the picker to — at
  // least 3:1 against the light panel and the dark one alike — so a coloured run is drawn as the value it is sent with
  // in both themes. `band` is that bound as relative luminance; a colour of somebody's own is picked inside it.
  colourHues: [
    { key: "grey", name: "Grey", shades: ["#6c6c6c", "#818181", "#939393", "#797979", "#8c8c8c"] },
    { key: "red", name: "Red", shades: ["#db0118", "#f73030", "#f86056", "#b95e55", "#ce7369"] },
    { key: "orange", name: "Orange", shades: ["#b05100", "#ca6520", "#e47325", "#9f6d51", "#b97f5f"] },
    { key: "amber", name: "Amber", shades: ["#8e6503", "#a67a21", "#bc8a26", "#8c7651", "#a2895e"] },
    { key: "lime", name: "Lime", shades: ["#597604", "#6c8c20", "#7b9f25", "#6e7f50", "#7f935d"] },
    { key: "green", name: "Green", shades: ["#017d38", "#219449", "#29a855", "#54835e", "#62986e"] },
    { key: "teal", name: "Teal", shades: ["#097a70", "#229085", "#28a397", "#54817b", "#63958e"] },
    { key: "cyan", name: "Cyan", shades: ["#077690", "#238ca8", "#299fbe", "#557f8d", "#6393a3"] },
    { key: "blue", name: "Blue", shades: ["#035dff", "#3e7cf5", "#5b91f6", "#5378bd", "#6d8dc7"] },
    { key: "purple", name: "Purple", shades: ["#9625ff", "#9f5ef5", "#ab79f6", "#8968bf", "#9a7fc8"] },
    { key: "pink", name: "Pink", shades: ["#cf0388", "#ec2a9f", "#f852ae", "#b35c88", "#ce6c9d"] },
  ],
  colourShades: [
    { key: "deep", name: (hue) => hue + ", dark" },
    { key: "vivid", name: (hue) => hue },
    { key: "light", name: (hue) => hue + ", light" },
    { key: "muted", name: (hue) => hue + ", muted" },
    { key: "soft", name: (hue) => hue + ", soft" },
  ],
  band: { darkest: 0.1447, lightest: 0.296 },
  ownKept: 11,
  sizes: [
    { key: "small", name: "Small", sentPx: 12 },
    { key: "normal", name: "Normal", sentPx: null },
    { key: "large", name: "Large", sentPx: 18 },
    { key: "larger", name: "Larger", sentPx: 24 },
    { key: "largest", name: "Largest", sentPx: 32 },
  ],
  alignments: [
    { key: "start", name: "Align left", icon: "format_align_left", command: "justifyLeft" },
    { key: "center", name: "Centre", icon: "format_align_center", command: "justifyCenter" },
    { key: "end", name: "Align right", icon: "format_align_right", command: "justifyRight" },
    { key: "justify", name: "Justify", icon: "format_align_justify", command: "justifyFull" },
  ],
  // The emoji the *Emoji* menu offers: a fixed set of the common ones, in the order the grid draws them, each named by
  // what it shows so a screen reader and the name bubble say it. A choice is text, so it goes wherever words go. The
  // menu draws each from Noto Emoji's Apache-2.0 pictures at `emojiPictures`, which the client commits beside itself.
  emoji: [
    { char: "😀", name: "Grinning face" }, { char: "😂", name: "Face with tears of joy" }, { char: "🙂", name: "Slightly smiling face" },
    { char: "😉", name: "Winking face" }, { char: "😊", name: "Smiling face with smiling eyes" }, { char: "😍", name: "Smiling face with heart-eyes" },
    { char: "🤔", name: "Thinking face" }, { char: "😮", name: "Face with open mouth" }, { char: "😢", name: "Crying face" },
    { char: "😅", name: "Grinning face with sweat" }, { char: "🙏", name: "Folded hands" }, { char: "👍", name: "Thumbs up" },
    { char: "👎", name: "Thumbs down" }, { char: "👏", name: "Clapping hands" }, { char: "🙌", name: "Raising hands" },
    { char: "👋", name: "Waving hand" }, { char: "💪", name: "Flexed biceps" }, { char: "🤝", name: "Handshake" },
    { char: "❤️", name: "Red heart" }, { char: "🔥", name: "Fire" }, { char: "🎉", name: "Party popper" },
    { char: "✅", name: "Check mark button" }, { char: "❌", name: "Cross mark" }, { char: "⚠️", name: "Warning" },
    { char: "⭐", name: "Star" }, { char: "💡", name: "Light bulb" }, { char: "📌", name: "Pushpin" },
    { char: "📎", name: "Paperclip" }, { char: "📅", name: "Calendar" }, { char: "⏰", name: "Alarm clock" },
    { char: "☕", name: "Hot beverage" }, { char: "🚀", name: "Rocket" },
  ],
  emojiPictures: "https://cdn.jsdelivr.net/gh/googlefonts/noto-emoji@99701e07d3d1fe1bdbcdb14964547f0fd592966f/2D/svg/",
  // Every table is sent with one border, `1px solid #d0d5dd`, and `4px 8px` of padding; the editor draws the same.
  table: { defaultRows: 3, defaultColumns: 3, maxRows: 200, maxColumns: 20 },
  // A picture is drawn at the width it is sent at, its own and at most `sentMaxWidth` pixels until its grip sizes it, and
  // fitted to the editor's width. `sampleStoreBytes` stands in for the platform's quota on the session store, which
  // decides what survives a reload: past it, the pictures are kept in memory only.
  pictures: {
    maxBytes: 2 * 1024 * 1024, maxTotalBytes: 2.5 * 1024 * 1024, maxCount: 16, sentMaxWidth: 600, sampleStoreBytes: 1024 * 1024,
    types: ["image/png", "image/jpeg", "image/gif"], typeNames: "PNG, JPEG, and GIF",
  },
};

// A draft the writing assistant formats because it was asked to: *put the prices in a table*, *bullet the open
// points*. Each entry is one block of the closed set — a paragraph, a bold lead-in, a list, or a table whose first row is
// its header. Anything asked without such words is drafted plain, as `full` above.
MailFathomDesign.data.composeFormattedDraft = [
  { p: "Hello," },
  { p: "here is where the three hosting offers stand, side by side as you asked:" },
  { table: [
    ["Supplier", "Monthly price", "Critical response", "Notice"],
    ["Nordwind Hosting", "€4,200", "2 h", "3 months"],
    ["Contoso Cloud", "€3,850", "4 h", "1 month"],
    ["Fabrikam IT", "€4,600", "1 h", "6 months"],
  ] },
  { lead: "Open points" },
  { list: [
    "Contoso has not yet confirmed the 5% cap on CPI indexation.",
    "Fabrikam's six-month notice period needs a look from legal.",
  ] },
  { p: "If the cap is confirmed, I suggest we choose Nordwind Hosting by 28 August. The figures are in the [comparison sheet](https://files.example.com/procurement/hosting-2026)." },
];
