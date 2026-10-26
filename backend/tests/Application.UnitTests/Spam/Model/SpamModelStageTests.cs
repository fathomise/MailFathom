// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Model;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Model;

public sealed class SpamModelStageTests
{
    private const string ModelBuild = "example-provider/spam-model-2026-09";

    private const string InstructionRevision = "0123456789ab";

    private static readonly ClassifiableEmail Email = new(
        StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000f00d")),
        MailAccountId.Create("acct-1"),
        MailFolderAlias.Create("INBOX"),
        RemoteEmailKeywords.Create([]));

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);

    private static readonly DeterministicSpamReading NotSpamReading = new(SpamVerdict.NotSpam, null, []);

    private static readonly DeterministicSpamReading SpamReading = new(
        SpamVerdict.Spam,
        null,
        [SpamSignal.Create(SpamSignalKind.ProviderSpamVerdict, "X-Spam-Flag", "YES", SpamSignalProvenance.FromMessageHeader("X-Spam-Flag"))]);

    private readonly RecordingSpamModelStageTelemetry telemetry = new();

    [Fact]
    public async Task DecideAsync_ModelStageNotConsulted_AsksNothingAndRecordsTheDeterministicReading()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(0.99, SpamSubcategory.Scam));
        var settings = Settings(SpamModelStageSettings.NotConsulted);

        // Act
        var classification = await this.DecideAsync(judge, NotSpamReading, settings);

        // Assert
        Assert.Empty(judge.Asked);
        Assert.Empty(this.telemetry.Consultations);
        Assert.Equal(SpamVerdict.NotSpam, classification.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, classification.DecidedBy);
        Assert.Equal(settings.Profile, classification.Profile);
    }

    [Fact]
    public async Task DecideAsync_NotSpamReadingAndAProbabilityAtTheThreshold_RecordsTheModelsSpamVerdictAndKind()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(0.7, SpamSubcategory.Scam));
        var settings = Settings(SpamModelStageSettings.Consulted(false, 0.7));

        // Act
        var classification = await this.DecideAsync(judge, NotSpamReading, settings);

        // Assert
        Assert.Equal(SpamModelJob.DecideVerdict, Assert.Single(judge.Asked).Job);
        Assert.Equal(SpamVerdict.Spam, classification.Verdict);
        Assert.Equal(SpamClassificationStage.Model, classification.DecidedBy);
        Assert.Equal(SpamSubcategory.Scam, classification.Subcategory);
        Assert.Equal(0.7, classification.Assessment?.Score);
        Assert.Equal(settings.Profile, classification.Profile);
        Assert.Equal([(SpamModelJob.DecideVerdict, SpamModelOutcome.Answered)], this.telemetry.Consultations);
    }

    [Fact]
    public async Task DecideAsync_ProbabilityBelowTheThreshold_RecordsTheModelsNotSpamVerdictWithNoSubcategory()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(0.69, SpamSubcategory.Phishing, SpamPhishingRequest.Credentials, SpamImpersonatedParty.Bank));

        // Act
        var classification = await this.DecideAsync(judge, NotSpamReading, Settings(SpamModelStageSettings.Consulted(false, 0.7)));

        // Assert
        Assert.Equal(SpamVerdict.NotSpam, classification.Verdict);
        Assert.Equal(SpamClassificationStage.Model, classification.DecidedBy);
        Assert.Null(classification.Subcategory);
        Assert.Null(classification.PhishingRequest);
        Assert.Null(classification.ImpersonatedParty);
    }

    [Fact]
    public async Task DecideAsync_DeterministicSpamWithNamingSwitchedOff_AsksNothing()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(null, SpamSubcategory.Marketing));

        // Act
        var classification = await this.DecideAsync(judge, SpamReading, Settings(SpamModelStageSettings.Consulted(false, 0.5)));

        // Assert
        Assert.Empty(judge.Asked);
        Assert.Equal(SpamVerdict.Spam, classification.Verdict);
        Assert.Null(classification.Subcategory);
    }

    [Fact]
    public async Task DecideAsync_DeterministicSpamWithNamingSwitchedOn_KeepsTheDeterministicVerdictBesideTheModelsKind()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(null, SpamSubcategory.Marketing));

        // Act
        var classification = await this.DecideAsync(judge, SpamReading, Settings(SpamModelStageSettings.Consulted(true, 0.5)));

        // Assert
        Assert.Equal(SpamModelJob.NameSubcategory, Assert.Single(judge.Asked).Job);
        Assert.Equal(SpamVerdict.Spam, classification.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, classification.DecidedBy);
        Assert.Null(classification.Assessment);
        Assert.Equal(SpamSubcategory.Marketing, classification.Subcategory);
        Assert.Contains(classification.Signals, static signal => signal.Kind is SpamSignalKind.ProviderSpamVerdict);
    }

    [Fact]
    public async Task DecideAsync_ThePersonsNotJunkCorrection_NeverAsksTheModel()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(0.99, SpamSubcategory.Scam));
        var corrected = new DeterministicSpamReading(
            SpamVerdict.NotSpam,
            null,
            [SpamSignal.Create(SpamSignalKind.JunkKeyword, JunkKeywords.NotJunk, null, SpamSignalProvenance.FromMailboxKeyword(JunkKeywords.NotJunk))]);

        // Act
        var classification = await this.DecideAsync(judge, corrected, Settings(SpamModelStageSettings.Consulted(true, 0.5)));

        // Assert
        Assert.Empty(judge.Asked);
        Assert.Equal(SpamVerdict.NotSpam, classification.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, classification.DecidedBy);
    }

    [Theory]
    [InlineData(SpamModelOutcome.BudgetExhausted)]
    [InlineData(SpamModelOutcome.ProviderUnavailable)]
    [InlineData(SpamModelOutcome.AnswerUnreadable)]
    [InlineData(SpamModelOutcome.InputTooLarge)]
    [InlineData(SpamModelOutcome.ScannerUnavailable)]
    public async Task DecideAsync_ModelLeavesNoAnswer_RecordsTheDeterministicReadingWholeAndCountsWhy(SpamModelOutcome outcome)
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(SpamModelJudgement.Unanswered(outcome));

        // Act
        var classification = await this.DecideAsync(judge, NotSpamReading, Settings(SpamModelStageSettings.Consulted(false, 0.5)));

        // Assert
        Assert.Equal(SpamVerdict.NotSpam, classification.Verdict);
        Assert.Equal(SpamClassificationStage.Deterministic, classification.DecidedBy);
        Assert.Null(classification.Subcategory);
        Assert.DoesNotContain(classification.Signals, static signal => signal.Kind is SpamSignalKind.ModelJudgement);
        Assert.Equal(SpamClassificationProfile.Create(), classification.Profile);
        Assert.Equal([(SpamModelJob.DecideVerdict, outcome)], this.telemetry.Consultations);
    }

    [Fact]
    public async Task DecideAsync_NoModelServesSpamClassification_CountsItAndAsksNothing()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(0.99, SpamSubcategory.Scam), isActive: false);

        // Act
        var classification = await this.DecideAsync(judge, NotSpamReading, Settings(SpamModelStageSettings.Consulted(false, 0.5)));

        // Assert
        Assert.Empty(judge.Asked);
        Assert.Equal(SpamClassificationProfile.Create(), classification.Profile);
        Assert.Equal([(SpamModelJob.DecideVerdict, SpamModelOutcome.NotActivated)], this.telemetry.Consultations);
    }

    [Fact]
    public async Task DecideAsync_DetailsTheKindDoesNotAllow_DropsThemAndKeepsTheOnesItDoes()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(
            0.9,
            SpamSubcategory.BusinessEmailCompromise,
            SpamPhishingRequest.PaymentDetails,
            SpamImpersonatedParty.Executive));

        // Act
        var classification = await this.DecideAsync(judge, NotSpamReading, Settings(SpamModelStageSettings.Consulted(false, 0.5)));

        // Assert
        Assert.Equal(SpamSubcategory.BusinessEmailCompromise, classification.Subcategory);
        Assert.Null(classification.PhishingRequest);
        Assert.Equal(SpamImpersonatedParty.Executive, classification.ImpersonatedParty);
    }

    [Fact]
    public async Task DecideAsync_PhishingAnswer_RecordsWhatItAsksForAndWhoItImpersonatesWithTheModelsSignals()
    {
        // Arrange
        var judge = new ScriptedSpamModelJudge(Answered(
            0.95,
            SpamSubcategory.Phishing,
            SpamPhishingRequest.OneTimeCode,
            SpamImpersonatedParty.Courier));

        // Act
        var classification = await this.DecideAsync(judge, NotSpamReading, Settings(SpamModelStageSettings.Consulted(false, 0.5)));

        // Assert
        Assert.Equal(SpamPhishingRequest.OneTimeCode, classification.PhishingRequest);
        Assert.Equal(SpamImpersonatedParty.Courier, classification.ImpersonatedParty);
        var modelSignal = Assert.Single(classification.Signals, static signal => signal.Kind is SpamSignalKind.ModelJudgement);
        Assert.Equal(SpamSignalProvenance.FromModel(ModelBuild, InstructionRevision), modelSignal.Provenance);
    }

    [Fact]
    public async Task DecideAsync_ReadingAtTheSignalBound_KeepsTheModelsJudgementAheadOfTheReadingsSignals()
    {
        // Arrange
        var full = new DeterministicSpamReading(
            SpamVerdict.Spam,
            null,
            [
                .. Enumerable.Range(0, SpamClassification.MaximumSignals).Select(index => SpamSignal.Create(
                    SpamSignalKind.ProviderSpamVerdict,
                    $"X-Synthetic-{index}",
                    "YES",
                    SpamSignalProvenance.FromMessageHeader($"X-Synthetic-{index}"))),
            ]);
        var judge = new ScriptedSpamModelJudge(Answered(null, SpamSubcategory.Marketing));

        // Act
        var classification = await this.DecideAsync(judge, full, Settings(SpamModelStageSettings.Consulted(true, 0.5)));

        // Assert
        Assert.Equal(SpamClassification.MaximumSignals, classification.Signals.Count);
        Assert.Contains(classification.Signals, static signal => signal.Kind is SpamSignalKind.ModelJudgement);
    }

    private static SpamClassificationSettings Settings(SpamModelStageSettings modelStage) =>
        SpamClassificationSettings.Create(true, [Email.FolderAlias], modelStage);

    private static SpamModelJudgement Answered(
        double? probability,
        SpamSubcategory subcategory,
        SpamPhishingRequest? phishingRequest = null,
        SpamImpersonatedParty? impersonatedParty = null) =>
        SpamModelJudgement.Answered(new SpamModelAnswer(
            probability,
            subcategory,
            phishingRequest,
            impersonatedParty,
            ModelBuild,
            InstructionRevision,
            [new SpamModelChoice("spam_kind", "scam")]));

    [Fact]
    public async Task DecideAsync_AccountWhosePostureScans_IsJudgedUnderThatAccountsPostureAndNoOther()
    {
        // Arrange
        const string marker = "AKIAEXAMPLEKEY";

        var scannedAccount = MailAccountId.Create("acct-scanned");
        var scanner = new MarkerSensitiveContentScanner(marker, SensitiveContentScannerKind.Secrets, TimeProvider.System);
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [SensitiveContentScannerPlan.Create(scanner.Scanner, [MarkerSensitiveContentScanner.Category], [])]);
        using var permits = new SensitiveContentScanConcurrency(plan.Bounds.MaximumConcurrentScans);
        var egressGuard = new SensitiveContentEgressGuard(
            FixedSensitiveContentPostures.Of(
                SensitiveContentPosture.ScanningNothing,
                (scannedAccount, SensitiveContentPosture.Scanning(
                    [scanner.Scanner],
                    new SensitiveContentRedactor(plan, [scanner], TimeProvider.System, permits),
                    SensitiveContentScreeningPolicy.ScreeningNothing(),
                    SensitiveContentDerivationStamp.Compute(plan, [scanner])))),
            new RecordingSensitiveContentEgressTelemetry(),
            TimeProvider.System);
        var judge = new GuardingSpamModelJudge(egressGuard);
        var stage = SpamModelStages.Over(
            judge,
            this.telemetry,
            renderer: new BoundedBodyEmailContentRenderer($"The deploy key is {marker}."),
            egressGuard: egressGuard);
        var settings = Settings(SpamModelStageSettings.Consulted(false, 0.5));

        // Act
        await stage.DecideAsync(Email with { AccountId = scannedAccount }, SomeContent(), NotSpamReading, settings, EvaluatedAt, TestContext.Current.CancellationToken);
        await stage.DecideAsync(Email, SomeContent(), NotSpamReading, settings, EvaluatedAt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, judge.GuardedBodies.Count);
        Assert.DoesNotContain(marker, judge.GuardedBodies[0], StringComparison.Ordinal);
        Assert.Contains(marker, judge.GuardedBodies[1], StringComparison.Ordinal);
    }

    private static StoredEmailContent SomeContent()
    {
        var rawMime = Encoding.ASCII.GetBytes("Subject: synthetic\r\n\r\nA synthetic body.\r\n");

        return new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime));
    }

    private Task<SpamClassification> DecideAsync(
        ScriptedSpamModelJudge judge,
        DeterministicSpamReading reading,
        SpamClassificationSettings settings) =>
        SpamModelStages.Over(judge, this.telemetry).DecideAsync(
            Email,
            SomeContent(),
            reading,
            settings,
            EvaluatedAt,
            TestContext.Current.CancellationToken);

    /// <summary>Guards the body it is asked about the way the real judge does, under whatever scope the stage entered, and keeps what came back.</summary>
    private sealed class GuardingSpamModelJudge(SensitiveContentEgressGuard egressGuard) : ISpamModelJudge
    {
        public List<string> GuardedBodies { get; } = [];

        public bool IsActive => true;

        public async Task<SpamModelJudgement> JudgeAsync(
            SpamModelEvidence evidence,
            SpamModelJob job,
            CancellationToken cancellationToken)
        {
            this.GuardedBodies.Add(await egressGuard.GuardAsync(
                SensitiveContentEgressPoint.SpamClassificationInput,
                evidence.Body,
                cancellationToken));

            return SpamModelJudgement.Unanswered(SpamModelOutcome.ProviderUnavailable);
        }
    }
}
