// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Mcp.Tools;

/// <summary>The published wording of the send time <c>send_email</c>, <c>reply_to_email</c>, and <c>forward_email</c> take identically.</summary>
/// <remarks>
/// The three read the same two arguments through the same parse into the same hold, so what a caller learns about a
/// send time from one of them is true of the others; written three times, the wording would drift by an edit to one
/// of them, which no build compares. A <c>const</c> reference in an attribute is the literal it names, so each tool's
/// published input schema is unchanged by the sharing.
/// </remarks>
internal static class SendTimeDescriptions
{
    /// <summary>The <c>sendAt</c> argument, in either of its two forms.</summary>
    public const string SendAt =
        "When the message should leave, as an ISO 8601 date and time, or omit it to send as soon as possible. Two forms "
        + "are accepted. With an offset or Z and no sendAtTimeZone, such as 2026-11-09T09:00:00+01:00, it is that exact "
        + "instant. Without an offset and together with sendAtTimeZone, such as 2026-11-09T09:00, it is that time on a "
        + "clock in that zone — prefer this form when the person named a local time, because the zone's daylight-saving "
        + "rules decide the offset and you do not have to know it. Writing an offset and a zone together is refused. The "
        + "time must still be to come and no further ahead than the deployment holds a message, which is a few months "
        + "by default; the refusal says how far. A repeated call with an idempotencyKey already used keeps the time the "
        + "first call named: a retry never reschedules.";

    /// <summary>The <c>sendAtTimeZone</c> argument, which is only ever given beside a <c>sendAt</c> without an offset.</summary>
    public const string SendAtTimeZone =
        "The IANA time zone sendAt is read in, such as Europe/Warsaw, when sendAt is written without an offset. Omit it "
        + "when sendAt carries its own offset, and omit both to send as soon as possible; a zone given without sendAt is "
        + "refused.";
}
