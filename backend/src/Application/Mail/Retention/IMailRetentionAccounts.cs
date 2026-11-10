// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Mail.Retention;

/// <summary>Answers which mail accounts a retention walk may reach.</summary>
/// <remarks>
/// An account this deployment does not serve is on no page, an account nobody is assigned to among them. A run already
/// queued for an account that has since lost its last user is a job naming that account, which the queue does not hand
/// out while nobody is assigned it.
/// </remarks>
public interface IMailRetentionAccounts
{
    /// <summary>Reads the next page of accounts in identifier order.</summary>
    /// <param name="after">The account the previous page ended at, or <see langword="null" /> to begin.</param>
    /// <param name="limit">How many accounts one page reads.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts retention may run for, and where the next page begins.</returns>
    Task<MailRetentionAccountPage> ReadPageAsync(Guid? after, int limit, CancellationToken cancellationToken);
}
