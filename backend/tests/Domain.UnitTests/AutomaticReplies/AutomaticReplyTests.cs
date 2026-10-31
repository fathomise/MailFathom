// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using Xunit;

namespace MailFathom.Domain.UnitTests.AutomaticReplies;

/// <summary>Covers what an automatic reply is, when it answers, and which of its two texts a sender is answered with.</summary>
public sealed class AutomaticReplyTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Private = MailAccountId.Create("private");

    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset End = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The period is half-open: it answers from its start, and stops at its end rather than after it.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(7 * 24 * 60, false)]
    public void IsAnswering_AnInstantAgainstThePeriod_AnswersWithinItAlone(int minutesFromStart, bool expected)
    {
        // Arrange
        var reply = Reply();

        // Act
        var answering = reply.IsAnswering(Work, Start.AddMinutes(minutesFromStart));

        // Assert
        Assert.Equal(expected, answering);
    }

    /// <summary>A reply switched off before its period ends answers nothing, inside the period included.</summary>
    [Fact]
    public void IsAnswering_SwitchedOff_AnswersNothing()
    {
        // Arrange
        var reply = Reply(isOn: false);

        // Act
        var answering = reply.IsAnswering(Work, Start.AddHours(1));

        // Assert
        Assert.False(answering);
    }

    /// <summary>Mail arriving on an account the reply does not name is not answered.</summary>
    [Fact]
    public void IsAnswering_AnAccountTheReplyDoesNotName_AnswersNothing()
    {
        // Arrange
        var reply = Reply();

        // Act
        var answering = reply.IsAnswering(Private, Start.AddHours(1));

        // Assert
        Assert.False(answering);
    }

    /// <summary>A sender inside the organization is answered with the text for colleagues, and everybody else with the other.</summary>
    [Theory]
    [InlineData(true, "Back on Monday; ask Jan meanwhile.")]
    [InlineData(false, "I am away until Monday.")]
    public void TextFor_EachSideOfTheOrganization_AnswersWithItsOwnText(bool inside, string expected)
    {
        // Arrange
        var reply = Reply(insideText: "Back on Monday; ask Jan meanwhile.");

        // Act
        var text = reply.TextFor(inside);

        // Assert
        Assert.Equal(expected, text);
    }

    /// <summary>With no text for colleagues, a colleague is answered with the text everybody else gets.</summary>
    [Fact]
    public void TextFor_NoTextForColleagues_AnswersAColleagueWithTheOtherText()
    {
        // Arrange
        var reply = Reply(insideText: "   ");

        // Act
        var text = reply.TextFor(senderIsInsideOrganization: true);

        // Assert
        Assert.Null(reply.InsideText);
        Assert.Equal("I am away until Monday.", text);
    }

    /// <summary>What is not a reply is refused with a reason rather than repaired.</summary>
    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("I am away.", 0, 1)]
    [InlineData("I am away.", 1, 0)]
    [InlineData("I am away.", 1, -1)]
    public void TryCreate_WhatIsNotAReply_IsRefusedWithAReason(string outsideText, int accountCount, int days)
    {
        // Act
        var created = AutomaticReply.TryCreate(
            insideText: null,
            outsideText,
            [.. new[] { Work }.Take(accountCount)],
            Start,
            Start.AddDays(days),
            isOn: true,
            out var reply,
            out var refusal);

        // Assert
        Assert.False(created);
        Assert.Null(reply);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
    }

    /// <summary>One account named twice is refused, and so is a text longer than the bound.</summary>
    [Fact]
    public void TryCreate_AnAccountNamedTwiceOrATextTooLong_IsRefused()
    {
        // Act
        var twice = AutomaticReply.TryCreate(null, "Away.", [Work, Work], Start, End, true, out _, out _);
        var tooLong = AutomaticReply.TryCreate(
            new string('x', AutomaticReply.MaximumTextLength + 1),
            "Away.",
            [Work],
            Start,
            End,
            true,
            out _,
            out _);

        // Assert
        Assert.False(twice);
        Assert.False(tooLong);
    }

    private static AutomaticReply Reply(string? insideText = null, bool isOn = true)
    {
        Assert.True(AutomaticReply.TryCreate(
            insideText,
            "I am away until Monday.",
            [Work],
            Start,
            End,
            isOn,
            out var reply,
            out _));

        return reply!;
    }
}
