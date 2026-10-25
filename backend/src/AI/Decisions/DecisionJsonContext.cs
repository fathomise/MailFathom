// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;

namespace MailFathom.AI.Decisions;

/// <summary>Serializes the decision types without reflection, as everything serialized in this repository is.</summary>
/// <remarks>
/// Combined with Microsoft.Extensions.AI's own resolver in <see cref="SerializerOptions" />, because a decision carries that
/// library's types — the input's <see cref="ChatMessage" />, <see cref="UsageDetails" />, and the values of an
/// <see cref="AdditionalPropertiesDictionary" /> — and that resolver is what describes them.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DecisionResponse))]
[JsonSerializable(typeof(IReadOnlyList<DecisionAnswer>))]
[JsonSerializable(typeof(IReadOnlyList<DecisionQuestion>))]
[JsonSerializable(typeof(DecisionOptions))]
internal sealed partial class DecisionJsonContext : JsonSerializerContext
{
    /// <summary>Gets the options every decision type is written and read with.</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = CreateOptions();

    /// <summary>Describes a type through <see cref="SerializerOptions" />, so a value typed as <see cref="object" /> inside it resolves through both resolvers.</summary>
    /// <typeparam name="T">The type to describe.</typeparam>
    /// <returns>The description.</returns>
    public static JsonTypeInfo<T> TypeInfoFor<T>() => (JsonTypeInfo<T>)SerializerOptions.GetTypeInfo(typeof(T));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(AIJsonUtilities.DefaultOptions)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(Default, AIJsonUtilities.DefaultOptions.TypeInfoResolver),
        };

        options.MakeReadOnly();

        return options;
    }
}
