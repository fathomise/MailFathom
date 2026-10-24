// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.ComponentModel;
using MailFathom.Application.Mail.Delivery.Tracking;

namespace MailFathom.Mcp.Tools.Accounts;

/// <summary>Publishes how much an account's outbox holds.</summary>
/// <remarks>
/// The outbox is a member of the account rather than one of its folders, so it is counted here and listed by
/// <c>list_outgoing_emails</c>. Each number counts exactly the entries that listing holds at those stages.
/// </remarks>
[Description("What this account has not sent yet, as two counts. List the messages themselves with list_outgoing_emails.")]
internal sealed record ListedAccountOutbox
{
    /// <summary>Gets how many sends are queued or being sent.</summary>
    [Description("How many messages are queued or being sent, including one stopped mid-attempt whose outcome nobody knows.")]
    public required int Waiting { get; init; }

    /// <summary>Gets how many sends a mail server refused and nobody has decided about.</summary>
    [Description("How many messages a mail server refused, or that ran out of attempts, and that the person has not decided about yet.")]
    public required int Refused { get; init; }

    /// <summary>Publishes one account's count.</summary>
    /// <param name="count">The count the outbox reading produced.</param>
    /// <returns>The wire representation of <paramref name="count" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="count" /> is <see langword="null" />.</exception>
    public static ListedAccountOutbox From(AccountOutboxCount count)
    {
        ArgumentNullException.ThrowIfNull(count);

        return new ListedAccountOutbox
        {
            Waiting = count.Waiting,
            Refused = count.Refused,
        };
    }
}
