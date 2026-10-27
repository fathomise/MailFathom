// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>Counts what one account's outbox holds, which is what a client decides whether to show it from.</summary>
/// <remarks>
/// Two numbers rather than one, because they ask for different things of the person: what is waiting will leave on its
/// own, and what was refused waits for them. Each counts exactly the entries a listing holds at those stages, so a
/// dismissal lowers one of them and a client shows the outbox while either is above zero.
/// </remarks>
/// <param name="AccountId">The account counted.</param>
/// <param name="Waiting">The sends at <c>Recorded</c>, and those at <c>TransmissionBegun</c> nobody dismissed.</param>
/// <param name="Refused">The sends at <c>Refused</c> nobody dismissed.</param>
public sealed record AccountOutboxCount(MailAccountId AccountId, int Waiting, int Refused);
