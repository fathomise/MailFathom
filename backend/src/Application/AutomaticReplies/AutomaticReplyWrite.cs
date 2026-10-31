// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.AutomaticReplies;

/// <summary>What became of saving a user's automatic reply.</summary>
/// <param name="Outcome">Whether it was saved, and why not where it was not.</param>
/// <param name="Saved">The reply as saved, present exactly when <paramref name="Outcome" /> is <see cref="AutomaticReplyWriteOutcome.Saved" />.</param>
/// <param name="Account">The account a refusal is about, present exactly for the two refusals that name one.</param>
public sealed record AutomaticReplyWrite(
    AutomaticReplyWriteOutcome Outcome,
    StoredAutomaticReply? Saved = null,
    MailAccountId? Account = null);
