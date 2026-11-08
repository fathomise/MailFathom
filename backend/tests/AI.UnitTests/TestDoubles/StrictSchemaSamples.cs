// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Writes documents a strict JSON schema admits, spanning its shapes, for a reader to be held to.</summary>
/// <remarks>
/// <para>
/// A reader must refuse nothing its agent's schema admits, so a test hands it what the schema admits rather than what a
/// model happened to write. The documents span the schema's corners: one where every nullable member is <c>null</c>,
/// every array empty, every boolean false, and every <c>enum</c> at its first value; and, for each alternative an
/// <c>anyOf</c> offers, one where every nullable member is written, every array holds an item, every boolean is true,
/// and every <c>enum</c> is at its first value, and another with every <c>enum</c> at its last.
/// </para>
/// <para>
/// A string is written as a plain sentence unless the test names what a member holds, because what the schema cannot
/// say about a string — a date, a length, a pattern — is the reader's to bound and the test's to state.
/// </para>
/// </remarks>
internal static class StrictSchemaSamples
{
    /// <summary>The text a string is written as where the test names nothing else for it.</summary>
    public const string PlainText = "A plain sentence";

    /// <summary>Writes the documents a schema admits at its corners.</summary>
    /// <param name="schema">The schema as it is sent.</param>
    /// <param name="textFor">What a string member holds, by the member's name, or <see langword="null" /> for <see cref="PlainText" />.</param>
    /// <returns>The documents, distinct.</returns>
    public static IReadOnlyList<string> DocumentsAdmittedBy(JsonElement schema, Func<string?, string?>? textFor = null)
    {
        var widest = Math.Max(1, WidestChoiceIn(schema));
        Corner[] corners =
        [
            new(Written: false, Alternative: 0, LastValue: false),
            .. Enumerable.Range(0, widest).SelectMany(static alternative => new[]
            {
                new Corner(Written: true, alternative, LastValue: false),
                new Corner(Written: true, alternative, LastValue: true),
            }),
        ];

        return [.. corners.Select(corner => Sample(schema, schema, corner, member: null, textFor, depth: 0)?.ToJsonString() ?? "null").Distinct()];
    }

    private static JsonNode? Sample(
        JsonElement node,
        JsonElement root,
        Corner corner,
        string? member,
        Func<string?, string?>? textFor,
        int depth)
    {
        if (depth > StrictSchemaSubset.MaximumNesting * 3)
        {
            throw new InvalidOperationException("The schema nests past what strict mode admits.");
        }

        if (node.TryGetProperty("$ref", out var reference))
        {
            return Sample(StrictSchemaSubset.Resolve(root, reference.GetString()!), root, corner, member, textFor, depth + 1);
        }

        if (node.TryGetProperty("anyOf", out var alternatives))
        {
            var written = alternatives.EnumerateArray().Where(static alternative => StrictSchemaSubset.TypeOf(alternative) != "null").ToArray();
            var nullable = written.Length < alternatives.GetArrayLength();

            return !corner.Written && nullable
                ? null
                : Sample(written[corner.Alternative % written.Length], root, corner, member, textFor, depth + 1);
        }

        if (node.TryGetProperty("enum", out var values))
        {
            var chosen = corner.LastValue ? values[values.GetArrayLength() - 1] : values[0];

            return JsonNode.Parse(chosen.GetRawText());
        }

        return StrictSchemaSubset.TypeOf(node) switch
        {
            "object" => new JsonObject(node.GetProperty("properties").EnumerateObject()
                .Select(property => KeyValuePair.Create(property.Name, Sample(property.Value, root, corner, property.Name, textFor, depth + 1)))),
            "array" => corner.Written
                ? new JsonArray(Sample(node.GetProperty("items"), root, corner, member, textFor, depth + 1))
                : [],
            "string" => JsonValue.Create(textFor?.Invoke(member) ?? PlainText),
            "boolean" => JsonValue.Create(corner.Written),
            "integer" or "number" => JsonValue.Create(0),
            _ => null,
        };
    }

    /// <summary>The most non-null alternatives any one choice in the schema offers.</summary>
    private static int WidestChoiceIn(JsonElement node)
    {
        var own = node.TryGetProperty("anyOf", out var alternatives)
            ? alternatives.EnumerateArray().Count(static alternative => StrictSchemaSubset.TypeOf(alternative) != "null")
            : 0;

        return StrictSchemaSubset.ChildrenOf(node, string.Empty)
            .Select(static child => WidestChoiceIn(child.Node))
            .Append(own)
            .Max();
    }

    /// <summary>Which corner of the schema one document is written at.</summary>
    /// <param name="Written">Whether nullable members are written, arrays hold an item, and booleans are true.</param>
    /// <param name="Alternative">Which non-null alternative each choice takes, wrapping where a choice offers fewer.</param>
    /// <param name="LastValue">Whether each <c>enum</c> takes its last value rather than its first.</param>
    private sealed record Corner(bool Written, int Alternative, bool LastValue);
}
