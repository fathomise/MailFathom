// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.ReplyDrafts;
using MailFathom.Application.Emails.ReplyDrafts;
using Xunit;

namespace MailFathom.AI.UnitTests.ReplyDrafts;

/// <summary>Covers what survives the reading of a model's answer to a passage rewriting.</summary>
/// <remarks>
/// An answer is all or nothing, so what is asserted is that one text per passage reads, that anything else reads as
/// nothing rather than as a guess about which text belongs where, and that what is read is fit to stand inside a paragraph.
/// </remarks>
public sealed class PassageRewriteReadingTests
{
    [Fact]
    public void Read_OneTextForEveryPassage_ReadsThemInOrder()
    {
        // Act
        var rewrite = PassageRewriteReading.Read("""{"passages": ["Tuesday suits us.", "Thanks."]}""", passageCount: 2);

        // Assert
        Assert.True(rewrite.WasWritten);
        Assert.Equal(["Tuesday suits us.", "Thanks."], rewrite.Passages);
    }

    /// <summary>A model told to answer with one object still fences it or writes around it, which loses nothing.</summary>
    [Fact]
    public void Read_AFencedAnswerWithProseAroundIt_ReadsTheObject()
    {
        // Arrange
        const string answer = """
            Here are the passages:
            ```json
            {"passages": ["Tuesday suits us."]}
            ```
            """;

        // Act
        var rewrite = PassageRewriteReading.Read(answer, passageCount: 1);

        // Assert
        Assert.Equal(["Tuesday suits us."], rewrite.Passages);
    }

    /// <summary>A text too few or too many leaves no way to tell which passage each belongs to.</summary>
    [Theory]
    [InlineData("""{"passages": ["Tuesday suits us."]}""")]
    [InlineData("""{"passages": ["Tuesday suits us.", "Thanks.", "Best, Ola."]}""")]
    [InlineData("""{"passages": []}""")]
    [InlineData("""{"rewritten": ["Tuesday suits us.", "Thanks."]}""")]
    public void Read_AnAnswerNotCarryingOneTextPerPassage_ReadsNothing(string answer)
    {
        // Act
        var rewrite = PassageRewriteReading.Read(answer, passageCount: 2);

        // Assert
        Assert.False(rewrite.WasWritten);
        Assert.Empty(rewrite.Passages);
    }

    /// <summary>A blank text would read as a passage the person asked to have deleted, which nobody asked.</summary>
    [Theory]
    [InlineData("""{"passages": ["Tuesday suits us.", "  "]}""")]
    [InlineData("""{"passages": ["Tuesday suits us.", null]}""")]
    public void Read_AnAnswerLeavingAPassageBlank_ReadsNothing(string answer)
    {
        // Act
        var rewrite = PassageRewriteReading.Read(answer, passageCount: 2);

        // Assert
        Assert.False(rewrite.WasWritten);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Tuesday suits us.")]
    [InlineData("""{"passages": "Tuesday suits us."}""")]
    public void Read_AnAnswerHoldingNoReadableObject_ReadsNothing(string? answer)
    {
        // Act
        var rewrite = PassageRewriteReading.Read(answer, passageCount: 1);

        // Assert
        Assert.False(rewrite.WasWritten);
    }

    /// <summary>A passage stands inside one paragraph, so the space around it is dropped and a line break inside it is read as a space.</summary>
    [Fact]
    public void Read_ATextWithSpaceAroundItAndALineBreakInside_ReadsItAsOneTrimmedLine()
    {
        // Act
        var rewrite = PassageRewriteReading.Read("""{"passages": ["  Tuesday suits us.\nSee you then.  "]}""", passageCount: 1);

        // Assert
        Assert.Equal(["Tuesday suits us. See you then."], rewrite.Passages);
    }

    /// <summary>A passage stands inside a draft, so one rewritten past the draft's own bound is shortened to it.</summary>
    [Fact]
    public void Read_ATextPastTheBound_ShortensItToTheBound()
    {
        // Arrange
        var answer = $$"""{"passages": ["{{new string('a', PassageRewrite.MaximumPassageLength + 10)}}"]}""";

        // Act
        var rewrite = PassageRewriteReading.Read(answer, passageCount: 1);

        // Assert
        Assert.Equal(PassageRewrite.MaximumPassageLength, Assert.Single(rewrite.Passages).Length);
    }
}
