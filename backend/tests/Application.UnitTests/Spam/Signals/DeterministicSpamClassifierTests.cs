// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Spam.Signals;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Signals;

public sealed class DeterministicSpamClassifierTests
{
    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private static readonly MailFolderAlias Junk = MailFolderAlias.Create("JUNK");

    private static readonly IReadOnlySet<SpamVerdictAct> NoActs = new HashSet<SpamVerdictAct>();

    [Fact]
    public void Read_AMessageCarryingNothing_IsUndeterminedWithNoSignals()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(SpamHeaderFacts.None, Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.Equal(SpamVerdict.Undetermined, reading.Verdict);
        Assert.Empty(reading.Signals);
        Assert.Null(reading.Assessment);
    }

    [Fact]
    public void Read_AnOccurrenceInTheJunkFolder_IsSpamAndRecordsThePlacement()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(SpamHeaderFacts.None, Junk, isJunkFolder: true, RemoteEmailKeywords.None, NoActs);

        // Assert
        var signal = Assert.Single(reading.Signals);

        Assert.Equal(SpamVerdict.Spam, reading.Verdict);
        Assert.Equal(SpamSignalKind.JunkFolderPlacement, signal.Kind);
        Assert.Equal("JUNK", signal.Name);
        Assert.Null(signal.Observation);
        Assert.Equal(SpamSignalSource.FolderPlacement, signal.Provenance.Source);
    }

    /// <summary>The placement is a decision somebody already acted on, so it outranks a header saying otherwise.</summary>
    [Fact]
    public void Read_AnOccurrenceInTheJunkFolderWhoseProviderSaidNo_IsStillSpam()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = FactsWithHeaders(("X-Spam-Flag", "NO"));

        // Act
        var reading = classifier.Read(facts, Junk, isJunkFolder: true, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.Equal(SpamVerdict.Spam, reading.Verdict);
    }

    [Theory]
    [InlineData("YES", SpamVerdict.Spam)]
    [InlineData("yes", SpamVerdict.Spam)]
    [InlineData("NO", SpamVerdict.NotSpam)]
    [InlineData("no", SpamVerdict.NotSpam)]
    public void Read_AProviderSpamFlag_ReachesTheVerdictItStates(string flag, SpamVerdict expected)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(FactsWithHeaders(("X-Spam-Flag", flag)), Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.Equal(expected, reading.Verdict);
    }

    [Theory]
    [InlineData("Yes, score=15.2 required=5.0 tests=BAYES_99", SpamVerdict.Spam)]
    [InlineData("No, score=-2.6 required=5.0 tests=BAYES_00", SpamVerdict.NotSpam)]
    [InlineData("Maybe, score=1.0 required=5.0", SpamVerdict.Undetermined)]
    public void Read_AProviderSpamStatus_ReachesTheVerdictItsFirstWordStates(string status, SpamVerdict expected)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(FactsWithHeaders(("X-Spam-Status", status)), Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.Equal(expected, reading.Verdict);
    }

    [Fact]
    public void Read_AStatusCarryingBothNumbers_RecordsThemAsOneAssessment()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = FactsWithHeaders(("X-Spam-Status", "Yes, score=15.2 required=5.0 tests=BAYES_99"));

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.NotNull(reading.Assessment);

        var assessment = reading.Assessment;

        Assert.Equal(15.2, assessment.Score);
        Assert.Equal(5.0, assessment.Threshold);
        Assert.True(assessment.ClearsThreshold);
    }

    /// <summary>A score in an unknown scale is a signal rather than a measurement, so no assessment is recorded from one.</summary>
    [Theory]
    [InlineData("X-Spam-Status", "Yes, score=15.2")]
    [InlineData("X-Spam-Score", "15.2")]
    [InlineData("X-Spam-Level", "***************")]
    public void Read_AScoreWithNoThresholdBesideIt_RecordsNoAssessment(string field, string value)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(FactsWithHeaders((field, value)), Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.Null(reading.Assessment);
        Assert.Contains(reading.Signals, signal => signal.Kind is SpamSignalKind.ProviderSpamVerdict);
    }

    /// <summary>A single word with two accepted values answers a message that disagrees with itself.</summary>
    [Fact]
    public void Read_AFlagAndAStatusThatDisagree_IsAnsweredByTheFlag()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = FactsWithHeaders(
            ("X-Spam-Flag", "NO"),
            ("X-Spam-Status", "Yes, score=15.2 required=5.0"));

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.Equal(SpamVerdict.NotSpam, reading.Verdict);
    }

    /// <summary>A failure the receiving server chose to deliver anyway is recorded, and does not become a spam verdict.</summary>
    [Fact]
    public void Read_AnAuthenticationFailure_IsRecordedWithoutDecidingTheVerdict()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = SpamHeaderFacts.Create(
            [new MessageAuthenticationResult("dmarc", "fail", "header.from=example.test", IsForwarded: false)],
            []);

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        var signal = Assert.Single(reading.Signals);

        Assert.Equal(SpamVerdict.Undetermined, reading.Verdict);
        Assert.Equal(SpamSignalKind.SenderAuthentication, signal.Kind);
        Assert.Equal("dmarc", signal.Name);
        Assert.Equal("fail header.from=example.test", signal.Observation);
        Assert.Equal("Authentication-Results", signal.Provenance.Origin);
    }

    /// <summary>An ARC outcome is a claim a relay signed, so it is kept apart from what this mailbox's server saw.</summary>
    [Fact]
    public void Read_AForwardedAuthenticationResult_IsRecordedAsItsOwnKindAndOrigin()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = SpamHeaderFacts.Create(
            [new MessageAuthenticationResult("spf", "pass", Detail: null, IsForwarded: true)],
            []);

        // Act
        var reading = classifier.Read(facts, Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs);

        // Assert
        var signal = Assert.Single(reading.Signals);

        Assert.Equal(SpamSignalKind.ForwardedSenderAuthentication, signal.Kind);
        Assert.Equal("pass", signal.Observation);
        Assert.Equal("ARC-Authentication-Results", signal.Provenance.Origin);
    }

    /// <summary>Truncating a record keeps what the verdict rests on, so the placement leads whatever else was observed.</summary>
    [Fact]
    public void Read_APlacementBesideHeaders_PutsThePlacementFirst()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();
        var facts = SpamHeaderFacts.Create(
            [new MessageAuthenticationResult("spf", "pass", Detail: null, IsForwarded: false)],
            [new ProviderSpamHeaderValue("X-Spam-Flag", "YES")]);

        // Act
        var reading = classifier.Read(facts, Junk, isJunkFolder: true, RemoteEmailKeywords.None, NoActs);

        // Assert
        Assert.Equal(
            [SpamSignalKind.JunkFolderPlacement, SpamSignalKind.SenderAuthentication, SpamSignalKind.ProviderSpamVerdict],
            reading.Signals.Select(signal => signal.Kind));
    }

    /// <summary>The person's correction outranks every other signal, the junk folder and the provider's own verdict included.</summary>
    [Theory]
    [InlineData("$NotJunk")]
    [InlineData("$notjunk")]
    [InlineData("$NOTJUNK")]
    public void Read_NotJunkInTheJunkFolderUnderASpamHeader_DecidesNotSpam(string keyword)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(
            FactsWithHeaders(("X-Spam-Flag", "YES")),
            Junk,
            isJunkFolder: true,
            RemoteEmailKeywords.Create([keyword]),
            NoActs);

        // Assert
        Assert.Equal(SpamVerdict.NotSpam, reading.Verdict);
        var mark = Assert.Single(reading.Signals, signal => signal.Kind is SpamSignalKind.JunkKeyword);
        Assert.Equal("$NotJunk", mark.Name);
        Assert.Equal(SpamSignalSource.MailboxKeyword, mark.Provenance.Source);
    }

    /// <summary>A junk keyword somebody else set is a decision already acted on, and it decides spam over a clean header.</summary>
    [Theory]
    [InlineData("$Junk")]
    [InlineData("$junk")]
    [InlineData("$Phishing")]
    [InlineData("$PHISHING")]
    public void Read_AJunkKeywordSomebodyElseSet_DecidesSpam(string keyword)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(
            FactsWithHeaders(("X-Spam-Flag", "NO")),
            Inbox,
            isJunkFolder: false,
            RemoteEmailKeywords.Create([keyword]),
            NoActs);

        // Assert
        Assert.Equal(SpamVerdict.Spam, reading.Verdict);
        Assert.Contains(reading.Signals, signal => signal.Kind is SpamSignalKind.JunkKeyword);
    }

    /// <summary>RFC 9051 has <c>$Junk</c> and <c>$NotJunk</c> together count as neither, so the rest of the message decides.</summary>
    [Theory]
    [InlineData("NO", SpamVerdict.NotSpam)]
    [InlineData("YES", SpamVerdict.Spam)]
    public void Read_JunkAndNotJunkTogether_AreReadAsIfNeitherWereThere(string flag, SpamVerdict expected)
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(
            FactsWithHeaders(("X-Spam-Flag", flag)),
            Inbox,
            isJunkFolder: false,
            RemoteEmailKeywords.Create(["$Junk", "$NotJunk", "$Phishing"]),
            NoActs);

        // Assert
        Assert.Equal(expected, reading.Verdict);
        Assert.Equal(
            ["$NotJunk", "$Junk", "$Phishing"],
            reading.Signals.Where(signal => signal.Kind is SpamSignalKind.JunkKeyword).Select(signal => signal.Name));
    }

    /// <summary>A keyword this deployment's verdict wrote is no evidence, and leaves no signal behind.</summary>
    [Fact]
    public void Read_JunkKeywordsTheVerdictWroteItself_AreNotSignals()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(
            SpamHeaderFacts.None,
            Inbox,
            isJunkFolder: false,
            RemoteEmailKeywords.Create(["$Junk", "$Phishing"]),
            new HashSet<SpamVerdictAct> { SpamVerdictAct.MarkedJunk, SpamVerdictAct.MarkedPhishing });

        // Assert
        Assert.Equal(SpamVerdict.Undetermined, reading.Verdict);
        Assert.Empty(reading.Signals);
    }

    /// <summary>
    /// A message an earlier verdict filed into the junk folder and marked <c>$Junk</c> is decided again on what remains —
    /// which is what lets a model's verdict be undone by switching the model off rather than becoming a deterministic one.
    /// </summary>
    [Fact]
    public void Read_AMessageTheVerdictFiledAndMarkedItself_IsDecidedWithoutItsOwnActs()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(
            FactsWithHeaders(("X-Spam-Flag", "NO")),
            Junk,
            isJunkFolder: true,
            RemoteEmailKeywords.Create(["$Junk"]),
            new HashSet<SpamVerdictAct> { SpamVerdictAct.FiledIntoJunk, SpamVerdictAct.MarkedJunk });

        // Assert
        Assert.Equal(SpamVerdict.NotSpam, reading.Verdict);
        Assert.DoesNotContain(
            reading.Signals,
            signal => signal.Kind is SpamSignalKind.JunkFolderPlacement or SpamSignalKind.JunkKeyword);
    }

    /// <summary>A <c>$NotJunk</c> beside the <c>$Junk</c> the verdict wrote is the person correcting it, not a pair that cancels.</summary>
    [Fact]
    public void Read_NotJunkBesideTheVerdictsOwnJunk_DecidesNotSpam()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act
        var reading = classifier.Read(
            SpamHeaderFacts.None,
            Inbox,
            isJunkFolder: false,
            RemoteEmailKeywords.Create(["$Junk", "$NotJunk"]),
            new HashSet<SpamVerdictAct> { SpamVerdictAct.MarkedJunk });

        // Assert
        Assert.Equal(SpamVerdict.NotSpam, reading.Verdict);
    }

    [Fact]
    public void Read_NoFacts_IsRefused()
    {
        // Arrange
        var classifier = new DeterministicSpamClassifier();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => classifier.Read(facts: null!, Inbox, isJunkFolder: false, RemoteEmailKeywords.None, NoActs));
    }

    private static SpamHeaderFacts FactsWithHeaders(params (string Field, string Value)[] headers) =>
        SpamHeaderFacts.Create(
            [],
            [.. headers.Select(header => new ProviderSpamHeaderValue(header.Field, header.Value))]);
}
