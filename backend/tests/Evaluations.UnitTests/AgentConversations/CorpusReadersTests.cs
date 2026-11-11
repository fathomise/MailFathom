// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Domain.Access;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Tasks;
using MailFathom.Evaluations.AgentConversations;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Evaluations.UnitTests.AgentConversations;

/// <summary>Proves the deployment's own readers answer over the corpus, so a tool that finds nothing in a run is the model's doing rather than the harness's.</summary>
public sealed class CorpusReadersTests
{
    private static AgentConversationReaders Readers() =>
        CorpusReaders.For(
            AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.MailRead,
                MailFathomPermission.MailDraftsWrite,
                MailFathomPermission.MailSend,
                MailFathomPermission.CalendarRead,
                MailFathomPermission.TasksRead),
            new CorpusKnowledgeSearch(AgentConversationScenario.Mailbox),
            UserClocks.Reading(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task ContentReader_AConversationsThread_ReadsEveryMessageInItRenderedFromItsMime()
    {
        // Arrange
        var thread = CorpusReaders.ThreadOf(PersonalAgenda.ArchiveBoxes);

        // Act
        var read = await Readers().ContentReader.ReadContentAsync(GetEmailContentRequest.CreateForThread(thread), TestContext.Current.CancellationToken);

        // Assert
        var bodies = read.Emails.Select(static outcome => outcome.Content?.Body.PlainText.Text ?? string.Empty).ToArray();

        Assert.Equal(PersonalAgenda.ArchiveBoxes.Count, bodies.Length);
        Assert.Contains(bodies, static body => body.Contains("Emil", StringComparison.Ordinal));
    }

    /// <summary>
    /// A reply the person has not sent is the person's own Agent's to read and no other agent's, as in a deployment, so
    /// a case about one measures the Agent rather than a harness that would show it to any reader.
    /// </summary>
    [Theory]
    [InlineData(MailboxReaderKind.ConversationAgent, true)]
    [InlineData(MailboxReaderKind.Agent, false)]
    public async Task ContentReader_AReplyNotSent_IsReadByThePersonsOwnAgentAlone(MailboxReaderKind reader, bool readable)
    {
        // Arrange
        var reply = UnsentMail.ScheduledConfirmation.Message;

        // Act
        var read = await Readers().ContentReader.ReadContentAsync(
            GetEmailContentRequest.Create([reply.Id]) with { Reader = reader },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(readable, read.Emails.Single().Content is not null);
    }

    /// <summary>Each reply not sent is read back with the state its case is about, which is what the turn marks it with.</summary>
    [Fact]
    public async Task ProvenanceReader_EveryReplyNotSent_CarriesItsState()
    {
        // Arrange
        UnsentMessage[] replies =
        [
            UnsentMail.ScheduledConfirmation,
            UnsentMail.DraftedPromise,
            UnsentMail.ReplyBeingSent,
            UnsentMail.FailedAcceptance,
        ];

        // Act
        var provenance = await Readers().ProvenanceReader.ReadAsync(
            [.. replies.Select(static reply => reply.Message.Id)],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [UnsentEmailState.Scheduled, UnsentEmailState.Draft, UnsentEmailState.Sending, UnsentEmailState.Failed],
            replies.Select(reply => provenance[reply.Message.Id].Unsent));
    }

    /// <summary>The listing reads the corpus newest first through the deployment's own use case, so a recency case measures the Agent rather than a timeline that holds nothing.</summary>
    [Fact]
    public async Task Timeline_TheNewestPage_ReadsTheCorpusNewestFirst()
    {
        // Act
        var page = await Readers().Timeline.ListEmailsAsync(
            new ListEmailsRequest { Reader = MailboxReaderKind.ConversationAgent, PageSize = 5 },
            TestContext.Current.CancellationToken);

        // Assert
        var received = page.Emails.Select(static summary => summary.ReceivedAt).ToArray();

        Assert.Equal((5, true), (page.Emails.Count, page.HasMore));
        Assert.Equal(received.OrderDescending(), received);
    }

    /// <summary>A reply the person has not sent is in the person's own Agent's listing, marked as a draft as a deployment stores it, so a case about one can list it.</summary>
    [Fact]
    public async Task Timeline_AReplyNotSent_IsListedForThePersonsOwnAgentAsADraft()
    {
        // Arrange
        var reply = UnsentMail.ScheduledConfirmation.Message;

        // Act
        var page = await Readers().Timeline.ListEmailsAsync(
            new ListEmailsRequest { Reader = MailboxReaderKind.ConversationAgent, PageSize = 100 },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(page.Emails.Single(summary => summary.StoredEmailId == reply.Id).Flags.IsDraft);
    }

    [Fact]
    public async Task StateBrowser_AConversationWithADerivedState_ReadsItsEntries()
    {
        // Act
        var state = await Readers().StateBrowser.ReadStateAsync(CorpusReaders.ThreadOf(PersonalAgenda.KestrelQuayMove), MailboxReaderKind.Agent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, state?.Entries.Count);
    }

    [Fact]
    public async Task Calendar_WednesdaysWindow_ReadsWhatIsScheduledThatDay()
    {
        // Act
        var events = await Readers().Calendar.ReadWindowAsync(
            new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero),
            origin: null,
            count: 50,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["Lunch with Ada Zielinska", "Budget review with finance"], events?.Select(static entry => entry.Title.Value));
    }

    [Fact]
    public async Task Tasks_TheTasksMailProposed_ReadsTheOnesReadOutOfMail()
    {
        // Act
        var page = await Readers().Tasks.ReadPageAsync(PersonalTaskOrigin.Proposed, pageSize: 50, cursor: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["Reply to Quayside Supplies about the A4 paper delivery", PersonalAgenda.HostileTaskTitle],
            page?.Tasks.Select(static task => task.Title));
    }

    [Fact]
    public async Task ResponseAuthoring_AReplyToACorpusMessage_IsAddressedToItsSender()
    {
        // Arrange
        var answered = PersonalAgenda.ArchiveBoxes[2];

        // Act
        var authored = await Readers().ResponseAuthoring.AuthorAsync(
            new AuthoredResponseRequest
            {
                Reader = MailboxReaderKind.Agent,
                AnsweredEmailId = answered.Id,
                Act = AuthoredResponseAct.Reply,
                PlainTextBody = "Thank you.",
                Recipients = [],
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([answered.Sender], authored.Email?.Recipients.Select(static recipient => recipient.Address));
    }

    [Fact]
    public void ThreadOf_EveryConversation_NamesAThreadNoOtherConversationOrMessageCarries()
    {
        // Arrange
        IReadOnlyList<IReadOnlyList<CorpusMessage>> exchanges = [.. CorpusMessage.Exchanges, .. HostileMail.Exchanges, .. FalseClaimMail.Exchanges, .. PolishCorpus.Exchanges];

        // Act
        var threads = exchanges.Select(static exchange => CorpusReaders.ThreadOf(exchange).Value).ToHashSet();

        // Assert
        Assert.Equal(
            (exchanges.Count, false),
            (threads.Count, exchanges.SelectMany(static exchange => exchange).Any(message => threads.Contains(message.Id.Value))));
    }
}
