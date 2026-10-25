// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.AI.Decisions;

/// <summary>What a model answered to one question: the option it chose and the probability it gave every option.</summary>
/// <remarks>
/// <para>
/// An unanswered question is part of the shape rather than an error, because a provider can decline a question — one
/// contract by omitting it, another by refusing it — and a caller has to be able to tell that from an answer. It carries
/// no value and no probabilities, so nothing reading it can mistake it for an answer that gave every option zero.
/// </para>
/// <para>
/// The probabilities name exactly the options the question offered. An adapter that cannot establish that refuses the
/// whole response instead of filling the gap, so a missing option is never read as a probability of zero.
/// </para>
/// </remarks>
public sealed class DecisionAnswer
{
    /// <summary>Initializes an answer.</summary>
    /// <param name="questionId">The identifier of the question this answers.</param>
    /// <param name="value">The option chosen, or <see langword="null" /> where the question went unanswered.</param>
    /// <param name="probabilities">The probability given each option, keyed by its value; empty where the question went unanswered.</param>
    /// <exception cref="ArgumentException">Thrown when the identifier is blank, or an unanswered question carries probabilities.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="probabilities" /> is <see langword="null" />.</exception>
    [JsonConstructor]
    public DecisionAnswer(string questionId, string? value, IReadOnlyDictionary<string, double> probabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(questionId);
        ArgumentNullException.ThrowIfNull(probabilities);

        if (value is null && probabilities.Count > 0)
        {
            throw new ArgumentException("An unanswered question carries no probabilities.", nameof(probabilities));
        }

        this.QuestionId = questionId;
        this.Value = value;
        this.Probabilities = new Dictionary<string, double>(probabilities, StringComparer.Ordinal);
    }

    /// <summary>Gets the identifier of the question this answers.</summary>
    public string QuestionId { get; }

    /// <summary>Gets the option chosen, or <see langword="null" /> where the question went unanswered.</summary>
    public string? Value { get; }

    /// <summary>Gets the probability given each option, keyed by its value.</summary>
    public IReadOnlyDictionary<string, double> Probabilities { get; }

    /// <summary>Gets whether the model answered the question at all.</summary>
    [JsonIgnore]
    public bool IsAnswered => this.Value is not null;

    /// <summary>Records that a question went unanswered.</summary>
    /// <param name="questionId">The identifier of the question.</param>
    /// <returns>The unanswered answer.</returns>
    public static DecisionAnswer Unanswered(string questionId) => new(questionId, value: null, new Dictionary<string, double>());
}
