// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Application.Preferences;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Receipts;

/// <summary>Reports a colleague's send as read when a person opens it in the client.</summary>
/// <remarks>
/// <para>
/// Opening a message is the act ADR 0026 decides, and it is the only one recorded here: the client says which flag
/// changes it submitted because the reader opened the messages, and a rule, the spam verdict, an MCP caller, <em>mark
/// as read</em>, and another mail client each set <c>\Seen</c> without reaching this. Only a message an organization's
/// delivery was recorded on is read for its sender, so opening any other mail reports nothing to anybody.
/// </para>
/// <para>
/// A person who turned <see cref="ClientPreferences.MarkReadOnOpen" /> off has asked for their opening to write
/// nothing, and it records no read either — checked here as well as in the client, because the setting is the person's
/// whatever client submitted the change. The same answer holds whichever custody phase the account is in: the read
/// hangs on the act, which every phase carries, and never on where the flag goes.
/// </para>
/// </remarks>
/// <param name="store">Finds the deliveries the opened messages carry and records each read.</param>
/// <param name="preferences">Holds whether the person lets opening a message mark it read.</param>
/// <param name="authorization">Names the person who opened the messages.</param>
/// <param name="retryPolicy">Retries the write where another replica recorded the same read at the same moment.</param>
/// <param name="timeProvider">Supplies the time the messages were opened.</param>
public sealed class OpenedMailReceipts(
    IReceiptReportStore store,
    IClientPreferencesStore preferences,
    AccessAuthorization authorization,
    OptimisticConcurrencyRetryPolicy retryPolicy,
    TimeProvider timeProvider)
{
    /// <summary>Records the person's reading of the messages they opened, where any of them is a colleague's send.</summary>
    /// <param name="opened">The messages the person opened, each one whose read flag change was accepted.</param>
    /// <param name="cancellationToken">Cancels the read of the preference and the write.</param>
    /// <returns>A task that completes when the reads are recorded, or at once where there is nothing to record.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller acts for no user.</exception>
    public async Task RecordAsync(IReadOnlyCollection<StoredEmailId> opened, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(opened);

        if (opened.Count == 0)
        {
            return;
        }

        var own = await preferences.ReadAsync(authorization.RequireUser(), cancellationToken) ?? ClientPreferences.Unset;

        if (!own.MarkReadOnOpen)
        {
            return;
        }

        var openedAt = timeProvider.GetUtcNow();

        await retryPolicy.CommitAsync(
            (session, attemptCancellationToken) => store.RecordOpenedAsync(session, opened, openedAt, attemptCancellationToken),
            cancellationToken);
    }
}
