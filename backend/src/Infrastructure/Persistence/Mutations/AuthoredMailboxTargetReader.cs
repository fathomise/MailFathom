// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Reads, from the local mailbox copy, where one email is in the identities a mutation names.</summary>
/// <remarks>
/// <para>
/// The projection names six columns and joins one folder row, which is everything a mutation is addressed by and
/// nothing a message carries. Reading the entity would pull a subject, the participants, and every derived column into
/// memory to answer a question none of them takes part in, so the query is written as a projection rather than as a
/// lookup that discards most of what it loaded.
/// </para>
/// <para>
/// A tombstoned row answers as an absent email, on the same terms every read of stored mail applies, so answering about
/// mail no listing serves cannot make the write surface a way to learn that a row exists.
/// </para>
/// <para>
/// A local-only copy, kept after its server lost it, is the one row with a dead occurrence this answers for: a listing
/// serves it, and the change a person makes on it commits locally, with the occurrence naming the message and opening
/// no record.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class AuthoredMailboxTargetReader(MailFathomDbContext readContext) : IAuthoredMailboxTargetReader
{
    /// <inheritdoc />
    public async Task<AuthoredMailboxTarget?> FindAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var emailId = storedEmailId.Value;

        var located = await readContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.Id == emailId)
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Select(email => new
            {
                email.MailboxAccountId,
                email.UidValidity,
                email.Uid,
                Alias = email.MailFolderBinding.Alias,
                Generation = email.MailFolderBinding.ResolutionGeneration,
                RemotePath = email.MailFolderBinding.RemotePath,
                email.FolderId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        // A row that never held an occurrence, or whose occurrence the drain cleared, has nothing a request can name.
        if (located is not { UidValidity: { } uidValidity, Uid: { } uid })
        {
            return null;
        }

        var folder = new MailFolderResolution(
            MailFolderAlias.Create(located.Alias),
            MailFolderResolutionGeneration.Create(located.Generation),
            RemoteFolderPath.Create(located.RemotePath));

        return new AuthoredMailboxTarget(
            EmailOccurrenceId.Create(
                MailAccountId.Create(located.MailboxAccountId),
                folder.Id,
                ImapUidValidity.Create(uidValidity),
                ImapUid.Create(uid)),
            folder,
            MailFolderId.Create(located.FolderId));
    }
}
