// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MailFathom.AI.Orchestration;

/// <summary>Finds the JSON object an agent was asked to answer with inside whatever it wrote around it, and reads it.</summary>
/// <remarks>
/// <para>
/// A model told to answer with one object still fences it, prefaces it, or writes a sentence after it often enough that
/// treating any of those as a failed derivation would throw away a usable answer. The outermost braces are what is
/// read; anything either side of them is discarded unexamined.
/// </para>
/// <para>
/// Every agent answering in one document is read through <see cref="Read{TDocument}" />, so every one of them follows
/// one policy about a member it was not asked for. A member no level of the document declares is ignored, because
/// refusing a whole answer over a field a model invented loses everything else it wrote. A member the document declares
/// only inside one nested object, written instead on the object that holds it — <c>keepTopic</c> beside <c>title</c>
/// rather than inside it — is read where it belongs, provided the answer wrote that nested object and gave it no value
/// of its own for the member. Ignoring it would drop a field whose absence changes what the document means. A member
/// declared inside several nested objects, or inside the items of an array, has no single place it belongs, so it is
/// ignored like an unknown one rather than guessed at.
/// </para>
/// <para>
/// A member written as <c>null</c> is read as one left out, which is what lets a strict schema, where every member is
/// required, stand for the same answers as an instruction that says to omit a member that does not apply.
/// </para>
/// </remarks>
internal static class AgentJsonAnswer
{
    private const string JsonFence = "```";

    /// <summary>Takes the fence and the prose off an answer, leaving the object or nothing.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <returns>The JSON object, or <see langword="null" /> where the answer holds none.</returns>
    internal static string? Unfenced(string? answerText)
    {
        if (string.IsNullOrWhiteSpace(answerText))
        {
            return null;
        }

        var text = answerText.Replace(JsonFence, string.Empty, StringComparison.Ordinal);
        var opening = text.IndexOf('{', StringComparison.Ordinal);
        var closing = text.LastIndexOf('}');

        return opening >= 0 && closing > opening ? text[opening..(closing + 1)] : null;
    }

    /// <summary>Reads the object out of an answer, placing a member written one level too high where it belongs.</summary>
    /// <typeparam name="TDocument">The shape the agent answers in.</typeparam>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="shape">The shape's serialization metadata, from the agent's own source-generated context.</param>
    /// <returns>The document, or <see langword="null" /> where the answer holds no object that reads as the shape.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="shape" /> is <see langword="null" />.</exception>
    internal static TDocument? Read<TDocument>(string? answerText, JsonTypeInfo<TDocument> shape)
        where TDocument : class
    {
        ArgumentNullException.ThrowIfNull(shape);

        if (Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(Rehomed(json, shape) ?? json, shape);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Rewrites the object with every misplaced member moved into the nested object it belongs to.</summary>
    /// <returns>The rewritten object, or <see langword="null" /> where nothing was misplaced and the answer reads as written.</returns>
    private static string? Rehomed(string json, JsonTypeInfo shape)
    {
        var homes = NestedHomesOf(shape);

        if (homes.Count is 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = shape.Options.AllowTrailingCommas,
            CommentHandling = shape.Options.ReadCommentHandling,
        });

        if (document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        var members = document.RootElement.EnumerateObject().ToArray();

        // The last occurrence of a name is the one the serializer reads, so it is the one that is moved or written into.
        var guests = members
            .Select((member, position) => (Member: member, Position: position))
            .Where(candidate => candidate.Member.Value.ValueKind is not JsonValueKind.Null
                && homes.TryGetValue(candidate.Member.Name, out var home)
                && LastObjectNamed(members, home) is { } nested
                && !WritesMember(members[nested].Value, candidate.Member.Name))
            .GroupBy(candidate => candidate.Member.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static named => named.Last())
            .ToArray();

        if (guests.Length is 0)
        {
            return null;
        }

        var hosts = guests
            .GroupBy(guest => LastObjectNamed(members, homes[guest.Member.Name])!.Value)
            .ToDictionary(static host => host.Key, static host => host.Select(static guest => guest.Member).ToArray());
        var moved = members
            .Where(member => guests.Any(guest => guest.Member.Name.Equals(member.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(static member => member.Name)
            .ToHashSet(StringComparer.Ordinal);

        return Written(members, moved, hosts);
    }

    /// <summary>Writes the root object again, leaving the moved members out of it and adding each to its host.</summary>
    private static string Written(
        JsonProperty[] members,
        HashSet<string> moved,
        Dictionary<int, JsonProperty[]> hosts)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            for (var position = 0; position < members.Length; position++)
            {
                var member = members[position];

                if (hosts.TryGetValue(position, out var arriving))
                {
                    WriteHost(writer, member, arriving);
                }
                else if (!moved.Contains(member.Name))
                {
                    member.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Writes a nested object with the members arriving from above, replacing any it wrote only as <c>null</c>.</summary>
    private static void WriteHost(Utf8JsonWriter writer, JsonProperty host, JsonProperty[] arriving)
    {
        writer.WritePropertyName(host.Name);
        writer.WriteStartObject();

        foreach (var own in host.Value.EnumerateObject()
            .Where(own => !arriving.Any(guest => guest.Name.Equals(own.Name, StringComparison.OrdinalIgnoreCase))))
        {
            own.WriteTo(writer);
        }

        foreach (var guest in arriving)
        {
            guest.WriteTo(writer);
        }

        writer.WriteEndObject();
    }

    /// <summary>Where the last member of a name holding an object sits, or nothing where the answer wrote no such object.</summary>
    private static int? LastObjectNamed(JsonProperty[] members, string name)
    {
        var position = Array.FindLastIndex(members, member => member.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        return position >= 0 && members[position].Value.ValueKind is JsonValueKind.Object ? position : null;
    }

    /// <summary>Whether a nested object gives a member a value of its own, a <c>null</c> counting as no value.</summary>
    private static bool WritesMember(JsonElement nested, string name) =>
        nested.EnumerateObject().Any(own => own.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && own.Value.ValueKind is not JsonValueKind.Null);

    /// <summary>Maps each member the root does not declare, and exactly one of its nested objects does, to that object's name.</summary>
    /// <remarks>Read off the shape's own metadata, so a document gains or loses a home by its declaration alone.</remarks>
    private static Dictionary<string, string> NestedHomesOf(JsonTypeInfo shape)
    {
        var declared = shape.Properties.Select(static property => property.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return shape.Properties
            .Select(property => (Home: property.Name, Shape: shape.Options.GetTypeInfo(property.PropertyType)))
            .Where(static nested => nested.Shape.Kind is JsonTypeInfoKind.Object)
            .SelectMany(static nested => nested.Shape.Properties.Select(member => (Member: member.Name, nested.Home)))
            .Where(nested => !declared.Contains(nested.Member))
            .GroupBy(static nested => nested.Member, StringComparer.OrdinalIgnoreCase)
            .Where(static named => named.Count() == 1)
            .ToDictionary(static named => named.Key, static named => named.Single().Home, StringComparer.OrdinalIgnoreCase);
    }
}
