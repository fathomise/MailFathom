// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Linq.Expressions;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>States which stored rows a tombstone hides, once for every query that has to exclude one.</summary>
/// <remarks>
/// <para>
/// A row whose remote occurrence has gone, or which a delete that issued no expunge left flagged, is hidden by default
/// and readable only where the disappearance was observed under an account keeping what its server loses, so the rule
/// is several columns rather than one and no longer reads as an obvious null check at its call sites. Every
/// query that narrows to mail a reader may see composes this expression instead of restating it: a listing, a search, a
/// content read, and both backfills, which must not spend extraction or a provider's embeddings on mail nothing may
/// retrieve.
/// </para>
/// <para>
/// Written once because the two ways to get it wrong are opposite and both are silent. Repeating only the timestamp
/// check hides mail an operator configured
/// <see cref="RemotelyDeletedEmailDisposition.RetainLocalCopy" /> to keep; forgetting the check entirely serves mail the
/// server no longer holds and nobody agreed to keep.
/// </para>
/// </remarks>
internal static class StoredEmailTombstone
{
    /// <summary>Admits the rows that are still part of the local mailbox, which PostgreSQL evaluates in full.</summary>
    /// <remarks>
    /// <para>
    /// It is an expression rather than a method so it composes into a query as a predicate the provider translates. A
    /// helper called inside a lambda would either fail to translate or drag the rest of the pipeline into the process.
    /// </para>
    /// <para>
    /// A delete a mirrored account committed locally first hides its message at once through a column of the row
    /// itself, while the row, its payload, and everything derived from it stay until the server has removed the
    /// occurrence and the disposition the record carries takes over. It is a column rather than a test of the record
    /// so this stays a predicate over one row: the vector search composes it beneath an aggregate, and a correlated
    /// subplan there would keep the joins below it out of PostgreSQL's parallel workers.
    /// </para>
    /// </remarks>
    internal static Expression<Func<StoredEmailEntity, bool>> IsNotTombstoned { get; } =
        email => email.LocalDeleteCommittedAt == null
            && ((email.RemoteExpungeObservedAt == null && email.AuthoredDeleteFlaggedAt == null)
                || email.IsRetainedAfterRemoteDelete);

    /// <summary>Admits the rows that became a settled tombstone before a moment, which PostgreSQL evaluates in full.</summary>
    /// <param name="moment">The moment the row must have become one before.</param>
    /// <returns>The predicate.</returns>
    /// <remarks>
    /// A row is a tombstone from the moment the server was first seen to have removed or flagged it. A row only
    /// <see cref="StoredEmailEntity.LocalDeleteCommittedAt" /> hides is left out, because that delete has not settled and
    /// may still be withdrawn or given up. A flag always precedes the expunge that follows it, so the flag is the older
    /// of the two wherever both are set.
    /// </remarks>
    internal static Expression<Func<StoredEmailEntity, bool>> SettledBefore(DateTimeOffset moment) =>
        email => !email.IsRetainedAfterRemoteDelete
            && (email.AuthoredDeleteFlaggedAt ?? email.RemoteExpungeObservedAt) < moment;
}
