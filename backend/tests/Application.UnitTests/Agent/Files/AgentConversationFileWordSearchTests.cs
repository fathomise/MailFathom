// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Agent.Files;
using Xunit;

namespace MailFathom.Application.UnitTests.Agent.Files;

/// <summary>Covers how a conversation's passages are ranked by the words of a query.</summary>
public sealed class AgentConversationFileWordSearchTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rank_PassagesHoldingSomeOfTheWords_PutsTheOneHoldingMostFirstAndLeavesOutTheOnesHoldingNone()
    {
        // Arrange
        var handbook = Document("019a0000-0000-7000-8000-0000000000e1", "The car park closes at 19:00. The spare key for the bike shed is in drawer B. Lunch is at noon.");
        var passages = Passages(handbook, (0, 29), (30, 77), (78, 95));

        // Act
        var ranked = AgentConversationFileWordSearch.Rank("car park spare key bike shed", [(handbook, passages)], limit: 5);

        // Assert
        Assert.Equal([passages[1], passages[0]], ranked);
    }

    /// <summary>A long word is matched by its stem, so a query's plural finds a passage's singular whatever the case.</summary>
    [Fact]
    public void Rank_ALongWordInAnotherForm_FindsThePassageByItsStem()
    {
        // Arrange
        var ledger = Document("019a0000-0000-7000-8000-0000000000e2", "Each INVOICE is paid within thirty days.");
        var passages = Passages(ledger, (0, 40));

        // Act
        var ranked = AgentConversationFileWordSearch.Rank("invoices", [(ledger, passages)], limit: 5);

        // Assert
        Assert.Equal(passages, ranked);
    }

    [Fact]
    public void Rank_AQueryOfNoWord_FindsNothing()
    {
        // Arrange
        var notes = Document("019a0000-0000-7000-8000-0000000000e3", "a b c");

        // Act
        var ranked = AgentConversationFileWordSearch.Rank("? !", [(notes, Passages(notes, (0, 5)))], limit: 5);

        // Assert
        Assert.Empty(ranked);
    }

    private static AgentConversationFileText Document(string id, string text) =>
        new(
            new AgentConversationFile(AgentConversationFileId.Create(new Guid(id)), "notes.txt", AgentConversationFileKind.Document, "text/plain", text.Length, Now, Reading: null),
            text,
            []);

    private static AgentConversationFilePassage[] Passages(AgentConversationFileText document, params (int Start, int End)[] spans) =>
        [.. spans.Select((span, ordinal) => new AgentConversationFilePassage(document.File.Id, ordinal, span.Start, span.End, Segment: null))];
}
