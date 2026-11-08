// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Orchestration;

/// <summary>Builds the strict JSON schemas the agents answering in one document ask a provider for.</summary>
/// <remarks>
/// <para>
/// Every builder here writes only what strict mode accepts: an object lists every property as required and admits no
/// other, a member that does not always apply is nullable rather than optional, and no keyword strict mode drops —
/// <c>maxLength</c>, <c>maxItems</c>, a numeric range, a pattern — is written at all. The bound each of those would have
/// stated is the reading's to enforce, as it is for a model asked without a schema.
/// </para>
/// <para>
/// A number the turn fixes, such as the messages or passages it published, is an <c>enum</c> of exactly those numbers,
/// so a number outside the turn is an answer a provider honouring the schema cannot produce. A provider caps the values
/// one schema may enumerate, so a turn publishing more than <see cref="MaximumEnumeratedNumbers" /> falls back to a plain
/// integer that the reading bounds, as it does one publishing none, which no validator accepts as an empty <c>enum</c>.
/// </para>
/// </remarks>
internal static class StrictJsonSchema
{
    /// <summary>The most numbers one member enumerates before it falls back to a plain integer.</summary>
    /// <remarks>
    /// A quarter of the thousand enumerated values a provider admits across one schema, so a schema of three per-turn
    /// enumerations beside its fixed vocabularies stays inside that limit however long the turn.
    /// </remarks>
    internal const int MaximumEnumeratedNumbers = 250;

    /// <summary>Builds the response format carrying one schema.</summary>
    /// <param name="schema">The root object schema.</param>
    /// <param name="name">The name the schema is sent under.</param>
    /// <param name="description">What the document is, as the provider shows it to the model.</param>
    /// <returns>The response format.</returns>
    internal static ChatResponseFormatJson ResponseFormat(JsonObject schema, string name, string description)
    {
        ArgumentNullException.ThrowIfNull(schema);

        using var document = JsonDocument.Parse(schema.ToJsonString());

        return ChatResponseFormat.ForJsonSchema(document.RootElement.Clone(), name, description);
    }

    /// <summary>An object requiring every one of its properties and admitting no other, as strict mode asks.</summary>
    /// <param name="properties">The object's properties, each with its schema.</param>
    /// <returns>The object schema.</returns>
    internal static JsonObject ObjectOf(JsonObject properties) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray([.. properties.Select(static property => JsonValue.Create(property.Key))]),
        ["additionalProperties"] = false,
    };

    /// <summary>An array of one kind of item.</summary>
    /// <param name="items">The schema every item follows.</param>
    /// <returns>The array schema.</returns>
    internal static JsonObject ArrayOf(JsonNode items) => new() { ["type"] = "array", ["items"] = items };

    /// <summary>A value following any one of several schemas.</summary>
    /// <param name="alternatives">The schemas admitted.</param>
    /// <returns>The schema admitting each alternative.</returns>
    internal static JsonObject AnyOf(params JsonNode[] alternatives) => new() { ["anyOf"] = new JsonArray(alternatives) };

    /// <summary>A value following a schema, or <c>null</c> where the member does not apply.</summary>
    /// <param name="schema">The schema a written value follows.</param>
    /// <returns>The nullable schema.</returns>
    internal static JsonObject Nullable(JsonNode schema) => AnyOf(schema, new JsonObject { ["type"] = "null" });

    /// <summary>An integer that is one of the numbers a turn published.</summary>
    /// <param name="numbers">The numbers the turn published.</param>
    /// <returns>
    /// An <c>enum</c> of the numbers, or a plain integer where the turn published none or more than
    /// <see cref="MaximumEnumeratedNumbers" />.
    /// </returns>
    internal static JsonObject NumberOf(IReadOnlyCollection<int> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        return numbers.Count is 0 or > MaximumEnumeratedNumbers
            ? IntegerType()
            : new JsonObject
            {
                ["type"] = "integer",
                ["enum"] = new JsonArray([.. numbers.Select(static number => JsonValue.Create(number))]),
            };
    }

    /// <summary>The numbers from zero up to, but not including, a count, which is how a turn numbers what it lists.</summary>
    /// <param name="count">How many items the turn listed.</param>
    /// <returns>An integer that is one of the turn's positions.</returns>
    internal static JsonObject PositionOf(int count) => NumberOf([.. Enumerable.Range(0, Math.Max(count, 0))]);

    /// <summary>A string that is one of a fixed vocabulary.</summary>
    /// <param name="values">The words admitted.</param>
    /// <returns>The string <c>enum</c>.</returns>
    internal static JsonObject Enumeration(IEnumerable<string> values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray([.. values.Select(static value => JsonValue.Create(value))]),
    };

    /// <summary>A reference to a definition under the root's <c>$defs</c>.</summary>
    /// <param name="definition">The definition's name.</param>
    /// <returns>The reference.</returns>
    internal static JsonObject Reference(string definition) => new() { ["$ref"] = $"#/$defs/{definition}" };

    /// <summary>A string.</summary>
    /// <returns>The string schema.</returns>
    internal static JsonObject StringType() => new() { ["type"] = "string" };

    /// <summary>A boolean.</summary>
    /// <returns>The boolean schema.</returns>
    internal static JsonObject BooleanType() => new() { ["type"] = "boolean" };

    /// <summary>An integer.</summary>
    /// <returns>The integer schema.</returns>
    internal static JsonObject IntegerType() => new() { ["type"] = "integer" };
}
