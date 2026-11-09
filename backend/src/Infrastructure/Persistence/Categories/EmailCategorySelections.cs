// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Categories;

/// <summary>Names the stored category records a change to an account's own categories reaches.</summary>
internal static class EmailCategorySelections
{
    /// <summary>Narrows to the records of one account naming an own category its record no longer declares.</summary>
    /// <param name="records">The records to narrow.</param>
    /// <param name="account">The account the mail belongs to.</param>
    /// <param name="declaredNames">Every own category the account's record declares now, compared exactly.</param>
    /// <returns>The records to remove; a default's are never among them.</returns>
    internal static IQueryable<EmailCategoryEntity> OfWithdrawnOwnCategories(
        this IQueryable<EmailCategoryEntity> records,
        string account,
        IReadOnlyList<string> declaredNames) =>
        records.Where(record => record.Kind == EmailCategoryKind.Own
            && record.StoredEmail!.MailboxAccountId == account
            && !declaredNames.Contains(record.Category));
}
