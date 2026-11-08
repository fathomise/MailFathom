// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.ThreadStates;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.AI.UnitTests.ThreadStates;

/// <summary>Covers what survives the reading of a model's answer about where a conversation stands.</summary>
/// <remarks>
/// Everything here is about an answer this system did not write. What is asserted is that a usable answer is read, that
/// an answer with no object to read is withheld rather than settled or raised, that an object whose statements all fell
/// away still settles, and above all that no statement leaves the reading citing a message the turn did not publish.
/// </remarks>
public sealed class ThreadStateReadingTests
{
    private static readonly StoredEmailId First = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Second = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public void Read_AnAnswerAboutEveryAspect_ReadsThemInAspectOrder()
    {
        // Arrange
        const string answer = """
            {
              "agreements": [{ "text": "The response time stays at two hours.", "messages": [0] }],
              "openQuestions": [{ "text": "The upper limit is unsettled.", "messages": [1] }],
              "commitments": [
                { "text": "Karolina sends the figures.", "messages": [1], "owedBy": "Karolina", "dueAt": "2026-09-12" }
              ],
              "differences": [{ "text": "The second quotation raises the price by eight percent.", "messages": [0, 1] }]
            }
            """;

        // Act
        var entries = ThreadStateReading.Read(answer, Messages(), topicShown: false).Entries;

        // Assert
        Assert.Equal(
            [
                ThreadStateAspect.Agreement,
                ThreadStateAspect.OpenQuestion,
                ThreadStateAspect.Commitment,
                ThreadStateAspect.VersionDifference,
            ],
            entries.Select(static entry => entry.Aspect));
        Assert.Equal([First], entries[0].Sources);
        Assert.Equal("Karolina", entries[2].OwedBy);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), entries[2].DueAt);
        Assert.Equal([First, Second], entries[3].Sources);
    }

    /// <summary>The whole point of numbering the turn: a citation is a position this deployment published.</summary>
    [Theory]
    [InlineData("[-1]")]
    [InlineData("[7]")]
    [InlineData("[]")]
    public void Read_AStatementCitingAMessageTheTurnNeverPublished_DropsTheStatement(string messages)
    {
        // Arrange
        var answer = $$"""{ "agreements": [{ "text": "They settled the price.", "messages": {{messages}} }] }""";

        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown: false);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Empty(derivation.Entries);
    }

    /// <summary>A citation partly outside the turn keeps the part inside it rather than losing the statement.</summary>
    [Fact]
    public void Read_AStatementCitingOnePublishedMessageAndOneNot_KeepsThePublishedOne()
    {
        // Arrange
        const string answer =
            """{ "agreements": [{ "text": "They settled the price.", "messages": [9, 1] }] }""";

        // Act
        var entries = ThreadStateReading.Read(answer, Messages(), topicShown: false).Entries;

        // Assert
        Assert.Equal([Second], Assert.Single(entries).Sources);
    }

    [Fact]
    public void Read_AStatementCitingOneMessageTwice_CitesItOnce()
    {
        // Arrange
        const string answer =
            """{ "agreements": [{ "text": "They settled the price.", "messages": [0, 0, 0] }] }""";

        // Act
        var entries = ThreadStateReading.Read(answer, Messages(), topicShown: false).Entries;

        // Assert
        Assert.Equal([First], Assert.Single(entries).Sources);
    }

    /// <summary>A model handed a long exchange writes one statement per message; the block keeps what it leads with.</summary>
    [Fact]
    public void Read_MoreStatementsOfOneAspectThanTheBlockHolds_KeepsTheLeadingOnes()
    {
        // Arrange
        var written = string.Join(
            ',',
            Enumerable
                .Range(0, EmailThreadState.MaximumEntriesPerAspect + 4)
                .Select(static ordinal => $$"""{ "text": "Agreement {{ordinal}}.", "messages": [0] }"""));
        var answer = $$"""{ "agreements": [{{written}}] }""";

        // Act
        var entries = ThreadStateReading.Read(answer, Messages(), topicShown: false).Entries;

        // Assert
        Assert.Equal("Agreement 0.", Assert.Single(entries).Text);
    }

    /// <summary>An owner and a date belong to a commitment, so one written on another aspect is dropped rather than refused.</summary>
    [Fact]
    public void Read_AnAgreementWrittenWithAnOwnerAndADate_KeepsTheStatementWithoutThem()
    {
        // Arrange
        const string answer = """
            { "agreements": [
                { "text": "They settled the price.", "messages": [0], "owedBy": "Karolina", "dueAt": "2026-09-12" }
            ] }
            """;

        // Act
        var entry = Assert.Single(ThreadStateReading.Read(answer, Messages(), topicShown: false).Entries);

        // Assert
        Assert.Null(entry.OwedBy);
        Assert.Null(entry.DueAt);
    }

    /// <summary>A date a reader would act on is dropped where it cannot be read, and the commitment stays.</summary>
    [Theory]
    [InlineData("next Friday")]
    [InlineData("")]
    [InlineData("2026-13-45")]
    public void Read_ACommitmentWhoseDateCannotBeRead_KeepsTheCommitmentWithoutADate(string dueAt)
    {
        // Arrange
        var answer = $$"""
            { "commitments": [{ "text": "Karolina sends the figures.", "messages": [0], "dueAt": "{{dueAt}}" }] }
            """;

        // Act
        var entry = Assert.Single(ThreadStateReading.Read(answer, Messages(), topicShown: false).Entries);

        // Assert
        Assert.Equal(ThreadStateAspect.Commitment, entry.Aspect);
        Assert.Null(entry.DueAt);
    }

    /// <summary>A model told to answer with one object still fences it and writes a sentence around it.</summary>
    [Theory]
    [InlineData("""Here is the state: ```json { "agreements": [{ "text": "They settled.", "messages": [0] }] } ``` I hope this helps.""")]
    [InlineData("""```{ "agreements": [{ "text": "They settled.", "messages": [0] }] }```""")]
    public void Read_AnObjectWrittenInsideProseOrAFence_IsStillRead(string answer)
    {
        // Act
        var entry = Assert.Single(ThreadStateReading.Read(answer, Messages(), topicShown: false).Entries);

        // Assert
        Assert.Equal("They settled.", entry.Text);
    }

    /// <summary>
    /// An answer with no object to read is a fault in how one reply came back rather than a fact about the conversation,
    /// so it is withheld and the conversation stays outstanding instead of being recorded as having nothing to say.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I could not read the conversation.")]
    [InlineData("{ not json at all }")]
    [InlineData("""{ "agreements": "none" }""")]
    [InlineData("""{ "agreements": [{ "text": "They settled the price.", "messages": [0] }""")]
    public void Read_AnAnswerNothingCanBeReadOutOf_WithholdsItAsUnreadable(string? answer)
    {
        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown: false);

        // Assert
        Assert.Equal(ThreadStateWithholding.AnswerUnreadable, derivation.Withheld);
        Assert.Empty(derivation.Entries);
    }

    /// <summary>An object carrying nothing is the answer the instructions ask for when there is nothing to say.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "agreements": [], "openQuestions": [], "commitments": [], "differences": [] }""")]
    public void Read_AnObjectCarryingNoStatements_SettlesAsNothingToSay(string answer)
    {
        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown: false);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Empty(derivation.Entries);
    }

    [Theory]
    [InlineData("""{ "agreements": [{ "messages": [0] }] }""")]
    [InlineData("""{ "agreements": [{ "text": "   ", "messages": [0] }] }""")]
    [InlineData("""{ "agreements": [null] }""")]
    public void Read_AStatementSayingNothing_IsDroppedAndTheAnswerSettles(string answer)
    {
        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown: false);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Empty(derivation.Entries);
    }

    [Fact]
    public void Read_AnAnswerNamingTheConversation_ProposesItsTopicAndStanding()
    {
        // Arrange
        const string answer = """
            {
              "openQuestions": [{ "text": "The upper limit is unsettled.", "messages": [1] }],
              "title": { "topic": " Racking quotation ", "subjectChanged": true, "standing": "Upper limit still open" }
            }
            """;

        // Act
        var title = ThreadStateReading.Read(answer, Messages(), topicShown: false).Title;

        // Assert
        Assert.Equal(new ThreadTitleProposal("Racking quotation", StatesSubjectChange: true, "Upper limit still open"), title);
    }

    [Fact]
    public void Read_AnAnswerKeepingTheTopicItWasShown_ProposesNoNewTopic()
    {
        // Arrange
        const string answer = """{ "title": { "keepTopic": true, "topic": "Something else", "standing": "" } }""";

        // Act
        var title = ThreadStateReading.Read(answer, Messages(), topicShown: true).Title;

        // Assert
        Assert.Equal(new ThreadTitleProposal(Topic: null, StatesSubjectChange: false, string.Empty), title);
    }

    /// <summary>Keeping a topic the turn never showed would leave the conversation untitled, so it is no answer about the topic.</summary>
    [Fact]
    public void Read_AnAnswerKeepingATopicItWasNotShown_IsWithheldAsUnreadable()
    {
        // Arrange
        const string answer = """{ "title": { "keepTopic": true, "standing": "" } }""";

        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown: false);

        // Assert
        Assert.Equal(ThreadStateWithholding.AnswerUnreadable, derivation.Withheld);
    }

    [Fact]
    public void Read_AnAnswerKeepingATopicItWasNotShownBesideANewOne_ProposesTheNewTopic()
    {
        // Arrange
        const string answer = """{ "title": { "keepTopic": true, "topic": "Racking quotation", "standing": "" } }""";

        // Act
        var title = ThreadStateReading.Read(answer, Messages(), topicShown: false).Title;

        // Assert
        Assert.Equal(new ThreadTitleProposal("Racking quotation", StatesSubjectChange: false, string.Empty), title);
    }

    /// <summary>
    /// Two answers gpt-6-luna wrote in the evaluation run that put "keepTopic" beside "title" rather than inside it, one
    /// where the turn showed a topic and one where it showed none. The reading does not move a field into the object it
    /// belongs to, so each title carries neither a topic nor a kept one and the answer is withheld; the instruction
    /// saying where the field goes is what answers this.
    /// </summary>
    [Theory]
    [InlineData(RecordedAnswerKeepingTheTopicOutsideTheTitle, true)]
    [InlineData(RecordedPolishAnswerKeepingATopicOutsideTheTitle, false)]
    public void Read_ARecordedAnswerKeepingTheTopicOutsideTheTitle_IsWithheldAsUnreadable(string answer, bool topicShown)
    {
        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown);

        // Assert
        Assert.Equal(ThreadStateWithholding.AnswerUnreadable, derivation.Withheld);
    }

    /// <summary>A standing is a reading of what is still open, so one written beside nothing open says nothing the statements carry.</summary>
    [Fact]
    public void Read_AStandingBesideNothingOpenOrOwed_IsStoredEmpty()
    {
        // Arrange
        const string answer = """
            {
              "agreements": [{ "text": "The price holds.", "messages": [0] }],
              "title": { "topic": "Racking quotation", "standing": "Waiting on the signed order" }
            }
            """;

        // Act
        var title = ThreadStateReading.Read(answer, Messages(), topicShown: false).Title;

        // Assert
        Assert.NotNull(title);
        Assert.Empty(title.Standing);
    }

    [Theory]
    [InlineData("""{ "title": { "topic": "<b>Racking</b>", "standing": "" } }""")]
    [InlineData("""{ "title": { "topic": "Racking\nquotation", "standing": "" } }""")]
    [InlineData("""{ "title": { "topic": "See https://example.test", "standing": "" } }""")]
    [InlineData("""{ "title": { "standing": "" } }""")]
    [InlineData("""{ "openQuestions": [{ "text": "Open.", "messages": [1] }], "title": { "topic": "Racking", "standing": "**Open**" } }""")]
    public void Read_ATitleThatIsNotOnePlainLine_IsWithheldAsUnreadable(string answer)
    {
        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown: false);

        // Assert
        Assert.Equal(ThreadStateWithholding.AnswerUnreadable, derivation.Withheld);
    }

    /// <summary>A topic a few characters past its bound is still the line its row is recognized by, so it is cut rather than costing the answer.</summary>
    [Fact]
    public void Read_ATopicPastItsBound_IsCutToItAndMarkedAsCut()
    {
        // Arrange
        var answer = $$"""{ "title": { "topic": "{{new string('a', ThreadTitle.MaximumTopicLength + 1)}}", "standing": "" } }""";

        // Act
        var derivation = ThreadStateReading.Read(answer, Messages(), topicShown: false);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Equal(new string('a', ThreadTitle.MaximumTopicLength - 1) + "…", derivation.Title?.Topic);
    }

    /// <summary>A line is judged whole before it is cut, so markup or a line break past the bound is refused rather than cut away.</summary>
    [Theory]
    [InlineData("\\nmore")]
    [InlineData(" https://example.test")]
    [InlineData(" <b>")]
    public void Read_ATitleLinePastItsBoundCarryingMoreThanTextBeyondTheCut_IsWithheldAsUnreadable(string tail)
    {
        // Arrange
        var line = new string('a', ThreadTitle.MaximumStandingLength) + tail;
        var answers = new[]
        {
            $$"""{ "title": { "topic": "{{line}}", "standing": "" } }""",
            $$"""{ "openQuestions": [{ "text": "Open.", "messages": [1] }], "title": { "topic": "Racking", "standing": "{{line}}" } }""",
        };

        // Act
        var derivations = answers.Select(answer => ThreadStateReading.Read(answer, Messages(), topicShown: false)).ToArray();

        // Assert
        Assert.All(derivations, derivation => Assert.Equal(ThreadStateWithholding.AnswerUnreadable, derivation.Withheld));
    }

    /// <summary>
    /// The two answers gpt-6-luna wrote in the evaluation run that the reading withheld whole: one topic two characters
    /// past its bound, one standing eight past it, every statement beside them readable.
    /// </summary>
    [Theory]
    [InlineData(RecordedAnswerWithATopicPastItsBound)]
    [InlineData(RecordedAnswerWithAStandingPastItsBound)]
    public void Read_ARecordedAnswerWhoseTitleRanLong_SettlesWithEveryStatementAndATitleWithinBounds(string answer)
    {
        // Arrange
        IReadOnlyList<DerivableThreadMessage> messages =
        [
            .. Messages(),
            new(StoredEmailId.Create(Guid.CreateVersion7()), 2, "Frida", new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero), "noted", StubEmailProvenanceReader.KnownCorrespondent),
        ];

        // Act
        var derivation = ThreadStateReading.Read(answer, messages, topicShown: false);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Equal(3, derivation.Entries.Count);
        var title = Assert.IsType<ThreadTitleProposal>(derivation.Title);
        Assert.True(ThreadTitle.IsPlainLine(title.Topic, ThreadTitle.MaximumTopicLength));
        Assert.True(ThreadTitle.IsPlainLine(title.Standing, ThreadTitle.MaximumStandingLength));
    }

    [Fact]
    public void Read_AnAnswerCarryingNoTitle_SettlesWithoutOne()
    {
        // Act
        var derivation = ThreadStateReading.Read("""{ "agreements": [] }""", Messages(), topicShown: false);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Null(derivation.Title);
    }

    private const string RecordedAnswerKeepingTheTopicOutsideTheTitle = """
        {
          "keepTopic": true,
          "agreements": [
            {
              "text": "Invoice 7819 is fully settled: the €1,150.00 payment was received and the remaining balance is €0.00.",
              "messages": [1, 2, 3]
            }
          ],
          "openQuestions": [
            {
              "text": "The revised copy of invoice 7842 and confirmation that its payment was made are still awaited.",
              "messages": [2, 3]
            }
          ],
          "commitments": [
            {
              "text": "Northstar Ledgerworks undertook to pay the outstanding €2,480.00 on invoice 7842 by 10 September 2026.",
              "messages": [1, 3],
              "owedBy": "Northstar Ledgerworks",
              "dueAt": "2026-09-10"
            }
          ],
          "title": {
            "standing": "The invoice 7842 payment is due by 10 September 2026; the revised invoice copy and payment confirmation are awaited."
          }
        }
        """;

    private const string RecordedPolishAnswerKeepingATopicOutsideTheTitle = """
        {
          "keepTopic": true,
          "title": {
            "standing": "Oczekiwana jest wycena przed 18 września; nie wiadomo, czy jest już gotowa."
          },
          "openQuestions": [
            {
              "text": "Mara pyta, czy wycena 30 krzeseł jest już gotowa, ale nie otrzymała odpowiedzi.",
              "messages": [2]
            }
          ],
          "commitments": [
            {
              "text": "Wycena 30 krzeseł ma zostać przesłana przed 18 września, o co poprosiła Mara.",
              "messages": [2],
              "dueAt": "2026-09-17"
            }
          ]
        }
        """;

    private const string RecordedAnswerWithATopicPastItsBound = """
        {
          "agreements": [
            {
              "text": "They confirmed that INV-4821 for SEK 18,750 was received and that its billing address will be corrected to the address Frida provided.",
              "messages": [1, 2]
            }
          ],
          "openQuestions": [
            {
              "text": "Receipt of the SEK 9,860 payment for INV-4807 has not been confirmed; it had not yet appeared in the account.",
              "messages": [1, 2]
            }
          ],
          "commitments": [
            {
              "text": "Payment of SEK 12,400 for INV-4798 is scheduled for 10 September 2026; the INV-4821 billing address will be corrected, and receipt of the INV-4807 payment will be monitored and confirmed.",
              "messages": [1, 2],
              "dueAt": "2026-09-10"
            }
          ],
          "title": {
            "topic": "Invoice receipt, outstanding balance, billing correction, and payment confirmation",
            "standing": "INV-4798 payment is scheduled for 10 September 2026; the INV-4821 address correction and confirmation of receipt of INV-4807 remain pending."
          }
        }
        """;

    private const string RecordedAnswerWithAStandingPastItsBound = """
        {
          "agreements": [
            {
              "text": "They confirmed INV-4821 should show Suite 4D instead of Suite 4B, with its total unchanged at €2,480.",
              "messages": [0, 1, 2]
            }
          ],
          "openQuestions": [
            {
              "text": "Rosalía proposed a 15-minute reconciliation check-in on 11 September 2026 at 10:30 CET and asked whether the time works; acceptance is unanswered.",
              "messages": [2]
            }
          ],
          "commitments": [
            {
              "text": "The €1,150 settlement for INV-4798 is scheduled for 10 September 2026; the corrected INV-4821 copy was also promised for 14 July, with confirmation to follow once its payment is applied.",
              "messages": [1],
              "dueAt": "2026-09-10"
            }
          ],
          "title": {
            "topic": "Invoice INV-4821 received — billing correction and payment update",
            "standing": "INV-4798 settlement is due 10 September; the revised INV-4821 copy and application confirmation remain pending, as does an answer on the proposed 11 September check-in."
          }
        }
        """;

    private static IReadOnlyList<DerivableThreadMessage> Messages() =>
    [
        new(First, 0, "Karolina", new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero), "the price holds", StubEmailProvenanceReader.KnownCorrespondent),
        new(Second, 1, "Piotr", new DateTimeOffset(2026, 9, 7, 15, 0, 0, TimeSpan.Zero), "what about the limit", StubEmailProvenanceReader.KnownCorrespondent),
    ];
}
