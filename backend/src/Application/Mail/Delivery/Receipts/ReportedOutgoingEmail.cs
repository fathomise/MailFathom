// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Carries the account's own message a report named, as far as applying the report needs it.</summary>
/// <param name="Id">The outgoing email.</param>
/// <param name="Subject">The subject it was composed with, or <see langword="null" /> where it had none.</param>
/// <param name="RecipientAddresses">Its recipients' addresses, position by position in the order it named them.</param>
public sealed record ReportedOutgoingEmail(OutgoingEmailId Id, string? Subject, IReadOnlyList<string> RecipientAddresses);
