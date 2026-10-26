// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.AI.DecisionModels;
using MailFathom.AI.Decisions;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Reads an OpenRouter Decisions answer into one answer per question, or refuses it whole.</summary>
/// <remarks>
/// <para>
/// Strict by design, and for the reason <see cref="OpenRouterDecisionClient" /> gives: a probability is read only where
/// the provider stated it, for exactly the options the question offered, as a finite number in [0, 1]. A missing
/// <c>probabilities</c> member, an option left out, an option never offered, an answer of another type, and a value out
/// of range each make the response unreadable. None is read with a default, because a missing option read as zero is a
/// verdict nobody gave.
/// </para>
/// <para>
/// A question the response carries no key for is unanswered rather than unreadable. That is the one gap the shape names
/// on purpose: a provider may decline a question, and an unanswered question is something a caller can act on, while a
/// distribution with a hole in it is not.
/// </para>
/// <para>
/// The provider's <c>usage.cost</c> is not read. It is a figure in dollars, and what a run reports is the tokens the
/// provider counted and never money; it survives only in the raw representation.
/// </para>
/// </remarks>
internal static class OpenRouterDecisionReading
{
    private const string YesNoType = "noul";
    private const string ChoiceType = "choice";
    private const string ScaleType = "score";

    /// <summary>Reads a response body against the questions it answers.</summary>
    /// <param name="body">The body of a successful response.</param>
    /// <param name="questions">The questions the request asked.</param>
    /// <param name="modelAlias">The deployment's name for the model, which an unreadable answer is reported under.</param>
    /// <returns>The answers, in the order the questions were asked.</returns>
    /// <exception cref="DecisionFailedException">Thrown when the body is not one readable answer per question it carries.</exception>
    public static DecisionResponse Read(string body, IReadOnlyList<DecisionQuestion> questions, string modelAlias)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(questions);

        // Read ahead of the answers, so an answer refused below still carries what the provider billed for it: the model
        // ran, and a period that missed those tokens would admit more than its ceiling allows.
        UsageDetails? usage = null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            usage = ReadUsage(root.GetProperty("usage"));

            return ReadDocument(root, questions, usage);
        }
        catch (Exception unreadable) when (unreadable
            is JsonException
            or InvalidOperationException
            or FormatException
            or KeyNotFoundException
            or ArgumentException
            or OverflowException)
        {
            // A required member absent, a member of the wrong kind, and a key stated twice are all the same finding: the
            // body is not the contract's shape.
            throw new DecisionFailedException(modelAlias, DecisionFailure.AnswerUnreadable, unreadable) { Usage = usage };
        }
    }

    private static DecisionResponse ReadDocument(JsonElement root, IReadOnlyList<DecisionQuestion> questions, UsageDetails usage)
    {
        var answers = root.GetProperty("answers");

        if (answers.ValueKind is not JsonValueKind.Object)
        {
            throw NotTheContract();
        }

        DecisionAnswer[] read =
        [
            .. questions.Select(question => answers.TryGetProperty(question.Id, out var answer)
                ? ReadAnswer(question, answer)
                : DecisionAnswer.Unanswered(question.Id)),
        ];

        return new DecisionResponse(read)
        {
            ResponseId = OptionalString(root, "id"),
            ModelId = root.GetProperty("model").GetString(),
            Usage = usage,
            RawRepresentation = root.Clone(),
        };
    }

    private static DecisionAnswer ReadAnswer(DecisionQuestion question, JsonElement answer)
    {
        var expectedType = question.Kind switch
        {
            DecisionQuestionKind.YesNo => YesNoType,
            DecisionQuestionKind.Choice => ChoiceType,
            DecisionQuestionKind.Scale => ScaleType,
            _ => throw NotTheContract(),
        };

        if (answer.ValueKind is not JsonValueKind.Object
            || !string.Equals(OptionalString(answer, "type"), expectedType, StringComparison.Ordinal))
        {
            throw NotTheContract();
        }

        return question.Kind switch
        {
            DecisionQuestionKind.YesNo => ReadYesNo(question, answer),
            DecisionQuestionKind.Choice => ReadChoice(question, answer),
            _ => ReadScale(question, answer),
        };
    }

    private static DecisionAnswer ReadYesNo(DecisionQuestion question, JsonElement answer)
    {
        var yes = Probability(answer.GetProperty(YesNoType));

        // The contract states the probability of yes alone, so the chosen value is the more probable of the two and an
        // exact half reads as yes, which is the only side that was stated.
        return new DecisionAnswer(
            question.Id,
            yes >= 0.5 ? DecisionQuestion.Yes : DecisionQuestion.No,
            new Dictionary<string, double>
            {
                [DecisionQuestion.Yes] = yes,
                [DecisionQuestion.No] = 1 - yes,
            });
    }

    private static DecisionAnswer ReadChoice(DecisionQuestion question, JsonElement answer)
    {
        var probabilities = Distribution(answer, [.. question.Options.Select(option => option.Value)]);
        var chosen = answer.GetProperty(ChoiceType).GetString();

        if (chosen is null || !probabilities.ContainsKey(chosen))
        {
            throw NotTheContract();
        }

        return new DecisionAnswer(question.Id, chosen, probabilities);
    }

    /// <summary>Reads a scale answer, whose distribution the contract keys by each level's position.</summary>
    /// <remarks>
    /// The chosen value is the most probable level, the first of several that tie. The contract's own <c>score</c> is an
    /// expected position rather than a level, so it is not what an answer names.
    /// </remarks>
    private static DecisionAnswer ReadScale(DecisionQuestion question, JsonElement answer)
    {
        var positions = Enumerable.Range(0, question.Options.Count)
            .Select(position => position.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        var byPosition = Distribution(answer, positions);

        var probabilities = question.Options
            .Select((option, position) => KeyValuePair.Create(option.Value, byPosition[positions[position]]))
            .ToDictionary(StringComparer.Ordinal);
        var chosen = question.Options.MaxBy(option => probabilities[option.Value])!.Value;

        return new DecisionAnswer(question.Id, chosen, probabilities);
    }

    /// <summary>Reads a distribution that must name exactly the given keys, each a probability.</summary>
    private static Dictionary<string, double> Distribution(JsonElement answer, string[] offered)
    {
        if (!answer.TryGetProperty("probabilities", out var stated) || stated.ValueKind is not JsonValueKind.Object)
        {
            throw NotTheContract();
        }

        var probabilities = stated.EnumerateObject()
            .ToDictionary(member => member.Name, member => Probability(member.Value), StringComparer.Ordinal);

        if (probabilities.Count != offered.Length || !offered.All(probabilities.ContainsKey))
        {
            throw NotTheContract();
        }

        return probabilities;
    }

    private static double Probability(JsonElement value) =>
        value.ValueKind is JsonValueKind.Number
        && value.GetDouble() is var probability
        && double.IsFinite(probability)
        && probability is >= 0 and <= 1
            ? probability
            : throw NotTheContract();

    /// <summary>Reads what the provider billed, refusing a count no provider could have meant.</summary>
    /// <remarks>
    /// A negative count would be subtracted from the run's tally and the period's row, letting later calls past their
    /// ceilings, and would run a monotonic counter backwards; a sum past the range would do the same by wrapping.
    /// </remarks>
    private static UsageDetails ReadUsage(JsonElement usage)
    {
        var input = usage.GetProperty("input_tokens").GetInt64();
        var output = usage.GetProperty("output_tokens").GetInt64();

        if (input < 0 || output < 0)
        {
            throw NotTheContract();
        }

        return new UsageDetails
        {
            InputTokenCount = input,
            OutputTokenCount = output,
            TotalTokenCount = checked(input + output),
        };
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    /// <summary>The finding that the body is not the contract's shape, which the read turns into an unreadable answer.</summary>
    private static FormatException NotTheContract() => new("The decision answer is not in the contract's shape.");
}
