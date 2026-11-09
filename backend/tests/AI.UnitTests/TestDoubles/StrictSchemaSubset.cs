// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Frozen;
using System.Text.Json;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Names everything in a JSON schema that a provider's strict mode would refuse.</summary>
/// <remarks>
/// The subset is the one OpenAI's structured-output documentation states, which the providers reached through OpenRouter
/// follow: the root is an object rather than a choice, every object requires every property it declares and admits no
/// other, only the keywords below appear, an <c>enum</c> is never empty, nesting stays within ten levels, and the schema
/// stays within five thousand properties and a thousand enumerated values. A schema refused for any of these fails the
/// request outright rather than being honoured loosely, which is why every agent's schema is held to the whole list.
/// </remarks>
internal static class StrictSchemaSubset
{
    /// <summary>The deepest nesting of objects strict mode accepts.</summary>
    public const int MaximumNesting = 10;

    /// <summary>The most object properties one schema may declare.</summary>
    public const int MaximumProperties = 5000;

    /// <summary>The most values one schema may enumerate across every <c>enum</c>.</summary>
    public const int MaximumEnumeratedValues = 1000;

    private static readonly FrozenSet<string> Keywords = FrozenSet.ToFrozenSet(
    [
        "type", "properties", "required", "additionalProperties", "items", "anyOf", "enum", "const", "$defs", "$ref", "description",
    ]);

    /// <summary>Names every departure from the strict subset.</summary>
    /// <param name="schema">The schema as it is sent.</param>
    /// <returns>One line per departure, empty where the schema keeps to the subset.</returns>
    public static IReadOnlyList<string> ViolationsOf(JsonElement schema)
    {
        var violations = new List<string>();

        if (TypeOf(schema) != "object" || schema.TryGetProperty("anyOf", out _))
        {
            violations.Add("the root is not an object");
        }

        violations.AddRange(Departures(schema, "$"));

        var properties = SchemasIn(schema).Sum(static node => node.TryGetProperty("properties", out var declared) ? declared.EnumerateObject().Count() : 0);
        var enumerated = SchemasIn(schema).Sum(static node => node.TryGetProperty("enum", out var values) ? values.GetArrayLength() : 0);
        var nesting = NestingOf(schema, schema, []);

        if (properties > MaximumProperties)
        {
            violations.Add($"{properties} properties, past {MaximumProperties}");
        }

        if (enumerated > MaximumEnumeratedValues)
        {
            violations.Add($"{enumerated} enumerated values, past {MaximumEnumeratedValues}");
        }

        if (nesting > MaximumNesting)
        {
            violations.Add($"{nesting} levels of nesting, past {MaximumNesting}");
        }

        return violations;
    }

    /// <summary>Names what one schema node and everything beneath it does that strict mode refuses.</summary>
    private static IEnumerable<string> Departures(JsonElement node, string path)
    {
        foreach (var keyword in node.EnumerateObject().Where(static member => !Keywords.Contains(member.Name)))
        {
            yield return $"{path} uses '{keyword.Name}'";
        }

        if (node.TryGetProperty("enum", out var values) && values.GetArrayLength() is 0)
        {
            yield return $"{path} enumerates nothing";
        }

        if (TypeOf(node) == "object")
        {
            var declared = node.TryGetProperty("properties", out var properties)
                ? properties.EnumerateObject().Select(static property => property.Name).Order(StringComparer.Ordinal).ToArray()
                : [];
            var required = node.TryGetProperty("required", out var names)
                ? names.EnumerateArray().Select(static name => name.GetString()!).Order(StringComparer.Ordinal).ToArray()
                : [];

            if (!declared.SequenceEqual(required))
            {
                yield return $"{path} does not require exactly the properties it declares";
            }

            if (!node.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind is not JsonValueKind.False)
            {
                yield return $"{path} admits properties it does not declare";
            }
        }

        foreach (var (child, childPath) in ChildrenOf(node, path))
        {
            foreach (var departure in Departures(child, childPath))
            {
                yield return departure;
            }
        }
    }

    /// <summary>How many objects deep the schema reaches, following every reference to the definition it names.</summary>
    private static int NestingOf(JsonElement node, JsonElement root, HashSet<string> following)
    {
        if (node.TryGetProperty("$ref", out var reference))
        {
            var name = reference.GetString()!;

            // A definition reached again inside itself nests without end, which is past any limit.
            if (!following.Add(name))
            {
                return MaximumNesting + 1;
            }

            var depth = NestingOf(Resolve(root, name), root, following);

            following.Remove(name);

            return depth;
        }

        var own = TypeOf(node) == "object" ? 1 : 0;
        var deepest = ChildrenOf(node, string.Empty)
            .Where(static child => !child.Path.StartsWith(".$defs", StringComparison.Ordinal))
            .Select(child => NestingOf(child.Node, root, following))
            .DefaultIfEmpty(0)
            .Max();

        return own + deepest;
    }

    /// <summary>Every schema node in the document, the definitions included.</summary>
    private static IEnumerable<JsonElement> SchemasIn(JsonElement node) =>
        ChildrenOf(node, string.Empty).SelectMany(static child => SchemasIn(child.Node)).Prepend(node);

    /// <summary>The schemas a node holds directly: its properties, its items, its alternatives, and its definitions.</summary>
    internal static IEnumerable<(JsonElement Node, string Path)> ChildrenOf(JsonElement node, string path)
    {
        if (node.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                yield return (property.Value, $"{path}.{property.Name}");
            }
        }

        if (node.TryGetProperty("items", out var items))
        {
            yield return (items, $"{path}[]");
        }

        if (node.TryGetProperty("anyOf", out var alternatives))
        {
            foreach (var (alternative, position) in alternatives.EnumerateArray().Select(static (alternative, position) => (alternative, position)))
            {
                yield return (alternative, $"{path}|{position}");
            }
        }

        if (node.TryGetProperty("$defs", out var definitions))
        {
            foreach (var definition in definitions.EnumerateObject())
            {
                yield return (definition.Value, $"{path}.$defs.{definition.Name}");
            }
        }
    }

    /// <summary>The definition a reference names under the root's <c>$defs</c>.</summary>
    internal static JsonElement Resolve(JsonElement root, string reference) =>
        root.GetProperty("$defs").GetProperty(reference["#/$defs/".Length..]);

    /// <summary>The single type a node declares, or <see langword="null" /> where it declares none.</summary>
    internal static string? TypeOf(JsonElement node) =>
        node.TryGetProperty("type", out var type) && type.ValueKind is JsonValueKind.String ? type.GetString() : null;
}
