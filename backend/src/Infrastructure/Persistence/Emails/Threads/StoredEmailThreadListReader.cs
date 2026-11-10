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

/// <summary>Reads a walk of the mail list grouped by conversation out of stored mail.</summary>
/// <remarks>
/// <para>
/// Six reads per walk, none of them per row: the walk itself, which chooses the conversations from what the filters
/// let through and orders them in the database; a second grouping that counts the chosen ones whole; the messages each
/// listed conversation is quoted by; who wrote into each; the categories each holds; and the title a derivation stored for each. The first is the
/// one that grows with the mailbox, because it groups every message the filters let through.
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

    /// <summary>Reads what each conversation of a page is quoted by, who wrote into it, which categories it holds, and what it was called, in the page's order.</summary>
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
        var categories = (await Categories(emails, scope, threadIds).ToArrayAsync(cancellationToken))
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
                    categories[thread.EmailThreadId],
                    titles.GetValueOrDefault(thread.EmailThreadId),
                    readerAddresses)),
        ];
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
    /// It groups every message the filter lets through before the keyset narrows the groups, because the provider
    /// cannot reduce a member of a constructed row back to the grouped column it came from, and the newest message of each group is picked by a correlated subquery that the
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
    /// <remarks>
    /// A message counts as unread while the server has not seen it and somebody other than the reader sent it. The row
    /// is constructed from an anonymous projection, because the provider cannot reduce a member of a constructed row
    /// back to the grouped column it came from.
    /// </remarks>
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
                MessageCount = conversation.Count(),
                UnreadCount = conversation.Count(email => !email.IsMarkedSeen
                    && (email.SenderNormalizedAddress == null || !readerForms.Contains(email.SenderNormalizedAddress))),
                AttachedCount = conversation.Count(email => email.AttachmentCount > 0),
            })
            .Select(static conversation => new StoredThreadListRow(
                conversation.EmailThreadId,
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

    /// <summary>Reads every category the messages of each listed conversation hold, each once, by the rule its counts follow.</summary>
    /// <remarks>
    /// One read for the page, bounded by the page's length times what one account can hold: seven defaults and fifteen own
    /// categories, because an own category leaving the account's record takes its rows with it.
    /// </remarks>
    internal static IQueryable<StoredThreadCategoryRow> Categories(
        IQueryable<StoredEmailEntity> emails,
        MailboxScope scope,
        Guid[] threadIds) =>
        Readable(emails, scope)
            .Where(email => threadIds.Contains(email.EmailThreadId!.Value))
            .SelectMany(
                static email => email.Categories,
                static (email, held) => new { EmailThreadId = email.EmailThreadId!.Value, held.Category })
            .Distinct()
            .Select(static held => new StoredThreadCategoryRow(held.EmailThreadId, held.Category));

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
        IEnumerable<StoredThreadCategoryRow> categories,
        StoredThreadTitleRow? title,
        IReadOnlySet<EmailAddress> readerAddresses) => new()
        {
            ThreadId = EmailThreadId.Create(thread.EmailThreadId),
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
            Categories = [.. categories.Select(static held => held.Category)],
            Title = title is null
                ? null
                : new ListedThreadTitle(
                    ThreadTitle.Create(title.Topic, title.Standing ?? string.Empty),
                    new ThreadStateRevision(title.DerivedFromMessageCount, title.DerivedFromLatestArrival)),
        };

    internal sealed record StoredThreadListRow(
        Guid EmailThreadId,
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

    internal sealed record StoredThreadCategoryRow(Guid EmailThreadId, string Category);

    internal sealed record StoredThreadTitleRow(
        Guid EmailThreadId,
        string Topic,
        string? Standing,
        int DerivedFromMessageCount,
        DateTimeOffset? DerivedFromLatestArrival);
}
