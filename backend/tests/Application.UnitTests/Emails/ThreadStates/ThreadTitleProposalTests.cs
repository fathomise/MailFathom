// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Provenance;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ThreadStates;

/// <summary>Covers the rule that keeps a conversation's topic as replies arrive, and takes its standing every time.</summary>
public sealed class ThreadTitleProposalTests
{
    private const string KeptTopic = "Racking quotation for the Gdańsk warehouse";

    private const string NewTopic = "Racking installation dates";

    [Fact]
    public void SettleFor_AConversationWithNoTopicYet_TakesTheProposedTopic()
    {
        // Arrange
        var proposal = new ThreadTitleProposal(NewTopic, StatesSubjectChange: false, "Dates still open");

        // Act
        var title = proposal.SettleFor(Conversation(messageCount: 6, keptTopic: null));

        // Assert
        Assert.NotNull(title);
        Assert.Equal(NewTopic, title.Topic);
        Assert.Equal("Dates still open", title.Standing);
    }

    [Fact]
    public void SettleFor_AYoungConversationGivenANewTopic_TakesItWithoutAStatedSubjectChange()
    {
        // Arrange
        var proposal = new ThreadTitleProposal(NewTopic, StatesSubjectChange: false, string.Empty);

        // Act
        var title = proposal.SettleFor(
            Conversation(ThreadTitleProposal.YoungConversationMessageCount, KeptTopic));

        // Assert
        Assert.Equal(NewTopic, title?.Topic);
    }

    [Fact]
    public void SettleFor_AnOlderConversationGivenANewTopicWithoutASubjectChange_KeepsItsTopicAndTakesTheStanding()
    {
        // Arrange
        var proposal = new ThreadTitleProposal(NewTopic, StatesSubjectChange: false, "Karolina owes the figures");

        // Act
        var title = proposal.SettleFor(
            Conversation(ThreadTitleProposal.YoungConversationMessageCount + 1, KeptTopic));

        // Assert
        Assert.NotNull(title);
        Assert.Equal(KeptTopic, title.Topic);
        Assert.Equal("Karolina owes the figures", title.Standing);
    }

    [Fact]
    public void SettleFor_AnOlderConversationWhoseSubjectChanged_TakesTheNewTopic()
    {
        // Arrange
        var proposal = new ThreadTitleProposal(NewTopic, StatesSubjectChange: true, string.Empty);

        // Act
        var title = proposal.SettleFor(Conversation(messageCount: 12, KeptTopic));

        // Assert
        Assert.Equal(NewTopic, title?.Topic);
    }

    [Fact]
    public void SettleFor_AProposalThatKeepsTheTopic_KeepsTheStoredOne()
    {
        // Arrange
        var proposal = new ThreadTitleProposal(Topic: null, StatesSubjectChange: false, string.Empty);

        // Act
        var title = proposal.SettleFor(Conversation(messageCount: 2, KeptTopic));

        // Assert
        Assert.NotNull(title);
        Assert.Equal(KeptTopic, title.Topic);
        Assert.Empty(title.Standing);
    }

    [Fact]
    public void SettleFor_AProposalThatKeepsATopicNothingHolds_GivesNoTitle()
    {
        // Arrange
        var proposal = new ThreadTitleProposal(Topic: null, StatesSubjectChange: false, string.Empty);

        // Act
        var title = proposal.SettleFor(Conversation(messageCount: 2, keptTopic: null));

        // Assert
        Assert.Null(title);
    }

    [Fact]
    public void SettleFor_AConversationThatIsAllJunk_GivesNoTitle()
    {
        // Arrange
        var proposal = new ThreadTitleProposal(NewTopic, StatesSubjectChange: false, string.Empty);

        // Act
        var title = proposal.SettleFor(
            Conversation(messageCount: 2, keptTopic: null, StubEmailProvenanceReader.UnverifiedStranger));

        // Assert
        Assert.Null(title);
    }

    private static DerivableThread Conversation(
        int messageCount,
        string? keptTopic,
        EmailProvenance? provenance = null) =>
        new(
            EmailThreadId.Create(Guid.CreateVersion7()),
            "Racking",
            new ThreadStateRevision(messageCount, new DateTimeOffset(2026, 9, 7, 16, 0, 0, TimeSpan.Zero)),
            [
                .. Enumerable.Range(0, messageCount).Select(position => new DerivableThreadMessage(
                    StoredEmailId.Create(Guid.CreateVersion7()),
                    position,
                    "Karolina",
                    new DateTimeOffset(2026, 9, 7, 15, position, 0, TimeSpan.Zero),
                    "we can hold the two-hour response time",
                    provenance ?? StubEmailProvenanceReader.KnownCorrespondent)),
            ],
            ExceedsBound: false,
            keptTopic);
}
