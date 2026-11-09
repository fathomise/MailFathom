// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Emails;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Erases a stored message the server will never hold as that row.</summary>
/// <remarks>
/// A copy, or the destination of a move across accounts, is written as a second stored message before the server is
/// told to make it, so a change given up, or one whose occurrence the server never named, leaves a row the mailbox does
/// not hold as such. A move the server finished without naming where it put the message leaves one too, at an occurrence
/// the message has already left, with the destination's own discovery standing for it. Neither row answers for anything
/// still on the source, so no source removal is owed for it; the cascade every stored message's erasure runs takes its
/// derived rows, and its content objects are released in the same transaction.
/// </remarks>
[RequiresIntegrationCoverage]
internal static class LocalCopyErasure
{
    /// <summary>Stages the erasure of one such row in the caller's transaction, where it still exists.</summary>
    /// <param name="session">The transaction the erasure commits in.</param>
    /// <param name="writeContext">The context that session enlisted.</param>
    /// <param name="storedEmailId">The row to erase.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns><see langword="true" /> when a row was staged for erasure; <see langword="false" /> when none stood.</returns>
    public static async Task<bool> EraseAsync(
        IPersistenceSession session,
        MailFathomDbContext writeContext,
        Guid storedEmailId,
        CancellationToken cancellationToken)
    {
        if (await writeContext.StoredEmails.FindAsync([storedEmailId], cancellationToken) is not { } row)
        {
            return false;
        }

        Guid[] removedIds = [row.Id];

        await AccountStoredContentLedger.RemoveAsync(writeContext, removedIds, cancellationToken);
        await ReleasedContentObjects.ReleaseForStoredEmailsAsync(session, removedIds, cancellationToken);
        writeContext.StoredEmails.Remove(row);

        return true;
    }
}
