// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>Everything a decision request came back with: one answer per question, and what the provider reported about the call.</summary>
/// <remarks>
/// The members beside <see cref="Answers" /> follow <see cref="ChatResponse" />, so what a middleware reads off a chat
/// response — the model that answered, the tokens counted, the provider's own object — it reads off a decision the same
/// way.
/// </remarks>
internal sealed class DecisionResponse
{
    /// <summary>Initializes a response over its answers.</summary>
    /// <param name="answers">One answer per question asked, in the order the questions were given.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answers" /> is <see langword="null" />.</exception>
    [JsonConstructor]
    public DecisionResponse(IReadOnlyList<DecisionAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        this.Answers = [.. answers];
    }

    /// <summary>Gets one answer per question asked, in the order the questions were given.</summary>
    public IReadOnlyList<DecisionAnswer> Answers { get; }

    /// <summary>Gets or sets the provider's identifier for this response.</summary>
    public string? ResponseId { get; set; }

    /// <summary>Gets or sets the exact model build that answered, as the provider named it.</summary>
    /// <remarks>Not the name the request asked for: an identifier that resolves to a dated build is reported here as that build, which is what a threshold calibrated against one build has to be checked against.</remarks>
    public string? ModelId { get; set; }

    /// <summary>Gets or sets the tokens the provider counted for the call.</summary>
    public UsageDetails? Usage { get; set; }

    /// <summary>Gets or sets the provider's own object this response was read from.</summary>
    /// <remarks>Never serialized, so a cached answer carries nothing the provider sent beyond what is read into the members above.</remarks>
    [JsonIgnore]
    public object? RawRepresentation { get; set; }

    /// <summary>Gets or sets values a provider or a middleware attached that no member above names.</summary>
    public AdditionalPropertiesDictionary? AdditionalProperties { get; set; }

    /// <summary>Finds the answer to one question.</summary>
    /// <param name="questionId">The identifier the question was asked under.</param>
    /// <returns>The answer, which may be one that went unanswered.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no question was asked under that identifier.</exception>
    public DecisionAnswer AnswerTo(string questionId) =>
        this.Answers.FirstOrDefault(answer => string.Equals(answer.QuestionId, questionId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("No question was asked under that identifier.");
}
