// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Written for strict mode: every object lists every property as required and admits no other, and a member that does
/// not always apply is nullable rather than optional. Strict mode drops keywords such as <c>maxItems</c> and
/// <c>maxLength</c>, so no bound is stated here at all — <see cref="ReplyDraftReading" /> enforces every one of them, as it
/// does for a model asked without the schema.
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

        var properties = new JsonObject { ["body"] = ArrayOf(AnyOf(StringType(), Reference("paragraph"), Reference("list"), Reference("table"))) };

        if (turn.MessageNumbers.Count > 0)
        {
            properties["claims"] = ArrayOf(ObjectOf(new JsonObject
            {
                ["text"] = StringType(),
                ["messages"] = ArrayOf(NumberOf(turn.MessageNumbers)),
            }));
        }

        if (turn.PersonNumbers.Count > 0)
        {
            properties["recipients"] = ArrayOf(NumberOf(turn.PersonNumbers));
        }

        var schema = ObjectOf(properties);

        schema["$defs"] = Definitions([.. turn.Links.Select(static link => link.Position)]);

        using var document = JsonDocument.Parse(schema.ToJsonString());

        return ChatResponseFormat.ForJsonSchema(document.RootElement.Clone(), Name, Description);
    }

    private static JsonObject Definitions(IReadOnlyList<int> linkNumbers)
    {
        var run = new JsonObject
        {
            ["text"] = StringType(),
            ["bold"] = BooleanType(),
            ["italic"] = BooleanType(),
            ["colour"] = Nullable(Enumeration(ReplyDraftBlockReading.SwatchNames)),
        };

        if (linkNumbers.Count > 0)
        {
            run["link"] = Nullable(NumberOf(linkNumbers));
        }

        return new JsonObject
        {
            ["run"] = AnyOf(StringType(), ObjectOf(run)),
            ["runs"] = ArrayOf(Reference("run")),
            ["alignment"] = Nullable(Enumeration(Alignments)),
            ["paragraph"] = ObjectOf(new JsonObject
            {
                ["type"] = Enumeration(["paragraph"]),
                ["runs"] = Reference("runs"),
                ["align"] = Reference("alignment"),
            }),
            ["list"] = ObjectOf(new JsonObject
            {
                ["type"] = Enumeration(["list"]),
                ["ordered"] = BooleanType(),
                ["items"] = ArrayOf(AnyOf(StringType(), Reference("runs"))),
            }),
            ["cell"] = AnyOf(
                StringType(),
                Reference("runs"),
                ObjectOf(new JsonObject { ["runs"] = Reference("runs"), ["align"] = Reference("alignment") })),
            ["table"] = ObjectOf(new JsonObject
            {
                ["type"] = Enumeration(["table"]),
                ["header"] = BooleanType(),
                ["rows"] = ArrayOf(ArrayOf(Reference("cell"))),
            }),
        };
    }

    /// <summary>An object requiring every one of its properties and admitting no other, as strict mode asks.</summary>
    private static JsonObject ObjectOf(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray([.. properties.Select(static property => JsonValue.Create(property.Key))]),
        ["additionalProperties"] = false,
    };

    private static JsonObject ArrayOf(JsonNode items) => new() { ["type"] = "array", ["items"] = items };

    private static JsonObject AnyOf(params JsonNode[] alternatives) => new() { ["anyOf"] = new JsonArray(alternatives) };

    private static JsonObject Nullable(JsonNode schema) => AnyOf(schema, new JsonObject { ["type"] = "null" });

    private static JsonObject NumberOf(IEnumerable<int> numbers) => new()
    {
        ["type"] = "integer",
        ["enum"] = new JsonArray([.. numbers.Select(static number => JsonValue.Create(number))]),
    };

    private static JsonObject Enumeration(IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray([.. values.Select(static value => JsonValue.Create(value))]),
    };

    private static JsonObject Reference(string definition) => new() { ["$ref"] = $"#/$defs/{definition}" };

    private static JsonObject StringType() => new() { ["type"] = "string" };

    private static JsonObject BooleanType() => new() { ["type"] = "boolean" };
}
