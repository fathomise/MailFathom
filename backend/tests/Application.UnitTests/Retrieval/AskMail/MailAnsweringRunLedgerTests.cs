// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers what one run is allowed to send, call, and consume, and which of those refusals cuts rather than stops.</summary>
/// <remarks>
/// The ledger is decidable from its own bounds, so every test here runs it directly: no provider, no agent, and no
/// framework type is involved in deciding what a question may cost.
/// </remarks>
public sealed class MailAnsweringRunLedgerTests
{
    [Fact]
    public void AdmitPassages_ALookupInsideTheRunsCeiling_HandsOverEveryPassageAndCutsNothing()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 1_000);
        var found = PassagesOf(100, 100, 100);

        // Act
        var admitted = ledger.AdmitPassages(found);

        // Assert
        Assert.Equal(3, admitted.Count);
        Assert.False(ledger.RetrievalWasTruncated);
    }

    /// <summary>Whole passages rather than a cut across the last one: an extract ending mid-word buys a few hundred characters and costs the model a readable message.</summary>
    [Fact]
    public void AdmitPassages_ALookupPastTheRunsCeiling_HandsOverTheWholePassagesThatFitAndSaysItCut()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        var found = PassagesOf(100, 100, 100);

        // Act
        var admitted = ledger.AdmitPassages(found);

        // Assert
        Assert.Equal(2, admitted.Count);
        Assert.True(ledger.RetrievalWasTruncated);
    }

    /// <summary>Retrieval order is relevance order, so a run approaching its ceiling must not start preferring short messages to relevant ones.</summary>
    [Fact]
    public void AdmitPassages_ASmallerPassageBehindOneThatDoesNotFit_IsNotReachedForInstead()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 150);
        var found = PassagesOf(100, 200, 10);

        // Act
        var admitted = ledger.AdmitPassages(found);

        // Assert
        Assert.Single(admitted);
        Assert.Equal(100, admitted[0].Text.Length);
    }

    /// <summary>The ceiling holds across a run rather than across a lookup, which is the whole reason it exists beside the per-lookup bounds.</summary>
    [Fact]
    public void AdmitPassages_ASecondLookupAfterTheCeilingWasReached_HandsOverNothing()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        ledger.AdmitPassages(PassagesOf(100, 100, 100));

        // Act
        var admitted = ledger.AdmitPassages(PassagesOf(100));

        // Assert
        Assert.Empty(admitted);
        Assert.True(ledger.RetrievalWasTruncated);
    }

    /// <summary>The sender's name and address reach the provider beside the extract, so they spend the run's allowance as the extract does.</summary>
    [Theory]
    [InlineData("Ewa Nowak of the network team", null)]
    [InlineData(null, "ewa.nowak@network.example.org")]
    public void AdmitPassages_APassageWhoseSenderTakesItPastTheCeiling_IsNotHandedOver(string? displayName, string? address)
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 120);
        var passage = ScriptedEmailKnowledgeSearch.Passage(new string('a', 100)) with
        {
            SenderDisplayName = displayName,
            SenderAddress = address,
        };

        // Act
        var admitted = ledger.AdmitPassages([passage]);

        // Assert
        Assert.Empty(admitted);
        Assert.True(ledger.RetrievalWasTruncated);
    }

    /// <summary>A listing hands a provider the subject and the sender somebody wrote, so a page spends the allowance a lookup does, and is cut where it runs out.</summary>
    [Fact]
    public void AdmitSummaries_APagePastTheRunsCeiling_HandsOverTheLeadingSummariesThatFitAndSaysItCut()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        var page = SummariesOf(100, 100, 100);

        // Act
        var admitted = ledger.AdmitSummaries(page);

        // Assert
        Assert.Equal(
            (2, true, 200, 2),
            (admitted.Count, ledger.RetrievalWasTruncated, ledger.Read().RetrievedCharacters, ledger.Read().MessagesRetrieved));
    }

    /// <summary>One allowance covers searching and listing together, so a run cannot page through the mailbox beside the ceiling on searching it.</summary>
    [Fact]
    public void AdmitSummaries_AfterALookupSpentTheCeiling_HandsOverNothing()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        ledger.AdmitPassages(PassagesOf(100, 100));

        // Act
        var admitted = ledger.AdmitSummaries(SummariesOf(100));

        // Assert
        Assert.Empty(admitted);
    }

    /// <summary>A summary spends only what its author wrote: a subject and a sender, never the dates, flags, and identity this deployment recorded.</summary>
    [Fact]
    public void AdmitSummaries_ASummary_SpendsItsSubjectAndItsSender()
    {
        // Arrange
        var ledger = LedgerAllowing();
        var summary = SyntheticEmailSummaries.Create(subject: "Quote", senderAddress: "ada@example.org") with { SenderDisplayName = "Ada" };

        // Act
        ledger.AdmitSummaries([summary]);

        // Assert
        Assert.Equal("Quote".Length + "Ada".Length + "ada@example.org".Length, ledger.Read().RetrievedCharacters);
    }

    /// <summary>A census hands a provider the senders and domains somebody wrote, so its groups spend the shared allowance by their keys and are cut where it runs out.</summary>
    [Fact]
    public void AdmitCensusGroups_GroupsPastTheRunsCeiling_HandsOverTheLeadingGroupsThatFitAndCountsNoMessage()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 20);
        MailCensusGroup[] groups = [new("harbourline.test", 20), new("quietfjord.test", 15), new(null, 2)];

        // Act
        var admitted = ledger.AdmitCensusGroups(groups);

        // Assert
        Assert.Equal(
            (1, true, "harbourline.test".Length, 0),
            (admitted.Count, ledger.RetrievalWasTruncated, ledger.Read().RetrievedCharacters, ledger.Read().MessagesRetrieved));
    }

    /// <summary>A group keyed by nothing — mail with no sender address, or undated — carries no text somebody wrote, so it spends nothing.</summary>
    [Fact]
    public void AdmitCensusGroups_AGroupKeyedByNothing_SpendsNothing()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 1);

        // Act
        var admitted = ledger.AdmitCensusGroups([new MailCensusGroup(null, 4)]);

        // Assert
        Assert.Equal((1, 0), (admitted.Count, ledger.Read().RetrievedCharacters));
    }

    [Fact]
    public void AdmitPassages_WithoutALookupResult_IsRefused()
    {
        // Arrange
        var ledger = LedgerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ledger.AdmitPassages(null!));
    }

    [Fact]
    public void RequireAllowanceForNextCall_ARunInsideItsCallCeiling_TakesOneAllowancePerCall()
    {
        // Arrange
        var ledger = LedgerAllowing(providerCalls: 3);

        // Act, Assert
        ledger.RequireAllowanceForNextCall();
        ledger.RequireAllowanceForNextCall();
        ledger.RequireAllowanceForNextCall();

        Assert.Throws<MailAnsweringBudgetExhaustedException>(ledger.RequireAllowanceForNextCall);
    }

    /// <summary>The call ceiling is what holds when a provider reports no usage, which is why it exists beside the token one.</summary>
    [Fact]
    public void RequireAllowanceForNextCall_ARunPastItsCallCeiling_StopsTheRunRatherThanCuttingIt()
    {
        // Arrange
        var ledger = LedgerAllowing(providerCalls: 1);
        ledger.RequireAllowanceForNextCall();

        // Act
        var failure = Assert.Throws<MailAnsweringBudgetExhaustedException>(ledger.RequireAllowanceForNextCall);

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, failure.Scope);
    }

    /// <summary>What a call will cost is unknowable until it is answered, so the call that crosses the ceiling is paid for and the next one is refused.</summary>
    [Fact]
    public void RequireAllowanceForNextCall_ARunThatHasConsumedItsTokens_IsRefusedOnTheNextCall()
    {
        // Arrange
        var ledger = LedgerAllowing(tokens: 100);
        ledger.RequireAllowanceForNextCall();
        ledger.RecordSpend(new ChatTokenUsage(90, 30));

        // Act, Assert
        Assert.Throws<MailAnsweringBudgetExhaustedException>(ledger.RequireAllowanceForNextCall);
    }

    [Fact]
    public void RequireAllowanceForNextCall_ARunUnderItsTokenCeiling_IsStillAllowed()
    {
        // Arrange
        var ledger = LedgerAllowing(tokens: 100);
        ledger.RequireAllowanceForNextCall();
        ledger.RecordSpend(new ChatTokenUsage(40, 30));

        // Act, Assert
        ledger.RequireAllowanceForNextCall();
    }

    [Fact]
    public void RecordSpend_WithoutUsage_IsRefused()
    {
        // Arrange
        var ledger = LedgerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ledger.RecordSpend(null!));
    }

    /// <summary>A file's extract reaches the provider in the same envelope the body's does, so it spends the same allowance, and so does the name its sender gave the file.</summary>
    [Fact]
    public void AdmitPassages_APassageWhoseAttachmentExtractsTakeItPastTheCeiling_IsNotHandedOver()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 150);
        var passage = ScriptedEmailKnowledgeSearch.Passage(new string('a', 100)) with
        {
            AttachmentExtracts =
            [
                new EmailKnowledgeAttachmentExtract(0, "contract.pdf", AttachmentTextKind.Document, Segment: null, new string('c', 40)),
            ],
        };

        // Act
        var admitted = ledger.AdmitPassages([passage]);

        // Assert
        Assert.Empty(admitted);
        Assert.True(ledger.RetrievalWasTruncated);
    }

    [Fact]
    public void AdmitPassages_APassageCarryingAttachmentExtracts_SpendsThemBesideItsOwnExtract()
    {
        // Arrange
        var ledger = LedgerAllowing();
        var passage = ScriptedEmailKnowledgeSearch.Passage(new string('a', 100)) with
        {
            AttachmentExtracts =
            [
                new EmailKnowledgeAttachmentExtract(0, "a.pdf", AttachmentTextKind.Document, Segment: null, new string('c', 40)),
                new EmailKnowledgeAttachmentExtract(1, FileName: null, AttachmentTextKind.ImageDescription, Segment: null, new string('d', 10)),
            ],
        };

        // Act
        ledger.AdmitPassages([passage]);

        // Assert
        Assert.Equal(100 + 5 + 40 + 10, ledger.Read().RetrievedCharacters);
    }

    [Fact]
    public void AdmitPage_APageInsideTheRunsCeiling_GrantsAllOfItAndCountsItsMessage()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 1_000);

        // Act
        var granted = ledger.AdmitPage(StoredEmailId.Create(Guid.CreateVersion7()), 400);

        // Assert
        Assert.Equal(400, granted);
        Assert.False(ledger.RetrievalWasTruncated);
        Assert.Equal((400, 1), (ledger.Read().RetrievedCharacters, ledger.Read().MessagesRetrieved));
    }

    /// <summary>A page is the continuation the model asked for by name, so the part the allowance covers is handed over rather than nothing.</summary>
    [Fact]
    public void AdmitPage_APagePastTheRunsCeiling_GrantsWhatIsLeftAndSaysItCut()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        ledger.AdmitPassages(PassagesOf(100));

        // Act
        var granted = ledger.AdmitPage(StoredEmailId.Create(Guid.CreateVersion7()), 400);

        // Assert
        Assert.Equal(150, granted);
        Assert.True(ledger.RetrievalWasTruncated);
    }

    /// <summary>A file's words spend the same allowance mail does, and count no message, because a file is not mail.</summary>
    [Fact]
    public void AdmitFileText_WordsPastWhatMailLeft_GrantsTheRestAndCountsNoMessage()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        ledger.AdmitPassages(PassagesOf(100));
        var messagesBefore = ledger.Read().MessagesRetrieved;

        // Act
        var granted = ledger.AdmitFileText(400);

        // Assert
        Assert.Equal(150, granted);
        Assert.True(ledger.RetrievalWasTruncated);
        Assert.Equal((250, messagesBefore), (ledger.Read().RetrievedCharacters, ledger.Read().MessagesRetrieved));
    }

    [Fact]
    public void AdmitPage_AfterTheCeilingWasSpent_GrantsNothingAndCountsNoMessage()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 100);
        ledger.AdmitPage(StoredEmailId.Create(Guid.CreateVersion7()), 100);

        // Act
        var granted = ledger.AdmitPage(StoredEmailId.Create(Guid.CreateVersion7()), 50);

        // Assert
        Assert.Equal(0, granted);
        Assert.Equal(1, ledger.Read().MessagesRetrieved);
    }

    [Fact]
    public void AdmitPage_ANegativeLength_IsRefused()
    {
        // Arrange
        var ledger = LedgerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.AdmitPage(StoredEmailId.Create(Guid.CreateVersion7()), -1));
    }

    /// <summary>A run has room for at most as many calls as its call ceiling leaves, and a run with none left has room for none.</summary>
    [Theory]
    [InlineData(2, 1, false)]
    [InlineData(3, 1, true)]
    [InlineData(2, 2, true)]
    [InlineData(1, 2, false)]
    [InlineData(4, 1, false)]
    public void HasRoomForAtMost_CallsMadeAgainstACeilingOfFour_IsTrueOnlyWithThatManyOrFewerLeft(int callsMade, int calls, bool expected)
    {
        // Arrange
        var ledger = LedgerAllowing(providerCalls: 4);

        foreach (var _ in Enumerable.Range(0, callsMade))
        {
            ledger.RequireAllowanceForNextCall();
        }

        // Act
        var hasRoom = ledger.HasRoomForAtMost(calls);

        // Assert
        Assert.Equal(expected, hasRoom);
    }

    /// <summary>A run whose costliest call, made that many times more, would reach its token ceiling has room for no more calls than that, however many its call ceiling leaves.</summary>
    [Theory]
    [InlineData(400, 1, true)]
    [InlineData(300, 1, false)]
    [InlineData(300, 2, true)]
    public void HasRoomForAtMost_TheCostliestCallMadeAgain_ForecastsAgainstTheTokenCeiling(int inputTokens, int calls, bool expected)
    {
        // Arrange
        var ledger = LedgerAllowing(providerCalls: 8, tokens: 1_000);
        ledger.RequireAllowanceForNextCall();
        ledger.RecordSpend(new ChatTokenUsage(inputTokens, 100));

        // Act
        var hasRoom = ledger.HasRoomForAtMost(calls);

        // Assert
        Assert.Equal(expected, hasRoom);
    }

    /// <summary>A run already past its token ceiling has no last call to make: the next one is refused.</summary>
    [Fact]
    public void HasRoomForAtMost_ARunPastItsTokenCeiling_IsFalse()
    {
        // Arrange
        var ledger = LedgerAllowing(providerCalls: 8, tokens: 1_000);
        ledger.RequireAllowanceForNextCall();
        ledger.RecordSpend(new ChatTokenUsage(900, 200));

        // Act
        var hasRoom = ledger.HasRoomForAtMost(1);

        // Assert
        Assert.False(hasRoom);
    }

    /// <summary>Asking about fewer than one further call is refused, because no run can be on fewer than its next call.</summary>
    [Fact]
    public void HasRoomForAtMost_NoCalls_IsRefused()
    {
        // Arrange
        var ledger = LedgerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.HasRoomForAtMost(0));
    }

    /// <summary>Raising the ceilings keeps what was spent counted, lets the run read and call past where it stopped, and forgets that a lookup was cut.</summary>
    [Fact]
    public void Widen_TheFirstTime_WidensWhatTheRestOfTheRunMayAddAndForgetsTheCut()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 150, providerCalls: 1);
        ledger.RequireAllowanceForNextCall();
        ledger.AdmitPassages(PassagesOf(100, 100));

        // Act
        var raised = ledger.Widen(MailAnsweringRunBounds.Create(1_000, 4, 160_000));
        var admitted = ledger.AdmitPassages(PassagesOf(100));
        ledger.RequireAllowanceForNextCall();

        // Assert
        Assert.True(raised);
        Assert.Single(admitted);
        Assert.False(ledger.RetrievalWasTruncated);
        Assert.Equal((2, 200), (ledger.Read().ProviderCalls, ledger.Read().RetrievedCharacters));
    }

    /// <summary>A run raises its ceilings once: a second raise leaves them where the first put them.</summary>
    [Fact]
    public void Widen_ASecondTime_LeavesTheCeilingsAsTheFirstRaiseSetThem()
    {
        // Arrange
        var ledger = LedgerAllowing();
        var first = MailAnsweringRunBounds.Create(40_000, 16, 160_000);
        ledger.Widen(first);

        // Act
        var raisedAgain = ledger.Widen(MailAnsweringRunBounds.Create(80_000, 32, 320_000));

        // Assert
        Assert.False(raisedAgain);
        Assert.Equal(first, ledger.Bounds);
    }

    /// <summary>Bounds lowering any one ceiling while raising the other two are refused, because a run's ceilings only widen.</summary>
    [Theory]
    [InlineData(10_000, 16, 160_000)]
    [InlineData(40_000, 4, 160_000)]
    [InlineData(40_000, 16, 40_000)]
    public void Widen_BoundsLoweringOneCeiling_IsRefused(int retrievedCharacters, int providerCalls, long tokens)
    {
        // Arrange
        var ledger = LedgerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentException>(
            () => ledger.Widen(MailAnsweringRunBounds.Create(retrievedCharacters, providerCalls, tokens)));
    }

    [Fact]
    public void Constructor_WithoutBounds_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringRunLedger(null!));
    }

    private static MailAnsweringRunLedger LedgerAllowing(
        int retrievedCharacters = 20_000,
        int providerCalls = 8,
        long tokens = 80_000) =>
        new(MailAnsweringRunBounds.Create(retrievedCharacters, providerCalls, tokens));

    private static IReadOnlyList<EmailSummary> SummariesOf(params int[] subjectLengths) =>
        [.. subjectLengths.Select(length => SyntheticEmailSummaries.Create(subject: new string('s', length)))];

    private static IReadOnlyList<EmailKnowledgePassage> PassagesOf(params int[] lengths) =>
        [.. lengths.Select(length => ScriptedEmailKnowledgeSearch.Passage(new string('a', length)))];
}
