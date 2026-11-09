// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Answers which mail accounts a retention walk may reach.</summary>
/// <remarks>
/// An account this deployment does not serve is on no page, and neither is one an erasure on this replica has withheld.
/// An erasure on another replica is excluded by the queue instead: each account's run is a job naming that account,
/// which the erasure locks or waits for.
/// </remarks>
public interface IMailRetentionAccounts
{
    /// <summary>Reads the next page of accounts in identifier order.</summary>
    /// <param name="after">The account the previous page ended at, or <see langword="null" /> to begin.</param>
    /// <param name="limit">How many accounts one page reads.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts retention may run for, and where the next page begins.</returns>
    Task<MailRetentionAccountPage> ReadPageAsync(Guid? after, int limit, CancellationToken cancellationToken);

    /// <summary>Gets whether an erasure on this replica is deciding about the account.</summary>
    /// <param name="account">The account asked about.</param>
    /// <returns><see langword="true" /> while the account is withheld.</returns>
    bool IsWithheld(MailAccountId account);
}
