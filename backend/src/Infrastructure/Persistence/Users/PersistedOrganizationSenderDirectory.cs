// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Dkim;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads what verifying an organization's signature needs from PostgreSQL.</summary>
/// <remarks>
/// Every read uses the scoped context and joins no transaction, because extraction runs before the batch it belongs to
/// commits. The context is resolved on the first read rather than when the reader is composed, so composing the
/// extraction chain opens no database and an account answered without one never resolves it. An account identifier
/// that is not a stored record's is an account in no organization, which is what an account declared only in
/// configuration is.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedOrganizationSenderDirectory(Lazy<MailFathomDbContext> readContext) : IOrganizationSenderDirectory
{
    private readonly Dictionary<Guid, Guid?> organizationsByAccount = [];

    /// <inheritdoc />
    /// <remarks>
    /// Remembered for the scope this instance lives in, because the messages one scope extracts arrive in a handful of
    /// accounts at most, and asking once per message would be a round trip per arrival for an answer that moves only
    /// when an administrator moves an account. A move is seen by the next scope.
    /// </remarks>
    public async Task<Guid?> FindOrganizationAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(account.Value, out var accountId))
        {
            return null;
        }

        if (!this.organizationsByAccount.TryGetValue(accountId, out var organizationId))
        {
            organizationId = await readContext.Value.MailAccountRecords
                .AsNoTracking()
                .Where(record => record.Id == accountId)
                .Select(record => record.OrganizationId)
                .FirstOrDefaultAsync(cancellationToken);
            this.organizationsByAccount[accountId] = organizationId;
        }

        return organizationId;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<byte>?> FindVerifyingKeyAsync(
        Guid organizationId,
        Guid keyId,
        CancellationToken cancellationToken)
    {
        var publicKey = await readContext.Value.OrganizationSigningKeys
            .AsNoTracking()
            .Where(key => key.Id == keyId && key.OrganizationId == organizationId && key.RevokedAt == null)
            .Select(key => key.PublicKey)
            .FirstOrDefaultAsync(cancellationToken);

        return publicKey is null ? null : publicKey.AsMemory();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unbounded by design: the answer is the organization's identifiers and nothing else, an organization holds the
    /// accounts its administrator assigned rather than anything a sender writes, and it is read only for a message
    /// whose signature already verified against the organization's own key.
    /// </remarks>
    public async Task<IReadOnlyList<MailAccountId>> ListAccountsAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var accounts = await readContext.Value.MailAccountRecords
            .AsNoTracking()
            .Where(record => record.OrganizationId == organizationId)
            .Select(record => record.Id)
            .ToListAsync(cancellationToken);

        return [.. accounts.Select(static account => MailAccountId.Create(account.ToString("D")))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserId>> ListAssignedUsersAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var context = readContext.Value;
        var users = await context.MailAccountAssignments
            .AsNoTracking()
            .Where(assignment => context.MailAccountRecords
                .Any(record => record.Id == assignment.MailAccountId && record.OrganizationId == organizationId))
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return [.. users.Order().Select(UserId.Create)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The addresses are compared in memory rather than in SQL, because a recipient is stored as the request named it
    /// and only <see cref="EmailAddress" /> knows the comparison form. A record holds at most
    /// <see cref="OutgoingEmailRequest.MaximumRecipientCount" /> recipients, so what is read is bounded by the number
    /// of records one account sent with one <c>Message-ID</c>, which is one.
    /// </remarks>
    public async Task<IReadOnlyList<EmailAddress>> ListBlindCopyRecipientsAsync(
        IReadOnlyCollection<MailAccountId> sentThrough,
        string internetMessageId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sentThrough);

        string[] accounts = [.. sentThrough.Select(static account => account.Value)];

        var addresses = await readContext.Value.OutgoingEmailRecipients
            .AsNoTracking()
            .Where(recipient => recipient.Role == OutgoingRecipientRole.Bcc
                && recipient.OutgoingEmail.InternetMessageId == internetMessageId
                && accounts.Contains(recipient.OutgoingEmail.MailboxAccountId))
            .Select(recipient => recipient.Address)
            .ToListAsync(cancellationToken);

        return
        [
            .. addresses
                .Select(static address => EmailAddress.TryCreate(displayName: null, address, out var parsed) ? parsed : (EmailAddress?)null)
                .OfType<EmailAddress>(),
        ];
    }
}
