// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>The answers models wrote to one agent in the last evaluation run, as they wrote them.</summary>
/// <remarks>
/// Each is the raw text an agent's reader was handed — fence, prose, and all — beside the case it answered, taken from
/// the evaluation store and kept only where it is the agent's own answer rather than a judge's. The corpus those cases
/// read is the evaluation suite's synthetic mail, so nothing in them is anybody's correspondence.
/// </remarks>
internal static class RecordedAnswers
{
    /// <summary>Reads every answer recorded for one agent.</summary>
    /// <param name="agent">The agent's name as the evaluation store files its cases, such as <c>ThreadState</c>.</param>
    /// <returns>The answers, each beside the case it answered.</returns>
    public static IReadOnlyList<RecordedAnswer> Of(string agent)
    {
        using var resource = typeof(RecordedAnswers).Assembly.GetManifestResourceStream($"RecordedAnswers.{agent}.json")
            ?? throw new InvalidOperationException($"No answers are recorded for '{agent}'.");

        return JsonSerializer.Deserialize<RecordedAnswer[]>(resource, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException($"The answers recorded for '{agent}' do not read.");
    }
}

/// <summary>One answer a model wrote to an evaluation case.</summary>
/// <param name="Scenario">The case it answered.</param>
/// <param name="Answer">What the model wrote.</param>
internal readonly record struct RecordedAnswer(string Scenario, string Answer)
{
    /// <inheritdoc />
    public override string ToString() => this.Scenario;
}
