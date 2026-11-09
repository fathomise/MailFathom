// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.Categories;

/// <summary>Reads what each account offers from its own record.</summary>
/// <param name="synchronizationOptions">The snapshot of the accounts this scope may reach.</param>
internal sealed class ConfiguredMailCategoryOfferReader(MailSynchronizationOptions synchronizationOptions)
    : IMailCategoryOfferReader
{
    /// <inheritdoc />
    public MailCategoryOffer OfferOf(MailAccountId account) =>
        synchronizationOptions.FindConfiguredAccount(account)?.Categorization is { Enabled: true } categorization
            ? MailCategoryOffer.Of(categorization.DisabledDefaults, categorization.OwnCategories.Select(static category => category.Name))
            : MailCategoryOffer.None;
}
