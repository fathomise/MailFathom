// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Persistence.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Recognizes the authors of stored messages by the contact books their receiving accounts reach as those books stand now.</summary>
/// <remarks>
/// <para>
/// This is the one place a read combines a stored verdict with the books, and every read of a verdict goes through it —
/// a summary, a message's provenance, and a rule evaluation alike — so the client, MCP, the agent, spam classification,
/// the automatic reply, and the rules all see the same answer. The books a receiving account reaches are its
/// organization's and the own book of every user it is assigned to. A mailbox's collected book is never read:
/// collection writes down whoever wrote to the mailbox, forgers included. Of what those books hold,
/// <see cref="SenderTrustHolding.RecognizingContactsOf" /> counts only an address a person or an administrator wrote.
/// </para>
/// <para>
/// The rows of one read are answered together: one read of the books their accounts reach, then one query over their
/// senders' addresses in those books, on the index the book and the address make. An account identifier that is not a
/// stored record's — an account declared only in configuration — reaches no book, because no assignment names it.
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

        IReadOnlyList<SenderTrustHolding> holdings = accounts.Count == 0
            ? []
            : await ReadHoldingsAsync(context, accounts, addresses, cancellationToken);

        return
        [
            .. rows.Select(row => row.Recognize(row.Address is { } address
                ? SenderTrustHolding.RecognizingContactsOf(holdings, row.MailboxAccountId, address)
                : RecognizingContacts.None)),
        ];
    }

    /// <summary>Reads every holding of the addresses in the books the accounts reach, whatever wrote it.</summary>
    private static async Task<IReadOnlyList<SenderTrustHolding>> ReadHoldingsAsync(
        MailFathomDbContext context,
        Dictionary<Guid, string> accounts,
        string[] addresses,
        CancellationToken cancellationToken)
    {
        var books = await ReadBooksAsync(context, accounts, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        Guid[] accountIds = [.. accounts.Keys];

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
                accounts[organization.Id],
                ContactBookHolder.OfOrganization(organization.OrganizationId).Key,
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
