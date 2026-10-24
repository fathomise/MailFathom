// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Folders;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>EF Core implementation for durable alias bindings.</summary>
/// <remarks>
/// The read path uses the scoped context because it joins no transaction. The write path uses the context enlisted in
/// the caller's session, so a binding can only be written inside the transaction the caller opened.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFolderResolutionStore(
    MailFathomDbContext readContext,
    TimeProvider timeProvider)
    : IMailFolderResolutionStore
{
    /// <inheritdoc />
    public async Task<MailFolderResolution?> GetCurrentResolutionAsync(
        MailAccountId account,
        MailFolderAlias folderAlias,
        CancellationToken cancellationToken)
    {
        var aliasValue = folderAlias.Value;
        var accountValue = account.Value;

        // Every generation of an alias is kept, because occurrences stay attributable to the folder they came from,
        // so the current binding is the highest generation rather than the only row. The user leads the narrowing, as
        // it leads the index: an alias is MailFathom's own name within one user's account.
        var entity = await readContext.MailFolderBindings
            .AsNoTracking()
            .Where(folder => folder.MailboxAccountId == accountValue
                && folder.Alias == aliasValue)
            .OrderByDescending(folder => folder.ResolutionGeneration)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : MailFolderBindingResolver.ToResolution(entity);
    }

    /// <inheritdoc />
    public async Task<MailFolderAlias?> GetAliasBoundToAsync(
        MailAccountId account,
        RemoteFolderPath remotePath,
        CancellationToken cancellationToken)
    {
        var pathValue = remotePath.Value;
        var accountValue = account.Value;

        // The generation is compared against the alias's own highest rather than taken as the highest of the rows the
        // path matched: an alias the server has since made MailFathom rebind still holds its earlier generation naming
        // the old folder, and answering with that alias would name a folder it no longer is. Ordered so that two aliases
        // bound to one folder — which configuration permits and nothing else disambiguates — answer the same way twice.
        // The delimiter column is deliberately not compared, which is the reading RemoteFolderPath.NamesSameFolderAs
        // states: a binding written before the server reported a delimiter names the folder one written after names.
        var alias = await readContext.MailFolderBindings
            .AsNoTracking()
            .Where(folder => folder.MailboxAccountId == accountValue
                && folder.RemotePath == pathValue
                && folder.ResolutionGeneration == readContext.MailFolderBindings
                    .Where(binding => binding.MailboxAccountId == folder.MailboxAccountId
                        && binding.Alias == folder.Alias)
                    .Max(binding => binding.ResolutionGeneration))
            .OrderBy(folder => folder.Alias)
            .Select(folder => folder.Alias)
            .FirstOrDefaultAsync(cancellationToken);

        return alias is null ? null : MailFolderAlias.Create(alias);
    }

    /// <inheritdoc />
    public async Task SaveResolutionAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderResolution resolution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var existingBinding = await MailFolderBindingResolver.FindAsync(
            writeContext,
            account,
            resolution.Id,
            cancellationToken);

        if (existingBinding is null)
        {
            await MailFolderBindingResolver.AddAsync(writeContext, account, resolution, cancellationToken);

            return;
        }

        // A row already holding this generation is only this run's own binding when it names the same remote folder.
        // Two overlapping runs that resolved the same alias from the same generation to different folders would
        // otherwise both proceed: the loser would adopt the winner's row and store its own folder's occurrences and
        // checkpoint under it, so one (alias, generation) would name two remote folders — exactly what the generation
        // exists to make impossible. It is reported as a conflict, and the next run resolves against what is durable.
        if (MailFolderBindingResolver.ToResolution(existingBinding) != resolution)
        {
            throw new PersistenceConcurrencyConflictException(
                $"Folder alias {account.Value}/{resolution.Id} was bound to a different remote folder by another writer before this run recorded its own binding.");
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeclareFoldersAsync(
        IPersistenceSession session,
        MailAccountId account,
        IReadOnlyList<MailFolderMapping> mappings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountValue = account.Value;
        var custodyPhase = await writeContext.MailboxAccounts
            .Where(row => row.Id == accountValue)
            .Select(row => (MailAccountCustodyPhase?)row.CustodyPhase)
            .FirstOrDefaultAsync(cancellationToken);

        // An account MailFathom has stored nothing for has no row yet, and the first binding writes one; only a mirrored
        // account takes its folders from what is declared.
        if (custodyPhase != MailAccountCustodyPhase.Mirrored)
        {
            return false;
        }

        var liveFolders = await writeContext.MailFolders
            .Where(folder => folder.MailboxAccountId == accountValue && folder.ErasedAt == null)
            .ToListAsync(cancellationToken);
        var knownFolderCount = liveFolders.Count;

        foreach (var mapping in mappings)
        {
            MailFolderProjection.Declare(
                liveFolders,
                accountValue,
                mapping.Alias.Value,
                mapping.RemotePath?.Value,
                mapping.SpecialUse,
                () => Guid.CreateVersion7(timeProvider.GetUtcNow()));
        }

        writeContext.MailFolders.AddRange(liveFolders.Skip(knownFolderCount));

        var declared = mappings.Select(static mapping => mapping.Alias).ToHashSet();
        var erasedAt = timeProvider.GetUtcNow();

        // The alias stays on the retired row, which is what keeps the mail still filed under it out of every read
        // until the withdrawn folder's erasure removes it.
        foreach (var withdrawn in liveFolders.Where(folder =>
                     folder.Alias is { } alias && !declared.Contains(MailFolderAlias.Create(alias))))
        {
            withdrawn.ErasedAt = erasedAt;
        }

        return writeContext.ChangeTracker.HasChanges();
    }

    /// <inheritdoc />
    public async Task<bool> ProjectFolderAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderResolution resolution,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountValue = account.Value;
        var custodyPhase = await writeContext.MailboxAccounts
            .Where(row => row.Id == accountValue)
            .Select(row => (MailAccountCustodyPhase?)row.CustodyPhase)
            .FirstOrDefaultAsync(cancellationToken);

        // Only the source is the truth on a mirrored account. Once custody has moved, the folder rows are the hierarchy
        // people edit, and a projection from the server would undo their edits.
        if (custodyPhase != MailAccountCustodyPhase.Mirrored)
        {
            return false;
        }

        var binding = await MailFolderBindingResolver.GetRequiredAsync(
            writeContext,
            account,
            resolution.Id,
            cancellationToken);
        var folder = await this.ProjectAsync(writeContext, binding, role, cancellationToken);

        binding.FolderId = folder.Id;

        return writeContext.ChangeTracker.HasChanges();
    }

    /// <inheritdoc />
    public async Task<bool> PlaceFolderAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderAlias folderAlias,
        RemoteFolderPath remotePath,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountValue = account.Value;
        var aliasValue = folderAlias.Value;
        var custodyPhase = await writeContext.MailboxAccounts
            .Where(row => row.Id == accountValue)
            .Select(row => (MailAccountCustodyPhase?)row.CustodyPhase)
            .FirstOrDefaultAsync(cancellationToken);
        var hasRow = await writeContext.MailFolders
            .AnyAsync(folder => folder.MailboxAccountId == accountValue && folder.ErasedAt == null && folder.Alias == aliasValue, cancellationToken);

        if (custodyPhase != MailAccountCustodyPhase.Mirrored || !hasRow)
        {
            return false;
        }

        // Shaped as the binding the next run records will shape it, without recording one: the binding is the run's.
        var placedAt = new MailFolderBindingEntity
        {
            MailboxAccountId = accountValue,
            Alias = aliasValue,
            RemotePath = remotePath.Value,
            HierarchyDelimiter = remotePath.HierarchyDelimiter?.ToString(),
            MailboxAccount = null!,
        };

        await this.ProjectAsync(writeContext, placedAt, role, cancellationToken);

        return writeContext.ChangeTracker.HasChanges();
    }

    private async Task<MailFolderEntity> ProjectAsync(
        MailFathomDbContext writeContext,
        MailFolderBindingEntity binding,
        MailFolderSpecialUse? role,
        CancellationToken cancellationToken)
    {
        var accountValue = binding.MailboxAccountId;
        var liveFolders = await writeContext.MailFolders
            .Where(folder => folder.MailboxAccountId == accountValue && folder.ErasedAt == null)
            .ToListAsync(cancellationToken);
        var currentBindings = await writeContext.MailFolderBindings
            .AsNoTracking()
            .Where(candidate => candidate.MailboxAccountId == accountValue
                && candidate.ResolutionGeneration == writeContext.MailFolderBindings
                    .Where(other => other.MailboxAccountId == candidate.MailboxAccountId && other.Alias == candidate.Alias)
                    .Max(other => other.ResolutionGeneration))
            .Select(candidate => new { candidate.RemotePath, candidate.Alias })
            .ToArrayAsync(cancellationToken);
        var aliasesByRemotePath = currentBindings
            .GroupBy(candidate => candidate.RemotePath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(candidate => candidate.Alias).Order(StringComparer.Ordinal).First(),
                StringComparer.Ordinal);
        var knownFolderCount = liveFolders.Count;

        var folder = MailFolderProjection.Project(
            liveFolders,
            aliasesByRemotePath,
            binding,
            role,
            () => Guid.CreateVersion7(timeProvider.GetUtcNow()));

        writeContext.MailFolders.AddRange(liveFolders.Skip(knownFolderCount));

        return folder;
    }
}
