// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.AutomaticReplies;

namespace MailFathom.Application.AutomaticReplies;

/// <summary>A user's automatic reply as it was last saved, with the revision that save produced.</summary>
/// <param name="Reply">What the user set.</param>
/// <param name="Revision">
/// The identity of that save. Every save mints a new one, and a sender is answered once per revision, so a reply the
/// user rewrote — a new period, a new text — answers each sender afresh rather than staying silent towards everybody it
/// already answered under the old one.
/// </param>
public sealed record StoredAutomaticReply(AutomaticReply Reply, Guid Revision);
