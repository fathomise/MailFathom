// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>One file a conversation holds, with its octets.</summary>
/// <param name="File">The file, as everything but its octets describes it.</param>
/// <param name="Content">The whole file.</param>
public sealed record AgentConversationFileContent(AgentConversationFile File, ReadOnlyMemory<byte> Content);
