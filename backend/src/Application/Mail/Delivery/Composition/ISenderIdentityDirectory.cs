// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Composition;

/// <summary>Resolves the identities accounts send as, whichever users the current work unit acts for.</summary>
/// <remarks>
/// <see cref="IOutgoingSenderIdentityReader" /> answers from the settings a work unit was prepared with, which hold the
/// accounts of the users it acts for and no others. A question about accounts outside that set — a colleague's, whose
/// mail arrived in an account somebody else is assigned — is answered here instead. It is asked per user rather than per
/// account, because an account's settings are composed from the records of the users it is assigned to, so one
/// composition per user answers every account that user is served.
/// </remarks>
public interface ISenderIdentityDirectory
{
    /// <summary>Reads the identity every account the given users are served sends as.</summary>
    /// <param name="users">The users whose accounts are asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each account those users are served that configures an address to send from, with that identity.</returns>
    Task<IReadOnlyDictionary<MailAccountId, OutgoingSenderIdentity>> ReadServedIdentitiesAsync(
        IReadOnlyCollection<UserId> users,
        CancellationToken cancellationToken);
}
