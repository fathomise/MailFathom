// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Categories;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Answers every category question the same way, and remembers what it was asked.</summary>
internal sealed class ScriptedEmailCategoryJudge : IEmailCategoryJudge
{
    /// <inheritdoc />
    public bool IsActive { get; set; } = true;

    /// <summary>Gets or sets the answer to the whole set of questions.</summary>
    public EmailCategoryJudgement Answer { get; set; } = EmailCategoryJudgement.Answered([]);

    /// <summary>Gets or sets the answer to one own category's question.</summary>
    public EmailCategoryJudgement OwnCategoryAnswer { get; set; } = EmailCategoryJudgement.Answered([]);

    /// <summary>Gets or sets what each question is asked under now.</summary>
    public EmailCategoryQuestionRevisions Revisions { get; set; } = new("default-revision", new Dictionary<string, string>());

    /// <summary>Gets or sets what runs as each question is asked.</summary>
    public Action? OnAsked { get; set; }

    /// <summary>Gets every consultation of the whole set.</summary>
    public List<(EmailCategoryEvidence Evidence, IReadOnlyList<OwnMailCategory> OwnCategories)> Asked { get; } = [];

    /// <summary>Gets every consultation of one own category.</summary>
    public List<OwnMailCategory> AskedAbout { get; } = [];

    /// <inheritdoc />
    public Task<EmailCategoryJudgement> JudgeAsync(
        EmailCategoryEvidence evidence,
        IReadOnlyList<OwnMailCategory> ownCategories,
        CancellationToken cancellationToken)
    {
        this.Asked.Add((evidence, ownCategories));
        this.OnAsked?.Invoke();

        return Task.FromResult(this.Answer);
    }

    /// <inheritdoc />
    public Task<EmailCategoryJudgement> JudgeOwnCategoryAsync(
        EmailCategoryEvidence evidence,
        OwnMailCategory category,
        CancellationToken cancellationToken)
    {
        this.AskedAbout.Add(category);
        this.OnAsked?.Invoke();

        return Task.FromResult(this.OwnCategoryAnswer);
    }

    /// <inheritdoc />
    public EmailCategoryQuestionRevisions RevisionsOf(IReadOnlyList<OwnMailCategory> ownCategories) => this.Revisions;
}
