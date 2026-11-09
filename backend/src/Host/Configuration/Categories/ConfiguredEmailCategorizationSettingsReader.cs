// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Configuration.Categories;

/// <summary>Reads an account's categorization from the settings its scope was prepared with.</summary>
internal sealed class ConfiguredEmailCategorizationSettingsReader(MailSynchronizationOptions synchronizationOptions)
    : IEmailCategorizationSettingsReader
{
    /// <inheritdoc />
    public EmailCategorizationSettings SettingsFor(MailAccountId account) =>
        synchronizationOptions.FindConfiguredAccount(account) is { Categorization: { Enabled: true } declared }
            ? new EmailCategorizationSettings(
                IsEnabled: true,
                [.. declared.OwnCategories.Select(static category => new OwnMailCategory(category.Name, category.Description))])
            : EmailCategorizationSettings.Disabled;
}
