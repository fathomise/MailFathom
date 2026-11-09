// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Retention;

/// <summary>One page of the accounts a retention walk visits.</summary>
/// <param name="Accounts">The accounts of the page retention may run for.</param>
/// <param name="ResumeAfter">Where the next page begins, or <see langword="null" /> when this was the last.</param>
public sealed record MailRetentionAccountPage(IReadOnlyList<MailAccountId> Accounts, Guid? ResumeAfter);
