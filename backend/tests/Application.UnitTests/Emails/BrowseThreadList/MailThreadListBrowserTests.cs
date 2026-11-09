// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Categories;
using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.BrowseTimeline;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Observability;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.BrowseThreadList;

/// <summary>Covers the list a screen reading by thread is drawn from: how it is paged, who the reader is, and what each row quotes.</summary>
/// <remarks>
/// Which conversations are listed — the spam and Trash rule, the account narrowing in the query, the counts — is the
/// query's, and the integration suite holds it against PostgreSQL. What is decided here is what the use case does with
/// the rows it is handed.
/// </remarks>
public sealed class MailThreadListBrowserTests
{
    /// <summary>The literal the scanner in the guarded-egress test reports, standing in for a credential in mail.</summary>
    private const string Marker = "AKIAEXAMPLEKEY";

    private const string ReaderAddress = "me@example.test";

    private static readonly MailAccountId Account = MailAccountId.Create("personal");
    private static readonly MailAccountId SecondAccount = MailAccountId.Create("work");
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    /// <summary>Who the reader is decides what counts as received, so it is every address their accounts send as and nothing else.</summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_ARequestForEveryAccount_ReadsUnderEveryAccountAndTheAddressesTheyAllSendAs()
    {
        // Arrange
        var reader = ReaderAnswering([]);
        var browser = BrowserOver(reader, [], sendingAs: [(Account, ReaderAddress), (SecondAccount, "me@work.example.test")]);

        // Act
        await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var arguments = Assert.Single(reader.ReceivedCalls()).GetArguments();
        var filter = Assert.IsType<EmailTimelineFilter>(arguments[0]);
        var readerAddresses = Assert.IsAssignableFrom<IReadOnlySet<EmailAddress>>(arguments[1]);

        Assert.Equal([Account, SecondAccount], filter.Selection.Scope.AccountIds);
        Assert.Equal(
            [ReaderAddress, "me@work.example.test"],
            readerAddresses.Select(address => address.Address).Order(StringComparer.Ordinal));
    }

    /// <summary>A row quotes its newest message and draws its sender mark from the newest one somebody else sent, which are two messages once the reader has replied.</summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_AConversationTheReaderAnsweredLast_QuotesTheReplyAndMarksTheSenderOfTheMessageBeforeIt()
    {
        // Arrange
        var received = Summary(10, "anna@example.test") with
        {
            SenderVerification = SenderVerification.NotEstablished with { DeploymentTrust = SenderTrustLevel.Trusted },
        };
        var reply = Summary(11, ReaderAddress);
        var thread = Listed(1) with { NewestEmailId = reply.StoredEmailId, NewestReceivedEmailId = received.StoredEmailId };
        var browser = BrowserOver(ReaderAnswering([thread]), [], summaries: [received, reply]);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single(page.Conversations);

        Assert.True(row.NewestIsReaders);
        Assert.Equal(reply.StoredEmailId, row.Newest.StoredEmailId);
        Assert.Equal(SenderTrustLevel.Trusted, row.NewestReceivedSender?.DeploymentTrust);
    }

    [Fact]
    public async Task BrowseGroupedPageAsync_AConversationEndingOnAReceivedMessage_IsNotTheReadersAndCarriesItsPreview()
    {
        // Arrange
        var thread = Listed(1);
        var browser = BrowserOver(
            ReaderAnswering([thread]),
            [thread],
            previews: new InMemoryStoredEmailPreviews().With(thread.NewestEmailId, "Shall we meet on Tuesday?"));

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single(page.Conversations);

        Assert.False(row.NewestIsReaders);
        Assert.Equal("Shall we meet on Tuesday?", row.NewestPreview);
        Assert.NotNull(row.NewestReceivedSender);
    }

    [Fact]
    public async Task BrowseGroupedPageAsync_AConversationWrittenOnlyByTheReader_CarriesNoSenderMark()
    {
        // Arrange
        var own = Summary(10, ReaderAddress);
        var thread = Listed(1) with { NewestEmailId = own.StoredEmailId, NewestReceivedEmailId = null };
        var browser = BrowserOver(ReaderAnswering([thread]), [], summaries: [own]);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single(page.Conversations);

        Assert.True(row.NewestIsReaders);
        Assert.Null(row.NewestReceivedSender);
    }

    [Fact]
    public async Task BrowseGroupedPageAsync_MoreAuthorsThanARowNames_NamesTheFirstOnesAndSaysThereAreMore()
    {
        // Arrange
        var authors = Enumerable
            .Range(1, IEmailThreadListReader.MaximumNamedParticipants + 1)
            .Select(ordinal => new ListedThreadParticipant($"author{ordinal}@example.test", null, IsReader: false))
            .ToArray();
        var thread = Listed(1) with { Participants = authors };
        var browser = BrowserOver(ReaderAnswering([thread]), [thread]);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single(page.Conversations);

        Assert.Equal(
            authors.Take(IEmailThreadListReader.MaximumNamedParticipants).Select(author => author.Address),
            row.Thread.Participants.Select(participant => participant.Address));
        Assert.True(row.MoreParticipantsNotNamed);
    }

    /// <summary>A conversation whose newest message was erased between the two reads has nothing to quote, so it is left off the page rather than drawn empty.</summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_AConversationWhoseNewestMessageIsGone_IsLeftOffThePage()
    {
        // Arrange
        var kept = Listed(1);
        var gone = Listed(2);
        var browser = BrowserOver(ReaderAnswering([kept, gone]), [kept]);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([kept.ThreadId], page.Conversations.Select(row => row.Thread.ThreadId));
    }

    /// <summary>A conversation's set is what its account shows: a hidden default leaves it, and what stays stands in the account's order.</summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_AConversationHoldingAHiddenDefault_NamesOnlyWhatTheAccountShows()
    {
        // Arrange
        var thread = Listed(1) with { Categories = ["Legal", "travel", "finance"] };
        var offers = new StubMailCategories(
            new Dictionary<MailAccountId, MailCategoryOffer> { [Account] = MailCategoryOffer.Of(["travel"], ["Legal"]) },
            new Dictionary<StoredEmailId, IReadOnlyList<string>>());
        var browser = BrowserOver(ReaderAnswering([thread]), [thread], categoryOffers: offers);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["finance", "Legal"], Assert.Single(page.Conversations).Thread.Categories);
    }

    /// <summary>An account that does not categorize shows nothing, whatever was recorded while it did.</summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_AnAccountThatDoesNotCategorize_NamesNoCategory()
    {
        // Arrange
        var thread = Listed(1) with { Categories = ["finance"] };
        var browser = BrowserOver(ReaderAnswering([thread]), [thread]);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(Assert.Single(page.Conversations).Thread.Categories);
    }

    [Fact]
    public async Task BrowseGroupedPageAsync_WithTheEgressGuardOn_ServesNoDetectedValueInAnyQuotedText()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, TimeProvider.System);
        var newest = Summary(1, "anna@example.test") with
        {
            Subject = $"the key is {Marker}",
            SenderDisplayName = $"deploy bot {Marker}",
        };
        var thread = Listed(1) with
        {
            Participants = [new ListedThreadParticipant("anna@example.test", $"deploy bot {Marker}", IsReader: false)],
            Title = new ListedThreadTitle(
                ThreadTitle.Create($"Key {Marker}", $"Rotate {Marker}"),
                new ThreadStateRevision(1, null)),
        };
        var browser = BrowserOver(
            ReaderAnswering([thread]),
            [],
            summaries: [newest],
            previews: new InMemoryStoredEmailPreviews().With(thread.NewestEmailId, $"it reads {Marker} today"),
            egressGuard: egress.Guard);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single(page.Conversations);

        Assert.DoesNotContain(Marker, row.Newest.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, row.Newest.SenderDisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, row.NewestPreview, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, Assert.Single(row.Thread.Participants).DisplayName, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, row.Thread.Title!.Title.Topic, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, row.Thread.Title.Title.Standing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowseGroupedPageAsync_APageThatWasServed_ReportsTheListReadAndWhatItReturned()
    {
        // Arrange
        var readTelemetry = new RecordingMailboxReadTelemetry();
        var threads = Enumerable.Range(1, 2).Select(ordinal => Listed(ordinal)).ToArray();
        var browser = BrowserOver(ReaderAnswering(threads), threads, readTelemetry: readTelemetry);

        // Act
        await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.Single(readTelemetry.Reads);

        Assert.Equal(MailboxReadOperation.ListEmailThreads, read.Operation);
        Assert.Equal(2, read.ResultCount);
        Assert.True(read.WasClosed);
    }

    [Fact]
    public async Task BrowseGroupedPageAsync_ACallerWithoutTheMailReadGrant_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(
            ReaderAnswering([]),
            [],
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.AssistantUse));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    /// <summary>A grouped list pages in both directions from the cursors it issued, handing a backward page back in the sorted order.</summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_PagingForwardAndBack_VisitsTheSameConversationsInTheSortedOrder()
    {
        // Arrange
        var grouped = Enumerable.Range(1, 5).Select(Grouped).ToArray();
        var browser = BrowserOver(GroupedReaderAnswering(grouped), [.. grouped.Select(conversation => conversation.Thread)]);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var first = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest { PageSize = 2 }, cancellationToken);
        var second = await browser.BrowseGroupedPageAsync(
            new BrowseTimelineRequest { PageSize = 2, Cursor = first.NextCursor },
            cancellationToken);
        var backToFirst = await browser.BrowseGroupedPageAsync(
            new BrowseTimelineRequest { PageSize = 2, Cursor = second.PreviousCursor, PageDirection = TimelinePageDirection.Backward },
            cancellationToken);

        // Assert
        Assert.Equal([grouped[0].Thread.ThreadId, grouped[1].Thread.ThreadId], first.Conversations.Select(row => row.Thread.ThreadId));
        Assert.Null(first.PreviousCursor);
        Assert.Equal([grouped[2].Thread.ThreadId, grouped[3].Thread.ThreadId], second.Conversations.Select(row => row.Thread.ThreadId));
        Assert.NotNull(second.NextCursor);
        Assert.Equal(first.Conversations.Select(row => row.Thread.ThreadId), backToFirst.Conversations.Select(row => row.Thread.ThreadId));
        Assert.Null(backToFirst.PreviousCursor);
        Assert.NotNull(backToFirst.NextCursor);
    }

    /// <summary>The grouped read takes the list's filters and account as the message list does, with every folder and the junk folder in its scope.</summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_AnAccountAnAiFilterAndTheListsFilters_ReadUnderThoseFiltersAcrossEveryFolderOfThatAccount()
    {
        // Arrange
        var reader = GroupedReaderAnswering([]);
        var browser = BrowserOver(reader, []);
        var dueBefore = DateTimeOffset.Parse("2026-11-09T00:00:00Z", CultureInfo.InvariantCulture);

        // Act
        await browser.BrowseGroupedPageAsync(
            new BrowseTimelineRequest
            {
                Accounts = [MailAccountSelector.For(SecondAccount)],
                CarriesMarkOfAspect = EmailEnrichmentAspect.Commitment,
                MarkDueBefore = dueBefore,
                IsMarkedSeen = false,
                HasAttachments = true,
                Order = EmailTimelineDirection.OldestFirst,
            },
            TestContext.Current.CancellationToken);

        // Assert
        var filter = Assert.IsType<EmailTimelineFilter>(Assert.Single(reader.ReceivedCalls()).GetArguments()[0]);

        Assert.Equal([SecondAccount], filter.Selection.Scope.AccountIds);
        Assert.True(filter.Selection.Scope.IncludesJunkMail);
        Assert.Empty(filter.Selection.Scope.SelectedFolders);
        Assert.Equal(
            (EmailEnrichmentAspect.Commitment, (DateTimeOffset?)dueBefore, (bool?)false, (bool?)true, EmailTimelineDirection.OldestFirst),
            (filter.Selection.Mark?.Aspect, filter.Selection.Mark?.DueBefore, filter.Selection.IsMarkedSeen, filter.Selection.HasAttachments, filter.Direction));
    }

    /// <summary>A conversation has no folder, so a folder beside the grouping, or a request for junk mail, is refused before anything is read.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BrowseGroupedPageAsync_AFolderOrJunkMail_IsRefusedRatherThanIgnored(bool namesAFolder, bool includesJunk)
    {
        // Arrange
        var reader = GroupedReaderAnswering([]);
        var browser = BrowserOver(reader, []);

        // Act
        await Assert.ThrowsAsync<MailboxQueryFilterInvalidException>(() => browser.BrowseGroupedPageAsync(
            new BrowseTimelineRequest
            {
                Folders = namesAFolder ? [MailFolderReference.CreateNamedByCaller("role:Inbox")] : [],
                IncludeJunkMail = includesJunk,
            },
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(reader.ReceivedCalls());
    }

    /// <summary>
    /// A cursor from the message list read under the very scope the grouped list reads under still names a different
    /// list, so it is refused rather than read as a position among conversations.
    /// </summary>
    [Fact]
    public async Task BrowseGroupedPageAsync_ACursorFromTheMessageListUnderTheSameScope_IsRefused()
    {
        // Arrange
        var browser = BrowserOver(GroupedReaderAnswering([]), []);
        var messageList = MessageListOver([Summary(1, "anna@example.test"), Summary(2, "anna@example.test")]);
        var messages = await messageList.BrowsePageAsync(
            new BrowseTimelineRequest { PageSize = 1, IncludeJunkMail = true },
            TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<MailboxQueryCursorFilterMismatchException>(() => browser.BrowseGroupedPageAsync(
            new BrowseTimelineRequest { PageSize = 1, Cursor = messages.NextCursor },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BrowseGroupedPageAsync_ACallerAssignedNoAccount_AnswersAnEmptyPageWithoutReading()
    {
        // Arrange
        var reader = GroupedReaderAnswering([]);
        var browser = BrowserOver(reader, [], assignedAccounts: []);

        // Act
        var page = await browser.BrowseGroupedPageAsync(new BrowseTimelineRequest(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(page.Conversations);
        Assert.Equal((null, null), (page.NextCursor, page.PreviousCursor));
        Assert.Empty(reader.ReceivedCalls());
    }

    /// <summary>Answers each grouped walk from a list sorted newest first, read backwards for an oldest-first walk, beyond the position it continues from.</summary>
    private static IEmailThreadListReader GroupedReaderAnswering(IReadOnlyList<GroupedEmailThread> newestFirst)
    {
        var reader = Substitute.For<IEmailThreadListReader>();
        reader
            .ReadGroupedPageAsync(
                Arg.Any<EmailTimelineFilter>(),
                Arg.Any<IReadOnlySet<EmailAddress>>(),
                Arg.Any<EmailTimelinePosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var walk = call.ArgAt<EmailTimelineFilter>(0).Direction is EmailTimelineDirection.NewestFirst
                    ? newestFirst
                    : [.. newestFirst.Reverse()];
                var skipped = call.ArgAt<EmailTimelinePosition?>(2) is { } position
                    ? walk.TakeWhile(grouped => grouped.Position != position).Count() + 1
                    : 0;

                return Task.FromResult<IReadOnlyList<GroupedEmailThread>>([.. walk.Skip(skipped).Take(call.ArgAt<int>(3))]);
            });

        return reader;
    }

    /// <summary>Groups the numbered conversation, standing at its newest message.</summary>
    private static GroupedEmailThread Grouped(int ordinal) => Grouped(Listed(ordinal));

    /// <summary>Groups one conversation, standing at its newest message, which arrives earlier the higher its number.</summary>
    private static GroupedEmailThread Grouped(ListedEmailThread thread) => new(
        thread,
        new EmailTimelinePosition(
            DateTimeOffset.Parse("2026-10-30T12:00:00Z", null).AddHours(-OrdinalOf(thread.NewestEmailId)),
            thread.NewestEmailId));

    /// <summary>Builds the message list over the same caller and folders as <see cref="BrowserOver" />, answering every walk with the given messages.</summary>
    private static MailTimelineBrowser MessageListOver(IReadOnlyList<EmailSummary> messages)
    {
        var accountCatalog = Substitute.For<ICallerMailAccountCatalog>();
        accountCatalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns(
            [SyntheticServedAccount.Of(Account), SyntheticServedAccount.Of(SecondAccount)]);

        var timeline = Substitute.For<IStoredEmailTimelineReader>();
        timeline
            .ReadPageAsync(
                Arg.Any<EmailTimelineFilter>(),
                Arg.Any<EmailTimelinePosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(messages));

        var enrichments = Substitute.For<IStoredEmailEnrichmentReader>();
        enrichments.ReadEnrichmentsAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<StoredEmailId, EmailEnrichment>>(new Dictionary<StoredEmailId, EmailEnrichment>()));

        var participation = StubMailFolderParticipation.Mapping(
            new MailFolderIdentity(Account, Inbox),
            new MailFolderIdentity(SecondAccount, Inbox));

        return new MailTimelineBrowser(
            timeline,
            StubMailCategories.OfferingNothing,
            new InMemoryStoredEmailPreviews(),
            enrichments,
            Substitute.For<IEmailThreadReader>(),
            new MailboxScopeResolver(
                accountCatalog,
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            SensitiveContentEgressGuards.Inactive(),
            new RecordingMailboxReadTelemetry(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    /// <summary>Answers each grouped walk with the given conversations, newest first in the order given.</summary>
    private static IEmailThreadListReader ReaderAnswering(IReadOnlyList<ListedEmailThread> threads) =>
        GroupedReaderAnswering([.. threads.Select(Grouped)]);

    /// <summary>Lists one conversation whose newest message is a received one, numbered so the list order is the order of the numbers.</summary>
    private static ListedEmailThread Listed(int ordinal) => new()
    {
        ThreadId = EmailThreadId.Create(new Guid($"11111111-0000-0000-0000-{ordinal:D12}")),
        MessageCount = 2,
        UnreadCount = 1,
        HasAttachments = false,
        NewestEmailId = SummaryId(ordinal),
        NewestReceivedEmailId = SummaryId(ordinal),
        Participants = [new ListedThreadParticipant("anna@example.test", "Anna", IsReader: false)],
        Title = null,
        Categories = [],
    };

    private static StoredEmailId SummaryId(int ordinal) =>
        StoredEmailId.Create(new Guid($"00000000-0000-0000-0000-{ordinal:D12}"));

    private static EmailSummary Summary(int ordinal, string sender) => SyntheticEmailSummaries.Create(
        DateTimeOffset.Parse("2026-10-30T09:00:00Z", null).AddMinutes(ordinal),
        SummaryId(ordinal).Value,
        Account.Value,
        Inbox.Value,
        $"Message {ordinal}",
        sender);

    private static MailThreadListBrowser BrowserOver(
        IEmailThreadListReader reader,
        IReadOnlyList<ListedEmailThread> quotedThreads,
        IReadOnlyList<EmailSummary>? summaries = null,
        InMemoryStoredEmailPreviews? previews = null,
        IMailCategoryOfferReader? categoryOffers = null,
        IReadOnlyList<(MailAccountId Account, string Address)>? sendingAs = null,
        IReadOnlyList<MailAccountId>? assignedAccounts = null,
        SensitiveContentEgressGuard? egressGuard = null,
        IMailboxReadTelemetry? readTelemetry = null,
        AccessAuthorization? authorization = null)
    {
        var accountCatalog = Substitute.For<ICallerMailAccountCatalog>();
        accountCatalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            .. (assignedAccounts ?? [Account, SecondAccount])
                .OrderBy(accountId => accountId.Value, StringComparer.Ordinal)
                .Select(accountId => SyntheticServedAccount.Of(accountId)),
        ]);

        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        foreach (var (account, address) in sendingAs ?? [(Account, ReaderAddress)])
        {
            _ = EmailAddress.TryCreate(displayName: null, address, out var parsed);
            senderIdentities.FindSenderIdentity(account).Returns(OutgoingSenderIdentity.Create(account, parsed));
        }

        var participation = StubMailFolderParticipation.Mapping(
            new MailFolderIdentity(Account, Inbox),
            new MailFolderIdentity(SecondAccount, Inbox));

        return new MailThreadListBrowser(
            reader,
            new InMemoryStoredEmailSummaries().WithAll(
                summaries ?? [.. quotedThreads.Select(thread => Summary(OrdinalOf(thread.NewestEmailId), "anna@example.test"))]),
            previews ?? new InMemoryStoredEmailPreviews(),
            categoryOffers ?? new StubMailCategories(),
            accountCatalog,
            senderIdentities,
            new MailboxScopeResolver(
                accountCatalog,
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            egressGuard ?? SensitiveContentEgressGuards.Inactive(),
            readTelemetry ?? new RecordingMailboxReadTelemetry(),
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    private static int OrdinalOf(StoredEmailId storedEmailId) =>
        int.Parse(storedEmailId.Value.ToString("D")[^12..], CultureInfo.InvariantCulture);
}
