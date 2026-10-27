// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Infrastructure.Mail.Dkim;

/// <summary>Answers what verifying an organization's signature needs to know about the deployment's own state.</summary>
/// <remarks>
/// Every answer is read at verification time, so a key revoked, a member who left, and a send record deleted since the
/// message was signed each change the next verification and nothing already stored. No answer carries an organization's
/// name, and none of them is logged.
/// </remarks>
internal interface IOrganizationSenderDirectory
{
    /// <summary>Finds the organization an account belongs to.</summary>
    /// <param name="account">The account.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization, or <see langword="null" /> where the account belongs to none or is no longer held.</returns>
    Task<Guid?> FindOrganizationAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Finds the public half of one of an organization's keys, unless it was revoked.</summary>
    /// <param name="organizationId">The organization the key must belong to.</param>
    /// <param name="keyId">The key a signature's selector names.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The 32-byte Ed25519 public key of an active or retired key, or <see langword="null" /> for a revoked key, another organization's key, or none.</returns>
    Task<ReadOnlyMemory<byte>?> FindVerifyingKeyAsync(Guid organizationId, Guid keyId, CancellationToken cancellationToken);

    /// <summary>Lists the mail accounts that belong to an organization now.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts, in no particular order.</returns>
    Task<IReadOnlyList<MailAccountId>> ListAccountsAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Lists the users any of an organization's mail accounts is assigned to now, in one read.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each such user once, in the order of their identifiers.</returns>
    /// <remarks>An account's sending identity is composed from the records of the users it is assigned to, so these are whom every member account's identity is read through.</remarks>
    Task<IReadOnlyList<UserId>> ListAssignedUsersAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Lists the blind-copy recipients of the sends one of several accounts made with a <c>Message-ID</c>.</summary>
    /// <param name="sentThrough">The accounts the send must have been made through.</param>
    /// <param name="internetMessageId">The <c>Message-ID</c> the enqueued MIME carried.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every blind-copy address those records name; none where no record survives.</returns>
    Task<IReadOnlyList<EmailAddress>> ListBlindCopyRecipientsAsync(
        IReadOnlyCollection<MailAccountId> sentThrough,
        string internetMessageId,
        CancellationToken cancellationToken);
}
