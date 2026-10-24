// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;

namespace MailFathom.Application.Mail.Delivery.Tracking;

/// <summary>One bounded page of an account's outbox, newest first.</summary>
/// <param name="Sends">The sends, ordered by when each one was written down, newest first.</param>
/// <param name="NextCursor">The boundary the following page is asked with, and <see langword="null" /> at the end of the outbox.</param>
public sealed record AccountOutboxPage(IReadOnlyList<AccountOutboxEntry> Sends, OutboxCursor? NextCursor);
