// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Photos;
using MailFathom.Application.Emails.BrowseThreadList;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what a row of the mail list grouped by conversation puts on the wire.</summary>
/// <remarks>
/// The mapping is positional, so two strings or two flags swapped would otherwise compile and pass: each field is
/// arranged with a value no other field holds and asserted as itself.
/// </remarks>
public sealed class ClientMailThreadListResponsesTests
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

    /// <summary>Every field of a row reaches the wire as itself: the reader's own newest message, the received one's sender facts, and the title.</summary>
    [Fact]
    public void For_AConversationTheReaderAnswered_PutsEveryFieldOnTheWire()
    {
        // Arrange
        var listed = new BrowsedListedThread(
            new ListedEmailThread
            {
                ThreadId = Answered,
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
                Categories = ["finance", "Legal"],
            },
            MoreParticipantsNotNamed: false,
            Summary(ReadersReply, Answered, "Re: plans", ReaderAddress, "Me", FirstJuly.AddHours(2)),
            "Sounds good.",
            new SenderVerification
            {
                AuthorAuthentication = AuthorAuthenticationOutcome.Authenticated,
                DeploymentTrust = SenderTrustLevel.Trusted,
            });

        // Act
        var row = ClientMailThreadListEntryResponse.For(listed, new Dictionary<EmailAddress, PersonPhoto>());

        // Assert
        Assert.Equal([new ClientMailCategoryResponse("finance", Name: null), new ClientMailCategoryResponse("Legal", "Legal")], row.Categories);
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
    public void For_AConversationOnlyTheReaderWroteWithNoTitle_PutsTheAbsencesOnTheWire()
    {
        // Arrange
        var listed = new BrowsedListedThread(
            new ListedEmailThread
            {
                ThreadId = Unanswered,
                MessageCount = 1,
                UnreadCount = 0,
                HasAttachments = false,
                NewestEmailId = ReadersNote,
                NewestReceivedEmailId = null,
                Participants = [new ListedThreadParticipant(ReaderAddress, DisplayName: null, IsReader: true)],
                Title = null,
                Categories = [],
            },
            MoreParticipantsNotNamed: false,
            Summary(ReadersNote, Unanswered, "a note", ReaderAddress, senderDisplayName: null, FirstJuly),
            NewestPreview: null,
            NewestReceivedSender: null);

        // Act
        var row = ClientMailThreadListEntryResponse.For(listed, new Dictionary<EmailAddress, PersonPhoto>());

        // Assert
        Assert.Equal(
            (Unanswered.Value, true, (string?)ReaderAddress, (string?)null),
            (row.ThreadId, row.Newest.Own, row.Newest.SenderAddress, row.Newest.Preview));
        Assert.Null(row.Title);
        Assert.Null(row.NewestReceivedSender);
        Assert.Empty(row.Categories);
    }

    /// <summary>A conversation row quotes its newest message, and says whether that message is local only.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_AConversationWhoseNewestMessageIsLocalOnly_SaysSo(bool isLocalOnly)
    {
        // Arrange
        var listed = new BrowsedListedThread(
            new ListedEmailThread
            {
                ThreadId = Answered,
                MessageCount = 1,
                UnreadCount = 0,
                HasAttachments = false,
                NewestEmailId = ReadersReply,
                NewestReceivedEmailId = null,
                Participants = [],
                Title = null,
                Categories = [],
            },
            MoreParticipantsNotNamed: false,
            Summary(ReadersReply, Answered, "Re: plans", ReaderAddress, "Me", FirstJuly) with { IsLocalOnly = isLocalOnly },
            "Sounds good.",
            NewestReceivedSender: null);

        // Act
        var row = ClientMailThreadListEntryResponse.For(listed, new Dictionary<EmailAddress, PersonPhoto>());

        // Assert
        Assert.Equal(isLocalOnly, row.Newest.LocalOnly);
    }

    private static EmailSummary Summary(
        StoredEmailId storedEmailId,
        EmailThreadId threadId,
        string subject,
        string senderAddress,
        string? senderDisplayName,
        DateTimeOffset receivedAt) => new()
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
            SenderVerification = SenderVerification.NotEstablished,
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
            Flags = new StoredEmailFlags(
                receivedAt,
                IsSeen: false,
                IsAnswered: false,
                IsFlagged: false,
                IsDraft: false,
                IsDeleted: false,
                EmailKeywords.None),
        };
}
