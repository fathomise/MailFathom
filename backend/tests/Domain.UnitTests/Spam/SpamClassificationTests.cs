// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

public sealed class SpamClassificationTests
{
    private static readonly StoredEmailId Occurrence = StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001"));

    private static readonly DateTimeOffset EvaluatedAt = new(2026, 8, 12, 9, 30, 0, TimeSpan.Zero);

    private static readonly SpamClassificationProfile Profile =
        SpamClassificationProfile.Create();

    [Fact]
    public void Create_MoreSignalsThanTheBoundAdmits_KeepsTheOnesTheStagesProducedFirst()
    {
        // Arrange
        var written = Enumerable
            .Range(0, SpamClassification.MaximumSignals + 20)
            .Select(index => Signal($"field-{index}"))
            .ToArray();

        // Act
        var classification = Classification(signals: written);

        // Assert
        Assert.Equal(
            written.Take(SpamClassification.MaximumSignals).Select(signal => signal.Name),
            classification.Signals.Select(signal => signal.Name));
    }

    [Fact]
    public void Create_AnEvaluationTimeAtAnotherOffset_IsHeldAsTheInstantItNames()
    {
        // Arrange
        var written = new DateTimeOffset(2026, 8, 12, 11, 30, 0, TimeSpan.FromHours(2));

        // Act
        var classification = Classification(evaluatedAt: written);

        // Assert
        Assert.Equal(TimeSpan.Zero, classification.EvaluatedAt.Offset);
        Assert.Equal(written.UtcDateTime, classification.EvaluatedAt.UtcDateTime);
    }

    [Fact]
    public void Create_AVerdictOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Classification(verdict: (SpamVerdict)42));
    }

    [Fact]
    public void Create_AStageOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Classification(decidedBy: (SpamClassificationStage)42));
    }

    [Fact]
    public void Create_NoSignalList_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => SpamClassification.Create(
            Occurrence,
            SpamVerdict.Undetermined,
            SpamClassificationStage.Deterministic,
            assessment: null,
            Profile,
            signals: null!,
            EvaluatedAt));
    }

    /// <summary>A record written before the profile joined it carries none, and is read back rather than refused.</summary>
    [Fact]
    public void Create_NoProfile_RecordsTermsNothingCanCompare()
    {
        // Arrange, Act
        var classification = Classification(profile: default(SpamClassificationProfile));

        // Assert
        Assert.False(classification.Profile.IsSpecified);
    }

    [Fact]
    public void Create_AProfile_RecordsTheTermsTheVerdictWasReachedUnder()
    {
        // Arrange, Act
        var classification = Classification();

        // Assert
        Assert.Equal(Profile, classification.Profile);
    }

    /// <summary>A spam verdict the deterministic stage reached alone is whole without a subcategory.</summary>
    [Fact]
    public void Create_DeterministicSpamWithoutASubcategory_IsWhole()
    {
        // Arrange, Act
        var classification = Classification();

        // Assert
        Assert.Null(classification.Subcategory);
        Assert.Null(classification.SubcategoryNamedBy);
    }

    [Theory]
    [InlineData(SpamVerdict.NotSpam, SpamClassificationStage.Model)]
    [InlineData(SpamVerdict.Undetermined, SpamClassificationStage.Model)]
    [InlineData(SpamVerdict.NotSpam, SpamClassificationStage.Person)]
    public void Create_ASubcategoryOnAVerdictOtherThanSpam_IsRefused(SpamVerdict verdict, SpamClassificationStage decidedBy)
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            verdict,
            decidedBy,
            signals: [ModelJudgement()],
            subcategory: SpamSubcategory.Marketing));

        Assert.Equal("subcategory", failure.ParamName);
    }

    /// <summary>The deterministic stage names no subcategory, so one with no model judgement behind it has nobody who named it.</summary>
    [Fact]
    public void Create_ASubcategoryNoStageCouldHaveNamed_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            signals: [Signal("X-Spam-Flag")],
            subcategory: SpamSubcategory.Scam));

        Assert.Equal("subcategory", failure.ParamName);
    }

    /// <summary>The model only naming the subcategory of a deterministic verdict leaves the verdict the deterministic stage's.</summary>
    [Fact]
    public void Create_TheModelNamingTheSubcategoryOfDeterministicSpam_KeepsTheVerdictDeterministic()
    {
        // Arrange, Act
        var classification = Classification(
            signals: [Signal("X-Spam-Flag"), ModelJudgement()],
            subcategory: SpamSubcategory.Phishing,
            phishingRequest: SpamPhishingRequest.Credentials,
            impersonatedParty: SpamImpersonatedParty.Bank);

        // Assert
        Assert.Equal(SpamClassificationStage.Deterministic, classification.DecidedBy);
        Assert.Equal(SpamClassificationStage.Model, classification.SubcategoryNamedBy);
        Assert.Equal(SpamSubcategory.Phishing, classification.Subcategory);
        Assert.Equal(SpamPhishingRequest.Credentials, classification.PhishingRequest);
        Assert.Equal(SpamImpersonatedParty.Bank, classification.ImpersonatedParty);
    }

    [Fact]
    public void Create_TheModelDecidingTheVerdict_NamesTheModelForBoth()
    {
        // Arrange, Act
        var classification = Classification(
            decidedBy: SpamClassificationStage.Model,
            signals: [ModelJudgement()],
            subcategory: SpamSubcategory.BusinessEmailCompromise,
            impersonatedParty: SpamImpersonatedParty.Executive);

        // Assert
        Assert.Equal(SpamClassificationStage.Model, classification.DecidedBy);
        Assert.Equal(SpamClassificationStage.Model, classification.SubcategoryNamedBy);
    }

    [Fact]
    public void Create_AVerdictTheModelDecidedWithNoJudgementBehindIt_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            verdict: SpamVerdict.NotSpam,
            decidedBy: SpamClassificationStage.Model));

        Assert.Equal("decidedBy", failure.ParamName);
    }

    /// <summary>A judgement past the signal bound is not kept, so it cannot vouch for the subcategory either.</summary>
    [Fact]
    public void Create_AModelJudgementPastTheSignalBound_DoesNotVouchForTheSubcategory()
    {
        // Arrange
        SpamSignal[] written =
        [
            .. Enumerable.Range(0, SpamClassification.MaximumSignals).Select(index => Signal($"field-{index}")),
            ModelJudgement(),
        ];

        // Act, Assert
        Assert.Throws<ArgumentException>(() => Classification(signals: written, subcategory: SpamSubcategory.Scam));
    }

    [Theory]
    [InlineData(SpamVerdict.Spam, null)]
    [InlineData(SpamVerdict.Spam, SpamSubcategory.Extortion)]
    [InlineData(SpamVerdict.NotSpam, null)]
    public void Create_ThePersonsOwnVerdict_NamesThePersonForBoth(SpamVerdict verdict, SpamSubcategory? subcategory)
    {
        // Arrange, Act
        var classification = Classification(verdict, SpamClassificationStage.Person, subcategory: subcategory);

        // Assert
        Assert.Equal(SpamClassificationStage.Person, classification.DecidedBy);
        Assert.Equal(subcategory, classification.Subcategory);
        Assert.Equal(subcategory is null ? null : SpamClassificationStage.Person, classification.SubcategoryNamedBy);
    }

    [Fact]
    public void Create_ThePersonConcludingNothing_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            SpamVerdict.Undetermined,
            SpamClassificationStage.Person));

        Assert.Equal("verdict", failure.ParamName);
    }

    /// <summary>Only the model names phishing detail, so the person's subcategory carries none.</summary>
    [Fact]
    public void Create_PhishingDetailOnThePersonsSubcategory_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            decidedBy: SpamClassificationStage.Person,
            subcategory: SpamSubcategory.Phishing,
            phishingRequest: SpamPhishingRequest.OneTimeCode));

        Assert.Equal("phishingRequest", failure.ParamName);
    }

    [Fact]
    public void Create_PhishingDetailWithoutASubcategory_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            signals: [ModelJudgement()],
            impersonatedParty: SpamImpersonatedParty.Courier));

        Assert.Equal("impersonatedParty", failure.ParamName);
    }

    [Theory]
    [InlineData(SpamSubcategory.BusinessEmailCompromise)]
    [InlineData(SpamSubcategory.Scam)]
    [InlineData(SpamSubcategory.Marketing)]
    public void Create_ARequestOnAnythingButPhishing_IsRefused(SpamSubcategory subcategory)
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            signals: [ModelJudgement()],
            subcategory: subcategory,
            phishingRequest: SpamPhishingRequest.PaymentDetails));

        Assert.Equal("phishingRequest", failure.ParamName);
    }

    [Theory]
    [InlineData(SpamSubcategory.MalwareLure)]
    [InlineData(SpamSubcategory.Scam)]
    [InlineData(SpamSubcategory.Extortion)]
    [InlineData(SpamSubcategory.Marketing)]
    public void Create_AnImpersonatedPartyOnASubcategoryThatImpersonatesNobody_IsRefused(SpamSubcategory subcategory)
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => Classification(
            signals: [ModelJudgement()],
            subcategory: subcategory,
            impersonatedParty: SpamImpersonatedParty.Supplier));

        Assert.Equal("impersonatedParty", failure.ParamName);
    }

    [Fact]
    public void Create_ANamedValueOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => Classification(
            signals: [ModelJudgement()],
            subcategory: (SpamSubcategory)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => Classification(
            signals: [ModelJudgement()],
            subcategory: SpamSubcategory.Phishing,
            phishingRequest: (SpamPhishingRequest)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => Classification(
            signals: [ModelJudgement()],
            subcategory: SpamSubcategory.Phishing,
            impersonatedParty: (SpamImpersonatedParty)42));
    }

    /// <summary>Only the person replaces the person's own verdict; every other recorded verdict is replaced by any stage.</summary>
    [Theory]
    [InlineData(SpamClassificationStage.Deterministic, SpamClassificationStage.Deterministic, true)]
    [InlineData(SpamClassificationStage.Deterministic, SpamClassificationStage.Model, true)]
    [InlineData(SpamClassificationStage.Deterministic, SpamClassificationStage.Person, false)]
    [InlineData(SpamClassificationStage.Person, SpamClassificationStage.Deterministic, true)]
    [InlineData(SpamClassificationStage.Person, SpamClassificationStage.Model, true)]
    [InlineData(SpamClassificationStage.Person, SpamClassificationStage.Person, true)]
    public void MayReplace_AVerdictRecordedByAStage_AnswersWhetherThisStageOutranksIt(
        SpamClassificationStage replacingStage,
        SpamClassificationStage recordedBy,
        bool expected)
    {
        // Arrange
        var classification = Classification(verdict: SpamVerdict.NotSpam, decidedBy: replacingStage);

        // Act
        var mayReplace = classification.MayReplace(recordedBy);

        // Assert
        Assert.Equal(expected, mayReplace);
    }

    private static SpamClassification Classification(
        SpamVerdict verdict = SpamVerdict.Spam,
        SpamClassificationStage decidedBy = SpamClassificationStage.Deterministic,
        SpamClassificationProfile? profile = null,
        IReadOnlyList<SpamSignal>? signals = null,
        DateTimeOffset? evaluatedAt = null,
        SpamSubcategory? subcategory = null,
        SpamPhishingRequest? phishingRequest = null,
        SpamImpersonatedParty? impersonatedParty = null) => SpamClassification.Create(
        Occurrence,
        verdict,
        decidedBy,
        assessment: null,
        profile ?? Profile,
        signals ?? [],
        evaluatedAt ?? EvaluatedAt,
        subcategory,
        phishingRequest,
        impersonatedParty);

    private static SpamSignal ModelJudgement() => SpamSignal.Create(
        SpamSignalKind.ModelJudgement,
        "spam_kind",
        observation: "phishing",
        SpamSignalProvenance.FromModel("example/decider-2026-09-01", "3f9a1c"));

    private static SpamSignal Signal(string name) => SpamSignal.Create(
        SpamSignalKind.ProviderSpamVerdict,
        name,
        observation: null,
        SpamSignalProvenance.FromMessageHeader(name));
}
