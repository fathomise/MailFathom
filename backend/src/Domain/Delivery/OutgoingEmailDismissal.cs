// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Domain.Delivery;

/// <summary>Records that somebody took a refused send, or one whose outcome nobody will learn, out of its account's outbox.</summary>
/// <remarks>
/// It is a decision of its own rather than a stage, because nothing about the send itself changed: a refused send is
/// still refused, and one whose transmission began still has an outcome nobody knows. What changed is that a person
/// accepted that, so the send stops waiting in the place they look. Offering the send again, which only an operator can
/// do once it is dismissed, clears it.
/// </remarks>
/// <param name="DismissedAt">When the send was dismissed.</param>
/// <param name="DismissedBy">The user who dismissed it.</param>
public sealed record OutgoingEmailDismissal(DateTimeOffset DismissedAt, UserId DismissedBy);
