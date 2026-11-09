// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Retention;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads how long an account keeps its tombstones from the bound section.</summary>
/// <remarks>An account a reload removed after the walk handed it out reports disabled, since no operator decision applies to it any more.</remarks>
internal sealed class ConfiguredMailTombstoneRetentionReader(MailSynchronizationOptions settings)
    : IMailTombstoneRetentionReader
{
    /// <inheritdoc />
    public MailTombstoneRetention GetTombstoneRetention(MailAccountId account) =>
        settings.FindConfiguredAccount(account)?.CreateTombstoneRetention() ?? MailTombstoneRetention.Disabled;
}
