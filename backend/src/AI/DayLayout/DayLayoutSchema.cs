// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.DayLayout;

/// <summary>The strict JSON schema one arrangement's answer is asked to be a document of, built for the day it arranges.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="DayLayoutDocument" />: the placements, each naming a task by the position the turn gave it, an
/// instant to begin, and a length, and the positions of the tasks that do not fit. Every position is an <c>enum</c> of the
/// day's own positions, so a placement naming a task the day does not hold is an answer a provider honouring the schema
/// cannot produce.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so no bound is stated here.
/// <see cref="DayLayoutReading" /> brings a length outside the bounds a placement is offered with inside them and keeps
/// at most one placement per task, because the hour a task was placed at survives both. It drops a placement whose
/// start is not an instant inside the day's window, because there is no honest way to guess which hour was meant.
/// </para>
/// </remarks>
internal static class DayLayoutSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "day_layout";

    private const string Description = "An arrangement of one person's day: when to do each task, and which tasks do not fit.";

    /// <summary>Builds the response format for one day.</summary>
    /// <param name="taskCount">How many tasks the day holds, which a placement names by position.</param>
    /// <returns>The strict schema's response format.</returns>
    internal static ChatResponseFormatJson For(int taskCount)
    {
        var schema = StrictJsonSchema.ObjectOf(new JsonObject
        {
            ["placements"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["task"] = StrictJsonSchema.PositionOf(taskCount),
                ["startAt"] = StrictJsonSchema.StringType(),
                ["minutes"] = StrictJsonSchema.IntegerType(),
            })),
            ["notToday"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.PositionOf(taskCount)),
        });

        return StrictJsonSchema.ResponseFormat(schema, Name, Description);
    }
}
