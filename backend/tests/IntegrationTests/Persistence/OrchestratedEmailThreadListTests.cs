// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the conversation list groups, filters, counts, and pages against a real PostgreSQL database.</summary>
/// <remarks>
/// <para>
/// Three claims only this suite can settle. Which conversations are listed is decided by a subquery over the spam
/// verdict and the folders in Trash, and a conversation whose every message is spam, in Trash, or some of each has to
/// be left out while one with a single ordinary message is listed whole. The counts — every message, and the unread
/// ones somebody else sent — are aggregates the provider has to translate. And the keyset over the newest arrival has
/// to visit each conversation once, in order, across pages.
/// </para>
/// <para>
/// The database is shared with every other class in the collection, so the walk reads every listed conversation and
/// the assertions are made over this class's own: six conversations seeded once under fixed identities and a block of
/// UIDs no other class uses. How the use case pages, narrows to an account, and quotes a row is decided without a
/// database and stays in the unit suite.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedEmailThreadListTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "thread-list";

    private const string TrashFolderAlias = "thread-list-trash";

    private const string ReaderAddress = "me@mailfathom.test";

    /// <summary>Small enough that this class's two listed conversations land on different pages.</summary>
    private const int PageSize = 1;

    private static readonly DateTimeOffset FirstReceivedAt = SyntheticEmail.ReceivedAt;

    private static readonly Guid Answered = Guid.Parse("0197a000-0000-7000-8000-000000002887");
    private static readonly Guid OneOrdinaryAmongSpam = Guid.Parse("0197a000-0000-7000-8000-000000002888");
    private static readonly Guid AllSpam = Guid.Parse("0197a000-0000-7000-8000-000000002889");
    private static readonly Guid SpamAndTrash = Guid.Parse("0197a000-0000-7000-8000-000000002890");
    private static readonly Guid TrashOnly = Guid.Parse("0197a000-0000-7000-8000-000000002891");
    private static readonly Guid AcrossFolders = Guid.Parse("0197a000-0000-7000-8000-000000002919");

    private static readonly Guid[] SeededThreads = [Answered, OneOrdinaryAmongSpam, AllSpam, SpamAndTrash, TrashOnly, AcrossFolders];

    /// <summary>The messages this class seeds, each into its own UID from a block no other class uses.</summary>
    private static readonly SeededMessage[] SeededMessages =
    [
        new(9100, Answered, FolderAlias, "anna@mailfathom.test", MinutesAfter: 60, Seen: false, Spam: false),
        new(9101, Answered, FolderAlias, ReaderAddress, MinutesAfter: 120, Seen: true, Spam: false),
        new(9102, OneOrdinaryAmongSpam, FolderAlias, "offer@mailfathom.test", MinutesAfter: 10, Seen: true, Spam: true),
        new(9103, OneOrdinaryAmongSpam, FolderAlias, "bob@mailfathom.test", MinutesAfter: 20, Seen: true, Spam: false),
        new(9104, AllSpam, FolderAlias, "offer@mailfathom.test", MinutesAfter: 30, Seen: false, Spam: true),
        new(9105, AllSpam, FolderAlias, "offer@mailfathom.test", MinutesAfter: 40, Seen: false, Spam: true),
        new(9106, SpamAndTrash, FolderAlias, "offer@mailfathom.test", MinutesAfter: 50, Seen: false, Spam: true),
        new(9107, SpamAndTrash, TrashFolderAlias, "carol@mailfathom.test", MinutesAfter: 55, Seen: true, Spam: false),
        new(9108, TrashOnly, TrashFolderAlias, "carol@mailfathom.test", MinutesAfter: 65, Seen: true, Spam: false),
        new(9109, AcrossFolders, FolderAlias, "bob@mailfathom.test", MinutesAfter: 70, Seen: true, Spam: false),
        new(9110, AcrossFolders, TrashFolderAlias, "carol@mailfathom.test", MinutesAfter: 80, Seen: false, Spam: false),
    ];

    [Fact]
    public async Task ReadPageAsync_PagingEveryConversation_ListsOnlyThoseWithAMessageNeitherSpamNorTrashedNewestFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await EnsureSeededAsync(services, cancellationToken);

        // Act
        var listed = await WalkEveryPageAsync(services, cancellationToken);

        // Assert
        var ours = listed.Where(thread => SeededThreads.Contains(thread.ThreadId.Value)).ToArray();

        Assert.Equal([Answered, AcrossFolders, OneOrdinaryAmongSpam], ours.Select(thread => thread.ThreadId.Value));
        Assert.Equal(listed.Count, listed.Select(thread => thread.ThreadId).Distinct().Count());
    }

    /// <summary>
    /// Grouped with no filter, the list holds the conversations the ungrouped list holds, each standing at its newest
    /// message that is neither spam nor in Trash, and a walk back from the far end visits them once in the reverse order.
    /// </summary>
    [Fact]
    public async Task ReadGroupedPageAsync_WalkingForwardThenBack_ListsEachListedConversationOnceAtItsNewestMessageOutsideSpamAndTrash()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stored = await EnsureSeededAsync(services, cancellationToken);

        // Act
        var forward = await WalkGroupedAsync(services, isRemotelySeen: null, EmailTimelineDirection.NewestFirst, from: null, cancellationToken);
        var backward = await WalkGroupedAsync(services, isRemotelySeen: null, EmailTimelineDirection.OldestFirst, forward[^1].Position, cancellationToken);

        // Assert
        var ours = forward.Where(grouped => SeededThreads.Contains(grouped.Thread.ThreadId.Value)).ToArray();

        Assert.Equal(
            [(Answered, stored[9101]), (AcrossFolders, stored[9109]), (OneOrdinaryAmongSpam, stored[9103])],
            ours.Select(grouped => (grouped.Thread.ThreadId.Value, grouped.Position.StoredEmailId)));
        Assert.Equal(forward.Count, forward.Select(grouped => grouped.Thread.ThreadId).Distinct().Count());
        Assert.Equal(
            forward.SkipLast(1).Reverse().Select(grouped => grouped.Thread.ThreadId),
            backward.Select(grouped => grouped.Thread.ThreadId));
    }

    /// <summary>A conversation spread across folders is positioned by what passed and described whole, its trashed newest message included.</summary>
    [Fact]
    public async Task ReadGroupedPageAsync_AConversationSpreadAcrossFolders_IsCountedAndQuotedWhole()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stored = await EnsureSeededAsync(services, cancellationToken);

        // Act
        var forward = await WalkGroupedAsync(services, isRemotelySeen: null, EmailTimelineDirection.NewestFirst, from: null, cancellationToken);

        // Assert
        var across = Assert.Single(forward, grouped => grouped.Thread.ThreadId.Value == AcrossFolders);

        Assert.Equal((2, stored[9110]), (across.Thread.MessageCount, across.Thread.NewestEmailId));
    }

    /// <summary>
    /// A filter narrows the grouped list to conversations with a message that passes it outside spam and Trash: an
    /// unread spam message and an unread trashed one keep nothing listed, and the listed one stands at the unread message.
    /// </summary>
    [Fact]
    public async Task ReadGroupedPageAsync_UnderTheUnreadFilter_ListsOnlyConversationsWithAnUnreadMessageOutsideSpamAndTrash()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stored = await EnsureSeededAsync(services, cancellationToken);

        // Act
        var unread = await WalkGroupedAsync(services, isRemotelySeen: false, EmailTimelineDirection.NewestFirst, from: null, cancellationToken);

        // Assert
        var ours = unread.Where(grouped => SeededThreads.Contains(grouped.Thread.ThreadId.Value)).ToArray();

        Assert.Equal(
            [(Answered, stored[9100], 2)],
            ours.Select(grouped => (grouped.Thread.ThreadId.Value, grouped.Position.StoredEmailId, grouped.Thread.MessageCount)));
    }

    /// <summary>A conversation is counted whole, Spam and Trash included, and only mail somebody else sent and nobody read is unread.</summary>
    [Fact]
    public async Task ReadPageAsync_ListedConversations_CountEveryMessageAndTheUnreadOnesTheReaderDidNotSend()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var stored = await EnsureSeededAsync(services, cancellationToken);

        // Act
        var listed = await WalkEveryPageAsync(services, cancellationToken);

        // Assert
        var answered = Assert.Single(listed, thread => thread.ThreadId.Value == Answered);
        var amongSpam = Assert.Single(listed, thread => thread.ThreadId.Value == OneOrdinaryAmongSpam);

        Assert.Equal((2, 1), (answered.MessageCount, answered.UnreadCount));
        Assert.Equal(stored[9101], answered.NewestEmailId);
        Assert.Equal(stored[9100], answered.NewestReceivedEmailId);
        Assert.Equal(
            [("anna@mailfathom.test", false), (ReaderAddress, true)],
            answered.Participants.Select(participant => (participant.Address, participant.IsReader)));

        Assert.Equal((2, 0), (amongSpam.MessageCount, amongSpam.UnreadCount));
        Assert.Equal(stored[9103], amongSpam.NewestEmailId);
    }

    private static async Task<IReadOnlyList<ListedEmailThread>> WalkEveryPageAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        _ = EmailAddress.TryCreate(displayName: null, ReaderAddress, out var reader);
        IReadOnlySet<EmailAddress> readerAddresses = new HashSet<EmailAddress> { reader };

        return await services.AsCallerInScopeAsync(
            async (scope, token) =>
            {
                var mailboxScope = await scope.GetRequiredService<MailboxScopeResolver>().ReadableScopeAsync(
                    MailboxReaderKind.Person,
                    [],
                    [],
                    JunkMailInclusion.Included,
                    token);
                var listReader = scope.GetRequiredService<IEmailThreadListReader>();
                List<ListedEmailThread> listed = [];
                EmailThreadListPosition? after = null;

                while (true)
                {
                    var page = await listReader.ReadPageAsync(mailboxScope, readerAddresses, after, PageSize, token);

                    if (page.Count is 0)
                    {
                        return (IReadOnlyList<ListedEmailThread>)listed;
                    }

                    listed.AddRange(page);
                    after = page[^1].Position;
                }
            },
            [MailFathomPermission.MailRead],
            cancellationToken);
    }

    /// <summary>Walks the grouped list one conversation at a time in one direction, from a position or from that direction's leading end.</summary>
    private static async Task<IReadOnlyList<GroupedEmailThread>> WalkGroupedAsync(
        OrchestratedMailFathomServices services,
        bool? isRemotelySeen,
        EmailTimelineDirection direction,
        EmailTimelinePosition? from,
        CancellationToken cancellationToken)
    {
        _ = EmailAddress.TryCreate(displayName: null, ReaderAddress, out var reader);
        IReadOnlySet<EmailAddress> readerAddresses = new HashSet<EmailAddress> { reader };

        return await services.AsCallerInScopeAsync(
            async (scope, token) =>
            {
                var mailboxScope = await scope.GetRequiredService<MailboxScopeResolver>().ReadableScopeAsync(
                    MailboxReaderKind.Person,
                    [],
                    [],
                    JunkMailInclusion.Included,
                    token);
                var filter = EmailTimelineFilter.Create(
                    mailboxScope,
                    senderAddress: null,
                    recipientAddress: null,
                    subjectFragment: null,
                    receivedOnOrAfter: null,
                    receivedBefore: null,
                    isRemotelySeen,
                    isRemotelyFlagged: null,
                    keyword: null,
                    hasAttachments: null,
                    direction);
                var listReader = scope.GetRequiredService<IEmailThreadListReader>();
                List<GroupedEmailThread> walked = [];
                var after = from;

                while (true)
                {
                    var page = await listReader.ReadGroupedPageAsync(filter, readerAddresses, after, PageSize, token);

                    if (page.Count is 0)
                    {
                        return (IReadOnlyList<GroupedEmailThread>)walked;
                    }

                    walked.AddRange(page);
                    after = page[^1].Position;
                }
            },
            [MailFathomPermission.MailRead],
            cancellationToken);
    }

    /// <summary>Writes the six conversations once and answers each seeded UID's stored identity.</summary>
    /// <remarks>
    /// The messages go through the production write path and settlement, so each is a row a deployment produces; what a
    /// synchronization run and the threading, spam, and folder work would decide afterwards is then stated directly —
    /// the conversation each belongs to, whether it was read, its verdict, and that its folder sits in Trash. Every step
    /// is idempotent, so whichever test runs first writes it and the other finds it.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<uint, StoredEmailId>> EnsureSeededAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken)
    {
        var bindings = new Dictionary<string, MailFolderResolution>
        {
            [FolderAlias] = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken),
            [TrashFolderAlias] = await OrchestratedFolderBinding.CommitAsync(services, TrashFolderAlias, cancellationToken),
        };
        Dictionary<uint, StoredEmailId> stored = [];

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var message in SeededMessages)
                {
                    var occurrenceId = SyntheticEmail.OccurrenceIn(bindings[message.Folder], message.Uid);
                    var subject = $"thread-list-{message.Uid}";

                    stored[message.Uid] = await repository.UpsertMetadataAsync(
                        session,
                        SyntheticEmail.RemoteMetadataOf(occurrenceId, subject, isRemotelySeen: message.Seen),
                        SyntheticEmail.ExtractionFrom(
                            occurrenceId,
                            subject,
                            SyntheticEmail.BodyTextContaining($"thread{message.Uid}", wordCount: 12),
                            message.Sender,
                            FirstReceivedAt.AddMinutes(message.MinutesAfter)),
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        await OrchestratedSettlement.ApplyAsync(services, [.. stored.Values], FirstReceivedAt, cancellationToken);
        await PlaceInConversationsAsync(services, stored, cancellationToken);
        await JudgeSpamAsync(services, stored, cancellationToken);
        await PutTrashFolderInTrashAsync(services, stored[SeededMessages.First(message => message.Folder == TrashFolderAlias).Uid], cancellationToken);

        return stored;
    }

    private static Task<bool> PlaceInConversationsAsync(
        OrchestratedMailFathomServices services,
        Dictionary<uint, StoredEmailId> stored,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            var present = await context.EmailThreads
                .Where(thread => SeededThreads.Contains(thread.Id))
                .Select(thread => thread.Id)
                .ToArrayAsync(token);

            context.EmailThreads.AddRange(SeededThreads.Except(present).Select(threadId => new EmailThreadEntity
            {
                Id = threadId,
                MailboxAccountId = SyntheticMailAccount.AccountId.Value,
                AssembledAt = FirstReceivedAt,
            }));
            await context.SaveChangesAsync(token);

            foreach (var message in SeededMessages)
            {
                var storedEmailId = stored[message.Uid].Value;

                await context.StoredEmails
                    .Where(email => email.Id == storedEmailId)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(email => email.EmailThreadId, message.Thread)
                            .SetProperty(email => email.IsRemotelySeen, message.Seen),
                        token);
            }

            return true;
        },
        cancellationToken);

    private static async Task JudgeSpamAsync(
        OrchestratedMailFathomServices services,
        Dictionary<uint, StoredEmailId> stored,
        CancellationToken cancellationToken)
    {
        foreach (var message in SeededMessages.Where(message => message.Spam))
        {
            await services.CommitProducingAsync(
                (scope, session, token) => scope.GetRequiredService<IEmailSpamClassificationStore>().SaveAsync(
                    session,
                    SpamClassification.Create(
                        stored[message.Uid],
                        SpamVerdict.Spam,
                        SpamClassificationStage.Deterministic,
                        assessment: null,
                        SpamClassificationProfile.Create(),
                        [],
                        FirstReceivedAt),
                    token),
                cancellationToken);
        }
    }

    /// <summary>Puts the class's Trash folder beneath the account's Trash, or makes it the Trash where the account has none yet.</summary>
    /// <remarks>
    /// An account holds one Trash, so whichever folder another class may already have given the role stays it; a folder
    /// beneath Trash is in Trash, which is the rule the list reads, so either arrangement exercises it.
    /// </remarks>
    private static Task<bool> PutTrashFolderInTrashAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId trashedEmail,
        CancellationToken cancellationToken) => services.InScopeAsync(
        async (scope, token) =>
        {
            var context = scope.GetRequiredService<MailFathomDbContext>();
            var trashedEmailId = trashedEmail.Value;
            var accountId = SyntheticMailAccount.AccountId.Value;
            var folderId = await context.StoredEmails
                .Where(email => email.Id == trashedEmailId)
                .Select(email => email.FolderId)
                .SingleAsync(token);
            var trash = await context.MailFolders
                .Where(folder => folder.MailboxAccountId == accountId
                    && folder.Role == MailFolderSpecialUse.Trash
                    && folder.ErasedAt == null)
                .Select(folder => (Guid?)folder.Id)
                .SingleOrDefaultAsync(token);

            if (trash is { } trashId && trashId != folderId)
            {
                await context.MailFolders
                    .Where(folder => folder.Id == folderId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(folder => folder.ParentId, trashId), token);
            }
            else if (trash is null)
            {
                await context.MailFolders
                    .Where(folder => folder.Id == folderId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(folder => folder.Role, MailFolderSpecialUse.Trash),
                        token);
            }

            return true;
        },
        cancellationToken);

    /// <summary>One message this class seeds: where it is, who sent it, and what was decided about it.</summary>
    private sealed record SeededMessage(
        uint Uid,
        Guid Thread,
        string Folder,
        string Sender,
        int MinutesAfter,
        bool Seen,
        bool Spam);
}
