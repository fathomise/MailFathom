// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.AI.Decisions;

/// <summary>A closed question: what is asked, and every answer the model may give to it.</summary>
/// <remarks>
/// <para>
/// The options are part of the question rather than of the answer, which is the property a closed question is asked
/// for: a model cannot be talked into an answer outside them, so text inside the input can move a probability and can
/// never write a verdict.
/// </para>
/// <para>
/// A yes-or-no question carries its two options as <see cref="Yes" /> and <see cref="No" />, so every kind is answered
/// the same way — a value and a probability per option — and a caller reads all three kinds through one shape.
/// </para>
/// </remarks>
internal sealed class DecisionQuestion
{
    /// <summary>The value a yes-or-no question's affirmative option is named by.</summary>
    public const string Yes = "yes";

    /// <summary>The value a yes-or-no question's negative option is named by.</summary>
    public const string No = "no";

    private DecisionQuestion(
        string id,
        DecisionQuestionKind kind,
        string instructions,
        IReadOnlyList<DecisionQuestionOption> options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(instructions);
        ArgumentNullException.ThrowIfNull(options);

        RequireUsableOptions(options);

        this.Id = id;
        this.Kind = kind;
        this.Instructions = instructions;
        this.Options = [.. options];
    }

    /// <summary>Gets the identifier the caller assigned, which its answer carries back.</summary>
    public string Id { get; }

    /// <summary>Gets the shape of answer the question asks for.</summary>
    public DecisionQuestionKind Kind { get; }

    /// <summary>Gets what the model is asked.</summary>
    public string Instructions { get; }

    /// <summary>Gets every answer the model may give, in the order the caller wrote them; for a scale, lowest level first.</summary>
    public IReadOnlyList<DecisionQuestionOption> Options { get; }

    /// <summary>Asks a yes-or-no question.</summary>
    /// <param name="id">The identifier the answer carries back.</param>
    /// <param name="instructions">What the model is asked.</param>
    /// <param name="yesMeans">What answering yes means, or empty where the question says it alone.</param>
    /// <param name="noMeans">What answering no means, or empty where the question says it alone.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier or the instructions are blank.</exception>
    public static DecisionQuestion YesNo(string id, string instructions, string yesMeans = "", string noMeans = "") =>
        new(id, DecisionQuestionKind.YesNo, instructions, [new(Yes, yesMeans), new(No, noMeans)]);

    /// <summary>Asks the model to pick one of several named options.</summary>
    /// <param name="id">The identifier the answer carries back.</param>
    /// <param name="instructions">What the model is asked.</param>
    /// <param name="options">The options, at least two, each under a distinct value.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier or the instructions are blank, or the options are fewer than two, blank, or repeated.</exception>
    public static DecisionQuestion Choice(
        string id,
        string instructions,
        IReadOnlyList<DecisionQuestionOption> options) =>
        new(id, DecisionQuestionKind.Choice, instructions, options);

    /// <summary>Asks the model to place its answer on ordered levels.</summary>
    /// <param name="id">The identifier the answer carries back.</param>
    /// <param name="instructions">What the model is asked.</param>
    /// <param name="levels">The levels, at least two, lowest first, each under a distinct value.</param>
    /// <returns>The question.</returns>
    /// <exception cref="ArgumentException">Thrown when the identifier or the instructions are blank, or the levels are fewer than two, blank, or repeated.</exception>
    public static DecisionQuestion Scale(
        string id,
        string instructions,
        IReadOnlyList<DecisionQuestionOption> levels) =>
        new(id, DecisionQuestionKind.Scale, instructions, levels);

    /// <summary>Refuses a set of questions no request could carry.</summary>
    /// <param name="questions">The questions one request asks.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="questions" /> or one of them is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when there are no questions, or two share an identifier.</exception>
    /// <remarks>Identifiers are compared exactly, because an answer is matched to its question by the identifier as written.</remarks>
    public static void RequireUsable(IReadOnlyList<DecisionQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        if (questions.Count is 0)
        {
            throw new ArgumentException("A request asks at least one question.", nameof(questions));
        }

        if (questions.Any(question => question is null))
        {
            throw new ArgumentNullException(nameof(questions), "A request carries no null question.");
        }

        if (questions.Select(question => question.Id).Distinct(StringComparer.Ordinal).Count() != questions.Count)
        {
            throw new ArgumentException("Two questions in one request share an identifier.", nameof(questions));
        }
    }

    private static void RequireUsableOptions(IReadOnlyList<DecisionQuestionOption> options)
    {
        if (options.Count < 2)
        {
            throw new ArgumentException("A closed question offers at least two answers.", nameof(options));
        }

        if (options.Any(option => option is null || string.IsNullOrWhiteSpace(option.Value) || option.Description is null))
        {
            throw new ArgumentException("Every answer a question offers has a value and a description, which may be empty.", nameof(options));
        }

        if (options.Select(option => option.Value).Distinct(StringComparer.Ordinal).Count() != options.Count)
        {
            throw new ArgumentException("Two answers a question offers share a value.", nameof(options));
        }
    }
}
