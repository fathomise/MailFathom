// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>How many messages of one account carry one category, as a census by category reads it.</summary>
/// <param name="MailboxAccountId">The account the messages belong to.</param>
/// <param name="Category">The category as its records spell it.</param>
/// <param name="Count">How many messages of the account carry it.</param>
internal sealed record MailCategoryCensusRow(string MailboxAccountId, string Category, int Count);
