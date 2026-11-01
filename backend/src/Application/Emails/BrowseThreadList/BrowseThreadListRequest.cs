// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>One request for a page of a person's conversations.</summary>
public sealed record BrowseThreadListRequest
{
    /// <summary>Gets the account the list is narrowed to, or <see langword="null" /> for every account the person is assigned.</summary>
    public MailAccountSelector? Account { get; init; }

    /// <summary>Gets how many conversations the page may hold, or <see langword="null" /> for the default.</summary>
    public int? PageSize { get; init; }

    /// <summary>Gets the cursor a previous page returned, or <see langword="null" /> for the first page.</summary>
    public string? Cursor { get; init; }
}
