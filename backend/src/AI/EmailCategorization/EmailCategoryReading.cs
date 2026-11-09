// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using MailFathom.AI.SpamClassification;
using MailFathom.Application.Categories;
using MailFathom.Domain.Categories;

namespace MailFathom.AI.EmailCategorization;

/// <summary>Reads a decision model's answers into the categories a message holds, or refuses them as unreadable.</summary>
/// <remarks>
/// Whole or absent. The default choice is required, because a message holds exactly one and an answer without it says
/// nothing about what the mail is. An own question that went unanswered is simply not held. The default is the most
/// probable option; a tie goes to the one offered first.
/// </remarks>
internal static class EmailCategoryReading
{
    /// <summary>Reads one response.</summary>
    /// <param name="response">What the model answered.</param>
    /// <param name="ownCategories">The own categories it was asked about.</param>
    /// <param name="ownQuestions">The questions asked about them, in the same order.</param>
    /// <param name="requestedModel">The model the request asked for, named where the response does not name a usable build.</param>
    /// <returns>What the message holds, or <see langword="null" /> where the answer cannot be read.</returns>
    internal static IReadOnlyList<EmailCategoryAssignment>? Read(
        DecisionResponse response,
        IReadOnlyList<OwnMailCategory> ownCategories,
        IReadOnlyList<DecisionQuestion> ownQuestions,
        string requestedModel)
    {
        ArgumentNullException.ThrowIfNull(response);

        var modelBuild = SpamClassificationReading.UsableBuild(response.ModelId)
            ?? SpamClassificationReading.UsableBuild(requestedModel)
            ?? "unnamed";

        if (Find(response, EmailCategoryQuestions.DefaultQuestionId) is not { IsAnswered: true } defaultAnswer
            || MostProbableDefault(defaultAnswer) is not { } chosen)
        {
            return null;
        }

        var held = ownCategories.Zip(ownQuestions)
            .Select(own => (Category: own.First, Question: own.Second, Answer: Find(response, own.Second.Id)))
            .Where(static own => own.Answer is { IsAnswered: true })
            .Select(static own => (own.Category, own.Question, Yes: own.Answer!.Probabilities.GetValueOrDefault(DecisionQuestion.Yes)))
            .Where(static own => own.Yes >= EmailCategoryQuestions.OwnCategoryThreshold)
            .Select(own => new EmailCategoryAssignment(
                EmailCategoryKind.Own,
                own.Category.Name,
                own.Yes,
                modelBuild,
                DecisionQuestionSet.RevisionOf([own.Question])));

        return
        [
            new EmailCategoryAssignment(
                EmailCategoryKind.Default,
                chosen.Option,
                chosen.Probability,
                modelBuild,
                EmailCategoryQuestions.DefaultRevision),
            .. held,
        ];
    }

    private static DecisionAnswer? Find(DecisionResponse response, string questionId) =>
        response.Answers.FirstOrDefault(answer => string.Equals(answer.QuestionId, questionId, StringComparison.Ordinal));

    private static (string Option, double Probability)? MostProbableDefault(DecisionAnswer answer) =>
        EmailCategoryQuestions.DefaultOptionValues
            .Select((option, position) => (Option: option, Position: position, Probability: answer.Probabilities.GetValueOrDefault(option, -1)))
            .Where(static candidate => candidate.Probability >= 0)
            .OrderByDescending(static candidate => candidate.Probability)
            .ThenBy(static candidate => candidate.Position)
            .Select(static candidate => ((string Option, double Probability)?)(candidate.Option, candidate.Probability))
            .FirstOrDefault();
}
