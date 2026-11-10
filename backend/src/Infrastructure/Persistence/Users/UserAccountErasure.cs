// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Filing;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Removes one user and everything recorded about them, and the mail of an account erased on purpose.</summary>
/// <remarks>
/// <para>
/// The mail graph belongs to the account rather than to the user, so the two are removed separately and never one as a
/// side effect of the other. The user's own record goes with the user — their credentials, their contacts, their
/// preferences, their sessions, their assignments, and the drafts and recurring sends they authored in any account. A
/// mailbox is never among them, the one this user alone was assigned included: it stays with its mail, assigned to
/// nobody and out of every flow, until somebody is assigned it again or an administrator erases it, which is
/// <see cref="EraseMailOfAccountAsync" />.
/// </para>
/// <para>
/// Most of each removal is the schema's. The mail graph hangs off <c>mailbox_accounts</c>, so deleting that row takes
/// the account's folders, the mail beneath them, and everything derived from that mail — contents, search documents,
/// chunks, vectors, classifications, threads, mutations, rule executions, audited citations, and queued work —
/// without anything here naming one of them. The user's own subtree cascades from the user row in the same way.
/// </para>
/// <para>
/// What neither cascade reaches is the tables that record a mail account as a plain identifier with no foreign key
/// onto one. Those are the seam's own work, and they are enumerated from the model rather than from a list somebody
/// maintains: a table added later that names an account without keying onto one is discharged by the same walk on the
/// day it appears, instead of being remembered about after an erasure request has already been answered. Each of
/// those statements narrows on the account, which is what the account-keyed graph bought — a row recorded against an
/// account that was authorized and has never synchronized, a sealed refresh token being the one that occurs, carries
/// the account it was written for and is reached by the same statement.
/// </para>
/// <para>
/// Erasing a user needs nothing stopped first. Every row it takes keys onto the user, and a writer keyed to a mail
/// account — a synchronization run, a job — writes the mailbox's rows rather than the person's, which stay. The rows
/// the person authored are taken twice, once before the user row is deleted and once after: deleting that row is what
/// refuses any further write keyed onto it, so the second pass reaches what a writer committed while the first was
/// running.
/// </para>
/// <para>
/// The contact book is two books, and each half is reached by a cascade from its own holder rather than by the walk.
/// What the user wrote down keys onto the user row and goes when the user does; what a mailbox collected keys onto
/// <c>mailbox_accounts</c>, beside that account's folders, threads, and jobs, and so goes only with the account.
/// <c>contact_addresses</c> is reached either way in a second hop, keying onto <c>contacts</c> through
/// <c>(ContactId, BookHolderId)</c>, so the addresses go with the person whichever book the person was in. That split
/// is what an erasure request owes about a book of third parties: the records this user assembled leave with them,
/// and the records a mailbox assembled stay with the mailbox.
/// </para>
/// </remarks>
internal static class UserAccountErasure
{
    private const string AccountIdentifierPropertyName = nameof(MailFolderBindingEntity.MailboxAccountId);

    private const string UserPropertyName = nameof(MailDraftEntity.UserId);

    /// <summary>Erases one user and everything recorded about them, leaving every mail account they were assigned in place.</summary>
    /// <param name="session">The transaction the whole erasure runs in, so a partial one is never committed.</param>
    /// <param name="userId">The user to remove.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What was removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    [RequiresIntegrationCoverage]
    public static async Task<UserErasure> EraseAsync(
        IPersistenceSession session,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The user row is held for the rest of the transaction, so two erasures of one user are serialized, and a
        // write that keys onto that row — an assignment naming it — waits on the foreign-key check until this
        // transaction ends and is then refused against a row that is gone.
        await writeContext.Database
            .SqlQueryRaw<Guid>(UserRowLockStatement(writeContext.Model), userId)
            .ToListAsync(cancellationToken);

        // Read before anything is deleted, because everything below reaches the payload rows by cascade and a cascade
        // removes the only pointer to an object without any application code seeing it. An erasure answered to a data
        // subject has to be true of the bucket as well as of the database, so this is the one deletion path that may
        // not leave its objects to the sweep.
        await ReleasedContentObjects.ReleaseForUserAsync(session, userId, cancellationToken);

        var rowsErasedBesideTheCascade = await EraseAuthoredRowsAsync(writeContext, userId, cancellationToken);

        // The assignments go with the user row by cascade, and an account it leaves assigned to nobody stays whole.
        var erasedUsers = await writeContext.UserAccounts
            .Where(user => user.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);

        rowsErasedBesideTheCascade += await EraseAuthoredRowsAsync(writeContext, userId, cancellationToken);

        return new UserErasure(erasedUsers > 0, rowsErasedBesideTheCascade);
    }

    /// <summary>Erases everything stored for one mail account.</summary>
    /// <param name="session">The transaction the erasure runs in.</param>
    /// <param name="accountId">The account, as the text every mail row names it by.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many rows the statements removed beside the cascade.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is blank.</exception>
    /// <remarks>
    /// Narrowed on the account column alone, and ended by deleting the account's <c>mailbox_accounts</c> row so the
    /// cascade takes the folders and everything beneath them. No user narrows it: one mailbox is one copy of the mail
    /// however many people are assigned it, so erasing the mailbox erases that copy for all of them. The record of the
    /// account itself is the caller's to delete, because whether the account is going away at all is the caller's
    /// question rather than this walk's.
    /// </remarks>
    [RequiresIntegrationCoverage]
    public static async Task<int> EraseMailOfAccountAsync(
        IPersistenceSession session,
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await ReleasedContentObjects.ReleaseForMailAccountAsync(session, accountId, cancellationToken);

        var erased = 0;
        var mailboxAccounts = MailboxAccountEntityTypeOf(writeContext.Model);
        var tables = TablesTheCascadeDoesNotReach(writeContext.Model).Append(mailboxAccounts);

        foreach (var entityType in tables)
        {
            var accountColumn = entityType == mailboxAccounts
                ? QuotedColumn(entityType, nameof(MailboxAccountEntity.Id))
                : QuotedColumn(entityType, AccountIdentifierPropertyName);

            // Every identifier in it is one the model supplied, and the value is a parameter.
            var statement = $$"""
                DELETE FROM {{QuotedTable(entityType)}}
                WHERE {{accountColumn}} = {0}
                """;

            erased += await writeContext.Database.ExecuteSqlRawAsync(
                statement,
                [accountId],
                cancellationToken);
        }

        erased += await WithdrawUnlandedFilingsIntoAsync(writeContext, accountId, cancellationToken);

        // A filing names two accounts and neither by the column the walk above reads, so it goes when either does.
        erased += await writeContext.CrossAccountFilings
            .Where(filing => filing.DestinationAccountId == accountId || filing.SourceAccountId == accountId)
            .ExecuteDeleteAsync(cancellationToken);

        return erased;
    }

    /// <summary>Takes back the source's half of every filing into the account that has not landed, where the source outlives it.</summary>
    /// <returns>How many removals were withdrawn.</returns>
    /// <remarks>
    /// Such a filing is what holds its source's delete back, and the destination's copy is going with this account's
    /// mail. Deleting the filing alone would free that delete to be issued against the only copy left. So the delete,
    /// which nothing has issued yet, goes with the filing, and the source shows again where it was — the same end a filing
    /// given up reaches, which leaves the message in one account rather than in none.
    /// </remarks>
    private static async Task<int> WithdrawUnlandedFilingsIntoAsync(
        MailFathomDbContext writeContext,
        string accountId,
        CancellationToken cancellationToken)
    {
        var unlanded = writeContext.CrossAccountFilings.Where(filing =>
            filing.DestinationAccountId == accountId &&
            filing.SourceAccountId != accountId &&
            (filing.Stage == CrossAccountFilingStage.Recorded || filing.Stage == CrossAccountFilingStage.AppendIssued));

        var withdrawn = await writeContext.MailboxMutations
            .Where(mutation => mutation.Stage == MailboxMutationStage.Recorded &&
                unlanded.Any(filing => filing.RemovalMutationId == mutation.Id))
            .ExecuteDeleteAsync(cancellationToken);

        var delete = MailboxMutation.Delete.Name;

        await writeContext.StoredEmails
            .Where(source => source.LocalDeleteCommittedAt != null &&
                unlanded.Any(filing => filing.SourceStoredEmailId == source.Id) &&
                !writeContext.MailboxMutations.Any(mutation =>
                    mutation.StoredEmailId == source.Id &&
                    mutation.Mutation == delete &&
                    mutation.Stage != MailboxMutationStage.Completed &&
                    mutation.Stage != MailboxMutationStage.Abandoned &&
                    mutation.Stage != MailboxMutationStage.Cancelled))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(source => source.LocalDeleteCommittedAt, (DateTimeOffset?)null),
                cancellationToken);

        return withdrawn;
    }

    /// <summary>Deletes what one user authored in one account, leaving the mailbox and everybody else's rows whole.</summary>
    /// <param name="session">The transaction the removal runs in.</param>
    /// <param name="accountId">The account, as the text every mail row names it by.</param>
    /// <param name="userId">The user whose authored rows go.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>How many rows the statements removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is blank.</exception>
    /// <remarks>
    /// What an unassignment owes. A draft is read only by its author among the account's assigned users and a
    /// recurring send is stopped only by the person who declared it, so both become unreadable by anybody the moment
    /// that person is no longer assigned — and a standing instruction left running would go on sending from a mailbox
    /// its author cannot see. The mail itself is untouched, because it is the mailbox's rather than theirs.
    /// </remarks>
    [RequiresIntegrationCoverage]
    public static async Task<int> EraseAuthoredRowsOfAccountAsync(
        IPersistenceSession session,
        string accountId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var erased = 0;

        foreach (var entityType in TablesNamingTheirAuthor(writeContext.Model))
        {
            // Everything in it is either a parameter or an identifier the model itself supplied.
            var statement = $$"""
                DELETE FROM {{QuotedTable(entityType)}}
                WHERE {{QuotedColumn(entityType, AccountIdentifierPropertyName)}} = {0}
                  AND {{QuotedColumn(entityType, UserPropertyName)}} = {1}
                """;

            erased += await writeContext.Database.ExecuteSqlRawAsync(
                statement,
                [accountId, userId],
                cancellationToken);
        }

        return erased;
    }

    /// <summary>Names the two tables that keep their author, which an erasure takes by the user rather than by the account.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>The entity types recording an act one person took: their drafts and their recurring sends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Named rather than derived, and that is the point: <c>outgoing_emails</c> carries an author too and is
    /// deliberately absent, because a submitted message belongs to the mailbox like the sent mail it becomes and
    /// erasing whoever asked for it may not withdraw it from everybody else assigned that mailbox. A rule reading
    /// "every table with both columns" would take it, which is why this list is two names a reader can check against
    /// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>.
    /// </remarks>
    internal static IReadOnlyList<IEntityType> TablesNamingTheirAuthor(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return
        [
            PersistedSchemaNames.EntityTypeOf<MailDraftEntity>(model),
            PersistedSchemaNames.EntityTypeOf<RecurringSendEntity>(model),
        ];
    }

    /// <summary>Names the tables that record a mail account and that deleting that account's row would leave behind.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>
    /// The entity types the erasure has to take itself: each names a mail account, none is reached by a cascade from
    /// <c>mailbox_accounts</c>, and none is reached by a cascade from another of them either — so the list is the
    /// smallest set of statements that discharges all of them.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Derived from the model because that is the one description of the schema that cannot be out of date with it. The
    /// unit test over this walk is what keeps the answer readable: it states the tables by name, so a table entering or
    /// leaving the list is a diff somebody reviews rather than a silent change in what an erasure reaches.
    /// </remarks>
    internal static IReadOnlyList<IEntityType> TablesTheCascadeDoesNotReach(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var reachedFromTheAccount = CascadeClosureOf([MailboxAccountEntityTypeOf(model)]);

        IEntityType[] namingAnAccount =
        [
            .. model.GetEntityTypes()
                .Where(entityType => entityType.FindProperty(AccountIdentifierPropertyName) is not null)
                .Where(entityType => !reachedFromTheAccount.Contains(entityType))
                .OrderBy(entityType => entityType.GetTableName(), StringComparer.Ordinal),
        ];

        var reachedFromEachOther = namingAnAccount
            .SelectMany(candidate => CascadeClosureOf([candidate]).Where(reached => reached != candidate))
            .ToHashSet();

        return [.. namingAnAccount.Where(candidate => !reachedFromEachOther.Contains(candidate))];
    }

    /// <summary>Deletes the rows one user authored, in every account they were assigned.</summary>
    /// <returns>How many rows the pass removed.</returns>
    private static async Task<int> EraseAuthoredRowsAsync(
        MailFathomDbContext writeContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var erased = 0;

        foreach (var entityType in TablesNamingTheirAuthor(writeContext.Model))
        {
            // A draft is what one person is writing and a recurring send is a standing instruction one person gave, so
            // both go with that person even where the mailbox stays. Everything in the statement is either a parameter
            // or an identifier the model itself supplied.
            var statement = $$"""
                DELETE FROM {{QuotedTable(entityType)}}
                WHERE {{QuotedColumn(entityType, UserPropertyName)}} = {0}
                """;

            erased += await writeContext.Database.ExecuteSqlRawAsync(statement, [userId], cancellationToken);
        }

        // A change to a mailbox's rules is the mailbox's record rather than the person's, so its row stays and only
        // stops saying who made the change.
        erased += await writeContext.MailRuleSetRevisions
            .Where(revision => revision.ActorUserId == userId)
            .ExecuteUpdateAsync(revision => revision.SetProperty(row => row.ActorUserId, (Guid?)null), cancellationToken);

        return erased;
    }

    /// <summary>Walks the tables a delete of <paramref name="roots" /> reaches through cascading foreign keys.</summary>
    private static HashSet<IEntityType> CascadeClosureOf(IEnumerable<IEntityType> roots)
    {
        var reached = new HashSet<IEntityType>(roots);
        var pending = new Queue<IEntityType>(reached);

        // A walk over a graph rather than work over a sequence, which is why it is a loop: each table reached opens
        // the tables that cascade from it, and a cycle would otherwise be walked forever.
        while (pending.TryDequeue(out var principal))
        {
            var dependents = principal.GetReferencingForeignKeys()
                .Where(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Cascade)
                .Select(foreignKey => foreignKey.DeclaringEntityType);

            foreach (var dependent in dependents)
            {
                if (reached.Add(dependent))
                {
                    pending.Enqueue(dependent);
                }
            }
        }

        return reached;
    }

    /// <summary>The statement that holds one user's row for the rest of the transaction.</summary>
    private static string UserRowLockStatement(IModel model)
    {
        var userEntityType = PersistedSchemaNames.EntityTypeOf<UserAccountEntity>(model);

        var userKeyColumn = QuotedColumn(userEntityType, nameof(UserAccountEntity.Id));

        return $$"""
            SELECT {{userKeyColumn}} AS "Value" FROM {{QuotedTable(userEntityType)}}
            WHERE {{userKeyColumn}} = {0}
            FOR UPDATE
            """;
    }

    private static IEntityType MailboxAccountEntityTypeOf(IModel model) =>
        PersistedSchemaNames.EntityTypeOf<MailboxAccountEntity>(model);

    /// <summary>Quotes a table name the model states, which is where every identifier in the statement comes from.</summary>
    private static string QuotedTable(IEntityType entityType) => PersistedSchemaNames.QuotedTable(entityType);

    /// <summary>Quotes the column one mapped property is stored in.</summary>
    private static string QuotedColumn(IEntityType entityType, string propertyName) =>
        PersistedSchemaNames.QuotedColumn(entityType, propertyName);
}
