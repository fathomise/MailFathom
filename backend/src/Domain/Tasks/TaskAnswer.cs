// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Domain.Tasks;

/// <summary>Cites the reply that answered a task its person was waiting on.</summary>
/// <remarks>The reply is cited rather than copied, for the reason a task's source message is: the task keeps no second copy of the mailbox.</remarks>
/// <param name="Message">The message that carried the answer.</param>
/// <param name="ArrivedAt">The instant it arrived.</param>
public sealed record TaskAnswer(StoredEmailId Message, DateTimeOffset ArrivedAt);
