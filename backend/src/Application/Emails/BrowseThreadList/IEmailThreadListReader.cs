// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>Reads the conversations a person's mail list holds, grouped from the local mailbox copy.</summary>
public interface IEmailThreadListReader
{
    /// <summary>The most participants one listed conversation names.</summary>
    /// <remarks>Enough for a row to say who is in the exchange; a mailing list's hundred authors are a count, not a row.</remarks>
    const int MaximumNamedParticipants = 8;

    /// <summary>Reads one walk of a mail list grouped by conversation.</summary>
    /// <param name="filter">The list's filters, its account scope with junk mail included, and the direction the walk reads in.</param>
    /// <param name="readerAddresses">The addresses the reader sends as, which decide what counts as received and who is the reader.</param>
    /// <param name="continueAfter">The position the walk continues beyond, or <see langword="null" /> from the leading end.</param>
    /// <param name="limit">How many conversations to read at most.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// The conversations, each at the position of its newest message that passed the read, ordered by that position in
    /// the filter's direction and beginning strictly beyond <paramref name="continueAfter" />.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A conversation is in the walk while at least one of its messages passes the filter and is neither judged spam
    /// nor in Trash or a folder beneath it. Its position is the newest such message, so the filters decide which
    /// conversations are listed and where each stands, exactly as they decide which messages an ungrouped list holds.
    /// One that is all spam, all Trash, or some of each is the mail somebody put away, and is left out.
    /// </para>
    /// <para>
    /// Once it is in, the conversation is described whole: every message of it the scope admits is counted and quoted,
    /// spam and Trash included, whatever the filter let through. Folders do not narrow the description, because a
    /// conversation gathers its messages from every folder the scope admits, which is what opening it reads.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<GroupedEmailThread>> ReadGroupedPageAsync(
        EmailTimelineFilter filter,
        IReadOnlySet<EmailAddress> readerAddresses,
        EmailTimelinePosition? continueAfter,
        int limit,
        CancellationToken cancellationToken);
}
