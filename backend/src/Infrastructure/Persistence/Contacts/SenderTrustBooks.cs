// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Emails.Trust;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Reads, under the address lock and in the storing transaction, the contacts the books a receiving account reaches hold for its senders.</summary>
/// <remarks>
/// An account identifier that is not a stored record's — an account declared only in configuration — reaches no
/// organization's book and no user's, because no assignment names it, so its senders are held by nobody here.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SenderTrustBooks : ISenderTrustBooks
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SenderAddressHolding>> LockAndReadAsync(
        IPersistenceSession session,
        IReadOnlyCollection<MailAccountId> accounts,
        IReadOnlyCollection<EmailAddress> senders,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(senders);

        var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        string[] addresses = [.. senders.Select(sender => sender.NormalizedAddress).Distinct(StringComparer.Ordinal)];

        await ContactAddressLocks.TakeAsync(context, addresses, cancellationToken);

        var accountsById = accounts
            .Select(account => (Account: account, Parsed: Guid.TryParse(account.Value, out var id) ? id : (Guid?)null))
            .Where(pair => pair.Parsed is not null)
            .ToDictionary(pair => pair.Parsed!.Value, pair => pair.Account);

        if (accountsById.Count == 0 || addresses.Length == 0)
        {
            return [];
        }

        var books = await ReadBooksAsync(context, accountsById, cancellationToken);
        string[] holderKeys = [.. books.Select(book => book.HolderKey).Distinct(StringComparer.Ordinal)];

        var held = await context.ContactAddresses
            .AsNoTracking()
            .Where(row => holderKeys.Contains(row.BookHolderId) && addresses.Contains(row.NormalizedAddress))
            .Select(row => new { row.ContactId, row.BookHolderId, row.NormalizedAddress, row.WriterKind })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. books.Join(
                held,
                book => book.HolderKey,
                row => row.BookHolderId,
                (book, row) => new SenderAddressHolding(
                    book.Account,
                    row.NormalizedAddress,
                    ContactId.Create(row.ContactId),
                    book.Kind,
                    row.WriterKind),
                StringComparer.Ordinal),
        ];
    }

    /// <summary>Lists each account's organization book and the own book of every user it is assigned to.</summary>
    private static async Task<List<ReachedBook>> ReadBooksAsync(
        MailFathomDbContext context,
        Dictionary<Guid, MailAccountId> accountsById,
        CancellationToken cancellationToken)
    {
        Guid[] accountIds = [.. accountsById.Keys];

        var organizations = await context.MailAccountRecords
            .AsNoTracking()
            .Where(record => accountIds.Contains(record.Id) && record.OrganizationId != null)
            .Select(record => new { record.Id, OrganizationId = record.OrganizationId!.Value })
            .ToArrayAsync(cancellationToken);

        var assignments = await context.MailAccountAssignments
            .AsNoTracking()
            .Where(assignment => accountIds.Contains(assignment.MailAccountId))
            .Select(assignment => new { assignment.MailAccountId, assignment.UserId })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. organizations.Select(organization => new ReachedBook(
                accountsById[organization.Id],
                ContactBookHolder.OfOrganization(organization.OrganizationId).Key,
                SenderTrustBook.Organization)),
            .. assignments.Select(assignment => new ReachedBook(
                accountsById[assignment.MailAccountId],
                ContactBookHolder.Of(UserId.Create(assignment.UserId)).Key,
                SenderTrustBook.Personal)),
        ];
    }

    /// <summary>One book a receiving account reaches.</summary>
    private sealed record ReachedBook(MailAccountId Account, string HolderKey, SenderTrustBook Kind);
}
