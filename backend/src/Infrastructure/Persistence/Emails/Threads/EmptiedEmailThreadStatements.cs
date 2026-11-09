// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>Composes the two statements that remove a conversation no stored row belongs to any more.</summary>
/// <remarks>
/// <para>
/// Two statements because the second has to read what the first's lock waited out. Each runs under its own snapshot,
/// so a message whose placement committed before the lock is seen by the re-check, and one placed after it waits on the
/// key check, finds the conversation gone, and retries as a concurrency conflict from a fresh read.
/// </para>
/// <para>
/// A placement still in flight already holds a key-share lock on its conversation, which is what <c>SKIP LOCKED</c>
/// passes by. Without that the delete would wait for it and then set the arriving message's conversation to null.
/// </para>
/// </remarks>
internal static class EmptiedEmailThreadStatements
{
    /// <summary>Composes the statement that locks up to a limit of one account's emptied surviving conversations.</summary>
    /// <param name="model">The model the names are read from.</param>
    /// <returns>A statement over the account as <c>{0}</c> and the limit as <c>{1}</c>.</returns>
    internal static string LockEmptied(IModel model)
    {
        var (threads, threadKey, threadAccount, mergedInto, emails, emailThread) = Names(model);

        return $$"""
            SELECT thread.{{threadKey}} AS "Value"
            FROM {{threads}} AS thread
            WHERE thread.{{threadAccount}} = {0}
              AND thread.{{mergedInto}} IS NULL
              AND NOT EXISTS (SELECT 1 FROM {{emails}} AS email WHERE email.{{emailThread}} = thread.{{threadKey}})
            LIMIT {1}
            FOR UPDATE OF thread SKIP LOCKED
            """;
    }

    /// <summary>Composes the statement that removes the locked conversations still empty, each with what was folded into it.</summary>
    /// <param name="model">The model the names are read from.</param>
    /// <returns>A statement over the locked conversations as <c>{0}</c>, answering how many of them it removed.</returns>
    /// <remarks>
    /// The folded rows go in the same statement as their survivor because the key between them is checked when the
    /// statement ends. <c>UNION</c> rather than <c>UNION ALL</c> ends the walk on a chain that closed a cycle.
    /// </remarks>
    internal static string RemoveWithFolded(IModel model)
    {
        var (threads, threadKey, _, mergedInto, emails, emailThread) = Names(model);

        return $$"""
            WITH RECURSIVE folded (root, member) AS (
                SELECT thread.{{threadKey}}, thread.{{threadKey}}
                FROM {{threads}} AS thread
                WHERE thread.{{threadKey}} = ANY({0})
                UNION
                SELECT folded.root, thread.{{threadKey}}
                FROM {{threads}} AS thread
                JOIN folded ON thread.{{mergedInto}} = folded.member
            ),
            holding AS (
                SELECT DISTINCT folded.root
                FROM folded
                JOIN {{emails}} AS email ON email.{{emailThread}} = folded.member
            ),
            removed AS (
                DELETE FROM {{threads}} AS thread
                USING folded
                WHERE thread.{{threadKey}} = folded.member
                  AND NOT EXISTS (SELECT 1 FROM holding WHERE holding.root = folded.root)
                RETURNING folded.root
            )
            SELECT count(DISTINCT removed.root)::int AS "Value" FROM removed
            """;
    }

    private static (string Threads, string ThreadKey, string ThreadAccount, string MergedInto, string Emails, string EmailThread) Names(IModel model)
    {
        var thread = PersistedSchemaNames.EntityTypeOf<EmailThreadEntity>(model);
        var email = PersistedSchemaNames.EntityTypeOf<StoredEmailEntity>(model);

        return (
            PersistedSchemaNames.QuotedTable(thread),
            PersistedSchemaNames.QuotedColumn(thread, nameof(EmailThreadEntity.Id)),
            PersistedSchemaNames.QuotedColumn(thread, nameof(EmailThreadEntity.MailboxAccountId)),
            PersistedSchemaNames.QuotedColumn(thread, nameof(EmailThreadEntity.MergedIntoEmailThreadId)),
            PersistedSchemaNames.QuotedTable(email),
            PersistedSchemaNames.QuotedColumn(email, nameof(StoredEmailEntity.EmailThreadId)));
    }
}
