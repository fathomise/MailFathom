// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What Settings shows of the signed-in person before they change anything: the settings the server holds for them,
// which follow them between machines. The mail accounts and their folders are `mailAccounts` in workspace.js, and the
// footer each account sends with is `footers` there; what the person wrote themselves for an account is `ownFooters`
// below, as they would edit it.
MailFathomDesign.data.settings = {
  // The zones the time zone control offers, as the zone and how it reads; the design draws a handful of the whole list.
  timeZones: [
    ["Europe/Warsaw", "Warsaw - Central European Time"],
    ["Europe/London", "London - Greenwich Mean Time"],
    ["Europe/Berlin", "Berlin - Central European Time"],
    ["America/New_York", "New York - Eastern Time"],
    ["Asia/Tokyo", "Tokyo - Japan Standard Time"],
  ],
  timeZone: "Europe/Warsaw",
  // The person's own language: what MailFathom writes to them in. The interface language is the device's, apart.
  language: "en",
  // Most first: the first number is the one a colleague calls.
  phoneNumbers: [
    { label: "mobile", number: "+48 600 555 012" },
    { label: "work", number: "+48 22 555 0143" },
  ],
  preferredAddress: "k.kowalska@nordwind.example",
  workingHours: { days: ["mon", "tue", "wed", "thu", "fri"], start: "08:30", end: "16:30" },
  automaticReply: {
    on: false,
    startsAt: "2026-11-17T08:00",
    endsAt: "2026-11-24T18:00",
    accounts: ["a1"],
    outsideText: "Thank you for your message. I am away until 24 November and read no mail until then. For anything urgent about an order, write to orders@nordwind.example.",
    insideText: "Away until Monday 24 November. Piotr Nowak covers procurement while I am out - write to him for anything that cannot wait.",
  },
  // Where this client's telemetry goes when the server forwards it.
  telemetryAddress: "telemetry.nordwind.example",
};

// The footer the person wrote for an account, as they would edit it: markup, and the images uploaded with it, each
// referred to from the markup by its `cid:` name. An account missing here has no footer of their own — the work
// account's footer is its organization's template, and the shared Board sends with none.
MailFathomDesign.data.ownFooters = {
  a2: {
    markup: "<p><strong>Karolina Kowalska</strong><br>\n+48 600 555 012</p>\n<p><img src=\"cid:wave\" alt=\"\" width=\"64\"></p>",
    images: [{ name: "wave", file: "wave-mark.png", size: "6 KB" }],
  },
};
