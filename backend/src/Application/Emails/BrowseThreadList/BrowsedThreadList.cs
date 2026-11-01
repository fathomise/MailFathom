// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>One page of a person's conversations.</summary>
/// <param name="Threads">The conversations, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the list.</param>
/// <param name="PageSize">How many conversations the read ran under.</param>
public sealed record BrowsedThreadList(
    IReadOnlyList<BrowsedListedThread> Threads,
    string? NextCursor,
    int PageSize);
