// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Mutations;

/// <summary>How many changes MailFathom holds for one account that its mail server does not have yet, and since when the oldest has waited.</summary>
/// <param name="Count">How many changes wait; at least one.</param>
/// <param name="OldestCommittedAt">When the oldest of them was committed, which is the instant a person first saw it as made.</param>
/// <remarks>
/// It is the one place a person is told that their mail server is behind what MailFathom shows: the account freshness
/// line turns the account not current once the oldest change has waited sixty seconds, measured from this instant
/// rather than from when a client first read it.
/// </remarks>
public sealed record MailboxWaitingChanges(int Count, DateTimeOffset OldestCommittedAt);
