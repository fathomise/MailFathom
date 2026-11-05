// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Sets;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>Keeps each mail account's rules, the set's version, and the archive of every committed write in PostgreSQL.</summary>
/// <remarks>
/// A write serializes on the set row's lock rather than on an optimistic retry: the version a writer states it read is
/// compared under the lock, so a second write never commits a set it did not see, and the comparison is the caller's to
/// answer rather than something a retry could converge on. The rules are rewritten whole on every commit — at most a
/// couple of hundred rows — so the stored order is always the order a pass reads.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailRuleSetStore(MailFathomDbContext readContext) : IMailRuleSetStore
{
    /// <inheritdoc />
    /// <remarks>
    /// One statement reads the version and the rules together, so a reader never pairs one commit's version with
    /// another commit's rules.
    /// </remarks>
    public async Task<StoredMailRuleSet> ReadAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var accountId = account.Value;

        var stored = await readContext.MailRuleSets
            .AsNoTracking()
            .Where(set => set.MailboxAccountId == accountId)
            .Select(set => new
            {
                set.Version,
                Rules = readContext.MailRules
                    .Where(rule => rule.MailboxAccountId == set.MailboxAccountId)
                    .OrderBy(rule => rule.Position)
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return stored is null
            ? StoredMailRuleSet.Empty(account)
            : new StoredMailRuleSet(account, stored.Version, [.. stored.Rules.Select(ToDeclaration)]);
    }

    /// <inheritdoc />
    public async Task<StoredMailRuleSet> LockAsync(
        IPersistenceSession session,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountId = account.Value;

        // Nothing is created here, because the policy commits whatever an attempt staged and a refused or stale write
        // must leave no row behind. An account nobody has committed a rule for has no set row to lock, and its first
        // write creates one in CommitAsync instead.
        var version = await writeContext.Database
            .SqlQuery<long>($"""SELECT "Version" AS "Value" FROM mail_rule_sets WHERE "MailboxAccountId" = {accountId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);

        var rules = await writeContext.MailRules
            .AsNoTracking()
            .Where(rule => rule.MailboxAccountId == accountId)
            .OrderBy(rule => rule.Position)
            .ToListAsync(cancellationToken);

        return new StoredMailRuleSet(account, version, [.. rules.Select(ToDeclaration)]);
    }

    /// <inheritdoc />
    public async Task CommitAsync(
        IPersistenceSession session,
        MailRuleSetArchiveEntry committed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(committed);

        if (!committed.Surface.IsSpecified)
        {
            throw new ArgumentException("A committed change names the surface it came through.", nameof(committed));
        }

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountId = committed.Account.Value;

        // An account nothing has synchronized yet has no row for the set's foreign key, so its first write adds the same
        // row a first folder binding would. A binding that loses the race to it violates the account's primary key, which
        // is a named concurrency conflict, and is retried over the row this write left.
        await writeContext.Database.ExecuteSqlAsync(
            $"""INSERT INTO mailbox_accounts ("Id", "LocalMailFoldersRevision", "RestoreGeneration") VALUES ({accountId}, 0, 0) ON CONFLICT ("Id") DO NOTHING""",
            cancellationToken);

        await writeContext.MailRules
            .Where(rule => rule.MailboxAccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);

        // A first write had no row to lock, so two of them can both get here. Each inserts the set row, and the one
        // committing second violates its primary key, a named concurrency conflict: the retry locks the row the first
        // one wrote and answers a version conflict.
        if (committed.Version == 1)
        {
            writeContext.MailRuleSets.Add(new MailRuleSetEntity { MailboxAccountId = accountId, Version = committed.Version });
        }
        else
        {
            await writeContext.MailRuleSets
                .Where(set => set.MailboxAccountId == accountId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Version, committed.Version), cancellationToken);
        }

        writeContext.MailRules.AddRange(committed.Declarations.Select((declaration, position) => ToEntity(accountId, declaration, position)));
        writeContext.MailRuleSetRevisions.Add(new MailRuleSetRevisionEntity
        {
            Id = Guid.CreateVersion7(committed.CommittedAt),
            MailboxAccountId = accountId,
            Version = committed.Version,
            Revision = committed.Revision.Value,
            ReplacedRevision = committed.ReplacedRevision.Value,
            ActorUserId = committed.Actor?.Value,
            Surface = committed.Surface.ToString(),
            CommittedAt = committed.CommittedAt,
            Declarations = StoredMailRuleDeclaration.SerializeSet(committed.Declarations),
        });
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledMailRule>> ReadScheduledRulesAsync(CancellationToken cancellationToken)
    {
        var scheduled = await readContext.MailRules
            .AsNoTracking()
            .Where(rule => rule.Enabled && rule.Schedule != null)
            .OrderBy(rule => rule.MailboxAccountId)
            .ThenBy(rule => rule.Position)
            .Select(rule => new { rule.MailboxAccountId, rule.Id, rule.Schedule })
            .ToListAsync(cancellationToken);

        return
        [
            .. scheduled.Select(rule => new ScheduledMailRule(
                MailAccountId.Create(rule.MailboxAccountId),
                MailRuleId.Create(rule.Id),
                StoredMailRuleDeclaration.ReadSchedule(rule.Schedule)!)),
        ];
    }

    /// <inheritdoc />
    public async Task<MailRuleSetArchiveEntry?> FindArchivedAsync(
        MailAccountId account,
        MailRuleSetRevision revision,
        DateTimeOffset committedAtOrBefore,
        CancellationToken cancellationToken)
    {
        var accountId = account.Value;
        var revisionValue = revision.Value;

        var archived = await readContext.MailRuleSetRevisions
            .AsNoTracking()
            .Where(row => row.MailboxAccountId == accountId
                && row.Revision == revisionValue
                && row.CommittedAt <= committedAtOrBefore)
            .OrderByDescending(row => row.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return archived is null ? null : ToArchiveEntry(archived);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Whether a retained execution names a row's revision is asked of the account's executions directly, which no index
    /// answers by revision. The number of rows asked about is the bound, and an account's archive rarely holds more than
    /// a handful of rows past the window, so the cost is one probe per expired row; an index over the executions'
    /// account and revision is the upgrade if an archive ever grows to where it shows.
    /// </remarks>
    public async Task<int> EraseExpiredArchiveAsync(
        MailAccountId account,
        DateTimeOffset committedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var accountId = account.Value;

        var expired = await readContext.MailRuleSetRevisions
            .Where(row => row.MailboxAccountId == accountId
                && row.CommittedAt < committedBefore
                && readContext.MailRuleSetRevisions.Any(newer => newer.MailboxAccountId == accountId && newer.Version > row.Version)
                && !readContext.MailRuleExecutions.Any(execution => execution.MailboxAccountId == accountId && execution.Revision == row.Revision)
                && !readContext.MailRuleEvaluationRuns.Any(run => run.MailboxAccountId == accountId && run.Revision == row.Revision))
            .OrderBy(row => row.CommittedAt)
            .Select(row => row.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return expired.Count == 0
            ? 0
            : await readContext.MailRuleSetRevisions
                .Where(row => expired.Contains(row.Id))
                .ExecuteDeleteAsync(cancellationToken);
    }

    private static MailRuleEntity ToEntity(string accountId, MailRuleDeclaration declaration, int position) => new()
    {
        Id = declaration.Id.Value,
        MailboxAccountId = accountId,
        Position = position,
        Name = declaration.Name,
        NormalizedName = declaration.Name.ToUpper(CultureInfo.InvariantCulture),
        Enabled = declaration.Enabled,
        Condition = declaration.ConditionText,
        Actions = StoredMailRuleDeclaration.SerializeActions(declaration.Actions),
        StopWhenMatched = declaration.StopWhenMatched,
        Triggers = [.. declaration.Triggers.Select(trigger => trigger.Name)],
        Schedule = declaration.Schedule?.Declaration,
    };

    private static MailRuleDeclaration ToDeclaration(MailRuleEntity entity) => new(
        MailRuleId.Create(entity.Id),
        entity.Name,
        entity.Enabled,
        entity.Condition,
        StoredMailRuleDeclaration.DeserializeActions(entity.Actions),
        entity.StopWhenMatched,
        StoredMailRuleDeclaration.ReadTriggers(entity.Triggers),
        StoredMailRuleDeclaration.ReadSchedule(entity.Schedule));

    private static MailRuleSetArchiveEntry ToArchiveEntry(MailRuleSetRevisionEntity entity) => new(
        MailAccountId.Create(entity.MailboxAccountId),
        entity.Version,
        MailRuleSetRevision.Restore(entity.Revision),
        MailRuleSetRevision.Restore(entity.ReplacedRevision),
        entity.ActorUserId is { } actor ? UserId.Create(actor) : null,
        MailRuleChangeSurface.TryParse(entity.Surface, out var surface) ? surface : default,
        entity.CommittedAt,
        StoredMailRuleDeclaration.DeserializeSet(entity.Declarations));
}
