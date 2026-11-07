// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.Retrieval;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the rules the run's own instruction has to carry, which are the whole of what decides them.</summary>
/// <remarks>
/// A rule stated only in the instruction text is a rule nothing else in the process implements, so the text is where it
/// is asserted. What each assertion protects is a behavior an operator would meet as a wrong answer rather than as a
/// failure: an answer written in the language of the mail instead of the language of the question, or a mailbox
/// reported as holding nothing because every lookup was worded in a language it does not carry.
/// </remarks>
public sealed class MailAnsweringInstructionsTests
{
    /// <summary>The instruction as one line, so an assertion is about what it says rather than about where it wraps.</summary>
    private static readonly string Instruction = MailAnsweringInstructions.Text.ReplaceLineEndings(" ");

    /// <summary>The language of an answer follows the question, because that is the one thing the caller did ask for.</summary>
    [Fact]
    public void Text_TheInstruction_StatesThatAnAnswerIsWrittenInTheLanguageOfTheQuestion()
    {
        // Assert
        Assert.Contains(
            "in the language the question was asked in, whatever language the mail is in",
            Instruction,
            StringComparison.Ordinal);
    }

    /// <summary>A quotation checked against the mail it came from has to be the words the mail carried.</summary>
    [Fact]
    public void Text_TheInstruction_StatesThatQuotedMailKeepsItsOwnWording()
    {
        // Assert
        Assert.Contains("in its own wording", Instruction, StringComparison.Ordinal);
        Assert.Contains("rendering into the question's language", Instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// The lexical half of retrieval matches a word against a word, so a lookup worded in the language of the question
    /// reaches mail written in another language only as far as the vector half happens to carry it.
    /// </summary>
    [Fact]
    public void Text_TheInstruction_StatesThatALookupIsWordedInTheLanguageTheMailIsLikelyWrittenIn()
    {
        // Assert
        Assert.Contains(ScopedMailKnowledgeRetrieval.QueryArgumentName, Instruction, StringComparison.Ordinal);
        Assert.Contains(
            "in the language that mail is likely written in, which need not be the language you were asked in",
            Instruction,
            StringComparison.Ordinal);
    }

    /// <summary>One empty lookup is not a mailbox without an answer, and another language is one of the things to try before saying it is.</summary>
    [Fact]
    public void Text_TheInstruction_StatesThatAnEmptyLookupIsRetriedInAnotherLanguage()
    {
        // Assert
        Assert.Contains("another language this mailbox plausibly holds", Instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every word of a lexical lookup is required and none is stemmed, so a lookup that found nothing is as often too long
    /// or in the wrong grammatical form as it is in the wrong language.
    /// </summary>
    [Fact]
    public void Text_TheInstruction_StatesThatAnEmptyLookupIsRetriedWithFewerWordsOrAnotherForm()
    {
        // Assert
        Assert.Contains("try fewer words", Instruction, StringComparison.Ordinal);
        Assert.Contains("the other forms an inflected word takes", Instruction, StringComparison.Ordinal);
    }

    /// <summary>A quotation on its own is written in the mail's language, so the answer around it is what keeps the reply in the question's.</summary>
    [Fact]
    public void Text_TheInstruction_StatesThatAQuotationNeverStandsInForTheAnswer()
    {
        // Assert
        Assert.Contains("A quotation never stands in for the answer", Instruction, StringComparison.Ordinal);
    }

    /// <summary>A citation shortened to its first characters names no message, so the claim beside it cannot be checked.</summary>
    [Fact]
    public void Text_TheInstruction_AsksForTheCitedIdentifierInFull()
    {
        // Assert
        Assert.Contains("Write the identifier in full, exactly as the attribute carries it, never shortened", Instruction, StringComparison.Ordinal);
    }

    /// <summary>
    /// A filter the question did not state hides mail that could answer it, and the turn's clock read as a period is the
    /// shape that did it: a question naming no period searched from the first of the month and reported nothing found.
    /// </summary>
    [Fact]
    public void Text_TheInstruction_SetsNoReceivedBoundOrAttachmentFilterTheQuestionDoesNotState()
    {
        // Assert
        Assert.Contains("A question that names no period gets no received bounds", Instruction, StringComparison.Ordinal);
        Assert.Contains("one that asks about no attachment gets no attachment filter", Instruction, StringComparison.Ordinal);
        Assert.Contains("never a period on its own", Instruction, StringComparison.Ordinal);
    }

    /// <summary>A period the question does state is still applied as a filter, resolved against the turn's own clock.</summary>
    [Fact]
    public void Text_TheInstruction_ResolvesAStatedPeriodAgainstTheTurnsClock()
    {
        // Assert
        Assert.Contains("a period the question states or plainly implies", Instruction, StringComparison.Ordinal);
        Assert.Contains("resolve \"this week\", \"since Tuesday\" and \"last quarter\" against what the turn states", Instruction, StringComparison.Ordinal);
        Assert.Contains(AnchoredInstant.WrittenForm, Instruction, StringComparison.Ordinal);
    }
}
