// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Agent.Files;

/// <summary>What became of a file handed to a conversation: the file it became, or why it was not taken.</summary>
/// <param name="File">The file, where it was taken.</param>
/// <param name="Refusal">Why it was not, where it was not.</param>
public sealed record AgentConversationFileIntake(AgentConversationFile? File, AgentConversationFileRefusal? Refusal)
{
    /// <summary>States that the file was taken.</summary>
    /// <param name="file">The file it became.</param>
    /// <returns>The intake.</returns>
    public static AgentConversationFileIntake Taken(AgentConversationFile file) => new(file ?? throw new ArgumentNullException(nameof(file)), Refusal: null);

    /// <summary>States that the file was not taken.</summary>
    /// <param name="refusal">Why.</param>
    /// <returns>The intake.</returns>
    public static AgentConversationFileIntake Refused(AgentConversationFileRefusal refusal) => new(File: null, refusal);
}
