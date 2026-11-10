// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Reads the visible mail that has passed the age its account keeps.</summary>
public interface IAgedStoredMailReader
{
    /// <summary>Reads one batch of an account's mail past the age, in identifier order.</summary>
    /// <param name="session">The transaction the batch is read in.</param>
    /// <param name="account">The account whose mail is read.</param>
    /// <param name="retention">The folders left alone, read from <see cref="MailMessageRetention.ExcludedFolders" />.</param>
    /// <param name="receivedBefore">The moment a message must have been received before, or stored where the server named no date.</param>
    /// <param name="resumeAfter">The last message of the previous batch, so a row this run could not retire is not read again; <see langword="null" /> for the first.</param>
    /// <param name="limit">The most rows this call reads.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The messages, which are fewer than the limit when no more could be taken now.</returns>
    /// <remarks>
    /// A row hidden or already being deleted is not read, nor one with any other change outstanding, nor one a standing
    /// draft or an unfinished send still owns, nor one whose retention delete at this occurrence has already been given
    /// up, which is what keeps a refused delete from being asked for every night.
    /// </remarks>
    Task<IReadOnlyList<AgedStoredMail>> ReadAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailMessageRetention retention,
        DateTimeOffset receivedBefore,
        StoredEmailId? resumeAfter,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>One message past the age its account keeps.</summary>
/// <param name="Email">The message.</param>
/// <param name="Folder">The folder a client lists it under.</param>
/// <param name="SourceFolder">The folder its account's configuration names it by.</param>
/// <param name="Occurrence">Where a mail server holds it, or <see langword="null" /> where it holds no occurrence any more, which is what a drained held message is.</param>
public sealed record AgedStoredMail(
    StoredEmailId Email,
    MailFolderId Folder,
    MailFolderAlias SourceFolder,
    EmailOccurrenceId? Occurrence);
