// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.AI.AgentConversations;

/// <summary>One of the person's accounts as the Agent's turn names it.</summary>
/// <param name="Id">The deployment's own name for the account, which a proposal names it by.</param>
/// <param name="ClosesWithFooter">Whether a message sent from it closes with the person's footer, which the turn states and nothing of which it carries.</param>
internal sealed record AgentNamedAccount(MailAccountId Id, bool ClosesWithFooter);
