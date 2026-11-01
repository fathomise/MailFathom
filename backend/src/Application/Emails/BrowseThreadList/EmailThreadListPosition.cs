// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>Where one conversation sits in the list of conversations, which is what a page continues after.</summary>
/// <param name="NewestArrival">When the conversation's newest message arrived, or <see langword="null" /> where none of its messages carried a usable date.</param>
/// <param name="ThreadId">The conversation, which breaks a tie between two that last moved at the same instant.</param>
/// <remarks>
/// The list is newest first and a conversation with no dated message sorts after every dated one, the way an undated
/// message sorts in the timeline. The position is a value rather than an offset, so a conversation that moves to the
/// top while somebody scrolls does not shift the rest of the list under their cursor.
/// </remarks>
public readonly record struct EmailThreadListPosition(DateTimeOffset? NewestArrival, EmailThreadId ThreadId);
