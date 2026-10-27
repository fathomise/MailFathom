// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Spam;

namespace MailFathom.AI.SpamClassification;

/// <summary>Reads a decision model's answers into the spam model stage's answer, or refuses them as unreadable.</summary>
/// <remarks>
/// <para>
/// The adapter has already refused a distribution that does not name exactly the options offered, each a probability in
/// [0, 1], so what is left to read here is meaning. The question deciding the kind is required: unanswered, there is
/// nothing to read, and the whole answer is refused rather than taken as ordinary mail. The two detail questions are
/// not, and an unanswered one is recorded as nothing named.
/// </para>
/// <para>
/// The spam probability is one minus the probability of <c>not_spam</c>, and the kind is the most probable of the six
/// whatever option won overall, so a message the model thinks is narrowly ordinary mail still has the kind its
/// threshold would name if an operator set it lower. A tie between kinds goes to the one offered first.
/// </para>
/// </remarks>
internal static class SpamClassificationReading
{
    /// <summary>The longest model build the answer carries, leaving room in a signal's origin for the revision beside it.</summary>
    private const int MaximumModelBuildLength = 100;

    /// <summary>Reads one response.</summary>
    /// <param name="response">What the model answered.</param>
    /// <param name="job">The job its questions were asked for.</param>
    /// <param name="requestedModel">The model the request asked for, named where the response does not name a usable build.</param>
    /// <returns>The answer, or <see langword="null" /> where the answer cannot be read.</returns>
    internal static SpamModelAnswer? Read(DecisionResponse response, SpamModelJob job, string requestedModel)
    {
        ArgumentNullException.ThrowIfNull(response);

        var kindQuestion = job is SpamModelJob.DecideVerdict
            ? SpamClassificationQuestions.VerdictQuestionId
            : SpamClassificationQuestions.KindQuestionId;

        if (Find(response, kindQuestion) is not { IsAnswered: true } kindAnswer
            || MostProbableKind(kindAnswer) is not { } kind)
        {
            return null;
        }

        double? spamProbability = job is SpamModelJob.DecideVerdict
            ? kindAnswer.Probabilities.TryGetValue(SpamClassificationQuestions.NotSpamOption, out var notSpam)
                ? Math.Clamp(1 - notSpam, 0, 1)
                : null
            : null;

        if (job is SpamModelJob.DecideVerdict && spamProbability is null)
        {
            return null;
        }

        var request = Find(response, SpamClassificationQuestions.PhishingRequestQuestionId);
        var party = Find(response, SpamClassificationQuestions.ImpersonatedPartyQuestionId);

        return new SpamModelAnswer(
            spamProbability,
            kind,
            request?.Value is { } requested ? SpamClassificationQuestions.RequestOf(requested) : null,
            party?.Value is { } pretended ? SpamClassificationQuestions.PartyOf(pretended) : null,
            UsableBuild(response.ModelId) ?? UsableBuild(requestedModel) ?? "unnamed",
            SpamClassificationQuestions.RevisionFor(job),
            [
                .. response.Answers
                    .Where(static answer => answer.Value is not null)
                    .Select(static answer => new SpamModelChoice(answer.QuestionId, answer.Value!)),
            ]);
    }

    private static DecisionAnswer? Find(DecisionResponse response, string questionId) =>
        response.Answers.FirstOrDefault(answer => string.Equals(answer.QuestionId, questionId, StringComparison.Ordinal));

    private static SpamSubcategory? MostProbableKind(DecisionAnswer answer) => SpamClassificationQuestions.KindOptions
        .Select((option, position) => (Option: option, Position: position, Probability: answer.Probabilities.GetValueOrDefault(option, -1)))
        .Where(static candidate => candidate.Probability >= 0)
        .OrderByDescending(static candidate => candidate.Probability)
        .ThenBy(static candidate => candidate.Position)
        .Select(static candidate => SpamClassificationQuestions.KindOf(candidate.Option))
        .FirstOrDefault();

    /// <summary>Accepts a model build only in a form a signal's provenance can carry.</summary>
    /// <remarks>
    /// The build is the provider's text, so one carrying a control character, the separator a provenance origin splits
    /// on, or more than fits is not recorded as it came; the requested model is the next best name for the call.
    /// </remarks>
    private static string? UsableBuild(string? build) =>
        build is { Length: > 0 and <= MaximumModelBuildLength } named
        && !string.IsNullOrWhiteSpace(named)
        && !named.Contains('@', StringComparison.Ordinal)
        && !named.Any(char.IsControl)
            ? named.Trim()
            : null;
}
