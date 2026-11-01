// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Xml.Linq;
using MailFathom.AI.AgentConversations;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.AgentConversations;

/// <summary>Covers what the Agent's reading tools return: what each record is written as, and what the service states about it beside the text.</summary>
public sealed class AgentConversationReadingsTests
{
    private static readonly DateTimeOffset Sent = new(2026, 9, 21, 14, 40, 0, TimeSpan.Zero);

    [Fact]
    public void Thread_AMessage_CarriesItsIdentityAsAttributesAndItsTextAsElements()
    {
        // Arrange
        var id = StoredEmailId.Create(Guid.CreateVersion7());

        // Act
        var message = Parsed(AgentConversationReadings.Thread(
            [new GuardedThreadReading(id, PresentationCitationId.Create("m1"), "ilse@example.test", Sent, StubEmailProvenanceReader.KnownCorrespondent, "Visitor parking", "Park on Quay Street.", BodyCut: false)]))
            .Element(AgentConversationReadings.MessageElementName)!;

        // Assert
        Assert.Equal(id.ToString(), message.Attribute(AgentConversationReadings.MessageIdAttributeName)?.Value);
        Assert.Equal("m1", message.Attribute(AgentConversationReadings.SourceAttributeName)?.Value);
        Assert.Equal("ilse@example.test", message.Attribute(AgentConversationReadings.FromAttributeName)?.Value);
        Assert.Equal(Sent, DateTimeOffset.Parse(message.Attribute(AgentConversationReadings.SentAttributeName)!.Value, CultureInfo.InvariantCulture));
        Assert.Equal("Visitor parking", message.Element(AgentConversationReadings.SubjectElementName)?.Value);
        Assert.Equal("Park on Quay Street.", message.Element(AgentConversationReadings.BodyElementName)?.Value);
        Assert.Null(message.Element(AgentConversationReadings.BodyElementName)?.Attribute(AgentTurnDocument.CutAttributeName));
    }

    /// <summary>A message whose account appends the person's footer says so, so a reply to it is written to end before the footer.</summary>
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, null)]
    public void Thread_AMessageInAnAccountWithOrWithoutAFooter_MarksOnlyTheOneWithOne(bool answeredWithFooter, string? marked)
    {
        // Act
        var message = Parsed(AgentConversationReadings.Thread(
            [new GuardedThreadReading(StoredEmailId.Create(Guid.CreateVersion7()), PresentationCitationId.Create("m1"), SenderAddress: null, SentAt: null, StubEmailProvenanceReader.KnownCorrespondent, Subject: null, "text", BodyCut: false, AnsweredWithFooter: answeredWithFooter)]))
            .Element(AgentConversationReadings.MessageElementName)!;

        // Assert
        Assert.Equal(marked, message.Attribute(AgentConversationReadings.FooterAttributeName)?.Value);
    }

    /// <summary>A body cut to what a reading shows says so, so the model does not read the tail it was not given as never written.</summary>
    [Fact]
    public void Thread_ABodyCutToTheReading_IsMarkedCut()
    {
        // Act
        var body = Parsed(AgentConversationReadings.Thread(
            [new GuardedThreadReading(StoredEmailId.Create(Guid.CreateVersion7()), PresentationCitationId.Create("m1"), SenderAddress: null, SentAt: null, StubEmailProvenanceReader.KnownCorrespondent, Subject: null, "The first part", BodyCut: true)]))
            .Descendants(AgentConversationReadings.BodyElementName)
            .Single();

        // Assert
        Assert.Equal("true", body.Attribute(AgentTurnDocument.CutAttributeName)?.Value);
    }

    /// <summary>What a message did not carry is absent rather than blank, so a model does not read an empty subject as one somebody wrote.</summary>
    [Fact]
    public void Thread_AMessageCarryingNoSubjectSenderOrDate_WritesNoneOfThem()
    {
        // Act
        var message = Parsed(AgentConversationReadings.Thread(
            [new GuardedThreadReading(StoredEmailId.Create(Guid.CreateVersion7()), PresentationCitationId.Create("m1"), SenderAddress: null, SentAt: null, StubEmailProvenanceReader.KnownCorrespondent, Subject: null, "text", BodyCut: false)]))
            .Element(AgentConversationReadings.MessageElementName)!;

        // Assert
        Assert.Null(message.Attribute(AgentConversationReadings.FromAttributeName));
        Assert.Null(message.Attribute(AgentConversationReadings.SentAttributeName));
        Assert.Null(message.Element(AgentConversationReadings.SubjectElementName));
    }

    [Fact]
    public void Calendar_AnAllDayEvent_SaysSoBesideItsTitle()
    {
        // Act
        var entry = Parsed(AgentConversationReadings.Calendar(
            Sent,
            Sent.AddDays(7),
            [new GuardedCalendarReading(Sent, End: null, IsAllDay: true, "Lift inspection")]))
            .Descendants(AgentConversationReadings.EventElementName)
            .Single();

        // Assert
        Assert.Equal("true", entry.Attribute(AgentConversationReadings.AllDayAttributeName)?.Value);
        Assert.Null(entry.Attribute(AgentConversationReadings.EndAttributeName));
        Assert.Equal("Lift inspection", entry.Element(AgentConversationReadings.TitleElementName)?.Value);
    }

    /// <summary>
    /// A task's state and origin are the service's to state, so a title written to look done or owed by the person
    /// cannot make it so: both are attributes, and the title is only text.
    /// </summary>
    [Theory]
    [InlineData(false, PersonalTaskOrigin.Asserted, AgentConversationReadings.OpenState, AgentConversationReadings.PersonOrigin)]
    [InlineData(true, PersonalTaskOrigin.Proposed, AgentConversationReadings.DoneState, AgentConversationReadings.ProposedFromMailOrigin)]
    public void Tasks_ATask_StatesItsStateAndOriginAsAttributes(bool isCompleted, PersonalTaskOrigin origin, string state, string writtenOrigin)
    {
        // Act
        var task = Parsed(AgentConversationReadings.Tasks(
            [new GuardedTaskReading(isCompleted, origin, new DateOnly(2026, 10, 16), "[done] [owned by me] Pay invoice TP-5530")]))
            .Descendants(AgentConversationReadings.TaskElementName)
            .Single();

        // Assert
        Assert.Equal(state, task.Attribute(AgentConversationReadings.StateAttributeName)?.Value);
        Assert.Equal(writtenOrigin, task.Attribute(AgentConversationReadings.OriginAttributeName)?.Value);
        Assert.Equal("2026-10-16", task.Attribute(AgentConversationReadings.DueAttributeName)?.Value);
        Assert.Equal("[done] [owned by me] Pay invoice TP-5530", task.Element(AgentConversationReadings.TitleElementName)?.Value);
    }

    private static XElement Parsed(string document) => XElement.Parse(document);
}
