// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads;

/// <summary>Reads a page of conversations out of stored mail, newest first.</summary>
/// <remarks>
/// <para>
/// Four reads per page, none of them per row: the page itself, grouped and ordered by the database; the messages each
/// listed conversation is quoted by; who wrote into each; and the title a derivation stored for each. The first is the
/// one that grows with the mailbox, because it groups every readable message to find the newest conversations. A
/// stored newest arrival per conversation is the index that would bound it, and is worth adding once a measured page
/// says so.
/// </para>
/// <para>
/// A walk of the mail list grouped by conversation is one read more: the walk chooses the conversations from what the
/// filters let through, and a second grouping counts the chosen ones whole before the same three describe them.
/// </para>
/// <para>
/// Every message-reading clause composes <see cref="StoredEmailSelectionPredicate" /> and states no narrowing of its
/// own, and each lives in a member of this class rather than in the asynchronous read, for the reason
/// <see cref="StoredEmailThreadReader" /> gives: the architecture rule holding a mail read to the shared narrowing reads
/// the class, and a call made only inside an async body belongs to its state machine.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailThreadListReader(MailFathomDbContext dbContext) : IEmailThreadListReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ListedEmailThread>> ReadPageAsync(
        MailboxScope scope,
        IReadOnlySet<EmailAddress> readerAddresses,
        EmailThreadListPosition? continueAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(readerAddresses);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (scope.AccountIds.Count is 0)
        {
            return [];
        }

        var trashedFolderIds = await StoredTrashedFolders.ReadAsync(
            dbContext.MailFolders,
            [.. scope.AccountIds.Select(static accountId => accountId.Value)],
            cancellationToken);
        string[] readerForms = [.. readerAddresses.Select(static address => address.NormalizedAddress).Distinct(StringComparer.Ordinal)];

        var threads = await Page(dbContext.StoredEmails.AsNoTracking(), scope, trashedFolderIds, readerForms, continueAfter, limit)
            .ToArrayAsync(cancellationToken);

        return await this.DescribedAsync(threads, scope, readerForms, readerAddresses, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GroupedEmailThread>> ReadGroupedPageAsync(
        EmailTimelineFilter filter,
        IReadOnlySet<EmailAddress> readerAddresses,
        EmailTimelinePosition? continueAfter,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(readerAddresses);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var scope = filter.Selection.Scope;

        if (scope.AccountIds.Count is 0)
        {
            return [];
        }

        var trashedFolderIds = await StoredTrashedFolders.ReadAsync(
            dbContext.MailFolders,
            [.. scope.AccountIds.Select(static accountId => accountId.Value)],
            cancellationToken);
        string[] readerForms = [.. readerAddresses.Select(static address => address.NormalizedAddress).Distinct(StringComparer.Ordinal)];
        var emails = dbContext.StoredEmails.AsNoTracking();

        var grouped = await GroupedPage(emails, filter, trashedFolderIds, continueAfter, limit).ToArrayAsync(cancellationToken);

        if (grouped.Length is 0)
        {
            return [];
        }

        var counted = await Counted(emails, scope, [.. grouped.Select(static row => row.EmailThreadId)], readerForms)
            .ToDictionaryAsync(static row => row.EmailThreadId, cancellationToken);
        var described = (await this.DescribedAsync(
                [.. grouped.Where(row => counted.ContainsKey(row.EmailThreadId)).Select(row => counted[row.EmailThreadId])],
                scope,
                readerForms,
                readerAddresses,
                cancellationToken))
            .ToDictionary(static thread => thread.ThreadId.Value);

        return
        [
            .. grouped
                .Where(row => described.ContainsKey(row.EmailThreadId))
                .Select(row => new GroupedEmailThread(
                    described[row.EmailThreadId],
                    new EmailTimelinePosition(row.NewestArrival, StoredEmailId.Create(row.NewestEmailId)))),
        ];
    }

    /// <summary>Reads what each conversation of a page is quoted by, who wrote into it, and what it was called, in the page's order.</summary>
    /// <remarks>A conversation whose newest message has gone between the reads has nothing to quote and is left out.</remarks>
    private async Task<IReadOnlyList<ListedEmailThread>> DescribedAsync(
        StoredThreadListRow[] threads,
        MailboxScope scope,
        string[] readerForms,
        IReadOnlySet<EmailAddress> readerAddresses,
        CancellationToken cancellationToken)
    {
        if (threads.Length is 0)
        {
            return [];
        }

        var emails = dbContext.StoredEmails.AsNoTracking();
        Guid[] threadIds = [.. threads.Select(static thread => thread.EmailThreadId)];

        var quoted = await Quoted(emails, scope, threadIds, readerForms)
            .ToDictionaryAsync(static row => row.EmailThreadId, cancellationToken);
        var participants = (await Participants(emails, scope, threadIds).ToArrayAsync(cancellationToken))
            .ToLookup(static row => row.EmailThreadId);
        var titles = await Titles(dbContext.EmailThreadStates.AsNoTracking(), threadIds)
            .ToDictionaryAsync(static row => row.EmailThreadId, cancellationToken);

        return
        [
            .. threads
                .Where(thread => quoted.ContainsKey(thread.EmailThreadId))
                .Select(thread => Listed(
                    thread,
                    quoted[thread.EmailThreadId],
                    participants[thread.EmailThreadId],
                    titles.GetValueOrDefault(thread.EmailThreadId),
                    readerAddresses)),
        ];
    }

    /// <summary>Groups the readable mail by conversation and reads one page of the conversations still listed.</summary>
    /// <param name="emails">The stored mail.</param>
    /// <param name="scope">What the reader may read, junk included.</param>
    /// <param name="trashedFolderIds">The folders in Trash of every account in the scope.</param>
    /// <param name="readerForms">The comparison forms of the addresses the reader sends as.</param>
    /// <param name="continueAfter">The position the previous page ended on, or <see langword="null" />.</param>
    /// <param name="limit">How many conversations to read at most.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// The grouping, the keyset, and the order are applied to an anonymous projection and the row is constructed last,
    /// because the provider cannot reduce a member of a constructed row back to the grouped column it came from.
    /// </remarks>
    internal static IQueryable<StoredThreadListRow> Page(
        IQueryable<StoredEmailEntity> emails,
        MailboxScope scope,
        Guid[] trashedFolderIds,
        string[] readerForms,
        EmailThreadListPosition? continueAfter,
        int limit)
    {
        var readable = Readable(emails, scope);
        var kept = StoredEmailSelectionPredicate.WithoutMailJudgedSpam(readable, scope.JunkFolders)
            .Where(email => !trashedFolderIds.Contains(email.FolderId))
            .Select(static email => email.EmailThreadId);

        var conversations = readable
            .Where(email => kept.Contains(email.EmailThreadId))
            .GroupBy(static email => email.EmailThreadId!.Value)
            .Select(conversation => new
            {
                EmailThreadId = conversation.Key,
                NewestArrival = conversation.Max(email => email.ReceivedAt),
                MessageCount = conversation.Count(),
                UnreadCount = conversation.Count(email => !email.IsRemotelySeen
                    && (email.SenderNormalizedAddress == null || !readerForms.Contains(email.SenderNormalizedAddress))),
                AttachedCount = conversation.Count(email => email.AttachmentCount > 0),
            });

        if (continueAfter is { } boundary)
        {
            var boundaryId = boundary.ThreadId.Value;

            conversations = boundary.NewestArrival is { } arrival
                ? conversations.Where(conversation => conversation.NewestArrival == null
                    || conversation.NewestArrival < arrival
                    || (conversation.NewestArrival == arrival && conversation.EmailThreadId < boundaryId))
                : conversations.Where(conversation => conversation.NewestArrival == null
                    && conversation.EmailThreadId < boundaryId);
        }

        return conversations
            .OrderBy(static conversation => conversation.NewestArrival == null)
            .ThenByDescending(static conversation => conversation.NewestArrival)
            .ThenByDescending(static conversation => conversation.EmailThreadId)
            .Take(limit)
            .Select(static conversation => new StoredThreadListRow(
                conversation.EmailThreadId,
                conversation.NewestArrival,
                conversation.MessageCount,
                conversation.UnreadCount,
                conversation.AttachedCount > 0));
    }

    /// <summary>Groups the mail a filter lets through by conversation and reads one walk of the conversations it keeps listed.</summary>
    /// <param name="emails">The stored mail.</param>
    /// <param name="filter">The list's filters and account scope, junk included, and the direction the walk reads in.</param>
    /// <param name="trashedFolderIds">The folders in Trash of every account in the scope.</param>
    /// <param name="continueAfter">The position the walk continues beyond, or <see langword="null" />.</param>
    /// <param name="limit">How many conversations to read at most.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// <para>
    /// A conversation stands at its newest message that passes the filter and is neither judged spam nor in Trash, which
    /// is <see cref="StoredEmailSelectionPredicate.Matching" /> narrowed by the rule that keeps a conversation listed.
    /// The newest is the one the newest-first timeline reads first: dated before undated, then latest, then the highest
    /// identifier, so its arrival is the group's latest and its identifier breaks the tie. The keyset and the order are
    /// the timeline's own over that pair, which is what lets a grouped page carry a message list's cursor.
    /// </para>
    /// <para>
    /// It groups every message the filter lets through before the keyset narrows the groups, for the reason
    /// <see cref="Page" /> does, and the newest message of each group is picked by a correlated subquery that the
    /// statement repeats wherever the keyset and the order read it. A stored newest passing message per conversation
    /// cannot exist, since what passes depends on the filters; measuring a page against a large mailbox decides whether
    /// a window function ranking each group once is worth the raw SQL it would take.
    /// </para>
    /// </remarks>
    internal static IQueryable<StoredGroupedThreadRow> GroupedPage(
        IQueryable<StoredEmailEntity> emails,
        EmailTimelineFilter filter,
        Guid[] trashedFolderIds,
        EmailTimelinePosition? continueAfter,
        int limit)
    {
        var passing = StoredEmailSelectionPredicate.Matching(
            emails.Where(static email => email.EmailThreadId != null),
            filter.Selection);
        var conversations = StoredEmailSelectionPredicate.WithoutMailJudgedSpam(passing, filter.Selection.Scope.JunkFolders)
            .Where(email => !trashedFolderIds.Contains(email.FolderId))
            .GroupBy(static email => email.EmailThreadId!.Value)
            .Select(static conversation => new
            {
                EmailThreadId = conversation.Key,
                NewestArrival = conversation.Max(email => email.ReceivedAt),
                NewestEmailId = conversation
                    .OrderBy(email => email.ReceivedAt == null)
                    .ThenByDescending(email => email.ReceivedAt)
                    .ThenByDescending(email => email.Id)
                    .Select(email => email.Id)
                    .First(),
            });

        if (continueAfter is { } boundary)
        {
            var boundaryId = boundary.StoredEmailId.Value;

            conversations = (filter.Direction, boundary.ReceivedAt) switch
            {
                (EmailTimelineDirection.NewestFirst, { } arrival) => conversations.Where(conversation =>
                    conversation.NewestArrival == null
                    || conversation.NewestArrival < arrival
                    || (conversation.NewestArrival == arrival && conversation.NewestEmailId < boundaryId)),
                (EmailTimelineDirection.NewestFirst, null) => conversations.Where(conversation =>
                    conversation.NewestArrival == null && conversation.NewestEmailId < boundaryId),
                (EmailTimelineDirection.OldestFirst, { } arrival) => conversations.Where(conversation =>
                    conversation.NewestArrival != null
                    && (conversation.NewestArrival > arrival
                        || (conversation.NewestArrival == arrival && conversation.NewestEmailId > boundaryId))),
                _ => conversations.Where(conversation =>
                    conversation.NewestArrival != null || conversation.NewestEmailId > boundaryId),
            };
        }

        var ordered = filter.Direction is EmailTimelineDirection.NewestFirst
            ? conversations
                .OrderBy(static conversation => conversation.NewestArrival == null)
                .ThenByDescending(static conversation => conversation.NewestArrival)
                .ThenByDescending(static conversation => conversation.NewestEmailId)
            : conversations
                .OrderByDescending(static conversation => conversation.NewestArrival == null)
                .ThenBy(static conversation => conversation.NewestArrival)
                .ThenBy(static conversation => conversation.NewestEmailId);

        return ordered
            .Take(limit)
            .Select(static conversation => new StoredGroupedThreadRow(
                conversation.EmailThreadId,
                conversation.NewestArrival,
                conversation.NewestEmailId));
    }

    /// <summary>Counts each named conversation whole, over every message of it the scope admits.</summary>
    /// <remarks>The counts are <see cref="Page" />'s, read for conversations a grouped walk already chose rather than for a page this query orders.</remarks>
    internal static IQueryable<StoredThreadListRow> Counted(
        IQueryable<StoredEmailEntity> emails,
        MailboxScope scope,
        Guid[] threadIds,
        string[] readerForms) =>
        Readable(emails, scope)
            .Where(email => threadIds.Contains(email.EmailThreadId!.Value))
            .GroupBy(static email => email.EmailThreadId!.Value)
            .Select(conversation => new
            {
                EmailThreadId = conversation.Key,
                NewestArrival = conversation.Max(email => email.ReceivedAt),
                MessageCount = conversation.Count(),
                UnreadCount = conversation.Count(email => !email.IsRemotelySeen
                    && (email.SenderNormalizedAddress == null || !readerForms.Contains(email.SenderNormalizedAddress))),
                AttachedCount = conversation.Count(email => email.AttachmentCount > 0),
            })
            .Select(static conversation => new StoredThreadListRow(
                conversation.EmailThreadId,
                conversation.NewestArrival,
                conversation.MessageCount,
                conversation.UnreadCount,
                conversation.AttachedCount > 0));

    /// <summary>Reads the newest message of each listed conversation and the newest one somebody else sent.</summary>
    /// <remarks>Undated messages sort last, the way they do in the list, so a conversation is quoted by a message that carries a date wherever one does.</remarks>
    internal static IQueryable<StoredThreadQuotedRow> Quoted(
        IQueryable<StoredEmailEntity> emails,
        MailboxScope scope,
        Guid[] threadIds,
        string[] readerForms) =>
        Readable(emails, scope)
            .Where(email => threadIds.Contains(email.EmailThreadId!.Value))
            .GroupBy(static email => email.EmailThreadId!.Value)
            .Select(conversation => new StoredThreadQuotedRow(
                conversation.Key,
                conversation
                    .OrderBy(email => email.ReceivedAt == null)
                    .ThenByDescending(email => email.ReceivedAt)
                    .ThenByDescending(email => email.Id)
                    .Select(email => email.Id)
                    .First(),
                conversation
                    .Where(email => email.SenderNormalizedAddress == null || !readerForms.Contains(email.SenderNormalizedAddress))
                    .OrderBy(email => email.ReceivedAt == null)
                    .ThenByDescending(email => email.ReceivedAt)
                    .ThenByDescending(email => email.Id)
                    .Select(email => (Guid?)email.Id)
                    .FirstOrDefault()));

    /// <summary>Reads who wrote into each listed conversation, once per address, with the name they last wrote beside it.</summary>
    /// <remarks>
    /// Every author of the page is read and the bound is applied per conversation afterwards. A page of a hundred
    /// conversations from long mailing lists is the ceiling, and a window function ranking authors per conversation in
    /// the database is the step past it.
    /// </remarks>
    internal static IQueryable<StoredThreadParticipantRow> Participants(
        IQueryable<StoredEmailEntity> emails,
        MailboxScope scope,
        Guid[] threadIds) =>
        Readable(emails, scope)
            .Where(email => threadIds.Contains(email.EmailThreadId!.Value) && email.SenderNormalizedAddress != null)
            .GroupBy(static email => new { EmailThreadId = email.EmailThreadId!.Value, NormalizedAddress = email.SenderNormalizedAddress! })
            .Select(wrote => new StoredThreadParticipantRow(
                wrote.Key.EmailThreadId,
                wrote.Key.NormalizedAddress,
                wrote
                    .OrderBy(email => email.SentAt == null)
                    .ThenByDescending(email => email.SentAt)
                    .Select(email => email.SenderAddress ?? email.SenderNormalizedAddress!)
                    .First(),
                wrote
                    .Where(email => email.SenderDisplayName != null)
                    .OrderBy(email => email.SentAt == null)
                    .ThenByDescending(email => email.SentAt)
                    .Select(email => email.SenderDisplayName)
                    .FirstOrDefault(),
                wrote.Min(email => email.ReceivedAt)));

    /// <summary>Reads the title a derivation stored for each listed conversation, where one did.</summary>
    internal static IQueryable<StoredThreadTitleRow> Titles(
        IQueryable<EmailThreadStateEntity> states,
        Guid[] threadIds) =>
        states
            .Where(state => threadIds.Contains(state.EmailThreadId) && state.Topic != null)
            .Select(static state => new StoredThreadTitleRow(
                state.EmailThreadId,
                state.Topic!,
                state.Standing,
                state.DerivedFromMessageCount,
                state.DerivedFromLatestArrival));

    private static IQueryable<StoredEmailEntity> Readable(IQueryable<StoredEmailEntity> emails, MailboxScope scope) =>
        StoredEmailSelectionPredicate.WithinScope(emails.Where(static email => email.EmailThreadId != null), scope);

    private static ListedEmailThread Listed(
        StoredThreadListRow thread,
        StoredThreadQuotedRow quoted,
        IEnumerable<StoredThreadParticipantRow> participants,
        StoredThreadTitleRow? title,
        IReadOnlySet<EmailAddress> readerAddresses) => new()
        {
            ThreadId = EmailThreadId.Create(thread.EmailThreadId),
            NewestArrival = thread.NewestArrival,
            MessageCount = thread.MessageCount,
            UnreadCount = thread.UnreadCount,
            HasAttachments = thread.HasAttachments,
            NewestEmailId = StoredEmailId.Create(quoted.NewestEmailId),
            NewestReceivedEmailId = quoted.NewestReceivedEmailId is { } received ? StoredEmailId.Create(received) : null,
            Participants =
            [
                .. participants
                    .OrderBy(static participant => participant.FirstArrival is null)
                    .ThenBy(static participant => participant.FirstArrival)
                    .ThenBy(static participant => participant.NormalizedAddress, StringComparer.Ordinal)
                    .Take(IEmailThreadListReader.MaximumNamedParticipants + 1)
                    .Select(participant => new ListedThreadParticipant(
                        participant.Address,
                        participant.DisplayName,
                        readerAddresses.Any(address => string.Equals(address.NormalizedAddress, participant.NormalizedAddress, StringComparison.Ordinal)))),
            ],
            Title = title is null
                ? null
                : new ListedThreadTitle(
                    ThreadTitle.Create(title.Topic, title.Standing ?? string.Empty),
                    new ThreadStateRevision(title.DerivedFromMessageCount, title.DerivedFromLatestArrival)),
        };

    internal sealed record StoredThreadListRow(
        Guid EmailThreadId,
        DateTimeOffset? NewestArrival,
        int MessageCount,
        int UnreadCount,
        bool HasAttachments);

    internal sealed record StoredGroupedThreadRow(Guid EmailThreadId, DateTimeOffset? NewestArrival, Guid NewestEmailId);

    internal sealed record StoredThreadQuotedRow(Guid EmailThreadId, Guid NewestEmailId, Guid? NewestReceivedEmailId);

    internal sealed record StoredThreadParticipantRow(
        Guid EmailThreadId,
        string NormalizedAddress,
        string Address,
        string? DisplayName,
        DateTimeOffset? FirstArrival);

    internal sealed record StoredThreadTitleRow(
        Guid EmailThreadId,
        string Topic,
        string? Standing,
        int DerivedFromMessageCount,
        DateTimeOffset? DerivedFromLatestArrival);
}
