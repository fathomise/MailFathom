// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Persistence.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Recognizes the authors of stored messages by the member directory and the contact books their receiving accounts reach as those stand now.</summary>
/// <remarks>
/// <para>
/// This is the one place a read combines a stored verdict with the books, and every read of a verdict goes through it —
/// a summary, a message's provenance, and a rule evaluation alike — so the client, MCP, the agent, spam classification,
/// the automatic reply, and the rules all see the same answer. The books a receiving account reaches are its
/// organization's and the own book of every user it is assigned to. A mailbox's collected book is never read:
/// collection writes down whoever wrote to the mailbox, forgers included. Of what those books hold,
/// <see cref="SenderTrustHolding.RecognizingContactsOf" /> counts only an address a person or an administrator wrote.
/// The member directory is read on every account that belongs to an organization, the whole of it, the users the
/// account is assigned to included.
/// </para>
/// <para>
/// The rows of one read are answered together: one read of the accounts' organizations and one of their assignments,
/// one query over their senders' addresses in the books those reach, on the index the book and the address make, and
/// one over the same addresses among the organizations' members, on the index on the accounts' comparison form. An
/// account identifier that is not a stored record's — an account declared only in configuration — reaches no book and
/// no directory, because no record names it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class SenderTrustBooks
{
    /// <summary>Reaches the verdict each row's reader sees.</summary>
    /// <param name="context">The context the books are read through, untracked.</param>
    /// <param name="rows">The stored verdicts, at most one read's worth.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>One verdict per row, in the same order.</returns>
    internal static async Task<IReadOnlyList<SenderTrust>> RecognizeAsync(
        MailFathomDbContext context,
        IReadOnlyList<StoredSenderTrustRow> rows,
        CancellationToken cancellationToken)
    {
        string[] addresses = [.. rows.Select(static row => row.Address).OfType<string>().Distinct(StringComparer.Ordinal)];
        Dictionary<Guid, string> accounts = addresses.Length == 0
            ? []
            : rows
                .Where(static row => row.Address is not null)
                .Select(static row => row.MailboxAccountId)
                .Distinct(StringComparer.Ordinal)
                .Select(static account => (Account: account, Parsed: Guid.TryParse(account, out var id) ? id : (Guid?)null))
                .Where(static pair => pair.Parsed is not null)
                .ToDictionary(static pair => pair.Parsed!.Value, static pair => pair.Account);

        if (accounts.Count == 0)
        {
            return [.. rows.Select(static row => row.Recognize(RecognizingContacts.None))];
        }

        var organizations = await ReadOrganizationsAsync(context, accounts, cancellationToken);
        var holdings = await ReadHoldingsAsync(context, accounts, organizations, addresses, cancellationToken);
        var members = await ReadMembersAsync(context, organizations, addresses, cancellationToken);

        return
        [
            .. rows.Select(row => row.Recognize(row.Address is { } address
                ? SenderTrustHolding.RecognizingContactsOf(holdings, row.MailboxAccountId, address) with
                {
                    DirectoryMember = organizations.TryGetValue(row.MailboxAccountId, out var organization)
                        && members.TryGetValue((organization, address), out var member)
                            ? member
                            : null,
                }
                : RecognizingContacts.None)),
        ];
    }

    /// <summary>Reads the organization each account belongs to, keyed by the account as the rows name it, leaving out an account in none.</summary>
    private static async Task<Dictionary<string, Guid>> ReadOrganizationsAsync(
        MailFathomDbContext context,
        Dictionary<Guid, string> accounts,
        CancellationToken cancellationToken)
    {
        Guid[] accountIds = [.. accounts.Keys];

        var organizations = await context.MailAccountRecords
            .AsNoTracking()
            .Where(record => accountIds.Contains(record.Id) && record.OrganizationId != null)
            .Select(record => new { record.Id, OrganizationId = record.OrganizationId!.Value })
            .ToArrayAsync(cancellationToken);

        return organizations.ToDictionary(
            organization => accounts[organization.Id],
            static organization => organization.OrganizationId,
            StringComparer.Ordinal);
    }

    /// <summary>Names the member each address is the address of, in each organization, under the rules the member directory reads its members' addresses by.</summary>
    /// <remarks>
    /// An address is a member's where an account in the organization holds it, a person of the same organization is
    /// assigned it, and nobody else is: a shared mailbox is never one person's. The address is unique across the
    /// deployment, so each one names at most one member.
    /// </remarks>
    private static async Task<Dictionary<(Guid Organization, string Address), UserId>> ReadMembersAsync(
        MailFathomDbContext context,
        Dictionary<string, Guid> organizations,
        string[] addresses,
        CancellationToken cancellationToken)
    {
        Guid?[] organizationIds = [.. organizations.Values.Distinct().Select(static organization => (Guid?)organization)];

        if (organizationIds.Length == 0)
        {
            return [];
        }

        var members = await context.MailAccountRecords
            .AsNoTracking()
            .Where(account => organizationIds.Contains(account.OrganizationId)
                && addresses.Contains(account.NormalizedEmailAddress!))
            .Join(
                context.MailAccountAssignments,
                static account => account.Id,
                static assignment => assignment.MailAccountId,
                static (account, assignment) => new { account.Id, account.OrganizationId, account.NormalizedEmailAddress, assignment.UserId })
            .Join(
                context.UserAccounts,
                static held => held.UserId,
                static user => user.Id,
                static (held, user) => new { held, user.OrganizationId, user.Type })
            .Where(member => member.OrganizationId == member.held.OrganizationId
                && member.Type == UserType.User
                && !context.MailAccountAssignments.Any(other =>
                    other.MailAccountId == member.held.Id && other.UserId != member.held.UserId))
            .Select(static member => new
            {
                Organization = member.held.OrganizationId!.Value,
                Address = member.held.NormalizedEmailAddress!,
                member.held.UserId,
            })
            .ToArrayAsync(cancellationToken);

        return members.ToDictionary(
            static member => (member.Organization, member.Address),
            static member => UserId.Create(member.UserId));
    }

    /// <summary>Reads every holding of the addresses in the books the accounts reach, whatever wrote it.</summary>
    private static async Task<IReadOnlyList<SenderTrustHolding>> ReadHoldingsAsync(
        MailFathomDbContext context,
        Dictionary<Guid, string> accounts,
        Dictionary<string, Guid> organizations,
        string[] addresses,
        CancellationToken cancellationToken)
    {
        var books = await ReadBooksAsync(context, accounts, organizations, cancellationToken);
        string[] holderKeys = [.. books.Select(static book => book.HolderKey).Distinct(StringComparer.Ordinal)];

        if (holderKeys.Length == 0)
        {
            return [];
        }

        var held = await context.ContactAddresses
            .AsNoTracking()
            .Where(row => holderKeys.Contains(row.BookHolderId) && addresses.Contains(row.NormalizedAddress))
            .Select(row => new { row.ContactId, row.BookHolderId, row.NormalizedAddress, row.WriterKind })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. books.Join(
                held,
                static book => book.HolderKey,
                static row => row.BookHolderId,
                static (book, row) => new SenderTrustHolding(
                    book.Account,
                    row.NormalizedAddress,
                    row.ContactId,
                    book.IsOrganizationBook,
                    row.WriterKind),
                StringComparer.Ordinal),
        ];
    }

    /// <summary>Lists each account's organization book and the own book of every user it is assigned to.</summary>
    private static async Task<List<ReachedBook>> ReadBooksAsync(
        MailFathomDbContext context,
        Dictionary<Guid, string> accounts,
        Dictionary<string, Guid> organizations,
        CancellationToken cancellationToken)
    {
        Guid[] accountIds = [.. accounts.Keys];

        var assignments = await context.MailAccountAssignments
            .AsNoTracking()
            .Where(assignment => accountIds.Contains(assignment.MailAccountId))
            .Select(assignment => new { assignment.MailAccountId, assignment.UserId })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. organizations.Select(static organization => new ReachedBook(
                organization.Key,
                ContactBookHolder.OfOrganization(organization.Value).Key,
                IsOrganizationBook: true)),
            .. assignments.Select(assignment => new ReachedBook(
                accounts[assignment.MailAccountId],
                ContactBookHolder.Of(UserId.Create(assignment.UserId)).Key,
                IsOrganizationBook: false)),
        ];
    }

    /// <summary>One book a receiving account reaches.</summary>
    private sealed record ReachedBook(string Account, string HolderKey, bool IsOrganizationBook);
}
