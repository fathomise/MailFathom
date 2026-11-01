// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>What verifying a message against the keys of the receiving account's organization established.</summary>
/// <param name="ReceivingOrganizationId">The organization the receiving account belongs to, whose keys were consulted.</param>
/// <param name="SenderIsMember">Whether a signature of that organization proved the author a member who sent the message to the receiving account.</param>
/// <remarks>
/// It exists only for a receiving account that belongs to an organization; an account in none verifies nothing and
/// carries no value at all. A signature that failed, named no key of the organization, or named a revoked one leaves
/// <paramref name="SenderIsMember" /> false and is never a statement against the message, because a relay may
/// legitimately have altered it.
/// </remarks>
public sealed record OrganizationSenderVerification(Guid ReceivingOrganizationId, bool SenderIsMember)
{
    /// <summary>Gets the accounts of the organization that send as the author, which the signature proved the message came through.</summary>
    /// <remarks>Empty wherever <see cref="SenderIsMember" /> is false. It is what ties the message to the send it was, so a receipt between two of the organization's accounts is recorded against that send and no other.</remarks>
    public IReadOnlyList<MailAccountId> AuthorAccounts { get; init; } = [];
}
