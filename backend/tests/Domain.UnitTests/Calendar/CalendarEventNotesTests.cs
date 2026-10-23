// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.Domain.UnitTests.Calendar;

/// <summary>Covers what an event's notes are trimmed to, which layout they keep, and which characters they refuse.</summary>
public sealed class CalendarEventNotesTests
{
    [Fact]
    public void Create_SurroundingWhitespace_IsTrimmedAndTheTextIsKept()
    {
        // Act
        var notes = CalendarEventNotes.Create("  Bring the slides  \n");

        // Assert
        Assert.Equal("Bring the slides", notes.Value);
    }

    /// <summary>Notes are read as somebody wrote them, so the line breaks and tabs that lay them out are kept.</summary>
    [Theory]
    [InlineData("Agenda:\n- budget\n- hiring")]
    [InlineData("Agenda:\r\n- budget")]
    [InlineData("Room:\t4.12")]
    public void Create_LineBreaksAndTabs_AreKept(string value)
    {
        // Act
        var notes = CalendarEventNotes.Create(value);

        // Assert
        Assert.Equal(value, notes.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void TryCreate_NothingToRead_IsNotNotes(string? value)
    {
        // Act
        var read = CalendarEventNotes.TryCreate(value, out var notes);

        // Assert
        Assert.False(read);
        Assert.False(notes.IsSpecified);
    }

    [Fact]
    public void Create_AtTheBound_IsAccepted()
    {
        // Arrange
        var longest = new string('a', CalendarEventNotes.MaximumLength);

        // Act
        var notes = CalendarEventNotes.Create(longest);

        // Assert
        Assert.Equal(CalendarEventNotes.MaximumLength, notes.Value.Length);
    }

    [Fact]
    public void Create_LongerThanTheBound_IsRefused()
    {
        // Arrange
        var overlong = new string('a', CalendarEventNotes.MaximumLength + 1);

        // Act
        var refusal = Assert.Throws<ArgumentException>(() => CalendarEventNotes.Create(overlong));

        // Assert
        Assert.Equal("value", refusal.ParamName);
    }

    /// <summary>Line breaks aside, a character that renders as nothing could make the notes read as text they do not contain.</summary>
    [Theory]
    [InlineData("Bring\u0000the slides")]
    [InlineData("Bring\u001Bthe slides")]
    [InlineData("Bring‮the slides")]
    [InlineData("Bring​the slides")]
    [InlineData("Bring the slides")]
    [InlineData("Bring the slides")]
    public void Create_ACharacterThatCarriesNoGlyph_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => CalendarEventNotes.Create(value));
    }

    [Fact]
    public void Create_AnUnpairedSurrogate_IsRefused()
    {
        // Arrange
        var illFormed = "Bring" + (char)0xD800 + "the slides";

        // Act, Assert
        Assert.Throws<ArgumentException>(() => CalendarEventNotes.Create(illFormed));
    }
}
