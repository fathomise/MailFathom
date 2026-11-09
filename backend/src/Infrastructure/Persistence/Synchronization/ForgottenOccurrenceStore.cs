// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Synchronization;

/// <summary>EF Core state for the occurrences a delete left on their server and took out of MailFathom for good.</summary>
/// <param name="readContext">The scoped context reads go through.</param>
[RequiresIntegrationCoverage]
internal sealed class ForgottenOccurrenceStore(MailFathomDbContext readContext) : IForgottenOccurrenceStore
{
    /// <inheritdoc />
    public async Task<IReadOnlySet<ImapUid>> ReadForgottenUidsAsync(
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        if (uids.Count == 0)
        {
            return new HashSet<ImapUid>();
        }

        var accountValue = account.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;
        uint[] uidValues = [.. uids.Select(static uid => uid.Value)];

        var forgotten = await readContext.MailboxForgottenOccurrences
            .AsNoTracking()
            .Where(occurrence => occurrence.MailboxAccountId == accountValue
                && readContext.MailFolderBindings.Any(folder => folder.Id == occurrence.MailFolderBindingId
                    && folder.Alias == alias
                    && folder.ResolutionGeneration == generation)
                && occurrence.UidValidity == uidValidityValue
                && uidValues.Contains(occurrence.Uid))
            .Select(static occurrence => occurrence.Uid)
            .ToArrayAsync(cancellationToken);

        return forgotten.Select(ImapUid.Create).ToHashSet();
    }

    /// <inheritdoc />
    public async Task RetireOtherUidValiditiesAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        CancellationToken cancellationToken)
    {
        var sessionContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var accountValue = account.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;

        await sessionContext.MailboxForgottenOccurrences
            .Where(occurrence => occurrence.MailboxAccountId == accountValue
                && sessionContext.MailFolderBindings.Any(folder => folder.Id == occurrence.MailFolderBindingId
                    && folder.Alias == alias
                    && folder.ResolutionGeneration == generation)
                && occurrence.UidValidity != uidValidityValue)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>Erases what an account remembers of the folders an erasure took, under every binding each alias has had.</summary>
    /// <param name="sessionContext">The context the erasure is committing through.</param>
    /// <param name="accountValue">The account the folders belong to.</param>
    /// <param name="aliases">The erased folders' aliases, which may be none.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the removal is done.</returns>
    internal static async Task EraseOfFoldersAsync(
        MailFathomDbContext sessionContext,
        string accountValue,
        string[] aliases,
        CancellationToken cancellationToken)
    {
        if (aliases.Length == 0)
        {
            return;
        }

        await sessionContext.MailboxForgottenOccurrences
            .Where(occurrence => occurrence.MailboxAccountId == accountValue
                && sessionContext.MailFolderBindings.Any(folder => folder.Id == occurrence.MailFolderBindingId
                    && aliases.Contains(folder.Alias)))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
