// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>One page of a person's mail list grouped by conversation.</summary>
/// <param name="Conversations">The conversations, in the order the list is sorted in.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
/// <param name="PreviousCursor">The cursor the preceding page is asked with, or <see langword="null" /> at the beginning of the list.</param>
/// <param name="PageSize">How many conversations the read ran under.</param>
public sealed record BrowsedConversationPage(
    IReadOnlyList<BrowsedListedThread> Conversations,
    string? NextCursor,
    string? PreviousCursor,
    int PageSize);
