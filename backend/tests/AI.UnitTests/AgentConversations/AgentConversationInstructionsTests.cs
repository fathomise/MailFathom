// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.AgentConversations;
using MailFathom.AI.Orchestration;
using MailFathom.AI.Retrieval;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers the instruction the Agent is composed with.</summary>
public sealed class AgentConversationInstructionsTests
{
    /// <summary>The Agent searches through the same tool as a question does, so it is held to the same rule about a category.</summary>
    [Fact]
    public void TextFor_AnyLanguage_CarriesTheRuleForNarrowingByACategory()
    {
        // Act
        var text = AgentConversationInstructions.TextFor(UserLanguage.English);

        // Assert
        Assert.Contains(ScopedMailKnowledgeRetrieval.CategoryFilterRule, text, StringComparison.Ordinal);
    }

    /// <summary>Every language the deployment writes in has an instruction, and it names that language as the one the Agent's own words are in.</summary>
    [Fact]
    public void TextFor_EveryLanguage_NamesThatLanguageForTheAgentsOwnWords()
    {
        // Act
        var texts = Enum.GetValues<UserLanguage>().Select(static language => (language, AgentConversationInstructions.TextFor(language)));

        // Assert
        Assert.All(texts, static pair => Assert.Contains($"Write your own words — your answer, a summary, a paraphrase — in {pair.language}.", pair.Item2, StringComparison.Ordinal));
    }

    /// <summary>A turn names the accounts it was handed and says nothing of accounts where it was handed none.</summary>
    [Fact]
    public void ComposeTurn_NoAccountToName_LeavesTheAccountsOutOfTheTurn()
    {
        // Arrange
        var askedAt = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

        // Act
        var naming = AgentConversationComposition.ComposeTurn(askedAt, UserTimeZone.Coordinated, [new AgentNamedAccount(MailAccountId.Create("primary"), ClosesWithFooter: false)], [], AgentMessageScope.Mailbox(), [], "What is due?");
        var silent = AgentConversationComposition.ComposeTurn(askedAt, UserTimeZone.Coordinated, [], [], AgentMessageScope.Mailbox(), [], "What is due?");

        // Assert
        Assert.Contains("The person's accounts: primary.", naming, StringComparison.Ordinal);
        Assert.DoesNotContain("accounts", silent, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith($"<{AgentTurnDocument.AskElementName}>What is due?</{AgentTurnDocument.AskElementName}>", silent, StringComparison.Ordinal);
        Assert.DoesNotContain("footer", naming, StringComparison.Ordinal);
    }

    /// <summary>A turn names the languages the person's mailboxes are kept in, so a search can try each, and says nothing of languages where it was handed none.</summary>
    [Fact]
    public void ComposeTurn_MailboxesInTwoLanguages_NamesBothLanguages()
    {
        // Arrange
        var askedAt = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
        IReadOnlyList<AgentNamedAccount> accounts = [new(MailAccountId.Create("primary"), ClosesWithFooter: false)];

        // Act
        var naming = AgentConversationComposition.ComposeTurn(askedAt, UserTimeZone.Coordinated, accounts, [MailAccountLanguage.English, MailAccountLanguage.Polish], AgentMessageScope.Mailbox(), [], "Which train?");
        var silent = AgentConversationComposition.ComposeTurn(askedAt, UserTimeZone.Coordinated, accounts, [], AgentMessageScope.Mailbox(), [], "Which train?");

        // Assert
        Assert.Contains("The mailboxes you may search are kept in: English, Polish.", naming, StringComparison.Ordinal);
        Assert.DoesNotContain("kept in", silent, StringComparison.Ordinal);
    }

    /// <summary>A turn names which accounts close a message with the person's footer, and nothing of what the footer says, because the turn is never handed it.</summary>
    [Fact]
    public void ComposeTurn_AnAccountThatAppendsAFooter_NamesItAmongThoseClosingWithOne()
    {
        // Arrange
        var askedAt = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
        AgentNamedAccount[] accounts =
        [
            new(MailAccountId.Create("primary"), ClosesWithFooter: true),
            new(MailAccountId.Create("private"), ClosesWithFooter: false),
        ];

        // Act
        var turn = AgentConversationComposition.ComposeTurn(askedAt, UserTimeZone.Coordinated, accounts, [], AgentMessageScope.Mailbox(), [], "Write to Karolina.");

        // Assert
        Assert.Contains("The person's accounts: primary, private.", turn, StringComparison.Ordinal);
        Assert.Contains("Messages sent from these accounts close with the person's footer: primary.", turn, StringComparison.Ordinal);
    }

    [Fact]
    public void TextFor_AnyLanguage_LeavesTheClosingOfAMessageFromAFootedAccountToItsFooter()
    {
        // Act
        var text = AgentConversationInstructions.TextFor(UserLanguage.English);

        // Assert
        Assert.Contains("Some accounts close every message sent from them with the person's footer", text, StringComparison.Ordinal);
        Assert.Contains("footer=\"true\"", text, StringComparison.Ordinal);
    }

    /// <summary>A turn names the person's zone and its offset, and states now on their clock, so a time they name is read where they are.</summary>
    [Fact]
    public void ComposeTurn_APersonInAZoneOfTheirOwn_NamesTheZoneAndStatesNowOnTheirClock()
    {
        // Arrange
        var askedAt = new DateTimeOffset(2026, 9, 14, 22, 30, 0, TimeSpan.Zero);
        Assert.True(UserTimeZone.TryRead("Europe/Warsaw", out var warsaw));

        // Act
        var turn = AgentConversationComposition.ComposeTurn(askedAt, warsaw, [], [], AgentMessageScope.Mailbox(), [], "Send it tomorrow at 8.");

        // Assert
        Assert.Contains("The person's time zone: Europe/Warsaw, at UTC+02:00 now.", turn, StringComparison.Ordinal);
        Assert.Contains("2026-09-15T00:30 (Tuesday)", turn, StringComparison.Ordinal);
    }

    /// <summary>A question about a merged arrival names its messages by identifier, for the model to read through its tools.</summary>
    [Fact]
    public void ComposeTurn_AQuestionAboutANotification_NamesTheMessagesItStandsFor()
    {
        // Arrange
        var askedAt = new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);
        var scope = AgentMessageScope.Notification(NotificationId.Create(Guid.CreateVersion7()));
        var first = StoredEmailId.Create(Guid.CreateVersion7());
        var second = StoredEmailId.Create(Guid.CreateVersion7());

        // Act
        var naming = AgentConversationComposition.ComposeTurn(askedAt, UserTimeZone.Coordinated, [], [], scope, [first, second], "Summarize these.");
        var holdingNone = AgentConversationComposition.ComposeTurn(askedAt, UserTimeZone.Coordinated, [], [], scope, [], "Summarize these.");

        // Assert
        Assert.Contains($"by id: {first}, {second}.", naming, StringComparison.Ordinal);
        Assert.Contains("none of which is still held", holdingNone, StringComparison.Ordinal);
    }

    /// <summary>A day and an hour written the way the mail and the calendar write them are the ones the person recognises.</summary>
    [Fact]
    public void TextFor_TheInstruction_StatesHowADateAndATimeAreWritten()
    {
        // Act
        var text = string.Join(' ', AgentConversationInstructions.TextFor(UserLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("Write a date as the day before the month's name", text, StringComparison.Ordinal);
        Assert.Contains("a time on the 24-hour clock", text, StringComparison.Ordinal);
    }

    /// <summary>The client draws an answer as Markdown, so the Agent may structure one with it, and only as far as the client draws it.</summary>
    [Fact]
    public void TextFor_TheInstruction_AllowsLightMarkdownAndRefusesWhatTheClientDoesNotDraw()
    {
        // Act
        var text = string.Join(' ', AgentConversationInstructions.TextFor(UserLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("Your answer is shown as Markdown", text, StringComparison.Ordinal);
        Assert.Contains("An answer that is a sentence or two stays plain sentences.", text, StringComparison.Ordinal);
        Assert.Contains("never write headings, links, images, or HTML", text, StringComparison.Ordinal);
    }

    /// <summary>A later message correcting a date seldom repeats the words the first was found by, so the Agent reads the whole conversation first.</summary>
    [Fact]
    public void TextFor_TheInstruction_ReadsTheConversationForALaterCorrection()
    {
        // Act
        var text = string.Join(' ', AgentConversationInstructions.TextFor(UserLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.Contains("may be changed by a later message of the same conversation", text, StringComparison.Ordinal);
        Assert.Contains("read the conversation that message belongs to and answer from the latest message that settles it", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Agent reads drafts and sends that have not left, so the instruction names every state the turn marks one with
    /// and says that none of them is known to have been received — including a send whose attempt was never answered,
    /// which may have arrived and is not offered again.
    /// </summary>
    [Fact]
    public void TextFor_TheInstruction_NamesEveryUnsentStateAndReadsNoneAsReceived()
    {
        // Act
        var text = string.Join(' ', AgentConversationInstructions.TextFor(UserLanguage.English).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // Assert
        Assert.All(
            [
                $"{AgentTurnDocument.UnsentAttributeName}=\"{AgentTurnDocument.DraftUnsent}\"",
                $"\"{AgentTurnDocument.ScheduledUnsent}\"",
                $"\"{AgentTurnDocument.SendingUnsent}\"",
                $"\"{AgentTurnDocument.FailedUnsent}\"",
                "None of them is mail anybody is known to have received",
                "an attempt whose answer never came back, which may or may not have arrived and is not offered again",
            ],
            fragment => Assert.Contains(fragment, text, StringComparison.Ordinal));
    }

    /// <summary>A quotation keeps the mail's language, so an answer that is only a quotation is not written in the person's.</summary>
    [Fact]
    public void TextFor_EveryLanguage_FramesAQuotationInThatLanguage()
    {
        // Act
        var texts = Enum.GetValues<UserLanguage>().Select(static language => (language, string.Join(' ', AgentConversationInstructions.TextFor(language).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))));

        // Assert
        Assert.All(texts, static pair => Assert.Contains($"A quotation never stands alone as your answer: even when the person asks for the exact words, say in {pair.language} what they are", pair.Item2, StringComparison.Ordinal));
    }

    /// <summary>The Agent set received bounds and an attachment filter no question stated, so the rule mail answering carries reaches it too.</summary>
    [Fact]
    public void TextFor_AnyLanguage_SetsNoReceivedBoundOrAttachmentFilterTheQuestionDoesNotState()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("A question that names no period gets no received bounds, and one that asks about no attachment gets no attachment filter", text, StringComparison.Ordinal);
        Assert.Contains("naming a document, such as an invoice, a contract, or a report, is not asking about an attachment", text, StringComparison.Ordinal);
        Assert.Contains("never a period on its own", text, StringComparison.Ordinal);
    }

    /// <summary>A question the mailbox cannot answer ran ten to seventeen searches, so the search is bounded.</summary>
    [Fact]
    public void TextFor_AnyLanguage_EndsTheSearchAfterAFewLookupsFindNothing()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("other words, another of those languages, the other forms an inflected word takes", text, StringComparison.Ordinal);
        Assert.Contains("search in each of those languages before concluding that the mail is not there", text, StringComparison.Ordinal);
        Assert.Contains($"{AgentTurnDocument.FromNameAttributeName} and {AgentTurnDocument.FromAttributeName} attributes: answer who wrote", text, StringComparison.Ordinal);
        Assert.Contains("After a few such lookups find nothing, say that the mailbox does not answer rather than searching on.", text, StringComparison.Ordinal);
    }

    /// <summary>After compaction a detail the person settled was searched for in mail and doubted when it was not found.</summary>
    [Fact]
    public void TextFor_AnyLanguage_AnswersWhatThePersonSettledFromTheConversation()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("A detail the person settled earlier in this conversation, or that its summary records, is answered from the conversation", text, StringComparison.Ordinal);
        Assert.Contains("not finding it in the mailbox is no reason to doubt it", text, StringComparison.Ordinal);
    }

    /// <summary>A corrected answer opened with the conclusion it was correcting.</summary>
    [Fact]
    public void TextFor_AnyLanguage_SaysAnEarlierMisreadingFirst()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("When an earlier answer of yours misread the mail, say so first", text, StringComparison.Ordinal);
    }

    /// <summary>"Propose it." yielded the event and an invitation nobody asked for.</summary>
    [Fact]
    public void TextFor_AnyLanguage_ProposesOnlyWhatThePersonAskedFor()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.DoesNotContain("or clearly needs", text, StringComparison.Ordinal);
        Assert.Contains("Propose only what the person asked for, and one proposal per act.", text, StringComparison.Ordinal);
        Assert.Contains("A proposed event invites nobody", text, StringComparison.Ordinal);
    }

    /// <summary>A grant that cannot write was asked to write, and the person is owed the wording rather than silence.</summary>
    [Fact]
    public void TextFor_AnyLanguage_GivesTheWordingWhereNoToolCanProposeIt()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("Where you have no tool to propose what the person asked for, say so and give them the wording", text, StringComparison.Ordinal);
    }

    /// <summary>A message mentioning a time was scheduled to leave at it.</summary>
    [Fact]
    public void TextFor_AnyLanguage_NeverReadsATimeTheMessageNamesAsItsSendTime()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("A time the message's own text names", text, StringComparison.Ordinal);
        Assert.Contains("is never a time for it to leave", text, StringComparison.Ordinal);
    }

    /// <summary>Follow-ups were written as the Agent's own offers rather than as questions the person would ask.</summary>
    [Fact]
    public void TextFor_AnyLanguage_WritesFollowUpsAsThePersonsQuestionsNeverAsOffers()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("A suggestion is a question the person would ask, never an offer of yours", text, StringComparison.Ordinal);
    }

    /// <summary>A request to summarise a conversation was answered by showing its state.</summary>
    [Fact]
    public void TextFor_AnyLanguage_ReadsAConversationItIsAskedToSummariseRatherThanShowingItsState()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("When the person asks where a conversation stands or what is still open in it", text, StringComparison.Ordinal);
        Assert.Contains("A request to read, summarise, or retell a conversation is not that", text, StringComparison.Ordinal);
    }

    /// <summary>"18.50 EUR" became "€18.50".</summary>
    [Fact]
    public void TextFor_AnyLanguage_CopiesAmountsCodesAndVersionsExactly()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("Copy an amount with its currency, a code, or a version exactly as the mail writes it", text, StringComparison.Ordinal);
    }

    /// <summary>Every lookup found something unrelated, so the rule that fires only after empty lookups never stopped the run.</summary>
    [Fact]
    public void TextFor_AnyLanguage_StopsSearchingAfterThreeLookupsBringNothingNew()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains(
            "Once three lookups in a row bring back nothing new about what was asked — no mail, mail you have already seen, or mail about something else — stop searching and answer from what you have.",
            text,
            StringComparison.Ordinal);
    }

    /// <summary>A proposal the person dictated was held back for a question about a detail the mail added.</summary>
    [Fact]
    public void TextFor_AnyLanguage_ProposesWhatThePersonDictatedAsTheySaidIt()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("When the person says who to write to and what to say, propose it as they said it: a detail the mail adds is mentioned beside the proposal, never asked about first.", text, StringComparison.Ordinal);
    }

    /// <summary>"The same message" to the other Ingrid was proposed as a reply to her mail rather than as a new message to her.</summary>
    [Fact]
    public void TextFor_AnyLanguage_KeepsTheActOfAProposalItCorrects()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("Correcting an earlier proposal keeps its act: \"the same message\" to somebody else is a new message to them, not a reply to their mail.", text, StringComparison.Ordinal);
    }

    /// <summary>A Polish answer keeping English task titles unquoted read as mixed language, though the titles were kept as the instruction asks.</summary>
    [Fact]
    public void TextFor_AnyLanguage_QuotesWordsKeptInAnotherLanguage()
    {
        // Act
        var text = Flattened(UserLanguage.Polish);

        // Assert
        Assert.Contains("Put a title, a subject, or any other words you keep in a language other than", text, StringComparison.Ordinal);
        Assert.Contains("in quotation marks.", text, StringComparison.Ordinal);
    }

    /// <summary>"See you on Sunday" was answered without the date an earlier message of the thread fixed for that Sunday.</summary>
    [Fact]
    public void TextFor_AnyLanguage_DatesADayAMessageNamesOnlyByItsWeekday()
    {
        // Act
        var text = Flattened(UserLanguage.English);

        // Assert
        Assert.Contains("Where a message names a day only by its weekday or relatively — \"on Sunday\", \"next week\" — give the date its conversation fixes for it.", text, StringComparison.Ordinal);
    }

    private static string Flattened(UserLanguage language) => AgentConversationInstructions.TextFor(language).ReplaceLineEndings(" ");
}
