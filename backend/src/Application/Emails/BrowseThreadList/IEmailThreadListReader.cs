// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.BrowseThreadList;

/// <summary>Reads the conversations a person's mail holds, newest first, from the local mailbox copy.</summary>
public interface IEmailThreadListReader
{
    /// <summary>The most participants one listed conversation names.</summary>
    /// <remarks>Enough for a row to say who is in the exchange; a mailing list's hundred authors are a count, not a row.</remarks>
    const int MaximumNamedParticipants = 8;

    /// <summary>Reads one page of conversations.</summary>
    /// <param name="scope">The accounts and folders the reader may read, with junk mail included, which the read is narrowed by.</param>
    /// <param name="readerAddresses">The addresses the reader sends as, which decide what counts as received and who is the reader.</param>
    /// <param name="continueAfter">The position the previous page ended on, or <see langword="null" /> for the first page.</param>
    /// <param name="limit">How many conversations to read at most.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The conversations newest first, by their newest message and then by identity.</returns>
    /// <remarks>
    /// <para>
    /// A conversation is listed while at least one message of it the scope admits is neither judged spam nor in Trash or
    /// a folder beneath it. One that is all spam, all Trash, or some of each is the mail somebody put away, and is left
    /// out; one with a single ordinary message is listed whole, every message counted.
    /// </para>
    /// <para>
    /// Folders do not narrow it. A conversation gathers its messages from every folder the scope admits, which is what
    /// opening it reads, and a list narrowed to one folder would quote a conversation by the half of it that sits there.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ListedEmailThread>> ReadPageAsync(
        MailboxScope scope,
        IReadOnlySet<EmailAddress> readerAddresses,
        EmailThreadListPosition? continueAfter,
        int limit,
        CancellationToken cancellationToken);
}
