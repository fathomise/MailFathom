// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery.Receipts;

namespace MailFathom.Application.Emails.Summaries;

/// <summary>Marks a stored email as the delivery report or read receipt it was recognized as when it arrived.</summary>
/// <param name="Kind">Which of the two reports it is.</param>
/// <param name="Recipients">What it said about each recipient it named, in the order it named them.</param>
/// <param name="ReportedAt">When it says it was written, or when it arrived where it carried no usable date.</param>
/// <param name="NamesOwnMessage">Whether it named a message this account sent, which is the only case its outcomes were applied.</param>
/// <param name="OwnMessageSubject">The subject of that message, or <see langword="null" /> where it names none or that one had no subject.</param>
/// <remarks>
/// The message stays an ordinary stored email in the folder it arrived in; the mark only changes what is said about it.
/// A report marks a message whether or not it named one of the account's own, because a bounce for mail the account
/// never sent arrives as mail all the same.
/// </remarks>
public sealed record EmailReportMark(
    ReceiptKind Kind,
    IReadOnlyList<EmailReportRecipient> Recipients,
    DateTimeOffset? ReportedAt,
    bool NamesOwnMessage,
    string? OwnMessageSubject);
