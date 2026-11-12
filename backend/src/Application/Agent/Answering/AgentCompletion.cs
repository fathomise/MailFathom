// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;

namespace MailFathom.Application.Agent.Answering;

/// <summary>What the ending of an answer that completed carries, as the composition hands it back.</summary>
/// <param name="FollowUps">The questions the agent suggests asking next, each one an <see cref="AgentFollowUps.IsAskable" /> line, and empty where it suggested none.</param>
/// <param name="Ceiling">The ceiling the run reached and answered at, or <see langword="null" /> where it answered within its mode.</param>
public sealed record AgentCompletion(IReadOnlyList<PresentationText> FollowUps, AgentRunCeiling? Ceiling)
{
    /// <summary>Gets the completion of an answer that suggested nothing and reached no ceiling.</summary>
    public static AgentCompletion Plain { get; } = new([], Ceiling: null);
}
