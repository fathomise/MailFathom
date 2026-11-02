// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>One conversation of a mail list grouped by conversation, and where it stands in that list.</summary>
/// <param name="Thread">The whole conversation, described as the conversation list describes it.</param>
/// <param name="Position">The position of its newest message that passed the read, which is what the list is ordered and paged by.</param>
/// <remarks>
/// The two are read under different rules on purpose. The position is the newest message the filters let through, so
/// the filters decide where a conversation stands; the description counts and quotes every message of it the reader
/// may read, so a row says what the conversation is rather than what of it matched.
/// </remarks>
public sealed record GroupedEmailThread(ListedEmailThread Thread, EmailTimelinePosition Position);
