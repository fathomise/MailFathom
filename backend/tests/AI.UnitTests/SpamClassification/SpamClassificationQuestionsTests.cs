// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.AI.SpamClassification;
using MailFathom.Application.Spam.Model;
using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.AI.UnitTests.SpamClassification;

/// <summary>Covers what each of the model stage's two jobs offers a model, and what each offered option reads back as.</summary>
public sealed class SpamClassificationQuestionsTests
{
    private static readonly string[] KindOptions =
        ["phishing", "business_email_compromise", "malware_lure", "scam", "extortion", "marketing"];

    /// <summary>One distribution over ordinary mail and the six kinds decides the verdict and the kind together.</summary>
    [Fact]
    public void For_DecideVerdict_OffersNotSpamBesideTheSixKinds()
    {
        // Act
        var verdict = SpamClassificationQuestions.For(SpamModelJob.DecideVerdict)[0];

        // Assert
        Assert.Equal(SpamClassificationQuestions.VerdictQuestionId, verdict.Id);
        Assert.Equal([SpamClassificationQuestions.NotSpamOption, .. KindOptions], verdict.Options.Select(option => option.Value));
    }

    /// <summary>A verdict the deterministic stage already reached is not the model's to contradict, so ordinary mail is not on offer.</summary>
    [Fact]
    public void For_NameSubcategory_OffersOnlyTheSixKinds()
    {
        // Act
        var kind = SpamClassificationQuestions.For(SpamModelJob.NameSubcategory)[0];

        // Assert
        Assert.Equal(SpamClassificationQuestions.KindQuestionId, kind.Id);
        Assert.Equal(KindOptions, kind.Options.Select(option => option.Value));
    }

    [Theory]
    [InlineData(SpamModelJob.DecideVerdict)]
    [InlineData(SpamModelJob.NameSubcategory)]
    public void For_EitherJob_AsksBothDetailsAfterTheKind(SpamModelJob job)
    {
        // Act
        var questions = SpamClassificationQuestions.For(job);

        // Assert
        Assert.Equal(
            [SpamClassificationQuestions.PhishingRequestQuestionId, SpamClassificationQuestions.ImpersonatedPartyQuestionId],
            questions.Skip(1).Select(question => question.Id));
    }

    [Fact]
    public void For_AnUndefinedJob_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SpamClassificationQuestions.For((SpamModelJob)99));
    }

    [Fact]
    public void KindOf_EveryOfferedKind_ReadsBackAsItsOwnSubcategory()
    {
        // Act
        var kinds = SpamClassificationQuestions.KindOptions.Select(SpamClassificationQuestions.KindOf);

        // Assert
        Assert.Equal(Enum.GetValues<SpamSubcategory>().Cast<SpamSubcategory?>(), kinds);
    }

    /// <summary>Ordinary mail is a verdict rather than a kind, so it reads as no kind at all.</summary>
    [Fact]
    public void KindOf_TheNotSpamOption_ReadsAsNoKind()
    {
        // Act & Assert
        Assert.Null(SpamClassificationQuestions.KindOf(SpamClassificationQuestions.NotSpamOption));
    }

    [Fact]
    public void RequestOf_EveryOfferedRequest_ReadsBackAsItsOwnRequest()
    {
        // Arrange
        var offered = OptionsOf(SpamModelJob.DecideVerdict, SpamClassificationQuestions.PhishingRequestQuestionId);

        // Act
        var requests = offered.Select(SpamClassificationQuestions.RequestOf);

        // Assert
        Assert.Equal(Enum.GetValues<SpamPhishingRequest>().Cast<SpamPhishingRequest?>(), requests);
    }

    [Fact]
    public void PartyOf_EveryOfferedParty_ReadsBackAsItsOwnParty()
    {
        // Arrange
        var offered = OptionsOf(SpamModelJob.DecideVerdict, SpamClassificationQuestions.ImpersonatedPartyQuestionId);

        // Act
        var parties = offered.Select(SpamClassificationQuestions.PartyOf);

        // Assert
        Assert.Equal(Enum.GetValues<SpamImpersonatedParty>().Cast<SpamImpersonatedParty?>(), parties);
    }

    [Fact]
    public void RequestOf_AnOptionNeverOffered_ReadsAsNothing()
    {
        // Act & Assert
        Assert.Null(SpamClassificationQuestions.RequestOf("a_password_please"));
    }

    [Fact]
    public void PartyOf_AnOptionNeverOffered_ReadsAsNothing()
    {
        // Act & Assert
        Assert.Null(SpamClassificationQuestions.PartyOf("the_tax_office"));
    }

    /// <summary>A threshold is calibrated against the revision, so asking the same questions again must name the same one.</summary>
    [Theory]
    [InlineData(SpamModelJob.DecideVerdict)]
    [InlineData(SpamModelJob.NameSubcategory)]
    public void RevisionFor_OneJob_IsTwelveLowercaseHexadecimalCharactersEveryTime(SpamModelJob job)
    {
        // Act
        var revision = SpamClassificationQuestions.RevisionFor(job);

        // Assert
        Assert.Matches("^[0-9a-f]{12}$", revision);
        Assert.Equal(revision, SpamClassificationQuestions.RevisionFor(job));
    }

    /// <summary>The two jobs ask different questions, so a threshold calibrated for one is visibly not calibrated for the other.</summary>
    [Fact]
    public void RevisionFor_TheTwoJobs_NamesADifferentRevisionForEach()
    {
        // Act & Assert
        Assert.NotEqual(
            SpamClassificationQuestions.RevisionFor(SpamModelJob.DecideVerdict),
            SpamClassificationQuestions.RevisionFor(SpamModelJob.NameSubcategory));
    }

    private static IEnumerable<string> OptionsOf(SpamModelJob job, string questionId) =>
        SpamClassificationQuestions.For(job).Single(question => string.Equals(question.Id, questionId, StringComparison.Ordinal)).Options.Select(option => option.Value);
}
