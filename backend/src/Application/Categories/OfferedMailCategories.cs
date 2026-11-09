// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;

namespace MailFathom.Application.Categories;

/// <summary>Applies what each account offers to a read of its mail, in both directions: what may be asked for, and what may be shown.</summary>
/// <param name="offerReader">Answers which categories each account offers.</param>
/// <param name="recordReader">Reads the categories recorded on stored messages.</param>
/// <remarks>
/// A switched-off default stays recorded, so every read that names or publishes a category has to come through here.
/// Two readers each remembering that would be two chances for a hidden category to leak from one of them.
/// </remarks>
public sealed class OfferedMailCategories(
    IMailCategoryOfferReader offerReader,
    IEmailCategoryRecordReader recordReader)
{
    /// <summary>Reads the category a request asked for against the accounts it reads.</summary>
    /// <param name="scope">The accounts and folders the read is restricted to.</param>
    /// <param name="category">The caller's text, or <see langword="null" /> or blank for any category.</param>
    /// <returns>The selection, or <see langword="null" /> where no category was asked for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxQueryFilterInvalidException">Thrown when no account in scope offers the name, a default switched off included.</exception>
    /// <remarks>
    /// One account offering the name is enough, because a read naming no account reads every account its caller may, and
    /// a difference between two mailboxes' settings must not refuse it. The accounts that do not offer it are left out of
    /// the selection, which is what keeps a category one of them has hidden from selecting its mail.
    /// </remarks>
    public EmailCategorySelection? SelectionWithin(MailboxScope scope, string? category)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        var requested = category.Trim();
        var offers = scope.AccountIds.Select(account => (Account: account, Offer: offerReader.OfferOf(account))).ToArray();
        var offering = offers
            .Select(entry => (entry.Account, Name: entry.Offer.NameMatching(requested)))
            .Where(static entry => entry.Name is not null)
            .ToArray();

        if (offering.Length is 0)
        {
            throw MailboxQueryFilterInvalidException.CategoryNotOffered(
            [
                .. offers
                    .SelectMany(static entry => entry.Offer.Names)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.Ordinal),
            ]);
        }

        return new EmailCategorySelection(
            requested.ToUpperInvariant(),
            [.. offering.Select(static entry => entry.Name!).Distinct(StringComparer.Ordinal)],
            [.. offering.Select(static entry => entry.Account)]);
    }

    /// <summary>Puts on each summary the categories its account shows.</summary>
    /// <param name="summaries">The summaries about to be published.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The summaries in the same order, each carrying its categories.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summaries" /> is <see langword="null" />.</exception>
    public async Task<IReadOnlyList<EmailSummary>> WithCategoriesAsync(
        IReadOnlyList<EmailSummary> summaries,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var offers = summaries
            .Select(static summary => summary.Account)
            .Distinct()
            .ToDictionary(static account => account, offerReader.OfferOf);
        var asked = summaries
            .Where(summary => offers[summary.Account].Names.Count > 0)
            .Select(static summary => summary.StoredEmailId)
            .ToArray();

        if (asked.Length is 0)
        {
            return summaries;
        }

        var recorded = await recordReader.ReadAsync(asked, cancellationToken);

        return
        [
            .. summaries.Select(summary => recorded.TryGetValue(summary.StoredEmailId, out var names)
                ? summary with { Categories = offers[summary.Account].Shown(names) }
                : summary),
        ];
    }
}
