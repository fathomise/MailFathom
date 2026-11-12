// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using System.Xml.Linq;
using MailFathom.AI.AgentConversations;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Discovery.Presentation.Citations;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;
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

    /// <summary>How long the whole body is travels beside how much is shown, so the opening of a long message is not read as all of it.</summary>
    [Fact]
    public void Thread_ABodyWithAStatedLength_SaysHowLongTheWholeIsBesideHowMuchIsShown()
    {
        // Act
        var message = Parsed(AgentConversationReadings.Thread(
            [OneMessage() with { BodyCharacters = 12_000, BodyShown = 4_000 }]))
            .Element(AgentConversationReadings.MessageElementName)!;

        // Assert
        Assert.Equal("12000", message.Attribute(AgentConversationReadings.BodyCharactersAttributeName)?.Value);
        Assert.Equal("4000", message.Attribute(AgentConversationReadings.BodyShownAttributeName)?.Value);
    }

    /// <summary>Every file is named, a read one with how much was read and an unread one with why, so none is passed over as though it were not there.</summary>
    [Fact]
    public void Thread_AMessageCarryingAReadAndAnUnreadFile_NamesBothAndSaysWhatEachCameTo()
    {
        // Arrange
        GuardedAttachmentReading[] attachments =
        [
            new(0, "contract.pdf", "application/pdf", 48_213, new AttachmentTextReading(0, AttachmentTextKind.Document, "Extracted", 14, AttachmentTextSegmentKind.Page, 38_211)),
            new(1, "archive.zip", "application/zip", 1_024, new AttachmentTextReading(1, AttachmentTextKind.Document, "UnsupportedFormat", 0, null, 0)),
            new(2, FileName: null, "image/png", 512, Reading: null),
        ];

        // Act
        var files = Parsed(AgentConversationReadings.Thread([OneMessage() with { Attachments = attachments }]))
            .Descendants(AgentConversationReadings.AttachedFileElementName)
            .ToArray();

        // Assert
        Assert.Equal(
            [
                ("0", "contract.pdf", "14", "Page", "38211", null),
                ("1", "archive.zip", null, null, null, "UnsupportedFormat"),
                ("2", null, null, null, null, AgentConversationReadings.NeverReadReason),
            ],
            files.Select(static file => (
                file.Attribute(AgentConversationReadings.AttachmentAttributeName)?.Value,
                file.Attribute(AgentConversationReadings.FileAttributeName)?.Value,
                file.Attribute(AgentConversationReadings.PagesAttributeName)?.Value,
                file.Attribute(AgentConversationReadings.PageKindAttributeName)?.Value,
                file.Attribute(AgentConversationReadings.CharactersAttributeName)?.Value,
                file.Attribute(AgentConversationReadings.UnreadAttributeName)?.Value)));
        Assert.Equal("48213", files[0].Attribute(AgentConversationReadings.SizeAttributeName)?.Value);
        Assert.Equal(AgentConversationReadings.DocumentReadAs, files[0].Attribute(AgentConversationReadings.ReadAsAttributeName)?.Value);
    }

    /// <summary>A page says what part of the whole it is and where the next begins, and the last page names no next one.</summary>
    [Theory]
    [InlineData(4_000, 8_000, "8000", "true")]
    [InlineData(8_000, 10_000, null, null)]
    public void MessagePage_APageOfALongBody_StatesItsPlaceInTheWhole(int start, int end, string? next, string? cut)
    {
        // Act
        var page = Parsed(AgentConversationReadings.MessagePage(new GuardedMessagePage(
            StoredEmailId.Create(Guid.CreateVersion7()),
            PresentationCitationId.Create("m2"),
            new PageBounds(10_000, start, end, CeilingReached: false),
            "the rest of the minutes")));

        // Assert
        Assert.Equal(AgentConversationReadings.MessagePageElementName, page.Name.LocalName);
        Assert.Equal("m2", page.Attribute(AgentConversationReadings.SourceAttributeName)?.Value);
        Assert.Equal("10000", page.Attribute(AgentConversationReadings.CharactersAttributeName)?.Value);
        Assert.Equal(start.ToString(CultureInfo.InvariantCulture), page.Attribute(AgentConversationReadings.PageStartAttributeName)?.Value);
        Assert.Equal(end.ToString(CultureInfo.InvariantCulture), page.Attribute(AgentConversationReadings.PageEndAttributeName)?.Value);
        Assert.Equal(next, page.Attribute(AgentConversationReadings.NextAttributeName)?.Value);
        Assert.Equal(cut, page.Attribute(AgentTurnDocument.CutAttributeName)?.Value);
        Assert.Equal("the rest of the minutes", page.Value);
    }

    [Fact]
    public void MessagePage_APageTheRunsCeilingCut_SaysSo()
    {
        // Act
        var page = Parsed(AgentConversationReadings.MessagePage(new GuardedMessagePage(
            StoredEmailId.Create(Guid.CreateVersion7()),
            PresentationCitationId.Create("m1"),
            new PageBounds(10_000, 0, 1_200, CeilingReached: true),
            "the opening")));

        // Assert
        Assert.Equal("true", page.Attribute(AgentConversationReadings.CeilingReachedAttributeName)?.Value);
    }

    /// <summary>Each part names its page as an attribute the service wrote, and a model's account of a picture is marked as one.</summary>
    [Fact]
    public void AttachmentPage_APageCrossingTwoPlacesOfAPictureDescription_QuotesEachUnderItsPlaceAndMarksTheKind()
    {
        // Act
        var page = Parsed(AgentConversationReadings.AttachmentPage(new GuardedAttachmentPage(
            StoredEmailId.Create(Guid.CreateVersion7()),
            PresentationCitationId.Create("m1"),
            AttachmentPosition: 1,
            "scan.pdf",
            AttachmentTextKind.ImageDescription,
            PageCount: 2,
            AttachmentTextSegmentKind.Page,
            new PageBounds(900, 0, 900, CeilingReached: false),
            [
                new GuardedAttachmentPart(new AttachmentTextSegment(AttachmentTextSegmentKind.Page, 1, Label: null, StartOffset: 0), "A signed form."),
                new GuardedAttachmentPart(new AttachmentTextSegment(AttachmentTextSegmentKind.Page, 2, Label: null, StartOffset: 450), "A stamp."),
            ])));

        // Assert
        Assert.Equal("1", page.Attribute(AgentConversationReadings.AttachmentAttributeName)?.Value);
        Assert.Equal("scan.pdf", page.Attribute(AgentConversationReadings.FileAttributeName)?.Value);
        Assert.Equal(AgentConversationReadings.PictureDescriptionReadAs, page.Attribute(AgentConversationReadings.ReadAsAttributeName)?.Value);
        Assert.Equal("2", page.Attribute(AgentConversationReadings.PagesAttributeName)?.Value);
        Assert.Null(page.Attribute(AgentConversationReadings.NextAttributeName));
        Assert.Equal(
            [("1", "A signed form."), ("2", "A stamp.")],
            page.Elements(AgentConversationReadings.PartElementName).Select(static part => (
                part.Attribute(AgentConversationReadings.SegmentNumberAttributeName)?.Value,
                part.Value)));
    }

    [Fact]
    public void AttachmentUnread_AFileNothingCouldRead_NamesItAndTheReason()
    {
        // Act
        var unread = Parsed(AgentConversationReadings.AttachmentUnread(
            StoredEmailId.Create(Guid.CreateVersion7()),
            PresentationCitationId.Create("m1"),
            new GuardedAttachmentReading(0, "plans.dwg", "application/acad", 9_000, new AttachmentTextReading(0, AttachmentTextKind.Document, "UnsupportedFormat", 0, null, 0))));

        // Assert
        Assert.Equal(AgentConversationReadings.AttachmentUnreadElementName, unread.Name.LocalName);
        Assert.Equal("plans.dwg", unread.Attribute(AgentConversationReadings.FileAttributeName)?.Value);
        Assert.Equal("UnsupportedFormat", unread.Attribute(AgentConversationReadings.UnreadAttributeName)?.Value);
    }

    private static GuardedThreadReading OneMessage() => new(
        StoredEmailId.Create(Guid.CreateVersion7()),
        PresentationCitationId.Create("m1"),
        SenderAddress: null,
        SentAt: null,
        StubEmailProvenanceReader.KnownCorrespondent,
        Subject: null,
        "text",
        BodyCut: false);

    private static XElement Parsed(string document) => XElement.Parse(document);
}
