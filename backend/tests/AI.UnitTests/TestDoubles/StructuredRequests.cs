// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json;
using MailFathom.AI.Chat;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Reads the response format a request carried, and writes the answer a provider gives, over either API an agent speaks.</summary>
/// <remarks>
/// The two APIs place a strict schema differently — nested under <c>response_format.json_schema</c> for chat
/// completions, flat under <c>text.format</c> for responses — so a test asserting what an agent asked for reads it here
/// rather than knowing both shapes itself.
/// </remarks>
internal static class StructuredRequests
{
    /// <summary>Writes the payload a provider answers with over one API.</summary>
    /// <param name="api">The API the request went through.</param>
    /// <param name="content">The answer's text.</param>
    /// <returns>The payload.</returns>
    public static string AnswerOver(ChatProviderApi api, string content)
    {
        var text = JsonSerializer.Serialize(content.ReplaceLineEndings(string.Empty));

        return api is ChatProviderApi.Responses
            ? "{\"id\":\"resp-1\",\"object\":\"response\",\"created_at\":1,\"model\":\"a-chat-model\",\"status\":\"completed\","
              + "\"output\":[{\"type\":\"message\",\"id\":\"msg-1\",\"status\":\"completed\",\"role\":\"assistant\","
              + $"\"content\":[{{\"type\":\"output_text\",\"text\":{text},\"annotations\":[]}}]}}]}}"
            : "{\"id\":\"chatcmpl-1\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"a-chat-model\","
              + $"\"choices\":[{{\"index\":0,\"message\":{{\"role\":\"assistant\",\"content\":{text}}},\"finish_reason\":\"stop\"}}]}}";
    }

    /// <summary>Reads the strict schema a request asked for, wherever its API places it.</summary>
    /// <param name="requestBody">The request as it was sent.</param>
    /// <param name="api">The API it was sent through.</param>
    /// <returns>The format's type, its name, whether it is strict, and the schema.</returns>
    public static (string? Type, string? Name, bool Strict, JsonElement Schema) SchemaFormatOf(string requestBody, ChatProviderApi api)
    {
        using var sent = JsonDocument.Parse(requestBody);

        var (format, schemaHolder) = api is ChatProviderApi.Responses
            ? (sent.RootElement.GetProperty("text").GetProperty("format"), sent.RootElement.GetProperty("text").GetProperty("format"))
            : (sent.RootElement.GetProperty("response_format"), sent.RootElement.GetProperty("response_format").GetProperty("json_schema"));

        return (
            format.GetProperty("type").GetString(),
            schemaHolder.GetProperty("name").GetString(),
            schemaHolder.GetProperty("strict").GetBoolean(),
            schemaHolder.GetProperty("schema").Clone());
    }

    /// <summary>Whether a request carried any response format at all, over either API.</summary>
    /// <param name="requestBody">The request as it was sent.</param>
    /// <returns>Whether a format was sent.</returns>
    public static bool CarriesResponseFormat(string requestBody)
    {
        using var sent = JsonDocument.Parse(requestBody);

        return sent.RootElement.TryGetProperty("response_format", out _)
            || (sent.RootElement.TryGetProperty("text", out var text) && text.TryGetProperty("format", out _));
    }
}
