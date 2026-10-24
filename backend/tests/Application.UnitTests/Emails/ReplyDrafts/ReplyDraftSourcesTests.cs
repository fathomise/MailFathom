// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ReplyDrafts;

/// <summary>Covers how the reads of several context messages become the one correspondence a drafting is written from.</summary>
public sealed class ReplyDraftSourcesTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Merged_OneRead_ReturnsItAsItIs()
    {
        // Arrange
        var only = Read("The racking quotation", [Message(Monday, "first")], ["karolina@example.test"], ["Best, Maren"]);

        // Act
        var merged = ReplyDraftSources.Merged([only], MailReplyDrafting.MaximumMessages);

        // Assert
        Assert.Same(only, merged);
    }

    /// <summary>Two context messages of one conversation read the same messages, which the turn carries once.</summary>
    [Fact]
    public void Merged_TwoReadsOfOneConversation_CarriesEachMessageOnce()
    {
        // Arrange
        var messages = new[] { Message(Monday, "first"), Message(Monday.AddDays(1), "second") };

        // Act
        var merged = ReplyDraftSources.Merged(
            [Read("Subject", messages, [], []), Read("Subject", messages, [], [])],
            MailReplyDrafting.MaximumMessages);

        // Assert
        Assert.Equal(messages.Select(static message => message.StoredEmailId), merged.Messages.Select(static message => message.StoredEmailId));
    }

    /// <summary>Reads of two conversations interleave by when each message was written, and every position is the place it now holds.</summary>
    [Fact]
    public void Merged_ReadsOfTwoConversations_OrdersTheMessagesByWhenTheyWereWrittenAndNumbersThemAfresh()
    {
        // Arrange
        var firstOfA = Message(Monday, "a1");
        var lastOfA = Message(Monday.AddDays(2), "a2");
        var onlyOfB = Message(Monday.AddDays(1), "b1");

        // Act
        var merged = ReplyDraftSources.Merged(
            [Read("A", [firstOfA, lastOfA], [], []), Read("B", [onlyOfB], [], [])],
            MailReplyDrafting.MaximumMessages);

        // Assert
        Assert.Equal(["a1", "b1", "a2"], merged.Messages.Select(static message => message.Text));
        Assert.Equal([0, 1, 2], merged.Messages.Select(static message => message.Position));
    }

    /// <summary>A message nobody dated is placed before every dated one, in read order, so it is the first a bound drops.</summary>
    [Fact]
    public void Merged_AMessageCarryingNoDate_IsPlacedBeforeEveryDatedOne()
    {
        // Arrange
        var undated = Message(sentAt: null, "undated");

        // Act
        var merged = ReplyDraftSources.Merged(
            [Read("A", [Message(Monday, "dated")], [], []), Read("B", [undated], [], [])],
            MailReplyDrafting.MaximumMessages);

        // Assert
        Assert.Equal(["undated", "dated"], merged.Messages.Select(static message => message.Text));
    }

    /// <summary>A reply answers where the exchanges got to, so a merge past the bound keeps the most recent end of the whole.</summary>
    [Fact]
    public void Merged_ReadsPastTheBound_KeepsTheMostRecentMessages()
    {
        // Arrange
        var a = Enumerable.Range(0, 3).Select(day => Message(Monday.AddDays(day * 2), $"a{day}")).ToArray();
        var b = Enumerable.Range(0, 3).Select(day => Message(Monday.AddDays((day * 2) + 1), $"b{day}")).ToArray();

        // Act
        var merged = ReplyDraftSources.Merged([Read("A", a, [], []), Read("B", b, [], [])], maximumMessages: 3);

        // Assert
        Assert.Equal(["b1", "a2", "b2"], merged.Messages.Select(static message => message.Text));
        Assert.Equal([0, 1, 2], merged.Messages.Select(static message => message.Position));
    }

    /// <summary>One correspondent writing in both conversations is one person a reply may be proposed to.</summary>
    [Fact]
    public void Merged_ACorrespondentInBothConversations_IsNumberedOnce()
    {
        // Act
        var merged = ReplyDraftSources.Merged(
            [
                Read("A", [Message(Monday, "a")], ["karolina@example.test", "jan@example.test"], []),
                Read("B", [Message(Monday, "b")], ["KAROLINA@example.test", "ewa@example.test"], []),
            ],
            MailReplyDrafting.MaximumMessages);

        // Assert
        Assert.Equal(
            ["karolina@example.test", "jan@example.test", "ewa@example.test"],
            merged.Participants.Select(static participant => participant.Address.Address));
        Assert.Equal([0, 1, 2], merged.Participants.Select(static participant => participant.Position));
    }

    /// <summary>A reply is written in one account's manner under the subject of the message it was first asked about.</summary>
    [Fact]
    public void Merged_ReadsOfTwoConversations_TakesTheSubjectAndTheMannerFromTheFirstAlone()
    {
        // Act
        var merged = ReplyDraftSources.Merged(
            [
                Read("The racking quotation", [Message(Monday, "a")], [], ["Best, Maren"]),
                Read("The delivery", [Message(Monday, "b")], [], ["Cheers, M."]),
            ],
            MailReplyDrafting.MaximumMessages);

        // Assert
        Assert.Equal("The racking quotation", merged.Subject);
        Assert.Equal(["Best, Maren"], merged.StyleSamples);
    }

    /// <summary>The person's own message in a second context conversation is a message of the correspondence, never also a sample of their manner.</summary>
    [Fact]
    public void Merged_ASampleThatOpensAMessageOfAnotherConversation_IsLeftOutOfTheManner()
    {
        // Arrange
        var sentInTheSecond = Message(Monday, "Thanks, Vasco - the bays are booked for Friday.");

        // Act
        var merged = ReplyDraftSources.Merged(
            [
                Read("The racking quotation", [Message(Monday, "a")], [], ["Thanks, Vasco - the bays", "Best, Maren"]),
                Read("The delivery", [sentInTheSecond], [], []),
            ],
            MailReplyDrafting.MaximumMessages);

        // Assert
        Assert.Equal(["Best, Maren"], merged.StyleSamples);
    }

    private static ReplyDraftMessage Message(DateTimeOffset? sentAt, string text) =>
        new(StoredEmailId.Create(Guid.CreateVersion7()), Position: 0, "Karolina", sentAt, text);

    private static ReplyDraftSources Read(
        string subject,
        IReadOnlyList<ReplyDraftMessage> messages,
        IReadOnlyList<string> participantAddresses,
        IReadOnlyList<string> styleSamples) =>
        new(
            subject,
            [.. messages.Select(static (message, position) => message with { Position = position })],
            [
                .. participantAddresses.Select(static (address, position) =>
                {
                    EmailAddress.TryCreate(displayName: null, address, out var parsed);

                    return new ReplyDraftParticipant(position, parsed);
                }),
            ],
            styleSamples);
}
