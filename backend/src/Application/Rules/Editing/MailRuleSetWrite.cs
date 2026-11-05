// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Editing;

/// <summary>Which account's rules a write changes, and the set version the writer read before deciding on the change.</summary>
/// <param name="Account">The account whose rules change, which must be one this deployment serves.</param>
/// <param name="ExpectedVersion">The version the writer read; the write is refused when the set has moved past it.</param>
/// <remarks>
/// Stating the version read is what turns a lost update into a refusal. A rule set is ordered and stop-when-matched, so
/// two edits to two different rules are still two edits to what the set means, and merging them would commit a set
/// neither writer wrote.
/// </remarks>
public sealed record MailRuleSetWrite(MailAccountId Account, long ExpectedVersion);
