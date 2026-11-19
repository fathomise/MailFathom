// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

MailFathomDesign.data.calendarDays = [
  { dow: "MON", d: "24", today: false, ev: [
    { time: "11:00", h: 11, t: "Call with Wrona Law Office", kind: "meeting", src: "Legal opinion - indexation cap" },
  ] },
  { dow: "TUE", d: "25", today: false, ev: [
    { time: "12:30", h: 12, t: "CPI calculation from Marta", kind: "mail", src: "CPI calculation 2027" },
  ] },
  { dow: "WED", d: "26", today: false, ev: [
    { time: "09:00", h: 9, t: "Fabrikam rollout review", kind: "meeting", src: "Q4 delivery dates", link: "https://meet.fabrikam.example/j/4821-rollout-review" },
  ] },
  { dow: "THU", d: "27", today: true, ev: [
    { time: "12:30", h: 12, t: "Budget sync with Adam", kind: "meeting", src: "CPI calculation 2027" },
    { time: "15:00", h: 15, t: "Rollout status with Piotr", kind: "meeting", src: "Re: rollout schedule", link: "https://meet.nordwind.example/r/rollout-status" },
  ] },
  { dow: "FRI", d: "28", today: false, ev: [
    { time: "all day", h: 0, t: "Decision: Contoso addendum", kind: "deadline", src: "Contract addendum - signatures" },
    { time: "13:00", h: 13, t: "Call with Anna Kowalska", kind: "meeting", src: "Contract addendum - signatures" },
  ] },
  { dow: "SAT", d: "29", today: false, ev: [
    { time: "all day", h: 0, t: "Payment of invoice 08/2026", kind: "deadline", src: "Invoice 08/2026" },
  ] },
  { dow: "SUN", d: "30", today: false, ev: [] },
];


// The sample week as dates, for reading a day out of a typed event.
MailFathomDesign.data.weekdayDates = { "monday": "24", "tuesday": "25", "wednesday": "26", "thursday": "27", "friday": "28", "saturday": "29", "sunday": "30", "today": "27", "tomorrow": "28", "day after tomorrow": "29" };

// The line above the calendar.
MailFathomDesign.data.calendarBriefing = "Three tasks due today, one of them clashing with a meeting, and 2 meetings. The Contoso addendum decision is due tomorrow - the draft reply still is not ready.";

// What an event made from the open thread starts with.
MailFathomDesign.data.eventFromThread = { day: "28", date: "28.08", time: "13:00" };
