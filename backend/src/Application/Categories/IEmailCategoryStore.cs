// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Categories;

/// <summary>Reads the arriving mail still owed a category, and writes down what a model said about it.</summary>
public interface IEmailCategoryStore
{
    /// <summary>Reads one keyset batch of the account's arriving mail no model has categorized.</summary>
    /// <param name="account">The account.</param>
    /// <param name="resumeAfter">The last message of the previous batch, or <see langword="null" /> to start from the first.</param>
    /// <param name="batchSize">The most messages to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages in identity order.</returns>
    Task<IReadOnlyList<EmailAwaitingCategorization>> GetEmailsAwaitingCategorizationAsync(
        MailAccountId account,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Stages what a model said a message holds.</summary>
    /// <param name="session">The transaction to stage it in.</param>
    /// <param name="email">The message.</param>
    /// <param name="assignments">Its categories.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="false" /> where the message is already categorized or is gone, so nothing was staged.</returns>
    Task<bool> SaveAsync(
        IPersistenceSession session,
        StoredEmailId email,
        IReadOnlyList<EmailCategoryAssignment> assignments,
        CancellationToken cancellationToken);
}
