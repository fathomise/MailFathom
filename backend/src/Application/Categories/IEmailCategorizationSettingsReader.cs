// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Categories;

/// <summary>Answers what an account decided about sorting its mail into categories.</summary>
public interface IEmailCategorizationSettingsReader
{
    /// <summary>Reads one account's settings.</summary>
    /// <param name="account">The account.</param>
    /// <returns>Its settings, or <see cref="EmailCategorizationSettings.Disabled" /> for an account this deployment does not declare.</returns>
    EmailCategorizationSettings SettingsFor(MailAccountId account);
}
