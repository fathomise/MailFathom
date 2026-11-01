// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the conversation list route refuses and what it puts on the wire.</summary>
/// <remarks>
/// Which conversations are listed, how a page is cut, and who the reader is are covered where they are decided. What
/// is asserted here is the transport: that every mistake a request can carry is a <c>400</c> rather than a fault, and
/// that each field of a row reaches the wire as the field it is — the mapping is positional, so two strings or two
/// flags swapped would otherwise compile and pass.
/// </remarks>
public sealed class ClientMailThreadListEndpointTests
{
    private const string ReaderAddress = "me@example.test";

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailFolderId Inbox = MailFolderId.Create(Guid.Parse("83ecb521-bf32-7f67-8287-e8b923809bb3"));

    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly EmailThreadId Answered = EmailThreadId.Create(new Guid("11111111-1111-1111-1111-111111111111"));

    private static readonly EmailThreadId Unanswered = EmailThreadId.Create(new Guid("22222222-2222-2222-2222-222222222222"));

    private static readonly StoredEmailId ReadersReply = StoredEmailId.Create(new Guid("33333333-3333-3333-3333-333333333333"));

    private static readonly StoredEmailId AnnasMessage = StoredEmailId.Create(new Guid("44444444-4444-4444-4444-444444444444"));

    private static readonly StoredEmailId ReadersNote = StoredEmailId.Create(new Guid("55555555-5555-5555-5555-555555555555"));

    private readonly IEmailThreadListReader listReader = Substitute.For<IEmailThreadListReader>();

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailThreadListRoute_IsThePathAClientComposes() =>
        Assert.Equal("/threads", ClientMailThreadListEndpoint.MailThreadListRoute);

    [Theory]
    [InlineData("work", null, 0)]
    [InlineData("work", null, MailboxQueryPageSize.MaximumValue + 1)]
    [InlineData("work", "not-a-cursor", null)]
    [InlineData("elsewhere", null, null)]
    [InlineData("work\u0001", null, null)]
    public async Task ReadThreadsAsync_ARequestCarryingAMistake_IsRefusedAsABadRequest(
        string account,
        string? cursor,
        int? pageSize)
    {
        // Arrange
        this.Listing();

        // Act
        var result = await this.ReadAsync(account, pageSize, cursor);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>A cursor is part of the list it was taken from, so one from every account cannot continue one account's list.</summary>
    [Fact]
    public async Task ReadThreadsAsync_ACursorIssuedForEveryAccount_IsRefusedForOneAccount()
    {
        // Arrange
        var everyAccount = EmailThreadListCursor
            .After(new EmailThreadListPosition(FirstJuly, Answered), EmailThreadListCursor.FingerprintOf(account: null))
            .Encode();

        // Act
        var result = await this.ReadAsync("work", pageSize: null, everyAccount);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    /// <summary>Every field of a row reaches the wire as itself: the reader's own newest message, the received one's sender facts, and the title.</summary>
    [Fact]
    public async Task ReadThreadsAsync_AConversationTheReaderAnswered_PutsEveryFieldOnTheWire()
    {
        // Arrange
        this.Listing();

        // Act
        var result = await this.ReadAsync(account: null, pageSize: null, cursor: null);

        // Assert
        var row = Assert.IsType<Ok<ClientMailThreadListResponse>>(result.Result).Value!.Threads[0];

        Assert.Equal(
            (Answered.Value, "work", 2, 1, true, false),
            (row.ThreadId, row.Account, row.MessageCount, row.UnreadCount, row.HasAttachments, row.MoreParticipantsNotNamed));
        Assert.Equal(
            [("anna@example.test", "Anna", false), (ReaderAddress, (string?)"Me", true)],
            row.Participants.Select(participant => (participant.Address, participant.DisplayName, participant.Reader)));
        Assert.Equal(
            (ReadersReply.Value, (string?)"Re: plans", (DateTimeOffset?)FirstJuly.AddHours(2), (string?)ReaderAddress, (string?)"Me", (string?)"Sounds good.", true),
            (row.Newest.Id, row.Newest.Subject, row.Newest.ReceivedAt, row.Newest.SenderAddress, row.Newest.SenderDisplayName, row.Newest.Preview, row.Newest.Own));
        Assert.Equal(
            ("Authenticated", "Trusted", "NotSigned", false),
            (row.NewestReceivedSender!.AuthorAuthentication, row.NewestReceivedSender.DeploymentTrust, row.NewestReceivedSender.SignatureOutcome, row.NewestReceivedSender.SignerMatchesAuthor));
        Assert.Equal(
            ("Summer plans", "Waiting on Anna.", 2, (DateTimeOffset?)FirstJuly.AddHours(2)),
            (row.Title!.Topic, row.Title.Standing, row.Title.DerivedFromMessageCount, row.Title.DerivedFromLatestArrival));
    }

    /// <summary>A conversation only the reader wrote has no received message to mark, and one with no title says so rather than inventing one.</summary>
    [Fact]
    public async Task ReadThreadsAsync_AConversationOnlyTheReaderWroteWithNoTitle_PutsTheAbsencesOnTheWire()
    {
        // Arrange
        this.Listing();

        // Act
        var result = await this.ReadAsync(account: null, pageSize: null, cursor: null);

        // Assert
        var row = Assert.IsType<Ok<ClientMailThreadListResponse>>(result.Result).Value!.Threads[1];

        Assert.Equal(
            (Unanswered.Value, true, (string?)ReaderAddress, (string?)null),
            (row.ThreadId, row.Newest.Own, row.Newest.SenderAddress, row.Newest.Preview));
        Assert.Null(row.Title);
        Assert.Null(row.NewestReceivedSender);
    }

    /// <summary>Arranges two conversations: one the reader answered last, with a title, and one only the reader wrote, with none.</summary>
    private void Listing()
    {
        IReadOnlyList<ListedEmailThread> page =
        [
            new ListedEmailThread
            {
                ThreadId = Answered,
                NewestArrival = FirstJuly.AddHours(2),
                MessageCount = 2,
                UnreadCount = 1,
                HasAttachments = true,
                NewestEmailId = ReadersReply,
                NewestReceivedEmailId = AnnasMessage,
                Participants =
                [
                    new ListedThreadParticipant("anna@example.test", "Anna", IsReader: false),
                    new ListedThreadParticipant(ReaderAddress, "Me", IsReader: true),
                ],
                Title = new ListedThreadTitle(
                    ThreadTitle.Create("Summer plans", "Waiting on Anna."),
                    new ThreadStateRevision(2, FirstJuly.AddHours(2))),
            },
            new ListedEmailThread
            {
                ThreadId = Unanswered,
                NewestArrival = FirstJuly,
                MessageCount = 1,
                UnreadCount = 0,
                HasAttachments = false,
                NewestEmailId = ReadersNote,
                NewestReceivedEmailId = null,
                Participants = [new ListedThreadParticipant(ReaderAddress, DisplayName: null, IsReader: true)],
                Title = null,
            },
        ];

        this.listReader
            .ReadPageAsync(
                Arg.Any<MailboxScope>(),
                Arg.Any<IReadOnlySet<EmailAddress>>(),
                Arg.Any<EmailThreadListPosition?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(page);
    }

    private Task<Results<Ok<ClientMailThreadListResponse>, ProblemHttpResult>> ReadAsync(
        string? account,
        int? pageSize,
        string? cursor) => ClientMailThreadListEndpoint.ReadThreadsAsync(
            account,
            pageSize,
            cursor,
            this.Browser(),
            UnresolvedPersonPhotos.Create(),
            TestContext.Current.CancellationToken);

    /// <summary>Builds the use case behind the route over the real scope resolution, with storage and the instruments stood in for.</summary>
    private MailThreadListBrowser Browser()
    {
        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.ReadAssignedAccountsAsync(Arg.Any<CancellationToken>()).Returns([SyntheticServedAccount.Of(Work)]);

        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        _ = EmailAddress.TryCreate(displayName: null, ReaderAddress, out var reader);
        senderIdentities.FindSenderIdentity(Work).Returns(OutgoingSenderIdentity.Create(Work, reader));

        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        summaries.ReadSummariesAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<StoredEmailId, EmailSummary>>(new Dictionary<StoredEmailId, EmailSummary>
            {
                [ReadersReply] = Summary(ReadersReply, Answered, "Re: plans", ReaderAddress, "Me", FirstJuly.AddHours(2), SenderVerification.NotEstablished),
                [AnnasMessage] = Summary(AnnasMessage, Answered, "plans", "anna@example.test", "Anna", FirstJuly.AddHours(1), new SenderVerification
                {
                    AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                    DeploymentTrust = SenderTrustLevel.Trusted,
                }),
                [ReadersNote] = Summary(ReadersNote, Unanswered, "a note", ReaderAddress, null, FirstJuly, SenderVerification.NotEstablished),
            }));

        var previews = Substitute.For<IStoredEmailPreviewReader>();
        previews.ReadPreviewsAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<StoredEmailId, string>>(
                new Dictionary<StoredEmailId, string> { [ReadersReply] = "Sounds good." }));

        var readTelemetry = Substitute.For<IMailboxReadTelemetry>();
        readTelemetry.BeginRead(Arg.Any<MailboxReadOperation>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<IMailboxReadScope>());

        var participation = StubMailFolderParticipation.Mapping(new MailFolderIdentity(Work, MailFolderAlias.Create("INBOX")));

        return new MailThreadListBrowser(
            this.listReader,
            summaries,
            previews,
            catalog,
            senderIdentities,
            new MailboxScopeResolver(
                catalog,
                participation,
                StubDeploymentMailFolders.Of(participation),
                StubMailFolderMappings.ResolvingNothing),
            SensitiveContentEgressGuards.Inactive(),
            readTelemetry,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));
    }

    private static EmailSummary Summary(
        StoredEmailId storedEmailId,
        EmailThreadId threadId,
        string subject,
        string senderAddress,
        string? senderDisplayName,
        DateTimeOffset receivedAt,
        SenderVerification senderVerification) => new()
        {
            StoredEmailId = storedEmailId,
            Account = Work,
            Folder = new StoredEmailFolder(Inbox, "INBOX", MailFolderAlias.Create("INBOX")),
            ThreadId = threadId,
            Subject = subject,
            SentAt = receivedAt,
            ReceivedAt = receivedAt,
            SizeOctets = 2048,
            SenderAddress = senderAddress,
            SenderDisplayName = senderDisplayName,
            ToAddresses = ["someone@example.test"],
            SenderVerification = senderVerification,
            SenderAuthenticationEvidence = SenderAuthenticationEvidence.None,
            MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
            Attachments = new StoredEmailAttachmentSummary(
                AttachmentCount: 0,
                TotalSizeOctets: 0,
                InlineResourceCount: 0,
                IsEncrypted: false,
                CarriesUnverifiedSignature: false,
                ContainsUnexpandedTnefPart: false),
            ContentAvailability = StoredEmailContentAvailability.Available,
            RemoteFlags = new RemoteEmailFlagSnapshot(
                receivedAt,
                IsSeen: false,
                IsAnswered: false,
                IsFlagged: false,
                IsDraft: false,
                IsDeleted: false,
                RemoteEmailKeywords.None),
        };
}
