// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>Why a file was not taken into a conversation.</summary>
public enum AgentConversationFileRefusal
{
    /// <summary>The upload carried no octets.</summary>
    Empty = 0,

    /// <summary>The file holds more than <see cref="AgentConversationFileBounds.MaximumFileOctets" />.</summary>
    TooLarge = 1,

    /// <summary>The octets are neither a document the attachment text reader reads nor a picture a chat request carries.</summary>
    KindNotTaken = 2,

    /// <summary>The conversation already holds <see cref="AgentConversationFileBounds.MaximumFilesPerConversation" />.</summary>
    ConversationFull = 3,

    /// <summary>No conversation of this person stands under that identifier, and none could be started there.</summary>
    NoSuchConversation = 4,
}
