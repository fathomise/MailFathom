// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Answers how long one mail account keeps its visible mail.</summary>
public interface IMailMessageRetentionReader
{
    /// <summary>Gets the message retention currently configured for one account.</summary>
    /// <param name="account">The account asked about.</param>
    /// <returns>The retention, which is <see cref="MailMessageRetention.Disabled" /> for an account no longer configured.</returns>
    /// <remarks>The age is read from the configuration as it stands, so a pass reaches mail stored before it was turned on.</remarks>
    MailMessageRetention GetMessageRetention(MailAccountId account);
}
