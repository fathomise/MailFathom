// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Preferences;

/// <summary>Whether a person reads their mail list one message at a time or one conversation at a time.</summary>
/// <remarks>
/// <para>
/// A closed enumeration rather than a C# <see langword="enum" />, for the reason <see cref="ClientMessageView" /> is one:
/// the name travels both ways across the client endpoint and is stored as itself, so a member rename must not change
/// either and an ordinal would mean nothing to the client reading the response.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a choice. It reports itself through
/// <see cref="IsSpecified" />, refuses to answer for a name, and is rejected by the converter below, so nothing
/// undeclared reaches a response or the stored document.
/// </para>
/// </remarks>
[JsonConverter(typeof(ClientMailListModeJsonConverter))]
public readonly record struct ClientMailListMode
{
    private readonly string? name;

    private ClientMailListMode(string name) => this.name = name;

    /// <summary>Gets the choice to list every message on its own row.</summary>
    /// <remarks>What an unset preference reads as, because it is how this client has always listed mail.</remarks>
    public static ClientMailListMode Messages { get; } = new("messages");

    /// <summary>Gets the choice to list every conversation on one row.</summary>
    public static ClientMailListMode Threads { get; } = new("threads");

    /// <summary>Gets every choice this build publishes.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<ClientMailListMode> All { get; } = [Messages, Threads];

    /// <summary>Gets whether this value names a published choice rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the published name, which is what a client reads and what the stored document holds.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a choice.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a list mode.");

    /// <summary>Reports the choice by its published name.</summary>
    /// <param name="name">The name to resolve.</param>
    /// <param name="mode">The choice the name publishes, or the struct default when no choice publishes it.</param>
    /// <returns><see langword="true" /> when the name is one this build publishes.</returns>
    public static bool TryParse(string? name, out ClientMailListMode mode)
    {
        mode = All.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));

        return mode.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="ClientMailListMode" /> as its published name, and refuses anything else.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so the client endpoint and the
/// persisted document are read and written the same way without either registering it.
/// </remarks>
public sealed class ClientMailListModeJsonConverter : JsonConverter<ClientMailListMode>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a published choice.</exception>
    public override ClientMailListMode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? ParseOrThrow(reader.GetString())
            : throw new JsonException("A list mode is the name of one, which is a JSON string.");

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the struct default rather than a choice.</exception>
    public override void Write(Utf8JsonWriter writer, ClientMailListMode value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(value.IsSpecified
            ? value.Name
            : throw new JsonException("The value is the default of the struct and does not name a list mode."));
    }

    private static ClientMailListMode ParseOrThrow(string? name) => ClientMailListMode.TryParse(name, out var mode)
        ? mode
        : throw new JsonException("The value does not name a list mode this build publishes.");
}
