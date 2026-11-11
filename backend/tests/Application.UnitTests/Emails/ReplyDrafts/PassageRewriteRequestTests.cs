// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.ReplyDrafts;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.ReplyDrafts;

/// <summary>Covers which passage rewritings a request admits, and that a refusal names the rule without quoting the draft.</summary>
public sealed class PassageRewriteRequestTests
{
    /// <summary>Twenty-eight characters: the first sentence fills 0 to 11, a space stands at 12, and the second runs to the end.</summary>
    private const string Draft = "Thanks Anna. We accept, Ola.";

    /// <summary>Gets one candidate per rule a request can break, each breaking that rule alone.</summary>
    public static TheoryData<string?, DraftPassage[]?, string?> Refused => new()
    {
        { null, [new DraftPassage(0, 5)], "Shorter" },
        { "   ", [new DraftPassage(0, 2)], "Shorter" },
        { new string('a', PassageRewriteRequest.MaximumTextLength + 1), [new DraftPassage(0, 5)], "Shorter" },
        { Draft, null, "Shorter" },
        { Draft, [], "Shorter" },
        { Draft, [.. Enumerable.Range(0, PassageRewriteRequest.MaximumPassages + 1).Select(static start => new DraftPassage(start, 1))], "Shorter" },
        { Draft, [new DraftPassage(0, 0)], "Shorter" },
        { Draft, [new DraftPassage(-1, 3)], "Shorter" },
        { Draft, [new DraftPassage(20, 20)], "Shorter" },
        { Draft, [new DraftPassage(int.MaxValue, int.MaxValue)], "Shorter" },
        { Draft, [new DraftPassage(13, 5), new DraftPassage(0, 11)], "Shorter" },
        { Draft, [new DraftPassage(0, 11), new DraftPassage(10, 5)], "Shorter" },
        { Draft, [new DraftPassage(12, 1)], "Shorter" },
        { Draft, [new DraftPassage(0, 11)], null },
        { Draft, [new DraftPassage(0, 11)], " " },
        { Draft, [new DraftPassage(0, 11)], new string('i', ReplyDraftRequest.MaximumInstructionLength + 1) },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void RefusalOf_ACandidateBreakingOneRule_NamesItWithoutQuotingTheDraft(
        string? text,
        DraftPassage[]? passages,
        string? instruction)
    {
        // Act
        var refusal = PassageRewriteRequest.RefusalOf(text, passages, instruction);

        // Assert
        Assert.NotNull(refusal);
        Assert.DoesNotContain("Anna", refusal, StringComparison.Ordinal);
    }

    /// <summary>Two passages meeting edge to edge are two neighbouring sentences of one paragraph, which is not an overlap.</summary>
    [Fact]
    public void Constructor_PassagesInOrderMeetingEdgeToEdge_IsAdmitted()
    {
        // Act
        var request = new PassageRewriteRequest(Draft, [new DraftPassage(0, 12), new DraftPassage(12, 16)], "Shorter");

        // Assert
        Assert.Equal([new DraftPassage(0, 12), new DraftPassage(12, 16)], request.Passages);
    }

    /// <summary>The invariants hold for every caller, so a request built past the boundary's own checks is refused rather than handed on.</summary>
    [Fact]
    public void Constructor_APassageOutsideTheDraft_IsRefusedAsAnArgument() =>
        Assert.Throws<ArgumentException>(() => new PassageRewriteRequest(Draft, [new DraftPassage(20, 20)], "Shorter"));
}
