// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.Decisions;
using MailFathom.AI.SpamClassification;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.AI.UnitTests.SpamClassification;

/// <summary>Covers what a decision model's answer is read as, and which answers are refused as unreadable.</summary>
public sealed class SpamClassificationReadingTests
{
    private const string RequestedModel = "vendor/requested-decider";

    private const string AnsweringBuild = "vendor/decider-2026-10-01";

    [Fact]
    public void Read_AVerdictDistribution_ReadsTheSpamProbabilityAsOneMinusNotSpam()
    {
        // Arrange
        var response = Responding(Verdict(("not_spam", 0.25), ("phishing", 0.5), ("scam", 0.25)));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(0.75, answer?.SpamProbability);
    }

    /// <summary>A message the model thinks is narrowly ordinary mail still names the kind a lower threshold would record.</summary>
    [Fact]
    public void Read_NotSpamWinningOverall_StillNamesTheMostProbableOfTheSixKinds()
    {
        // Arrange
        var response = Responding(Verdict(("not_spam", 0.75), ("marketing", 0.125), ("scam", 0.0625), ("phishing", 0.0625)));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(SpamSubcategory.Marketing, answer?.Subcategory);
    }

    [Fact]
    public void Read_TwoKindsEquallyProbable_NamesTheOneOfferedFirst()
    {
        // Arrange
        var response = Responding(Verdict(("scam", 0.5), ("phishing", 0.5)));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(SpamSubcategory.Phishing, answer?.Subcategory);
    }

    /// <summary>The kind is the one thing an answer must carry, so an answer without it is refused rather than read as ordinary mail.</summary>
    [Theory]
    [InlineData(SpamModelJob.DecideVerdict)]
    [InlineData(SpamModelJob.NameSubcategory)]
    public void Read_AnAnswerWithoutTheKindQuestion_IsUnreadable(SpamModelJob job)
    {
        // Arrange
        var response = Responding(Detail(SpamClassificationQuestions.PhishingRequestQuestionId, "credentials"));

        // Act
        var answer = SpamClassificationReading.Read(response, job, RequestedModel);

        // Assert
        Assert.Null(answer);
    }

    [Fact]
    public void Read_TheKindQuestionLeftUnanswered_IsUnreadable()
    {
        // Arrange
        var response = Responding(DecisionAnswer.Unanswered(SpamClassificationQuestions.VerdictQuestionId));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Null(answer);
    }

    /// <summary>Without the probability of ordinary mail there is no spam probability, and a verdict cannot be read without one.</summary>
    [Fact]
    public void Read_AVerdictDistributionWithoutNotSpam_IsUnreadable()
    {
        // Arrange
        var response = Responding(new DecisionAnswer(
            SpamClassificationQuestions.VerdictQuestionId,
            "phishing",
            new Dictionary<string, double> { ["phishing"] = 1 }));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Null(answer);
    }

    /// <summary>The verdict is the deterministic stage's, so the job naming its kind reports no probability of its own.</summary>
    [Fact]
    public void Read_TheNamingJob_NamesTheKindAndCarriesNoProbability()
    {
        // Arrange
        var response = Responding(Kind(("extortion", 0.75), ("scam", 0.25)));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.NameSubcategory, RequestedModel);

        // Assert
        Assert.NotNull(answer);
        Assert.Equal(SpamSubcategory.Extortion, answer.Subcategory);
        Assert.Null(answer.SpamProbability);
    }

    [Fact]
    public void Read_BothDetailsAnswered_ReadsWhatIsAskedForAndWhoIsImpersonated()
    {
        // Arrange
        var response = Responding(
            Verdict(("phishing", 1)),
            Detail(SpamClassificationQuestions.PhishingRequestQuestionId, "one_time_code"),
            Detail(SpamClassificationQuestions.ImpersonatedPartyQuestionId, "courier"));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(SpamPhishingRequest.OneTimeCode, answer?.PhishingRequest);
        Assert.Equal(SpamImpersonatedParty.Courier, answer?.ImpersonatedParty);
    }

    [Fact]
    public void Read_BothDetailsLeftUnanswered_NamesNeither()
    {
        // Arrange
        var response = Responding(
            Verdict(("phishing", 1)),
            DecisionAnswer.Unanswered(SpamClassificationQuestions.PhishingRequestQuestionId),
            DecisionAnswer.Unanswered(SpamClassificationQuestions.ImpersonatedPartyQuestionId));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.NotNull(answer);
        Assert.Null(answer.PhishingRequest);
        Assert.Null(answer.ImpersonatedParty);
    }

    [Fact]
    public void Read_ABuildTheResponseNamed_RecordsThatBuild()
    {
        // Arrange
        var response = Responding(Verdict(("not_spam", 1)));
        response.ModelId = AnsweringBuild;

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(AnsweringBuild, answer?.ModelBuild);
    }

    /// <summary>The build is the provider's text, so one a signal's provenance could not carry is replaced by the model the call asked for.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("vendor/decider@2026-10-01")]
    [InlineData("vendor/decider\n2026-10-01")]
    [InlineData("vendor/decider\u00072026-10-01")]
    public void Read_ABuildAProvenanceCannotCarry_RecordsTheRequestedModelInstead(string? build)
    {
        // Arrange
        var response = Responding(Verdict(("not_spam", 1)));
        response.ModelId = build;

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(RequestedModel, answer?.ModelBuild);
    }

    [Fact]
    public void Read_ABuildLongerThanAProvenanceHolds_RecordsTheRequestedModelInstead()
    {
        // Arrange
        var response = Responding(Verdict(("not_spam", 1)));
        response.ModelId = "vendor/" + new string('d', 101);

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(RequestedModel, answer?.ModelBuild);
    }

    [Fact]
    public void Read_NeitherBuildUsable_RecordsTheCallAsUnnamed()
    {
        // Arrange
        var response = Responding(Verdict(("not_spam", 1)));
        response.ModelId = "vendor@decider";

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, requestedModel: string.Empty);

        // Assert
        Assert.Equal("unnamed", answer?.ModelBuild);
    }

    [Theory]
    [InlineData(SpamModelJob.DecideVerdict)]
    [InlineData(SpamModelJob.NameSubcategory)]
    public void Read_AnyReadableAnswer_CarriesTheRevisionOfTheQuestionsItsJobAsks(SpamModelJob job)
    {
        // Arrange
        var response = Responding(job is SpamModelJob.DecideVerdict ? Verdict(("scam", 1)) : Kind(("scam", 1)));

        // Act
        var answer = SpamClassificationReading.Read(response, job, RequestedModel);

        // Assert
        Assert.Equal(SpamClassificationQuestions.RevisionFor(job), answer?.InstructionRevision);
    }

    /// <summary>Each choice becomes a model judgement signal, so every answered question and only those is carried, in the order asked.</summary>
    [Fact]
    public void Read_SomeQuestionsAnswered_CarriesTheChoiceOfEachAnsweredQuestion()
    {
        // Arrange
        var response = Responding(
            Verdict(("business_email_compromise", 1)),
            DecisionAnswer.Unanswered(SpamClassificationQuestions.PhishingRequestQuestionId),
            Detail(SpamClassificationQuestions.ImpersonatedPartyQuestionId, "executive"));

        // Act
        var answer = SpamClassificationReading.Read(response, SpamModelJob.DecideVerdict, RequestedModel);

        // Assert
        Assert.Equal(
            [
                new SpamModelChoice(SpamClassificationQuestions.VerdictQuestionId, "business_email_compromise"),
                new SpamModelChoice(SpamClassificationQuestions.ImpersonatedPartyQuestionId, "executive"),
            ],
            answer?.Choices ?? []);
    }

    private static DecisionResponse Responding(params DecisionAnswer[] answers) => new(answers);

    /// <summary>Answers the verdict question over every option it offers, each at zero unless stated.</summary>
    private static DecisionAnswer Verdict(params (string Option, double Probability)[] stated) =>
        Distribution(SpamClassificationQuestions.VerdictQuestionId, SpamModelJob.DecideVerdict, stated);

    /// <summary>Answers the kind question over every option it offers, each at zero unless stated.</summary>
    private static DecisionAnswer Kind(params (string Option, double Probability)[] stated) =>
        Distribution(SpamClassificationQuestions.KindQuestionId, SpamModelJob.NameSubcategory, stated);

    private static DecisionAnswer Distribution(
        string questionId,
        SpamModelJob job,
        (string Option, double Probability)[] stated)
    {
        var probabilities = SpamClassificationQuestions.For(job)[0].Options.ToDictionary(
            option => option.Value,
            option => stated.FirstOrDefault(entry => string.Equals(entry.Option, option.Value, StringComparison.Ordinal)).Probability);

        return new DecisionAnswer(questionId, stated.MaxBy(entry => entry.Probability).Option, probabilities);
    }

    private static DecisionAnswer Detail(string questionId, string option) =>
        new(questionId, option, new Dictionary<string, double> { [option] = 1 });
}
