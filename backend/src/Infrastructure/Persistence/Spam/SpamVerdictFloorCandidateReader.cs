// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Floor;
using MailFathom.Application.Spam.Signals;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Spam;

/// <summary>EF Core reading of the mail one account holds below the spam floor.</summary>
/// <remarks>
/// The query states the same rule <see cref="DeterministicSpamClassifier.LiftToFloor" /> decides, so a message it
/// selects is one the pass raises: a message the rule would leave alone and the query selected anyway would be read again
/// on every run, ahead of the mail behind it. The junk folder is the one the message is in now
/// (<c>Folder</c>), which is right in every custody phase; whether classification still awaits it is read against the
/// binding, which is what classification's own scope is read against. The keywords are compared in the folded form
/// <see cref="RemoteEmailKeywords" /> stores them in, which the column's GIN index serves.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class SpamVerdictFloorCandidateReader(MailFathomDbContext dbContext) : ISpamVerdictFloorCandidateReader
{
    private static readonly string Junk = RemoteEmailKeywords.Normalized(JunkKeywords.Junk)!;

    private static readonly string NotJunk = RemoteEmailKeywords.Normalized(JunkKeywords.NotJunk)!;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SpamVerdictFloorCandidate>> GetCandidatesAsync(
        MailAccountId account,
        SpamVerdictFloorTerms terms,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        var mailboxAccountId = account.Value;
        string[] junkFolders = [.. terms.JunkFolders.Select(static alias => alias.Value)];
        string[] classifiedFolders = [.. terms.ClassifiedFolders.Select(static alias => alias.Value)];
        var awaitedWhenStoredAfter = terms.AwaitedWhenStoredAfter;

        var emails = dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.MailboxAccountId == mailboxAccountId)
            .Where(email => email.SpamClassification == null
                || (email.SpamClassification.Verdict != SpamVerdict.Spam
                    && email.SpamClassification.DecidedBy != SpamClassificationStage.Person))
            .Where(email =>
                (email.RemoteKeywords.Contains(Junk) && !email.RemoteKeywords.Contains(NotJunk))
                || (email.Folder.Alias != null
                    && junkFolders.Contains(email.Folder.Alias)
                    && !(email.RemoteKeywords.Contains(NotJunk)
                        && (!email.RemoteKeywords.Contains(Junk)
                            || dbContext.EmailSpamVerdictActs.Any(act =>
                                act.StoredEmailId == email.Id && act.Act == SpamVerdictAct.MarkedJunk)))))
            .Where(email => !(email.SpamClassification == null
                && email.ContentAvailability == StoredEmailContentAvailability.Available
                && email.StoredAt > awaitedWhenStoredAfter
                && classifiedFolders.Contains(email.MailFolderBinding.Alias)));

        if (resumeAfter is { } position)
        {
            var boundary = position.Value;

            emails = emails.Where(email => email.Id > boundary);
        }

        var rows = await emails
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new { email.Id, email.Folder.Alias, email.RemoteKeywords })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. rows.Select(static row => new SpamVerdictFloorCandidate(
                StoredEmailId.Create(row.Id),
                row.Alias is { } alias ? (MailFolderAlias?)MailFolderAlias.Create(alias) : null,
                RemoteEmailKeywords.Create(row.RemoteKeywords))),
        ];
    }
}
