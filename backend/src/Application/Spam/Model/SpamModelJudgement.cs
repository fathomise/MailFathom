// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Spam;

namespace MailFathom.Application.Spam.Model;

/// <summary>What one consultation of the model came back with: an answer, or the reason there is none.</summary>
public sealed record SpamModelJudgement
{
    private SpamModelJudgement(SpamModelOutcome outcome, SpamModelAnswer? answer)
    {
        this.Outcome = outcome;
        this.Answer = answer;
    }

    /// <summary>Gets how the consultation ended.</summary>
    public SpamModelOutcome Outcome { get; }

    /// <summary>Gets the answer, present exactly when <see cref="Outcome" /> is <see cref="SpamModelOutcome.Answered" />.</summary>
    public SpamModelAnswer? Answer { get; }

    /// <summary>Records an answer the model gave.</summary>
    /// <param name="answer">The answer, read into the domain's values.</param>
    /// <returns>The judgement.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answer" /> is <see langword="null" />.</exception>
    public static SpamModelJudgement Answered(SpamModelAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new SpamModelJudgement(SpamModelOutcome.Answered, answer);
    }

    /// <summary>Records that the model gave no answer, and why.</summary>
    /// <param name="outcome">The reason.</param>
    /// <returns>The judgement.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> is <see cref="SpamModelOutcome.Answered" /> or not a defined member.</exception>
    public static SpamModelJudgement Unanswered(SpamModelOutcome outcome) =>
        outcome is SpamModelOutcome.Answered || !Enum.IsDefined(outcome)
            ? throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A judgement without an answer names why there is none.")
            : new SpamModelJudgement(outcome, answer: null);
}

/// <summary>A model's answer about one message, read into the domain's closed values.</summary>
/// <param name="SpamProbability">The probability the model gave that the message is spam at all, or <see langword="null" /> for the job that only names a subcategory.</param>
/// <param name="Subcategory">The most probable kind of spam among the six.</param>
/// <param name="PhishingRequest">What a phishing message asks for, or <see langword="null" /> where the model left the question unanswered.</param>
/// <param name="ImpersonatedParty">Who a phishing or business-email-compromise message pretends to come from, or <see langword="null" /> where the model left the question unanswered.</param>
/// <param name="ModelBuild">The model build that answered, as the provider named it.</param>
/// <param name="InstructionRevision">The revision of the questions the model was asked, so a threshold can be checked against the call it was calibrated for.</param>
/// <param name="Choices">The option the model chose for each question it answered, in the order they were asked.</param>
/// <remarks>
/// The subcategory and both details are what the model said, not yet what a record may hold: whether any of them is
/// recorded depends on the verdict and the subcategory beside it, and a value the record's invariants forbid is dropped
/// by whoever composes the record rather than repaired here.
/// </remarks>
public sealed record SpamModelAnswer(
    double? SpamProbability,
    SpamSubcategory Subcategory,
    SpamPhishingRequest? PhishingRequest,
    SpamImpersonatedParty? ImpersonatedParty,
    string ModelBuild,
    string InstructionRevision,
    IReadOnlyList<SpamModelChoice> Choices);

/// <summary>The option a model chose for one question.</summary>
/// <param name="Question">The question's identifier, which is what a model judgement signal is named by.</param>
/// <param name="Option">The option the model chose, as the question named it.</param>
public sealed record SpamModelChoice(string Question, string Option);
