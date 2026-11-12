// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>One file a conversation holds, as everything but its octets and its words describe it.</summary>
/// <param name="Id">The file's identifier.</param>
/// <param name="Name">The name the person's own machine gave the file, normalized as a mail attachment's is, which is shown and decides nothing; <see langword="null" /> where the upload named it nothing usable.</param>
/// <param name="Kind">What the octets proved the file to be.</param>
/// <param name="MediaType">The media type the octets proved, which is what it is read and sent as.</param>
/// <param name="ByteLength">How many octets it holds.</param>
/// <param name="AddedAt">When it was taken in, in UTC.</param>
/// <param name="Reading">What reading its words came to, or <see langword="null" /> for a picture, whose words nothing reads.</param>
/// <remarks>
/// <strong>Everything here is personal data.</strong> The name and the words came from a person's own machine and may
/// hold anything, so none of it reaches a log, a span, a metric, or a failure message, and every route that reads a file
/// names its owner.
/// </remarks>
public sealed record AgentConversationFile(
    AgentConversationFileId Id,
    string? Name,
    AgentConversationFileKind Kind,
    string MediaType,
    long ByteLength,
    DateTimeOffset AddedAt,
    AgentConversationFileReading? Reading);
