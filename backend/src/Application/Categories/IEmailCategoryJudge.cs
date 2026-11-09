// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Categories;

/// <summary>Asks a decision model what one message is, and which of its mailbox's own categories it matches.</summary>
/// <remarks>
/// A port for the reason the spam model stage's judge is one: nothing of the model, the questions, or the provider
/// crosses into this layer. Every way the call can go wrong is an outcome rather than an exception, because
/// categorization fails open.
/// </remarks>
public interface IEmailCategoryJudge
{
    /// <summary>Gets whether a decision model serves categorization at all.</summary>
    bool IsActive { get; }

    /// <summary>Asks about one message, in one request.</summary>
    /// <param name="evidence">What the model reads, already minimised.</param>
    /// <param name="ownCategories">The mailbox's own categories, each asked as one yes-or-no question.</param>
    /// <param name="cancellationToken">Withdraws the call.</param>
    /// <returns>What the message holds, or the reason there is no answer.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller withdrew the call.</exception>
    Task<EmailCategoryJudgement> JudgeAsync(
        EmailCategoryEvidence evidence,
        IReadOnlyList<OwnMailCategory> ownCategories,
        CancellationToken cancellationToken);
}
