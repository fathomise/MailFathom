// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Classification;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Classification;

/// <summary>EF Core state for the one whole-mailbox classification run an account may have outstanding.</summary>
[RequiresIntegrationCoverage]
internal sealed class ClassificationRunStore(MailFathomDbContext dbContext) : IClassificationRunStore
{
    /// <inheritdoc />
    public async Task<ClassificationRun?> FindOutstandingAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var mailboxAccountId = account.Value;
        var outstanding = await dbContext.ClassificationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.MailboxAccountId == mailboxAccountId
                    && run.EndedAt == null,
                cancellationToken);

        return outstanding is null ? null : Read(outstanding, account);
    }

    /// <inheritdoc />
    /// <remarks>One row per account, so the account's key is the whole of the lookup and no ordering is needed.</remarks>
    public async Task<ClassificationRun?> FindLatestAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var mailboxAccountId = account.Value;
        var latest = await dbContext.ClassificationRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(
                run => run.MailboxAccountId == mailboxAccountId,
                cancellationToken);

        return latest is null ? null : Read(latest, account);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One row per account, so a request that follows a completed run overwrites it rather than appending. The lookup
    /// resolves a row this session already staged from the change tracker, which is what lets a pass commit several
    /// batches through one session without inserting a second row under the same key.
    /// </remarks>
    public async Task SaveAsync(
        IPersistenceSession session,
        ClassificationRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var stored = await sessionContext.ClassificationRuns.FindAsync(
            [run.Account.Value],
            cancellationToken);

        if (stored is null)
        {
            stored = new ClassificationRunEntity
            {
                MailboxAccountId = run.Account.Value,

                // Written from the identity the request resolved through the deployment's catalog, which is the account
                // this run walks. A run belongs to the user whose mail it classifies.
                SpamFolderAliases = [],
                CategoryFolderAliases = [],
            };

            sessionContext.ClassificationRuns.Add(stored);
        }

        Write(stored, run);
    }

    private static ClassificationRun Read(
        ClassificationRunEntity entity,
        MailAccountId account) => new()
        {
            Account = account,
            RequestedAt = entity.RequestedAt,
            Terms = ClassificationRunTerms.Create(
                entity.SpamFolderAliases.Select(MailFolderAlias.Create),
                entity.CategoryFolderAliases.Select(MailFolderAlias.Create),
                entity.Posture,
                entity.Rescores,
                entity.OwnCategory),
            Profile = entity.Profile is { } profile ? SpamClassificationProfile.Restore(profile) : default,
            Position = entity.Position is { } position ? StoredEmailId.Create(position) : null,
            ClassifiedEmailCount = entity.ClassifiedEmailCount,
            SpamEmailCount = entity.SpamEmailCount,
            UndeterminedEmailCount = entity.UndeterminedEmailCount,
            SkippedEmailCount = entity.SkippedEmailCount,
            UnclassifiableEmailCount = entity.UnclassifiableEmailCount,
            ActedEmailCount = entity.ActedEmailCount,
            CategorizedEmailCount = entity.CategorizedEmailCount,
            CategoriesCurrentEmailCount = entity.CategoriesCurrentEmailCount,
            CategoriesUnansweredEmailCount = entity.CategoriesUnansweredEmailCount,
            CategoriesClearedEmailCount = entity.CategoriesClearedEmailCount,
            EndedAt = entity.EndedAt,
            Ending = entity.Ending,
        };

    private static void Write(ClassificationRunEntity entity, ClassificationRun run)
    {
        entity.RequestedAt = run.RequestedAt;
        entity.SpamFolderAliases = [.. run.Terms.SpamFolderAliases.Select(static alias => alias.Value)];
        entity.CategoryFolderAliases = [.. run.Terms.CategoryFolderAliases.Select(static alias => alias.Value)];
        entity.OwnCategory = run.Terms.OwnCategory;
        entity.Posture = run.Terms.Posture;
        entity.Rescores = run.Terms.Rescores;
        entity.Profile = run.Profile.IsSpecified ? run.Profile.Value : null;
        entity.Position = run.Position?.Value;
        entity.ClassifiedEmailCount = run.ClassifiedEmailCount;
        entity.SpamEmailCount = run.SpamEmailCount;
        entity.UndeterminedEmailCount = run.UndeterminedEmailCount;
        entity.SkippedEmailCount = run.SkippedEmailCount;
        entity.UnclassifiableEmailCount = run.UnclassifiableEmailCount;
        entity.ActedEmailCount = run.ActedEmailCount;
        entity.CategorizedEmailCount = run.CategorizedEmailCount;
        entity.CategoriesCurrentEmailCount = run.CategoriesCurrentEmailCount;
        entity.CategoriesUnansweredEmailCount = run.CategoriesUnansweredEmailCount;
        entity.CategoriesClearedEmailCount = run.CategoriesClearedEmailCount;
        entity.EndedAt = run.EndedAt;
        entity.Ending = run.Ending;
    }
}
