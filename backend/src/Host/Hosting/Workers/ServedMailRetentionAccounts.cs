// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Retention;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Reads the accounts a retention walk visits from the set this deployment serves.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this source.")]
internal sealed class ServedMailRetentionAccounts(IServedMailAccountReader servedAccounts) : IMailRetentionAccounts
{
    /// <inheritdoc />
    public async Task<MailRetentionAccountPage> ReadPageAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        var page = await servedAccounts.ReadServedVersionsAsync(after, limit, cancellationToken);

        return new MailRetentionAccountPage(
            [.. page.Select(static served => MailAccountId.Create(served.Id.ToString("D")))],
            page.Count == limit ? page[^1].Id : null);
    }
}
