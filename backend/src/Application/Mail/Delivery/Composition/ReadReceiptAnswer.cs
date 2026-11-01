// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Carries the read receipt (RFC 8098) a reader asked to send in answer to a message that requested one.</summary>
/// <param name="Recipient">The address the message's <c>Disposition-Notification-To</c> named.</param>
/// <param name="Subject">The receipt's subject.</param>
/// <param name="HumanReadableText">The first part of the report, in the reader's language, for a person to read.</param>
/// <param name="OriginalMessageId">The <c>Message-ID</c> of the message being answered, or <see langword="null" /> where it carried none.</param>
/// <param name="Threading">Where the receipt sits in the conversation of the message it answers.</param>
/// <remarks>
/// The disposition is always <c>manual-action/MDN-sent-manually; displayed</c>, because the one route to a receipt is the
/// reader's own press, after the message was shown to them.
/// </remarks>
public sealed record ReadReceiptAnswer(
    string Recipient,
    string Subject,
    string HumanReadableText,
    string? OriginalMessageId,
    OutgoingThreadPlacement Threading);
