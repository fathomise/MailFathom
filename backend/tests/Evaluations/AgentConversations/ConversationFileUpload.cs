// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Evaluations.AgentConversations;

/// <summary>One file a case's person uploaded into the conversation before asking.</summary>
/// <param name="Name">The name the file was uploaded under.</param>
/// <param name="Content">The octets, which the intake reads the kind and the words from.</param>
/// <param name="Carried">Whether the question carries it, rather than only the conversation holding it from earlier.</param>
internal sealed record ConversationFileUpload(string Name, ReadOnlyMemory<byte> Content, bool Carried = true);
