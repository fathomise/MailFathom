// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Carries a send of the organization that this deployment saw arrive in the receiving account.</summary>
/// <param name="OutgoingEmail">The send the arrived message is, found in the outbox of the account the organization's signature proved sent it.</param>
/// <param name="Outcomes">A delivered outcome for each of the send's recipients that is the receiving account.</param>
public sealed record OrganizationDelivery(OutgoingEmailId OutgoingEmail, IReadOnlyList<RecipientOutcomeUpdate> Outcomes);
