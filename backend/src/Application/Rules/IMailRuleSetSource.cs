// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules;

/// <summary>Reads the rule set a pass over one mail account runs against.</summary>
/// <remarks>
/// A pass reads its account's set once when it starts and holds what it was given for its duration. That is the whole
/// of the change contract for rules: a write committed while a pass runs reaches the next pass rather than this one, on
/// every replica, because the database is the only source and a commit produces a revision nobody has compiled yet.
/// There is nothing to invalidate and no signal to send.
/// </remarks>
public interface IMailRuleSetSource
{
    /// <summary>Reads and compiles one account's enabled rules as they stand now.</summary>
    /// <param name="account">The account the pass runs over.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account's rule set, held when one of its enabled rules no longer compiles.</returns>
    Task<MailRuleSet> ReadAsync(MailAccountId account, CancellationToken cancellationToken);
}
