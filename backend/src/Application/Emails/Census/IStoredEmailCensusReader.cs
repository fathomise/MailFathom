// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Emails.Census;

/// <summary>Counts the locally stored mail a selection admits, grouped as a census asks.</summary>
/// <remarks>
/// The port is read-only and joins no transaction, for the reason every read port does. It reads no body and no
/// stored content, only the columns a group is keyed by, and it returns figures and keys rather than mail.
/// </remarks>
public interface IStoredEmailCensusReader
{
    /// <summary>Counts the mail a selection admits.</summary>
    /// <param name="selection">The validated scope and filters.</param>
    /// <param name="grouping">What the mail is grouped by.</param>
    /// <param name="leadingGroups">How many groups the reading carries at most, at least one.</param>
    /// <param name="cancellationToken">Cancels the count, which is also how a use case bounds how long it may take.</param>
    /// <returns>The census, every figure exact for the selection at the moment it was counted.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="leadingGroups" /> is below one.</exception>
    Task<MailCensus> CountAsync(
        MailboxEmailSelection selection,
        MailCensusGrouping grouping,
        int leadingGroups,
        CancellationToken cancellationToken);
}
