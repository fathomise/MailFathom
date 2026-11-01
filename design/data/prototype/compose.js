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
