// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Resolves the identity any account sends as, whichever accounts the current work unit was prepared with.</summary>
/// <remarks>
/// <see cref="IOutgoingSenderIdentityReader" /> answers from the settings a work unit was prepared with, which hold the
/// accounts of the users it acts for and no others. A question about an account outside that set — a colleague's, whose
/// mail arrived in an account somebody else is assigned — is answered here instead, from that account's own settings.
/// </remarks>
public interface ISenderIdentityDirectory
{
    /// <summary>Gets the identity one account sends as.</summary>
    /// <param name="accountId">The account, which need not be one the current work unit holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The identity, or <see langword="null" /> when the account is not served or configures no address to send from.</returns>
    Task<OutgoingSenderIdentity?> FindSenderIdentityAsync(MailAccountId accountId, CancellationToken cancellationToken);
}
