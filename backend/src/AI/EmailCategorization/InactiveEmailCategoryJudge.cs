// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;
using MailFathom.Application.Decisions;

namespace MailFathom.AI.EmailCategorization;

/// <summary>Asks nothing, on a deployment where no decision model serves categorization.</summary>
internal sealed class InactiveEmailCategoryJudge : IEmailCategoryJudge
{
    /// <summary>The one instance, since it holds nothing and answers everything the same way.</summary>
    internal static readonly InactiveEmailCategoryJudge Instance = new();

    private InactiveEmailCategoryJudge()
    {
    }

    /// <inheritdoc />
    public bool IsActive => false;

    /// <inheritdoc />
    public Task<EmailCategoryJudgement> JudgeAsync(
        EmailCategoryEvidence evidence,
        IReadOnlyList<OwnMailCategory> ownCategories,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(ownCategories);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(EmailCategoryJudgement.Unanswered(DecisionModelOutcome.NotActivated));
    }

    /// <inheritdoc />
    public Task<EmailCategoryJudgement> JudgeOwnCategoryAsync(
        EmailCategoryEvidence evidence,
        OwnMailCategory category,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(category);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(EmailCategoryJudgement.Unanswered(DecisionModelOutcome.NotActivated));
    }

    /// <inheritdoc />
    public EmailCategoryQuestionRevisions RevisionsOf(IReadOnlyList<OwnMailCategory> ownCategories) =>
        EmailCategoryQuestions.RevisionsOf(ownCategories);
}
