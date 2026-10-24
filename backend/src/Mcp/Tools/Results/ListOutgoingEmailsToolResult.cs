// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Mcp.Tools.Outgoing;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes one page of an account's outbox.</summary>
[Description("One page of what an account has not sent yet, newest first, with the cursor of the next page.")]
internal sealed record ListOutgoingEmailsToolResult
{
    /// <summary>Gets the sends on this page.</summary>
    [Description("The sends on this page, newest first. Empty when the account has nothing waiting, sending, or refused.")]
    public required IReadOnlyList<ListedOutgoingEmail> OutgoingEmails { get; init; }

    /// <summary>Gets the cursor of the next page, or <see langword="null" /> when this is the last.</summary>
    [Description("The cursor to pass back as cursor to read the following page, with the same account and stage, or null when this page is the last.")]
    public string? NextCursor { get; init; }

    /// <summary>Publishes one page.</summary>
    /// <param name="page">The page the outbox reading produced.</param>
    /// <returns>The wire representation of <paramref name="page" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    public static ListOutgoingEmailsToolResult From(AccountOutboxPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListOutgoingEmailsToolResult
        {
            OutgoingEmails = [.. page.Sends.Select(ListedOutgoingEmail.From)],
            NextCursor = page.NextCursor?.Encode(),
        };
    }
}
