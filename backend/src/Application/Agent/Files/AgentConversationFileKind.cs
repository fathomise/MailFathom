// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>What a conversation's file is, as its octets prove it rather than as its name claims.</summary>
public enum AgentConversationFileKind
{
    /// <summary>A document the attachment text reader reads: its words reach the model, whole or a passage at a time.</summary>
    Document = 0,

    /// <summary>A picture, which reaches the model as a picture with the question that carries it.</summary>
    Picture = 1,
}
