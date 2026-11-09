// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Categories;

/// <summary>Answers which categories one mail account offers.</summary>
/// <remarks>Read through a port and re-read per request, so a write to the account's record takes effect at once and without anything being reclassified.</remarks>
public interface IMailCategoryOfferReader
{
    /// <summary>Gets what one account offers now.</summary>
    /// <param name="account">The account whose mail the question is about.</param>
    /// <returns>Its offer, or <see cref="MailCategoryOffer.None" /> where it does not categorize or this deployment serves no such account.</returns>
    MailCategoryOffer OfferOf(MailAccountId account);
}
