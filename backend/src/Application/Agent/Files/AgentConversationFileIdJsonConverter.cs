// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Agent.Files;

/// <summary>Writes a conversation's file identifier as the UUID string it is, and refuses one that names nothing.</summary>
public sealed class AgentConversationFileIdJsonConverter : JsonConverter<AgentConversationFileId>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string, is not a UUID, or is the empty UUID.</exception>
    public override AgentConversationFileId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A file identifier must be a JSON string, but the token was {reader.TokenType}.");
        }

        if (!reader.TryGetGuid(out var value) || value == Guid.Empty)
        {
            throw new JsonException("The value does not name a file.");
        }

        return AgentConversationFileId.Create(value);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer,
        AgentConversationFileId value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
