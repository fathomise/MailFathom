// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Nodes;
using MailFathom.AI.Orchestration;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.ReplyDrafts;

/// <summary>The strict JSON schema one drafting's answer is asked to be a document of, built for the numbers its turn published.</summary>
/// <remarks>
/// <para>
/// It follows <see cref="ReplyDraftDocument" />: a body of blocks, each a string or a paragraph, list, or table; a run
/// that is a string or an object carrying its optional link; claims citing messages; and recipients naming people. Every
/// message, person, and link number is an <c>enum</c> of the numbers this turn published, so a number outside the
/// conversation, a string where a number belongs, and a block written as a string holding JSON are answers a provider
/// honouring the schema cannot produce at all. An array the turn published nothing for is left out rather than given an
/// empty <c>enum</c>, which no schema validator accepts; the reading treats an absent array as an empty one.
/// </para>
/// <para>
/// Written for strict mode through <see cref="StrictJsonSchema" />, so no bound is stated here at all —
/// <see cref="ReplyDraftReading" /> enforces every one of them, as it does for a model asked without the schema.
/// </para>
/// </remarks>
internal static class ReplyDraftSchema
{
    /// <summary>The name the schema is sent under.</summary>
    internal const string Name = "reply_draft";

    private const string Description = "A reply draft: its body, the claims it makes with the messages that state them, and the people proposed to receive it.";

    private static readonly string[] Alignments = ["start", "center", "end", "justify"];

    /// <summary>Builds the response format for one turn.</summary>
    /// <param name="turn">The turn as it was sent, carrying the numbers it published.</param>
    /// <returns>The strict schema's response format.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="turn" /> is <see langword="null" />.</exception>
    internal static ChatResponseFormatJson For(ReplyDraftTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var properties = new JsonObject
        {
            ["body"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.AnyOf(
                StrictJsonSchema.StringType(),
                StrictJsonSchema.Reference("paragraph"),
                StrictJsonSchema.Reference("list"),
                StrictJsonSchema.Reference("table"))),
        };

        if (turn.MessageNumbers.Count > 0)
        {
            properties["claims"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["text"] = StrictJsonSchema.StringType(),
                ["messages"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.NumberOf(turn.MessageNumbers)),
            }));
        }

        if (turn.PersonNumbers.Count > 0)
        {
            properties["recipients"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.NumberOf(turn.PersonNumbers));
        }

        var schema = StrictJsonSchema.ObjectOf(properties);

        schema["$defs"] = Definitions([.. turn.Links.Select(static link => link.Position)]);

        return StrictJsonSchema.ResponseFormat(schema, Name, Description);
    }

    private static JsonObject Definitions(IReadOnlyList<int> linkNumbers)
    {
        var run = new JsonObject
        {
            ["text"] = StrictJsonSchema.StringType(),
            ["bold"] = StrictJsonSchema.BooleanType(),
            ["italic"] = StrictJsonSchema.BooleanType(),
            ["colour"] = StrictJsonSchema.Nullable(StrictJsonSchema.Enumeration(ReplyDraftBlockReading.SwatchNames)),
        };

        if (linkNumbers.Count > 0)
        {
            run["link"] = StrictJsonSchema.Nullable(StrictJsonSchema.NumberOf(linkNumbers));
        }

        return new JsonObject
        {
            ["run"] = StrictJsonSchema.AnyOf(StrictJsonSchema.StringType(), StrictJsonSchema.ObjectOf(run)),
            ["runs"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.Reference("run")),
            ["alignment"] = StrictJsonSchema.Nullable(StrictJsonSchema.Enumeration(Alignments)),
            ["paragraph"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["type"] = StrictJsonSchema.Enumeration(["paragraph"]),
                ["runs"] = StrictJsonSchema.Reference("runs"),
                ["align"] = StrictJsonSchema.Reference("alignment"),
            }),
            ["list"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["type"] = StrictJsonSchema.Enumeration(["list"]),
                ["ordered"] = StrictJsonSchema.BooleanType(),
                ["items"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.AnyOf(StrictJsonSchema.StringType(), StrictJsonSchema.Reference("runs"))),
            }),
            ["cell"] = StrictJsonSchema.AnyOf(
                StrictJsonSchema.StringType(),
                StrictJsonSchema.Reference("runs"),
                StrictJsonSchema.ObjectOf(new JsonObject { ["runs"] = StrictJsonSchema.Reference("runs"), ["align"] = StrictJsonSchema.Reference("alignment") })),
            ["table"] = StrictJsonSchema.ObjectOf(new JsonObject
            {
                ["type"] = StrictJsonSchema.Enumeration(["table"]),
                ["header"] = StrictJsonSchema.BooleanType(),
                ["rows"] = StrictJsonSchema.ArrayOf(StrictJsonSchema.ArrayOf(StrictJsonSchema.Reference("cell"))),
            }),
        };
    }
}
