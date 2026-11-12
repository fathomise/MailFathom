// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Application.StoredFiles;

namespace MailFathom.Application.Agent.Files;

/// <summary>Names one file a person handed to one of their conversations with the Agent.</summary>
/// <remarks>
/// Its value is the identifier of the stored file that holds the octets, because the two are one file: the conversation
/// row says whose conversation holds it and what reading it came to, and the stored file says where its octets are.
/// </remarks>
[JsonConverter(typeof(AgentConversationFileIdJsonConverter))]
public readonly record struct AgentConversationFileId
{
    private AgentConversationFileId(Guid value) => this.Value = value;

    /// <summary>Gets the non-empty UUID value.</summary>
    public Guid Value { get; }

    /// <summary>Gets the stored file holding the octets.</summary>
    public StoredFileId StoredFile => StoredFileId.Create(this.Value);

    /// <summary>Names a conversation's file by the identifier its stored file was minted under.</summary>
    /// <param name="value">The identifier.</param>
    /// <returns>The file's name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is empty.</exception>
    public static AgentConversationFileId Create(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A conversation's file is named by a non-empty identifier.", nameof(value));
        }

        return new AgentConversationFileId(value);
    }

    /// <summary>Names a conversation's file by the stored file holding its octets.</summary>
    /// <param name="file">The stored file.</param>
    /// <returns>The file's name.</returns>
    public static AgentConversationFileId Of(StoredFileId file) => Create(file.Value);

    /// <inheritdoc />
    public override string ToString() => this.Value.ToString("D");
}
